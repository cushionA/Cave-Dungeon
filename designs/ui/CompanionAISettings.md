# UI: 仲間AI設定画面 (CompanionAISettings)

## 概要
仲間キャラクターのAI行動ルール（戦術）とショートカット割当を編集するメニュー画面。
共通部品としてのモードプリセットと、それらを組み合わせた戦術プリセットの2層構造を提供する。
ID管理により、モード上書き時に参照元の戦術へ自動同期する。

## 種別 / 基本情報
- 種別: メニュー（フルスクリーンモーダル）
- 目的: 仲間AIの戦術編集・プリセット管理・ショートカット割当
- 入力方式: マウス+キーボード+ゲームパッド（両対応）
- MenuStackManager経由で Push/Pop

---

## Layout

### トップレベル
```
┌──────────────────────────────────────────────────────────────┐
│  [← 戻る]   仲間AI設定                           [未保存 ●] │  ← ヘッダー
├──────────────────────────────────────────────────────────────┤
│  ┌──────────┬──────────┐                                    │
│  │ 戦術編集 │ショートカット│                                 │  ← タブバー
│  └──────────┴──────────┘                                    │
├──────────────────────────────────────────────────────────────┤
│                                                              │
│                    タブコンテンツエリア                       │
│                                                              │
└──────────────────────────────────────────────────────────────┘
```

### タブ1: 戦術編集
```
┌─ 左: 戦術リスト ──┬─ 右: エディタ ────────────────────────┐
│                   │ [名前入力] [既存保存] [プリセットとして保存]│
│ ▼ 現在の戦術      │                                           │
│ ◆ 現在の戦術      │ ┌─ モードスロット 1-4 ──┬─ 参照表示 ─┐  │
│                   │ │ [Mode1] [Mode2]       │ プリセット名 │  │
│ ▼ プリセット(3/20)│ │ [Mode3] [+ 追加]      │ 参照:3件    │  │
│ ● 均衡型          │ └───────────────────────┴──────────────┘  │
│ ● 攻撃型          │                                           │
│ ● 防御型          │ [遷移ルール]                             │
│ [+ 新規作成]      │ ┌──────────────────────────────────┐     │
│                   │ │ HP<30 → Mode2                    │     │
│                   │ │ Enemy<2m → Mode3                 │     │
│                   │ │ [+ ルール追加]                    │     │
│                   │ └──────────────────────────────────┘     │
│                   │                                           │
└───────────────────┴───────────────────────────────────────────┘
```

### タブ2: ショートカット
```
┌──────────────────────────────────────────────────────────────┐
│ パッドの十字キー等で「現在の戦術」内のモードを素早く切替できます。│
│ 選択肢は現在編集中の戦術の modes[0..3] に対応する。             │
│                                                              │
│  ┌─ スロット1 ─┐  ┌─ スロット2 ─┐                         │
│  │  ↑ Button   │  │  → Button   │                         │
│  │ [Mode1 ▼]  │  │ [Mode2 ▼]  │                          │
│  └─────────────┘  └─────────────┘                         │
│                                                              │
│  ┌─ スロット3 ─┐  ┌─ スロット4 ─┐                         │
│  │  ↓ Button   │  │  ← Button   │                         │
│  │ [Mode3 ▼]  │  │ [未割当 ▼] │                          │
│  └─────────────┘  └─────────────┘                         │
└──────────────────────────────────────────────────────────────┘
```
- 戦術プリセットごとに shortcutModeBindings[4] を保持。各値はその戦術の
  modes 配列のindex (0..3)、または -1（未割当）。
- モード数が変動して保存値が範囲外になった場合は未割当に自動フォールバック。
- 戦術プリセットごと切り替える導線（L1/R1+十字キー等）は別途用意する予定
  （docs/FUTURE_TASKS.md 参照）。

---

## UXML階層

