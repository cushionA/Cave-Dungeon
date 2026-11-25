# System: AICore
Section: 2 — AI・仲間・連携

## 責務
AIBrainの基盤。3層判定アーキテクチャ（ターゲット切替→行動切替→デフォルト行動）、モードシステム、知覚（センサー）、行動選択のフレームワーク。敵AIと仲間AIの両方がこの基盤上で動作する。

## 依存
- 入力: DataContainer（CharacterVitals, CombatStats, CharacterFlags, DamageScoreEntry[]）、GameManager（イベントハブ）、MagicSystem（魔法実行）、ProjectileSystem（飛翔体発射）
- 出力: ActionSlot実行（移動/攻撃/魔法/ワープ等、全行動を統一構造で実行）

## アーキテクチャ準拠
- `Architect/07_AI判定システム再設計.md` の3層判定、AIConditionType(12種)、AIRule、AIMode に準拠
- `Architect/05_AIシステム.md` のセンサー、モード遷移に準拠

## コンポーネント構成
| コンポーネント | 責務 | MonoBehaviour? |
|--------------|------|---------------|
| AIBrain | 3層判定メインループ（Evaluate） | Yes |
| SensorSystem | 視野・聴覚・近接検知 → CharacterFlags更新 | Yes |
| ModeController | AIMode遷移管理 | No |
| ConditionEvaluator | AICondition配列のAND評価 | No |
| TargetSelector | AITargetSelect + TargetFilter でターゲット選択 | No |
| ActionExecutor | Dictionary\<ActionExecType, ActionBase\>で実行（switch文排除） | No |
| DamageScoreTracker | 累積ダメージスコアの加算・減衰管理 | No |
| DeliberationBuffer | 意図的な判断遅延（難易度調整用） | No |

---

## 統一行動データ: ActionSlot

**旧ActionData（ActState + byte param）は完全廃止。全キャラAI共通のActionSlotに統合。**
攻撃も魔法もワープも移動もガードも、全て同じ構造体で表現する。

```csharp
/// <summary>
/// AIの行動定義。全キャラクター（敵・味方・NPC）共通。
/// 実行パターン（ActionExecType） × パラメータの汎用構造。
/// ターゲットは第1層（targetRules）で確定済み。行動にターゲット上書きはない。
/// ※ 連携アクション（CoopAction）のみ、コンボ段ごとのターゲットをCoopActionManager側で独自管理する。
/// </summary>
[Serializable]
public struct ActionSlot
{
    /// <summary>実行パターン（5分類）</summary>
    public ActionExecType execType;

    /// <summary>
    /// 実行パターン固有の参照ID。
    /// - Attack: AttackMotionDataのインデックス（弱/強/スキル/コンボ段をこれで区別）
    /// - Cast: MagicDefinition ID（攻撃魔法も回復もバフもチャージ攻撃も飛翔体スキルも全て同じ）
    /// - Instant: InstantAction enum値（Dodge, WarpToTarget, WarpBehind, UseItem, InteractObject等）
    /// - Sustained: SustainedAction enum値（MoveToTarget, Follow, Retreat, Guard, Patrol等）
    /// - Broadcast: BroadcastAction enum値（DesignateTarget, Rally, Scatter等）
    /// </summary>
    public int paramId;

    /// <summary>
    /// 追加パラメータ。execType × paramId の組み合わせで意味が変わる:
    /// - Attack: （未使用）
    /// - Cast: （未使用、MagicDefinition側にcastTime等がある）
    /// - Instant: WarpBehind→オフセット距離、Dodge→回避方向等
    /// - Sustained: 持続時間（秒）。0 = 無期限（次の行動判定で切り替わるまで持続）
    ///   例: Guard paramValue=5.0 → 5秒ガード後に自動終了
    ///        Follow paramValue=0  → 別の行動ルールが成立するまで追従し続ける
    ///   ※ NPC AIカスタムではプレイヤーがこの値を自由に設定できる
    /// - Broadcast: 効果持続時間（秒）。DesignateTarget→指定維持時間等
    /// </summary>
    public float paramValue;

    /// <summary>エディタ表示用の名前</summary>
    public string displayName;
}
```

### ActionExecType — 実行パターン5分類

