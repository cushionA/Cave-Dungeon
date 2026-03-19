# Sprint: Section 2 — AI・仲間・連携

## 完了条件
仲間AIがプレイヤーに追従し、3層判定ループで自律行動する。プレイヤーがスタンス切替・連携ボタンで仲間スキル発動+コンボ入力が機能する。敵がスポーンしAI判定で戦闘し、撃破時にドロップ（EXP/通貨/アイテム）する。魔法ProjectileがJob System並列移動する。ゲートがエリア進行を制御する。AIルールビルダーで仲間AIをカスタマイズできる。

## 既存機能の活用
| 既存機能 | 対応方法 | 備考 |
|----------|---------|------|
| Common_SharedTypes | 拡張 | Section 2の共通型・Enumを追加 |
| DataContainer_SoACore | そのまま利用 | AI/Enemy/Companionデータ追加 |
| GameManager_Core | そのまま利用 | 新イベント追加（OnEnemyDefeated等） |
| InputSystem_Core | そのまま利用 | cooperationPressed入力追加 |
| PlayerMovement | そのまま利用 | Companion追従ターゲット |
| DamageSystem | そのまま利用 | AI/魔法からの呼び出し |
| MapSystem | そのまま利用 | GateSystem連携 |
| SaveSystem_Core | そのまま利用 | Gate永続化、AIPreset保存 |
| InventorySystem | そのまま利用 | KeyGate判定 |
| WeaponSystem_SkillChargeAerial | 参照 | 飛翔体生成をProjectileSystemに統合 |

## 実装順序 — システム系

全38機能。36がシステム系、2がコンテンツ系。

### Layer 0（依存: Section 1のみ）

| # | 機能名 | システム | カテゴリ | テスト数目安 | 依存 | 状態 |
|---|--------|---------|----------|------------|------|------|
| 1 | Common_Section2Types | Common | system | 5 | Common_SharedTypes | pending |
| 2 | AICore_ActionSlotAndBase | AICore | system | 5 | Common_Section2Types, DataContainer_SoACore | pending |
| 3 | AICore_ConditionEvaluator | AICore | system | 5 | Common_Section2Types | pending |

### Layer 1（← Layer 0）

| # | 機能名 | システム | カテゴリ | テスト数目安 | 依存 | 状態 |
|---|--------|---------|----------|------------|------|------|
| 4 | AICore_TargetSelector | AICore | system | 5 | AICore_ConditionEvaluator | pending |
| 5 | AICore_ActionExecutor | AICore | system | 5 | AICore_ActionSlotAndBase | pending |
| 6 | AICore_DamageScoreTracker | AICore | system | 4 | Common_Section2Types | pending |
| 7 | AICore_SensorSystem | AICore | system | 4 | DataContainer_SoACore | pending |

### Layer 2（← Layer 0-1）

| # | 機能名 | システム | カテゴリ | テスト数目安 | 依存 | 状態 |
|---|--------|---------|----------|------------|------|------|
| 8 | AICore_JudgmentLoop | AICore | system | 5 | AICore_TargetSelector, AICore_ActionExecutor | pending |
| 9 | AICore_ModeController | AICore | system | 4 | AICore_JudgmentLoop | pending |
| 10 | AICore_DeliberationBuffer | AICore | system | 3 | AICore_JudgmentLoop | pending |
| 11 | MagicSystem_ProjectileCore | MagicSystem | system | 5 | DamageSystem_CoreCalculation, DataContainer_SoACore | pending |
| 12 | MagicSystem_ProjectileMovement | MagicSystem | system | 5 | MagicSystem_ProjectileCore | pending |

### Layer 3（← Layer 0-2）

| # | 機能名 | システム | カテゴリ | テスト数目安 | 依存 | 状態 |
|---|--------|---------|----------|------------|------|------|
| 13 | MagicSystem_BulletFeatures | MagicSystem | system | 5 | MagicSystem_ProjectileCore | pending |
| 14 | MagicSystem_CastingFlow | MagicSystem | system | 5 | MagicSystem_ProjectileCore, AICore_ActionSlotAndBase | pending |
| 15 | MagicSystem_HitProcessing | MagicSystem | system | 4 | MagicSystem_ProjectileCore, DamageSystem_CoreCalculation | pending |
| 16 | CompanionAI_FollowBehavior | CompanionAI | system | 4 | AICore_ModeController, PlayerMovement_GroundMovement | pending |
| 17 | CompanionAI_StanceManager | CompanionAI | system | 4 | AICore_JudgmentLoop | pending |
| 18 | CompanionAI_Controller | CompanionAI | system | 5 | CompanionAI_FollowBehavior, CompanionAI_StanceManager | pending |
| 19 | EnemySystem_Controller | EnemySystem | system | 5 | AICore_ModeController, DamageSystem_CoreCalculation | pending |
| 20 | EnemySystem_SpawnManagement | EnemySystem | system | 4 | EnemySystem_Controller | pending |

