# バッチテストオーケストレーション

複数機能を一括テストする際のワークフロー。
テスト種別ごとにグルーピングし、PlayMode Enter/Exit やコンパイルチェックの回数を最小化する。

## 前提
- 各テスト種別の詳細: `references/test-types/t*.md`
- 機能→テストタイプのマッピング: `references/feature-test-matrix.md`
- エディタ操作: `/unicli` スキル経由

## Skill Graph

```
START
  │ 入力: features[] (テスト対象機能リスト)
  │
  └──→ GRAPH:BATCH_ORCHESTRATE
```

---

## GRAPH:BATCH_ORCHESTRATE

```
[1. CLASSIFY]
  │ 各機能を feature-test-matrix.md に照合:
  │   python tools/feature-db.py list → 機能一覧取得
  │   各機能名からカテゴリを判定（matrix.md のカテゴリ判定方法）
  │   カテゴリ別に◎必須/○推奨/△任意テストを列挙
  │
  └──→ [1.5. GATHER_FEATURE_CONTEXT]
         │ 各機能の実装詳細を収集（テスト設計の根拠とする）:
         │   python tools/feature-db.py get "機能名" → 実装ファイル・テストファイルパス取得
         │   Read: 実装ファイル → public API、状態変数、依存コンポーネントを把握
         │   Read: 既存テストファイル → カバー済みケースを把握
         │   Read: Architect/ の関連設計文書（必要時のみ）
         │
         └──→ [2. GROUP_BY_TYPE]
         │ テスト種別ごとに対象機能をグルーピング:
         │
         │ group_T6_preflight = [全機能]  ← 常に全機能対象
         │ group_T1_logic     = [◎/○の機能リスト]
         │ group_T4_animator  = [◎/○の機能リスト]
         │ group_playmode     = {
         │   T2: [◎/○の機能リスト],
         │   T3: [◎/○の機能リスト],
         │   T5: [◎/○の機能リスト],
         │   T7: [◎/○の機能リスト],
         │   T8: [◎/○の機能リスト],
         │   T9: [◎/○の機能リスト]
         │ }
         │
         └──→ [3. EXECUTE_GROUPS]
                │
                └──→ GRAPH:BATCH_EXECUTE
```

---

## GRAPH:BATCH_EXECUTE

```
[Phase 0: CONNECTION_CHECK]
  │ unicli check → 接続確認
  │ ├─[失敗]──→ ユーザーに `/unicli` セットアップを案内 → END
  │ └─[成功]──→ 続行
  │
  └──→ [Phase 1: PREFLIGHT — EditMode, 1回]
         │ Read: references/test-types/t6-preflight.md
         │ `/unicli`: Compile → コンパイル確認
         │ `/unicli`: Console.GetLog → エラー確認
         │ 全機能のシーン構成をT6チェック:
         │   - キャラクターレイヤー
         │   - 必須コンポーネント
         │   - GameManager初期化
         │   - プレハブ整合性
         │
         ├─[Fail]──→ [REPORT_PREFLIGHT] → END
         │             ※ 修正が必要な項目を一覧報告
         │
         └─[Pass]──→ [Phase 2: LOGIC — EditMode, 1回]
                       │ Read: references/test-types/t1-logic.md
                       │ `/unicli`: TestRunner.RunEditMode
                       │   → group_T1_logic の全機能テストを一括実行
                       │ 結果を機能ごとに分類して記録
                       │
                       └──→ [Phase 3: PLAYMODE_SESSION — PlayMode, 1セッション]
                              │
                              │ [3a. CONFIGURE — EditMode]
                              │   AutoInputTester設定（Menu.Execute）※ EditModeで実行すること
                              │   `/unicli`: Scene.Open（テストシーン）
                              │
                              │ [3b. PROFILER_START] (T5対象機能がある場合)
                              │   Read: references/test-types/t5-performance.md
                              │   `/unicli`: Profiler.StartRecording
                              │
                              │ [3c. ENTER_PLAY]
                              │   `/unicli`: PlayMode.Enter ← ★ここでのみEnterする
                              │
                              │ [3d. AUTO_INPUT_WAIT] (T2対象機能がある場合)
                              │   Read: references/test-types/t2-auto-input.md
                              │   ポーリング: 10秒間隔、最大180秒
                              │   `/unicli`: PlayMode.Status + Console.GetLog
                              │   完了判定: 「全」+「周完了」パターン
                              │   ├─[unicli応答なし]→ unicli check で確認
                              │   │   └─[応答なし]→ Unityクラッシュ報告 → END
                              │   └─[完了/タイムアウト]→ 続行
                              │
                              │ [3e. IN-PLAY CHECKS] (PlayMode中)
                              │   T3 Snapshot: Eval で状態値検証（該当時）
                              │   T4 Animator: Animator.Inspect で状態確認（該当時）
                              │   T7 Dynamic: シーン操作→検証→状態復元（該当時）
                              │   T8 UI: Eval で UI要素検証（該当時）
                              │   T9 Screenshot: Screenshot.Capture（該当時）
                              │
                              │ [3f. PROFILER_STOP] (T5対象機能がある場合)
                              │   `/unicli`: Profiler.StopRecording
                              │   `/unicli`: Profiler.AnalyzeFrames
                              │   `/unicli`: Profiler.FindSpikes --frameTimeThresholdMs 33
                              │
                              │ [3g. RUN_PLAYMODE_TESTS]
                              │   `/unicli`: TestRunner.RunPlayMode（PlayModeテストが存在する場合）
                              │
                              │ [3h. EXIT_PLAY]
                              │   `/unicli`: Screenshot.Capture（最終エビデンス）
                              │   `/unicli`: PlayMode.Exit ← ★ここでのみExitする
                              │
                              └──→ [Phase 4: AGGREGATE]
                                     │ → GRAPH:BATCH_REPORT
```

