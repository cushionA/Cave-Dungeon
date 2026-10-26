# System: WeaponSystem
Section: 1 — MVP

## 責務
武器の攻撃実行、コンボ管理、チャージ攻撃、スキル発動。AttackMotionDataに基づく攻撃フローの制御。

## 依存
- 入力: InputSystem（AttackInputType）、EquipmentSystem（WeaponData/ShieldData）、DataContainer
- 出力: DamageDealer（ヒットボックス生成）、アニメーション駆動

## 現在の実行定義契約（2026-10-26）

`ActionDefinition` / `AttackInfo` / `MotionDefinition` は編集用の入力で、`ActionCatalog.Register` が到達する遷移グラフ全体を検証・捕捉してから共通実行器へ登録する。失敗時は既存の登録を維持し、循環参照は登録IDへ解決する。同じSOの再登録と後から作る空中・落下variantは、初回の捕捉値を使う。

- `ActionDefinitionSnapshot` は費用・時間・移動・効果・遷移を保持する。公開照会と通知は同じ読取専用ビューを使い、入力配列・返却値から内部定義を書き換えられない。派生登録用の `ToData()` だけが独立した編集可能コピーを返す。
- `ActionDefinitionValidator` が有限・非負の数値、enum/flags、遷移のID・trigger・時間、時間合算を検証する。カタログは表示・攻撃・弾・空中物理と派生値のoverflowも登録前に検証する。0秒の技、Held/Manual、有限負値のキャンセル無効指定は維持する。
- カタログの技packet配列は非公開とし、`CreateAttack()` / `CreateMagic()` が実行ごとのコピーを返す。費用・占有・寿命は `ActionExecutionSystem`、攻撃値や装備効果の捕捉は既存の実行開始・発射契約へ接続する。
- `MotionPresentationData` はモーション・レイヤー・ブレンド・ループ・マスクを捕捉し、表示時に元SOの値を読み直さない。クリップ・マスク・音はUnityObject参照として保持し、内容の複製やAddressablesの解放管理は行わない。
- `AttackExecutionSettings` は入力受付窓と空中・落下物理を捕捉する。Playerは受理後に登録済みSOの識別子から取得するため、0秒で実行が完了していても設定を失わない。SOを実行中に編集しても登録済み動作へ反映せず、再読込み・ホットリロードは別途明示的な契約が必要。

以下の構成表・例にはMVP時の設計案を含む。現在の受付・費用・寿命は上記の共通実行契約を参照する。

## コンポーネント構成

| コンポーネント | 責務 | MonoBehaviour? |
|--------------|------|---------------|
| WeaponHolder | 武器スロット管理・攻撃入力ルーティング | Yes (IAbility) |
| ComboTracker | コンボ進行管理（チェーン追跡） | No |
| ChargeHandler | チャージ攻撃の溜め時間管理 | No |
| DamageDealer | ヒットボックス生成・当たり判定・DamageData生成 | Yes |
| AttackMover | 攻撃時移動制御（突進/停止/すり抜け/運搬） | Yes |
| ProjectileEmitter | 飛翔体発射（エフェクト弾） | Yes |

## インタフェース
```csharp
public class WeaponHolder : MonoBehaviour, IAbility
{
    AbilityType Type => AbilityType.Attack;

    // 攻撃要求
    void RequestAttack(AttackInputType inputType);

    // 状態
    bool IsAttacking { get; }
    int CurrentComboStep { get; }
    AttackMotionData CurrentMotion { get; }
}
```

## コンボシステム詳細

### isAutoChain の動作
```
[モーション0: motionValue=0.3, isAutoChain=true]   ← 入力なしで自動遷移
    ↓ (自動)
[モーション1: motionValue=0.2, isAutoChain=true]   ← 自動遷移続行
    ↓ (自動)
[モーション2: motionValue=1.5, isChainEndPoint=true] ← チェーン終端（大技）
```

### 通常コンボ（isAutoChain=false）
```
[弱1: inputWindow=0.4s] → (弱入力) → [弱2: inputWindow=0.4s] → (弱入力) → [弱3]
                         → (強入力) → [派生強1]
```

### 攻撃フロー
```
1. RequestAttack(inputType)
2. ComboTracker.GetNextMotion(inputType) → AttackMotionData or null
3. スタミナチェック → Consume(staminaCost)
4. アニメーション再生
5. AttackMover.StartMove(distance, duration, contactType)
6. DamageDealer.Activate(motionData) → ヒットボックス有効化
7. ヒット時 → DamageData生成 → IDamageable.ReceiveDamage()
8. hitCount < maxHitCount でヒット制限
9. isAutoChain → 自動で次モーションへ / else → inputWindow内の入力待ち
10. コンボ終了 → リカバリーモーション → 行動可能に
```

## 攻撃移動 (AttackMover)
```csharp
public class AttackMover
{
    void StartMove(float distance, float duration, AttackContactType contactType);
    // contactType:
    //   PassThrough → 敵をすり抜けて移動継続
    //   StopOnHit   → 敵に接触で停止
    //   Carry       → 敵を押しながら移動（運搬）
}
```

## スキル実行
```
1. GetActiveSkillSource() → Weapon or Shield
2. if Weapon: weaponData.skillMotions[index] + weaponData.skillMpCosts[index]
   if Shield: shieldData.shieldSkills[index] + shieldData.skillMpCosts[index]
3. MP消費チェック → 攻撃実行（通常攻撃と同じフロー）
4. スキル攻撃力参照:
   - Weapon skill → 武器の攻撃力
   - Shield skill → 盾の攻撃力
```

## 機能分解
| 機能名 | 説明 | テスト種別 | 優先度 |
|--------|------|-----------|--------|
| 弱攻撃チェーン | lightAttacks配列を順に実行 | PlayMode | High |
| 強攻撃チェーン | heavyAttacks配列を順に実行 | PlayMode | High |
| コンボ派生 | 弱→強の派生ルート | PlayMode | High |
| isAutoChain自動遷移 | 入力なしで次モーションへ自動遷移 | PlayMode | High |
| ヒットボックス・当たり判定 | コライダーベースのダメージ判定 | PlayMode | High |
| 攻撃時移動 | 3種の接触タイプによる移動制御 | PlayMode | High |
| チャージ攻撃 | 溜め時間によるモーション切替 | PlayMode | Medium |
| スキル発動 | MP消費技、参照先武器/盾の切替 | PlayMode | Medium |
| 空中攻撃 | aerialAttacks配列の実行 | PlayMode | Medium |
| 落下攻撃 | 空中から急降下攻撃 | PlayMode | Medium |
| 飛翔体発射 | ProjectileConfig設定に基づく弾発射 | PlayMode | Medium |
| ヒット数制限 | maxHitCountによるマルチヒット管理 | EditMode | Medium |

## 設計メモ
- 参考コードのWeaponAbillity.csを大幅リビルド。ActType enumの冗長な分岐を排除
- isCombo → isAutoChain にリネーム（意味を明確化）
- 攻撃モーションはすべてAttackMotionData配列で統一管理
- DamageDealerはヒットした対象のhashをDictionaryで追跡し、同一対象への重複ヒットを防止