### Layer 4（← Layer 0-3）

| # | 機能名 | システム | カテゴリ | テスト数目安 | 依存 | 状態 |
|---|--------|---------|----------|------------|------|------|
| 21 | EnemySystem_DropTable | EnemySystem | system | 4 | EnemySystem_Controller | pending |
| 22 | EnemySystem_LootAndReward | EnemySystem | system | 4 | EnemySystem_DropTable, CurrencySystem_Core, LevelUpSystem_Core | pending |
| 23 | EnemySystem_Pool | EnemySystem | system | 3 | EnemySystem_SpawnManagement | pending |
| 24 | CompanionAI_CoopInterruption | CompanionAI | system | 4 | CompanionAI_Controller | pending |
| 25 | CoopAction_CoreAndCombo | CoopAction | system | 5 | CompanionAI_CoopInterruption, InputSystem_Core | pending |
| 26 | CoopAction_CooldownTracker | CoopAction | system | 5 | CoopAction_CoreAndCombo | pending |
| 27 | GateSystem_ConditionCheck | GateSystem | system | 5 | MapSystem_MinimapAndWorld, SaveSystem_Core, InventorySystem_ItemManagement | pending |
| 28 | GateSystem_OpenClose | GateSystem | system | 4 | GateSystem_ConditionCheck | pending |

## 実装順序 — コンテンツ系

### Layer 5（← Layer 0-4）

| # | 機能名 | システム | カテゴリ | テスト数目安 | 依存 | 状態 |
|---|--------|---------|----------|------------|------|------|
| 29 | CoopAction_WarpAction | CoopAction | content | 4 | CoopAction_CoreAndCombo, AICore_TargetSelector | pending |
| 30 | CoopAction_ShieldAction | CoopAction | content | 4 | CoopAction_CoreAndCombo, MagicSystem_ProjectileCore | pending |
| 31 | CooldownReward_Feedback | CooldownReward | system | 3 | CoopAction_CooldownTracker | pending |
| 32 | GateSystem_Registry | GateSystem | system | 3 | GateSystem_OpenClose, SaveSystem_Core | pending |
| 33 | GateSystem_HintAndMap | GateSystem | system | 3 | GateSystem_OpenClose, MapSystem_MinimapAndWorld | pending |
| 34 | AIRuleBuilder_ActionRegistry | AIRuleBuilder | system | 4 | CompanionAI_Controller | pending |
| 35 | AIRuleBuilder_ModeEditor | AIRuleBuilder | system | 5 | AIRuleBuilder_ActionRegistry, AICore_ModeController | pending |

### Layer 6（← Layer 0-5）

| # | 機能名 | システム | カテゴリ | テスト数目安 | 依存 | 状態 |
|---|--------|---------|----------|------------|------|------|
| 36 | AIRuleBuilder_TransitionAndShortcut | AIRuleBuilder | system | 4 | AIRuleBuilder_ModeEditor | pending |
| 37 | AIRuleBuilder_PresetManager | AIRuleBuilder | system | 4 | AIRuleBuilder_ModeEditor, SaveSystem_Core | pending |
| 38 | AIRuleBuilder_Validator | AIRuleBuilder | system | 3 | AIRuleBuilder_ModeEditor | pending |

## 機能詳細

### 1. Common_Section2Types
Section 2で共有するユーティリティクラスとデータ型。ComboWindowTimer（入力受付ウィンドウ）、CooldownTracker（汎用クールダウン管理）、ActionInterruptHandler（アクション中断・復帰）。共通Enum（GateType, ActionExecType, InstantAction, SustainedAction, BroadcastAction）。共通Struct（ActionSlot, AICondition, AIRule, AITargetSelect, AIMode, DamageScoreEntry, TargetFilter, ReactionTrigger）。
- テスト: ComboWindowTimer開閉判定, CooldownTracker開始/完了/リセット, ActionInterruptHandler中断/復帰, ActionSlot生成, AICondition評価基盤