```csharp
/// <summary>
/// 行動の実行パターンを5種に分類。
/// ActionExecutorはDictionary<ActionExecType, ActionBase>でハンドラを引く。
/// </summary>
public enum ActionExecType : byte
{
    /// <summary>
    /// 近接攻撃系。AttackMotionData参照、ヒットボックス・motionValue・コンボ管理。
    /// paramIdでAttackMotionDataインデックスを引く。
    /// 弱攻撃/強攻撃/スキル攻撃/コンボ段はparamIdが違うだけ。
    /// </summary>
    Attack,

    /// <summary>
    /// 詠唱→発動→ProjectileSystem。paramIdでMagicDefinition IDを引く。
    /// 攻撃魔法、回復魔法、バフ、デバフ、召喚、チャージ攻撃、武器スキル飛翔体 — 全て同じ。
    /// </summary>
    Cast,

    /// <summary>
    /// アニメ1回再生して完了。ワープ、回避、アイテム使用、環境物利用等。
    /// paramIdでInstantAction enum値を引く。
    /// </summary>
    Instant,

    /// <summary>
    /// 開始→毎フレームTick→終了条件で終了。
    /// 移動、ガード、追従、挟撃維持、盾展開、隠密等。
    /// paramIdでSustainedAction enum値を引く。
    /// paramValueで持続時間を指定（0=無期限、次の行動判定で切り替わるまで持続）。
    /// NPCカスタムAIではプレイヤーが持続時間を自由に設定可能。
    /// オプションでreactionTrigger（AICondition[] + 反応ActionSlot）を持てる（カウンター系）。
    /// </summary>
    Sustained,

    /// <summary>
    /// 自分は何もしない。他キャラのAI状態を操作。
    /// ターゲット指示、集合、散開、挑発等。
    /// paramIdでBroadcastAction enum値を引く。
    /// </summary>
    Broadcast,
}
```

### paramId用サブ列挙 — Instant / Sustained / Broadcast

```csharp
/// <summary>Instant行動の種別。paramIdにキャストして使用。</summary>
public enum InstantAction : byte
{
    Dodge,              // 回避
    WarpToTarget,       // ターゲット位置にワープ
    WarpBehind,         // ターゲット背後にワープ
    UseItem,            // アイテム使用
    InteractObject,     // 環境物利用（ObjectNearby条件で判定）
    BodySlam,           // ノックバック体当たり
}

/// <summary>Sustained行動の種別。paramIdにキャストして使用。</summary>
public enum SustainedAction : byte
{
    MoveToTarget,       // ターゲットに接近
    Follow,             // 指定ターゲットに追従
    Retreat,            // ターゲットから距離を取る
    Flee,               // 逃走（ターゲットと逆方向に走り続ける。Retreatは距離を取って止まる、Fleeは逃げ続ける）
    Patrol,             // 巡回（paramValueでルートID）
    Guard,              // ガード構え
    Flank,              // 敵をターゲットと挟む位置を維持
    ShieldDeploy,       // 味方の前に盾を展開
    Decoy,              // 囮（DamageScore倍率を上げて引きつける）
    Cover,              // かばう（味方の前に立つ）
    Stealth,            // 隠密
    Orbit,              // ターゲット周辺を周回
}

/// <summary>Broadcast行動の種別。paramIdにキャストして使用。</summary>
public enum BroadcastAction : byte
{
    DesignateTarget,    // ターゲット指示
    Rally,              // 集合
    Scatter,            // 散開
    Taunt,              // 挑発（周囲の敵のターゲットを自分に向ける）
    FocusFire,          // 集中攻撃指示
    Disengage,          // 戦闘離脱指示
    ModeSync,           // モード同期（味方全員を同じモードに）
    DefendPoint,        // 地点を守る
    ReleaseOrder,       // 号令を解いて自律行動に戻す
}
```

### Sustained の reactionTrigger（カウンター系）

```csharp
/// <summary>
/// Sustained行動中に特定条件が成立したら反応行動を差し込む。
/// カウンター攻撃、カウンターワープ、カウンター回避等を実現。
/// </summary>
[Serializable]
public struct ReactionTrigger
{
    /// <summary>反応発動条件（AND評価）</summary>
    public AICondition[] conditions;

    /// <summary>条件成立時に実行する行動</summary>
    public ActionSlot reactionAction;
}
```

