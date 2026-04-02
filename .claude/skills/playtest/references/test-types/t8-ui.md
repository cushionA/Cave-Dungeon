# T8: UIテスト

## 概要
UI要素の存在・値・表示状態を `/unicli` Eval で検証する。
UI Toolkit（UXML/USS）と uGUI（Canvas）の両方に対応。

## 実行手段
- `/unicli`: Eval --code "C#式"（UI要素アクセス）
- `/unicli`: GameObject.Find --name "Canvas/..." （uGUI階層）
- `/unicli`: Screenshot.Capture（ビジュアルエビデンス）

## 対象
- HUD（HPバー、MPバー、スタミナバー）
- ダメージポップアップ
- メニュー（ポーズ、インベントリ、設定）
- ダイアログ（会話ウィンドウ）
- ミニマップ
- ボタン反応・ナビゲーション

## UI Toolkit 検証パターン

### 要素の存在確認
```bash
unicli exec Eval --code "var doc = GameObject.FindFirstObjectByType<UIDocument>(); Debug.Log(doc.rootVisualElement.Q<Label>(\"hp-label\") != null);"
```

### テキスト値の取得
```bash
unicli exec Eval --code "var doc = GameObject.FindFirstObjectByType<UIDocument>(); Debug.Log(doc.rootVisualElement.Q<Label>(\"hp-label\").text);"
```

### 表示状態
```bash
unicli exec Eval --code "var doc = GameObject.FindFirstObjectByType<UIDocument>(); var el = doc.rootVisualElement.Q(\"pause-menu\"); Debug.Log(el.resolvedStyle.display);"
```

### スタイル値
```bash
unicli exec Eval --code "var doc = GameObject.FindFirstObjectByType<UIDocument>(); var bar = doc.rootVisualElement.Q(\"hp-bar-fill\"); Debug.Log(bar.resolvedStyle.width);"
```

## uGUI 検証パターン

### Slider値（HPバー等）
```bash
unicli exec Eval --code "Debug.Log(GameObject.Find(\"Canvas/HPBar\").GetComponent<UnityEngine.UI.Slider>().value);"
```

### Text値
```bash
unicli exec Eval --code "Debug.Log(GameObject.Find(\"Canvas/ScoreText\").GetComponent<TMPro.TextMeshProUGUI>().text);"
```

### アクティブ状態
```bash
unicli exec Eval --code "Debug.Log(GameObject.Find(\"Canvas/PauseMenu\").activeSelf);"
```

### ボタン有効/無効
```bash
unicli exec Eval --code "Debug.Log(GameObject.Find(\"Canvas/AttackButton\").GetComponent<UnityEngine.UI.Button>().interactable);"
```

## テストシナリオ例

### HUD更新テスト
1. T3 でプレイヤーHP値を取得
2. T7 でダメージを与える（Component.SetProperty or AutoInput攻撃）
3. 数フレーム待ち
4. Eval で HPバーの表示値を取得
5. 内部HP値とHPバー表示値が一致するか検証

### メニュー表示テスト
1. AutoInput（T2）でメニューボタン入力（menuPressed）
2. Eval で PauseMenu の activeSelf / display を確認
3. Screenshot.Capture でエビデンス取得
4. 再度メニュー入力で閉じる → 非表示確認

### ダメージポップアップテスト
1. 攻撃ヒット前のポップアップ数を Eval で取得
2. AutoInput で攻撃ヒット
3. ポップアップが生成されたか確認（FindGameObjectsWithTag or 子オブジェクト数）
4. ポップアップのテキストがダメージ値と一致するか

## 設計指針
- UI Toolkit: `Q<T>("name")` / `Q("name")` でCSSセレクタ風にアクセス
- uGUI: `GameObject.Find("Canvas/階層/パス")` でアクセス
- Evalのコードは1行で完結させる（複数行は `;` で区切り）
- 数値比較は丸め誤差を考慮（float比較にはイプシロン使用）
- UI更新はフレーム遅延あり → 数フレーム待ってから検証

## 結果判定
- UI値が内部状態と一致 → Pass
- 表示/非表示が期待通り → Pass
- 不一致、要素不在 → Fail（期待値・実測値をレポートに記載）
