# T6: コンポーネント整合性テスト（プリフライト）

## 概要
テスト実行前にシーン構成の正しさを検証する。ここでFailしたら他テストを中断する門番。

## 実行手段
- `/unicli`: GameObject.Find --tag / --name / --requiredComponents
- `/unicli`: GameObject.GetComponents
- `/unicli`: Eval
- `/unicli`: Prefab.GetStatus

## 実行タイミング
- **FULL_WORKFLOW の Step 2.5**（シーンビルド直後、テスト実行前）
- **batch-test のグループ実行前**（全機能共通で1回）

## チェック項目

### 1. キャラクターレイヤー
```bash
# Player = Layer 12
unicli exec Eval --code "Debug.Log(GameObject.FindWithTag(\"Player\").layer);"
# → 12 であること

# Enemy = Layer 13 or 14
unicli exec Eval --code "foreach(var e in GameObject.FindGameObjectsWithTag(\"Enemy\")) Debug.Log(e.name + \":\" + e.layer);"
# → 全て 13 or 14 であること
```

### 2. HitBox レイヤー
```bash
# PlayerHitbox = Layer 10, EnemyHitbox = Layer 11
unicli exec Eval --code "var hbs = GameObject.FindObjectsByType<HitBox>(FindObjectsSortMode.None); foreach(var h in hbs) Debug.Log(h.name + \":\" + h.gameObject.layer);"
```

### 3. 必須コンポーネント存在
```bash
# プレイヤー必須コンポーネント
unicli exec GameObject.GetComponents --name "Player"
# → BaseCharacter, PlayerInputHandler, ActionExecutorController が含まれること

# 敵必須コンポーネント
unicli exec GameObject.GetComponents --tag "Enemy"
# → BaseCharacter, AIBrain が含まれること
```

### 4. GameManager初期化
```bash
unicli exec Eval --code "Debug.Log(GameManager.Instance != null);"
unicli exec Eval --code "Debug.Log(GameManager.Data != null);"
```

### 5. プレハブ整合性
```bash
unicli exec Prefab.GetStatus --name "Player"
# → overrides が意図したものか確認
```

### 6. AutoInputTester設定（T2実行前のみ）
```bash
unicli exec GameObject.Find --requiredComponents "AutoInputTester"
# → 存在すること、enabledであること
```

## 設計指針
- 全チェックをまとめて実行し、全結果を一括報告
- 1つでもFailなら他テストを**中断**（修正コストの無駄を防ぐ）
- Fix提案: どのオブジェクトの何が間違っているかを具体的に報告
- PlayMode進入前にEditModeで実行できるチェックを優先

## 結果判定
- 全チェックPass → 他テストに進む
- 1つでもFail → REPORT_PREFLIGHT_FAILURE → END（他テスト実行しない）
