# System: AIRuleBuilder
Section: 2 — AI・仲間・連携

## 責務
プレイヤーが仲間AIの行動パターンを全面的にカスタマイズするシステム。
複数モードの作成、モード内ルール編集、モード間の自動切替条件設定、手動切替ショートカットを提供する。
ゲームのコアバリュー「仲間AIカスタム」の中心システム。

## 依存
- 入力: CompanionAI_Basic（適用先）、AICore（ConditionEvaluator, TargetSelector）、InputSystem（ショートカット入力）
- 出力: CompanionAIConfig → 仲間AIの動作定義

---

## 全体構造

```
CompanionAIConfig（プレイヤーが編集する全体設定）
    │
    ├── modes[]: AIMode × 最大4つ
    │   ├── mode[0] "攻撃特化"
    │   │   ├── targetRules[]     ← カスタム可能
    │   │   ├── actionRules[]     ← カスタム可能
    │   │   ├── actions[]         ← 行動データ配列（汎用ActionSlot）
    │   │   └── defaultAction     ← カスタム可能
    │   ├── mode[1] "回復支援"
    │   └── ...
    │
    ├── modeTransitionRules[]: モード自動切替条件
    │   ├── [0] conditions: [味方HP<30%] → mode[1]
    │   ├── [1] conditions: [敵数=0]     → mode[0]
    │   └── defaultModeIndex: 0
    │
    ├── shortcutBindings[]: ショートカットキー割当
    │   ├── key1 → mode[0]
    │   ├── key2 → mode[1]
    │   └── ...
    │
    └── overrideSettings:
        ├── overrideTimeout: 15秒
        └── autoResumeOnCondition: true
```

---

## 汎用行動データ（ActionSlot）

行動の中身を「魔法」に限定せず、**行動タイプ + ターゲット条件 + パラメータ**の汎用構造で表現する。

ActionSlotの定義はAICore.mdに準拠（ActionExecType 5分類 + paramIdでサブ行動を指定）。

プレイヤーがルール構築UIで行動を選ぶ際は、ActionExecTypeとparamIdの組み合わせを自然言語UIで提示する:

| UI表示 | ActionExecType | paramId |
|--------|---------------|---------|
| 通常攻撃 | Attack | AttackMotionData[0] |
| 強攻撃 | Attack | AttackMotionData[2] |
| ファイアボール | Cast | MagicDefinition ID |
| ヒール | Cast | MagicDefinition ID |
| 回避 | Instant | InstantAction.Dodge |
| 背後ワープ | Instant | InstantAction.WarpBehind |
| ターゲットに接近 | Sustained | SustainedAction.MoveToTarget |
| 追従 | Sustained | SustainedAction.Follow |
| ガード | Sustained | SustainedAction.Guard |
| 逃走 | Sustained | SustainedAction.Flee |
| ターゲット指示 | Broadcast | BroadcastAction.DesignateTarget |

### 行動の解放システム

```csharp
/// <summary>
/// 解放済み行動を管理する。
/// 探索で「～の書」を入手すると新しい行動がUIに出現する。
/// execType + paramId の組み合わせで解放状態を管理。
/// </summary>
public class ActionUnlockRegistry
{
    private HashSet<(ActionExecType, int)> _unlockedActions;

    // 初期解放: Attack(0-2), Cast(習得済み魔法), Instant(Dodge),
    //           Sustained(MoveToTarget/Follow/Retreat/Flee/Patrol/Guard), Broadcast(なし)

    public bool IsUnlocked(ActionExecType exec, int paramId)
        => _unlockedActions.Contains((exec, paramId));

    public void Unlock(ActionExecType exec, int paramId)
        => _unlockedActions.Add((exec, paramId));
}
```

探索報酬として:
```
「挟撃の書」入手 → Sustained / SustainedAction.Flank 解放
  → ルール編集で「挟撃移動」が行動リストに出現

「守護陣の書」入手 → Sustained / SustainedAction.ShieldDeploy 解放
  → 味方の前に盾を展開する行動を組めるようになる

ボス撃破報酬「瞬影の書」入手 → Instant / InstantAction.WarpBehind 解放
  → ターゲット背後へのワープ行動が使えるようになる
```

---

## モード切替

### 自動切替（条件ベース）

