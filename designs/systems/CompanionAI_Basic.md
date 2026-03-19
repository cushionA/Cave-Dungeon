# System: CompanionAI_Basic
Section: 2 — AI・仲間・連携

## 責務
常駐仲間AIの基本行動。プレイヤー追従、スタンス切替（4種）、連携ボタンによる支援魔法発動。AICore基盤の3層判定の上で動作する。

## 依存
- 入力: AICore（AIBrain基盤・3層判定）、PlayerMovement（追従対象）、InputSystem（連携ボタン）
- 出力: 仲間の行動実行、支援魔法発動

## アーキテクチャ準拠
- AICore の3層判定ループ上で動作
- スタンスはAIModeのactionRules重みを動的調整する形で実現
- 将来のCompanionAIBrain拡張（CommandQueue）の土台を用意

## コンポーネント構成
| コンポーネント | 責務 | MonoBehaviour? |
|--------------|------|---------------|
| CompanionController | 仲間AI固有のロジック統括（AIBrain継承） | Yes |
| FollowBehavior | プレイヤー追従・リーシュ距離管理 | No |
| StanceManager | 4スタンスの切替・AIModeパラメータ変更 | No |

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

```csharp
public class CompanionController : AIBrain
{
    private ActionSlot? _interruptedAction;
    private int _interruptedTargetHash;

    public void InterruptForCoop()
    {
        _interruptedAction = _currentAction;
        _interruptedTargetHash = _currentTargetHash;
    }

    public void ResumeFromCoop()
    {
        if (_interruptedAction.HasValue)
        {
            ActionExecutor.Execute(_interruptedAction.Value, ObjectHash, _interruptedTargetHash);
            _interruptedAction = null;
        }
    }
}
```

## 仲間AIの3層判定例（ActionSlot統合版）
```
AIMode "通常":
  targetRules:
    [0] conditions: [HpRatio < 30, filter=味方]
        → targetSelects[0] = HpRatio昇順(味方) → HP最低の味方を回復対象に
    [1] conditions: [Count(敵) >= 1]
        → targetSelects[1] = Distance昇順(敵) → 最寄りの敵を攻撃対象に

  actionRules:
    [0] conditions: [ターゲット=味方, Distance < 5, SelfMpRatio > 20]
        → actions[0] = ActionSlot(Cast, paramId=ヒールID)
    [1] conditions: [Distance InRange(0, 3)]
        → actions[1] = ActionSlot(Attack, paramId=0)
    [2] conditions: [Distance InRange(3, 8)]
        → actions[2] = ActionSlot(Sustained, paramId=MoveToTarget)

  defaultActionIndex: 3 → actions[3] = ActionSlot(Sustained, paramId=Follow, paramValue=0)
```

## インタフェース
- `InputSystem.cooperationPressed` → CoopActionManager.TryActivate()（CoopAction.md参照）
- `CoopActionManager` → CompanionController.InterruptForCoop() / ResumeFromCoop()
- `GameManager.Events.OnCompanionStanceChanged` → StanceManager が重み調整
- CompanionBehaviorSetting（ScriptableObject）: 追従距離、スタンス倍率テーブル

## 機能分解
| 機能名 | 説明 | テスト種別 | 優先度 |
|--------|------|-----------|--------|
| Companion_FollowBehavior | 距離ベースの追従・テレポート判定 | EditMode | High |
| Companion_StanceManager | 4スタンス切替、actionRules重み調整 | EditMode | High |
| Companion_CoopInterruption | 連携発動時の行動割り込み・再開 | EditMode | High |
| Companion_HpMonitoring | プレイヤーHP監視 → 回復優先ターゲット切替 | EditMode | Medium |

## 設計メモ
- AICore基盤の上で動作。CompanionControllerはAIBrainを継承
- スタンスはAIModeのprobability倍率調整で実現（モード丸ごと切替ではない）
- 連携ボタンの発動・コンボ・クールタイムはCoopActionシステムが管理
- CompanionAI_Basicは連携の「受け手」（割り込み/再開）のみ担当
- 将来のCompanionAIBrain（CommandQueue対応）は、Evaluate()前に指示キューをチェックする形で拡張
