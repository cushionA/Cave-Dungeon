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
[Phase 1: PREFLIGHT — EditMode, 1回]
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
                       │ ※ PlayMode Enter は1回だけ
                       │
                       │ [3a. PROFILER_START] (T5対象機能がある場合)
                       │   Read: references/test-types/t5-performance.md
                       │   `/unicli`: Profiler.StartRecording
                       │
                       │ [3b. AUTO_INPUT] (T2対象機能がある場合)
                       │   Read: references/test-types/t2-auto-input.md
                       │   テスト設定 → PlayMode.Enter → ポーリング → 完了待ち
                       │   ※ GRAPH:AUTO_INPUT_EXECUTE と同じフロー
                       │
                       │ [3c. SNAPSHOT] (T3対象機能がある場合)
                       │   Read: references/test-types/t3-snapshot.md
                       │   AutoInput完了後（またはDynamic操作後）にEvalで状態取得
                       │
                       │ [3d. ANIMATOR] (T4対象機能がある場合)
                       │   Read: references/test-types/t4-animator.md
                       │   `/unicli`: Animator.Inspect で各キャラの状態確認
                       │
                       │ [3e. DYNAMIC] (T7対象機能がある場合)
                       │   Read: references/test-types/t7-dynamic.md
                       │   シーン操作 → 検証 → 状態復元
                       │
                       │ [3f. UI] (T8対象機能がある場合)
                       │   Read: references/test-types/t8-ui.md
                       │   Eval で UI要素の値・状態を検証
                       │
                       │ [3g. SCREENSHOT] (T9対象機能がある場合)
                       │   Read: references/test-types/t9-screenshot.md
                       │   `/unicli`: Screenshot.Capture
                       │
                       │ [3h. PROFILER_STOP] (T5対象機能がある場合)
                       │   `/unicli`: Profiler.StopRecording
                       │   `/unicli`: Profiler.AnalyzeFrames
                       │   `/unicli`: Profiler.FindSpikes --frameTimeThresholdMs 33
                       │
                       │ [3i. EXIT]
                       │   `/unicli`: PlayMode.Exit
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
                       │
                       │ === Batch Test Report ===
                       │ Features tested: N
                       │ Total: Pass M / Fail K / Warning W
                       │
                       │ ### Result Matrix
                       │ (上記マトリクス表)
                       │
                       │ ### Issues
                       │ 1. [機能名][Tx] 問題詳細
                       │    - Root cause: ...
                       │    - Fix: applied / proposed / needs-investigation
                       │
                       │ ### Performance (T5, 該当時)
                       │ - Avg frame time: N ms
                       │ - Spikes: ...
                       │
                       │ ### Evidence (T9)
                       │ - screenshot paths
                       │
                       │ ### Coverage Gaps
                       │ - [機能名]: T* テストが未作成
                       │
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
