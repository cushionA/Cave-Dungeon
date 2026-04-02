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
                   │ unicli exec Menu.Execute --menuPath "Tools/CLIInternal/Build Test Scene"
                   │ unicli exec Compile  # 再コンパイル待ち
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
                                                  └──→ [5.5. PERF_CHECK] (任意)
                                                         │ `/unicli`: Profiler.AnalyzeFrames でフレーム統計取得
                                                         │ `/unicli`: Profiler.FindSpikes でスパイク検出
                                                         │ 問題あればレポートに追記
                                                         │
                                                         └──→ [6. REPORT]
                                                                │ テスト結果サマリー出力
                                                                │ feature-db更新（下記ルール参照）
                                                                └──→ END
```

---

## GRAPH:SCENE_DESIGN

feature-dbの機能をテストするためのシーン設計を検討する。

```
[1. LOAD_FEATURES]
  │ python tools/feature-db.py list --status complete
  │ python tools/feature-db.py list --status in_progress
  │
  └──→ [1.5. INSPECT_CURRENT_SCENE] (シーンが開いている場合)
         │ `/unicli`: GameObject.GetHierarchy → 現在のシーン構成を取得
         │ `/unicli`: TestRunner.List → 既存テスト一覧を取得
         │
         └──→ [2. ANALYZE_COVERAGE]
                │ 各機能に必要なテスト環境を分析:
                │   - 必要なキャラクター構成（Player/Enemy/Companion）
                │   - 必要な地形（平地/段差/壁/ギャップ）
                │   - 必要な入力シーケンス
                │   - 必要なAI設定（AIInfo）
         │
         └──→ [3. DESIGN_PROPOSAL]
                │ 提案内容:
                │   - TestSceneBuilderへの変更案
                │   - AutoInputTesterへのテストケース追加案
                │   - 新規AIInfoアセットの必要性
                │
                ├─[--feature指定あり]──→ 特定機能のテスト環境のみ設計
                └─[指定なし]──────────→ 全機能のカバレッジギャップを報告
```

**制約**: シーン設計はテスト補助目的のみ。テスト以外の機能追加は禁止。

---

## GRAPH:AUTO_INPUT_DESIGN

AutoInputTesterのテストシーケンスを設計・設定する。

```
[1. READ_CURRENT_TESTS]
  │ Read: Assets/MyAsset/Runtime/Debug/AutoInputTester.cs
  │ 現在のテストカテゴリ12種を確認:
  │   Move, Jump, LightAttack, HeavyAttack, Skill, Dodge,
  │   Sprint, Guard, Buttons, Stamina, AerialAttack, Composite
  │
  └──→ [2. IDENTIFY_GAPS]
         │ feature-dbの機能一覧と照合
         │ テストされていない入力パターンを特定:
         │   - チャージ攻撃（長押し→リリース）
         │   - ガード中の被弾
         │   - 連携ボタン
         │   - 武器切り替え中の攻撃
         │
         ├─[--combat-only]──→ 戦闘系テストのみ設計
         ├─[--movement-only]─→ 移動系テストのみ設計
         │
         └──→ [3. GENERATE_SEQUENCE]
                │ テストステップをMovementInfo構造体の値として設計
                │
                │ 設計テンプレート:
                │   TestStep {
                │     name: "テスト名",
                │     duration: 秒数,
                │     input: MovementInfo { moveDirection, jumpPressed, ... },
                │     validation: "検証内容の説明"
                │   }
                │
                └──→ [4. OUTPUT]
                       │ AutoInputTester.csへの追加コード案を出力
                       │ OR CLIInternal経由での設定変更手順を出力
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

## テスト有効性の判断基準

feature-dbの機能に対して、以下の観点でテストの有効性を評価する:

### 必須テスト（EditMode）
- **データ変換テスト**: InputConverter, AIInfoConverter, CombatDataHelper
- **ダメージ計算テスト**: HpArmorLogic, GuardJudgmentLogic, HitReactionLogic
- **AI判定テスト**: ConditionEvaluator, TargetSelector, JudgmentLoop
- **状態管理テスト**: ActionExecutor, ChargeAttackLogic, GroundMovementLogic

### 推奨テスト（PlayMode / AutoInput）
- **物理連携テスト**: 接地判定、レイヤー衝突、ヒットボックス到達
- **タイミングテスト**: コンボウィンドウ、チャージ閾値、入力バッファ
- **統合テスト**: 攻撃→ヒット→ダメージ→リアクション の一連フロー
- **AI行動テスト**: モード遷移、ターゲット選択、攻撃実行

### テスト不要（検証コスト > 価値）
- UIレイアウトの正確性（目視確認で十分）
- アニメーション遷移の滑らかさ（主観的）
- BGM/SE の再生タイミング（人間が確認）

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
