# System Dependency Graph

## Section 1: MVP — 戦闘と探索の基盤

### 実装順序（レイヤー順）

```
Layer 0 (依存なし):
  DataContainer, InputSystem, MapSystem

Layer 1 (← Layer 0):
  GameManager         ← DataContainer
  PlayerMovement      ← InputSystem

Layer 2 (← Layer 0-1):
  EquipmentSystem     ← DataContainer, GameManager
  LevelStreaming      ← MapSystem
  CurrencySystem      ← DataContainer, GameManager
  InventorySystem     ← DataContainer, GameManager
  LevelUpSystem       ← DataContainer, GameManager

Layer 3 (← Layer 0-2):
  WeaponSystem        ← InputSystem, EquipmentSystem, DataContainer
  DamageSystem        ← DataContainer, EquipmentSystem

Layer 4 (← Layer 0-3):
  ParryGuardSystem    ← DamageSystem, EquipmentSystem
  ShopSystem          ← CurrencySystem, InventorySystem
  SaveSystem          ← MapSystem, (全ISaveable実装)
  UISystem_Basic      ← DataContainer, CurrencySystem, MapSystem
```

### 依存関係図

```
                    InputSystem ─────────────────┐
                        │                        │
                        ▼                        ▼
DataContainer ──► GameManager           PlayerMovement
    │                │
    │                ▼
    ├──────► EquipmentSystem ──────┐
    │                │             │
    │                ▼             ▼
    │          WeaponSystem   DamageSystem
    │                              │
    │                              ▼
    │                       ParryGuardSystem
    │
    ├──────► CurrencySystem ──► ShopSystem
    │                            ▲
    ├──────► InventorySystem ────┘
    │
    ├──────► LevelUpSystem
    │
    │         MapSystem ──► LevelStreaming
    │             │
    │             ▼
    └───► UISystem_Basic    SaveSystem (← 全ISaveable)
```

### システム間通信（Section 1）

| 発信 | → | 受信 | 方式 | 内容 |
|------|---|------|------|------|
| WeaponSystem | → | DamageSystem | DamageData構造体 | ヒット時のダメージ情報 |
| DamageSystem | → | ParryGuardSystem | メソッド呼び出し | ガード判定要求 |
| DamageSystem | → | UISystem_Basic | C# event (OnDamageDealt) | ダメージ数字表示 |
| DamageSystem | → | GameManager.Events | C# event (OnCharacterDeath) | 死亡通知 |
| EquipmentSystem | → | PlayerMovement | C# event (OnAbilityFlagsChanged) | 移動能力変更 |
| EquipmentSystem | → | WeaponSystem | C# event (OnEquipmentChanged) | 武器データ+アニメーション更新 |
| LevelUpSystem | → | EquipmentSystem | 再計算トリガー | ステータス変更→装備再計算 |
| MapSystem | → | LevelStreaming | AreaBoundary | エリア遷移トリガー |
| ParryGuardSystem | → | UISystem_Basic | C# event (OnGuardEvent) | JG成功/ガードブレイク表示 |

## Assembly Definition 設計

| asmdef | パス | 参照先 | 用途 |
|--------|------|--------|------|
| Game.Core | Assets/MyAsset/Core/ | Unity.Collections, Unity.InputSystem | SoAコンテナ, 共通型, GameManager |
| Game.Character | Assets/MyAsset/Character/ | Game.Core | BaseCharacter, Ability, Movement |
| Game.Combat | Assets/MyAsset/Combat/ | Game.Core, Game.Character | Weapon, Damage, Guard, Equipment |
| Game.AI | Assets/MyAsset/AI/ | Game.Core, Game.Character | AIBrain, Sensor, Companion, Enemy (Section 2) |
| Game.World | Assets/MyAsset/World/ | Game.Core | Map, LevelStreaming, SaveSystem |
| Game.Economy | Assets/MyAsset/Economy/ | Game.Core | Currency, Shop, Inventory, LevelUp |
| Game.UI | Assets/MyAsset/UI/ | Game.Core, Game.Combat, Game.Economy | HUD, Menu, Screen |
| Game.Editor | Assets/MyAsset/Editor/ | Game.Core, (全Runtime) | Editor拡張 |
| Game.Tests.EditMode | Assets/Tests/EditMode/ | Game.Core, Game.Combat, Game.Economy | EditModeテスト |
| Game.Tests.PlayMode | Assets/Tests/PlayMode/ | Game.Core, Game.Character, Game.Combat | PlayModeテスト |

