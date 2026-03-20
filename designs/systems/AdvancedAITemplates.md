# System: AdvancedAITemplates
Section: 4 — エンドコンテンツ

## 責務
AIルールのテンプレート管理。既存 PresetManager / CompanionAIConfig を拡張し、
カテゴリ分類・タグ付け・状況別推薦を提供する。

## 依存
- 入力:
  - `PresetManager` (Section 2) — 既存プリセット管理。AIPreset / CompanionAIConfig の保存・読込
  - `RuleEditorLogic.BuildConfig()` (Section 2) — CompanionAIConfig の生成
  - `CompanionAIConfig` (Section 2) — AIMode[] + ModeTransitionRule[] + shortcutModeBindings
  - `SaveManager.Register(ISaveable)` — テンプレート永続化
- 出力:
  - テンプレート適用 → `CompanionAIConfig` を仲間AIに反映
  - `GameEvents.OnCustomRulesChanged` — テンプレート適用後にAIルール変更を通知

## 旧設計からの変更点

### PresetManager との関係整理
- 旧設計: `AITemplateManager` が独自の `AITemplateData` リストを管理（PresetManager と重複）
- **新設計**: `AITemplateManager` は `PresetManager` を内部で使用し、メタ情報（カテゴリ・タグ）を `AITemplateData` で管理
- `AITemplateData.config` フィールドが既存 `CompanionAIConfig` をそのまま内包
- PresetManager の `AIPreset.config` と `AITemplateData.config` は同じ型

### ImportExport の除外
- 旧設計: `AITemplates_ImportExport` を4つ目の機能として定義
- **新設計**: 除外。将来のオンライン共有は別セクションで検討。現時点では YAGNI

## コンポーネント構成

| コンポーネント | 責務 | MonoBehaviour? |
|--------------|------|---------------|
| AITemplateManager | テンプレートの保存・読込・一覧・削除（ISaveable） | No |
| AITemplateSuggester | 状況に応じたテンプレートの推薦 | No (static class) |

## データ構造

### AITemplateData (Common_Section4 で定義)
```csharp
// Section4Structs.cs に定義済み
[Serializable]
public struct AITemplateData
{
    public string templateId;
    public string templateName;
    public string description;
    public string authorName;             // "System" or ユーザー名
    public AITemplateCategory category;
    public CompanionAIConfig config;      // 既存 Section 2 の構造体をそのまま使用
    public string[] tags;
}
```

### AITemplateManager (Pure Logic, ISaveable)
```csharp
/// <summary>
/// AIテンプレートの管理。
/// 内部で PresetManager を活用し、メタ情報を追加管理する。
/// </summary>
public class AITemplateManager : ISaveable
{
    public string SaveId => "AITemplateManager";

    private const int k_MaxTemplates = 30;

    /// <summary>テンプレートを保存。上限超過時は false</summary>
    public bool SaveTemplate(AITemplateData template);

    /// <summary>
    /// テンプレート一覧取得。カテゴリフィルタ対応。
    /// null で全件取得。
    /// </summary>
    public AITemplateData[] GetTemplates(AITemplateCategory? category = null);

    /// <summary>テンプレートID で取得。見つからなければ null</summary>
    public AITemplateData? GetTemplate(string templateId);

    /// <summary>テンプレート削除。システムテンプレートは削除不可</summary>
    public bool DeleteTemplate(string templateId);

    /// <summary>
    /// テンプレートを仲間に適用。
    /// 適用前の CompanionAIConfig を _previousConfig に保存（Revert用）。
    /// 適用後に GameEvents.FireCustomRulesChanged() を発火。
    /// </summary>
    public void ApplyTemplate(string templateId, int companionHash);

    /// <summary>適用前の設定に戻す</summary>
    public bool RevertTemplate(int companionHash);

    /// <summary>テンプレート適用中か</summary>
    public bool HasAppliedTemplate(int companionHash);

    /// <summary>
    /// 既存 PresetManager の AIPreset を AITemplateData に変換してインポート。
    /// PresetManager → AITemplateManager への移行パス。
    /// </summary>
    public void ImportFromPresetManager(PresetManager presetManager);

    // ISaveable
    public object Serialize();
    public void Deserialize(object data);
}
```

### AITemplateSuggester (Static Pure Logic)
```csharp
/// <summary>
/// 現在の戦闘状況に適したテンプレートを推薦する。
/// </summary>
public static class AITemplateSuggester
{
    /// <summary>
    /// 状況に合ったテンプレートIDを推薦順で返す（最大3件）。
    ///
    /// ロジック:
    /// 1. isBossFight → BossFight カテゴリを優先
    /// 2. enemyCount >= 5 → MobClear カテゴリを優先
    /// 3. playerHpRatio <= 0.3 → SupportFocus / Defensive カテゴリを優先
    /// 4. それ以外 → General / Aggressive カテゴリ
    ///
    /// カテゴリ一致後、タグでさらにフィルタリング。
    /// </summary>
    public static string[] SuggestTemplates(
        bool isBossFight,
        float playerHpRatio,
        int enemyCount,
        AITemplateData[] availableTemplates);
}
```

## インタフェース

### システム間接続点

| 接続先 | 方式 | 用途 |
|--------|------|------|
| PresetManager (Section 2) | コンポジション | 既存プリセットの読み書き基盤 |
| RuleEditorLogic (Section 2) | データ参照 | CompanionAIConfig 構造 |
| GameEvents.OnCustomRulesChanged | R3 Observable発火 | テンプレート適用後のAI更新通知 |
| SaveManager.Register() | メソッド呼び出し | テンプレートリスト永続化 |

## 機能分解

| 機能名 | 説明 | テスト種別 | 優先度 |
|--------|------|-----------|--------|
| AITemplates_Manager | テンプレート保存/読込/削除/一覧/ISaveable | EditMode | High |
| AITemplates_ApplyRevert | テンプレート適用・元のAIに戻す・GameEvents連携 | EditMode | High |
| AITemplates_Suggester | 状況別テンプレート推薦（ボス戦/雑魚/低HP判定） | EditMode | Medium |

## 設計メモ
- テンプレートの核は CompanionAIConfig（Section 2 で完成済み）。新しいデータ構造を作らない
- PresetManager の既存プリセット → AITemplateData への変換は `ImportFromPresetManager()` で一括対応
- システムテンプレート（authorName="System"）はゲーム開始時に 5-10 種プリインストール
- ユーザー作成テンプレートは Custom カテゴリに自動分類
- Revert 機能は直前の適用1回分のみ保持（スタックにはしない。シンプルに）
- Suggester は static class。状態を持たず、入力から純粋に推薦結果を計算