reactionTriggerの使用例:
- **カウンター攻撃**: Guard中（Sustained） + 被弾条件 → Attack（反撃）
- **カウンターワープ**: Guard中 + 被弾条件 → Instant(WarpBehind)
- **カウンター回避**: Guard中 + ProjectileNear条件 → Instant(Dodge)

---

## ActionBase 基底クラス

```csharp
/// <summary>
/// 全実行パターン共通の基底クラス。
/// ActionExecutorがDictionary<ActionExecType, ActionBase>で保持する。
/// </summary>
public abstract class ActionBase
{
    // === 共通フィールド（ActionSlotのparamId/paramValueとは別に、定義側で持つ） ===

    /// <summary>MP消費量</summary>
    public float mpCost;

    /// <summary>スタミナ消費量</summary>
    public float staminaCost;

    /// <summary>クールダウン時間（秒）</summary>
    public float cooldown;

    // === 共通メソッド ===

    /// <summary>行動完了時に発火するイベント</summary>
    public event Action OnCompleted;

    /// <summary>行動を開始する（OnExecuteをラップ）</summary>
    public void Execute(int selfHash, int targetHash, ActionSlot slot)
    {
        OnExecute(selfHash, targetHash, slot);
    }

    /// <summary>派生クラスで実装する実行処理</summary>
    protected abstract void OnExecute(int selfHash, int targetHash, ActionSlot slot);

    /// <summary>
    /// 行動を中断する。Sustained/Castで使用。
    /// Attack/Instant/Broadcastでは空実装。
    /// </summary>
    public virtual void Cancel() { }

    /// <summary>
    /// 毎フレーム更新。Sustainedで使用。
    /// 他のタイプでは空実装。
    /// </summary>
    public virtual void Tick(float deltaTime) { }

    /// <summary>行動を完了し、OnCompletedイベントを発火する</summary>
    protected void Complete()
    {
        OnCompleted?.Invoke();
    }
}
```

### 派生クラス概要

| クラス | ActionExecType | 主な処理 |
|--------|---------------|---------|
| AttackAction | Attack | AttackMotionData参照、ヒットボックス生成、motionValue適用、コンボ管理 |
| CastAction | Cast | 詠唱時間カウント→発動モーション→ProjectileSystem/MagicSystem呼び出し |
| InstantAction | Instant | アニメーション1回再生、即時効果適用（ワープ座標計算、アイテム効果等） |
| SustainedAction | Sustained | 開始処理→Tickで毎フレーム更新→終了条件判定。reactionTrigger評価含む |
| BroadcastAction | Broadcast | 対象キャラのAI状態を操作（ターゲット上書き、モード変更、集合点設定等） |

---

## AIMode（ActionSlot統合版）

```csharp
/// <summary>
/// AIの1つのモードを定義する。
/// 敵AIも仲間AIも同じ構造。行動はすべてActionSlotで統一。
/// </summary>
[Serializable]
public struct AIMode
{
    public string modeName;

    /// <summary>ターゲット切替ルール（優先度順）</summary>
    public AIRule[] targetRules;

    /// <summary>行動切替ルール（優先度順）</summary>
    public AIRule[] actionRules;

    /// <summary>ターゲット選択方法の配列（targetRulesから参照）</summary>
    public AITargetSelect[] targetSelects;

    /// <summary>行動データの配列（actionRulesから参照）</summary>
    public ActionSlot[] actions;

    /// <summary>行動ルールが全不一致の場合のデフォルト行動</summary>
    public int defaultActionIndex;

    /// <summary>判定間隔 x: ターゲット, y: 行動</summary>
    public Vector2 judgeInterval;
}
```

---

## AIBrain 3層判定ループ

