# System: BossSystem
Section: 3 — 世界の広がり

## 責務
ボス戦の制御全般。フェーズ遷移付きボスAI、アリーナロック、専用演出トリガーを管理する。

## 依存
- 入力: AICore（AIBrain, AIMode, ActionSlot）、DamageSystem（HP監視、ダメージ処理）、GameManager.Events
- 出力: OnBossPhaseChanged, OnBossDefeated, OnBossEncounterStart イベント → GateSystem（ClearGate開放）、UISystem（ボスHPバー）

## コンポーネント構成

| コンポーネント | 責務 | MonoBehaviour? |
|--------------|------|---------------|
| BossController | ボスAI統括。AIBrain拡張でフェーズ遷移を制御 | Yes |
| BossPhaseManager | フェーズ条件評価・切替実行。AIMode配列のスワップ | No |
| BossArenaManager | アリーナロック/解除、出入口コライダー管理 | Yes |
| BossDefinition | ボスのフェーズ構成・遷移条件・ドロップをデータ定義 | No (ScriptableObject) |

## インタフェース

### BossController → GameManager.Events
```csharp
// ボスエリアのトリガーに入ったら戦闘開始
GameManager.Events.OnBossEncounterStart?.Invoke(bossHash);

// フェーズ変更時
GameManager.Events.OnBossPhaseChanged?.Invoke(bossHash, oldPhase, newPhase);

// 撃破時
GameManager.Events.OnBossDefeated?.Invoke(bossHash);
```

### BossArenaManager → MapSystem
- 戦闘開始: 出入口コライダーを有効化（アリーナロック）
- 戦闘終了: コライダー無効化 + 永続ClearGateをOpen状態に

### BossController → UISystem
- ボスHPバー表示開始/フェーズ表示更新/非表示（既存UISystemのイベント経由）

## データフロー

```
プレイヤーがアリーナトリガーに侵入
    → BossArenaManager.LockArena()
    → BossController.StartEncounter()
    → GameManager.Events.OnBossEncounterStart

戦闘中:
    → AIBrain.Evaluate() で通常のAI判定ループ
    → BossPhaseManager.CheckTransition() を毎判定で実行
    → HP閾値到達 → BossPhaseManager.TransitionToPhase(nextPhase)
        → currentModes[] を次フェーズのAIMode[]にスワップ
        → 遷移演出トリガー（無敵時間 + カットイン等）
        → GameManager.Events.OnBossPhaseChanged

撃破:
    → BossController.OnDefeated()
    → GameManager.Events.OnBossDefeated
    → BossArenaManager.UnlockArena()
    → DropTable からドロップ生成
    → ClearGate 永続開放
```

## データ構造

### BossDefinition (ScriptableObject)
```csharp
[CreateAssetMenu(menuName = "Game/Boss/BossDefinition")]
public class BossDefinition : ScriptableObject
{
    [Header("基本情報")]
    public string bossName;
    public string bossId;          // ClearGate用の識別子

    [Header("フェーズ構成")]
    public BossPhaseData[] phases; // フェーズ0から順に定義

    [Header("報酬")]
    public DropTable dropTable;    // 既存DropTable再利用
    public int expReward;
    public int currencyReward;

    [Header("アリーナ")]
    public float arenaLockDelay;   // 侵入→ロックまでの猶予(秒)
    public bool allowReEntry;      // 死亡後の再挑戦可否
}
```

### BossPhaseData (Serializable)
```csharp
[System.Serializable]
public struct BossPhaseData
{
    public string phaseName;           // "第1形態" 等
    public AIMode[] modes;             // このフェーズのAIモード配列
    public PhaseCondition exitCondition; // このフェーズから次への遷移条件
    public float transitionInvincibleTime; // 遷移時無敵秒数
    public bool spawnAdds;             // 雑魚召喚フラグ
    public string[] addSpawnerIds;     // 召喚する雑魚のスポナーID
}
```

### BossPhaseManager (Pure Logic)
```csharp
public class BossPhaseManager
{
    public int CurrentPhase { get; private set; }
    public int MaxPhase { get; }

    // コンストラクタでBossDefinitionを受け取る
    public BossPhaseManager(BossDefinition definition);

    // 遷移条件チェック（毎判定間隔で呼ばれる）
    public bool CheckTransition(float currentHpRatio, float elapsedTime, int actionCount);

    // フェーズ遷移実行 → 新しいAIMode[]を返す
    public AIMode[] TransitionToNextPhase();

    // 現フェーズのデータ取得
    public BossPhaseData GetCurrentPhaseData();
}
```

### BossController (MonoBehaviour, extends AIBrain)
```csharp
public class BossController : MonoBehaviour
{
    [SerializeField] private BossDefinition _definition;
    [SerializeField] private BossArenaManager _arenaManager;

    private BossPhaseManager _phaseManager;
    private float _encounterElapsedTime;
    private bool _isEncounterActive;

    public void StartEncounter();
    public void OnDefeated();

    // AIBrainのEvaluate()をオーバーライドしてフェーズチェックを追加
    protected override void OnEvaluateComplete()
    {
        if (_phaseManager.CheckTransition(hpRatio, _encounterElapsedTime, actionCount))
        {
            AIMode[] newModes = _phaseManager.TransitionToNextPhase();
            SetModes(newModes); // AIBrainのモード配列を差し替え
        }
    }
}
```

### BossArenaManager (MonoBehaviour)
```csharp
public class BossArenaManager : MonoBehaviour
{
    [SerializeField] private Collider2D[] _lockColliders;  // 出入口封鎖用
    [SerializeField] private string _clearGateId;           // 撃破後に開くゲートID

    public ArenaState State { get; private set; }

    public void LockArena();
    public void UnlockArena();  // + GateRegistry.Open(_clearGateId)
}
```

## 機能分解

| 機能名 | 説明 | テスト種別 | 優先度 |
|--------|------|-----------|--------|
| BossSystem_PhaseManager | フェーズ条件評価・遷移ロジック（HP閾値、タイマー、行動回数） | EditMode | High |
| BossSystem_Controller | BossControllerのエンカウント開始/終了、AIBrain統合 | EditMode | High |
| BossSystem_Arena | アリーナロック/解除、ClearGate連携 | EditMode | High |
| BossSystem_AddSpawn | フェーズ遷移時の雑魚召喚トリガー | EditMode | Medium |
| BossSystem_Rewards | 撃破報酬（DropTable/EXP/通貨）配布 | EditMode | Medium |

## 設計メモ
- BossControllerはAIBrainを**継承ではなくコンポジション**で使用する。AIBrainをSerializeFieldで保持し、フェーズ遷移時にAIBrainのモード配列を差し替える方式。これにより既存AIBrainコードを変更せずにボス固有ロジックを追加できる
- フェーズ遷移中の無敵時間はDamageSystemのinvincibleフラグで実現（既存機能）
- 雑魚召喚はEnemySpawnerのActivate()を直接呼ぶ（新システム不要）
- ボスHPバーUIはSection 3のUISystem拡張で対応（設計書は作成せず、機能として追加）
