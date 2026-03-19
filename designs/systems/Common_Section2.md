# Common: Section 2 — AI・仲間・連携

## 概要
Section 2で追加される8システム（AICore, CompanionAI_Basic, EnemySystem, MagicSystem, CoopAction, AIRuleBuilder, GateSystem, CooldownReward）にまたがる共通要素。

## Section 1からの依存

Section 2は以下のSection 1システムを基盤として使用する:
- **DataContainer**: SoAコンテナ（CharacterVitals, CombatStats, CharacterFlags, DamageScoreEntry[]）
- **GameManager**: イベントハブ（OnCharacterDeath, OnDamageDealt等）
- **InputSystem**: cooperationPressed（連携ボタン入力）
- **PlayerMovement**: プレイヤー位置・状態（追従対象）
- **DamageSystem**: ダメージ計算結果 → DamageScoreTracker、ProjectileSystemのヒット処理
- **WeaponSystem**: スキル・チャージ攻撃の飛翔体 → ProjectileSystem
- **EquipmentSystem**: 仲間の装備情報（利用可能な行動リスト）
- **MapSystem**: エリア情報（ゲート配置）
- **SaveSystem**: ゲート状態・AIプリセットの永続化
- **InventorySystem**: アイテムドロップ受信
- **CurrencySystem**: 通貨ドロップ受信
- **LevelUpSystem**: 経験値受信

---

## 共通ユーティリティクラス（Game.Core配置）

Section 1-2を横断して使用する共通処理。Game.Coreに配置し全システムから参照可能。

### ComboWindowTimer — コンボ入力受付

武器コンボ（WeaponSystem）、連携コンボ（CoopAction）、将来の魔法コンボ等で共用。

```csharp
/// <summary>
/// コンボ入力受付の共通タイマー。
/// Open()で受付開始 → IsOpen中にTryAdvance()でコンボ継続。
/// </summary>
public class ComboWindowTimer
{
    private float _windowRemaining;
    private int _currentStep;
    private int _maxSteps;

    public bool IsOpen => _windowRemaining > 0f;
    public int CurrentStep => _currentStep;
    public bool IsMaxReached => _currentStep + 1 >= _maxSteps;

    public void Open(float windowDuration, int maxSteps)
    {
        _windowRemaining = windowDuration;
        _maxSteps = maxSteps;
    }

    public bool TryAdvance()
    {
        if (!IsOpen || IsMaxReached) return false;
        _currentStep++;
        return true;
    }

    public void Tick(float dt)
    {
        if (_windowRemaining > 0f) _windowRemaining -= dt;
    }

    public void Reset()
    {
        _windowRemaining = 0f;
        _currentStep = 0;
    }
}
```

| 使用箇所 | windowDuration | maxSteps |
|---------|---------------|----------|
| WeaponSystem（武器コンボ） | AttackMotionData.inputWindow | コンボルートの段数 |
| CoopAction（連携コンボ） | CoopActionBase.comboInputWindow | CoopActionBase.maxComboCount |

### CooldownTracker — 汎用クールタイム管理

行動別クールタイム（ActionExecutor）、連携スキルクールタイム（CoopAction）、魔法クールタイム（MagicCaster）で共用。

```csharp
/// <summary>
/// 汎用クールタイムトラッカー。
/// int型キーごとにクールタイム残り時間を管理する。
/// </summary>
public class CooldownTracker
{
    private Dictionary<int, float> _cooldownEndTimes;

    public CooldownTracker()
    {
        _cooldownEndTimes = new Dictionary<int, float>();
    }

    public bool IsReady(int key, float currentTime)
        => !_cooldownEndTimes.ContainsKey(key)
        || _cooldownEndTimes[key] <= currentTime;

    public void Start(int key, float duration, float currentTime)
        => _cooldownEndTimes[key] = currentTime + duration;

    public float GetRemaining(int key, float currentTime)
    {
        if (!_cooldownEndTimes.TryGetValue(key, out float next)) return 0f;
        return Math.Max(0f, next - currentTime);
    }
}
```

| 使用箇所 | キーの意味 |
|---------|-----------|
| ActionExecutor | ActionSlotのparamId（行動ごと） |
| CoopActionManager | CoopActionBase固有ID |
| MagicCaster | MagicDefinition ID |

### ActionInterruptHandler — 行動割り込み・再開

連携割り込み（CompanionController）、詠唱中断（MagicCaster）、Sustained中断で共用。

```csharp
/// <summary>
/// 行動の中断・再開を管理する。
/// 中断前の行動状態を保存し、割り込み終了後に復帰する。
/// </summary>
public class ActionInterruptHandler
{
    private ActionSlot? _savedAction;
    private int _savedTargetHash;

    public bool HasSavedState => _savedAction.HasValue;

    public void Save(ActionSlot current, int targetHash)
    {
        _savedAction = current;
        _savedTargetHash = targetHash;
    }

    public (ActionSlot action, int targetHash)? Restore()
    {
        if (!_savedAction.HasValue) return null;
        var result = (_savedAction.Value, _savedTargetHash);
        _savedAction = null;
        return result;
    }

    public void Clear() => _savedAction = null;
}
```

| 使用箇所 | 割り込み元 | 再開タイミング |
|---------|-----------|-------------|
| CompanionController | 連携ボタン（CoopAction） | 連携コンボ終了後 |
| MagicCaster | 怯み（被ダメージ） | 怯み解除後（再開せず次の判定へ） |
| SustainedAction | 3層判定の再評価 | 新しい行動が選ばれた時 |

---

## 共通Enum（Section 2追加分）