```
Evaluate(nowTime):
  1. SensorSystem.Update() → CharacterFlags の認識ビット更新
  2. ModeController.CheckTransition() → モード遷移チェック
  3. DeliberationBuffer.Tick() → 遅延中なら保留

  4. 第1層: ターゲット切替判定
     currentMode.targetRules[] を優先度順に評価
     → ConditionEvaluator.EvaluateAll(rule.conditions)
     → マッチ: TargetSelector.Select(targetSelects[rule.actionIndex])
     → 不マッチ: 現ターゲット維持

  5. 第2層: 行動切替判定（ターゲット確定状態で）
     currentMode.actionRules[] を優先度順に評価
     → マッチ: actions[rule.actionIndex] を実行
     → 全不マッチ: actions[defaultActionIndex] を実行

  6. 第3層: 行動実行（常に何かする = 棒立ち防止）
     ActionExecutor.Execute(actionSlot, selfHash, targetHash)
```

## ActionExecutor（Dictionary方式、switch文排除）

```csharp
/// <summary>
/// ActionSlotを実際の行動に変換・実行する。
/// Dictionary<ActionExecType, ActionBase>でハンドラを引く。switch文は使わない。
/// 全キャラAI共通。敵もNPCも同じExecutorで動く。
/// Register()メソッドで外部からハンドラを登録する。
/// </summary>
public class ActionExecutor
{
    private readonly Dictionary<ActionExecType, ActionBase> _handlers;

    public ActionExecutor()
    {
        _handlers = new Dictionary<ActionExecType, ActionBase>();
    }

    /// <summary>ActionExecTypeに対応するハンドラを登録する</summary>
    public void Register(ActionExecType execType, ActionBase handler)
    {
        _handlers[execType] = handler;
    }

    public void Execute(ActionSlot slot, int selfHash, int targetHash)
    {
        // ターゲットは第1層で確定済み。行動によるターゲット上書きはない。
        ActionBase handler = _handlers[slot.execType];
        handler.Execute(selfHash, targetHash, slot);
    }

    /// <summary>現在のSustained行動を中断する</summary>
    public void CancelCurrent()
    {
        _handlers[ActionExecType.Sustained].Cancel();
        _handlers[ActionExecType.Cast].Cancel();
    }

    /// <summary>Sustained行動の毎フレーム更新</summary>
    public void Tick(float deltaTime)
    {
        _handlers[ActionExecType.Sustained].Tick(deltaTime);
    }
}
```

---

## AIの行動カタログ（処理パターン別）

### Attack（近接攻撃系）
全ての近接攻撃。弱/強/スキル/コンボ段はparamId（AttackMotionDataインデックス）で区別するだけ。

| paramId例 | 行動 |
|-----------|------|
| 0 | 弱攻撃1段目 |
| 1 | 弱攻撃2段目 |
| 2 | 強攻撃 |
| 3 | スキル攻撃A |
| 4 | ジャンプ攻撃 |

### Cast（詠唱→発動→飛翔体/効果）
paramIdでMagicDefinition IDを引く。攻撃魔法も回復もチャージ攻撃もスキル飛翔体も全て同じ。

| 用途 | paramId参照先 |
|------|-------------|
| 攻撃魔法（ファイアボール等） | MagicDefinition ID |
| 回復魔法（ヒール等） | MagicDefinition ID |
| バフ/デバフ | MagicDefinition ID |
| 召喚 | MagicDefinition ID |
| チャージ攻撃 | MagicDefinition ID |
| 武器スキル飛翔体 | MagicDefinition ID |

### Instant（アニメ1回→即完了）
paramIdでInstantAction enum値を引く。

| InstantAction | 説明 |
|---------------|------|
| Dodge | 回避 |
| WarpToTarget | ターゲット位置にワープ |
| WarpBehind | ターゲット背後にワープ |
| UseItem | アイテム使用 |
| InteractObject | 環境物利用（AIConditionType.ObjectNearbyで条件判定） |
| BodySlam | ノックバック体当たり |

### Sustained（開始→毎フレームTick→終了条件で終了）
paramIdでSustainedAction enum値を引く。

| SustainedAction | 説明 |
|-----------------|------|
| MoveToTarget | ターゲットに接近 |
| Follow | 指定ターゲットに追従 |
| Retreat | ターゲットから距離を取る |
| Flee | 逃走（ターゲットと逆方向に走り続ける。Retreatは距離を取って止まる、Fleeは逃げ続ける） |
| Patrol | 巡回（paramValueでルートID） |
| Guard | ガード構え |
| Flank | 敵をターゲットと挟む位置を維持 |
| ShieldDeploy | 味方の前に盾を展開 |
| Decoy | 囮（DamageScore倍率を上げて引きつける） |
| Cover | かばう（味方の前に立つ） |
| Stealth | 隠密 |
| Orbit | ターゲット周辺を周回 |