```csharp
[Serializable]
public struct ModeTransitionRule
{
    /// <summary>切替条件（AICondition配列、AND結合）</summary>
    public AICondition[] conditions;

    /// <summary>条件成立時の遷移先モードインデックス</summary>
    public byte targetModeIndex;

    /// <summary>優先度（配列順 = 優先度順）</summary>
    // → 配列の先頭が最優先
}
```

評価:
```
毎判定間隔:
  for rule in modeTransitionRules:
      if EvaluateConditions(rule.conditions):
          currentMode = modes[rule.targetModeIndex]
          break
  // 全不一致 → defaultModeIndex
```

### 手動切替（ショートカット）

```
ショートカットキー割当（最大4つ）:
  方向キー上 + 連携ボタン → mode[0]
  方向キー右 + 連携ボタン → mode[1]
  方向キー下 + 連携ボタン → mode[2]
  方向キー左 + 連携ボタン → mode[3]
```

### 優先度

```
1. プレイヤー手動指示（最優先、即反映）
2. 条件自動切替ルール
3. 現在モード維持

手動切替後:
  → overrideTimeout秒（デフォルト15秒）経過で自動切替に戻る
  → または autoResumeOnCondition が true なら条件成立で自動復帰
  → 再度手動切替で上書き
```

---

## ルール構築UI（概念）

```
┌─── 仲間AIカスタム ───────────────────────────┐
│                                               │
│ [モード1: 攻撃特化] [モード2: 回復] [+追加]    │
│ ──────────────────────────────────────────── │
│                                               │
│ ◆ ターゲットルール                             │
│  [ルール1] もし: [累積ダメージ] [100以上]      │
│    → ターゲット: [ダメージスコア最大の敵]       │
│  [ルール2] もし: [敵がいる]                    │
│    → ターゲット: [最寄りの敵]                   │
│                                               │
│ ◆ 行動ルール                                  │
│  [ルール1] もし: [距離3以内]                   │
│    → 行動: [通常攻撃]                          │
│  [ルール2] もし: [距離3〜8]                    │
│    → 行動: [ファイアボール] 🔮                  │
│  [デフォルト]: [ターゲットに接近]               │
│                                               │
│ ──────────────────────────────────────────── │
│ ◆ モード切替条件                               │
│  [条件1] 味方HP30%以下 → モード2: 回復         │
│  [ショートカット] ↑+連携 → モード1             │
│                                               │
│ [プリセット保存] [プリセット読込]               │
└───────────────────────────────────────────────┘
```

行動選択時:
```
┌─── 行動を選択 ──────────────┐
│ ◆ 基本                      │
│   待機                       │
│   プレイヤー追従              │
│   ターゲットに接近            │
│   距離を取る                  │
│   逃走                       │
│   ガード                     │
│   回避                       │
│   通常攻撃                   │
│                              │
│ ◆ 魔法                      │
│   🔮 ファイアボール          │
│   🔮 ヒール                  │
│   🔮 バリア                  │
│                              │
│ ◆ 拡張（入手済み）           │
│   📜 プレイヤーワープ        │
│   📜 挟撃移動               │
│   📜 盾展開            [NEW] │
│                              │
│ ◆ 連携スキル                 │
│   ⚡ ワープ連携              │
│   ⚡ 盾連携                  │
└──────────────────────────────┘
```

行動選択後、Sustained行動の場合は持続時間を設定できる:
```
┌─── 行動パラメータ設定 ─────────┐
│ 行動: ガード                    │
│                                 │
│ 持続時間: [===●====] 5.0秒     │
│  ※ 0にすると次の行動判定まで持続 │
│                                 │
│ [詳細設定]                      │
│  カウンター: [被弾時] → [背後ワープ] │
└─────────────────────────────────┘
```

- **持続時間 > 0**: 指定秒数で自動終了 → 次の行動判定へ
- **持続時間 = 0**: 行動ルールの再評価で別行動が選ばれるまで持続（無期限）
- Sustained以外（Attack, Cast, Instant, Broadcast）は持続時間設定なし

---

## プリセットシステム

```csharp
/// <summary>
/// 仲間AIカスタム設定の保存単位。
/// </summary>
public class CompanionAIPreset : ScriptableObject
{
    public string presetName;
    public string description;

    // モード定義
    public AIMode[] modes;
    public ModeTransitionRule[] transitions;
    public int defaultModeIndex;

    // メタ情報
    public bool isSystemPreset;  // システム提供かカスタムか
}
```

