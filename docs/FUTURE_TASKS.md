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

- [ ] **長押しロックオン** — ため攻撃のように長押しで射角を変更し、ロックオン対象を切替可能にする
  - 現状: 射撃軌道に最も近い敵を自動ターゲット（ドット積ベース）
  - 追加: 長押し中にターゲット候補をUI表示、射角変更で切替
  - Projectile.TargetHash にロックオン対象を渡す仕組みは実装済み

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

- [x] AnimationBridgeのパラメータキーをstring→int hash化（Animator.StringToHashキャッシュ）
  - 発生元: PR #18 レビュー
  - 対象: `Assets/MyAsset/Core/Animation/AnimationBridge.cs`, `Assets/MyAsset/Runtime/Animation/CharacterAnimationController.cs`

- [x] AnimationBridgeのダーティフラグをパラメータ単位に細分化（全パラメータ再送信の回避）
  - 発生元: PR #18 レビュー
  - 対象: `Assets/MyAsset/Core/Animation/AnimationBridge.cs`

## 設計改善

- [ ] `ResetInternalState()` のユニットテスト追加（各フラグのリセット前後を直接検証）
  - 発生元: PR #25 レビュー
  - 対象: `Assets/MyAsset/Runtime/Character/PlayerCharacter.cs`

- [ ] AutoInputTester の `LogResetState()` と `TakeSnapshot()` のvitals取得パターンをヘルパーメソッドに統合
  - 発生元: PR #25 レビュー
  - 対象: `Assets/MyAsset/Runtime/Debug/AutoInputTester.cs`

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

- [ ] **装備変更→SoA書き戻しパイプライン未実装** — `IEquippable` 実装クラスが0件、`EquipmentStatCalculator.CalculateScaledAttack` の呼び出し元も0件。武器変更で攻撃力が反映されない
  - 対象: `Assets/MyAsset/Core/Equipment/EquipmentHolder.cs` / `EquipmentStatCalculator.cs`
  - 修正: Weapon/Shield/Core 各実装クラスを作り、`OnEquip(ownerHash)` で `EquipmentStatCalculator` を経由して `GameManager.Data.GetCombatStats(ownerHash)` を書き換える

- [ ] **Coyote Time 未実装** — 接地から離れた瞬間にジャンプ不可（`GroundMovementLogic.TryStartJump` が `!isGrounded` で即return）
  - 対象: `Assets/MyAsset/Core/Movement/GroundMovementLogic.cs:64`
  - 修正: 最後に接地した時刻からN秒以内のジャンプ入力を許容するバッファを追加

- [ ] **壁蹴り呼び出し経路ゼロ** — `AdvancedMovementLogic.TryWallKick` は定義済みだが、PlayerCharacter / ActionExecutor からの呼び出し0件
  - 対象: `Assets/MyAsset/Core/Movement/AdvancedMovementLogic.cs:23`
  - 修正: PlayerCharacter のジャンプ入力処理から壁接触判定→`TryWallKick` を呼ぶ経路を追加

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

### AI 配線漏れ（包括レビュー 2026-04-18）

- [x] **`ModeTransitionEditor` が CompanionController から未接続** — CompanionController に `ModeTransitionEditor` インスタンスを保持し `RequestModeSwitch(int modeIndex)` API を公開。手動切替中は `k_ManualOverrideTimeoutSeconds = 5f` の間 `EvaluateTransitions` を抑制、タイムアウト経過で自動切替復帰

- [x] **CompanionAISettings UI: フッターバーのパッド入力配線未実装** — UXML/USS にフッターバー（A/B/X/Y 凡例）を定義済みだが、実際のパッドボタン→UIアクション紐付けがない。誤認識防止に現在は `hidden` クラスで非表示。
  - 対象: `Assets/MyAsset/Runtime/UI/CompanionAISettingsController.cs` / `Assets/MyAsset/UI/CompanionAISettings/CompanionAISettings.uxml`
  - ✅ Phase 3b で対応: UI アクションマップに `MenuSave`（Gamepad West / Ctrl+S）/ `MenuSaveAsPreset`（Gamepad North / Ctrl+Shift+S）を追加。Controller が `InputActionAsset` を受け取って `Cancel`/`MenuSave`/`MenuSaveAsPreset` を購読し、入力アセット注入時のみ footer-bar の `hidden` クラスを実行時に外す（未注入なら非表示維持）。A（Submit）は UI モジュール経由のフォーカス済みボタンクリックで既存挙動。

