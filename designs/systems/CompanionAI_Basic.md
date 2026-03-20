# System: CompanionAI_Basic
Section: 2 — AI・仲間・連携

## 責務
常駐仲間AIの基本行動。プレイヤー追従、スタンス切替（4種）、連携ボタンによる支援魔法発動。AICore基盤の3層判定の上で動作する。
仲間MPシステム（バリア・消滅/復帰・二重MPプール・MP回復行動）を管理する。

## 依存
- 入力: AICore（AIBrain基盤・3層判定）、PlayerMovement（追従対象）、InputSystem（連携ボタン）、DamageSystem（バリア計算）
- 出力: 仲間の行動実行、支援魔法発動、消滅/復帰イベント

## アーキテクチャ準拠
- AICore の3層判定ループ上で動作
- スタンスはAIModeのactionRules重みを動的調整する形で実現
- 将来のCompanionAIBrain拡張（CommandQueue）の土台を用意

## コンポーネント構成
| コンポーネント | 責務 | MonoBehaviour? |
|--------------|------|---------------|
| CompanionController | 仲間AI固有のロジック統括（消滅/復帰を含む） | No |
| FollowBehavior | プレイヤー追従・リーシュ距離管理 | No |
| StanceManager | 4スタンス切替・AIModeパラメータ変更 | No |
| CompanionMpManager | 二重MPプール管理・バリア・消滅/復帰・MP回復行動 | No |

## 仲間MPシステム

### 概要

仲間はHPを持たない。被ダメージはバリア（盾判定 = ダメージ計算式準拠）でMP消費して防ぐ。
MP 0で消滅（ワープ退場）し、MP 50%回復で復帰する。

```
[reserveMP] ──自然補充──→ [currentMP (max: maxMP)] ──消費──→ 魔法/連携/バリア
     ↑                          ↑
 アイテム/CP のみ          自然回復（常時じわじわ）
                          MP回復行動（停止、怯みで中断）
                          消滅中（×1.3倍、設定可変）
```

### CompanionMpSettings（ScriptableObjectまたはstruct）

```csharp
[Serializable]
public struct CompanionMpSettings
{
    public float baseRecoveryRate;        // MP自然回復速度（/秒）
    public float mpRecoverActionRate;     // MP回復行動時の回復速度（/秒）
    public float vanishRecoveryMultiplier; // 消滅中の回復倍率（デフォルト1.3）
    public float returnThresholdRatio;    // 復帰閾値（デフォルト0.5 = maxMPの50%）
    public int maxReserveMp;              // reserveMP最大値
}
```

### CompanionMpManager（Pure Logic）

```csharp
public class CompanionMpManager
{
    // 状態
    public float CurrentMp { get; }
    public float MaxMp { get; }
    public int ReserveMp { get; }
    public int MaxReserveMp { get; }
    public bool IsVanished { get; }
    public bool IsRecovering { get; }  // MP回復行動中か

    // MP消費（魔法/連携/バリア共通）
    // 戻り値: 実際の消費量。MP不足なら残MP全消費
    public float ConsumeMp(float amount);

    // バリアダメージ処理（盾判定後のMP消費量を受け取る）
    // MP 0になったら消滅トリガー
    public void ApplyBarrierDamage(float mpCost);

    // 自然回復Tick（毎フレーム呼び出し）
    // reserveMP → currentMP 補充 + 消滅中の倍率適用
    public void Tick(float deltaTime);

    // MP回復行動の開始/中断
    public void StartMpRecovery();
    public void StopMpRecovery();

    // 消滅→復帰判定（Tick内で自動チェック）
    // 復帰時にイベント発火
    public event Action OnVanish;
    public event Action OnReturn;

    // reserveMP回復（アイテム/チェックポイント）
    public void RestoreReserveMp(int amount);
}
```

### 消滅/復帰フロー

```
通常状態
    ↓ バリアダメージでMP 0到達
消滅状態（OnVanish発火）
    - 連携ボタン使用不可
    - AI判定停止（Tickは回復のみ）
    - 回復倍率 × vanishRecoveryMultiplier
    ↓ currentMP >= maxMP × returnThresholdRatio
復帰状態（OnReturn発火）
    - プレイヤー近くにワープ復帰
    - AI判定再開
```

### バリア（被ダメージ処理）

仲間のIDamageable実装で、受けたダメージを盾判定（ガード計算式）に通し、
軽減後の値をMP消費に変換する。

```
受けたダメージ → 盾のGuardStats適用 → 軽減後ダメージ → MP消費
```

- ジャストガードも適用可能（AIルールで条件設定）
- ガードブレイク相当 = MP大量消費（一撃消滅もありうる）

## 追従ロジック
```
distance = |companion.position - player.position|

if distance < followDistance(2.0):
    → 待機、3層判定で戦闘行動を選択
elif distance < maxLeashDistance(15.0):
    → プレイヤー方向へ移動
else:
    → テレポート（プレイヤー近くにワープ）
```

追従はAIModeのデフォルト行動（defaultActionIndex）として設定。
戦闘中でもactionRulesが全不マッチなら追従に戻る（棒立ち防止）。

### 追従パラメータ（CompanionBehaviorSetting: ScriptableObject）
```csharp
followDistance = 2.0f       // この距離以内なら追従停止
maxLeashDistance = 15.0f    // これ以上離れたらテレポート
supportHpThreshold = 0.5f  // プレイヤーHP50%以下で回復優先
```

