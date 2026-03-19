# System: ChallengeMode
Section: 4 — エンドコンテンツ

## 責務
本編クリア後のやり込みコンテンツ。ボスラッシュ、タイムアタック、制限プレイ等の高難度チャレンジを管理する。

## 依存
- 入力: GameManager（Data/Events）、BossSystem（ボスデータ再利用）、EnemySystem（スポーン）、SaveSystem（クリア記録）
- 出力: OnChallengeStarted, OnChallengeCompleted, OnChallengeRankUpdated イベント

## コンポーネント構成

| コンポーネント | 責務 | MonoBehaviour? |
|--------------|------|---------------|
| ChallengeManager | チャレンジの一覧管理・進行制御・結果記録 | No |
| ChallengeDefinition | チャレンジのデータ定義（種別・条件・報酬） | No (ScriptableObject) |
| ChallengeRunner | チャレンジ実行中の状態管理・タイマー・勝敗判定 | No |
| ChallengeScoreCalculator | スコア計算（タイム・ダメージ・ランク） | No |

## データ構造

### ChallengeType
```csharp
public enum ChallengeType : byte
{
    BossRush,        // ボス連戦
    TimeAttack,      // 制限時間内クリア
    Survival,        // 耐久戦（Wave式）
    Restriction,     // 制限プレイ（装備制限、レベル制限等）
    ScoreAttack,     // 高スコア狙い
}
```

### ChallengeRank
```csharp
public enum ChallengeRank : byte
{
    None,    // 未クリア
    Bronze,  // クリア
    Silver,  // 条件達成
    Gold,    // 高スコア
    Platinum // パーフェクト
}
```

### ChallengeDefinition (ScriptableObject)
```csharp
[CreateAssetMenu(menuName = "Game/Challenge/ChallengeDefinition")]
public class ChallengeDefinition : ScriptableObject
{
    public string challengeId;
    public string challengeName;
    public string description;
    public ChallengeType challengeType;

    [Header("条件")]
    public float timeLimit;              // 制限時間（0=無制限）
    public int maxDeathCount;            // 最大死亡回数（0=即失敗）
    public string[] bossIds;             // BossRush用ボスID配列
    public int waveCount;                // Survival用Wave数
    public int levelCap;                 // Restriction用レベル上限（0=制限なし）

    [Header("ランク条件")]
    public float silverTimeThreshold;    // Silver以上のクリアタイム
    public float goldTimeThreshold;      // Gold以上のクリアタイム
    public int goldScoreThreshold;       // Gold以上のスコア

    [Header("報酬")]
    public int currencyReward;
    public string itemRewardId;
}
```

### ChallengeResult
```csharp
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
```

### ChallengeRunner (Pure Logic)
```csharp
public class ChallengeRunner
{
    public ChallengeState State { get; private set; } // Ready, Running, Completed, Failed
    public float ElapsedTime { get; private set; }
    public int CurrentWave { get; private set; }
    public int DeathCount { get; private set; }

    public void Start(ChallengeDefinition definition);
    public void Tick(float deltaTime);
    public void OnBossDefeated(string bossId);
    public void OnPlayerDeath();
    public void OnWaveCleared();
    public ChallengeResult GetResult();
}
```

### ChallengeScoreCalculator (Pure Logic)
```csharp
public static class ChallengeScoreCalculator
{
    public static int CalculateScore(ChallengeResult result, ChallengeDefinition definition);
    public static ChallengeRank EvaluateRank(ChallengeResult result, ChallengeDefinition definition);
}
```

## 機能分解

| 機能名 | 説明 | テスト種別 | 優先度 |
|--------|------|-----------|--------|
| ChallengeMode_Runner | チャレンジ進行管理（開始/Tick/終了/失敗判定） | EditMode | High |
| ChallengeMode_Score | スコア計算・ランク評価 | EditMode | High |
| ChallengeMode_Manager | チャレンジ一覧・記録・アンロック管理 | EditMode | Medium |
| ChallengeMode_BossRush | ボスラッシュ固有ロジック（連戦、インターバル） | EditMode | Medium |
| ChallengeMode_Survival | 耐久戦固有ロジック（Wave管理、難易度上昇） | EditMode | Low |

## 設計メモ
- チャレンジはSection 1-3の既存システムを再利用する。新しい戦闘ロジックは不要
- ボスラッシュはBossDefinition配列を順番に実行するだけ
- タイムアタックは既存ステージを時間制限付きで走らせるだけ
- 制限プレイはCharacterInfoのステータスをCap付きで適用
- スコアはクリアタイム×残HP率×コンボ数等の複合計算
