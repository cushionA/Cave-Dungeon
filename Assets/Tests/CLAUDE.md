# Assets/Tests/ — テストコード作業ガイド

このディレクトリで作業する際、以下の rules を**必ず参照**する。

## 自動参照される規約

@../../.claude/rules/test-driven.md
@../../.claude/rules/unity-conventions.md
@../../.claude/rules/architecture.md

## 補足（このディレクトリ固有）

### ディレクトリ構成
- `EditMode/` — Edit Mode テスト（ロジック・計算・データ変換）
- `PlayMode/` — Play Mode テスト（MonoBehaviour 連携・物理・コルーチン・シーン）
- `EditMode/Integration_*Tests.cs` — 結合テスト

### TDD ワークフロー（必須）
1. テスト作成（Red）
2. 全 Fail 確認
3. 実装
4. 全 Pass 確認（Green）
5. `python tools/feature-db.py` で記録

### テスト命名規則
`[機能名]_[条件]_[期待結果]`
- 例: `PlayerMovement_WhenSpeedIsZero_ShouldNotMove`
- 例: `HealthSystem_WhenDamageTaken_ShouldReduceHealth`

### 結合テスト 3 観点（必須）
1. **既存ロジック呼び出し検証**: 呼び先の効果まで検証（HP クランプ、アーマーブレイクボーナス等）
2. **状態シーケンス検証**: A→B→A 実行後の OnCompleted 発火回数、連続呼び出し時のハンドルキャンセル
3. **境界値・不変条件**: HP < 0 にならない、subscribe/unsubscribe 対称性

### テスト設計チェックリスト（機能実装時）
- [ ] 他システムのメソッドを呼んでいるか → 呼び先の効果まで検証するテストを書く
- [ ] イベントを購読/発行しているか → 購読解除・多重購読のテスト
- [ ] 状態を持つか → 連続操作・リセット後の再操作テスト
- [ ] リソース（ハンドル・マテリアル等）を確保するか → 解放テスト

詳細は `@../../.claude/rules/test-driven.md` を参照。