### 2. AICore_ActionSlotAndBase
ActionSlot統一データ構造（5種execType: Attack/Cast/Instant/Sustained/Broadcast）。ActionBase抽象クラスと5つの派生クラス（AttackAction, CastAction, InstantAction, SustainedAction, BroadcastAction）。
- テスト: ActionSlot各execType生成, ActionBase.Execute呼び出し, 各派生クラスの基本動作, パラメータ取得, displayName設定

### 3. AICore_ConditionEvaluator
12種類のAICondition評価（Count, HpRatio, Distance, DamageScore, EventFired, Timer, Random, HasStatus, AbilityFlag, MpRatio, StaminaRatio, IsState）。AND/OR結合。
- テスト: HpRatio条件判定, Distance条件判定, DamageScore閾値, 複合AND条件, Random確率判定

### 4. AICore_TargetSelector
TargetFilter適用 + TargetSortKey（11種: Distance, HpRatio, DamageScore, ThreatLevel, Count等）によるターゲット選択。AITargetSelectデータからの評価。
- テスト: 距離ソート, HpRatio最小選択, DamageScore最大選択, フィルタ除外, 対象なし時null

### 5. AICore_ActionExecutor
Dictionary<ActionExecType, ActionBase>によるswitch文排除ディスパッチ。アクション登録・実行・完了通知。
- テスト: アクション登録, execType→正しいAction実行, 未登録type→デフォルト, 実行中フラグ, 完了コールバック

### 6. AICore_DamageScoreTracker
キャラクターごとの累積ダメージスコア追跡。時間減衰。最大スコアキャラ取得。
- テスト: ダメージ加算, 時間減衰適用, 最大スコア取得, 対象死亡時クリア

### 7. AICore_SensorSystem
視覚（視野角+距離）、聴覚（音量+距離）、近接（距離のみ）検知。検知結果→CharacterFlagsに書き込み。
- テスト: 視野内検知, 視野外非検知, 壁越し遮蔽, 聴覚距離減衰

### 8. AICore_JudgmentLoop
3層判定ループ（ターゲット選択→アクション選択→デフォルトフォールバック）。targetRules[]優先順位評価、actionRules[]優先順位評価。
- テスト: targetRules優先順評価, actionRules優先順評価, 全ルール不一致→default, judgeInterval制御, ルール空時フォールバック

### 9. AICore_ModeController
AIModeの切替管理。モード遷移条件評価。現在モードのjudgeInterval管理。
- テスト: モード初期設定, 条件一致→遷移, 遷移優先順位, 遷移なし→現在維持

### 10. AICore_DeliberationBuffer
難易度別の決定遅延（Easy: 0.3-0.6s, Normal: 0.1-0.3s, Hard: 0.03-0.13s）。決定バッファリング。
- テスト: Easy遅延範囲, Hard遅延範囲, バッファ中再判定抑制

### 11. MagicSystem_ProjectileCore
Projectileデータ構造（casterHash, remainingHits, BulletProfile参照）。ProjectilePool（オブジェクトプール）。生成・消滅ライフサイクル。
- テスト: Projectile生成, casterHash保持, remainingHits減算, lifeTime消滅, Pool返却

### 12. MagicSystem_ProjectileMovement
6種弾道（Straight/Homing/Angle/Rain/Set/Stop）。ProjectileMoveJob（Job System並列移動）。速度・加速度・角度制御。
- テスト: Straight直進, Homing追尾, Angle角度射出, Job並列実行確認, 加速度適用

### 13. MagicSystem_BulletFeatures
BulletFeatureフラグ（Pierce, Explode, Reflect, Gravity, Platform, Shield, AreaEffect, Knockback）。子弾再帰生成（OnActivate/OnTimer/OnHit/OnDestroy）。
- テスト: Pierce貫通, Explode爆発範囲, Reflect反射, 子弾生成タイミング, 複数Feature組合せ

### 14. MagicSystem_CastingFlow
キャスト→発射→弾生成フロー。MP/クールダウンチェック。castTime待機。キャスト中断（被弾時）。MagicDefinition（ScriptableObject）参照。
- テスト: MP不足→キャスト失敗, castTime経過→発射, 被弾→キャスト中断, クールダウン中→拒否, 複数弾生成

### 15. MagicSystem_HitProcessing
命中時のダメージ/回復/バフ適用。casterHashからSoAコンテナ経由で最新ステータス取得。motionValue・attackElement・statusEffect適用。
- テスト: Attack型→ダメージ適用, Recover型→HP回復, Support型→バフ適用, casterHash→最新ステ反映

