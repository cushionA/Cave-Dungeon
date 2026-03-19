# System: AdvancedAITemplates
Section: 4 — エンドコンテンツ

## 責務
AIルールの共有・テンプレート管理。プリセットのインポート/エクスポート、状況別テンプレート提案を提供する。

## 依存
- 入力: AIRuleBuilder（AIMode, AIRule, ActionSlot）、SaveSystem（永続化）
- 出力: テンプレート適用 → AIBrain のモード更新

## コンポーネント構成

| コンポーネント | 責務 | MonoBehaviour? |
|--------------|------|---------------|
| AITemplateManager | テンプレートの保存・読込・一覧管理 | No |
| AITemplateData | テンプレート1つ分のデータ定義 | No (Serializable) |
| AITemplateSuggester | 状況に応じたテンプレートの推薦 | No |

## データ構造

### AITemplateData
```csharp
[System.Serializable]
public struct AITemplateData
{
    public string templateId;
    public string templateName;
    public string description;
    public string authorName;
    public AITemplateCategory category;
    public AIMode[] modes;
    public ModeTransitionRule[] transitionRules;
    public string[] tags;            // "ボス向け", "雑魚処理", "回復重視" 等
}
```

### AITemplateCategory
```csharp
public enum AITemplateCategory : byte
{
    General,         // 汎用
    BossFight,       // ボス戦特化
    MobClear,        // 雑魚掃討
    SupportFocus,    // 回復・バフ重視
    Aggressive,      // 攻撃特化
    Defensive,       // 防御特化
    Custom           // ユーザー作成
}
```

### AITemplateManager (Pure Logic)
```csharp
public class AITemplateManager
{
    // テンプレート保存
    public void SaveTemplate(AITemplateData template);

    // テンプレート一覧（カテゴリフィルタ対応）
    public AITemplateData[] GetTemplates(AITemplateCategory? category = null);

    // テンプレート適用（CompanionのAIBrainに反映）
    public void ApplyTemplate(string templateId, int companionHash);

    // テンプレート削除
    public void DeleteTemplate(string templateId);

    // JSON形式でエクスポート/インポート（将来の共有機能用）
    public string ExportToJson(string templateId);
    public AITemplateData ImportFromJson(string json);
}
```

### AITemplateSuggester (Pure Logic)
```csharp
public static class AITemplateSuggester
{
    // 現在の戦闘状況に適したテンプレートを推薦
    public static string[] SuggestTemplates(
        bool isBossFight, float playerHpRatio, int enemyCount,
        AITemplateData[] availableTemplates);
}
```

## 機能分解

| 機能名 | 説明 | テスト種別 | 優先度 |
|--------|------|-----------|--------|
| AITemplates_Manager | テンプレート保存/読込/削除/一覧 | EditMode | High |
| AITemplates_ApplyRevert | テンプレート適用・元のAIに戻す | EditMode | High |
| AITemplates_Suggester | 状況別テンプレート推薦 | EditMode | Medium |
| AITemplates_ImportExport | JSON形式でのインポート/エクスポート | EditMode | Low |

## 設計メモ
- テンプレートはAIRuleBuilder（Section 2）のデータをそのまま保存する仕組み
- プリインストールテンプレートをゲーム内に5-10種用意（チュートリアル的役割）
- ユーザー作成テンプレートはCustomカテゴリに保存
- 将来的にはオンラインでテンプレートを共有可能に（JSON export/import基盤だけ今作る）