- [x] **EnemyController に `BroadcastActionHandler` 未登録** — Attack/Cast/Instant/Sustained に加えて Broadcast も登録。挑発・集合指示系の Broadcast 行動が敵AIで実行可能になった

- [x] **デフォルト行動「何もしない」Idle アクションの runtime 実装** (部分完了) — `SustainedAction.Idle` enum 追加、ラベル・メタデータ・ActionTypeRegistry unlock 登録済み。SustainedActionHandler は paramId を保持するだけなので Idle も既存ハンドラで no-op 動作する
  - 残タスク: `CompanionAISettingsLogic.GcOrphanActionSlots` で default を Idle に正規化、`AIMode` 新規作成時の default 初期値を Idle に（UI 側の変更）
  - 優先度: 🟢 Low（UI UX 整合のための後続タスク。既存モードを壊さないよう慎重に）

- [x] **CompanionMpManager の MP総和が CeilToInt で消失** — `_reserveTransferCarry` accumulator で端数を累積し、`FloorToInt` で双方同時に動かす対称構造に修正。細かい dt で連続 Tick しても MP 総和は減らない

- [x] **混乱解除時の即時AI再評価なし** — 基盤 (`JudgmentLoop.ForceEvaluate()` / `GameEvents.OnConfusionCleared` / `ConfusionEffectProcessor(GameEvents)`) に加え、`EnemyController` / `CompanionController` の AIBrain 配線も完了。両 Controller は `IDisposable` 実装で R3 購読を管理し、自ハッシュ一致のイベントにのみ `ForceEvaluate()` を呼ぶ。`EnemyCharacter` / `CompanionCharacter` が `GameManager.Events` 注入＋ `OnDestroy` で `Dispose` 呼出し
  - 補足: Runtime `BossController` MonoBehaviour が未実装 (下記参照) のため Boss 側の配線は対応外。Runtime BossController 実装時に同じパターン (`BossControllerLogic` が `GameEvents` 注入 → 自ハッシュ一致で `ForceEvaluate`) を踏襲すること

- [ ] **BossPhaseManager の `invincibleTime` が BossController 側で適用されているか要検証** → **Runtime BossController 未実装のため未配線**
  - 現状: `BossControllerLogic.UpdateEncounter` は `BossPhaseTransitionResult.invincibleTime` を返すが、呼び出し元は EditMode テストのみ。Runtime 側の MonoBehaviour BossController が未実装のため、`DamageReceiver` の無敵フラグへの伝播も未配線
  - 対象: `Assets/MyAsset/Core/AI/Boss/BossController.cs`（未実装）/ `BossPhaseManager.cs`
  - 修正: Runtime `BossController` MonoBehaviour を新設する際に `UpdateEncounter` 戻り値の `invincibleTime` を `DamageReceiver.SetInvincible(duration)` (新設) に伝播する
  - 優先度: 🟡 Medium（Runtime BossController 実装と併せて対応する大タスク）

- [x] **CompanionAISettings UI で `AIMode.targetRules` / `targetSelects` が編集不可** — PR #32 (Phase 5 統合UI) で対応済み。`ShowModeDetailDialog` に「ターゲット選定」(`RebuildTargetSelectList` Dialogs.cs:2393) + 「ターゲット切替ルール」(`RebuildTargetRuleList` Dialogs.cs:2494) セクションあり、`AdjustTargetRulesForRemovedSelect` Logic で削除時の正規化もカバー済み