### 16. CompanionAI_FollowBehavior
距離ベース追従（< followDistance: 待機, < maxLeashDistance: 移動, >= maxLeashDistance: テレポート）。
- テスト: 近距離→待機, 中距離→追従移動, 遠距離→テレポート, followDistance設定変更

### 17. CompanionAI_StanceManager
4スタンス（Aggressive/Defensive/Supportive/Passive）。actionRules確率重み動的調整。スタンス切替イベント。
- テスト: Aggressive→攻撃重み×2.0, Supportive→回復重み×3.0, Passive→戦闘行動ゼロ, スタンス切替イベント発火

### 18. CompanionAI_Controller
CompanionController（AIBrainサブクラス）。AICore_JudgmentLoopを使った自律行動。FollowBehavior+StanceManager統合。
- テスト: 初期化→追従開始, 敵検知→戦闘行動, スタンス反映確認, 判定ループ実行, HP監視→ターゲット切替

### 19. EnemySystem_Controller
EnemyController（敵固有AI補助ロジック）。AICore_ModeControllerベースの敵行動。パトロール行動。
- テスト: ノーマルモード行動選択, 距離→攻撃/移動切替, HP低下→逃走, パトロール経路, ターゲットルール評価

### 20. EnemySystem_SpawnManagement
EnemySpawner（スポーンポイント管理、リスポーン、活性化範囲）。SaveSystem.OnRest→RefreshEnemies。
- テスト: スポーン生成, 活性化範囲外→非アクティブ, リスポーン条件, OnRest→全リフレッシュ

### 21. EnemySystem_DropTable
DropTable（ScriptableObject、確率的ルートテーブル）。EXP/通貨/アイテムのドロップ定義。
- テスト: 確率判定, 保証ドロップ, 空テーブル→ドロップなし, 複数アイテム同時ドロップ

### 22. EnemySystem_LootAndReward
LootDropper（ワールドにルートインスタンス化）。撃破時のEXP/通貨/アイテム分配。OnEnemyDefeatedイベント。
- テスト: EXP分配, 通貨加算, アイテムインベントリ追加, OnEnemyDefeated発火

### 23. EnemySystem_Pool
EnemyPool（オブジェクトプーリング）。取得・返却・プリウォーム。
- テスト: プール取得, プール返却→再利用, プリウォーム数確認

### 24. CompanionAI_CoopInterruption
InterruptForCoop()で現在ActionSlot+ターゲット保存。ResumeFromCoop()で復帰。ActionInterruptHandler利用。
- テスト: 中断→状態保存, 復帰→前アクション再開, 中断中フラグ, 連続中断ガード

### 25. CoopAction_CoreAndCombo
CoopActionManager + CoopActionBase（抽象）。発動フロー（死亡チェック→怯みチェック→コンボ判定→MP/CDチェック→中断→実行）。コンボステップ進行（comboSteps[]、入力ウィンドウ）。
- テスト: プレイヤー死亡→無視, 仲間怯み→拒否, コンボ継続→MP無消費, 入力ウィンドウ期限切れ→終了, maxComboCount到達→終了

### 26. CoopAction_CooldownTracker
非対称クールダウン: CD完了→MP無料+CDリスタート、CD中→MP消費+CDリセットなし。CoopCooldownTracker。
- テスト: CD完了→MP無料, CD中→MP消費, CD中発動→タイマー不変, CD完了→タイマーリスタート, MP不足→拒否

### 27. GateSystem_ConditionCheck
3種ゲート条件評価: ClearGate（GlobalFlag）、AbilityGate（AbilityFlag）、KeyGate（InventorySystem）。GateConditionChecker。
- テスト: ClearGate→フラグ確認, AbilityGate→能力確認, KeyGate→アイテム確認, 条件未達→closed, isPermanent判定

### 28. GateSystem_OpenClose
GateController（コライダー無効化、ビジュアル切替）。開閉メカニクス。GateDefinition（ScriptableObject）参照。
- テスト: 条件達成→コライダー無効, ビジュアル切替, 閉鎖時ヒント表示, 永続ゲート→セーブ

### 29. CoopAction_WarpAction
WarpCoopAction（テレポート: 背後/横/味方付近）。ターゲット選択→瞬間移動。
- テスト: 背後テレポート位置計算, 横テレポート位置計算, 味方付近移動, ターゲットなし→中止