## スタンス切替

| スタンス | 効果 | actionRules重み調整 |
|---------|------|-------------------|
| Aggressive | 積極攻撃、敵に接近 | 攻撃系×2.0、回復系×0.5 |
| Defensive | プレイヤー防衛、ガード多用 | 防御系×2.0、接近距離を縮小 |
| Supportive | 回復・バフ優先、攻撃控え | 回復系×3.0、攻撃系×0.3 |
| Passive | 戦闘しない、追従のみ | 全戦闘actionのprobability→0 |

スタンスごとにAIModeを丸ごと切り替えるのではなく、同じAIModeのactionRules内のprobabilityを動的に調整する。これによりスタンス設定はシンプルな倍率テーブルで表現できる。

## 連携ボタンシステム

連携ボタンの詳細は **CoopAction.md** を参照。CompanionAI_Basicは連携の「受け側」として以下を担当:

- 連携発動時の行動割り込み（InterruptForCoop）
- 連携終了後の行動再開（ResumeFromCoop）
- 怯み中の連携拒否判定
- **消滅中は連携拒否**

## MP回復行動

SustainedAction.MpRecover として実装。

```
条件: MP比率が閾値以下（AIルールで設定可能）
開始: 仲間が停止し、MP加速回復を開始
中断: 怯み（被ダメージ）で中断 → 通常回復に戻る
再開: 再びAIルールでMP回復条件を満たせば再開
終了: MP比率が上限に達したら自動終了
```

AIルール例:
```
conditions: [SelfMpRatio < 30, Count(敵InRange3) == 0]
→ actions = ActionSlot(Sustained, paramId=MpRecover)
```

## 仲間AIの3層判定例（MPシステム統合版）
```
AIMode "通常":
  targetRules:
    [0] conditions: [HpRatio < 30, filter=味方]
        → targetSelects[0] = HpRatio昇順(味方) → HP最低の味方を回復対象に
    [1] conditions: [Count(敵) >= 1]
        → targetSelects[1] = Distance昇順(敵) → 最寄りの敵を攻撃対象に

  actionRules:
    [0] conditions: [SelfMpRatio < 20, Count(敵InRange3) == 0]
        → actions[0] = ActionSlot(Sustained, paramId=MpRecover)
    [1] conditions: [ターゲット=味方, Distance < 5, SelfMpRatio > 20]
        → actions[1] = ActionSlot(Cast, paramId=ヒールID)
    [2] conditions: [Distance InRange(0, 3)]
        → actions[2] = ActionSlot(Attack, paramId=0)
    [3] conditions: [Distance InRange(3, 8)]
        → actions[3] = ActionSlot(Sustained, paramId=MoveToTarget)

  defaultActionIndex: 4 → actions[4] = ActionSlot(Sustained, paramId=Follow, paramValue=0)
```

## インタフェース
- `InputSystem.cooperationPressed` → CoopActionManager.TryActivate()（CoopAction.md参照）
- `CoopActionManager` → CompanionController.InterruptForCoop() / ResumeFromCoop()
- `GameManager.Events.OnCompanionStanceChanged` → StanceManager が重み調整
- `GameManager.Events.OnCompanionVanish` → UI通知（消滅表示）
- `GameManager.Events.OnCompanionReturn` → UI通知（復帰表示）
- CompanionBehaviorSetting（ScriptableObject）: 追従距離、スタンス倍率テーブル
- CompanionMpSettings: MP回復速度、消滅倍率等

## 機能分解
| 機能名 | 説明 | テスト種別 | 優先度 |
|--------|------|-----------|--------|
| Companion_FollowBehavior | 距離ベースの追従・テレポート判定 | EditMode | High |
| Companion_StanceManager | 4スタンス切替、actionRules重み調整 | EditMode | High |
| Companion_CoopInterruption | 連携発動時の行動割り込み・再開 | EditMode | High |
| Companion_MpManager | 二重MPプール管理・自然回復・消費 | EditMode | High |
| Companion_BarrierDamage | バリア（盾判定）→ MP消費変換 | EditMode | High |
| Companion_VanishReturn | 消滅/復帰フロー・イベント発火 | EditMode | High |
| Companion_MpRecoveryAction | MP回復行動（Sustained）・怯み中断 | EditMode | Medium |
| Companion_HpMonitoring | プレイヤーHP監視 → 回復優先ターゲット切替 | EditMode | Medium |

## 設計メモ
- AICore基盤の上で動作。CompanionControllerはAIBrainのロジックを統合
- スタンスはAIModeのprobability倍率調整で実現（モード丸ごと切替ではない）
- 連携ボタンの発動・コンボ・クールタイムはCoopActionシステムが管理
- CompanionAI_Basicは連携の「受け手」（割り込み/再開）のみ担当
- 仲間はHPを使用しない。HPフィールドは存在するが参照しない
- バリアは盾のダメージ計算式準拠。仲間専用のIDamageable実装で変換
- reserveMPの回復はアイテムとチェックポイントに限定（探索のリスクリワード）
- 将来のCompanionAIBrain（CommandQueue対応）は、Evaluate()前に指示キューをチェックする形で拡張