- [ ] **CompanionAISettings UI: TargetFilter IsSelf/IsPlayer 自動クリアの回帰テスト欠落** — `BuildTargetFilterSection` で Foldout 構築時に IsSelf/IsPlayer ビットを自動クリア＋onChanged 通知する修正が入っているが、これを検証するユニットテストがない
  - 対象: `Assets/MyAsset/Runtime/UI/CompanionAISettingsController.Dialogs.cs` / `Assets/Tests/EditMode/Integration_CompanionAISettingsFlowTests.cs`
  - 修正: 古い Config（IsSelf/IsPlayer 付き）をロード→Foldout を開く→保存、で自動的に bit がクリアされることを検証するテストを追加
  - 優先度: 🟡 Medium

- [ ] **CompanionAISettings UI: `RefreshShortcutDropdowns` が描画関数内でバッファを書き換える** — `Controller.cs:528-530` で描画関数が `bindings[i] = -1` の副作用を持つ。`_isDirty` も立たないため整合性が疑わしい
  - 対象: `Assets/MyAsset/Runtime/UI/CompanionAISettingsController.cs:528-530`
  - 修正: `CompanionAISettingsLogic` に `ClearInvalidShortcutBindings()` API を追加し、Switch/Add/Remove 時に Logic 側で明示的にクリーンアップ
  - 優先度: 🟡 Medium

- [ ] **CompanionAISettings UI: `AttachTooltipHandlers` の多重登録リスク** — `RefreshEditor` 毎に対象要素へ tooltip ハンドラを再登録するが、前回分の解除は `OnDisable` まで走らない。ダイアログを開閉する度に `_unsubscribeActions` が単調増加
  - 対象: `Assets/MyAsset/Runtime/UI/CompanionAISettingsController.cs:230-280 付近`
  - 修正: `element.userData` に登録済みフラグを持たせる、または Dialog スコープごとに unsubscribe セットを分離
  - 優先度: 🟡 Medium（長時間使い続けるとメモリ・CPU に影響）

- [x] **CompanionAISettings UI: マジックナンバー `ExecuteLater(50)` 等** — `k_DialogFocusDelayMs = 50` として const 化。CompanionAISettingsController.cs / Dialogs.cs の 5 箇所を置換

- [ ] **CompanionAISettings UI: Dialogs.cs が 1912 行で巨大** — 将来 `BuildTargetFilterSection` / `BuildConditionInputWidgets` / `ShowActionPickerDialog` を別 partial に分割
  - 優先度: 🟢 Minor（機能追加時に分割）

- [ ] **CompanionAISettings UI: `BuildConditionRow` / `BuildAttackPickerContent` の毎回アロケーション削減** — `Enum.GetValues` / `new Dictionary` を static キャッシュ化
  - 優先度: 🟢 Minor

- [ ] **CompanionAISettings UI: pattern simulation テストを実装直接テストへ移行** — PR #26 で追加した `Integration_CompanionAISettingsFlowTests.CompanionAISettings_UILike_*` は UI 実装の closure を自前で再現している。実装本体 (`RebuildActionSlotList`/`RebuildActionRuleList`) が将来 AIMode 値渡しに戻されてもテストは通る可能性がある
  - 修正案:
    1. `Assets/MyAsset/Runtime/UI/Game.UI.asmdef` (存在すれば) に `InternalsVisibleTo("Game.Tests.EditMode")` 属性を追加
    2. `RebuildActionSlotList` / `RebuildActionRuleList` を `internal` に公開
    3. テストから直接 VisualElement コンテナ + closure を渡して呼び、Button の clickable をシミュレート
  - 対象: `Assets/MyAsset/Runtime/UI/CompanionAISettingsController.Dialogs.cs` / `Assets/Tests/EditMode/Integration_CompanionAISettingsFlowTests.cs`
  - 優先度: 🟢 Minor（テストの堅牢性向上目的、機能に影響なし）