**Sustainedのオプション: reactionTrigger**
AICondition[] + ActionSlot でカウンター系を実現。Sustained行動中に条件が成立したら反応行動を差し込む。

| カウンター例 | Sustained行動 | reactionTrigger条件 | 反応ActionSlot |
|-------------|--------------|-------------------|---------------|
| カウンター攻撃 | Guard | 被弾（SelfActState == HitStun等） | Attack(paramId=反撃モーション) |
| カウンターワープ | Guard | 被弾 | Instant(WarpBehind) |
| カウンター回避 | Guard | ProjectileNear | Instant(Dodge) |

### Broadcast（自分は何もしない、他キャラのAI状態を操作）
paramIdでBroadcastAction enum値を引く。

| BroadcastAction | 説明 |
|-----------------|------|
| DesignateTarget | 担当や交戦相手がまだいない味方に標的を知らせる |
| Rally | 集合（味方に集合点を指定） |
| Scatter | 散開（味方を散らす） |
| Taunt | 挑発（周囲の敵のターゲットを自分に向ける） |
| FocusFire | 交戦中の味方にも、指示した一体を優先するよう求める |
| Disengage | 戦闘離脱指示 |
| ModeSync | モード同期（味方全員を同じモードに切替） |
| DefendPoint | 指定地点を守る |
| ReleaseOrder | 現在の任務を終了し、元の個体設定を再開する |

号令のIDはデータに保存される。既存の0～6を維持し、地点防衛と指示解除を末尾に追加した。各定義の使用者マスクをプレイヤー用の装備候補と実行時の検査で共用する。一回の発令には、実際の発令者に対応する一つのビットと、世代付きの個体参照を渡す。使用者の区分と受け手の陣営は別々に検査する。敵専用の挑発なら敵指揮官が発行し、効果を受けるのは対立陣営のAIである。AIの作者は `ActType.FieldOrder` の行動をモードに載せ、`ActData.fieldOrder` に号令を指定する。AIが地点防衛を出したときは、判断時に観測した発令者の位置が持ち場になる。2D試作では集合・標的指定・集中攻撃・地点防衛・解除・挑発を受信できる。散開・戦闘離脱・モード同期には将来用のIDと発令権限を割り当てている。[号令と権限](../../Assets/MyAsset/Core/AI/Core/FieldOrder.cs)・[2D戦場での配送](../../Assets/MyAsset/Runtime/AI/RuntimeFieldOrders.cs)

---

## 使用例

### 雑魚敵（ActionSlot統合版）

```
AIMode "通常":
  targetRules:
    [0] DamageScore > 100 → DamageScore降順
    [1] Count(敵) >= 1    → Distance昇順

  actionRules:
    [0] Distance < 3      → actions[0] = ActionSlot(Attack, paramId=0)           ← 弱攻撃
    [1] Distance 3-8      → actions[1] = ActionSlot(Cast, paramId=ファイアボールID) ← 攻撃魔法
    [2] SelfHpRatio < 20  → actions[2] = ActionSlot(Sustained, paramId=Flee)      ← 逃走

  defaultActionIndex: 3  → actions[3] = ActionSlot(Sustained, paramId=Patrol, paramValue=ルート0)
```

### ボス フェーズ切替

```
AIMode[0] "フェーズ1":
  actionRules:
    [0] Distance < 4   → ActionSlot(Attack, paramId=大振りモーション)
    [1] Distance 4-10  → ActionSlot(Cast, paramId=衝撃波ID)
    [2] SelfHpRatio<50 → ActionSlot(Sustained, paramId=Guard)
                          reactionTrigger: [被弾] → ActionSlot(Attack, paramId=カウンター)
  modeTransition: SelfHpRatio < 50 → mode[1]

AIMode[1] "フェーズ2":
  actionRules:
    [0] SelfHpRatio < 30 → ActionSlot(Cast, paramId=回復魔法ID, target=Self)
    [1] Distance < 5     → ActionSlot(Cast, paramId=範囲攻撃ID)
    [2] Distance 5-12    → ActionSlot(Cast, paramId=連続弾ID)
  default: ActionSlot(Sustained, paramId=Retreat, paramValue=8.0)
```

