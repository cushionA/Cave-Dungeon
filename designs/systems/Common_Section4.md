# Common Design: Section 4 — エンドコンテンツ

## 共通Enum（Enums.cs に追加）

```csharp
// ===== Section 4: チャレンジ・テンプレート・リーダーボード =====

/// <summary>
/// チャレンジモードの種別。
/// </summary>
public enum ChallengeType : byte
{
    BossRush,        // ボス連戦
    TimeAttack,      // 制限時間内クリア
    Survival,        // 耐久戦（Wave式）
    Restriction,     // 制限プレイ（装備制限、レベル制限等）
    ScoreAttack,     // 高スコア狙い
}

/// <summary>
/// チャレンジの達成ランク。
/// </summary>
public enum ChallengeRank : byte
{
    None,       // 未クリア
    Bronze,     // クリア
    Silver,     // 条件達成
    Gold,       // 高スコア
    Platinum,   // パーフェクト
}

/// <summary>
/// チャレンジの進行状態。
/// </summary>
public enum ChallengeState : byte
{
    Ready,       // 開始前
    Running,     // 実行中
    Completed,   // クリア
    Failed,      // 失敗
}

/// <summary>
/// AIテンプレートのカテゴリ。
/// PresetManager（Section 2）の AIPreset を拡張してカテゴリ分類する。
/// </summary>
public enum AITemplateCategory : byte
{
    General,         // 汎用
    BossFight,       // ボス戦特化
    MobClear,        // 雑魚掃討
    SupportFocus,    // 回復・バフ重視
    Aggressive,      // 攻撃特化
    Defensive,       // 防御特化
    Custom,          // ユーザー作成
}
```

## 共通Struct（Section4Structs.cs）

```csharp
using System;

namespace Game.Core
{
    /// <summary>
    /// チャレンジ1回分の結果データ。
    /// ChallengeRunner.GetResult() で生成、ChallengeScoreCalculator とLeaderboardManager が消費。
    /// </summary>
    [Serializable]
    public struct ChallengeResult
    {
        public string challengeId;
        public ChallengeRank rank;
        public float clearTime;         // クリアまでの経過秒数
        public int score;               // 最終スコア
        public int deathCount;          // 死亡回数
        public int totalDamageDealt;    // 合計与ダメージ
        public int totalDamageTaken;    // 合計被ダメージ
        public bool isNewRecord;        // 新記録フラグ（LeaderboardManager が設定）
    }

    /// <summary>
    /// リーダーボードの1チャレンジ分の記録。
    /// LeaderboardManager が内部管理し、ISaveable 経由で永続化。
    /// </summary>
    [Serializable]
    public struct LeaderboardEntry
    {
        public string challengeId;
        public ChallengeRank rank;       // 最高ランク
        public float bestTime;           // 最速クリアタイム
        public int bestScore;            // 最高スコア
        public int attemptCount;         // 挑戦回数
        public int clearCount;           // クリア回数
        public string dateAchieved;      // 最高記録達成日（ISO 8601）
    }

    /// <summary>
    /// AIテンプレートデータ。
    /// 既存 CompanionAIConfig（AIMode[] + ModeTransitionRule[] + shortcutBindings）に
    /// メタ情報（名前・カテゴリ・タグ）を付与した拡張構造体。
    /// </summary>
    [Serializable]
    public struct AITemplateData
    {
        public string templateId;
        public string templateName;
        public string description;
        public string authorName;             // "System" or ユーザー名
        public AITemplateCategory category;
        public CompanionAIConfig config;      // 既存の AIMode[] + TransitionRule[] を内包
        public string[] tags;                 // "ボス向け", "雑魚処理", "回復重視" 等
    }
}
```

### 旧設計からの変更点
- `AITemplateData` に `AIMode[]` と `ModeTransitionRule[]` を直接持たせず、既存 `CompanionAIConfig` をそのまま内包
- `CompanionAIConfig` は Section 2 の `RuleEditorLogic.BuildConfig()` で生成される構造体をそのまま使用
- これにより PresetManager の既存 `AIPreset` → `AITemplateData` へのマッピングが単純になる

## GameEvents 追加イベント（R3 Subject パターン）

```csharp
// GameEvents.cs に追加
// ===== Section 4: チャレンジ =====
private readonly Subject<string> _onChallengeStarted = new();
private readonly Subject<ChallengeResult> _onChallengeCompleted = new();
private readonly Subject<string> _onChallengeFailed = new();
private readonly Subject<(string challengeId, ChallengeRank newRank)> _onNewRecord = new();
public Observable<string> OnChallengeStarted => _onChallengeStarted;
public Observable<ChallengeResult> OnChallengeCompleted => _onChallengeCompleted;
public Observable<string> OnChallengeFailed => _onChallengeFailed;
public Observable<(string challengeId, ChallengeRank newRank)> OnNewRecord => _onNewRecord;

// Fire methods
public void FireChallengeStarted(string challengeId) => _onChallengeStarted.OnNext(challengeId);
public void FireChallengeCompleted(ChallengeResult result) => _onChallengeCompleted.OnNext(result);
public void FireChallengeFailed(string challengeId) => _onChallengeFailed.OnNext(challengeId);
public void FireNewRecord(string challengeId, ChallengeRank newRank)
    => _onNewRecord.OnNext((challengeId, newRank));

// Dispose() に追加
_onChallengeStarted.Dispose();
_onChallengeCompleted.Dispose();
_onChallengeFailed.Dispose();
_onNewRecord.Dispose();
```

## asmdef構成（Section 4）

既存asmdefに機能を追加する形で対応。新規asmdef作成は不要。

| asmdef | 追加内容 | 追加ディレクトリ |
|--------|---------|----------------|
| Game.Core (既存拡張) | Section 4共通Enum/Struct | `Core/Common/Section4Structs.cs`, `Core/Common/Enums.cs` 追記 |
| Game.AI (既存拡張) | AITemplateManager, AITemplateSuggester | `Core/AI/Templates/` |
| Game.World (既存拡張) | ChallengeRunner, ChallengeScoreCalculator, ChallengeManager, LeaderboardManager | `Core/World/Challenge/` |
| Game.Tests.EditMode (既存拡張) | 全EditModeテスト | `Tests/EditMode/` |
