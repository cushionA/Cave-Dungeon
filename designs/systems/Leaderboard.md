# System: Leaderboard
Section: 4 — エンドコンテンツ

## 責務
チャレンジモードのスコア・タイム記録をローカル管理する。
自己ベスト更新、チャレンジ別記録、統計情報を提供する。

## 依存
- 入力:
  - `ChallengeResult` (Section 4 Common) — チャレンジ完了時の結果データ
  - `GameEvents.OnChallengeCompleted` — チャレンジ完了イベント購読
  - `SaveManager.Register(ISaveable)` — 記録永続化
- 出力:
  - `GameEvents.FireNewRecord(challengeId, newRank)` — 新記録通知（UI表示トリガー）

## コンポーネント構成

| コンポーネント | 責務 | MonoBehaviour? |
|--------------|------|---------------|
| LeaderboardManager | 記録の保存・読込・ランキング生成（ISaveable） | No |

## データ構造

### LeaderboardEntry (Common_Section4 で定義)
```csharp
// Section4Structs.cs に定義済み
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
```

### LeaderboardManager (Pure Logic, ISaveable)
```csharp
/// <summary>
/// チャレンジ記録管理。Dictionary<string, LeaderboardEntry> で challengeId → 記録を保持。
/// ISaveable 実装で SaveManager 経由で永続化する。
/// </summary>
public class LeaderboardManager : ISaveable
{
    public string SaveId => "LeaderboardManager";

    /// <summary>
    /// 記録を更新する。
    /// - attemptCount は毎回インクリメント
    /// - クリア時は clearCount インクリメント
    /// - bestScore/bestTime/rank は最高値を保持
    /// - 新記録なら ChallengeResult.isNewRecord = true にして true を返す
    /// </summary>
    public bool UpdateRecord(ChallengeResult result);

    /// <summary>チャレンジ別ベスト記録を取得。未挑戦なら default LeaderboardEntry</summary>
    public LeaderboardEntry GetBestRecord(string challengeId);

    /// <summary>全記録を取得（ランク降順 → スコア降順ソート）</summary>
    public LeaderboardEntry[] GetAllRecords();

    /// <summary>
    /// 統計情報:
    /// attempts: 全チャレンジの総挑戦回数
    /// clears: 全チャレンジの総クリア回数
    /// platinums: Platinum ランク獲得数
    /// </summary>
    public (int attempts, int clears, int platinums) GetStatistics();

    /// <summary>特定チャレンジの挑戦回数</summary>
    public int GetAttemptCount(string challengeId);

    /// <summary>特定チャレンジのクリア済みか</summary>
    public bool HasCleared(string challengeId);

    // ISaveable
    public object Serialize();
    public void Deserialize(object data);
}
```

## インタフェース

### システム間接続点

| 接続先 | 方式 | 用途 |
|--------|------|------|
| ChallengeManager | メソッド呼び出し | チャレンジ完了時に UpdateRecord() |
| GameEvents.OnChallengeCompleted | R3 Observable購読 | チャレンジ完了→自動記録更新 |
| GameEvents.FireNewRecord() | R3 Observable発火 | 新記録時にUI通知 |
| SaveManager.Register() | メソッド呼び出し | 記録永続化 |

## データフロー

```
ChallengeRunner.GetResult()
    ↓ ChallengeResult
ChallengeScoreCalculator.CalculateScore() → score設定
ChallengeScoreCalculator.EvaluateRank()  → rank設定
    ↓ ChallengeResult（score/rank設定済み）
LeaderboardManager.UpdateRecord()
    ├─ attemptCount++
    ├─ clearCount++ (if Completed)
    ├─ bestScore = max(bestScore, score)
    ├─ bestTime = min(bestTime, clearTime)
    ├─ rank = max(rank, newRank)
    └─ isNewRecord → GameEvents.FireNewRecord()
```

## 機能分解

| 機能名 | 説明 | テスト種別 | 優先度 |
|--------|------|-----------|--------|
| Leaderboard_RecordUpdate | 記録更新・新記録判定・ISaveable | EditMode | High |
| Leaderboard_Statistics | 統計集計（総挑戦/クリア/Platinum数）・ソート済み一覧 | EditMode | Medium |

## 設計メモ
- ローカルのみ（オンラインランキングは将来検討）
- ISaveable 実装パターンは SaveManager.cs（Section 1）の既存パターンに準拠
- Serialize() は Dictionary<string, LeaderboardEntry> をそのまま返す
- UIはチャレンジ選択画面で自己ベストとランクを表示（UISystem は別設計）
- bestTime の初期値は float.MaxValue（未クリア状態を表現）
