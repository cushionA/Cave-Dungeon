# Reports Registry

**目的**: SisterGame プロジェクトで生成される各種レポート・引き継ぎノートの索引。
セッション境界を超える知識転送を `pipeline-state.json`（進行状態）+ `docs/compound/`（学習）と並ぶ**第三の柱**として外在化する。

**運用ルール**:
- 新規レポートを作成したら、本ファイルの該当カテゴリの**最上部**に 1 行追加（最新が上）
- レポートファイル名は `YYYY-MM-DD_<slug>.md` 形式
- 6 ヶ月経過したエントリは `_archive/` 配下にアーカイブ（`docs/ARCHIVED_TASKS.md` と同様の運用）

**アクセス手段**:
- セッション開始時: `/registry-check` で本 registry と直近 3 件の handoff を一覧
- セッション終了時: `/handoff-note` で `handoffs/` に新規 entry を生成
- 過去セッション再開時: `/resume-handoff` で前回 handoff note を読み込み state 復元

---

## カテゴリ一覧

| カテゴリ | 用途 | 詳細 |
|---------|------|------|
| [`analysis/`](analysis/README.md) | データ・コード・運用の分析レポート（cost、テストカバレッジ、依存性等） | 分析レポート |
| [`arch/`](arch/README.md) | アーキテクチャ調査・設計検討メモ（`Architect/` への昇格候補） | アーキ調査 |
| [`bugs/`](bugs/README.md) | バグ調査の記録（FUTURE_TASKS.md 登録前段階の調査メモ） | バグ調査 |
| [`experiments/`](experiments/README.md) | 試験的実装・実験結果（採用前の検証） | 実験 |
| [`handoffs/`](handoffs/README.md) | **セッション間引き継ぎノート**（Phase 17 で自動化） | 引き継ぎ |
| [`migrations/`](migrations/README.md) | データ・コード・ツールの移行作業ログ | 移行 |
| [`postmortems/`](postmortems/README.md) | インシデント・障害の事後検証 | 事後検証 |
| [`research/`](research/README.md) | 技術リサーチ（外部ライブラリ、論文、市場調査） | 技術調査 |
| [`reviews/`](reviews/README.md) | PR レビュー結果・コードレビュー記録（自動生成 + 手動） | レビュー |
| [`specs/`](specs/README.md) | 仕様書（`designs/` で扱わない短期スパン仕様） | 仕様 |
| [`surveys/`](surveys/README.md) | ユーザー調査・アンケート結果 | 調査 |

---

## 索引（最新順）

### handoffs/

<!-- 新規 handoff note を作成したら、ここに `- [YYYY-MM-DD タイトル](handoffs/YYYY-MM-DD_slug.md)` を最上部追加 -->

- [2026-04-25 Wave 2 Phase 17 implementation](handoffs/2026-04-25_wave2-phase17-handoff.md) — in-progress / branch: feature/wave2-phase17-handoff

### analysis/

- *エントリなし*

### arch/

- *エントリなし*

### bugs/

- *エントリなし*

### experiments/

- *エントリなし*

### migrations/

- *エントリなし*

### postmortems/

- *エントリなし*

### research/

- *エントリなし*

### reviews/

- *エントリなし*

### specs/

- *エントリなし*

### surveys/

- *エントリなし*

---

## 関連ドキュメント

- [`designs/pipeline-state.json`](../../designs/pipeline-state.json) — パイプライン進行状態（外在化の柱 1）
- [`docs/compound/`](../compound/README.md) — Compound Engineering 学習エントリ（外在化の柱 2）
- 本 registry — レポート索引（外在化の柱 3、Phase 17 で導入）
- [`docs/FUTURE_TASKS.md`](../FUTURE_TASKS.md) — 将来タスク管理
- [`docs/WAVE_PLAN.md`](../WAVE_PLAN.md) — Wave/Phase 計画 source of truth
