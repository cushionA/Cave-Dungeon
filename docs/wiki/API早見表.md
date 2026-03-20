# API早見表

## GameManager

```csharp
// データアクセス
GameManager.Data.GetCharacterInfo(int hashCode);
GameManager.Data.GetCombatStats(int hashCode);

// イベント購読
GameManager.Events.OnDamageDealt.Subscribe(result => { });
GameManager.Events.OnCharacterDeath.Subscribe(hash => { });

// イベント発火
GameManager.Events.FireDamageDealt(DamageResult result);
GameManager.Events.FireNewRecord(string challengeId, ChallengeRank rank);
```

## ISaveable

```csharp
public interface ISaveable
{
    string SaveId { get; }
    object Serialize();
    void Deserialize(object data);
}

// 登録
SaveManager.Register(ISaveable saveable);
```

## IGameSubManager

```csharp
public interface IGameSubManager
{
    int InitOrder { get; }
    void Initialize(SoACharaDataDic data, GameEvents events);
    void Dispose();
}
```

## IAbility

```csharp
public interface IAbility
{
    AbilityFlag Flag { get; }
    void Initialize();
    void Execute();
}
```

## IDamageable

```csharp
public interface IDamageable
{
    DamageResult TakeDamage(DamageRequest request);
}
```

## DamageSystem

```csharp
// ダメージ計算（各属性チャネル別）
DamageCalculator.Calculate(ElementalStatus attack, ElementalStatus defense, float motionValue);

// HP/アーマー適用
HpArmorLogic.ApplyDamage(CharacterInfo info, DamageResult result);
```

## AIBrain

```csharp
// 3層判定
AIBrain.Evaluate();

// 条件評価
ConditionEvaluator.Evaluate(AICondition[] conditions, AIContext context);

// ターゲット選択
TargetSelector.SelectTarget(TargetFilter filter, AIContext context);

// 行動実行
ActionExecutor.Execute(ActionSlot slot);
```

## CompanionAI

```csharp
// モード切替
CompanionController.SetMode(int modeIndex);

// AIルール適用
CompanionController.ApplyConfig(CompanionAIConfig config);

// 連携実行
CoopActionBase.Execute(CoopActionContext context);
```

## ChallengeMode

```csharp
// チャレンジ実行
ChallengeRunner.Start(ChallengeDefinition definition);
ChallengeRunner.Tick(float deltaTime);
ChallengeResult result = ChallengeRunner.GetResult();

// スコア計算
int score = ChallengeScoreCalculator.CalculateScore(ChallengeResult result);
ChallengeRank rank = ChallengeScoreCalculator.EvaluateRank(ChallengeResult result);

// 記録更新
bool isNewRecord = LeaderboardManager.UpdateRecord(ChallengeResult result);
```

## AITemplates

```csharp
// テンプレート管理
AITemplateManager.SaveTemplate(AITemplateData template);
AITemplateData[] templates = AITemplateManager.GetTemplates(AITemplateCategory? category);
AITemplateManager.ApplyTemplate(string templateId, CompanionController target);
AITemplateManager.RevertTemplate(CompanionController target);

// テンプレート推薦
AITemplateData[] suggestions = AITemplateSuggester.Suggest(AITemplateData[] all, AIContext context);
```

## BossSystem

```csharp
// ボス戦開始
BossControllerLogic.StartEncounter();

// ボス撃破イベント
BossControllerLogic.OnBossDefeated; // R3 Observable

// フェーズ遷移
BossPhaseManager.EvaluatePhase(float hpRatio, float elapsedTime, int actionCount);
```

## SaveManager

```csharp
// セーブ/ロード
SaveManager.Save(string slotId);
SaveManager.Load(string slotId);

// 登録
SaveManager.Register(ISaveable saveable);
```