- [ ] **CompanionAISettings UI: Edit button が setter を経由しない** — `RebuildActionSlotList` / `RebuildActionRuleList` の「変更」ボタンは getter で取得した配列を直接 `arr[idx] = picked` で書き換える。現状は配列参照型の性質で動作するが、将来 setter に dirty flag 立てや validation を追加した場合 silently skip される
  - 対象: `Assets/MyAsset/Runtime/UI/CompanionAISettingsController.Dialogs.cs` (RebuildActionSlotList/RebuildActionRuleList の edit button)
  - 修正: Edit 経路も Remove 経路と同様に「新配列を生成して setter 経由」に揃える
  - 優先度: 🟢 Minor

### 戦闘機構の未接続・未実装（包括レビュー 2026-04-18）

- [x] **クリティカル機構が未接続** → **仕様外として確定 + クリーンアップ済み**: クリティカルヒット機構は本プロジェクトでは採用しない。`DamageCalculator.IsCritical` / `ApplyCritical` / `GetWeaknessMultiplier` API、`CombatStats.criticalRate/criticalMultiplier` フィールド、`GameConstants.k_DefaultCritical*` 定数、`DamageCalculator.Calculate*` の `weakElement` 引数をすべて削除。SoA 構造体のサイズを 8 byte/キャラ削減

- [x] **`knockbackResistance` 未実装** — `CombatStats.knockbackResistance` フィールド追加、`CharacterInfo.knockbackResistance` から GameManager で配線。`DamageReceiver.ApplyKnockback` が `HpArmorLogic.CalculateKnockback(force, combat.knockbackResistance)` を呼ぶ

- [ ] **EnhancedGuard が JustGuardImmune 攻撃にも成立** — 要検証（ガード成立判定に AttackFeature.JustGuardImmune の除外処理を追加）
  - 対象: `Assets/MyAsset/Core/Damage/GuardJudgmentLogic.cs`

- [x] **Action armor 消費が EffectState に書き戻されない** — 修正済み。ActionEffectProcessor.EffectState は毎回 Evaluate で再計算される struct のため、ref 経由で削られた値は局所変数にしか残らなかった
  - 対応: DamageReceiver に `_actionArmorConsumed` 累積フィールドを追加し、effectState.actionArmorValue から差し引いて正味の残量を HpArmorLogic.ApplyDamage に渡す。消費量は毎ヒット加算、SetActionEffects/ClearActionEffects/ResetInternalState で 0 にリセット
  - テスト: Integration_DamageReceiverStateTests.cs 追加

- [x] **HitReaction 結果が ActState に書き込まれない** — 修正済み。HitReactionLogic.Determine の戻り値は DamageResult に格納されるだけで、SoA の CharacterFlags.ActState に反映されていなかった
  - 対応: DamageReceiver.ReceiveDamage 内に ApplyHitReactionToActState を追加。Flinch → ActState.Flinch / Knockback → Knockbacked / GuardBreak → GuardBroken / 死亡時 → Dead を書き戻す。次ヒット判定で HitReactionLogic.IsInHitstun が正しく評価されるようになる
  - テスト: Integration_DamageReceiverStateTests.cs 追加

### Runtime Bridge 整理（包括レビュー 2026-04-18）

- [x] **`InputBuffer` デッドコード** — ランタイム呼び出しゼロで tests 2 本のみ参照していたため、クラス本体 + .meta + テストを削除 (2026-04-22)。先行入力が必要になったら PlayerInputHandler に統合する形で再導入する

- [x] **`ActionCostValidator` の float→int キャストでMPコスト小数切り捨て** — int 型一本化で確定 (2026-04-22)
  - `CanAfford` / `DeductCost` の `mpCost` を `int` に変更、`(int)mpCost` キャスト削除
  - `AttackInfo.mpCost` / `AttackMotionData.mpCost` も `int` に揃え、破壊的変更を全呼び出し元に反映
  - 境界値テスト追加 (ジャスト消費 / 超過時 0 クランプ / currentMp == mpCost で実行可能)
  - 備考: `SkillConditionLogic.CanUseSkill` は `float mpCost` のままで、`ItemUsageLogic.TryUseMagic` / `CompanionMpManager` も独立したドメインのため変更対象外。将来的に統合する場合は全体方針を決めた上で一括リファクタリング