### 30. CoopAction_ShieldAction
ShieldCoopAction（防護シールド展開）。Projectile反射、足場化。
- テスト: シールド生成, Projectile反射, 足場判定, シールド持続時間

### 31. CooldownReward_Feedback
CD完了時アイコン発光+チャイムSE。MP無料発動時「FREE」テキスト+金フラッシュ+特殊SE。バランス設定ScriptableObject。
- テスト: CD完了→発光イベント, MP無料→FREEテキスト表示, SE再生確認

### 32. GateSystem_Registry
GateRegistry（全ゲート状態管理）。永続ゲート→SaveSystem連携。ゲートID→状態マッピング。
- テスト: ゲート登録, 状態取得, 永続化→セーブ/ロード

### 33. GateSystem_HintAndMap
ゲートヒント表示（「壁蹴りが必要」等）。ミニマップ上のゲートアイコン連携。
- テスト: ヒントテキスト表示, マップアイコン更新, ゲート開放→アイコン変化

### 34. AIRuleBuilder_ActionRegistry
ActionTypeRegistry（使用可能アクション管理）。アクションアンロックシステム（探索報酬/ボス報酬）。初期アクションセット。
- テスト: 初期アクション確認, アンロック追加, 未アンロック→使用不可, アンロック永続化

### 35. AIRuleBuilder_ModeEditor
RuleEditorLogic（モード作成/編集/並替）。ActionSlotBuilder。最大4モード制限。
- テスト: モード作成, ルール追加/削除, アクション設定, defaultActionIndex設定, 4モード上限

### 36. AIRuleBuilder_TransitionAndShortcut
ModeTransitionEditor（条件ベース自動切替）。ShortcutBindingEditor（方向+連携ボタン→モード切替）。手動オーバーライド（15秒タイムアウト）。
- テスト: 遷移条件設定, ショートカットバインド, 手動オーバーライド→タイムアウト復帰, 優先順位（手動>条件>現在）

### 37. AIRuleBuilder_PresetManager
PresetManager（システムプリセット + カスタムプリセット最大20スロット）。保存/読込/削除。SaveSystem連携。
- テスト: プリセット保存, プリセット読込→適用, 20スロット上限, 削除確認

### 38. AIRuleBuilder_Validator
RuleValidator（矛盾検出）。条件重複チェック、到達不能ルール検出、必須アクション欠落警告。
- テスト: 矛盾条件検出, 到達不能ルール警告, defaultAction未設定警告

## asmdef配置マッピング

| asmdef | 対象機能 |
|--------|---------|
| Game.Core (既存拡張) | Common_Section2Types |
| Game.AI (新規) | AICore全機能, CompanionAI, EnemySystem, CoopAction, CooldownReward, AIRuleBuilder |
| Game.Combat (既存拡張) | MagicSystem (Projectile/, Magic/) |
| Game.World (既存拡張) | GateSystem (Gate/) |
| Game.Tests.EditMode (既存拡張) | 全EditModeテスト |
| Game.Tests.PlayMode (既存拡張) | 全PlayModeテスト |

## 動作確認手順
全機能完了後、以下を確認:

1. **AI基盤テスト**: ConditionEvaluator + TargetSelector + ActionExecutor が正しく判定
2. **仲間追従テスト**: 距離に応じて待機/追従/テレポートが切り替わる
3. **スタンス切替テスト**: 4スタンスでactionRulesの重み変化を確認
4. **敵AI戦闘テスト**: 敵がtargetRules→actionRules→defaultで行動選択
5. **敵スポーン&ドロップテスト**: スポーン→撃破→EXP/通貨/アイテム獲得
6. **魔法発射テスト**: キャスト→発射→弾移動→命中→ダメージ/回復/バフ適用
7. **連携ボタンテスト**: 押下→仲間中断→スキル実行→コンボ→再開
8. **クールダウン報酬テスト**: CD完了→MP無料発動、CD中→MP消費
9. **ゲートテスト**: 3種ゲートの開閉条件確認
10. **AIルールビルダーテスト**: カスタムモード作成→保存→適用→仲間が新ルールで行動

## 統計
- 総機能数: 38
- 総テスト数目安: ~163
- カテゴリ: system 36, content 2
- 推定実装順: Layer 0 (3) → Layer 1 (4) → Layer 2 (5) → Layer 3 (8) → Layer 4 (8) → Layer 5 (7) → Layer 6 (3)
