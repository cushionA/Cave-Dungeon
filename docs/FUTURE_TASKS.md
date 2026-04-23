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

- [x] **EnemySpawner Runtime** — シーン上で敵を配置・活性化するMonoBehaviour
  - Core有 / Runtime無
  - activateRange外の非アクティブ化、休息リスポーン等のロジックはCoreにある
  - ✅ EnemySpawnerManager で実装完了（GameObjectプール + Core EnemySpawnerイベント駆動）

- [x] **ProjectileController** — 飛翔体をGameObjectとして飛ばす・衝突検知するMonoBehaviour
  - Core有 / Runtime無
  - 弾丸データモデル・命中判定ロジックはCoreにある
  - ✅ ProjectileController + ProjectileManager で実装完了

- [x] **LevelStreaming Runtime** — Additiveシーンロード/アンロードを実行するMonoBehaviour
  - Core有 / Runtime無
  - エリア境界Trigger検知→ロード指示のロジックはCoreにある
  - ✅ LevelStreamingOrchestrator + LevelStreamingController + AreaBoundaryTrigger で実装完了

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

- [x] **長押しロックオン** — **仕様外として確定** (2026-04-23 レビューで廃止)
  - 理由: 現状のドット積ベース軌道上サーチで十分。長押しロックオン機構は採用しない
  - 現状維持: `ProjectileMovement.cs` / `Projectile.TargetHash` を変更せず、射撃軌道に最も近い敵を自動ターゲット

### 🟡 ガードシステム関連（PR「refactor/ガードシステム整合化」でまとめて消化済み）

連続ジャストガード対応は上記PRで実装済み。関連する小機能を今後拡張する場合:

- [x] **連続ジャストガード** — JustGuad成立後6秒間は次の1回のガードでguardTimeSinceStartを無視して即ジャスガ成立
  - 対象: `Assets/MyAsset/Runtime/Combat/DamageReceiver.cs` / `Assets/MyAsset/Core/Guard/GuardJudgmentLogic.cs`
  - ✅ PR「refactor/ガードシステム整合化」で `k_ContinuousJustGuardWindow = 6.0f` + `_continuousJustGuardExpireTime` 状態管理 + Judge 引数 `inContinuousJustGuardWindow` で実装

- [x] **CharacterRefs 抜本リファクタ(ハッシュ→キャラ参照の一元化)** — PR #29 で `ManagedCharacter` 抽象クラスとして実装完了
  - ✅ `BaseCharacter : ManagedCharacter` 継承、SoACharaDataDic が `ManagedCharacter[]` を classType として自動生成
  - ✅ `GameManager.Data.GetManaged(hash)?.Damageable` 経路で `DamageDealer` / `ProjectileManager` / `HitBox` / `ProjectileController` がSoA逆引き統一
  - `CompanionCharacter.cs:76` / `EnemyCharacter.cs` に残る Awake 初期化時の `GetComponent` は ManagedCharacter の設計範囲外（キャッシュ用途）で許容

### 🟡 早期に必要（飛翔体拡張）

**注記**: 下記の飛翔体拡張項目は、PR #35 で**旧仕様での第一段階実装**を一部行った (ヒット回数キャラ別/弾丸サイズ変化/出現遅延/スポーンオフセット/追尾力変動)。
main に取り込まれた「仕様確定版」は新仕様 (perTargetHitLimit 二段管理・ShotAnchor 方式・appearDelay/moveStartDelay 二分化等) に刷新されているため、既存実装は**移行対象**として扱う。

- [ ] **ヒット回数キャラ別カウント化** — 仕様確定 (2026-04-23 レビュー)
  - 現状: HashSetで同一ターゲット1回制限
  - 仕様: `HashSet<int>` → `Dictionary<int, int> perTargetHits` に刷新
    - `BulletProfile.perTargetHitLimit` 追加 (デフォルト 1 = 現状互換)
    - 総数 `hitLimit` と `perTargetHitLimit` の二段管理 (各ターゲットへの上限 + 全体の上限)
    - 非Pierce: `hitLimit` 到達で消滅、Pierce: 寿命/コライダー到達まで継続
    - **ターゲット間 cooldown は不要** (被弾側のダメージ受付停止で二重ヒットは防げる)
    - **爆発範囲内の複数キャラは全員判定**
  - **Pool再利用設計**: Projectile pool で再利用時は `perTargetHits.Clear()` を `Projectile.Reset()` / `ProjectileManager.Acquire` 経路で必ず呼ぶ。毎フレーム `new Dictionary` は禁止
  - 対象: `Assets/MyAsset/Core/Combat/Projectile/Projectile.cs:46-62` / `ProjectileController.cs` / `BulletProfile.cs`
  - **PR #35 第一段階**: `ProjectileController._hitCounts: Dictionary<int,int>` + `TryRegisterHit` を実装済み。`perTargetHitLimit` 二段管理の分離と Projectile.Reset 経路の `Clear()` 強制は本仕様で継続対応

