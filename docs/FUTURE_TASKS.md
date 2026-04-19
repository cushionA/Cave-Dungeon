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

- [ ] `ResetInternalState()` のユニットテスト追加（各フラグのリセット前後を直接検証）
  - 発生元: PR #25 レビュー
  - 対象: `Assets/MyAsset/Runtime/Character/PlayerCharacter.cs`

- [ ] AutoInputTester の `LogResetState()` と `TakeSnapshot()` のvitals取得パターンをヘルパーメソッドに統合
  - 発生元: PR #25 レビュー
  - 対象: `Assets/MyAsset/Runtime/Debug/AutoInputTester.cs`

- [ ] `CharacterCollisionController.SyncCarriedPosition` の呼び出し元をコメントで明記（ActionExecutor.FixedUpdate から呼ぶ想定）
  - 発生元: PR #17 レビュー
  - 対象: `Assets/MyAsset/Runtime/Collision/CharacterCollisionController.cs`

### 戦闘系データ配線（包括レビュー 2026-04-18）

コア論理は実装されているが、接続部で配線漏れがある。ゲーム挙動が仕様通りにならないため、コンテンツ実装前に整合を取る。

- [x] **justGuardResistance が BuildMotionData でコピーされない** — AttackInfo から AttackMotionData への変換で欠落し、ジャストガード時の軽減が常に0で機能しない
  - 対象: `Assets/MyAsset/Core/Common/CombatDataHelper.cs:17-40`
  - ✅ PR「refactor/ガードシステム整合化」で BuildMotionData に justGuardResistance コピー追加

- [ ] **`weakElement` が `Element.None` ハードコード** — 弱点1.5倍ボーナスが永久発動しない
  - 対象: `Assets/MyAsset/Runtime/Combat/DamageReceiver.cs:255-256`
  - 前提: キャラ個別の弱点データがSoAにもCharacterInfoにも未定義。データ層追加から必要
  - 修正案: `CombatStats` または `CharacterInfo` に `weakElement` フィールド追加→`CalculateDamage` で defender の weakElement を取得して渡す

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

- [ ] **SoACharaDataDic が手書き実装** — `Packages/com.zabuton.container-pack` のSourceGeneratorが利用可能だが、現状は完全手書き（設計書 `Architect/01` で想定する `[ContainerSetting]` + `partial class` 未使用）
  - 対象: `Assets/MyAsset/Core/DataContainer/SoACharaDataDic.cs`
  - 修正: `[ContainerSetting]` 属性付きの `partial class` 宣言に変更、手書きの Add/Remove/Get/Dispose を自動生成に置換

- [ ] **`BaseCharacter.DamageReceiver` プロパティ未公開 + フィールドキャッシュなし** — Start() と OnPoolAcquire() で `GetComponent<DamageReceiver>()` が重複。設計書では `GameManager.Data.GetManaged(hash).DamageReceiver` 経由アクセスを想定
  - 対象: `Assets/MyAsset/Runtime/Character/BaseCharacter.cs:91,145`
  - 修正: `_damageReceiver` フィールド + `DamageReceiver` プロパティを追加、Awake でキャッシュ

- [ ] **`GameManager.Data.GetManaged(hash)` API 未実装** — 設計書（`Architect/01_データコンテナ.md:51`）で想定する managed（BaseCharacter）ハッシュアクセスが欠落
  - 対象: `Assets/MyAsset/Core/DataContainer/SoACharaDataDic.cs`
  - 修正: `BaseCharacter[] _managedArray` を追加し、`GetManaged(int hash)` でハッシュ→BaseCharacter を返す

- [ ] **SaveManager 内蔵の JsonUtility 永続化（`SetFileIO` 経路）が SaveDataStore と機能重複** — ランタイム配線で SaveDataStore を使う想定なら不要。削除候補
  - 対象: `Assets/MyAsset/Core/Save/SaveManager.cs:47-52,250-282`
  - 方針確認後: `SetFileIO` / `WriteToDisk` / `ReadFromDisk` / `SaveSlotWrapper` を削除

### AI 配線漏れ（包括レビュー 2026-04-18）

- [ ] **`ModeTransitionEditor` が CompanionController から未接続** — ショートカット手動モード切替が動作しない（自動切替のみ機能）
  - 対象: `Assets/MyAsset/Core/AI/Companion/CompanionController.cs` / `Assets/MyAsset/Core/AI/RuleBuilder/ModeTransitionEditor.cs`
  - 修正: CompanionController が ModeTransitionEditor を保持し、手動切替API（`RequestModeSwitch`）を公開。`EvaluateTransitions` より優先判定

