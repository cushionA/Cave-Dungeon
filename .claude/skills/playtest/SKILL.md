---
name: playtest
description: Run editor play testing workflow - build test scenes, execute auto-input tests, analyze errors, and fix issues. Use when testing feature-db features via editor play, designing auto-input sequences, or debugging runtime issues. Trigger on "playtest", "play test", "editor test", "auto input test", or when verifying game mechanics in editor.
user-invocable: true
argument-hint: [full|scene|auto-input|analyze|fix] [--feature FeatureName] [--combat-only] [--movement-only]
---

# Play Test: $ARGUMENTS

feature-dbに登録された機能をエディタ実行で検証するSkill Graphワークフロー。
エディタ操作は `/unicli` スキル経由で実行する（コマンド詳細は `/unicli` を参照）。

## Skill Graph

```
START
  │
  ├─[arg == "full" or no arg]──→ GRAPH:FULL_WORKFLOW
  │   └─[複数機能指定時]────────→ batch-test.md の GRAPH:BATCH_ORCHESTRATE
  ├─[arg == "scene"]───────────→ GRAPH:SCENE_DESIGN
  ├─[arg == "auto-input"]──────→ GRAPH:AUTO_INPUT_DESIGN
  ├─[arg == "analyze"]─────────→ GRAPH:ANALYZE_ERRORS
  └─[arg == "fix"]─────────────→ GRAPH:FIX_ISSUES
```

---

## GRAPH:FULL_WORKFLOW

完全なプレイテストサイクル。

```
[1. COMPILE_CHECK]
  │ unicli exec Compile
  │ unicli exec Console.GetLog --json
  │
  ├─[compile error]──→ [REPORT_COMPILE_ERROR] → END
  │
  └─[success]──→ [2. SCENE_BUILD]
                   │ `/unicli`: Menu.Execute "Tools/CLIInternal/Build Test Scene"
                   │ `/unicli`: Compile  # 再コンパイル待ち
                   │
                   └──→ [2.5. PREFLIGHT (T6)]
                          │ `/unicli`: GameObject.Find --tag "Player" → レイヤー確認
                          │ `/unicli`: GameObject.Find --tag "Enemy" → レイヤー・コンポーネント確認
                          │ `/unicli`: Eval → GameManager/CharacterRegistry初期化確認
                          │ `/unicli`: Prefab.GetStatus → プレハブ整合性
                          │ Fail時 → [REPORT_PREFLIGHT_FAILURE] → END
                          │
                          └──→ [3. RUN_EDIT_TESTS]
                          │ unicli exec TestRunner.RunEditMode
                          │
                          ├─[test failures]──→ [REPORT_TEST_FAILURE]
                          │                     │ 失敗テストの詳細を分析
                          │                     └──→ [4. RUN_AUTO_INPUT] (続行)
                          │
                          └─[all pass]──→ [4. RUN_AUTO_INPUT]
                                           │ unicli exec Scene.Open --path "Assets/Scenes/CoreTestScene.unity"
                                           │ → GRAPH:AUTO_INPUT_EXECUTE
                                           │
                                           └──→ [5. ANALYZE]
                                                  │ → GRAPH:ANALYZE_ERRORS
                                                  │
                                                  └──→ [5.5. PERF_CHECK (T5)] (任意)
                                                         │ ※ Profiler録画はAutoInput実行前に開始済みの場合のみ分析
                                                         │ `/unicli`: Profiler.StopRecording (録画中の場合)
                                                         │ `/unicli`: Profiler.AnalyzeFrames → フレーム統計
                                                         │ `/unicli`: Profiler.FindSpikes --frameTimeThresholdMs 33 → 30fps割れ検出
                                                         │ 問題あればレポートに追記
                                                         │
                                                         └──→ [6. REPORT]
                                                                │ テスト結果サマリー出力
                                                                │ feature-db更新（下記ルール参照）
                                                                └──→ END
```

---

## GRAPH:SCENE_DESIGN

feature-dbの機能をテストするためのシーン設計とテスト計画を策定する。

