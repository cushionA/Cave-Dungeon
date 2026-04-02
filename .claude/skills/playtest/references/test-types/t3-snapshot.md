# T3: シーン状態スナップショットテスト

## 概要
PlayMode中のゲーム状態を `/unicli` Eval / GameObject.* で外部から数値的に検証する。
AutoInputやDynamicテストの「検証パート」として組み合わせて使う。

## 実行手段
- `/unicli`: Eval --code "C#式"
- `/unicli`: GameObject.Find --name "オブジェクト名"
- `/unicli`: GameObject.GetComponents --name "オブジェクト名"

## 対象
- ゲーム状態の数値検証（HP、MP、スタミナ、スコア等）
- オブジェクト存在確認（敵が倒されたか、アイテムが消えたか）
- コンポーネント状態（enabled/disabled、プロパティ値）
- SoAデータコンテナの値

## 検証パターン

### HP/リソース値の取得
```bash
unicli exec Eval --code "Debug.Log(GameManager.Data.GetVitals(hash).hp);"
```

### レイヤー確認
```bash
unicli exec Eval --code "Debug.Log(GameObject.Find(\"Player\").layer);"
```

### コンポーネント状態
```bash
unicli exec GameObject.GetComponents --name "Player"
```

### SoAデータ確認
```bash
unicli exec Eval --code "Debug.Log(GameManager.Data != null);"
unicli exec Eval --code "var go = GameObject.Find(\"Player\"); Debug.Log(GameManager.Data.GetVitals(go.GetInstanceID()).hp);"
```

### オブジェクト数
```bash
unicli exec Eval --code "Debug.Log(GameObject.FindGameObjectsWithTag(\"Enemy\").Length);"
```

## 設計指針
- 1スナップショット = 1つの検証項目（複雑なEvalは分割）
- 期待値と実測値を明示的に比較
- PlayMode中のタイミングに注意: FixedUpdate完了後に取得すること
- 複数フレーム待ちが必要な場合はAutoInput（T2）と組み合わせる

## 結果判定
- 期待値と一致 → Pass
- 不一致 → Fail（期待値・実測値をレポートに記載）