### シスターの仲間AI（プレイヤーカスタム）

```
AIMode[0] "攻撃特化":
  targetRules:
    [0] DamageScore > 100 → DamageScore降順
    [1] Count(敵) >= 1    → Distance昇順
  actionRules:
    [0] Distance < 3      → ActionSlot(Attack, paramId=0)
    [1] Distance 3-8      → ActionSlot(Cast, paramId=ファイアボールID)
  default: ActionSlot(Sustained, paramId=Follow)   ← プレイヤー追従

AIMode[1] "回復支援":
  targetRules:
    [0] HpRatio < 30, 味方 → HpRatio昇順(味方)
  actionRules:
    [0] Distance < 5, SelfMpRatio > 20 → ActionSlot(Cast, paramId=ヒールID)
  default: ActionSlot(Sustained, paramId=Follow)

modeTransitionRules:
  [0] 味方HP30%以下 → mode[1]
  default: mode[0]
```

### カウンター系ボス（reactionTrigger活用）

```
AIMode "カウンター特化":
  actionRules:
    [0] Distance < 5 → ActionSlot(Sustained, paramId=Guard)
                        reactionTrigger:
                          [0] SelfActState == HitStun → ActionSlot(Instant, paramId=WarpBehind)
                          [1] ProjectileNear          → ActionSlot(Instant, paramId=Dodge)
    [1] Distance 5-10 → ActionSlot(Cast, paramId=挑発弾ID)
  default: ActionSlot(Sustained, paramId=MoveToTarget)
```

---

## 敵AIと仲間AIの統一

敵AIも仲間AIも同じActionSlot/AIMode/ActionExecutorで動作する。
違いは「誰が設定するか」だけ。

```
敵AI: AIInfo（ScriptableObject）にAIMode配列を定義
  → エディタで設定するだけ。プレイヤーは触れない

仲間AI: CompanionAIConfig にAIMode配列を定義
  → プレイヤーがAIRuleBuilderで編集可能
  → + modeTransitionRules（自動切替条件）
  → + shortcutBindings（手動切替）
```

両者ともAIModeの構造は完全に同じ。ActionSlotの中身もActionExecutorも共通。

---

## 条件評価システム（ConditionEvaluator）
```csharp
bool EvaluateAll(AICondition[] conditions, int selfHash, int targetHash)
{
    foreach (var cond in conditions)
    {
        float value = GetConditionValue(cond, selfHash, targetHash);
        if (!Compare(value, cond.op, cond.operandA, cond.operandB))
            return false;
    }
    return true;
}
```

AIConditionType(12種): None, Count, HpRatio, MpRatio, StaminaRatio, ArmorRatio, Distance, NearbyFaction, ProjectileNear, ObjectNearby, DamageScore, EventFired, SelfActState

CompareOp(9種): Less, LessEqual, Equal, GreaterEqual, Greater, NotEqual, InRange, HasFlag, HasAny

**シーン装置の利用**

1. シーンの装置に `AISceneGimmickController` を付け、認識ビット、操作点とその半径、効果の中心とその半径を設定する。たとえば落下物なら、操作点はレバー、効果の中心は落下先に置く。操作を遮るレイヤーの初期値は Ground。壁付けのレバーは操作点をプレイヤー側の面から少し離すと、壁自体で操作が遮られるのを防げる。
2. `On Activated` に扉を開く、落下物を解放するなどのシーン側の処理を割り当てる。イベントは起動時に一度発火する。演出や物理効果が遅れる場合は、その秒数を `Effect Delay` に設定する。再有効化すると新しい使用世代になる。
3. 敵・仲間の `AIInfo` で、使わせたいモードの行動に `ActType.InteractObject` を追加する。ルールからは `InstantAction.InteractObject` を使う。`ObjectNearby` は観測者が操作圏内にいて操作点まで遮られていない未使用装置の種類ビットを返す。共通観測に装置の照会元を接続していない場合は未取得となる。
4. AIは同じ2D物理ワールドにある装置から、そのtickの戦闘標的に効果が届くものを選ぶ。遅延がある場合は現在速度から標的の到達位置を推定し、自分・味方が効果範囲を横切る候補を除く。どれも使えない場合に備え、ガードや接近などの行動を同じモードに設定できる。実行直前にも位置、速度、遮蔽物、行動制限、標的・装置の世代を確かめる。