```
[1. LOAD_FEATURES]
  │ python tools/feature-db.py list --status complete
  │ python tools/feature-db.py list --status in_progress
  │
  └──→ [1.5. GATHER_FEATURE_CONTEXT]
         │ 各機能の実装詳細を収集（テスト設計の根拠とする）:
         │   python tools/feature-db.py get "機能名" → 実装ファイル・テストファイルパス取得
         │   Read: 実装ファイル → public API、状態変数、依存コンポーネントを把握
         │   Read: 既存テストファイル → カバー済みケースを把握
         │   Read: Architect/ の関連設計文書（SoA構造、アクション仕様等）
         │
         └──→ [1.7. INSPECT_CURRENT_SCENE] (シーンが開いている場合)
         │ `/unicli`: GameObject.GetHierarchy → 現在のシーン構成を取得
         │ `/unicli`: TestRunner.List → 既存テスト一覧を取得
         │ `/unicli`: GameObject.Find --tag "Enemy" → 敵配置確認
         │ `/unicli`: GameObject.Find --requiredComponents "AutoInputTester" → テスター確認
         │
         └──→ [2. MAP_TO_MATRIX]
                │ Read: references/feature-test-matrix.md
                │ 各機能をマトリクスに照合:
                │   - 機能カテゴリを特定
                │   - ◎必須/○推奨/△任意テストを列挙
                │   - 既存テスト（TestRunner.List）でカバー済みのものを除外
                │
                └──→ [3. DESIGN_PROPOSAL]
                       │ 各テスト種別の該当 t*.md を Read して設計:
                       │   - 不足テストケース（T1-T9ごと）
                       │   - シーン環境の変更案（エリア/キャラ/地形/UI要素追加）
                       │
                       ├─[--feature指定あり]──→ 特定機能のテスト計画のみ出力
                       └─[指定なし]──────────→ 全機能のカバレッジギャップを報告
```

**制約**: シーン設計はテスト補助目的のみ。テスト以外の機能追加は禁止。

---

## GRAPH:AUTO_INPUT_DESIGN

テストシーケンスを設計する。AutoInputTester（T2）だけでなく、全テスト種別を組み合わせて設計する。

```
[1. READ_CURRENT_STATE]
  │ Read: Assets/MyAsset/Runtime/Debug/AutoInputTester.cs （現在の12カテゴリ確認）
  │ `/unicli`: TestRunner.List → 既存EditModeテスト一覧
  │ python tools/feature-db.py list → 機能一覧
  │
  └──→ [1.5. GATHER_FEATURE_CONTEXT]
         │ テスト対象機能の実装詳細を収集:
         │   python tools/feature-db.py get "機能名" → 実装ファイルパス取得
         │   Read: 実装ファイル → テスト可能なAPI、状態遷移、入力パターンを把握
         │   Read: 既存テストファイル → カバー済みケース・未カバーのギャップ特定
         │   Read: Architect/ の関連文書 → 仕様上の期待動作を確認
         │
         └──→ [2. MAP_FEATURES_TO_TESTS]
         │ Read: references/feature-test-matrix.md
         │   - 各機能の◎必須テスト種別を列挙
         │   - 既存テストでカバー済みのものを除外
         │   - 未カバーのテスト種別を Gap として報告
         │
         ├─[--combat-only]──→ 攻撃系/防御系カテゴリのみ
         ├─[--movement-only]─→ 移動系カテゴリのみ
         │
         └──→ [3. DESIGN_PER_TYPE]
                │ 必要なテスト種別の t*.md を Read して設計:
                │   - T1: EditModeテストコード案
                │   - T2: AutoInputTester追加コード案（TestStep定義）
                │   - T3-T9: FULL_WORKFLOW / ANALYZE に組み込む検証手順
                │
                └──→ [4. OUTPUT]
                       │ テスト種別ごとの設計結果を出力
                       └──→ END
```

---

## GRAPH:AUTO_INPUT_EXECUTE

AutoInputTestを実行する。

**完了検出**: AutoInputTesterは全周回終了時に以下のログを出力する:
```
[AutoInputTester] 全{N}周完了: TOTAL PASS={X} TOTAL FAIL={Y}
```
この `全` + `周完了` パターンでマッチする。