```
Root (VisualElement: companion-ai-settings-root)
├─ Header (VisualElement: companion-ai-header)
│  ├─ BackButton (Button: back-button) — tooltip="メニューに戻る"
│  ├─ Title (Label: page-title) "仲間AI設定"
│  └─ DirtyIndicator (Label: dirty-indicator) — display切替
├─ TabBar (VisualElement: tab-bar)
│  ├─ TacticTabButton (Button: tab-button tab-button--active) "戦術編集"
│  └─ ShortcutTabButton (Button: tab-button) "ショートカット"
├─ TabContent (VisualElement: tab-content)
│  ├─ TacticTab (VisualElement: tactic-tab)
│  │  ├─ TacticList (VisualElement: tactic-list)
│  │  │  ├─ CurrentTacticSection (VisualElement: tactic-section)
│  │  │  │  ├─ SectionLabel (Label) "現在の戦術"
│  │  │  │  └─ CurrentTacticItem (Button: tactic-list-item tactic-list-item--current)
│  │  │  ├─ PresetSection (VisualElement: tactic-section)
│  │  │  │  ├─ SectionLabel (Label) "プリセット (3/20)"
│  │  │  │  └─ PresetScrollView (ScrollView: preset-scroll)
│  │  │  │     └─ [動的生成] PresetItem (Button: tactic-list-item)
│  │  │  └─ AddPresetButton (Button: add-preset-button) "＋ 新規作成"
│  │  │     — tooltip="空の戦術プリセットを追加"
│  │  └─ TacticEditor (VisualElement: tactic-editor)
│  │     ├─ EditorHeader (VisualElement: editor-header)
│  │     │  ├─ ConfigNameField (TextField: config-name-field)
│  │     │  ├─ SaveButton (Button: primary-button) "上書き保存"
│  │     │  │  — tooltip="現在編集中の内容でプリセットを上書き"
│  │     │  └─ SaveAsPresetButton (Button: secondary-button) "プリセット化"
│  │     │     — tooltip="現在の戦術を新しいプリセットとして保存"
│  │     ├─ ModesSection (VisualElement: modes-section)
│  │     │  ├─ SectionLabel (Label) "モードスロット (0-4)"
│  │     │  └─ ModeSlotsContainer (VisualElement: mode-slots)
│  │     │     └─ [動的生成 0-4個] ModeSlotButton (Button: mode-slot)
│  │     │        └─ [末尾] AddModeButton (Button: mode-slot--add) "＋"
│  │     │           — tooltip="モードを追加"
│  │     └─ TransitionSection (VisualElement: transition-section)
│  │        ├─ SectionLabel (Label) "遷移ルール"
│  │        ├─ TransitionList (VisualElement: transition-list)
│  │        │  └─ [動的生成] TransitionRow
│  │        └─ AddTransitionButton (Button) "＋ ルール追加"
│  └─ ShortcutTab (VisualElement: shortcut-tab) — display:none初期
│     ├─ Description (Label: shortcut-description)
│     └─ ShortcutGrid (VisualElement: shortcut-grid)
│        └─ [4個] ShortcutSlot (VisualElement: shortcut-slot)
│           ├─ SlotLabel (Label) "スロット1"
│           ├─ InputIcon (Label: input-icon) "↑"
│           └─ TacticDropdown (DropdownField) "Mode1 ▼"
│              — 選択肢: "(未割当)" + 現戦術の modes[i].modeName
│              — tooltip="このスロットに割り当てるモードを選択"
├─ DialogLayer (VisualElement: dialog-layer) — display:none初期
│  └─ [動的生成] ModalDialog
└─ TooltipPanel (VisualElement: tooltip-panel) — display:none初期
   └─ TooltipText (Label)
```

---

## ダイアログ定義

### Dialog 1: 戦術プリセット選択ダイアログ
プリセット項目をクリックした時に表示。
```
┌───────────────────────────────┐
│  均衡型                        │
│                                │
│  [現在の戦術に適用]             │
│  [編集する]                    │
│  [複製]                        │
│  [削除]   ← Customのみ有効     │
│  [キャンセル]                  │
└───────────────────────────────┘
```
- 「現在の戦術に適用」: 現在の戦術を選択プリセットで上書き（確認なし、undo可能）
- 「編集する」: このプリセットをエディタに読み込んで編集対象にする
- 「複製」: 新しいプリセットを作成
- 「削除」: 削除確認 → Delete実行（最低1個は残す）

