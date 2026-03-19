# System: ConfusionMagic
Section: 3 — 世界の広がり

## 責務
敵を混乱させて一時的に味方化する魔術システム。蓄積型状態異常として実装し、閾値到達で敵の陣営を反転させる。混乱敵はパーティ枠外で独立して行動する。

## 依存
- 入力: DamageSystem（StatusEffectManager, 蓄積モデル）、AICore（AIBrain, CharacterFlags）、MagicSystem（MagicCaster, Projectile）
- 出力: OnEnemyConfused, OnEnemyConfusionEnd イベント → AICore（ターゲット反転）、UISystem（混乱アイコン）

## コンポーネント構成

| コンポーネント | 責務 | MonoBehaviour? |
|--------------|------|---------------|
| ConfusionEffectProcessor | 混乱状態異常の蓄積・発症・解除ロジック | No |
| ConfusionAIOverride | 混乱中の敵AIのターゲット選択を反転（元味方→敵扱い） | No |
| ConfusionVisualFeedback | 混乱中のビジュアルフィードバック（頭上アイコン等） | Yes |

## インタフェース

### DamageSystem.StatusEffectManager → ConfusionEffectProcessor
- 既存の蓄積モデルに `Confusion` 状態異常を追加
- 蓄積値が閾値超過 → ConfusionEffectProcessor.ApplyConfusion()

### ConfusionEffectProcessor → CharacterFlags
```csharp
// 混乱発症時: 陣営フラグを反転
CharacterFlags.SetFaction(hash, Faction.Ally);  // 敵→味方
CharacterFlags.SetState(hash, CharacterState.Confused, true);

// 混乱解除時: 陣営フラグを復元
CharacterFlags.SetFaction(hash, originalFaction);
CharacterFlags.SetState(hash, CharacterState.Confused, false);
```

### ConfusionAIOverride → AIBrain
- 混乱中、AIBrainのTargetSelectorが参照するTargetFilterの陣営条件を反転
- 元の味方（プレイヤー側）をターゲット候補から除外、元の敵側をターゲットに
- AIBrainのモード/ルールはそのまま（敵が自分の攻撃パターンで元仲間を攻撃する）

### ConfusionEffectProcessor → GameManager.Events
```csharp
GameManager.Events.OnEnemyConfused?.Invoke(targetHash, controllerHash);
GameManager.Events.OnEnemyConfusionEnd?.Invoke(targetHash);
```

## データフロー

```
プレイヤーが混乱属性の攻撃/魔法を敵にヒット
    → DamageSystem → StatusEffectManager.AccumulateEffect(Confusion, value)
    → 蓄積値 >= 閾値
    → ConfusionEffectProcessor.ApplyConfusion(targetHash, duration, controllerHash)
        → CharacterFlags.SetFaction(Ally)
        → CharacterFlags.SetState(Confused, true)
        → ConfusionAIOverride.ActivateOverride(targetHash)
        → GameManager.Events.OnEnemyConfused

混乱中:
    → 敵AIは通常通りAIBrain.Evaluate()を実行
    → TargetSelectorが反転済みフィルタで元仲間を攻撃対象に選択
    → 他の敵からは攻撃対象にならない（Faction=Allyのため）

混乱解除（時間切れ or 一定ダメージ受け）:
    → ConfusionEffectProcessor.ClearConfusion(targetHash)
        → CharacterFlags.SetFaction(originalFaction)
        → CharacterFlags.SetState(Confused, false)
        → ConfusionAIOverride.DeactivateOverride(targetHash)
        → GameManager.Events.OnEnemyConfusionEnd

混乱中の敵が死亡:
    → DamageSystem → OnCharacterDeath
    → ConfusionEffectProcessor.OnConfusedEnemyDeath(hash) → 状態クリア
```

## データ構造

### ConfusionEffectProcessor (Pure Logic)
```csharp
public class ConfusionEffectProcessor
{
    private Dictionary<int, ConfusionState> _confusedEntities;

    // 混乱適用
    public void ApplyConfusion(int targetHash, float duration, int controllerHash);

    // 混乱解除
    public void ClearConfusion(int targetHash);

    // 毎フレーム残時間更新
    public void Tick(float deltaTime);

    // 混乱中か判定
    public bool IsConfused(int hash);

    // 混乱中の敵数
    public int ConfusedCount { get; }

    // 最大同時混乱数チェック（PartyManager.k_MaxConfusedEnemies）
    public bool CanConfuseMore();
}
```

### ConfusionState (内部データ)
```csharp
public struct ConfusionState
{
    public int targetHash;
    public int controllerHash;        // 混乱をかけたキャラのハッシュ
    public float remainingDuration;
    public Faction originalFaction;    // 復帰用に保存
    public float accumulatedDamage;    // 混乱中に受けたダメージ（解除閾値用）
}
```

### ConfusionAIOverride (Pure Logic)
```csharp
public class ConfusionAIOverride
{
    // 指定キャラのAIターゲットフィルタを反転
    public void ActivateOverride(int targetHash);

    // フィルタを元に戻す
    public void DeactivateOverride(int targetHash);
}
```

## 混乱耐性

| パラメータ | 説明 | 場所 |
|-----------|------|------|
| confusionResistance | 蓄積軽減率（0.0〜1.0）。1.0で完全耐性 | CharacterInfo (ScriptableObject) |
| confusionThreshold | 蓄積閾値。大きいほど発症しにくい | CharacterInfo |
| confusionDurationBase | 基本持続秒数 | Confusion属性のStatusEffectInfo |
| confusionBreakDamage | 混乱中にこの量のダメージを受けると解除 | CharacterInfo |

- **ボスは混乱耐性1.0**（完全耐性）。ボスを味方化するのはゲームバランス破壊
- 強敵は高耐性（閾値が高い、持続時間が短い）
- confusionBreakDamage: 味方が混乱敵を誤爆すると混乱が解ける仕組み

## 機能分解

| 機能名 | 説明 | テスト種別 | 優先度 |
|--------|------|-----------|--------|
| ConfusionMagic_Accumulation | 蓄積値加算、耐性軽減、閾値判定 | EditMode | High |
| ConfusionMagic_FactionSwitch | 陣営反転（CharacterFlags操作）、混乱解除時の復帰 | EditMode | High |
| ConfusionMagic_AIOverride | TargetFilterの反転でAIターゲットを切り替え | EditMode | High |
| ConfusionMagic_Duration | 持続時間Tick、一定ダメージ受けでの早期解除 | EditMode | Medium |
| ConfusionMagic_Limits | 最大同時混乱数制限、ボス耐性 | EditMode | Medium |

## 設計メモ
- **既存DamageSystemの蓄積モデルに完全統合**。混乱は新しい状態異常の1つとして追加するだけ。StatusEffectManager.AccumulateEffect()で蓄積、閾値超過でConfusionEffectProcessorが発動
- AIの行動パターン自体は変更しない。TargetFilterの陣営条件だけを反転させることで、敵が自分の攻撃を元仲間に使う面白さを維持
- 混乱敵はパーティ枠外（GDDの仕様）。召喚枠とは完全に独立
- ConfusionAIOverrideはAIBrainのコードを変更せずに実現。ConditionEvaluatorがCharacterFlagsを参照する際、Faction=Allyなので味方判定される
- 混乱は「闇」属性に紐づく。Dark属性の攻撃にconfusion蓄積値を設定するのが自然
