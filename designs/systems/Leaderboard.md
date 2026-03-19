# System: Leaderboard
Section: 4 — エンドコンテンツ

## 責務
チャレンジモードのスコア・タイム記録をローカル管理する。自己ベスト更新、チャレンジ別ランキング、統計情報を提供する。

## 依存
- 入力: ChallengeMode（ChallengeResult）、SaveSystem（永続化）
- 出力: OnNewRecord イベント → UISystem（記録更新通知）

## コンポーネント構成

| コンポーネント | 責務 | MonoBehaviour? |
|--------------|------|---------------|
| LeaderboardManager | 記録の保存・読込・ランキング生成 | No |
| LeaderboardEntry | 1記録分のデータ | No (struct) |

## データ構造

### LeaderboardEntry
```csharp
[System.Serializable]
public struct LeaderboardEntry
{
    public string challengeId;
    public ChallengeRank rank;
    public float bestTime;
    public int bestScore;
    public int attemptCount;
    public int clearCount;
    public string dateAchieved;    // ISO 8601 文字列
}
```

### LeaderboardManager (Pure Logic, ISaveable)
```csharp
public class LeaderboardManager : ISaveable
{
    // 記録を更新する。新記録ならtrueを返す
    public bool UpdateRecord(ChallengeResult result);

    // チャレンジ別ベスト記録を取得
    public LeaderboardEntry GetBestRecord(string challengeId);

    // 全記録を取得（ランク降順ソート）
    public LeaderboardEntry[] GetAllRecords();

    // 統計: 総プレイ回数、総クリア回数、Platinum獲得数
    public (int attempts, int clears, int platinums) GetStatistics();
}
```

## 機能分解

| 機能名 | 説明 | テスト種別 | 優先度 |
|--------|------|-----------|--------|
| Leaderboard_RecordUpdate | 記録更新・新記録判定 | EditMode | High |
| Leaderboard_Statistics | 統計集計・ランキング生成 | EditMode | Medium |

## 設計メモ
- ローカルのみ（オンラインランキングは将来検討）
- SaveSystem経由で永続化（ISaveable実装）
- UIはチャレンジ選択画面で自己ベストとランクを表示