### Dialog 2: 現在の戦術アクションダイアログ
現在の戦術項目をクリックした時に表示。
```
┌───────────────────────────────┐
│  現在の戦術                    │
│                                │
│  [編集する]                    │
│  [プリセットとして保存]         │
│  [キャンセル]                  │
└───────────────────────────────┘
```

### Dialog 3: モードスロットアクションダイアログ
戦術エディタ内のモードスロットをクリックした時に表示。
```
┌───────────────────────────────┐
│  Mode1: 攻撃型A                │
│                                │
│  [モードを変更]     ← プリセット選択 │
│  [このスロットから削除]        │
│  [モードプリセットとして保存]   │
│  [このモードだけ変更]          │  ← 独立コピー（安全弁）
│  [キャンセル]                  │
└───────────────────────────────┘
```
- 「モードを変更」: モードプリセット一覧から選択して差し替え（参照リンク維持）
- 「このスロットから削除」: モードスロットから外す
- 「モードプリセットとして保存」: 現モードを新規モードプリセットとして保存
- 「このモードだけ変更」: 参照を切り、独立コピーに変換（modeId→空文字列）

### Dialog 4: 上書き影響範囲ダイアログ
既存名で「プリセットとして保存」または「上書き保存」時に表示。
```
┌─────────────────────────────────────────┐
│  ⚠ 上書き確認                           │
│                                         │
│  「均衡型」を上書きします。              │
│  このプリセットは 3個の戦術から参照     │
│  されており、全て自動更新されます:       │
│                                         │
│    ・戦術A                              │
│    ・戦術B                              │
│    ・現在の戦術                         │
│                                         │
│  [上書きする]   [キャンセル]             │
└─────────────────────────────────────────┘
```
- モードプリセット上書き時は GetReferencingConfigs で参照元戦術を列挙
- 戦術プリセット上書き時は参照情報なし（戦術は直接使用のみ）
- デフォルトフォーカスは「キャンセル」（安全な方）

### Dialog 5: 未保存変更破棄確認
別プリセットに切り替え時・画面を閉じる時などに表示。
```
┌─────────────────────────────────────┐
│  未保存の変更があります。           │
│                                     │
│  現在の編集内容を破棄しますか？     │
│                                     │
│  [破棄する]   [キャンセル]           │
└─────────────────────────────────────┘
```
- デフォルトフォーカスは「キャンセル」

### Dialog 6: 削除確認
プリセット削除時に表示。
```
┌─────────────────────────────────────┐
│  「均衡型」を削除しますか？         │
│  この操作は取り消せません。         │
│                                     │
│  [削除]   [キャンセル]               │
└─────────────────────────────────────┘
```
- 「削除」ボタンは danger スタイル（赤）
- デフォルトフォーカスは「キャンセル」

---

## Styles (USS)

### テーマ変数（既存 game-ui-patterns.md の共通テーマを踏襲）
- `--color-bg-overlay` / `--color-bg-panel` / `--color-bg-input`
- `--color-accent` (プリセット選択/フォーカス)
- `--color-danger` (削除ボタン/未保存マーカー)
- `--color-text-primary` / `--color-text-secondary`
- `--spacing-xs/sm/md/lg/xl`

### 主要スタイル方針
- ルート: `position: absolute; 100% x 100%; flex-direction: column; background-color: var(--color-bg-overlay)`
- タブバー: `flex-direction: row; border-bottom`
- タブアクティブ: アクセント色の下線
- 戦術リスト: 左ペイン 320px固定、右ペイン flex-grow:1
- モードスロット: 120px x 80px、4列グリッド、追加ボタンは破線ボーダー
- プリセットアイテム（選択中）: border-left でアクセント色
- フォーカスリング: `border-width:2px; border-color: var(--color-accent)`（パッド対応）
- ダイアログ: 画面中央モーダル、背景は rgba(0,0,0,0.75)
- 未保存マーカー: 赤ドット `●`
- モードスロットのうち「プリセット参照あり」は右上にリンクアイコン、「独立コピー」は無表示
- トランジション: `transition-property: scale, background-color, border-color; duration: 0.15s`