## ターゲット選択（TargetSelector）
1. TargetFilter で候補を絞り込み（CharacterFlags のビット演算で高速フィルタ）
2. TargetSortKey で最適な1体を選択（Distance, HpRatio, DamageScore等11種）
3. isDescending で昇順/降順を切替

### 標的とスタンを使うルール

`GameManager` の固定tickで、状態効果を進めた後にキャラクターを観測する。`AITargetRelations` が持つ「誰が誰を狙うか」を世代付きでコピーし、同じ観測から被ターゲット数を読める。スタンの残り秒数は `StatusEffectStore` から取得する。観測を作成した後の標的変更と状態効果の解除は、次回の観測に現れる。

- プレイヤーを護衛する仲間は `targetsProtectedPlayer` を指定すると、現在のプレイヤーを狙う敵を候補にできる。別の味方を狙っている敵は、距離や被ターゲット数が同じでも区別できる。
- `stunMustLastBeyondSeconds` に正の秒数を設定すると、その時間を**超えて**スタンが残る候補を選ぶ。0で無効。1秒と設定した場合、残り1秒ちょうどの敵は条件から外れる。ここで指定するのは作者の固定値で、移動や攻撃の発生を含む到達予測は別途必要になる。
- 2つの条件を有効にすると、両方を満たす同じ候補を探す。優先候補がない場合に通常の選定へ進めたいルールは `continueIfEmpty` を使う。`AIInfo` のモードでは、優先条件と従来の距離選択をこの順で組み立てる。仲間AI設定画面からも設定できる。

同期評価とBurstの評価は同じ選定処理を使う。実行側は選択した個体の世代と判定版を照合する。スケジュール後に別の処理が実行者の標的を変更した場合は、その新しい命令を優先して古い判断を破棄する。装置の操作時は、移動した味方・遮蔽物などの物理状態も再確認する。

`AIInfo` の行動トリガー `CoolTimeReady` と `TargetStatusEffect` は変換に未対応。これらを指定した行動はスロットに残り、自動選択と既定行動の候補から外れる。ほかに使える行動がないモードでは、行動を開始しない。

### 2Dの攻撃に射線条件を付ける

2Dの `AIInfo` で行動の `Require Line Of Sight` を有効にすると、選んだ標的までの射線が通る場合にだけ、その行動を候補にできる。観測を確定するときに同じ物理シーンの相手を照会し、観測者が使用する Ground レイヤーで遮蔽を判定する。同期とBurstは同じ世代付き観測を読み、遮蔽されている場合や観測が取得できない場合は、待機など次に使える行動を選ぶ。[AI設定の変換](../../Assets/MyAsset/Core/AI/Core/AIInfoConverter.cs)・[射線の観測](../../Assets/MyAsset/Runtime/GameManager.cs)

### 3D集団戦で障害を迂回する

`EncounterCombat3D` の `Approach Waypoints` に、同じ部屋の床をつなぐ中継点を最大12個設定する。`Approach Ground Layers` には通行を支える床を、`Obstruction Layers` には通行や射線を遮る壁を指定する。通常の接近や集中攻撃では、同じ陣営が目撃した標的へ向けて、歩いて到達できる射程内の地点を選ぶ。中継点と終点の間は個体の幅と床を照会し、通行可能な候補のうち短い経路を使う。攻撃は個体自身の射線が通ってから始める。標的の変化や通路の閉塞があれば次の経路を選び直す。橋の上り下りやジャンプを含む特殊移動は別の経路が必要になる。[遭遇の移動](../../Assets/MyAsset/Runtime/AI/EncounterCombat3D.cs)・[接近経路](../../Assets/MyAsset/Runtime/AI/EncounterApproachRoutes3D.cs)