- [x] **`GameManager.Awake()` で `GetComponentInChildren` を直列3回** — `IGameSubManager` に統合。ProjectileManager(300) / EnemySpawnerManager(200) / LevelStreamingController(100) が `IGameSubManager` を実装し、`GetComponentsInChildren<IGameSubManager>(true)` 1 回で取得 → InitOrder 昇順でソートして一括 `Initialize(data, events)`。既存公開プロパティ（Projectiles/EnemySpawner/LevelStreaming）は型別キャッシュで維持

### Movement 拡張機能（包括レビュー 2026-04-18）

- [x] **カメラシェイクAPI** — 被弾・爆発・着地強調のフィードバック用基盤を実装
  - `Assets/MyAsset/Core/Camera/CameraShakeParams.cs` — イベントペイロード (magnitude/duration/frequency)
  - `Assets/MyAsset/Runtime/Camera/CameraShakeController.cs` — Perlin ノイズ駆動 MonoBehaviour (GameScene の Main Camera に付与想定)
  - `GameEvents.OnCameraShakeRequested` + `FireCameraShakeRequested(CameraShakeParams)` を追加し、Controller が subscribe
  - 残タスク (呼び出し経路の配線):
    - [ ] `DamageReceiver` で被弾時に `GameManager.Events.FireCameraShakeRequested(new(magnitude, duration))` を発火
    - [ ] 爆発 `ProjectileManager.ProcessExplosion` から同上
    - [ ] 着地強調 (高所落下) を `CharacterCollisionController` 等から同上
    - [ ] ポーズ中でもシェイクさせたい場合は `CameraShakeController.Update` を `Time.unscaledDeltaTime` に切替 (TODO コメント済み)
- [ ] **動的カメラBounds切替未実装** — エリア遷移時のカメラ範囲切替
- [ ] **Look-Ahead未実装** — プレイヤー移動方向に先行してカメラを動かす機能
- [ ] **坂道法線取得なし** — 傾斜移動・坂ジャンプ方向未対応（Ground判定は水平想定）
- [ ] **一方通行プラットフォーム（drop-through）未実装** — 下入力＋ジャンプで床を抜ける機能
- [ ] **GroundMovementLogic の移動定数がデータ駆動化されていない** — DodgeDuration/SprintSpeedMultiplier/DodgeSpeedMultiplier 等がハードコード。CharacterInfo で調整不可
  - 対象: `Assets/MyAsset/Core/Movement/GroundMovementLogic.cs:12-16`
  - 修正: MoveParams / CharacterInfo に相当フィールド追加

## バリデーション

- [ ] `CollisionMatrixSetup` の設定を起動時に自動チェックする仕組みを追加（レイヤー設定ミス防止）
  - 発生元: PR #17 レビュー
  - 対象: `Assets/MyAsset/Editor/CollisionMatrixSetup.cs`

- [x] **CurrencyManager.Add で int オーバーフロー未検知** — `int.MaxValue - _balance` 比較で飽和演算、超過時は `int.MaxValue` にクランプ

- [x] **InventoryManager.Add が同一itemIdの複数スタックを探索しない** — 2 pass化。既存スタック全走査で順次充填→余剰分は新スロットに追加 (k_MaxSlotCount=99 上限まで)。メソッド名は既存呼び出し (ShopLogic, SaveSystem テスト) との整合性を保つため `Add` に統一
  - 備考: 特定のローカル開発環境で Unity の JIT/ランタイムキャッシュが腐って `InventorySystemTests.InventoryManager_Add_OverflowsToNewStack` / `InventoryManager_Add_FillsExistingStacksBeforeCreatingNew` が旧実装相当の挙動を返す現象を確認（`ilspycmd` で DLL IL は新実装確認済み）。CI クリーン環境では通る想定のため `[Ignore]` は付与せずそのまま。ローカルで同現象が起きたら **Library/ 全削除 + Unity 完全再起動** で解消する