---

## Events / データフロー

### イベント一覧
| 要素 | イベント | ハンドラ |
|------|----------|----------|
| BackButton | clicked | `OnBackClicked` → 未保存確認 → MenuStackManager.Pop |
| TacticTabButton | clicked | `SwitchTab(Tactic)` |
| ShortcutTabButton | clicked | `SwitchTab(Shortcut)` |
| TacticListItem (Preset) | clicked | `ShowPresetActionDialog(configId)` |
| TacticListItem (Current) | clicked | `ShowCurrentTacticActionDialog()` |
| AddPresetButton | clicked | 空の戦術を Save → 選択 |
| ConfigNameField | value change | `_editingConfig.configName = value` + Dirty |
| SaveButton | clicked | 影響範囲計算 → Dialog 4 → `TacticalPresetRegistry.UpdateById` |
| SaveAsPresetButton | clicked | 新名入力 → 同名存在チェック → Dialog 4 or 直接 Save |
| ModeSlotButton | clicked | `ShowModeSlotActionDialog(slotIndex)` |
| AddModeButton | clicked | モードプリセット選択 → スロット追加 |
| AddTransitionButton | clicked | 空のルール追加 |
| ShortcutDropdown | value change | `SetShortcutBinding(i, modeIndex)` — 現戦術の modes[modeIndex] を割当（-1=未割当） + Dirty |

### データソース
- `ModePresetRegistry` — モードプリセット一覧（GetAll）
- `TacticalPresetRegistry` — 戦術プリセット一覧（GetAll, GetReferencingConfigs）
- `ActionTypeRegistry` — 利用可能なアクション一覧（モードエディタで使用、Phase4以降）
- `_currentTactic` — 現在の戦術（コントローラ内部保持、エディタバッファ）
- `_editingConfigId` — 編集中のプリセットID（`null` = 現在の戦術）
- `_isDirty` — 未保存フラグ

### Data Binding
- CurrentTacticItem ← `_currentTactic.configName` （未命名なら「現在の戦術」）
- PresetSection.Label ← `$"プリセット ({registry.Count}/20)"`
- PresetScrollView ← `tacticalRegistry.GetAll()`（変更時に再構築）
- ModeSlotsContainer ← `_editingConfig.modes`
- TransitionList ← `_editingConfig.modeTransitionRules`
- DirtyIndicator visible ← `_isDirty`

---

## ToolTip 仕様

全てのアイコンボタン・略称ボタンに `tooltip` 属性を設定する:
- BackButton: "メニューに戻る"
- AddPresetButton: "空の戦術プリセットを追加"
- SaveButton: "現在編集中の内容でプリセットを上書き"
- SaveAsPresetButton: "現在の戦術を新しいプリセットとして保存"
- AddModeButton: "モードを追加"
- AddTransitionButton: "遷移ルールを追加"
- ShortcutDropdown: "このスロットに割り当てるモードを選択"
- モードスロット上のリンクアイコン: "プリセット参照あり - このモードを上書きすると参照元も全て更新されます"

パッド操作時もツールチップを表示するため、フォーカス時にも表示する方針:
- `RegisterCallback<FocusInEvent>` でツールチップパネルに文字列を反映
- `RegisterCallback<FocusOutEvent>` で非表示
- `RegisterCallback<MouseEnterEvent>` / `MouseLeaveEvent` でも同様
- `tooltip` 属性だけではパッド操作時に出ないため、カスタムハンドラで対応

---

## Phase分け（このPRの実装範囲）

**Phase 3a: 骨格とタブ切替** ← このPRで実装
- UXML/USS/Controller の枠組み
- タブ切替（戦術編集 ↔ ショートカット）
- 戦術リスト（現在の戦術 + プリセット一覧表示）
- ヘッダー・戻るボタン
- ToolTip基盤（フォーカス/ホバー両対応）

**Phase 3b: 戦術エディタ核** ← このPRで実装
- 戦術名編集
- モードスロット表示・クリック→ダイアログ
- 遷移ルール表示（読み取り専用で十分、編集は後続Phase）
- 保存・プリセット化ボタン + 影響範囲ダイアログ

