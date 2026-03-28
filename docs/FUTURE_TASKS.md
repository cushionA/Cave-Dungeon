# 将来タスク

PRレビューや実装中に発生した「今すぐではないが後で対応すべきタスク」を記録する。
対応完了したらチェックを入れ、コミットで消化する。

---

## パフォーマンス

（現在なし）

## 設計改善

- [ ] `CharacterCollisionController.SyncCarriedPosition` の呼び出し元をコメントで明記（ActionExecutor.FixedUpdate から呼ぶ想定）
  - 発生元: PR #17 レビュー
  - 対象: `Assets/MyAsset/Runtime/Collision/CharacterCollisionController.cs`

## バリデーション

- [ ] `CollisionMatrixSetup` の設定を起動時に自動チェックする仕組みを追加（レイヤー設定ミス防止）
  - 発生元: PR #17 レビュー
  - 対象: `Assets/MyAsset/Editor/CollisionMatrixSetup.cs`

## 統合待ち

- [ ] `OnCollisionEnter2D` の `other.gameObject.GetInstanceID()` をプロファイルし、高頻度衝突シーンで問題がないか確認
  - 発生元: PR #17 レビュー
  - 対象: `Assets/MyAsset/Runtime/Collision/CharacterCollisionController.cs`
