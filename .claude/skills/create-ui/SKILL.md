---
name: create-ui
description: Create Unity UI screens using UI Toolkit (UXML/USS) or uGUI. Generates layout definitions, stylesheets, and backing scripts.
user-invocable: true
argument-hint: <UI screen name> [HUD|Menu|Dialog|Settings|Inventory|custom]
---

# Create UI: $ARGUMENTS

Unity UIの画面を作成する。UI Toolkit (UXML/USS) またはuGUIで実装。

## 手順

### ステップ1: 要件確認

ユーザーと対話して以下を確認:
- **画面の目的**: 何を表示・操作する画面か
- **含む要素**: ボタン、テキスト、スライダー、リスト等
- **データバインディング**: どのゲームデータと連動するか
- **遷移**: この画面からどこに行けるか

### ステップ2: UI構造定義

`instruction-formats/ui-structure.md` フォーマットに従い、UI構造を定義する。

```
# UI: [画面名]

## Layout
- Root (type: VisualElement)
  - style: [ルートスタイル]
  - children:
    - [要素名] (type: [タイプ])
      ...

## Styles (USS)
- .[クラス名]:
  - [プロパティ]: [値]

## Events
- [要素名].[イベント名] → [ハンドラ説明]

## Data Binding
- [要素名] ← [データソース].[プロパティ]
```

### ステップ3: UI構造をユーザーに提示

構造をユーザーに提示し、レイアウトやスタイルの修正を受け付ける。

### ステップ4: ファイル生成

承認後、以下のファイルを生成:

**UI Toolkit の場合:**
- `Assets/UI/[画面名]/[画面名].uxml` — レイアウト定義
- `Assets/UI/[画面名]/[画面名].uss` — スタイルシート
- `Assets/Scripts/UI/[画面名]Controller.cs` — イベントハンドリング・データバインディング

**uGUI の場合:**
- `Assets/Scripts/UI/[画面名]Controller.cs` — MonoBehaviourベースのUI制御
- プレハブはMCP経由またはEditor上で手動構築

### ステップ5: MCP経由でプレビュー（エディタ起動中の場合）

```python
# UIDocumentをシーンに配置して確認
manage_scene(action="screenshot", include_image=True, max_resolution=512)
```

## 画面テンプレート

### HUD（ヘッドアップディスプレイ）
```
常時表示: HP, エネルギー, ミニマップ, アイテムスロット
位置: 画面端に固定
特徴: 透過背景、小さいフォント、アニメーション対応
```

### Menu（メインメニュー）
```
表示: タイトル, 開始/設定/終了ボタン
位置: 画面中央
特徴: フルスクリーン背景、大きいフォント、BGM連動
```

### Dialog（会話ウィンドウ）
```
表示: キャラ名, 会話テキスト, 選択肢
位置: 画面下部
特徴: テキスト送り対応、キャラ立ち絵枠、SE連動
```

### Settings（設定画面）
```
表示: 音量スライダー, 解像度, フルスクリーン切替
位置: 画面中央（オーバーレイ）
特徴: 値の保存/読込、変更即反映
```

### Inventory（インベントリ）
```
表示: アイテムグリッド, 詳細パネル, 装備スロット
位置: 画面中央（オーバーレイ）
特徴: ドラッグ&ドロップ、ソート、フィルタ
```

## テスト

UI機能のテストは Edit Mode で:
- データバインディングの正確性
- イベントハンドラの呼び出し確認
- 表示/非表示の状態遷移

## コード規約

- UIControllerは `MonoBehaviour` + `[RequireComponent(typeof(UIDocument))]`
- UI要素の参照は `rootVisualElement.Q<T>("name")` でクエリ
- スタイルクラスの命名: `kebab-case`（例: `health-bar`, `menu-button`）
- イベント登録は `OnEnable`、解除は `OnDisable`

## 出力先
- `Assets/UI/[画面名]/` — UXML + USS
- `Assets/Scripts/UI/[画面名]Controller.cs` — バッキングスクリプト
- `designs/ui/[画面名].md` — UI構造定義（テキスト）
