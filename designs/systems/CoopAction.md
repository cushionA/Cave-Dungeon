# System: CoopAction
Section: 2 — AI・仲間・連携

## 責務
プレイヤーが連携ボタンで発動する「仲間への指示スキル」システム。コンボ対応、ターゲット条件、行動割り込み、クールタイム報酬を統括する。

## 依存
- 入力: CompanionAI_Basic（仲間の状態・行動中断/再開）、InputSystem（連携ボタン）、AICore（TargetSelector・ConditionEvaluator）
- 出力: 仲間の連携行動実行、ターゲット選択結果

## コンポーネント構成
| コンポーネント | 責務 | MonoBehaviour? |
|--------------|------|---------------|
| CoopActionManager | 連携全体の管理（発動判定、コンボ管理） | No |
| CoopActionBase | 連携アクション基底クラス（効果をオーバーライド） | No (abstract) |
| CoopActionSlot | 装備中の連携アクション管理（メニューでセット） | No |
| CoopCooldownTracker | アクション別クールタイム管理 | No |
| CoopComboState | コンボ段数・入力猶予の状態管理 | No |

---

## 連携アクション基底クラス

```csharp
/// <summary>
/// 連携アクションの基底クラス。
/// 各連携は継承してExecuteCombo()をオーバーライドし、
/// 個別の設定項目をサブクラスで定義する。
/// </summary>
public abstract class CoopActionBase
{
    // === 共通設定（全連携共通） ===
    public string actionName;          // 表示名
    public int mpCost;                 // MP消費量（初回のみ）
    public float cooldownDuration;     // クールタイム（秒）
    public int maxComboCount;          // 最大コンボ回数
    public float comboInputWindow;     // コンボ入力猶予（秒）

    // === コンボ段ごとのターゲット条件 ===
    public CoopComboStep[] comboSteps; // 各段のターゲット設定

    /// <summary>
    /// 連携効果の実行。サブクラスでオーバーライドする。
    /// </summary>
    /// <param name="comboIndex">現在のコンボ段（0始まり）</param>
    /// <param name="companion">仲間の参照</param>
    /// <param name="targetHash">ターゲットのハッシュ</param>
    public abstract void ExecuteCombo(int comboIndex, int companionHash, int targetHash);

    /// <summary>
    /// 連携終了時のクリーンアップ。サブクラスで必要に応じてオーバーライド。
    /// </summary>
    public virtual void OnComboEnd(int companionHash) { }
}

/// <summary>
/// コンボ1段分のターゲット設定。
/// </summary>
[Serializable]
public struct CoopComboStep
{
    /// <summary>この段のターゲット選択方法</summary>
    public AITargetSelect targetSelect;

    /// <summary>ターゲットが見つからない場合にコンボを中断するか</summary>
    public bool abortIfNoTarget;
}
```

---

## 連携アクションの具体例

### ワープ連携
```csharp
public class WarpCoopAction : CoopActionBase
{
    // === 個別設定 ===
    public bool warpBehindTarget;      // ターゲットの背後にワープするか
    public float warpOffset;           // ワープ先のオフセット距離

    public override void ExecuteCombo(int comboIndex, int companionHash, int targetHash)
    {
        ref CharacterVitals targetVitals = ref GameManager.Data.GetVitals(targetHash);
        ref CharacterVitals selfVitals = ref GameManager.Data.GetVitals(companionHash);

        float2 targetPos = targetVitals.position;
        float2 warpPos;

        if (warpBehindTarget)
        {
            // ターゲットの背後（ターゲットの向きの反対側）
            float facing = GetFacingDirection(targetHash);
            warpPos = targetPos + new float2(-facing * warpOffset, 0);
        }
        else
        {
            // ターゲットのそば
            warpPos = targetPos + new float2(warpOffset, 0);
        }

        // ワープ実行
        selfVitals.position = warpPos;
        // ワープエフェクト再生
    }
}
```

設定例:
```
ワープ連携:
  maxComboCount: 3
  comboInputWindow: 0.8秒
  comboSteps:
    [0] targetSelect: Distance昇順(敵) → 最寄りの敵の背後へ
    [1] targetSelect: HpRatio昇順(敵) → HP最低の敵の背後へ
    [2] targetSelect: Distance昇順(味方) → 最寄りの味方のそばへ
  warpBehindTarget: true（0,1段目）/ false（2段目）
  warpOffset: 1.5
```