- [ ] **EnemyController に `BroadcastActionHandler` 未登録** — 挑発・集合指示系のBroadcast系行動が敵AIで実行不可（Attack/Cast/Instant/Sustainedのみ登録）
  - 対象: `Assets/MyAsset/Core/AI/Enemy/EnemyController.cs:31-35`
  - 修正: `_executor.Register(new BroadcastActionHandler());` を追加

- [ ] **CompanionMpManager の MP総和が CeilToInt で消失** — `_currentMp += fromReserve`（float） + `_reserveMp -= CeilToInt(fromReserve)`（int）のズレで、毎回上方向に丸められて reserveMp が余分に減る
  - 対象: `Assets/MyAsset/Core/AI/Companion/CompanionMpManager.cs:101-107`
  - 修正: float型一本化 or 消費・供給を同じ丸め規則に統一

- [ ] **混乱解除時の即時AI再評価なし** — 要検証
  - 対象: `Assets/MyAsset/Core/Status/StatusEffectManager.cs` 付近
  - 修正: 混乱状態解除時に JudgmentLoop.ForceEvaluate を呼んで即座にターゲットを再選択

- [ ] **BossPhaseManager の `invincibleTime` が BossController 側で適用されているか要検証** — 構造体で値は返すが、実際に被ダメージを無効化する配線の有無を確認
  - 対象: `Assets/MyAsset/Core/AI/Boss/BossController.cs` / `BossPhaseManager.cs`

- [ ] **CompanionAISettings UI で `AIMode.targetRules` / `targetSelects` が編集不可** — JudgmentLoop 第1層（ターゲット選択）が UI 単独生成の Config では空のまま。仲間AI がターゲットを決められない。AIInfoConverter 自動生成経由だと動くが、UI で新規作成された Config は機能しない
  - 対象: `Assets/MyAsset/Runtime/UI/CompanionAISettingsController.Dialogs.cs` (ShowModeDetailDialog)
  - 修正案:
    1. モード詳細ダイアログに「ターゲット選択ルール」セクションを追加（actionRules と同じ形式で AIRule 配列を編集）
    2. `AITargetSelect` 配列の編集UIも追加（sortKey / elementFilter / isDescending / filter）
    3. `CompanionAISettingsLogic` に UpdateTargetRulesInMode / UpdateTargetSelectsInMode API を追加
  - 関連参照: `Assets/MyAsset/Core/Common/Section2Structs.cs` (AIMode 定義) / `Assets/MyAsset/Core/AI/Core/JudgmentLoop.cs` (targetRules/targetSelects 使用箇所)
  - 優先度: 🔴 High（仲間AIのコア機能、UI からの完全カスタムができない）

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

- [ ] **CompanionAISettings UI: マジックナンバー `ExecuteLater(50)` 等** — `k_DialogFocusDelayMs = 50` のような const 化が規約
  - 対象: `Controller.cs:974,992,1016,1059` / `Dialogs.cs:1775` 付近の数値リテラル
  - 優先度: 🟢 Minor

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

- [ ] **クリティカル機構が未接続** — `DamageCalculator.IsCritical` / `ApplyCritical` は実装済みだが `DamageReceiver.BuildResult` で `isCritical = false` ハードコード（UIの `DamagePopupController` は受信側として準備済み）
  - 対象: `Assets/MyAsset/Runtime/Combat/DamageReceiver.cs:333`
  - 修正: `CombatStats.criticalRate` / `criticalMultiplier` を参照し、`DamageCalculator.IsCritical` の判定をダメージ計算経路に挿入

- [ ] **`knockbackResistance` 未実装** — `DamageReceiver.cs:392` に TODO コメントあり、`HpArmorLogic.CalculateKnockback` は受け取る準備済み
  - 対象: `Assets/MyAsset/Core/DataContainer/SoAStructs.cs` / `DamageReceiver.cs:392`
  - 修正: `CombatStats` に `knockbackResistance` フィールド追加→ DamageReceiver から参照

- [ ] **EnhancedGuard が JustGuardImmune 攻撃にも成立** — 要検証（ガード成立判定に AttackFeature.JustGuardImmune の除外処理を追加）
  - 対象: `Assets/MyAsset/Core/Damage/GuardJudgmentLogic.cs`

- [ ] **Action armor 消費が EffectState に書き戻されない** — 要検証（ApplyDamage で ref 経由の値が反映されるか、ActionEffectProcessor 側の保持経路確認）

