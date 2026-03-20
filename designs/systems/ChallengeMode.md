# System: ChallengeMode
Section: 4 — エンドコンテンツ

## 責務
本編クリア後のやり込みコンテンツ。ボスラッシュ、タイムアタック、制限プレイ等の高難度チャレンジを管理する。

## 依存
- 入力:
  - `GameEvents.OnCharacterDeath` — プレイヤー死亡検知（死亡回数カウント）
  - `GameEvents.OnDamageDealt` — ダメージ集計（スコア計算用）
  - `GameEvents.OnEnemyDefeated` — 敵撃破検知（Wave進行、ボス撃破判定）
  - `BossControllerLogic.OnBossDefeated` — ボスラッシュ用ボス撃破コールバック
  - `BossControllerLogic.StartEncounter()` — ボスラッシュ用ボス戦開始
  - `SaveManager.Register(ISaveable)` — クリア記録永続化
- 出力:
  - `GameEvents.FireChallengeStarted(challengeId)` — チャレンジ開始通知
  - `GameEvents.FireChallengeCompleted(ChallengeResult)` — チャレンジ完了通知
  - `GameEvents.FireChallengeFailed(challengeId)` — チャレンジ失敗通知

## コンポーネント構成

| コンポーネント | 責務 | MonoBehaviour? |
|--------------|------|---------------|
| ChallengeManager | チャレンジの一覧管理・進行制御・結果記録（ISaveable） | No |
| ChallengeDefinition | チャレンジのデータ定義（ScriptableObject） | No (ScriptableObject) |
| ChallengeRunner | チャレンジ実行中の状態管理・タイマー・勝敗判定 | No |
| ChallengeScoreCalculator | スコア計算・ランク評価（static class） | No |
| BossRushLogic | ボスラッシュ固有ロジック（連戦管理） | No |
| SurvivalLogic | 耐久戦固有ロジック（Wave管理） | No |

## データ構造

### ChallengeDefinition (ScriptableObject)
```csharp
[CreateAssetMenu(menuName = "Game/Challenge/ChallengeDefinition")]
public class ChallengeDefinition : ScriptableObject
{
    [SerializeField] private string challengeId;
    [SerializeField] private string challengeName;
    [SerializeField] private string description;
    [SerializeField] private ChallengeType challengeType;

    [Header("条件")]
    [SerializeField] private float timeLimit;              // 制限時間（0=無制限）
    [SerializeField] private int maxDeathCount;            // 最大死亡回数（0=即失敗）

    [Header("BossRush")]
    [SerializeField] private string[] bossIds;             // BossControllerLogic に渡す bossId 配列

    [Header("Survival")]
    [SerializeField] private int waveCount;                // Wave数
    [SerializeField] private int enemiesPerWave;           // 1Waveあたりの敵数

    [Header("Restriction")]
    [SerializeField] private int levelCap;                 // レベル上限（0=制限なし）

    [Header("ランク条件")]
    [SerializeField] private float silverTimeThreshold;    // Silver以上のクリアタイム
    [SerializeField] private float goldTimeThreshold;      // Gold以上のクリアタイム
    [SerializeField] private int goldScoreThreshold;       // Gold以上のスコア
    [SerializeField] private int platinumScoreThreshold;   // Platinum のスコア

    [Header("報酬")]
    [SerializeField] private int currencyReward;
    [SerializeField] private string itemRewardId;

    // Properties
    public string ChallengeId => challengeId;
    public string ChallengeName => challengeName;
    public string Description => description;
    public ChallengeType Type => challengeType;
    public float TimeLimit => timeLimit;
    public int MaxDeathCount => maxDeathCount;
    public string[] BossIds => bossIds;
    public int WaveCount => waveCount;
    public int EnemiesPerWave => enemiesPerWave;
    public int LevelCap => levelCap;
    public float SilverTimeThreshold => silverTimeThreshold;
    public float GoldTimeThreshold => goldTimeThreshold;
    public int GoldScoreThreshold => goldScoreThreshold;
    public int PlatinumScoreThreshold => platinumScoreThreshold;
    public int CurrencyReward => currencyReward;
    public string ItemRewardId => itemRewardId;
}
```

### ChallengeRunner (Pure Logic)
```csharp
public class ChallengeRunner
{
    public ChallengeState State { get; private set; }
    public float ElapsedTime { get; private set; }
    public int CurrentWave { get; private set; }       // Survival用
    public int DeathCount { get; private set; }
    public int BossesDefeated { get; private set; }    // BossRush用
    public int TotalDamageDealt { get; private set; }
    public int TotalDamageTaken { get; private set; }

    /// <summary>チャレンジ開始</summary>
    public void Start(ChallengeDefinition definition);

    /// <summary>毎フレーム更新。制限時間超過時にFailedに遷移</summary>
    public void Tick(float deltaTime);

    /// <summary>
    /// ボス撃破通知。BossControllerLogic.OnBossDefeated から呼ばれる。
    /// BossRush: bossIds配列の順番と照合し、全撃破でCompleted。
    /// </summary>
    public void OnBossDefeated(string bossId);

    /// <summary>
    /// プレイヤー死亡通知。GameEvents.OnCharacterDeath から呼ばれる。
    /// maxDeathCount超過でFailed。
    /// </summary>
    public void OnPlayerDeath();

    /// <summary>Wave完了通知（Survival用）。全Wave完了でCompleted</summary>
    public void OnWaveCleared();

    /// <summary>
    /// ダメージ発生通知。GameEvents.OnDamageDealt から呼ばれる。
    /// プレイヤー側のダメージを集計する。
    /// </summary>
    public void OnDamageDealt(DamageResult result, int attackerHash, int defenderHash);

    /// <summary>結果を生成（State が Completed/Failed の時のみ有効）</summary>
    public ChallengeResult GetResult();
}
```