```csharp
// GateSystemで使用
public enum GateType : byte
{
    Clear,      // エリアクリア必要
    Ability,    // 特定能力必要
    Key,        // 特定アイテム必要
}

// AICore: ActionSlot関連（07_AI判定システム再設計で定義）
// ActionExecType, InstantAction, SustainedAction, BroadcastAction
// AIConditionType, CompareOp, TargetSortKey
```

## 共通構造体（Section 2追加分）

```csharp
// AICore: 07_AI判定システム再設計で定義
// ActionSlot, AICondition, AIRule, AITargetSelect, AIMode, DamageScoreEntry, TargetFilter, ReactionTrigger
```

## 共通ScriptableObject

| ScriptableObject | システム | 用途 |
|-----------------|---------|------|
| AIInfo | AICore | AIMode配列（ActionSlot[]含む）、センサー設定、モード遷移条件 |
| CompanionBehaviorSetting | CompanionAI | 追従距離、スタンス倍率テーブル |
| CompanionAIPreset | AIRuleBuilder | AIカスタムプリセット（システム/カスタム） |
| DropTable | EnemySystem | 敵撃破時のドロップ定義 |
| MagicDefinition | MagicSystem | 魔法データ（詠唱時間、BulletProfile、MP消費） |
| GateDefinition | GateSystem | ゲート開放条件定義 |

## 共通イベント（Section 2追加分）

| イベント | 発信元 | 受信先 | 内容 |
|---------|--------|--------|------|
| OnEnemyDefeated | EnemySystem | CurrencySystem, LevelUpSystem, InventorySystem | 撃破リワード配布 |
| OnCompanionStanceChanged | UISystem | CompanionAI_Basic | スタンス切替 |
| OnCustomRulesChanged | AIRuleBuilder | CompanionAI_Basic | ルール更新通知 |
| OnCoopActivated | CoopAction | UISystem | 連携発動通知 |
| OnGateOpened | GateSystem | MapSystem, SaveSystem | ゲート開放通知 |
| OnCooldownReady | CoopAction | UISystem | クールタイム完了通知 |
| OnFreeCoopActivated | CoopAction | UISystem | MP無料連携発動通知 |
| OnMagicCast | MagicCaster | UISystem | 魔法詠唱開始/完了通知 |
| OnActionTypeUnlocked | ActionUnlockRegistry | AIRuleBuilder UI | 新行動タイプ解放通知 |

## asmdef 構成（Section 2）

Section 1で空のGame.AI asmdefが用意済み。Section 2で以下を配置:

```
Game.Core (Assets/MyAsset/Core/) — 共通ユーティリティ追加
    └── Common/
        ├── ComboWindowTimer.cs      ← NEW
        ├── CooldownTracker.cs       ← NEW
        ├── ActionInterruptHandler.cs ← NEW
        ├── GateType enum            ← NEW
        └── （既存: Enums.cs, Structs.cs, Interfaces.cs）

Game.AI (Assets/MyAsset/AI/)
    ├── Core/           AIBrain, ConditionEvaluator, TargetSelector, ActionExecutor, DamageScoreTracker
    ├── Actions/        ActionBase, AttackAction, CastAction, InstantAction, SustainedAction, BroadcastAction
    ├── Companion/      CompanionController, FollowBehavior, StanceManager
    ├── Enemy/          EnemyController, EnemySpawner, DropTable, LootDropper
    ├── RuleBuilder/    RuleEditorLogic, ActionUnlockRegistry, PresetManager
    ├── Coop/           CoopActionManager, CoopActionBase, CoopComboState（+ 継承先）
    ├── Cooldown/       CooldownRewardFeedback
    └── Data/           AIInfo, CompanionBehaviorSetting, CompanionAIPreset（ScriptableObject）

Game.Combat (Assets/MyAsset/Combat/) — ProjectileSystem追加
    ├── Projectile/     ProjectileController, Projectile, ProjectileMoveJob, ProjectilePool
    ├── Magic/          MagicCaster, MagicDefinition
    └── （既存: Weapon, Damage, Guard, Equipment）

Game.World (Assets/MyAsset/World/) — 既存に追加
    └── Gate/           GateController, GateRegistry, GateConditionChecker
```

参照関係:
```
Game.Core ← Game.Character ← Game.Combat / Game.AI / Game.World / Game.Economy ← Game.UI
  Game.Combat: ProjectileSystem, MagicSystem（Section 2追加）
  Game.AI: AICore, CompanionAI, EnemySystem, CoopAction, AIRuleBuilder, CooldownReward（Section 2追加）
  共通ユーティリティ（ComboWindowTimer等）はGame.Coreにあるため全asmdefから参照可能
```

## Section 1 との統合ポイント

### 共通ユーティリティの遡及適用
ComboWindowTimer, CooldownTrackerはSection 1のWeaponSystem（コンボ入力受付）にも遡及適用する。
Section 2実装時にWeaponSystemのコンボ管理コードをComboWindowTimerに置き換えるリファクタリングを含む。

### 連携ボタン（cooperationPressed）
Section 1の InputSystem.MovementInfo に `cooperationPressed` は定義済み。
Section 2で CoopActionManager がこの入力を受け取って処理する。
プレイヤーの現在行動は中断しない（死亡中のみ無効）。

### GameManager.Events 拡張
Section 1で定義済みの OnCharacterDeath, OnDamageDealt に加えて、
Section 2で OnEnemyDefeated, OnCoopActivated, OnMagicCast 等を追加。

### ISaveable 実装
GateRegistry, CompanionAIPreset, ActionUnlockRegistry はISaveable実装でSaveSystemと連携。