---

## GRAPH:BATCH_REPORT

```
[1. COLLECT_RESULTS]
  │ 各フェーズの結果を集約
  │
  └──→ [2. BUILD_MATRIX]
         │ 機能 × テストタイプの結果マトリクスを構築:
         │
         │ | 機能名 | T1 | T2 | T3 | T4 | T5 | T6 | T7 | T8 | T9 | 判定 |
         │ |--------|----|----|----|----|----|----|----|----|----|----|
         │ | Movement | ✅ | ✅ | ✅ | — | — | ✅ | — | — | — | Pass |
         │ | Combat   | ✅ | ❌ | ✅ | ✅ | — | ✅ | ✅ | — | — | Fail |
         │ | HPBar    | — | — | ✅ | — | — | ✅ | — | ✅ | ✅ | Pass |
         │
         │ 凡例: ✅ Pass / ❌ Fail / ⚠ Warning / — Skip / 🔲 未実行
         │
         └──→ [3. FEATURE_DB_UPDATE]
                │ マトリクスの判定列に基づいて feature-db を更新:
                │
                │ 判定ルール（feature-test-matrix.md の適用ルール参照）:
                │   - ◎テストが全Pass → complete
                │   - ◎テストに1つでもFail → in_progress
                │   - ○テストのみFail → complete (警告付き)
                │
                │ python tools/feature-db.py update "機能名" --status <status> --test-passed N --test-failed M
                │
                └──→ [4. OUTPUT_REPORT]
                       │ SKILL.md の「レポートテンプレート（統一フォーマット）」に従い出力
                       │ Mode: "Batch" を設定
                       └──→ END
```

---

## 使い方

### 全機能テスト
```
/playtest full
```
→ feature-dbの全complete/in_progress機能をバッチテスト

### 特定機能群のテスト
```
/playtest full --feature Movement --feature Combat --feature HPSystem
```
→ 指定機能のみバッチテスト

### カテゴリ指定
```
/playtest full --combat-only
```
→ 攻撃系+防御系カテゴリの機能をバッチテスト

## 注意事項
- PlayMode セッションは**1回**に統合する（Enter/Exitの回数を最小化）
- T6 Preflight がFailしたら**即座に中断**（他テスト実行しない）
- Fix試行が3回失敗したらユーザーにエスカレーション
- 根本原因が複数システムにまたがる場合、修正を試みず分析結果のみ報告