- [x] **LevelUpLogic に最大レベルキャップなし** — `k_MaxLevel = 99` 追加。AddExp の while 条件に `_level < k_MaxLevel`、到達時は余剰 exp を破棄
  - 残タスク:
    - ステータスポイント refund API (振り直し)
    - `k_MaxLevel` をゲームバランス調整で差し替えやすいよう ScriptableObject 化 (現状は public const でコード変更必須)
    - 各ステータス (Str/Dex/Intel/Vit/Mnd/End) の上限値設定。現在 AllocatePoint は無制限で振れるので、99レベル×3pts=297pts を1項目に全振り可能。想定範囲を決めてバリデーション追加

- [x] **GateStateController.ForceClose が isPermanent を無視** — isPermanent=true (ボスクリア等の永続ゲート) の場合は ForceClose をスキップするよう修正。戻り値を `bool` にして実際に閉じたかを呼び出し元が判定できるように変更。GateSystem_OpenCloseTests に一時ゲート/永続ゲートの回帰テスト追加

- [x] **SoACharaDataDic の固定容量超過検知** — `GameManager.WarnIfCapacityUsageHigh` で使用率 80% 以上で `Debug.LogWarning`。開発ビルド限定 (`UNITY_EDITOR` / `DEVELOPMENT_BUILD`) で `Conditional` 属性によりリリースはゼロコスト。閾値を下回ったらフラグ復帰して再警告可能

## 統合待ち

- [ ] `OnCollisionEnter2D` の `other.gameObject.GetInstanceID()` をプロファイルし、高頻度衝突シーンで問題がないか確認
  - 発生元: PR #17 レビュー
  - 対象: `Assets/MyAsset/Runtime/Collision/CharacterCollisionController.cs`

- [ ] **Addressable 導入（包括レビュー 2026-04-18）** — `.claude/rules/asset-workflow.md` にグループ/ラベル/アドレス命名規則を定めているが、`Assets/MyAsset` 内で `Addressables.` / `AssetReference` / `LoadAssetAsync` 使用0件
  - 方針記載のグループ（Core/Player/Enemies/Stage_*/UI/Audio_*/Events/Debug/Placeholder）作成
  - ランタイムコードを `AssetReference` / `LoadAssetAsync` 経由に置換
  - Placeholder/Debug ラベルのリリースビルド除外設定

- [ ] **セーブ機能のランタイム配線（包括レビュー 2026-04-18）** — `SaveManager` + `SaveDataStore` は実装済み・結合テスト済みだが、ランタイムコード（GameManager/シーン遷移/UI）からの呼び出し0件
  - チェックポイント・タイトル画面・シーン遷移で `SaveManager.Save()` → `SaveDataStore.WriteToDisk()` を呼ぶ経路を追加
  - 新規ゲーム/スロット選択/ロードのUIフロー配線
  - オートセーブのトリガー（マップ遷移、ボス撃破等）

- [x] **`FlagManager.SwitchMap` がストリーミング経路から自動呼び出しされない** — `LevelStreamingOrchestrator.AttachFlagManager(FlagManager)` を新設。`NotifyLoadComplete` で接続済みなら自動 SwitchMap(sceneName)。加えて `OnAreaLoadCompleted` / `OnAreaUnloadCompleted` イベントを公開

- [ ] **AreaBoundaryTrigger に進入方向判定なし（包括レビュー 2026-04-18）** — 逆方向（退出）でもロードが発火する。Continuous Collision 要件の明記もない
  - 対象: `Assets/MyAsset/Runtime/Streaming/AreaBoundaryTrigger.cs:28-50`
  - 修正: 進入方向（velocity）判定を追加 + 高速移動での貫通対策を文書化

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
- [ ] 破損検知 + `.bak` フォールバック（JSON読み込み失敗時に前回成功分をロード）

### テスト拡充

- [ ] PlayMode テスト拡充（現状7本のみ、Runtime/Camera・Runtime/UI・Runtime/Debug は未テスト）
- [ ] `Integration_EventLifecycleTests` — イベント購読/解除の対称性テスト（Subject多重購読・リーク検知）
- [ ] ガード / パリィ / 状態異常の PlayMode 結合テスト
