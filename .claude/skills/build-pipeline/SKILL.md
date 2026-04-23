---
name: build-pipeline
description: Orchestrate the full game creation pipeline from concept to implementation. Chains design, planning, and implementation skills automatically.
user-invocable: true
argument-hint: <game concept or "continue">
---

# Build Pipeline: $ARGUMENTS

ゲームコンセプトから設計→計画→実装まで進行する。
**人間との対話を重視**し、各フェーズでユーザー確認を挟む。
"continue" が渡された場合は現在の進行状態から再開する。

## パイプラインの責任範囲

1. **設計補助** — 対話型要件整理、ジャンル調査、ワールド設定、共通設計、asmdef設計
2. **機能単位の実装と管理** — TDD実装、feature-db管理、重複検出
3. **テストの設計と実行** — 単体/結合テスト、MCP経由テスト、コンソール監視
4. **その他制作補助** — HTMLマップ案、パラメータ設計、図表、デバッグ支援等

### パイプラインが**やらない**こと
- タイル配置によるステージ自動生成（人間が主導）
- Unityシーンの自動構築（MCP経由の検証・補助のみ）
- ユーザー確認なしの自律進行

## 進行状態の管理

`designs/pipeline-state.json` で全体の進行状態を追跡する:

```json
{
  "phase": "idle|design|planning|implementation|code-review|review",
  "currentSection": 1,
  "totalSections": null,
  "currentBranch": "feature/pipeline-xxx",
  "completedFeatures": [],
  "pendingFeatures": [],
  "skippedFeatures": [],
  "failedAttempts": {},
  "lastAction": "説明",
  "lastUpdated": "2026-03-15T12:00:00Z"
}
```

- `phase: "idle"` はパイプライン待機中（初期値）。`/build-pipeline <コンセプト>` 開始時に `"design"` に遷移する
- `phase: "planning"` は旧仕様で `/plan-sprint` のフェーズだったが、現在は `design-systems` が機能分解まで担当するため**実装フェーズと接続する中間状態**として使用する

### 状態の読み書き手順

**読み込み**（パイプライン開始時・`continue` 時）:
```bash
python - <<'PY'
import json, pathlib
p = pathlib.Path("designs/pipeline-state.json")
state = json.loads(p.read_text(encoding="utf-8"))
print(state["phase"], state.get("currentSection"))
PY
```

**更新**（各フェーズ遷移時）:
```bash
python - <<'PY'
import json, pathlib, datetime
p = pathlib.Path("designs/pipeline-state.json")
state = json.loads(p.read_text(encoding="utf-8"))
state["phase"] = "implementation"  # 適宜変更
state["lastAction"] = "section-1 の機能分解完了"
state["lastUpdated"] = datetime.datetime.utcnow().isoformat() + "Z"
p.write_text(json.dumps(state, ensure_ascii=False, indent=2), encoding="utf-8")
PY
```

### 書き込みタイミング（必須）

| タイミング | 更新するフィールド |
|-----------|------------------|
| パイプライン開始 | `phase`, `currentBranch`, `lastAction`, `lastUpdated` |
| GDD 確定 | `totalSections`, `phase` → `"design"` 継続 |
| セクション設計完了 | `phase` → `"implementation"`, `pendingFeatures` を feature-db から取得して設定 |
| 機能完了 | `completedFeatures` に追記、`pendingFeatures` から削除、`lastAction`, `lastUpdated` |
| 機能失敗 3 回 | `skippedFeatures` に移動、`failedAttempts[name]` = 3 |
| セクション完了 | `currentSection` インクリメント、`phase` → 次セクションの `"design"` |
| パイプライン完了 | `phase` → `"idle"`, `currentBranch` → null |

## データ整合ルール

- **feature-db がSource of Truth**: 機能の状態はfeature-dbが正
- **pipeline-state.json はキャッシュ**: 進行位置を追跡するためのもの
- **整合チェック**: 各フェーズ遷移時にfeature-dbと照合
  - `python tools/feature-db.py list --status complete` の結果と `completedFeatures` を突合
  - 乖離があれば feature-db を正として state を補正し、警告を表示する