### asmdef設計の原則
- **循環参照禁止**: Core ← Character ← Combat / AI / World / Economy ← UI
- Game.Coreは全asmdefから参照される基盤
- Game.AIはSection 2で実装開始。asmdef自体はSection 1で作成しておく（空）
- Game.UIは他のRuntime asmdefを参照可能（表示のみ、ロジックなし）
- テスト用asmdefはテスト対象のRuntime asmdefを参照

## Section 2: AI・仲間・連携

### 実装順序（レイヤー順）
```
Layer 0 (Section 1の上に構築):
  AICore              ← DataContainer, GameManager

Layer 1:
  CompanionAI_Basic   ← AICore, PlayerMovement, InputSystem
  EnemySystem         ← AICore, DamageSystem

Layer 2:
  MagicSystem         ← DataContainer, DamageSystem
  AIRuleBuilder       ← CompanionAI_Basic
  CoopAction          ← CompanionAI_Basic, InputSystem, AICore(TargetSelector), MagicSystem
  GateSystem          ← MapSystem, SaveSystem, InventorySystem

Layer 3:
  CooldownReward      ← AIRuleBuilder, CompanionAI_Basic
```

### 依存関係図（Section 2 追加分）
```
Section 1 基盤
    │
    ▼
DataContainer ──► AICore ──────────┐
GameManager ──────┘                 │
                                    ▼
InputSystem ──► CompanionAI_Basic ──┬──► AIRuleBuilder ──► CooldownReward
PlayerMovement ──┘    │             │
                      │             └──► CoopAction（連携ボタンスキル）
DamageSystem ──► EnemySystem

                   MapSystem ──────► GateSystem
                   SaveSystem ─────────┘
                   InventorySystem ─────┘
```

### システム間通信（Section 2）

| 発信 | → | 受信 | 方式 | 内容 |
|------|---|------|------|------|
| EnemySystem | → | CurrencySystem | C# event (OnEnemyDefeated) | 通貨ドロップ |
| EnemySystem | → | LevelUpSystem | C# event (OnEnemyDefeated) | 経験値配布 |
| EnemySystem | → | InventorySystem | C# event (OnEnemyDefeated) | アイテムドロップ |
| InputSystem | → | CompanionAI_Basic | cooperationPressed | 連携ボタン |
| SaveSystem | → | EnemySystem | 休息通知 (OnRest) | 敵リスポーン |
| DamageSystem | → | AICore.DamageScoreTracker | OnDamageDealt | 累積ダメージスコア加算 |
| AIRuleBuilder | → | CompanionAI_Basic | OnCustomRulesChanged | ルール更新通知 |
| AIRuleBuilder | → | CooldownReward | OnCustomRulesChanged | ルールキャッシュ更新 |
| CoopAction | → | UISystem | OnCoopActivated | 連携演出トリガー |
| GateSystem | → | MapSystem | OnGateOpened | ミニマップ更新 |
| GateSystem | → | SaveSystem | OnGateOpened | ゲート状態永続化 |
| CooldownReward | → | UISystem | OnCooldownReady | クールタイム完了表示 |
| CooldownReward | → | UISystem | OnFreeCoopActivated | MP無料演出 |
| CompanionAI_Basic | → | CooldownReward | CooperationButton.Activate | MP無料判定要求 |

### asmdef 配置（Section 2）

```
Game.AI (Assets/MyAsset/AI/) — Section 2で実装開始
    ├── Core/           AIBrain, ConditionEvaluator, TargetSelector, DamageScoreTracker
    ├── Companion/      CompanionController, FollowBehavior, StanceManager, CooperationButton
    ├── Enemy/          EnemyController, EnemySpawner, DropTable, LootDropper
    ├── RuleBuilder/    RuleEditorLogic, ConditionBuilder, RulePresetManager
    ├── Cooldown/       CooldownRewardEvaluator, CooldownTracker
    └── Data/           AIInfo, CompanionBehaviorSetting, AIRulePreset (ScriptableObjects)

Game.World (Assets/MyAsset/World/) — 既存に追加
    ├── Gate/           GateController, GateRegistry, GateConditionChecker
    └── Coop/           CoopActionManager, CoopTrigger, CoopExecutor

Game.Core (Assets/MyAsset/Core/Common/) — 既存に追加
    └── Enums.cs        CoopActionType, GateType 追加

Game.Tests.EditMode — テスト参照追加
    └── Game.AI 参照追加
```