- [ ] **HitReaction 結果が ActState に書き込まれない** — 要検証（Flinch/GuardBroken 遷移後の次ヒット判定に影響）

### Runtime Bridge 整理（包括レビュー 2026-04-18）

- [ ] **`InputBuffer` デッドコード** — クラス定義あるが `new InputBuffer(` の呼び出し0件。PlayerInputHandler に統合するか削除
  - 対象: `Assets/MyAsset/Core/Input/InputBuffer.cs`

- [ ] **`ActionCostValidator` の float→int キャストでMPコスト小数切り捨て** — `CanAfford` と `DeductCost` の両方で `(int)mpCost` キャストしており整合性は保たれるが、小数MPコスト（例: 0.5）が意図通りにならない可能性
  - 対象: `Assets/MyAsset/Core/Combat/ActionCostValidator.cs:22,37`
  - 方針確認後: int型一本化 or 小数対応

- [ ] **`GameManager.Awake()` で `GetComponentInChildren` を直列3回** — IGameSubManager パターンへの統合検討
  - 対象: `Assets/MyAsset/Runtime/GameManager.cs:50-66`
  - 修正: ProjectileManager / EnemySpawnerManager / LevelStreamingController を IGameSubManager として登録し、GameManagerCore が一括初期化

### Movement 拡張機能（包括レビュー 2026-04-18）

- [ ] **カメラシェイクAPI未実装** — 被弾・爆発・着地強調のフィードバック不可
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

- [ ] **CurrencyManager.Add で int オーバーフロー未検知（包括レビュー 2026-04-18）** — 長時間プレイで通貨がオーバーフローする可能性
  - 対象: `Assets/MyAsset/Core/Economy/CurrencyManager.cs:24-32`
  - 修正: `if (_balance > int.MaxValue - amount)` で飽和演算（int.MaxValueクランプ）

- [ ] **InventoryManager.Add が同一itemIdの複数スタックを探索しない（包括レビュー 2026-04-18）** — 満杯の先頭スタックで止まり、他に空きがあっても追加不可。最大スロット上限もなし
  - 対象: `Assets/MyAsset/Core/Inventory/InventoryManager.cs:60-89`
  - 修正: 複数スタックを走査し残り分を新スタックへ流す + `k_MaxSlotCount` 制限追加

- [ ] **LevelUpLogic に最大レベルキャップなし + ステータスポイント refund 未対応（包括レビュー 2026-04-18）**
  - 対象: `Assets/MyAsset/Core/LevelUp/LevelUpLogic.cs`
  - 修正: `k_MaxLevel` 定数 + 振り直し（refund）API 追加

- [ ] **GateStateController.ForceClose が isPermanent を無視 — 要検証（包括レビュー 2026-04-18）** — 永続ゲートを強制クローズできるか仕様確認
  - 対象: `Assets/MyAsset/Core/World/Gate/GateController.cs:47-50`

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

- [ ] **`FlagManager.SwitchMap` がストリーミング経路から自動呼び出しされない（包括レビュー 2026-04-18）** — マップローカルフラグの切替が配線されていない
  - 対象: `Assets/MyAsset/Core/Streaming/LevelStreamingOrchestrator.cs` / `Core/Save/FlagManager.cs:61`
  - 修正: AreaLoad 完了イベントで FlagManager.SwitchMap(mapId) を呼ぶ購読を追加

- [ ] **AreaBoundaryTrigger に進入方向判定なし（包括レビュー 2026-04-18）** — 逆方向（退出）でもロードが発火する。Continuous Collision 要件の明記もない
  - 対象: `Assets/MyAsset/Runtime/Streaming/AreaBoundaryTrigger.cs:28-50`
  - 修正: 進入方向（velocity）判定を追加 + 高速移動での貫通対策を文書化

- [ ] **LevelStreamingController にロード失敗時のフォールバックなし — 要検証（包括レビュー 2026-04-18）**
  - 対象: `Assets/MyAsset/Runtime/Streaming/LevelStreamingController.cs:43-65`
  - 修正: `_loadOperation.isDone` 後に失敗検知（シーンが有効か）+ 既定シーンへのフォールバック

- [ ] **シーン遷移時の Enemy/Projectile 残留 — 要検証（包括レビュー 2026-04-18）** — エリア切替時の `EnemySpawnerManager.ClearAll` / `ProjectileManager.ReleaseAll` の呼び出し経路確認

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
