# Sprint: Section 1 — MVP: 戦闘と探索の基盤

## 完了条件
1つのエリアを探索し、プレイヤー単体で戦闘できる。装備変更でアニメーション・性能が変化する。ジャストガードのフィードバック（エフェクト・SE）が機能する。レベルアップ・能力値振り分け・ショップ・セーブポイントが機能する。

## 既存機能の活用
| 既存機能 | 対応方法 | 備考 |
|----------|---------|------|
| （なし） | — | feature-db空。全機能新規作成 |

## 実装順序 — システム系

全30機能。Section 1はすべてシステム系（コンテンツ系は敵種・ステージ等でSection 2以降）。

### Layer 0（依存なし）

| # | 機能名 | システム | テスト数目安 | 依存 | 状態 |
|---|--------|---------|------------|------|------|
| 1 | Common_SharedTypes | Common | 4 | なし | pending |
| 2 | DataContainer_SoACore | DataContainer | 4 | Common_SharedTypes | pending |
| 3 | InputSystem_Core | InputSystem | 5 | Common_SharedTypes | pending |
| 4 | MapSystem_Camera | MapSystem | 3 | なし | pending |
| 5 | MapSystem_MinimapAndWorld | MapSystem | 4 | MapSystem_Camera | pending |

### Layer 1（← Layer 0）

| # | 機能名 | システム | テスト数目安 | 依存 | 状態 |
|---|--------|---------|------------|------|------|
| 6 | GameManager_Core | GameManager | 4 | DataContainer_SoACore | pending |
| 7 | PlayerMovement_GroundMovement | PlayerMovement | 4 | InputSystem_Core | pending |
| 8 | PlayerMovement_AdvancedMovement | PlayerMovement | 4 | PlayerMovement_GroundMovement | pending |

### Layer 2（← Layer 0-1）

| # | 機能名 | システム | テスト数目安 | 依存 | 状態 |
|---|--------|---------|------------|------|------|
| 9 | EquipmentSystem_EquipUnequip | EquipmentSystem | 4 | GameManager_Core | pending |
| 10 | EquipmentSystem_StatCalculation | EquipmentSystem | 3 | EquipmentSystem_EquipUnequip | pending |
| 11 | EquipmentSystem_GripCombo | EquipmentSystem | 3 | EquipmentSystem_EquipUnequip | pending |
| 12 | EquipmentSystem_Animation | EquipmentSystem | 2 | EquipmentSystem_EquipUnequip | pending |
| 13 | CurrencySystem_Core | CurrencySystem | 4 | GameManager_Core | pending |
| 14 | InventorySystem_ItemManagement | InventorySystem | 5 | GameManager_Core | pending |
| 15 | InventorySystem_UsageAndBelt | InventorySystem | 4 | InventorySystem_ItemManagement | pending |
| 16 | LevelUpSystem_Core | LevelUpSystem | 4 | GameManager_Core | pending |
| 17 | LevelStreaming_Core | LevelStreaming | 4 | MapSystem_Camera | pending |

### Layer 3（← Layer 0-2）

| # | 機能名 | システム | テスト数目安 | 依存 | 状態 |
|---|--------|---------|------------|------|------|
| 18 | WeaponSystem_ComboCore | WeaponSystem | 4 | InputSystem_Core, EquipmentSystem_EquipUnequip | pending |
| 19 | WeaponSystem_HitboxAndMovement | WeaponSystem | 3 | WeaponSystem_ComboCore | pending |
| 20 | WeaponSystem_SkillChargeAerial | WeaponSystem | 4 | WeaponSystem_ComboCore | pending |
| 21 | DamageSystem_CoreCalculation | DamageSystem | 4 | EquipmentSystem_StatCalculation | pending |
| 22 | DamageSystem_HpArmorKnockback | DamageSystem | 4 | DamageSystem_CoreCalculation | pending |
| 23 | DamageSystem_StatusEffects | DamageSystem | 4 | DamageSystem_CoreCalculation | pending |

### Layer 4（← Layer 0-3）

| # | 機能名 | システム | テスト数目安 | 依存 | 状態 |
|---|--------|---------|------------|------|------|
| 24 | ParryGuard_GuardJudgment | ParryGuardSystem | 5 | DamageSystem_CoreCalculation, EquipmentSystem_EquipUnequip | pending |
| 25 | ParryGuard_Feedback | ParryGuardSystem | 4 | ParryGuard_GuardJudgment | pending |
| 26 | SaveSystem_Core | SaveSystem | 5 | MapSystem_MinimapAndWorld | pending |
| 27 | ShopSystem_Core | ShopSystem | 4 | CurrencySystem_Core, InventorySystem_ItemManagement | pending |
| 28 | UISystem_HUD | UISystem_Basic | 3 | GameManager_Core, CurrencySystem_Core | pending |
| 29 | UISystem_Menus | UISystem_Basic | 4 | UISystem_HUD, InventorySystem_ItemManagement, EquipmentSystem_EquipUnequip | pending |
| 30 | UISystem_BattleFeedback | UISystem_Basic | 2 | DamageSystem_CoreCalculation | pending |