```
[1. CONFIGURE]
  │ テスト対象に応じてAutoInputTesterの設定を選択:
  │ ※ Menu.Execute は EditorMode で呼ぶこと（PlayMode中はNG）
  │ ※ ConfigureAutoInputTester がシーンを自動保存する
  │
  ├─[--combat-only]──→ unicli exec Menu.Execute --menuPath "Tools/CLIInternal/Run Auto Input Combat"
  ├─[--movement-only]→ unicli exec Menu.Execute --menuPath "Tools/CLIInternal/Run Auto Input Movement"
  └─[全テスト]────────→ unicli exec Menu.Execute --menuPath "Tools/CLIInternal/Run Auto Input All"
  │
  └──→ [2. ENTER_PLAY]
         │ `/unicli`: PlayMode.Enter
         │
         └──→ [3. WAIT_COMPLETION]
                │ # ポーリング: 10秒間隔、最大180秒
                │ `/unicli`: PlayMode.Status → PlayMode中か確認
                │ `/unicli`: Console.GetLog → ログ確認
                │
                │ 完了判定: ログに「全」+「周完了」を含む行があるか
                │
                ├─[完了ログ検出]──→ [4. CAPTURE_AND_EXIT]
                │                    │ `/unicli`: Screenshot.Capture (ビジュアルエビデンス)
                │                    │ `/unicli`: PlayMode.Exit
                │                    │ Read: auto-input-test-log.txt
                │                    └──→ RETURN (結果を返す)
                │
                ├─[エラー検出]──→ [4. CAPTURE_AND_EXIT] (エラー内容も含めて返す)
                │
                └─[未完了]──→ [3. WAIT_COMPLETION] (リトライ、最大180秒でタイムアウト)
                               │ タイムアウト時: PlayMode.Exit → コンソールログを返してエラー報告
```

---

## GRAPH:ANALYZE_ERRORS

エラーログを分析して問題を分類する。

```
[1. COLLECT_LOGS]
  │ `/unicli`: Console.GetLog
  │ Read: auto-input-test-log.txt (存在する場合)
  │
  └──→ [2. INSPECT_SCENE]
         │ ログだけでなくシーン状態を直接検証する:
         │ `/unicli`: GameObject.Find → キャラクターの存在・レイヤー確認
         │ `/unicli`: GameObject.GetComponents → コンポーネント構成確認
         │ `/unicli`: Eval → SoAデータ・状態値の直接読み取り
         │   例: Eval --code "Debug.Log(GameObject.Find(\"Player\").layer);"
         │   例: Eval --code "Debug.Log(GameManager.Data != null);"
         │ `/unicli`: Animator.Inspect → アニメーション状態確認（必要時）
         │ `/unicli`: Prefab.GetStatus → プレハブ整合性確認（必要時）
         │
         └──→ [3. CATEGORIZE]
                │ エラーを分類:
                │
                │ A. コンパイルエラー → スクリプト修正が必要
                │ B. ランタイムエラー → 参照切れ・コンポーネント未アタッチ
                │ C. 物理/レイヤー問題 → CollisionMatrix設定ミス、レイヤー不一致
                │ D. AI行動問題 → AIInfo設定ミス、BridgeAIAction未発火
                │ E. 入力問題 → PlayerInputHandler設定ミス、バッファタイミング
                │ F. ロジックエラー → SoAデータ不整合、状態遷移バグ
                │ G. パフォーマンス問題 → スパイク、GCアロケーション
                │
                └──→ [4. ROOT_CAUSE]
                       │ 各エラーの根本原因を特定
                       │ references/known-issues.md の既知パターンと照合
                       │ 関連ソースファイルを Read で確認
                       │
                       └──→ [5. REPORT]
                              │ 構造化レポート出力
                              └──→ END
```

---

## GRAPH:FIX_ISSUES

分析結果に基づいて修正を適用する。**リトライ上限: 3回**。超過時はユーザーに報告して終了。

```
[1. READ_ANALYSIS]
  │ 直前のANALYZE_ERRORS結果を参照
  │ OR コンソールログから問題を再特定
  │ retryCount = 0
  │
  └──→ [2. PLAN_FIX]
         │ 修正計画を立案:
         │   - 影響範囲の確認
         │   - 既存テストへの影響
         │   - 最小限の変更で修正可能か
         │
         └──→ [3. APPLY_FIX]
                │ コード修正を適用
                │ retryCount++
                │
                ├─[retryCount > 3]──→ [ESCALATE]
                │                      │ 修正試行3回超過。変更をrevert提案し、
                │                      │ 問題の詳細をユーザーに報告して判断を仰ぐ
                │                      └──→ END
                │
                └──→ [4. VERIFY]
                       │ `/unicli`: Compile
                       │
                       ├─[compile error]──→ [3. APPLY_FIX] (修正を調整)
                       │
                       └─[success]──→ [5. TEST]
                                       │ `/unicli`: TestRunner.RunEditMode
                                       │ `/unicli`: GameObject.Find + GetComponents で修正対象を直接検証
                                       │ `/unicli`: Eval で状態値を確認（必要時）
                                       │
                                       ├─[test failure]──→ [3. APPLY_FIX] (修正を調整)
                                       │
                                       └─[all pass]──→ [6. DONE]
                                                        │ 修正内容をサマリー出力
                                                        └──→ END
```