### 盾連携
```csharp
public class ShieldCoopAction : CoopActionBase
{
    // === 個別設定 ===
    public float shieldDuration;       // 盾の持続時間
    public bool reflectsProjectiles;   // 飛び道具反射するか
    public bool isPlatform;            // 足場として機能するか
    public float shieldOffset;         // ターゲットからのオフセット

    public override void ExecuteCombo(int comboIndex, int companionHash, int targetHash)
    {
        ref CharacterVitals targetVitals = ref GameManager.Data.GetVitals(targetHash);
        float2 spawnPos = targetVitals.position + new float2(shieldOffset, 0);

        // 盾オブジェクト生成
        SpawnShield(spawnPos, shieldDuration, reflectsProjectiles, isPlatform);
    }

    public override void OnComboEnd(int companionHash)
    {
        // 古い盾の破棄等
    }
}
```

---

## 発動フロー

```
連携ボタン押下
    ↓
0. プレイヤー死亡中? → Yes: 無効（入力自体を無視）
   ※ それ以外（スタン・怯み・チャージ中・攻撃中等）→ 受付OK
   ※ プレイヤーの現在行動は一切中断しない（並行発動）
    ↓
1. 仲間が怯み中か? → Yes: 発動不可
    ↓ No
2. コンボ中か?
    ├── Yes: 次のコンボ段へ（MP不要）
    └── No: 新規発動
             ├── クールタイム消化済み? → MP無料
             └── 未消化? → MP消費チェック
                  ├── MP足りる → MP消費して発動（クールタイムタイマーは変えない）
                  └── MP不足 → 発動不可
    ↓
3. 仲間の現在行動を中断（中断前の状態を保存）
    ↓
4. CoopComboStep[comboIndex].targetSelect でターゲット選択
    ↓
5. action.ExecuteCombo(comboIndex, companionHash, targetHash)
    ↓
6. コンボ入力猶予タイマー開始（comboInputWindow秒）
    ├── 猶予内にボタン再押下 → comboIndex++ → 手順4へ
    └── 猶予切れ or maxComboCount到達 → コンボ終了
    ↓
7. action.OnComboEnd() → クリーンアップ
    ↓
8. 仲間の中断前の行動を再開
```

## CoopActionManager

```csharp
public class CoopActionManager
{
    private CoopActionSlot _slot;           // 装備中の連携アクション
    private CoopCooldownTracker _cooldown;  // クールタイム管理
    private CoopComboState _comboState;     // コンボ状態

    // 保存: 中断前のAI行動状態
    private int _interruptedActionIndex;
    private int _interruptedTargetHash;

    public bool TryActivate(int companionHash, ref CharacterFlags companionFlags,
                            ref CharacterVitals companionVitals)
    {
        CoopActionBase action = _slot.GetEquippedAction();
        if (action == null) return false;

        // 怯み中チェック
        if (IsStaggered(companionFlags)) return false;

        if (_comboState.IsInCombo)
        {
            // コンボ継続（MP不要）
            return ContinueCombo(companionHash, action);
        }

        // 新規発動
        bool isFree = _cooldown.IsReady(action);
        if (!isFree)
        {
            // MP消費（クールタイムタイマーは変えない）
            if (companionVitals.currentMp < action.mpCost) return false;
            companionVitals.currentMp -= (short)action.mpCost;
        }
        // クールタイム消化済みならMP無料、かつクールタイム再開始
        if (isFree)
        {
            _cooldown.StartCooldown(action);
        }

        // 現在行動を中断
        InterruptCurrentAction(companionHash);

        // コンボ開始
        _comboState.Start(action.maxComboCount, action.comboInputWindow);
        return ExecuteComboStep(companionHash, action, 0);
    }

    private bool ContinueCombo(int companionHash, CoopActionBase action)
    {
        int nextIndex = _comboState.CurrentIndex + 1;
        if (nextIndex >= action.maxComboCount) return false;

        _comboState.Advance();
        return ExecuteComboStep(companionHash, action, nextIndex);
    }

    private bool ExecuteComboStep(int companionHash, CoopActionBase action, int comboIndex)
    {
        // ターゲット選択
        CoopComboStep step = action.comboSteps[comboIndex];
        int targetHash = TargetSelector.Select(step.targetSelect, companionHash);

        if (targetHash == 0 && step.abortIfNoTarget)
        {
            EndCombo(companionHash, action);
            return false;
        }

        // 効果実行
        action.ExecuteCombo(comboIndex, companionHash, targetHash);
        return true;
    }

    private void EndCombo(int companionHash, CoopActionBase action)
    {
        action.OnComboEnd(companionHash);
        _comboState.Reset();
        // 中断前の行動を再開
        ResumeInterruptedAction(companionHash);
    }
}
```