## DamageScoreTracker
- DamageScoreEntry[4-8] per character（SoAコンテナ管理）
- ダメージ受領時: score += actualDamage × 倍率（ヘイト上昇状態なら1.2倍等）
- 毎判定時: score *= decayRate^elapsed（時間減衰）

## DeliberationBuffer（難易度調整）
```
Easy:   12フレーム遅延 + 0-6ランダム → 0.3-0.6秒
Normal: 6フレーム + 0-4ランダム → 0.1-0.33秒
Hard:   2フレーム + 0-2ランダム → 0.03-0.13秒
```

---

## インタフェース
- `GameManager.Events.OnDamageDealt` → DamageScoreTracker がスコア加算
- `GameManager.Events.OnCharacterDeath` → ターゲット消失時のフォールバック
- AIInfo（ScriptableObject）: AIMode[]（ActionSlot[]含む）、モード遷移設定、センサー設定

## 機能分解
| 機能名 | 説明 | テスト種別 | 優先度 |
|--------|------|-----------|--------|
| AICore_ActionBase | ActionBase基底（Execute/Cancel/Tick/Complete）と5派生クラスの実行 | EditMode | High |
| AICore_ActionExecutor | Dictionary\<ActionExecType, ActionBase\>によるswitch文排除のディスパッチ | EditMode | High |
| AICore_AttackExecution | Attack実行パターン（AttackMotionData参照、ヒットボックス、コンボ管理） | EditMode | High |
| AICore_CastExecution | Cast実行パターン（詠唱→発動→ProjectileSystem、Interrupt対応） | EditMode | High |
| AICore_InstantExecution | Instant実行パターン（ワープ、回避、アイテム使用、環境物利用） | EditMode | High |
| AICore_SustainedExecution | Sustained実行パターン（開始→Tick→終了条件、reactionTrigger評価） | EditMode | High |
| AICore_BroadcastExecution | Broadcast実行パターン（他キャラAI状態操作） | EditMode | Medium |
| AICore_ConditionEvaluator | AICondition配列のAND評価、CompareOp全演算子 | EditMode | High |
| AICore_TargetSelection | TargetFilter + TargetSortKey によるターゲット選択 | EditMode | High |
| AICore_ThreeLayerJudgment | 3層判定ループ（ターゲット切替→行動切替→デフォルト行動） | EditMode | High |
| AICore_ModeTransition | AIMode間の遷移条件評価・切替 | EditMode | High |
| AICore_DamageScore | 累積ダメージスコアの加算・減衰・最大値取得 | EditMode | Medium |
| AICore_Sensor | 視野角・距離検知、CharacterFlags更新 | PlayMode | Medium |
| AICore_DeliberationBuffer | 難易度別の判断遅延 | EditMode | Medium |
| AICore_CooldownManagement | 行動別・グローバルクールタイム管理 | EditMode | Medium |

## 設計メモ
- **ActionSlotで全行動を統一**。旧ActionData（ActState+param）は完全廃止
- **ActionExecType 5分類**で実行パターンを明確に分離（Attack/Cast/Instant/Sustained/Broadcast）
- **ActionBase基底クラス**で共通フィールド（mpCost, staminaCost, cooldown）と共通メソッド（Execute→OnExecute, Cancel, Tick, Complete+OnCompleted）を定義
- **ActionExecutorはDictionary方式**でswitch文を排除。新しい実行パターン追加時もDictionaryに1行追加するだけ
- 敵AIも仲間AIも同じActionSlot/AIMode/ActionExecutorで動作。違いは「誰が設定するか」だけ
- 攻撃も魔法もワープも移動もガードも同じActionSlot構造体で表現 → AIエディタが一つで済む
- Sustained行動のreactionTriggerでカウンター系を統一的に表現（カウンター攻撃、カウンターワープ、カウンター回避）
- 環境物利用はAIConditionType.ObjectNearbyで条件判定し、Instant(InteractObject)で実行
- プレイヤーがActionSlotのルールを組める仲間はシスター。召喚魔物は作者が設定した種の行動で戦い、指揮官を経由して騎士の簡易指揮を受ける。
- 07_AI判定システム再設計のActionDataをActionSlotに更新する必要あり