## Section 3: 世界の広がり

### 実装順序（レイヤー順）
```
Layer 0 (Section 1-2の上に構築):
  Common_Section3Types   ← Common_SharedTypes（共通Enum/Struct追加）

Layer 1 (← Layer 0):
  BossSystem_PhaseManager     ← AICore, DamageSystem
  ConfusionMagic_Accumulation ← DamageSystem(StatusEffectManager)
  BacktrackReward_Manager     ← MapSystem, EquipmentSystem, SaveSystem

Layer 2 (← Layer 0-1):
  BossSystem_Controller       ← BossSystem_PhaseManager, AICore(AIBrain)
  BossSystem_Arena            ← BossSystem_Controller, GateSystem(GateRegistry)
  ConfusionMagic_FactionSwitch ← ConfusionMagic_Accumulation, AICore(CharacterFlags)
  ConfusionMagic_AIOverride   ← ConfusionMagic_FactionSwitch, AICore(TargetSelector)
  ElementalGate_Interaction   ← GateSystem, DamageSystem(Element)
  BacktrackReward_Checker     ← BacktrackReward_Manager, EquipmentSystem(AbilityFlag)

Layer 3 (← Layer 0-2):
  SummonSystem_Manager        ← MagicSystem(MagicCaster), CompanionAI(FollowBehavior)
  SummonSystem_Controller     ← SummonSystem_Manager, AICore(AIBrain)
  BossSystem_AddSpawn         ← BossSystem_Controller, EnemySystem(EnemySpawner)
  BossSystem_Rewards          ← BossSystem_Controller, EnemySystem(DropTable)
  ElementalGate_Integration   ← ElementalGate_Interaction, GateSystem(GateRegistry)
  ConfusionMagic_Duration     ← ConfusionMagic_FactionSwitch
  ConfusionMagic_Limits       ← ConfusionMagic_FactionSwitch
  BacktrackReward_Reevaluation ← BacktrackReward_Checker
  BacktrackReward_MapIntegration ← BacktrackReward_Manager, MapSystem

Layer 4 (← Layer 0-3):
  SummonSystem_Lifetime       ← SummonSystem_Manager
  SummonSystem_MagicIntegration ← SummonSystem_Manager, MagicSystem
  SummonSystem_PartyLimit     ← SummonSystem_Manager
  ElementalGate_MultiHit      ← ElementalGate_Interaction
  ElementalGate_HintDisplay   ← ElementalGate_Integration, MapSystem
  BacktrackReward_Pickup      ← BacktrackReward_Checker
```

### 依存関係図（Section 3 追加分）
```
Section 1-2 基盤
    │
    ▼
AICore ──────────► BossSystem
DamageSystem ────────┘    │
                          ├──► BossArena ──► GateSystem(ClearGate)
                          └──► BossAddSpawn ──► EnemySystem(Spawner)

DamageSystem ──────► ConfusionMagic
(StatusEffect)           │
AICore ──────────────────┘ (CharacterFlags反転)

MagicSystem ─────► SummonSystem
CompanionAI ─────────┘ (FollowBehavior再利用)
AICore ──────────────┘ (AIBrain)

GateSystem ──────► ElementalGate
DamageSystem ────────┘ (Element属性検知)

MapSystem ───────► BacktrackReward
EquipmentSystem ─────┘ (AbilityFlag)
SaveSystem ──────────┘ (永続化)
```

### システム間通信（Section 3）