## クールタイムの挙動

```
タイムライン:
t=0   連携発動（クールタイム消化済み → MP無料、クールタイム15秒開始）
t=5   連携発動（クールタイム未消化 → MP消費、タイマー残り10秒のまま）
t=10  連携発動（クールタイム未消化 → MP消費、タイマー残り5秒のまま）
t=15  クールタイム完了（次回発動はMP無料）
t=16  連携発動（クールタイム消化済み → MP無料、クールタイム15秒再開始）
```

重要: MP消費で発動してもクールタイムタイマーはリセットも一時停止もしない。
クールタイムは前回の「無料発動」時点から常に一定時間で回復する。

## CoopCooldownTracker

```csharp
public class CoopCooldownTracker
{
    private float _cooldownRemaining;
    private float _cooldownDuration;

    public bool IsReady => _cooldownRemaining <= 0f;

    // クールタイム消化済みの場合のみ呼ぶ（無料発動時）
    public void StartCooldown(CoopActionBase action)
    {
        _cooldownDuration = action.cooldownDuration;
        _cooldownRemaining = _cooldownDuration;
    }

    public void Tick(float deltaTime)
    {
        if (_cooldownRemaining > 0f)
        {
            _cooldownRemaining -= deltaTime;
        }
    }

    // MP消費発動時はこのメソッドを呼ばない → タイマー変化なし
}
```

---

## 行動割り込みと再開

```csharp
// CompanionControllerに追加
public class CompanionController : AIBrain
{
    private ActionSlot? _interruptedAction;
    private int _interruptedTargetHash;

    // 現在の行動を中断して連携を優先
    public void InterruptForCoop()
    {
        _interruptedAction = _currentAction;
        _interruptedTargetHash = _currentTargetHash;
        // アニメーション中断等
    }

    // 連携終了後に中断前の行動を再開
    public void ResumeFromCoop()
    {
        if (_interruptedAction.HasValue)
        {
            ExecuteAction(_interruptedAction.Value, _interruptedTargetHash);
            _interruptedAction = null;
        }
        // なければ通常の3層判定に戻る
    }
}
```

---

## インタフェース
- `InputSystem.cooperationPressed` → CoopActionManager.TryActivate()
- `CompanionController.InterruptForCoop()` / `ResumeFromCoop()` → 行動割り込み
- `CoopCooldownTracker` → UIに残りクールタイム表示

## 機能分解
| 機能名 | 説明 | テスト種別 | 優先度 |
|--------|------|-----------|--------|
| Coop_ActionBase | 基底クラスとコンボステップ構造 | EditMode | High |
| Coop_ActivationFlow | 発動判定（怯み/MP/クールタイム） | EditMode | High |
| Coop_ComboSystem | コンボ段数管理・入力猶予・連続発動 | EditMode | High |
| Coop_TargetPerStep | コンボ段ごとのターゲット条件評価 | EditMode | High |
| Coop_Interruption | 行動割り込み・中断前行動の保存・再開 | EditMode | High |
| Coop_CooldownTracker | クールタイム管理（MP消費時はタイマー変更なし） | EditMode | High |
| Coop_WarpAction | ワープ連携（背後/そば、連続3回） | EditMode | Medium |
| Coop_ShieldAction | 盾連携（反射・足場） | EditMode | Medium |

## 設計メモ
- CoopActionBaseの継承で多様な連携を追加可能（ワープ、盾、回復、バフ等）
- 各サブクラスが個別の設定項目を持てる（Inspector設定可能）
- コンボ段ごとのターゲット条件でAITargetSelect/TargetFilterを再利用
- クールタイムは「無料発動」でのみリスタート。MP消費発動はタイマーに影響しない
- Section4でAdvancedAITemplatesと組み合わせた最適連携構築がエンドコンテンツ
