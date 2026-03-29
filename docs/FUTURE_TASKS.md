# 将来タスク

PRレビューや実装中に発生した「今すぐではないが後で対応すべきタスク」を記録する。
対応完了したらチェックを入れ、コミットで消化する。

---

## Runtime橋渡し（コンテンツ実装前に必須）

Coreロジックは実装済みだが、Unity上で動かすMonoBehaviour橋渡しが不足している。

### 🔴 ブロッカー（ゲームが動かない）

- [x] **Animation制御** — Animator制御が一切ない。キャラクターがアニメーションしない
  - Core無 / Runtime無
  - AnimatorController自動生成ツールはあるが、ランタイムの状態制御が未実装
  - ✅ PR #18 で実装完了（AnimationBridge + CharacterAnimationController）

- [x] **ActionExecutor Runtime** — 行動実行ロジック（ActionBase派生）をMonoBehaviourから駆動する橋渡し
  - Core有 / Runtime無
  - 攻撃・スキル・ガード・回避等の全行動がシーン上で実行できない
  - ✅ ActionExecutorController + RuntimeActionHandlers + ActionPhaseCoordinator + ActionCostValidator で実装完了

- [ ] **EnemySpawner Runtime** — シーン上で敵を配置・活性化するMonoBehaviour
  - Core有 / Runtime無
  - activateRange外の非アクティブ化、休息リスポーン等のロジックはCoreにある

- [ ] **ProjectileController** — 飛翔体をGameObjectとして飛ばす・衝突検知するMonoBehaviour
  - Core有 / Runtime無
  - 弾丸データモデル・命中判定ロジックはCoreにある

- [ ] **LevelStreaming Runtime** — Additiveシーンロード/アンロードを実行するMonoBehaviour
  - Core有 / Runtime無
  - エリア境界Trigger検知→ロード指示のロジックはCoreにある

### 🟡 早期に必要（最低限のゲーム体験）

- [ ] **Audio** — SE・BGM再生の仕組みが完全に無い
  - Core無 / Runtime無
  - Addressableからのロード→再生→解放のライフサイクル管理が必要

- [ ] **VFX/エフェクト** — ヒットエフェクト、能力発動演出等が無い
  - Core無 / Runtime無
  - パーティクルプール or Addressable即時ロードの設計が必要

- [ ] **StatusEffect表示** — 状態異常のUI/ビジュアルフィードバック
  - Core有（StatusEffectManager）/ 表示無
  - アイコン表示、画面エフェクト、タイマー表示等

### 🟢 後回し可能（コンテンツ追加段階で順次）

- [ ] **Event/Dialog** — イベントシーン再生（Timeline + 会話UI）
  - instruction-formats/event-scene.md でフォーマット定義済み
  - ステージ内の event_zone トリガーから呼び出す想定

---

## パフォーマンス

- [ ] AnimationBridgeのパラメータキーをstring→int hash化（Animator.StringToHashキャッシュ）
  - 発生元: PR #18 レビュー
  - 対象: `Assets/MyAsset/Core/Animation/AnimationBridge.cs`, `Assets/MyAsset/Runtime/Animation/CharacterAnimationController.cs`

- [ ] AnimationBridgeのダーティフラグをパラメータ単位に細分化（全パラメータ再送信の回避）
  - 発生元: PR #18 レビュー
  - 対象: `Assets/MyAsset/Core/Animation/AnimationBridge.cs`

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