### ChallengeScoreCalculator (Static Pure Logic)
```csharp
public static class ChallengeScoreCalculator
{
    /// <summary>
    /// スコア = (baseScore / clearTime) × hpRatio × deathPenalty
    /// baseScore: 10000（固定）
    /// hpRatio: プレイヤー残HP%（0.0-1.0）× 200（ボーナス）
    /// deathPenalty: max(0, 1.0 - deathCount × 0.1)
    /// </summary>
    public static int CalculateScore(ChallengeResult result, ChallengeDefinition definition);

    /// <summary>
    /// ランク評価:
    /// Platinum: score >= platinumScoreThreshold AND clearTime <= goldTimeThreshold AND deathCount == 0
    /// Gold: score >= goldScoreThreshold OR clearTime <= goldTimeThreshold
    /// Silver: clearTime <= silverTimeThreshold
    /// Bronze: クリアしていればBronze
    /// None: 未クリア
    /// </summary>
    public static ChallengeRank EvaluateRank(ChallengeResult result, ChallengeDefinition definition);
}
```

### BossRushLogic (Pure Logic)
```csharp
/// <summary>
/// ボスラッシュ固有ロジック。
/// ChallengeDefinition.BossIds[] の順番にボスを出現させ、
/// 各 BossControllerLogic.StartEncounter() を呼ぶ。
/// </summary>
public class BossRushLogic
{
    public int CurrentBossIndex { get; private set; }
    public int TotalBossCount { get; }
    public bool IsAllDefeated { get; }

    public BossRushLogic(string[] bossIds);

    /// <summary>次のボスID を返す。全撃破済みなら null</summary>
    public string GetNextBossId();

    /// <summary>ボス撃破を記録し、次に進む</summary>
    public void MarkDefeated(string bossId);
}
```

### SurvivalLogic (Pure Logic)
```csharp
/// <summary>
/// 耐久戦固有ロジック。
/// Wave数とWaveごとの敵数を管理する。
/// </summary>
public class SurvivalLogic
{
    public int CurrentWave { get; private set; }
    public int TotalWaves { get; }
    public int EnemiesRemainingInWave { get; private set; }
    public bool IsAllWavesCleared { get; }

    public SurvivalLogic(int totalWaves, int enemiesPerWave);

    /// <summary>敵撃破を記録。Wave内の敵が全滅したら true を返す</summary>
    public bool OnEnemyDefeated();

    /// <summary>次の Wave に進む</summary>
    public void AdvanceWave();
}
```

### ChallengeManager (Pure Logic, ISaveable)
```csharp
/// <summary>
/// チャレンジの一覧管理・アンロック状態・記録保持。
/// SaveManager.Register() で永続化。
/// </summary>
public class ChallengeManager : ISaveable
{
    public string SaveId => "ChallengeManager";

    /// <summary>アンロック済みチャレンジID一覧</summary>
    public string[] GetUnlockedChallengeIds();

    /// <summary>チャレンジをアンロック</summary>
    public void UnlockChallenge(string challengeId);

    /// <summary>アンロック済みか</summary>
    public bool IsUnlocked(string challengeId);

    // ISaveable
    public object Serialize();
    public void Deserialize(object data);
}
```

## インタフェース

### システム間接続点

| 接続先 | 方式 | 用途 |
|--------|------|------|
| GameEvents.OnCharacterDeath | R3 Observable購読 | プレイヤー死亡→ChallengeRunner.OnPlayerDeath() |
| GameEvents.OnDamageDealt | R3 Observable購読 | ダメージ集計→ChallengeRunner.OnDamageDealt() |
| GameEvents.OnEnemyDefeated | R3 Observable購読 | 敵撃破→SurvivalLogic.OnEnemyDefeated() |
| BossControllerLogic.OnBossDefeated | C# event購読 | ボス撃破→BossRushLogic.MarkDefeated() |
| BossControllerLogic.StartEncounter() | メソッド呼び出し | ボスラッシュで次のボス戦を開始 |
| SaveManager.Register() | メソッド呼び出し | ChallengeManager永続化 |
| LeaderboardManager.UpdateRecord() | メソッド呼び出し | チャレンジ完了時に記録更新 |

## 機能分解

| 機能名 | 説明 | テスト種別 | 優先度 |
|--------|------|-----------|--------|
| ChallengeMode_Runner | チャレンジ進行管理（開始/Tick/終了/失敗判定/ダメージ集計） | EditMode | High |
| ChallengeMode_Score | スコア計算（複合式）・ランク評価（4段階閾値） | EditMode | High |
| ChallengeMode_Manager | チャレンジ一覧・アンロック管理・ISaveable | EditMode | Medium |
| ChallengeMode_BossRush | ボスラッシュ固有ロジック（BossControllerLogic連携、連戦進行） | EditMode | Medium |
| ChallengeMode_Survival | 耐久戦固有ロジック（Wave管理、敵数カウント） | EditMode | Low |

## 設計メモ
- Section 1-3の既存システムを再利用。新しい戦闘ロジックは不要
- ボスラッシュは `BossControllerLogic.StartEncounter()` を順番に呼ぶだけ。BossPhaseData[] は既存の BossDefinition から取得
- タイムアタックは ChallengeRunner の timeLimit で制御
- 制限プレイは CharacterInfo のステータスを LevelCap で制限（ChallengeManager が一時制限を適用）
- スコア計算式は ChallengeScoreCalculator に集約。調整しやすいよう定数を分離
- ChallengeManager は ISaveable でアンロック状態のみ永続化。スコア記録は LeaderboardManager が担当