| 発信 | → | 受信 | 方式 | 内容 |
|------|---|------|------|------|
| BossController | → | BossArenaManager | メソッド呼び出し | アリーナロック/解除 |
| BossController | → | GameManager.Events | OnBossPhaseChanged | フェーズ遷移通知 |
| BossController | → | GameManager.Events | OnBossDefeated | 撃破通知→ClearGate開放 |
| BossArenaManager | → | GateRegistry | Open(clearGateId) | 永続ゲート開放 |
| StatusEffectManager | → | ConfusionEffectProcessor | ApplyConfusion | 蓄積閾値到達時 |
| ConfusionEffectProcessor | → | CharacterFlags | SetFaction/SetState | 陣営反転 |
| ConfusionEffectProcessor | → | GameManager.Events | OnEnemyConfused | 混乱開始通知 |
| MagicCaster | → | SummonManager | TrySummon | MagicType.Summon発動時 |
| SummonManager | → | GameManager.Events | OnSummonCreated/Dismissed | 召喚生成/解除 |
| ElementalGateInteractor | → | GateController | TryOpen | 属性条件達成時 |
| OnAbilityAcquired | → | BacktrackRewardManager | ReevaluateAll | 新能力獲得時 |
| BacktrackRewardManager | → | MapSystem | マーカー更新 | 報酬アクセス可能化 |

### asmdef 配置（Section 3）

```
Game.AI (既存拡張)
    ├── Boss/              BossController, BossPhaseManager, BossArenaManager
    ├── Summon/            SummonManager, SummonedCharacterController, SummonSpawner
    └── Confusion/         ConfusionEffectProcessor, ConfusionAIOverride

Game.World (既存拡張)
    ├── Gate/              (既存) + ElementalGateInteractor
    └── Backtrack/         BacktrackRewardManager, BacktrackRewardChecker, BacktrackRewardPickup

Game.Core (既存拡張)
    └── Common/            Section 3共通Enum/Struct、PartyManager（Section3Utilities.cs）

Game.Core/ScriptableObjects (既存拡張)
    ├── BossDefinition.cs
    ├── SummonDefinition.cs
    ├── ElementalGateDefinition.cs
    └── BacktrackRewardTable.cs
```

## Section 4: エンドコンテンツ

### 実装順序（レイヤー順）
```
Layer 0 (Section 1-3の上に構築):
  Common_Section4Types   ← Common_SharedTypes（共通Enum/Struct追加）

Layer 1 (← Layer 0):
  ChallengeMode_Runner   ← Common_Section4Types
  ChallengeMode_Score    ← Common_Section4Types
  AITemplates_Manager    ← Common_Section4Types, AIRuleBuilder

Layer 2 (← Layer 0-1):
  ChallengeMode_Manager  ← ChallengeMode_Runner, ChallengeMode_Score
  ChallengeMode_BossRush ← ChallengeMode_Runner, BossSystem
  ChallengeMode_Survival ← ChallengeMode_Runner, EnemySystem
  AITemplates_ApplyRevert ← AITemplates_Manager
  Leaderboard_RecordUpdate ← ChallengeMode_Score

Layer 3 (← Layer 0-2):
  Leaderboard_Statistics ← Leaderboard_RecordUpdate
  AITemplates_Suggester  ← AITemplates_Manager
```

### 依存関係図（Section 4 追加分）
```
Section 1-3 基盤
    │
    ▼
BossSystem ──────► ChallengeMode_BossRush ──┐
EnemySystem ─────► ChallengeMode_Survival ──┤
                                             ▼
Common_Section4Types ──► ChallengeMode_Runner ──► ChallengeMode_Manager
                    └──► ChallengeMode_Score ─────┘        │
                                  │                        ▼
                                  └──► Leaderboard_RecordUpdate ──► Leaderboard_Statistics

AIRuleBuilder ──► AITemplates_Manager ──► AITemplates_ApplyRevert
                              └──► AITemplates_Suggester
```

### asmdef 配置（Section 4）

```
Game.AI (既存拡張)
    └── Templates/         AITemplateManager, AITemplateApplier, AITemplateSuggester

Game.World (既存拡張)
    └── Challenge/         ChallengeRunner, ChallengeScoreCalculator, ChallengeManager,
                           BossRushLogic, SurvivalLogic, LeaderboardManager

Game.Core (既存拡張)
    └── Common/            Section 4共通Enum/Struct（Section4Structs.cs）
```
