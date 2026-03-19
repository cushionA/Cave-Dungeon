# Common Design: Section 3 — 世界の広がり

## 共通インターフェース

### IPhaseTransitionTarget
ボスや環境ギミックなどフェーズ遷移を持つオブジェクトが実装する。

```csharp
public interface IPhaseTransitionTarget
{
    int CurrentPhase { get; }
    int MaxPhase { get; }
    void TransitionToPhase(int phase);
    event System.Action<int, int> OnPhaseChanged; // (oldPhase, newPhase)
}
```

### ISummonable
召喚可能なキャラクターが実装する。

```csharp
public interface ISummonable
{
    int SummonerId { get; }
    float RemainingDuration { get; }
    void Initialize(int summonerHash, float duration, SummonDefinition definition);
    void Dismiss();
    event System.Action<ISummonable> OnDismissed;
}
```

### IConfusable
混乱状態を受けるキャラクターが実装する。

```csharp
public interface IConfusable
{
    bool IsConfused { get; }
    float ConfusionResistance { get; }
    void ApplyConfusion(float duration, int controllerHash);
    void ClearConfusion();
    event System.Action<IConfusable> OnConfusionStart;
    event System.Action<IConfusable> OnConfusionEnd;
}
```

## 共通Enum（Enums.cs に追加）

```csharp
// ボスフェーズ遷移条件の種類
public enum PhaseConditionType : byte
{
    HpThreshold,       // HP割合が閾値以下
    Timer,             // 経過時間
    ActionCount,       // 特定行動の累計回数
    AllAddsDefeated,   // 雑魚全滅
    Custom             // スクリプトで定義
}

// 召喚タイプ
public enum SummonType : byte
{
    Combat,      // 戦闘用召喚獣
    Utility,     // 探索補助（足場、照明等）
    Decoy        // 囮（敵ヘイトを集める）
}

// 属性ゲートの属性要件
public enum ElementalRequirement : byte
{
    Fire,        // 炎で点火/溶解
    Thunder,     // 雷で通電/起動
    Light,       // 聖で浄化/照射
    Dark,        // 闇で暗幕/隠し通路
    Slash,       // 斬撃で切断
    Strike,      // 打撃で破壊
    Pierce       // 刺突で穿孔
}

// バックトラック報酬種別
public enum BacktrackRewardType : byte
{
    Item,         // アイテム
    Currency,     // 通貨
    AbilityOrb,   // 新能力
    Shortcut,     // ショートカット開通
    Lore          // 世界観テキスト
}

// GateType に Elemental を追加（既存: Clear, Ability, Key）
// GateType.Elemental = 3

// ボスアリーナ状態
public enum ArenaState : byte
{
    Open,        // 通常通行可能
    Locked,      // 戦闘中ロック
    Cleared      // クリア済み（永続開放）
}
```

## 共通Struct（Structs.cs に追加）

```csharp
// ボスフェーズ遷移条件
[System.Serializable]
public struct PhaseCondition
{
    public PhaseConditionType type;
    public float threshold;    // HP割合 or 秒数 or 回数
}

// 召喚スロット情報
[System.Serializable]
public struct SummonSlot
{
    public int summonHash;        // 召喚獣のhashCode
    public float remainingTime;   // 残り時間
    public SummonType summonType;
}

// バックトラック報酬定義
[System.Serializable]
public struct BacktrackEntry
{
    public string rewardId;
    public BacktrackRewardType rewardType;
    public AbilityFlag requiredAbility;    // 必要な能力フラグ
    public string locationHint;            // マップ上のヒント文
    public bool collected;                 // 回収済みフラグ
}

// 属性ゲート定義（GateDefinition拡張用）
[System.Serializable]
public struct ElementalGateRequirement
{
    public ElementalRequirement element;
    public float minDamage;    // 必要最低ダメージ（0なら属性攻撃で触れるだけでOK）
}
```

## 共通ユーティリティ

### PartyManager（Section3Utilities.cs）
パーティ構成を管理する静的ヘルパー。最大パーティサイズの制約を管理。

```csharp
public static class PartyManager
{
    public const int k_MaxPartySize = 4;  // プレイヤー + 常駐仲間 + 一時仲間最大2
    public const int k_MaxSummonSlots = 2;

    // 現在のパーティ人数を返す（プレイヤー + 常駐仲間 + 召喚）
    public static int GetCurrentPartySize();

    // 召喚枠に空きがあるか
    public static bool CanSummon();

    // 混乱敵はパーティ枠外（制限なし、ただし同時最大は別途設定）
    public const int k_MaxConfusedEnemies = 3;
}
```

## GameManager.Events 追加イベント

```csharp
// ボス関連
public event System.Action<int, int, int> OnBossPhaseChanged;     // (bossHash, oldPhase, newPhase)
public event System.Action<int> OnBossDefeated;                    // (bossHash) → ClearGate開放
public event System.Action<int> OnBossEncounterStart;              // (bossHash) → アリーナロック

// 召喚関連
public event System.Action<int, int> OnSummonCreated;              // (summonerHash, summonHash)
public event System.Action<int, int> OnSummonDismissed;            // (summonerHash, summonHash)

// 混乱関連
public event System.Action<int, int> OnEnemyConfused;              // (targetHash, controllerHash)
public event System.Action<int> OnEnemyConfusionEnd;               // (targetHash)

// バックトラック関連
public event System.Action<string, AbilityFlag> OnBacktrackRewardAvailable;  // (rewardId, requiredAbility)
public event System.Action<string> OnBacktrackRewardCollected;               // (rewardId)
```

## asmdef構成（Section 3）

既存asmdefに機能を追加する形で対応。新規asmdefは不要。

| asmdef | 追加内容 | 備考 |
|--------|---------|------|
| Game.Core (既存拡張) | Section 3共通Enum/Struct、PartyManager | Enums.cs, Structs.cs, Section3Utilities.cs |
| Game.AI (既存拡張) | BossSystem（Boss/）、SummonSystem（Summon/）、ConfusionMagic（Confusion/） | AIBrain拡張 |
| Game.World (既存拡張) | ElementalGate（Gate/拡張）、BacktrackReward（Backtrack/） | GateSystem拡張 |
| Game.Tests.EditMode (既存拡張) | 全EditModeテスト | 参照変更なし |

### ディレクトリ追加

```
Assets/MyAsset/Core/AI/
    ├── Boss/              BossController, BossPhaseManager, BossArenaManager
    ├── Summon/            SummonManager, SummonedCharacterController
    └── Confusion/         ConfusionEffectProcessor, ConfusionAIOverride

Assets/MyAsset/Core/World/
    ├── Gate/              (既存) + ElementalGateInteractor
    └── Backtrack/         BacktrackRewardManager, BacktrackRewardChecker

Assets/MyAsset/Core/ScriptableObjects/
    ├── BossDefinition.cs
    ├── SummonDefinition.cs
    └── BacktrackRewardTable.cs
```