---

## テスト種別と機能マッピング

9種のテストタイプ（T1-T9）と、機能カテゴリごとの適用マトリクスで構成される。

### テスト種別一覧（詳細は各 references/test-types/t*.md を参照）

| ID | テスト種別 | 実行モード | 詳細 |
|----|-----------|-----------|------|
| T1 | EditModeロジック | EditMode | `references/test-types/t1-logic.md` |
| T2 | AutoInput動作 | PlayMode | `references/test-types/t2-auto-input.md` |
| T3 | シーン状態スナップショット | PlayMode | `references/test-types/t3-snapshot.md` |
| T4 | Animator状態 | PlayMode | `references/test-types/t4-animator.md` |
| T5 | パフォーマンス回帰 | PlayMode | `references/test-types/t5-performance.md` |
| T6 | プリフライト（門番） | EditMode | `references/test-types/t6-preflight.md` |
| T7 | 動的シーン操作 | PlayMode | `references/test-types/t7-dynamic.md` |
| T8 | UI検証 | PlayMode | `references/test-types/t8-ui.md` |
| T9 | スクリーンショット | PlayMode | `references/test-types/t9-screenshot.md` |

### 機能-テスト組み合わせマトリクス

詳細: `references/feature-test-matrix.md`

機能カテゴリ（移動系/攻撃系/UI系 等）ごとに◎必須/○推奨/△任意のテストタイプを定義。
SCENE_DESIGN / AUTO_INPUT_DESIGN / batch-test はこのマトリクスを参照してテスト計画を立てる。

### 適用ルール
1. **◎が1つでもFail → feature-db: in_progress**
2. **○のみFail → complete可能、レポートに警告**
3. **△ → 時間が許す場合のみ**
4. **T6 Fail → 他テスト中断**

### バッチテスト（複数機能一括）

複数機能を同時テストする場合は `batch-test.md` のワークフローを使用。
テスト種別ごとにグルーピングし、PlayMode Enter/Exit を最小化する。

## feature-db 更新ルール

GRAPH:FULL_WORKFLOW の Step 6 で、テスト結果に基づいて feature-db を更新する。

### 更新判定

1. **feature-db から対象機能を取得**: `python tools/feature-db.py list` で全機能一覧を取得
2. **テスト結果と照合**: AutoInputTest/EditModeテストの結果を機能ごとに分類
3. **ステータス更新**:
   - 対象機能の全テストPass → `python tools/feature-db.py update "機能名" --status complete --test-passed N --test-failed 0`
   - 失敗あり → `python tools/feature-db.py update "機能名" --status in_progress --test-passed N --test-failed M`
   - テスト対象外（テストが存在しない）→ 更新しない（SCENE_DESIGNでギャップとして報告）

### 注意事項
- AutoInputTestの結果はPlayMode統合テストであり、個別機能のステータスに直接マッピングしにくい場合がある
- その場合はテストレポートに「手動確認推奨」として記載し、feature-dbは変更しない

## UniCli利用規約

エディタ操作はすべて `/unicli` スキル経由で実行する。コマンドの詳細・パラメータは `/unicli` を参照。

- **接続確認**: ワークフロー開始時に `unicli check` → 失敗時はユーザーに `/unicli` でのセットアップを案内
- **フォールバック**: unicli不可時は unity-mcp MCP ツール群を使用
- **Menu.Execute**: ダイアログなしメニュー項目は `Tools/CLIInternal/` 配下
- **Compile**: コード変更後は必ず実行
- **分析時は受動的ログ解析だけでなく、`Eval`/`GameObject.*`/`Profiler.*` で能動的に検証する**

## 参考資料

- `references/known-issues.md` — 過去のセッションで発見した問題と解決策
- `references/test-scene-architecture.md` — TestSceneBuilder のエリア構成と配線
- `references/auto-input-patterns.md` — AutoInputTester のテストパターン設計
