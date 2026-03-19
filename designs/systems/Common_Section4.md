# Common Design: Section 4 — エンドコンテンツ

## 共通Enum（Enums.cs に追加）

```csharp
// チャレンジモードの種別
public enum ChallengeType : byte
{
    BossRush,        // ボス連戦
    TimeAttack,      // 制限時間内クリア
    Survival,        // 耐久戦（Wave式）
    Restriction,     // 制限プレイ
    ScoreAttack,     // 高スコア狙い
}

// チャレンジランク
public enum ChallengeRank : byte
{
    None,    // 未クリア
    Bronze,  // クリア
    Silver,  // 条件達成
    Gold,    // 高スコア
    Platinum // パーフェクト
}

// チャレンジ進行状態
public enum ChallengeState : byte
{
    Ready,       // 開始前
    Running,     // 実行中
    Completed,   // クリア
    Failed,      // 失敗
}

// AIテンプレートカテゴリ
public enum AITemplateCategory : byte
{
    General,
    BossFight,
    MobClear,
    SupportFocus,
    Aggressive,
    Defensive,
    Custom,
}
```

## 共通Struct

```csharp
// チャレンジ結果
public struct ChallengeResult
{
    public string challengeId;
    public ChallengeRank rank;
    public float clearTime;
    public int score;
    public int deathCount;
    public int totalDamageDealt;
    public int totalDamageTaken;
    public bool isNewRecord;
}

// リーダーボード記録
[System.Serializable]
public struct LeaderboardEntry
{
    public string challengeId;
    public ChallengeRank rank;
    public float bestTime;
    public int bestScore;
    public int attemptCount;
    public int clearCount;
    public string dateAchieved;
}
```

## GameManager.Events 追加イベント

```csharp
// チャレンジ関連
public event System.Action<string> OnChallengeStarted;               // challengeId
public event System.Action<ChallengeResult> OnChallengeCompleted;    // result
public event System.Action<string> OnChallengeFailed;                // challengeId
public event System.Action<string, ChallengeRank> OnNewRecord;      // challengeId, newRank
```

## asmdef構成（Section 4）

既存asmdefに機能を追加する形で対応。

| asmdef | 追加内容 |
|--------|---------|
| Game.Core (既存拡張) | Section 4共通Enum/Struct |
| Game.AI (既存拡張) | AITemplateManager, AITemplateSuggester |
| Game.World (既存拡張) | ChallengeRunner, ChallengeScoreCalculator, LeaderboardManager |
| Game.Tests.EditMode (既存拡張) | 全EditModeテスト |