## パイプライン全体フロー

### Phase 1: 設計 (design)

1. `/design-game` を実行
   - 対話型要件整理（コンセプト→GDD）
   - ワールド設定（asset-spec.json）
   - ジャンル調査→不足機能提案
2. ユーザーにGDDを提示し確認 → **承認待ち**
3. `/design-systems section-1` を実行
   - 既存機能との照合
   - 共通設計の抽出
   - asmdef設計
   - システム設計書作成
4. ユーザーにシステム設計を提示し確認 → **承認待ち**
5. `pipeline-state.json` を更新: `phase: "planning"`

### Phase 2: 計画 (planning)

旧 `/plan-sprint` は `/design-systems` に統合済み（2026-04-24）。
Phase 1 step 3 の `/design-systems` 実行内で以下が完了している:

- 既存機能との重複チェック
- 機能のカテゴリ分類（system/content）
- 依存解決・実装順序決定
- feature-db 登録
- スプリント計画出力（`designs/sprints/[セクション名].md`）

ユーザー承認後、`pipeline-state.json` を更新: `phase: "implementation"`

### Phase 3: 実装 (implementation)

`pendingFeatures` の先頭から順に:

1. `/create-feature {featureName}` を実行
2. テスト通過を確認
3. `completedFeatures` に移動
4. `pipeline-state.json` を更新
5. Git コミット + プッシュ
6. 次の機能へ → `pendingFeatures` が空になるまで繰り返す

全機能完了後:
- `python tools/feature-db.py summary` で進捗サマリー出力
- ユーザーに動作確認を案内

### Phase 4: レビュー (review)

セクション実装完了後、2段階のレビューを実施する。

#### Phase 4a: コードレビュー（自動）

`/simplify` を実行し、セクション内の全変更コードをレビュー・修正する。
修正後はテスト全体を再実行してリグレッションがないことを確認する。

#### Phase 4b: 検証と報告

1. テスト全体を実行（MCP経由 `run_tests` またはCLI）
2. コンソールエラーを確認（MCP経由 `read_console`）
3. 結果をユーザーに報告
4. 人間作業のリストアップ:
   - 未配置アセット: `python tools/feature-db.py assets --status pending`
   - アニメーション設定
   - ビジュアル調整
   - ゲームフィール調整（ScriptableObjectの値）

### セクション完了 → 次セクションへ

1. `currentSection` をインクリメント
2. `currentSection <= totalSections` なら:
   - `/design-systems section-{currentSection}` に戻る (Phase 1 step 3)
3. 全セクション完了なら: パイプライン完了

## "continue" で呼ばれた場合

1. `designs/pipeline-state.json` を読み込む
2. `phase` と `pendingFeatures` から現在地を特定
3. 中断したところから再開

## 各フェーズ間の遷移ルール

- **設計 → 計画**: ユーザー確認後
- **計画 → 実装**: ユーザー確認後
- **実装中の各機能**: 自動遷移（テストPass後に次へ）
- **実装 → レビュー**: 自動遷移
- **レビュー → 次セクション設計**: ユーザー確認後

## Git運用

### パイプライン開始時
- mainブランチが最新か確認
- 新しいfeatureブランチを作成: `feature/pipeline-{コンセプト短縮名}`

### 各フェーズでのコミット
- **設計完了時**: `docs(設計): GDD作成` / `docs(設計): セクションNのシステム設計・機能分解完了`
- **機能実装時**: create-featureが各機能ごとにコミット+プッシュ

### コミット後の文脈クリーン
コミット実行後は `/compact` で文脈を圧縮してから次の作業に進む。

## エラー時

- テスト失敗: 修正を試みる（最大3回）。`failedAttempts` に記録
- 3回失敗したらユーザーに報告して次の機能に進む（`skippedFeatures` に移動）
- スキル実行失敗: エラー内容をユーザーに報告して停止

## 出力
- `designs/pipeline-state.json`（進行状態）
- 各スキルの通常出力（GDD、設計書、テスト、コード等）