**Phase 3c: ダイアログ系** ← このPRで実装
- 6種のダイアログ（モーダル）
- 削除確認・未保存破棄確認
- 独立コピー化（安全弁）

**Phase 3d: ショートカットタブ** ← このPRで実装
- 4スロットのドロップダウン（現戦術の modes[0..3] を選択肢として表示）
- モード選択→shortcutModeBindings 反映（未割当は -1）
- 初期値は全スロット -1（未割当）。0埋めだと「modes[0]を指す」状態と区別できないため

**後続PR（このPRでは実装しない）**
- モード内部エディタ（AIRule/AITargetSelect/ActionSlot の GUI編集）
  → 現状はモードプリセットの追加・差替えまで。モード中身の編集は次PR
- 遷移ルールエディタ（追加・削除・条件編集 GUI）
  → 現状は表示のみ。次PR以降
- パッドナビゲーション詳細（BFS順）
  → Focus() ベースの基本フォーカスのみ実装。細かい順序制御は次PR

---

## テスト観点（Edit Mode）

Controller のロジック部分をテスト可能な形で分離し、以下を検証:
1. `CompanionAISettingsLogic_LoadPresets_PopulatesList` — registry から取得したプリセットを正しくバッファに反映
2. `CompanionAISettingsLogic_SelectPreset_SetsEditingConfigId` — 選択中の configId が正しく更新
3. `CompanionAISettingsLogic_EditField_MarksDirty` — フィールド編集で isDirty=true
4. `CompanionAISettingsLogic_Save_ClearsDirty` — 保存後 isDirty=false
5. `CompanionAISettingsLogic_SwitchTacticWithUnsavedChanges_ReturnsConfirmRequest` — 未保存で切替を試みると確認要求を返す
6. `CompanionAISettingsLogic_ConvertModeToIndependent_ClearsModeId` — 独立コピー化で modeId が空文字列になる
7. `CompanionAISettingsLogic_OverwriteExistingPreset_CascadesToReferencingConfigs` — モードプリセット上書きが戦術に波及
8. `CompanionAISettingsLogic_DeleteLastPreset_Rejected` — 最後の1個は削除不可
9. `CompanionAISettingsLogic_SetShortcutBinding_UpdatesModeIndex` — 現戦術の modeIndex が反映
   追加テスト:
   - `InitialShortcutBindings_AllUnassigned` — 初期値は -1 埋め
   - `SetShortcutBinding_UnassignedValue_Stored` — -1 で未割当に戻せる
   - `CreateDefaultShortcutBindings_ReturnsMinusOneFilled` — ヘルパーの検証
10. `CompanionAISettingsLogic_AddMode_ExceedsMaxModes_Rejected` — 5個目の追加は拒否（既存 RuleEditorLogic.k_MaxModes=4 と整合）

---

## ファイル構成（このPRで生成）

```
Assets/MyAsset/UI/CompanionAISettings/
├─ CompanionAISettings.uxml
└─ CompanionAISettings.uss

Assets/MyAsset/Runtime/UI/
└─ CompanionAISettingsController.cs    ← MonoBehaviour + UIDocument

Assets/MyAsset/Core/UI/
└─ CompanionAISettingsLogic.cs         ← 純ロジック（テスト可能）

Assets/Tests/EditMode/
└─ CompanionAISettings_LogicTests.cs   ← 10テスト
```

---

## 未決事項（要確認）

1. **既存戦術名の重複チェック**: 同名プリセット保存時に「上書きしますか？」を出す方針でOK？
2. **現在の戦術の永続化**: 現在の戦術は実行中セーブに保存される前提だが、今回UIからは runtime的な保存タイミングまでは触らない（単にバッファリング）でOK？
3. **モードプリセットの命名入力**: モードプリセット化ダイアログは今回 Dialog として実装する？ それとも簡易 Prompt でOK？
4. **ショートカットボタンのアイコン**: 上下左右キー表示は今回はテキスト（↑→↓←）で十分？
