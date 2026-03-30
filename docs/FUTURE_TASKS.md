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

- [ ] **ActionExecutor Runtime** — 行動実行ロジック（ActionBase派生）をMonoBehaviourから駆動する橋渡し
  - Core有 / Runtime無
  - 攻撃・スキル・ガード・回避等の全行動がシーン上で実行できない

- [x] **EnemySpawner Runtime** — シーン上で敵を配置・活性化するMonoBehaviour
  - Core有 / Runtime無
  - activateRange外の非アクティブ化、休息リスポーン等のロジックはCoreにある
  - ✅ EnemySpawnerManager で実装完了（GameObjectプール + Core EnemySpawnerイベント駆動）

- [x] **ProjectileController** — 飛翔体をGameObjectとして飛ばす・衝突検知するMonoBehaviour
  - Core有 / Runtime無
  - 弾丸データモデル・命中判定ロジックはCoreにある
  - ✅ ProjectileController + ProjectileManager で実装完了

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

### 🟡 早期に必要（ゲームプレイ拡張）

- [ ] **長押しロックオン** — ため攻撃のように長押しで射角を変更し、ロックオン対象を切替可能にする
  - 現状: 射撃軌道に最も近い敵を自動ターゲット（ドット積ベース）
  - 追加: 長押し中にターゲット候補をUI表示、射角変更で切替
  - Projectile.TargetHash にロックオン対象を渡す仕組みは実装済み

### 🟡 早期に必要（飛翔体拡張）

- [ ] **ヒット回数キャラ別カウント化** — HashSet→Dictionary<int,int>で同一キャラへの多段ヒット対応
  - 現状: HashSetで同一ターゲット1回制限
  - 変更: キャラごとにhitLimitまでヒット可能、非Pierceは最初のhitLimit到達で消滅
  - 対象: `ProjectileController.cs`

- [ ] **弾丸サイズ変化** — 時間経過でコライダー・スプライトが拡大/縮小
  - BulletProfileにstartScale/endScale/scaleTime追加
  - ProjectileManager.UpdateでElapsedTime/scaleTimeのLerpで制御

- [ ] **出現・移動開始遅延** — 生成後N秒間は非表示・判定無しで待機（寿命は消費しない）
  - BulletProfileにspawnDelay追加
  - Projectile.Tickで遅延中はElapsedTimeを加算しない

- [ ] **弾丸スポーン位置オフセット** — スポーン位置からのローカルオフセット
  - BulletProfileにspawnOffset (Vector2) 追加

- [ ] **ターゲット位置スポーン** — TargetHashのSoA位置に直接出現する弾丸
  - 呼び出し側（MagicCaster等）がSoAから位置取得してSpawnProjectileに渡す

- [ ] **追尾力の時間経過変動** — homingStrengthの時間経過による強化/減衰
  - 現状: homingStrength=5fハードコード
  - BulletProfileにhomingStrength/homingAcceleration追加

- [ ] **複数発発射対応** — 1回の発射で時間差・パターンで複数発を順次生成
  - MagicDefinitionのbulletCount+発射間隔で制御

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
