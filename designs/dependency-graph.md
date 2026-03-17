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

### 追加システム
```
Layer 0 (Section 1の上に構築):
  AICore              ← DataContainer, GameManager

Layer 1:
  CompanionAI_Basic   ← AICore, PlayerMovement, InputSystem
  EnemySystem         ← AICore, DamageSystem

Layer 2:
  AIRuleBuilder       ← CompanionAI_Basic
  CoopAction          ← CompanionAI_Basic

Layer 3:
  GateSystem          ← MapSystem, CoopAction
  CooldownReward      ← AIRuleBuilder, CoopAction
```

### 追加通信
| 発信 | → | 受信 | 方式 | 内容 |
|------|---|------|------|------|
| EnemySystem | → | CurrencySystem | C# event (OnCurrencyChanged) | 通貨ドロップ |
| EnemySystem | → | LevelUpSystem | C# event (OnExpGained) | 経験値配布 |
| EnemySystem | → | InventorySystem | C# event (OnItemAcquired) | アイテムドロップ |
| InputSystem | → | CompanionAI_Basic | cooperationPressed | 連携ボタン |
| SaveSystem | → | EnemySystem | 休息通知 | 敵リスポーン |

## Section 3-4

（未設計）