- [x] **弾丸サイズ変化** — 実装完了 (PR #35)
  - 仕様: `BulletProfile.startScale` / `endScale` / `scaleTime` (float) 追加
  - `ProjectileManager.Update` で `ElapsedTime / scaleTime` の Lerp でコライダー + SpriteRenderer スケール両方更新
  - ✅ `BulletProfile.startScale/endScale/scaleTime` + `Projectile.GetCurrentScale` 実装済み。`scaleTime<=0` 早期 return もあり (パフォーマンス最適化)
  - 補間曲線・コライダー種別対応・経過後挙動は実装者任せ (推奨: Linear / Circle+Box両対応 / endScale維持)
  - 残タスク: コライダー両対応（Circle+Box）は要継続検証（機能追加時）

- [ ] **出現・移動開始遅延（二分化）** — 仕様確定 (2026-04-23 レビュー)
  - `BulletProfile.appearDelay` (float) — **非表示**・判定無し・位置は**ShotAnchor に追従**・`ElapsedTime` 加算なし・`lifeTime` 消費しない
  - `BulletProfile.moveStartDelay` (float) — **表示あり**・判定無し・位置は**ShotAnchor に追従**・`ElapsedTime` 加算なし・`lifeTime` 消費しない
  - 適用順: `appearDelay` → `moveStartDelay` → 通常動作
  - **射手死亡時はどちらの遅延フェーズでもキャンセル**
  - **位置スナップ先**: 遅延中は項目5の `ShotAnchor` Transform に追従。Animator モーションとの連動性を保つため
  - 対象: `BulletProfile.cs` / `Projectile.cs` / `ProjectileManager.Update`
  - **PR #35 第一段階**: 単一の `BulletProfile.spawnDelay` を実装済み (非表示・判定無し・寿命消費なし)。`appearDelay` / `moveStartDelay` への二分化と ShotAnchor 追従は本仕様で継続対応

- [ ] **弾丸スポーン位置（アンカー統合）** — 仕様確定 (2026-04-23 レビュー、項目5/6/8統合)
  - 旧「弾丸スポーン位置オフセット」「ターゲット位置スポーン」「複数発発射対応」を**統合**
  - **発射位置アンカー方式**: キャラPrefab に `ShotAnchor` 子 GameObject を配置、Animator で Transform を動かしてモーションに合わせる
    - `BulletProfile.spawnOffset` (Vector2) は**廃止方向**、アンカーの Transform.position を使用
    - 推奨: まず単一 `ShotAnchor` で実装、必要に応じて `Dictionary<string, Transform>` + `BulletProfile.anchorKey` に拡張
  - **ターゲット位置スポーン**: `BulletProfile.spawnAtTarget` (bool) または `BulletFeature.SpawnAtTarget`
    - ターゲットの**中心座標** (`vitals.position`) を使用
    - **ターゲット未取得時は射手位置フォールバック**
    - 雨型・落下弾・ターゲット直上系はこのスポーン位置ロジックに統合
  - **複数発発射**: 1アクション内に複数 `BulletProfile` を並べ、各弾に `appearDelay` / `moveStartDelay` / アンカー指定を設定
    - エディター拡張で**扇形等の配置プリセット**を持てるのはアリ（将来タスク）
    - **MPコストは全弾で1回** (アクション単位で消費)
    - **発射中の追加入力受付なし**
    - **発生した弾丸はキャンセル不能** (アクションキャンセルされても発生保証)
  - 対象: `MagicCaster` / `ProjectileController` スポーン経路 / `ActionPhaseCoordinator` / `MagicDefinition` / `BulletProfile`
  - **PR #35 第一段階**: `BulletProfile.spawnOffset` (Vector2, ローカル座標) を実装済みだが、新仕様では `ShotAnchor` Transform 方式への移行が予定されているため**廃止方向**

- [x] **追尾力の時間経過変動** — 実装完了 (PR #35)
  - 現状: `ProjectileMovement.cs:21` の `homingStrength=5f` ハードコードを置換
  - 仕様: `BulletProfile.homingStrength` (float, デフォルト5) + `homingAcceleration` (float)
  - 実効値: `homingStrength + homingAcceleration * ElapsedTime`
  - ✅ `BulletProfile.homingStrength/homingAcceleration` + `Projectile.GetCurrentHomingStrength` を実装済み。`0` 指定時の `5f` フォールバックあり（旧挙動互換）
  - 残タスク: 上限値クランプ（暴走防止の20程度）は実用上の問題が出た時点で追加

### 🟢 後回し可能（コンテンツ追加段階で順次）

- [ ] **Event/Dialog** — イベントシーン再生（Timeline + 会話UI）
  - instruction-formats/event-scene.md でフォーマット定義済み
  - ステージ内の event_zone トリガーから呼び出す想定

---

## パフォーマンス

- [x] AnimationBridgeのパラメータキーをstring→int hash化（Animator.StringToHashキャッシュ）
  - 発生元: PR #18 レビュー
  - 対象: `Assets/MyAsset/Core/Animation/AnimationBridge.cs`, `Assets/MyAsset/Runtime/Animation/CharacterAnimationController.cs`

- [x] AnimationBridgeのダーティフラグをパラメータ単位に細分化（全パラメータ再送信の回避）
  - 発生元: PR #18 レビュー
  - 対象: `Assets/MyAsset/Core/Animation/AnimationBridge.cs`

## 設計改善

- [x] `ResetInternalState()` のユニットテスト追加（各フラグのリセット前後を直接検証）
  - 発生元: PR #25 レビュー
  - 対象: `Assets/MyAsset/Runtime/Character/PlayerCharacter.cs`
  - ✅ PR #34 (F1) で `PlayerCharacterResetInternalStateTests.cs` を追加

- [x] AutoInputTester の `LogResetState()` と `TakeSnapshot()` のvitals取得パターンをヘルパーメソッドに統合
  - 発生元: PR #25 レビュー
  - 対象: `Assets/MyAsset/Runtime/Debug/AutoInputTester.cs`
  - ✅ PR #34 (G1) で共通ヘルパーに抽出

- [x] `CharacterCollisionController.SyncCarriedPosition` の呼び出し元をコメントで明記 — ActionExecutorController.FixedUpdate から呼ぶ想定、運搬対象の SoA 自動解放挙動も docstring に追記

- [x] **BaseCharacter の GetComponent null フォールバック整理** — PR #34 で `TestSceneHelper.CreateBaseCharacterObjectWithDamageReceiver` を新設し、BaseCharacter 側のフォールバックを削除

- [ ] **GameManager.InitializeSubManagers の型 switch 拡張性** — PR #34 で `IGameSubManager` 経由の一括初期化に統合したが、個別の公開プロパティ (`Projectiles`/`EnemySpawner`/`LevelStreaming`) を維持するため `is ProjectileManager pm` 等の型別キャッシュがハードコードされている。SubManager 追加ごとに GameManager 編集が必要
  - 対象: `Assets/MyAsset/Runtime/GameManager.cs:85-97`
  - 修正案: `Dictionary<Type, IGameSubManager>` + `GetSubManager<T>()` 方式にし、既存プロパティはその薄いプロキシにする
  - 優先度: 🟢 Minor (現状 3 マネージャのみ)

- [ ] **CompanionController の手動オーバーライドタイムアウトがハードコード** — `k_ManualOverrideTimeoutSeconds = 5f` 固定で調整不可
  - 対象: `Assets/MyAsset/Core/AI/Companion/CompanionController.cs:16`
  - 修正案: `CompanionAIConfig` に `manualOverrideTimeoutSeconds` フィールドを追加し、コンストラクタで受け取って差し替え可能にする
  - 優先度: 🟢 Minor

- [ ] **DamageReceiver `_actionArmorConsumed` が時間差 ActionEffect 切替に未対応** — 1 行動内で ActionEffect が時間差で切り替わる場合 (例: 0-1s Armor50 → 1-2s Armor30)、累積消費量が新 Effect に繰り越されて残量計算が誤る可能性
  - 対象: `Assets/MyAsset/Runtime/Combat/DamageReceiver.cs:29`
  - 現状: 1 行動 = 1 Armor Effect 前提でコメント明記済み (PR #34)
  - 修正案: 時間差 Armor Effect を採用する要件が出た時点で、ActionEffect 切替検知→`_actionArmorConsumed` リセットの機構を追加
  - 優先度: 🟢 Minor (要件発生時)

- [ ] **AnimationBridge の浮動小数点 dirty 判定は精密一致のみ** — `current == value` で完全一致時のみ dirty 排除。近似一致は dirty 扱いで Animator に送信される
  - 対象: `Assets/MyAsset/Core/Animation/AnimationBridge.cs`
  - 影響: パフォーマンス上無害 (Animator への送信は O(1))、ただし最適化効果は限定的
  - 優先度: 🟢 Minor (計測してから判断)

- [ ] **壁蹴り経路の PlayMode 結合テスト欠落** — `AdvancedMovementLogic.TryWallKick` の純ロジック単体テストは 5 本あるが (PR #35)、PlayerCharacter の `IsTouchingWall` + `TryWallKick` 呼び出し経路を GameObject 配置で再現する結合テストがない
  - 修正案: 壁 GameObject + Player + AbilityFlag.WallKick 付与 → 空中ジャンプ入力で Rigidbody2D.linearVelocity が壁から離れる方向にキックされることを検証
  - 優先度: 🟢 Minor

- [ ] **CameraShakeController と Cinemachine 併用時の設計** — 現在は Camera の localPosition に直接加算 (PR #35)。Cinemachine を導入する場合は CinemachineImpulseSource/Listener に置換が必要
  - 対象: `Assets/MyAsset/Runtime/Camera/CameraShakeController.cs`
  - 優先度: 🟢 Minor

- [ ] **EnemyController/CompanionController の Obsolete コンストラクタ削除** — PR #35 で GameEvents 注入版に移行、旧版に `[Obsolete]` 付与済み。完全移行確認後に削除
  - 優先度: 🟢 Minor

### 戦闘系データ配線（包括レビュー 2026-04-18）

コア論理は実装されているが、接続部で配線漏れがある。ゲーム挙動が仕様通りにならないため、コンテンツ実装前に整合を取る。

- [x] **justGuardResistance が BuildMotionData でコピーされない** — AttackInfo から AttackMotionData への変換で欠落し、ジャストガード時の軽減が常に0で機能しない
  - 対象: `Assets/MyAsset/Core/Common/CombatDataHelper.cs:17-40`
  - ✅ PR「refactor/ガードシステム整合化」で BuildMotionData に justGuardResistance コピー追加

- [x] **`weakElement` が `Element.None` ハードコード** → **仕様外として確定**: 弱点ダメージ倍率は採用しない。弱点は `defense[channel]` を属性別に低く設定することで表現する。`CharacterInfo.weakPoint` は AI のターゲット選択フィルタ (TargetFilter) 用のメタデータとして残置。関連する `CombatStats.weakElement` フィールドは削除済み

- [x] **属性別ガードカット率が未適用** — `GuardStats.slashCut/strikeCut/pierceCut/fireCut/thunderCut/lightCut/darkCut` 定義済みだが、DamageReceiver は一律 `GuardJudgmentLogic.GetDamageReduction(guardResult)` のみ使用
  - 対象: `Assets/MyAsset/Runtime/Combat/DamageReceiver.cs:248-286`
  - ✅ PR「refactor/ガードシステム整合化」で `DamageCalculator.CalculateTotalDamageWithElementalCut` 追加、DamageReceiver から属性別カットを経由させる形に変更。
    同時に旧ガード軽減基本値(0.7/0.9/1.0)を廃止し JustGuard=完全0、その他はGuardStatsのカット率のみで軽減。

- [x] **ProjectileHitProcessor が DamageReceiver.ReceiveDamage を経由していない** — `HpArmorLogic.ApplyDamage` を直呼びしており、ガード/無敵/行動アーマー/SituationalBonus/ActionEffect.damageReduction/HitReaction/イベント発火を全スキップ
  - 対象: `Assets/MyAsset/Core/Combat/Projectile/ProjectileHitProcessor.cs:32-74`
  - ✅ PR「refactor/ガードシステム整合化」で ProcessHit のシグネチャを IDamageable 受け取りに変更。
    ProjectileController は衝突検知した DamageReceiver をそのまま渡し、ProjectileManager.ProcessExplosion も `GameManager.Data.GetManaged(hash)` で IDamageable を逆引きして通す。
    SoACharaDataDic に `IDamageable[] _damageables` + `GetManaged`/`SetManaged` を追加し、BaseCharacter が Start/OnPoolAcquire で登録する。
    飛翔体はジャストガード時アーマー削り0、被弾側スタミナ削りはguardStrengthで相殺する新仕様に合わせ済み。

- [ ] **装備変更→SoA書き戻しパイプライン未実装** — 仕様確定 (2026-04-23 レビュー)
  - 現状: `IEquippable` 実装クラスが0件、`EquipmentStatCalculator.CalculateScaledAttack` 呼び出し元0件。武器変更で攻撃力が反映されない
  - 仕様:
    - **全再計算方式**: 装備変更時に素の値＋全装備合算で CombatStats を再構築（差分ロールバックではない）
    - **アイテムデータが原本** (ScriptableObject 等)、装備時に値をコピーして利用
    - **副次効果（装備重量・スタミナ消費倍率 等）も含める**
    - **セット効果**: セット発動時に**差替え用装備データを参照**で持たせ、セット時はそちらのデータで再計算（例: 2点セット時は「炎の武具・覚醒」データに切替）
    - `IEquippable` 実装クラス (Weapon / Shield / Core) を追加し、`OnEquip(ownerHash)` で `EquipmentStatCalculator` 経由で `GameManager.Data.GetCombatStats(ownerHash)` を更新
  - 対象: `Assets/MyAsset/Core/Equipment/EquipmentHolder.cs` / `EquipmentStatCalculator.cs` / 新規実装クラス

- [x] **Coyote Time — 新仕様への移行** (2026-04-23 仕様確定、PR #35 で第一段階実装済み)
  - 現状実装: PR #35 で `GroundMovementLogic._timeSinceLeftGround` + `k_CoyoteTimeSeconds = 0.1f` のハードコード実装。`TryStartJump` で Coyote 窓内なら許容、成立時に窓消費
  - 新仕様追加分:
    - **データ駆動化**: `CharacterInfo.coyoteTime` (float, デフォルト 0.1s) から読む
    - **Wall Slide 中の扱い / jumpBufferTime との役割分担** を整理 (項目22の重複整理予定)
  - 対象: `Assets/MyAsset/Core/Movement/GroundMovementLogic.cs` / `CharacterInfo.cs`
  - ✅ PR TBD (batch-movement-data-driven) で **データ駆動化のみ** 完了: `CharacterInfo.coyoteTime` + `MoveParams.coyoteTime` フィールド追加、`GameManager.RegisterCharacterInternal` で転記、`GroundMovementLogic.IsInCoyoteWindowFor(MoveParams)` / `GetEffectiveCoyoteTime` 追加、`TryStartJump` を MoveParams 参照に置換 (未設定時は定数 `k_CoyoteTimeSeconds` にフォールバック)。テスト: `CoyoteTimeTests` に 4 ケース追加
  - **残タスク**: Wall Slide 中の扱い / jumpBufferTime との役割分担整理は本バッチではスキップ。別タスクで対応

- [ ] **壁蹴り呼び出し経路 — 新仕様への移行** (2026-04-23 仕様確定、PR #35 で第一段階実装済み)
  - 現状実装: PR #35 で `PlayerCharacter.IsTouchingWall` + 空中ジャンプ入力時の `AdvancedMovementLogic.TryWallKick` 呼び出し経路を追加
  - 新仕様追加分:
    - **キャラ共通モジュール化**: プレイヤー限定でなく敵・仲間にも拡張可能な形に (現状は PlayerCharacter のみ)
    - **左右交互制約**: 同じ側の壁は連続で蹴れない。`_lastWallSide` (Left/Right) または `_lastWallNormalX` を保持し、反対側のみ許可
    - **補助**: `_lastWallColliderId` で同一壁二度蹴り防止
    - **壁接触判定の統一**: `CharacterCollisionController` の Raycast/BoxCast 方針に合わせる
  - 対象: `Assets/MyAsset/Core/Movement/AdvancedMovementLogic.cs` / `PlayerCharacter.cs` / `EnemyCharacter.cs` / `CompanionCharacter.cs` / `CharacterCollisionController.cs`

### アーキテクチャ乖離（包括レビュー 2026-04-18）

設計文書と実装の乖離。動作への影響は小さいが、設計意図を達成する。

- [x] **SoACharaDataDic が手書き実装** — `Packages/com.zabuton.container-pack` のSourceGeneratorが利用可能だが、現状は完全手書き（設計書 `Architect/01` で想定する `[ContainerSetting]` + `partial class` 未使用）
  - ✅ `[ContainerSetting(structType, classType)]` + `partial class` 宣言に置換。既存 API 名（GetVitals 等）は `SoACharaDataDic.Compat.cs` でエイリアスとして維持

- [x] **`BaseCharacter.DamageReceiver` プロパティ未公開 + フィールドキャッシュなし** — Start() と OnPoolAcquire() で `GetComponent<DamageReceiver>()` が重複。設計書では `GameManager.Data.GetManaged(hash).DamageReceiver` 経由アクセスを想定
  - ✅ `_damageReceiver` / `_animationController` を Awake でキャッシュし、`DamageReceiver` / `AnimationController` / `Damageable` プロパティを公開

- [x] **`GameManager.Data.GetManaged(hash)` API 未実装** — 設計書（`Architect/01_データコンテナ.md:51`）で想定する managed（BaseCharacter）ハッシュアクセスが欠落
  - ✅ Core 側に `ManagedCharacter` 抽象クラスを追加し BaseCharacter が継承。SoACharaDataDic が `ManagedCharacter[]` を classType として自動生成、`GetManaged(hash) → ManagedCharacter` を公開。Runtime 側の飛翔体/ヒット系は `.Damageable` 経由で IDamageable を取得

- [x] **SaveManager 内蔵の JsonUtility 永続化（`SetFileIO` 経路）が SaveDataStore と機能重複** — SaveDataStore 一本化で確定 (2026-04-22)
  - `ISaveFileIO` / `SetFileIO` / private `WriteToDisk` / private `ReadFromDisk` / `SaveSlotWrapper` を削除
  - `Save` / `Load` / `HasSaveData` / `DeleteSaveData` はインメモリ操作のみに簡素化 (ディスク永続化は SaveDataStore に完全分離)
  - 外部呼び出し元ゼロのため破壊的変更なし、テストは SaveDataStore 経由で既に通る
  - 備考 (2026-04-23 レビュー): EasySave 導入は別タスクで将来判断 — 採用時は `SaveDataStore` を EasySave 経由に差し替える

### AI 配線漏れ（包括レビュー 2026-04-18）

- [x] **`ModeTransitionEditor` が CompanionController から未接続** — CompanionController に `ModeTransitionEditor` インスタンスを保持し `RequestModeSwitch(int modeIndex)` API を公開。手動切替中は `k_ManualOverrideTimeoutSeconds = 5f` の間 `EvaluateTransitions` を抑制、タイムアウト経過で自動切替復帰

- [x] **CompanionAISettings UI: フッターバーのパッド入力配線未実装** — UXML/USS にフッターバー（A/B/X/Y 凡例）を定義済みだが、実際のパッドボタン→UIアクション紐付けがない。誤認識防止に現在は `hidden` クラスで非表示。
  - 対象: `Assets/MyAsset/Runtime/UI/CompanionAISettingsController.cs` / `Assets/MyAsset/UI/CompanionAISettings/CompanionAISettings.uxml`
  - ✅ Phase 3b で対応: UI アクションマップに `MenuSave`（Gamepad West / Ctrl+S）/ `MenuSaveAsPreset`（Gamepad North / Ctrl+Shift+S）を追加。Controller が `InputActionAsset` を受け取って `Cancel`/`MenuSave`/`MenuSaveAsPreset` を購読し、入力アセット注入時のみ footer-bar の `hidden` クラスを実行時に外す（未注入なら非表示維持）。A（Submit）は UI モジュール経由のフォーカス済みボタンクリックで既存挙動。

- [x] **EnemyController に `BroadcastActionHandler` 未登録** — Attack/Cast/Instant/Sustained に加えて Broadcast も登録。挑発・集合指示系の Broadcast 行動が敵AIで実行可能になった

- [x] **デフォルト行動「何もしない」Idle アクションの runtime 実装** — `SustainedAction.Idle` enum 追加、ラベル・メタデータ・ActionTypeRegistry unlock 登録済み。SustainedActionHandler は paramId を保持するだけなので Idle も既存ハンドラで no-op 動作する
  - ✅ PR TBD で残タスク (`CompanionAISettingsLogic.GcOrphanActionSlots` で default を Idle に正規化、`AIMode` 新規作成時の default 初期値を Idle に) を実装

- [x] **CompanionMpManager の MP総和が CeilToInt で消失** — `_reserveTransferCarry` accumulator で端数を累積し、`FloorToInt` で双方同時に動かす対称構造に修正。細かい dt で連続 Tick しても MP 総和は減らない

- [x] **混乱解除時の即時AI再評価なし** — 基盤 (`JudgmentLoop.ForceEvaluate()` / `GameEvents.OnConfusionCleared` / `ConfusionEffectProcessor(GameEvents)`) に加え、`EnemyController` / `CompanionController` の AIBrain 配線も完了。両 Controller は `IDisposable` 実装で R3 購読を管理し、自ハッシュ一致のイベントにのみ `ForceEvaluate()` を呼ぶ。`EnemyCharacter` / `CompanionCharacter` が `GameManager.Events` 注入＋ `OnDestroy` で `Dispose` 呼出し
  - 補足: Runtime `BossController` MonoBehaviour が未実装 (下記参照) のため Boss 側の配線は対応外。Runtime BossController 実装時に同じパターン (`BossControllerLogic` が `GameEvents` 注入 → 自ハッシュ一致で `ForceEvaluate`) を踏襲すること

- [ ] **BossPhaseManager の `invincibleTime` が BossController 側で適用されているか要検証** → **Runtime BossController 未実装のため未配線**
  - 現状: `BossControllerLogic.UpdateEncounter` は `BossPhaseTransitionResult.invincibleTime` を返すが、呼び出し元は EditMode テストのみ。Runtime 側の MonoBehaviour BossController が未実装のため、`DamageReceiver` の無敵フラグへの伝播も未配線
  - 対象: `Assets/MyAsset/Core/AI/Boss/BossController.cs`（未実装）/ `BossPhaseManager.cs`
  - 修正: Runtime `BossController` MonoBehaviour を新設する際に `UpdateEncounter` 戻り値の `invincibleTime` を `DamageReceiver.SetInvincible(duration)` (新設) に伝播する
  - 優先度: 🟡 Medium（Runtime BossController 実装と併せて対応する大タスク）

- [x] **CompanionAISettings UI で `AIMode.targetRules` / `targetSelects` が編集不可** — PR #32 (Phase 5 統合UI) で対応済み。`ShowModeDetailDialog` に「ターゲット選定」(`RebuildTargetSelectList` Dialogs.cs:2393) + 「ターゲット切替ルール」(`RebuildTargetRuleList` Dialogs.cs:2494) セクションあり、`AdjustTargetRulesForRemovedSelect` Logic で削除時の正規化もカバー済み

- [x] **CompanionAISettings UI: TargetFilter IsSelf/IsPlayer 自動クリアの回帰テスト欠落** — `BuildTargetFilterSection` で Foldout 構築時に IsSelf/IsPlayer ビットを自動クリア＋onChanged 通知する修正が入っているが、これを検証するユニットテストがない
  - 対象: `Assets/MyAsset/Runtime/UI/CompanionAISettingsController.Dialogs.cs` / `Assets/Tests/EditMode/Integration_CompanionAISettingsFlowTests.cs`
  - 修正: 古い Config（IsSelf/IsPlayer 付き）をロード→Foldout を開く→保存、で自動的に bit がクリアされることを検証するテストを追加
  - ✅ PR #34 (F2) で回帰テストを追加

- [x] **CompanionAISettings UI: `RefreshShortcutDropdowns` が描画関数内でバッファを書き換える** — `Controller.cs:528-530` で描画関数が `bindings[i] = -1` の副作用を持つ。`_isDirty` も立たないため整合性が疑わしい
  - 対象: `Assets/MyAsset/Runtime/UI/CompanionAISettingsController.cs:528-530`
  - 修正: `CompanionAISettingsLogic` に `ClearInvalidShortcutBindings()` API を追加し、Switch/Add/Remove 時に Logic 側で明示的にクリーンアップ
  - ✅ PR #34 (E1) で `ClearInvalidShortcutBindings` を Logic 側に移譲

- [x] **CompanionAISettings UI: `AttachTooltipHandlers` の多重登録リスク** — `RefreshEditor` 毎に対象要素へ tooltip ハンドラを再登録するが、前回分の解除は `OnDisable` まで走らない。ダイアログを開閉する度に `_unsubscribeActions` が単調増加
  - 対象: `Assets/MyAsset/Runtime/UI/CompanionAISettingsController.cs:230-280 付近`
  - 修正: `element.userData` に登録済みフラグを持たせる、または Dialog スコープごとに unsubscribe セットを分離
  - ✅ PR #34 (E2) で tooltip ハンドラの多重登録を防止

- [x] **CompanionAISettings UI: マジックナンバー `ExecuteLater(50)` 等** — `k_DialogFocusDelayMs = 50` として const 化。CompanionAISettingsController.cs / Dialogs.cs の 5 箇所を置換

- [x] **CompanionAISettings UI: Dialogs.cs が 1912 行で巨大** — 将来 `BuildTargetFilterSection` / `BuildConditionInputWidgets` / `ShowActionPickerDialog` を別 partial に分割
  - ✅ PR #34 (E4) で機能単位 partial 分割を実施

- [x] **CompanionAISettings UI: `BuildConditionRow` / `BuildAttackPickerContent` の毎回アロケーション削減** — `Enum.GetValues` / `new Dictionary` を static キャッシュ化
  - ✅ PR #34 (E5) で static readonly キャッシュ化

- [x] **CompanionAISettings UI: pattern simulation テストを実装直接テストへ移行** — PR #26 で追加した `Integration_CompanionAISettingsFlowTests.CompanionAISettings_UILike_*` は UI 実装の closure を自前で再現している。実装本体 (`RebuildActionSlotList`/`RebuildActionRuleList`) が将来 AIMode 値渡しに戻されてもテストは通る可能性がある
  - 修正案:
    1. `Assets/MyAsset/Runtime/UI/Game.UI.asmdef` (存在すれば) に `InternalsVisibleTo("Game.Tests.EditMode")` 属性を追加
    2. `RebuildActionSlotList` / `RebuildActionRuleList` を `internal` に公開
    3. テストから直接 VisualElement コンテナ + closure を渡して呼び、Button の clickable をシミュレート
  - 対象: `Assets/MyAsset/Runtime/UI/CompanionAISettingsController.Dialogs.cs` / `Assets/Tests/EditMode/Integration_CompanionAISettingsFlowTests.cs`
  - ✅ PR #34 (F3) で Rebuild の直接呼び出しテストへ移行

- [x] **CompanionAISettings UI: Edit button が setter を経由しない** — `RebuildActionSlotList` / `RebuildActionRuleList` の「変更」ボタンは getter で取得した配列を直接 `arr[idx] = picked` で書き換える。現状は配列参照型の性質で動作するが、将来 setter に dirty flag 立てや validation を追加した場合 silently skip される
  - 対象: `Assets/MyAsset/Runtime/UI/CompanionAISettingsController.Dialogs.cs` (RebuildActionSlotList/RebuildActionRuleList の edit button)
  - 修正: Edit 経路も Remove 経路と同様に「新配列を生成して setter 経由」に揃える
  - ✅ PR #34 (E3) で setter 経由の新配列パターンに統一

### 戦闘機構の未接続・未実装（包括レビュー 2026-04-18）

- [x] **クリティカル機構が未接続** → **仕様外として確定 + クリーンアップ済み**: クリティカルヒット機構は本プロジェクトでは採用しない。`DamageCalculator.IsCritical` / `ApplyCritical` / `GetWeaknessMultiplier` API、`CombatStats.criticalRate/criticalMultiplier` フィールド、`GameConstants.k_DefaultCritical*` 定数、`DamageCalculator.Calculate*` の `weakElement` 引数をすべて削除。SoA 構造体のサイズを 8 byte/キャラ削減

- [x] **`knockbackResistance` 未実装** — `CombatStats.knockbackResistance` フィールド追加、`CharacterInfo.knockbackResistance` から GameManager で配線。`DamageReceiver.ApplyKnockback` が `HpArmorLogic.CalculateKnockback(force, combat.knockbackResistance)` を呼ぶ

- [ ] **Guard 系フラグ仕様整理** — 仕様確定 (2026-04-23 レビュー、元「EnhancedGuard が JustGuardImmune 攻撃にも成立」)
  - **目標仕様** (2026-04-23 レビュー決定):
    - **`EnhancedGuard` フラグは廃止** (PR #34 時点で既に `GuardJudgmentLogic` からは除去済み、enum 定義の残存も削除対象)
    - **`AttackFeature.JustGuardImmune`**: ジャスガ不成立、**通常ガードは成立**
    - **`AttackFeature.Unparriable`**: `JustGuardImmune` と同義 (統合可能、どちらか片方に統一)
    - **`AttackFeature.Unguardable`**: ガード不成立、**ただしジャスガは可能**
    - 2種のフラグは**組み合わせ可能・別々に設定**（例: Unguardable + JustGuardImmune = 完全防御不能）
  - ⚠️ **現状と命名衝突あり**:
    - 現在の `GuardJudgmentLogic.cs:62` では `Unparriable` 攻撃は**即 NoGuard** を返す (ガードも JustGuard も不可 = 完全防御不能相当)
    - これは目標仕様の `Unparriable` (= JustGuardImmune) とは意味が異なる
    - `Unguardable` フラグは現 enum に**存在しない**
  - **対応方針** (実装時に決定):
    - 案1: 現 `Unparriable` を `Unguardable` にリネーム + JustGuard 可能化 + 新 `Unparriable` を JustGuardImmune の別名として追加（または JustGuardImmune 単独運用）
    - 案2: 現 `Unparriable` は廃止 → `Unguardable` + `JustGuardImmune` の組み合わせ表現に全面移行
    - いずれにせよ `Unparriable` を使用している全箇所（enum 定義、Logic、AttackInfo 等）の意味再割り当てが必要
  - 対象: `Assets/MyAsset/Core/Common/Enums.cs:93` (Unparriable 定義) / `Assets/MyAsset/Core/Guard/GuardJudgmentLogic.cs:62-79` / `AttackFeature` enum / `EnhancedGuard` 参照箇所削除

- [x] **Action armor 消費が EffectState に書き戻されない** — 機械的修正済み (2026-04-22)。ActionEffectProcessor.EffectState は毎回 Evaluate で再計算される struct のため、ref 経由で削られた値は局所変数にしか残らなかった
  - 対応: DamageReceiver に `_actionArmorConsumed` 累積フィールドを追加し、effectState.actionArmorValue から差し引いて正味の残量を HpArmorLogic.ApplyDamage に渡す。消費量は毎ヒット加算、SetActionEffects/ClearActionEffects/ResetInternalState で 0 にリセット
  - テスト: Integration_DamageReceiverStateTests.cs 追加

- [x] **Action armor 追加仕様** — 2026-04-23 実装完了
  - ✅ DamageReceiver に `ActionExecutorController` の参照を Awake で取得し、
    `ApplyDamageToVitals` が `actionArmorJustBroken` を返すよう拡張。
    削り切った瞬間 (`actionArmorBefore > 0 && consumed > 0 && after <= 0`) を検出し、
    SuperArmor 非適用 && ガード非成功の場合は HitReactionLogic.None を Flinch/Knockback に上書き + `CancelAction()` で行動中断。
  - ✅ SuperArmor 中は完全保護 (`effectState.hasSuperArmor` チェックで shouldInterruptForArmorBreak=false)
  - 対象: `Assets/MyAsset/Runtime/Combat/DamageReceiver.cs`
  - テスト: `Integration_ActionArmorBreakToFlinchTests` (EditMode 11本) + `ActionArmorBreakFlinchIntegrationTests` (PlayMode 7本)

- [x] **HitReaction 結果が ActState に書き込まれない** — 機械的修正済み (2026-04-22)。HitReactionLogic.Determine の戻り値は DamageResult に格納されるだけで、SoA の CharacterFlags.ActState に反映されていなかった
  - 対応: DamageReceiver.ReceiveDamage 内に ApplyHitReactionToActState を追加。Flinch → ActState.Flinch / Knockback → Knockbacked / GuardBreak → GuardBroken / 死亡時 → Dead を書き戻す。次ヒット判定で HitReactionLogic.IsInHitstun が正しく評価されるようになる
  - テスト: Integration_DamageReceiverStateTests.cs 追加

- [x] **Flinch中の挙動仕様** — 2026-04-23 実装完了
  - ✅ DamageReceiver.ReceiveDamage 先頭で `wasInFlinch = currentActState == ActState.Flinch` を判定し、
    true なら `effectState.actionArmorValue = 0f` / `_actionArmorConsumed = 0f` / `vitals.currentArmor = 0f` を強制
    (ローカルコピー effectState を書き換え + SoA currentArmor 直接書き込み)
  - ✅ ApplyHitReactionToActState が `wasInFlinch` 引数を受け取り、
    HitReaction.Flinch の場合のみ `!wasInFlinch` ガードで上書きスキップ (タイマー延長なし)
  - ✅ Knockback / GuardBreak / Dead は wasInFlinch に関係なく上書き (Flinch → Knockbacked 遷移を許容)
  - 対象: `Assets/MyAsset/Runtime/Combat/DamageReceiver.cs`
  - テスト: `Integration_FlinchStateBehaviorTests` (EditMode 5本) + `FlinchStateBehaviorIntegrationTests` (PlayMode 5本)

### Runtime Bridge 整理（包括レビュー 2026-04-18）

- [x] **`InputBuffer` デッドコード** — ランタイム呼び出しゼロで tests 2 本のみ参照していたため、クラス本体 + .meta + テストを削除 (2026-04-22)。先行入力が必要になったら PlayerInputHandler に統合する形で再導入する

- [x] **`ActionCostValidator` の float→int キャストでMPコスト小数切り捨て** — int 型一本化で確定 (2026-04-22)
  - `CanAfford` / `DeductCost` の `mpCost` を `int` に変更、`(int)mpCost` キャスト削除
  - `AttackInfo.mpCost` / `AttackMotionData.mpCost` も `int` に揃え、破壊的変更を全呼び出し元に反映
  - 境界値テスト追加 (ジャスト消費 / 超過時 0 クランプ / currentMp == mpCost で実行可能)
  - 備考: `SkillConditionLogic.CanUseSkill` は `float mpCost` のままで、`ItemUsageLogic.TryUseMagic` / `CompanionMpManager` も独立したドメインのため変更対象外。将来的に統合する場合は全体方針を決めた上で一括リファクタリング

- [x] **`GameManager.Awake()` で `GetComponentInChildren` を直列3回** — `IGameSubManager` に統合。ProjectileManager(300) / EnemySpawnerManager(200) / LevelStreamingController(100) が `IGameSubManager` を実装し、`GetComponentsInChildren<IGameSubManager>(true)` 1 回で取得 → InitOrder 昇順でソートして一括 `Initialize(data, events)`。既存公開プロパティ（Projectiles/EnemySpawner/LevelStreaming）は型別キャッシュで維持

### Movement 拡張機能（包括レビュー 2026-04-18）

- [ ] **カメラシェイクAPI — 方針変更 (ProCamera2D 導入へ)** (2026-04-23 仕様確定)
  - **PR #35 で第一段階 (自前 Perlin 駆動 MonoBehaviour) を実装済み**:
    - `Assets/MyAsset/Core/Camera/CameraShakeParams.cs` — イベントペイロード
    - `Assets/MyAsset/Runtime/Camera/CameraShakeController.cs` — Perlin ノイズ駆動
    - `GameEvents.OnCameraShakeRequested` + `FireCameraShakeRequested`
  - **新方針**: ProCamera2D アセット導入後は本実装を廃止し、ProCamera2D の Shake 機能に置換する
  - 残タスク:
    - [ ] ProCamera2D 導入タイミングで `CameraShakeController` を撤去 / Impulse Source に置換
    - [ ] 呼び出し経路の配線 (`DamageReceiver` / `ProjectileManager.ProcessExplosion` / 着地強調)

- [ ] **動的カメラBounds切替未実装** — 方針確定 (2026-04-23 レビュー): **ProCamera2D 導入後に実装**
  - エリア遷移時のカメラ範囲切替
  - ProCamera2D の Bounds 機能を利用し、`AreaBoundaryTrigger.OnAreaLoaded` イベントで切替

- [ ] **Look-Ahead未実装** — 方針確定 (2026-04-23 レビュー): **ProCamera2D 導入後に実装**
  - プレイヤー移動方向に先行してカメラを動かす機能

- [ ] **坂道法線取得なし** — 仕様確定 (2026-04-23 レビュー)
  - 現状: 接地判定は Y座標比較のみ (`CharacterCollisionLogic.CheckGrounded`)、法線未取得
  - 仕様: Raycast/BoxCast に刷新、`RaycastHit2D.normal` を `GroundInfo.normal` として保持、velocity を normal に沿って射影、最大登坂角度 45°
  - 対象: `Assets/MyAsset/Core/Collision/CharacterCollisionLogic.cs:196-199` / `GroundMovementLogic.cs`

- [x] **一方通行プラットフォーム（drop-through）未実装** — 仕様確定 (2026-04-23 レビュー)
  - 下入力＋ジャンプで床を抜ける機能
  - 推奨: `PlatformEffector2D` + プレイヤーのレイヤー一時切替方式
  - ✅ `Core/Movement/DropThroughLogic.cs` (ピュアロジック) + `Runtime/Collision/DropThroughPlatform.cs` (マーカー MonoBehaviour) + `PlayerCharacter` に発動 API 追加で実装。レイヤーを `CharaInvincible` に一時切替 (`k_DropThroughDurationSeconds = 0.3f`)

- [x] **GroundMovementLogic の移動定数がデータ駆動化されていない** — 仕様確定 (2026-04-23 レビュー)
  - 現状: `GroundMovementLogic.cs:12-16` に `k_DodgeDuration` / `k_DodgeSpeedMultiplier` / `k_SprintSpeedMultiplier` 等がハードコード
  - 仕様:
    - **キャラ別値** を `CharacterInfo` に追加 (`dodgeDuration`, `dodgeSpeedMultiplier`, `sprintSpeedMultiplier`)
    - `MoveParams` にも同フィールド追加、GameManager 初期化で CharacterInfo から転記
    - **`CharacterInfo.jumpBufferTime` と `minJumpHoldTime` は両方残す** (役割が別: 着地前入力受付 vs 最小ホールド時間)
  - 対象: `Assets/MyAsset/Core/Movement/GroundMovementLogic.cs:12-16` / `CharacterInfo.cs` / `MoveParams`
  - ✅ 実装済み: `CharacterInfo` + `MoveParams` に 3 フィールド追加 (`dodgeDuration`/`dodgeSpeedMultiplier`/`sprintSpeedMultiplier`)。`GameManager.RegisterCharacterInternal` で転記。`GroundMovementLogic.CalculateHorizontalSpeed` と `UpdateDodge` で MoveParams 参照に置換 (未設定時は定数フォールバック)。定数 `k_DodgeDuration`/`k_DodgeSpeedMultiplier`/`k_SprintSpeedMultiplier` はフォールバック用に維持。テスト: `GroundMovementDataDrivenTests` 6 ケース追加

## バリデーション

- [x] `CollisionMatrixSetup` の設定を起動時に自動チェックする仕組みを追加（レイヤー設定ミス防止）
  - 発生元: PR #17 レビュー
  - 対象: `Assets/MyAsset/Editor/CollisionMatrixSetup.cs`
  - ✅ PR #34 (I1) で `CollisionMatrixValidator` を追加 (バッチモードでは無効化)

- [x] **CurrencyManager.Add で int オーバーフロー未検知** — `int.MaxValue - _balance` 比較で飽和演算、超過時は `int.MaxValue` にクランプ

- [x] **InventoryManager.Add が同一itemIdの複数スタックを探索しない** — 2 pass化。既存スタック全走査で順次充填→余剰分は新スロットに追加 (k_MaxSlotCount=99 上限まで)。メソッド名は既存呼び出し (ShopLogic, SaveSystem テスト) との整合性を保つため `Add` に統一
  - 備考: 特定のローカル開発環境で Unity の JIT/ランタイムキャッシュが腐って `InventorySystemTests.InventoryManager_Add_OverflowsToNewStack` / `InventoryManager_Add_FillsExistingStacksBeforeCreatingNew` が旧実装相当の挙動を返す現象を確認（`ilspycmd` で DLL IL は新実装確認済み）。CI クリーン環境では通る想定のため `[Ignore]` は付与せずそのまま。ローカルで同現象が起きたら **Library/ 全削除 + Unity 完全再起動** で解消する

- [x] **LevelUpLogic に最大レベルキャップなし** — `k_MaxLevel = 99` 追加。AddExp の while 条件に `_level < k_MaxLevel`、到達時は余剰 exp を破棄
  - 残タスク (仕様確定 2026-04-23 レビュー):
    - [x] **ステータスポイント refund API (振り直し)** — Logic 層実装完了 (PR #36)
      - ✅ `LevelUpLogic.RefundAllStatusPoints()` / `RefundStatus(StatType, int points)` を追加、テスト 14 ケース (`LevelUpRefundAndCapTests`)
      - 残タスク (Runtime 層): **特殊アイテム消費**による refund API 呼び出しの配線（ゴールド/無料ではなく、専用アイテムで発動）。コスト・回数制限は別途設計
    - [x] **`k_MaxLevel` の ScriptableObject 化** — 今回は **`k_MaxLevel` のみ** SO 外出し（`LevelUpConfig` SO）。他の定数 (`k_PointsPerLevel`, `k_HpPerVit` 等) はハードコードのまま維持
      - 役割: **ハードキャップ**として機能。下記「動的算出値」がこの値を**超えないことを保証**する
      - ✅ `Assets/MyAsset/Core/ScriptableObjects/LevelUpConfig.cs` を新設、`LevelUpLogic` に `LevelUpConfig` 注入コンストラクタ + 静的 `SetDefaultConfig` 経路を追加。既存 `k_MaxLevel` 定数は後方互換のため `k_DefaultMaxLevel (= 99)` と同値で残置。`MaxLevel` プロパティが SO の `maxLevel` を優先参照
    - [x] **各ステータス (Str/Dex/Intel/Vit/Mnd/End) の上限値設定** — 実装完了 (PR #36)
      - ✅ `CharacterInfo.statCaps` (int[6], デフォルト 99×6) フィールド追加
      - ✅ `LevelUpLogic.AllocatePoint(StatType, int[] statCaps = null)` で stat ごとの cap チェック（null 時は後方互換）
      - ✅ `LevelUpLogic.GetEffectiveMaxLevel(int[] statCaps)` で **動的算出**: `dynamicMaxLevel = sum(statCaps) / k_PointsPerLevel` を `LevelUpConfig.maxLevel` (ハードキャップ) でクランプ
      - ✅ PR #36 レビュー R1 対応: `GetEffectiveMaxLevel` が `LevelUpConfig.maxLevel` 経路 (SO) に統合、`LevelUpRefundAndCapTests` で回帰テスト追加

- [x] **GateStateController.ForceClose が isPermanent を無視** — isPermanent=true (ボスクリア等の永続ゲート) の場合は ForceClose をスキップするよう修正。戻り値を `bool` にして実際に閉じたかを呼び出し元が判定できるように変更。GateSystem_OpenCloseTests に一時ゲート/永続ゲートの回帰テスト追加

- [x] **SoACharaDataDic の固定容量超過検知** — `GameManager.WarnIfCapacityUsageHigh` で使用率 80% 以上で `Debug.LogWarning`。開発ビルド限定 (`UNITY_EDITOR` / `DEVELOPMENT_BUILD`) で `Conditional` 属性によりリリースはゼロコスト。閾値を下回ったらフラグ復帰して再警告可能

- [ ] **SaveDataStore の型偽造リスク (許可リスト制)** — PR #36 レビュー R2
  - 現状: `ResolveType(typeName)` が全アセンブリから任意型を検索できる。save ファイル偽造で任意型のインスタンス化経路 (Newtonsoft.Json `TypeNameHandling.All` 相当のリスク)
  - ローカルセーブ前提では低リスクだが、以下のケースで許可リスト制へ移行必要:
    - クラウドセーブ導入 (他クライアント由来の save を読む)
    - MOD 対応 (ユーザー提供 dll から型解決)
  - 修正案: `SaveTypeRegistry` を新設し `ISaveable` 実装型/`[Serializable]` 付き特定型のみ許可、未許可型は `JToken` にフォールバック
  - 対象: `Assets/MyAsset/Core/Save/SaveDataStore.cs:ResolveType`

- [ ] **SaveDataStore のアトミック書き込み** — PR #36 レビュー R6
  - 現状: `File.WriteAllText(filePath, fileJson)` 中の電源断で filePath が破損する可能性。現状は .bak 経由で前回成功状態に戻れるので致命的ではない
  - 改善案: temp ファイル書き込み → `File.Move(filePath, backupPath)` → `File.Move(tempPath, filePath)` の 3 段で アトミック化
  - 対象: `Assets/MyAsset/Core/Save/SaveDataStore.cs:WriteToDisk`
  - 優先度: 🟢 Minor

- [ ] **Flinch 解除後 armor 復元仕様の確定** — PR #36 レビュー R3
  - 現状 (PR #36): Flinch 中被弾時に `vitals.currentArmor = 0f` を強制。Flinch 解除後も 0 のままで、既存の `UpdateArmorRecovery` で自然回復する
  - 仕様確定必要: 「Flinch 解除直後に armor を Flinch 入り前の値に即時復元する」仕様なら前値保存→復元機構を追加
  - 対象: `Assets/MyAsset/Runtime/Combat/DamageReceiver.cs:ForceZeroArmorIfInFlinch`
  - 優先度: 🟢 Minor (仕様判断次第)

## 統合待ち

- [x] `OnCollisionEnter2D` の `other.gameObject.GetInstanceID()` をプロファイルし、高頻度衝突シーンで問題がないか確認
  - 発生元: PR #17 レビュー
  - 対象: `Assets/MyAsset/Runtime/Collision/CharacterCollisionController.cs`
  - ✅ PR #34 (H3) で `GetInstanceID()` を `GetHashCode()` に置換済み、本項は計測を経ずクローズ

- [ ] **Addressable 導入（包括レビュー 2026-04-18）** — `.claude/rules/asset-workflow.md` にグループ/ラベル/アドレス命名規則を定めているが、`Assets/MyAsset` 内で `Addressables.` / `AssetReference` / `LoadAssetAsync` 使用0件
  - 現状: 実装ゼロ (`com.unity.addressables` パッケージ未導入 / グループ未作成 / ランタイム参照はすべて Inspector 直接参照または `Resources.Load` 前提)
  - 将来対応方針:
    1. Package Manager から `com.unity.addressables` を導入し、`AddressableAssetSettings` を初期化
    2. 方針記載のグループ（Core/Player/Enemies/Stage_*/UI/Audio_*/Events/Debug/Placeholder）作成
    3. ランタイムコードを `AssetReference` / `LoadAssetAsync` 経由に置換 (既存の Inspector 直接参照は常駐オブジェクト間に限定)
    4. Placeholder/Debug ラベルのリリースビルド除外設定
    5. `Audio` / `VFX` 機能実装 (🟡 早期タスク参照) の前段で先行導入することを推奨
  - 優先度: 🟡 Medium (Audio/VFX 着手時に必須化)

- [ ] **セーブ機能のランタイム配線（包括レビュー 2026-04-18）** — `SaveManager` + `SaveDataStore` は実装済み・結合テスト済みだが、ランタイムコード（GameManager/シーン遷移/UI）からの呼び出し0件
  - チェックポイント・タイトル画面・シーン遷移で `SaveManager.Save()` → `SaveDataStore.WriteToDisk()` を呼ぶ経路を追加
  - 新規ゲーム/スロット選択/ロードのUIフロー配線
  - オートセーブのトリガー（マップ遷移、ボス撃破等）

- [x] **`FlagManager.SwitchMap` がストリーミング経路から自動呼び出しされない** — `LevelStreamingOrchestrator.AttachFlagManager(FlagManager)` を新設。`NotifyLoadComplete` で接続済みなら自動 SwitchMap(sceneName)。加えて `OnAreaLoadCompleted` / `OnAreaUnloadCompleted` イベントを公開

- [x] **AreaBoundaryTrigger に進入方向判定なし（包括レビュー 2026-04-18）** — 逆方向（退出）でもロードが発火する。Continuous Collision 要件の明記もない
  - 対象: `Assets/MyAsset/Runtime/Streaming/AreaBoundaryTrigger.cs:28-50`
  - 修正: 進入方向（velocity）判定を追加 + 高速移動での貫通対策を文書化
  - ✅ PR TBD で対応

- [x] **LevelStreamingController にロード失敗時のフォールバック** — `_loadOperation.isDone` 後に `HandleLoadComplete` で `SceneManager.GetSceneByName().IsValid() && isLoaded` を検証。無効なら `Debug.LogError` + `k_FallbackSceneName = "GameScene"` を Additive ロード。テスト用注入フック (`SetTestHooks`) も追加

- [x] **シーン遷移時の Enemy/Projectile 残留** — `LevelStreamingOrchestrator` に `OnAreaUnloadCompleted` イベントを新設、`LevelStreamingController.Initialize` で subscribe して `EnemySpawnerManager.ClearAll` / `ProjectileManager.ClearAll` を呼ぶ。両 ClearAll は既存実装（内部状態が null なら早期 return）

## 実装ゼロ領域（包括レビュー 2026-04-18）

### UI / QoL

- [ ] ポーズ ↔ `Time.timeScale` 連動ポリシー（メニュー/ポーズ画面表示時の挙動統一）
- [ ] 入力リマップUI
- [ ] ファストトラベル（セーブポイント間の移動）
- [ ] マップピン / ミニマップ
- [ ] インベントリの並び替え / お気に入り / カテゴリフィルタ
- [ ] ショップの在庫管理 / 買戻し機能

### セーブシステム拡張

- [ ] オートセーブ（マップ遷移・ボス撃破・チェックポイント通過時のトリガー）
- [ ] スロットメタデータ（プレイ時間 / 最終位置 / スクリーンショット）
- [ ] バージョンマイグレーションレイヤー（`SaveFileData.version` の差分対応）
- [x] 破損検知 + `.bak` フォールバック（JSON読み込み失敗時に前回成功分をロード）
  - ✅ PR #36 (B4) で `SaveDataStore.WriteToDisk` が既存ファイルを `.bak` にコピーしてから上書き、`ReadFromDisk` がパース失敗/entries null 検知時に `.bak` フォールバック
  - ✅ 破損検知ロジックを `TryReadSlotFile` に集約（空ファイル / JSONパース例外 / 必須フィールド欠落 / エントリーデシリアライズ失敗）
  - ✅ `DeleteSlot` は `.bak` も同時削除、テスト `SaveDataStoreBackupTests` 追加

### テスト拡充

- [x] PlayMode テスト拡充（現状7本のみ、Runtime/Camera・Runtime/UI・Runtime/Debug は未テスト）
  - ✅ PR #34 (F4) でカメラ追従 / HUD / AutoInputTester の基本 PlayMode テストを追加
- [x] `Integration_EventLifecycleTests` — イベント購読/解除の対称性テスト（Subject多重購読・リーク検知）
  - ✅ PR #34 (F5) で GameEvents の購読ライフサイクル結合テストを追加
- [x] ガード / パリィ / 状態異常の PlayMode 結合テスト
  - ✅ PR #34 (F6) で結合テストを追加