## 機能詳細

### 1. Common_SharedTypes
共通インターフェース（IAbility, IDamageable, IInteractable, ISaveable, IEquippable）、全Enum、共通構造体（ElementalStatus, DamageData, AttackMotionData等）の定義。
- テスト: ElementalStatus演算, DamageData生成, Flagsビット演算, GuardStats初期値

### 2. DataContainer_SoACore
SourceGeneratorによるSoAコンテナ自動生成、ハッシュ登録・削除（swap-back）、ref returnアクセス。
- テスト: Add/Remove, ref return取得, TryGetValue, Dispose

### 3. InputSystem_Core
Unity Input Systemのアクションマップ定義、移動/戦闘入力変換、先行入力バッファ。
- テスト: 移動方向正規化, AttackInputType変換, guardHeld検出, バッファ保持/期限切れ, アクションマップ切替

### 4. MapSystem_Camera
カメラ追従（SmoothDamp + デッドゾーン + 境界クランプ）。
- テスト: 追従位置計算, 境界クランプ, デッドゾーン内移動無視

### 5. MapSystem_MinimapAndWorld
ミニマップ描画（RenderTexture）、全体マップUI、訪問済みエリア記録、エリア境界定義。
- テスト: 訪問記録追加, 探索率計算, エリア境界判定, マップデータ永続化

### 6. GameManager_Core
シングルトン初期化、SoACharaDataDic保持、GameEventsハブ、各Manager登録と初期化順制御。
- テスト: Instance取得, Data非null, Events発火, Manager初期化順序

### 7. PlayerMovement_GroundMovement
地上移動（MoveStatus依存速度）、可変高度ジャンプ、Raycast接地判定、ダッシュ（スタミナ消費）。
- テスト: 移動速度適用, ジャンプ高度, 接地判定, ダッシュスタミナ消費

### 8. PlayerMovement_AdvancedMovement
壁蹴り（WallKick flag必要）、壁張り付き（WallCling flag必要）、落下攻撃遷移、重量ペナルティ反映。
- テスト: WallKickフラグ未所持で不発, WallCling滑り減速, 落下攻撃遷移条件, weightRatio→速度補正

### 9. EquipmentSystem_EquipUnequip
武器/盾/コアの装着・脱着、素手デフォルト適用、AbilityFlag合算（武器+盾+コア+コンボ効果）。
- テスト: 装備セット確認, 脱着→素手復帰, AbilityFlag合算, 必要能力値未満ペナルティ

### 10. EquipmentSystem_StatCalculation
攻撃力スケーリング（AnimationCurve）、重量比率計算、性能倍率算出（回避速度等）。
- テスト: スケーリング計算精度, weightRatio算出, 性能倍率カーブ適用

### 11. EquipmentSystem_GripCombo
片手⇔両手持ち切替、スキル優先判定（GripMode/weaponArts→SkillSource）、コンビネーション効果。
- テスト: GripMode切替, SkillSource判定3パターン, コンビネーション効果適用

### 12. EquipmentSystem_Animation
武器変更時のAnimatorController差し替え、盾変更時のスプライト切替。
- テスト: AnimatorController差替確認, 盾スプライト変更確認

### 13. CurrencySystem_Core
通貨獲得・消費・残高管理、デスペナルティ（20%ロスト）。
- テスト: Add/TrySpend, 残高不足時false, デスペナルティ20%, ISaveable永続化

### 14. InventorySystem_ItemManagement
アイテム追加・削除、カテゴリ別取得、スタック上限、売却可否判定（装備品/KeyItem/Magic/Flavor→不可）。
- テスト: Add/Remove, カテゴリフィルタ, スタック上限, CanSell判定, CanStack判定

### 15. InventorySystem_UsageAndBelt
消耗品使用（効果適用+在庫減）、魔法使用（MP消費+効果）、BeltShortcut（可変スロット、ループ回転）。
- テスト: 消耗品使用→在庫減, 魔法使用→MP消費, ベルト回転（Next/Prev/ループ）, ベルトセット/クリア

### 16. LevelUpSystem_Core
経験値蓄積、レベルアップ判定（AnimationCurveテーブル）、能力値振り分け、ステータス再計算。
- テスト: EXP加算, レベルアップ判定, ポイント振り分け, ステータス再計算連鎖

### 17. LevelStreaming_Core
Additive Scene Loading/Unloading、AreaTrigger検知、オーバーラップゾーン管理。
- テスト: ロード要求→シーン追加, アンロード条件, AreaTrigger進入検知, SceneRegistry参照

### 18. WeaponSystem_ComboCore
弱攻撃チェーン、強攻撃チェーン、弱→強コンボ派生、isAutoChain自動遷移。
- テスト: 弱チェーン順序, 強チェーン順序, 派生分岐, autoChain自動進行

### 19. WeaponSystem_HitboxAndMovement
ヒットボックス生成・当たり判定、maxHitCount制限、攻撃時移動（PassThrough/StopOnHit/Carry）。
- テスト: ヒットボックス有効化, ヒット数制限, 3種接触タイプ移動