| 種別 | 入手方法 | 内容 |
|------|---------|------|
| **システムプリセット** | 探索・ボス撃破・NPC会話 | 完成済みの行動パターン + 新ActionType解放 |
| **カスタムプリセット** | プレイヤーが保存 | 自由編集したカスタム設定（最大20枠） |

システムプリセットは**二重の報酬**:
1. そのまま使える完成済みパターン（カジュアル層）
2. 新ActionTypeが解放されて自分のカスタムに組み込める（やり込み層）

---

## コンポーネント構成

| コンポーネント | 責務 | MonoBehaviour? |
|--------------|------|---------------|
| RuleEditorLogic | モード/ルール/行動の追加・削除・並べ替え | No |
| ActionSlotBuilder | ActionSlot構築（行動タイプ選択→パラメータ設定） | No |
| ActionTypeRegistry | 解放済みActionTypeの管理 | No |
| ModeTransitionEditor | モード切替条件の編集 | No |
| ShortcutBindingEditor | ショートカット割当の編集 | No |
| PresetManager | プリセットの保存・読込・システムプリセット管理 | No |
| RuleValidator | ルールの妥当性チェック（到達不可能ルール警告等） | No |
| ConfigApplier | CompanionAIConfig → CompanionAIのAIModeに適用 | No |

---

## 行動実行の流れ

```
CompanionController.Evaluate()
    ↓
1. 手動オーバーライド中? → そのモードを使用
2. modeTransitionRules評価 → 条件成立ならモード切替
3. 現在モードの3層判定
    ↓
actionRules[i] マッチ → actions[resultIndex] = ActionSlot
    ↓
ActionSlot.execType で ActionExecutor がハンドラを引く:
    Attack    → AttackActionHandler（paramId = AttackMotionDataインデックス）
    Cast      → CastActionHandler（paramId = MagicDefinition ID）
    Instant   → InstantActionHandler（paramId = InstantAction enum値: Dodge, WarpBehind等）
    Sustained → SustainedActionHandler（paramId = SustainedAction enum値: Follow, Guard等）
    Broadcast → BroadcastActionHandler（paramId = BroadcastAction enum値: DesignateTarget等）
```

ターゲットは第1層（targetRules）で確定済み。行動にターゲット上書きはない。

---

## インタフェース
- `CompanionAI_Basic.ApplyConfig(CompanionAIConfig)` → 設定適用
- `InputSystem` → ショートカット入力でモード手動切替
- `SaveSystem` → プリセット永続化
- `ActionTypeRegistry.Unlock(ActionType)` → 新行動解放
- `GameManager.Events.OnPresetAcquired` → システムプリセット入手通知

## 機能分解
| 機能名 | 説明 | テスト種別 | 優先度 |
|--------|------|-----------|--------|
| AIRule_ActionSlot | 汎用ActionSlotの構築・実行分岐 | EditMode | High |
| AIRule_ModeEditor | モード作成・ルール編集・行動配置 | EditMode | High |
| AIRule_ModeTransition | モード自動切替条件の評価 | EditMode | High |
| AIRule_ManualOverride | ショートカット手動切替・タイムアウト復帰 | EditMode | High |
| AIRule_ActionTypeRegistry | 行動タイプの解放管理 | EditMode | Medium |
| AIRule_Validation | ルール矛盾検出・到達不可能ルール警告 | EditMode | Medium |
| AIRule_PresetManager | プリセット保存・読込・システムプリセット管理 | EditMode | Medium |
| AIRule_ConfigApply | CompanionAIConfig → CompanionAIのAIModeに適用 | EditMode | Medium |

## 設計メモ
- ゲームのコアバリュー。行動タイプの汎用構造でカスタムの深さを確保
- 行動タイプ解放がメトロイドヴァニアの探索報酬と結合（新能力→新戦術）
- システムプリセットは二重報酬: そのまま使える + 新ActionType解放
- 条件はAIConditionType(12種)、ターゲットはAITargetSelect — AICore基盤を完全再利用
- モード最大4つ = ショートカットキー4方向と対応
- プリセットはカスタム最大20枠、セーブデータに含む
