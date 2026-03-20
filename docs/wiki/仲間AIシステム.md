# 仲間AIシステム

## AIBrain 3層判定

AIBrainは3層の判定を毎判定間隔で実行:

```
Layer 1: ターゲット切替判定
    ↓
Layer 2: 行動切替判定（AIRule[] 優先度順）
    ↓
Layer 3: デフォルト行動（棒立ち防止）
```

### 条件評価

- **AIConditionType**: 12種（HP%, MP%, 距離, 属性, etc.）
- **CompareOp**: 6種（<, <=, ==, !=, >=, >）
- AIRule.conditions は **AND結合**、AIRule[] は **優先度順（先勝ち = OR結合）**

### ターゲット選択

- **DamageScore**: 累積ダメージ × 倍率 + 時間減衰（ヘイトシステム廃止）
- **TargetFilter**: CharacterFlagsのビット演算で高速フィルタリング

## CompanionAIConfig

仲間AIのカスタマイズ設定:

```csharp
public struct CompanionAIConfig
{
    public AIMode[] modes;                      // 最大4モード
    public ModeTransitionRule[] transitionRules; // モード自動切替条件
    public int[] shortcutModeBindings;           // ショートカット手動切替
}
```

### AIMode

```csharp
public struct AIMode
{
    public string modeName;
    public AIRule[] targetRules;  // ターゲット切替ルール
    public AIRule[] actionRules;  // 行動切替ルール
    public ActionSlot defaultAction;
}
```

## 連携アクション（CoopAction）

### 仕組み

1. プレイヤーが連携ボタンを押す
2. 仲間の現在行動を中断（怯み中でなければ）
3. CoopActionBaseの派生クラスを実行
4. 連携終了後に元の行動を再開

### コンボ対応

- 連打で最大N回連続発動
- MP消費は初回のみ
- 各コンボ段ごとにAITargetSelectでターゲット条件を個別設定

### クールタイム報酬

| 状態 | 効果 |
|------|------|
| CT消化済み | MP無料で発動 |
| CT未消化 | 仲間MPを消費（タイマーは変えない） |

## AIテンプレート（Section 4）

### AITemplateData

```csharp
public struct AITemplateData
{
    public string templateId;
    public string templateName;
    public AITemplateCategory category;  // General, BossFight, MobClear, etc.
    public bool isSystemPreset;          // システムプリセットは削除不可
    public CompanionAIConfig config;     // 既存構造体をそのまま内包
}
```

### AITemplateManager

- テンプレートの保存・取得・削除（ISaveable）
- ApplyTemplate: テンプレートを仲間に適用
- RevertTemplate: 直前1回分のみ元に戻す

### AITemplateSuggester

戦況に応じてテンプレートを推薦（最大3件）:

```
優先度: BossFight → MobClear → SupportFocus/Defensive → General/Aggressive
```

## コンポーネント構成

| コンポーネント | 責務 | MonoBehaviour? |
|--------------|------|---------------|
| AIBrain | 3層判定のEvaluate()を定期実行 | Yes |
| ConditionEvaluator | 条件式の評価 | No |
| TargetSelector | ターゲット選択ロジック | No |
| ActionExecutor | 行動実行（Dict<ActionExecType, ActionBase>） | No |
| CompanionController | AIBrain継承。モード手動/自動切替 | Yes |
| DamageScoreTracker | ダメージスコア追跡 | No |
| AITemplateManager | テンプレート管理（ISaveable） | No |
| AITemplateSuggester | テンプレート推薦（static） | No |