### 20. WeaponSystem_SkillChargeAerial
チャージ攻撃（溜め時間→モーション切替）、スキル発動（MP消費）、空中攻撃、落下攻撃、飛翔体発射。
- テスト: チャージ段階判定, スキルMP消費, 空中攻撃チェーン, 飛翔体生成

### 21. DamageSystem_CoreCalculation
属性別ダメージ計算式 `(atk²×mv)/(atk+def)`、ガードダメージ（カット率適用）、スタン中1.2倍。
- テスト: 基本計算式, 属性0時スキップ, ガードカット率適用, スタンボーナス

### 22. DamageSystem_HpArmorKnockback
HP適用（SoA書き戻し）、アーマー削り・回復・怯み、ノックバック、防御倍率、無敵時間。
- テスト: HP減少書き戻し, アーマー怯み閾値, ノックバック方向, 無敵時間中ダメージ無効

### 23. DamageSystem_StatusEffects
状態異常蓄積（ヒットごと加算）、閾値チェック→発症、DoT/行動阻害/デバフ持続、耐性一時上昇。
- テスト: 蓄積加算, 閾値超過→発症, DoTダメージtick, 発症後耐性上昇

### 24. ParryGuard_GuardJudgment
ガード状態管理、ジャストガード判定（タイミングウィンドウ）、ガードブレイク（スタミナ不足）、背面攻撃判定、JG抵抗計算、JG不可攻撃スキップ。
- テスト: JGウィンドウ内→JustGuard, JGウィンドウ外→Guarded, スタミナ不足→GuardBreak, 背面→NoGuard, JustGuardImmune→JGスキップ

### 25. ParryGuard_Feedback
JG成功時エフェクト（ヒットストップ+パーティクル+SE+カメラシェイク）、通常ガードSE、ガードブレイク演出、JG不可攻撃の赤警告エフェクト。
- テスト: JGフィードバック発火, 通常ガードSE発火, ブレイク演出発火, JG不可警告タイミング

### 26. SaveSystem_Core
セーブデータ書出（JSON）、読込・復元、セーブスロット管理、HP/MP/スタミナ回復、ファストトラベル。
- テスト: シリアライズ→ファイル保存, デシリアライズ→状態復元, 複数スロット管理, 回復処理, ファストトラベル遷移

### 27. ShopSystem_Core
購入処理（通貨消費+アイテム追加）、売却処理（買値30%）、商品ラインナップ（ScriptableObject）、有限在庫。
- テスト: 購入成功, 残高不足拒否, 売却額計算, 在庫減少

### 28. UISystem_HUD
HP/MP/スタミナバー（イベント駆動更新）、通貨残高表示、連携クールダウンインジケータ。
- テスト: HPバー更新, スタミナバーSmoothDamp, 通貨表示更新

### 29. UISystem_Menus
ポーズメニュー、インベントリ画面、装備変更画面、レベルアップ振り分け画面。
- テスト: ポーズ→Time.timeScale=0, インベントリ表示, 装備変更UI, 振り分けUI

### 30. UISystem_BattleFeedback
ヒット時のフローティングダメージ数字表示。
- テスト: ダメージ数字生成, 数字フェードアウト

## asmdef配置マッピング

| asmdef | 対象機能 |
|--------|---------|
| Game.Core | Common_SharedTypes, DataContainer, GameManager, InputSystem |
| Game.Character | PlayerMovement |
| Game.Combat | EquipmentSystem, WeaponSystem, DamageSystem, ParryGuardSystem |
| Game.World | MapSystem, LevelStreaming, SaveSystem |
| Game.Economy | CurrencySystem, InventorySystem, LevelUpSystem, ShopSystem |
| Game.UI | UISystem_HUD, UISystem_Menus, UISystem_BattleFeedback |
| Game.Tests.EditMode | 全EditModeテスト |
| Game.Tests.PlayMode | 全PlayModeテスト |

## 動作確認手順
全機能完了後、以下を確認:

1. **起動テスト**: GameScene起動→GameManager初期化→HUD表示
2. **移動テスト**: WASD移動、ジャンプ、ダッシュが動作
3. **装備テスト**: 武器/盾/コア変更→アニメーション変化、ステータス再計算
4. **戦闘テスト**: 弱/強コンボ、スキル発動、ダメージ計算が正しい
5. **ガードテスト**: ジャストガード→エフェクト+SE、ガードブレイク→スタン
6. **経済テスト**: 通貨獲得→ショップ購入→インベントリ追加
7. **レベルテスト**: EXP獲得→レベルアップ→セーブポイントで振り分け
8. **セーブテスト**: セーブ→ロード→状態復元、ファストトラベル
9. **エリアテスト**: AreaTrigger通過→隣接シーンAdditive Load→シームレス遷移
10. **インベントリテスト**: アイテム使用（ベルトショートカット）、魔法使用、売却可否

## 統計
- 総機能数: 30
- 総テスト数目安: ~115
- カテゴリ: 全てsystem
- 推定実装順: Layer 0 (5) → Layer 1 (3) → Layer 2 (9) → Layer 3 (6) → Layer 4 (7)
