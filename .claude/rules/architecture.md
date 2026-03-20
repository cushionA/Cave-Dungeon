---
description: Game architecture rules based on SisterGame design docs (Architect/). Updated by /design-systems.
paths:
  - "Assets/MyAsset/**/*.cs"
  - "Assets/Tests/**/*.cs"
---

# アーキテクチャルール

## コア設計原則（Architect/00_アーキテクチャ概要.md 準拠）

### SoA（Structure of Arrays）+ SourceGenerator
- キャラクターデータをフィールドごとの配列で管理し、キャッシュ効率を最大化
- SourceGeneratorにより情報クラスに属性を付けるだけでSoAコンテナ・アクセサが自動生成
- 手書きボイラープレートを排除する

### GameManager シングルトン（中央ハブ）
- GameManagerが全マネージャーとデータコンテナへの唯一の参照点
- すべてのゲームシステムは `GameManager` 経由でアクセスする
- 散在するシングルトンを排除し、依存関係を一元管理する

### ハッシュベースデータアクセス（GetComponent排除）
- GameObjectのhashCodeをキーとして `GameManager.Data` 経由でO(1)データアクセス
- GetComponentによる毎フレーム検索コストを完全に排除
- `GetComponent` / `FindObjectOfType` はAwake/Start以外で使用禁止

### コンポーネント拡張（Abilityシステム）
- ベースキャラクターは最小限の責務のみ持つ
- ジャンプ・ダッシュ等はAbilityコンポーネントとして追加・組み合わせ可能
- 新しい行動の追加が既存コードに影響しない（IAbility インターフェース）

### データ駆動（ScriptableObject）
- ゲームバランスに関わる値はすべてScriptableObject上の情報クラスで定義
- ハードコーディング禁止。Inspectorから調整可能にする

## 入力
- Input SystemパッケージのPlayerInputコンポーネントを使用
- 入力の受け取りは専用のInputHandlerクラスに集約
- ゲームロジックは入力ソースに依存しない（テスト時にモック可能）

## 状態管理
- ゲーム全体の状態（メニュー、プレイ中、ポーズ等）はステートマシンで管理
- 各状態はクラスとして分離（State パターン）

## レイヤー構成
```
[Input Layer]      — 入力受付、イベント発行
    ↓ events
[Game Logic Layer] — メカニクス、ルール、状態管理（GameManager中央ハブ）
    ↓ data (SoAコンテナ経由)
[Presentation Layer] — 表示、アニメーション、エフェクト、サウンド
```
- 上位レイヤーは下位を知らない
- 下位レイヤーはイベント購読で上位の変化を受け取る

## コンポーネント粒度
- 1コンポーネント = 1つの明確な責務
- 「このコンポーネントは何をするか」を1文で説明できること
- 説明に「〜と〜」が含まれる場合は分割を検討

## Section 1 固有ルール（/design-systems section-1 で追記）

### 装備システム
- 装備スロット: 武器(右手) / 盾(左手) / コア(1スロット)
- GripMode: OneHanded（盾併用） / TwoHanded（盾無効化）
- スキル優先: TwoHanded or shield.weaponArts → 武器スキル、else → 盾スキル
- 片手持ち盾スキルの攻撃は盾の攻撃力を参照する
- 装備変更時に全ステータス再計算（攻撃力、防御力、AbilityFlag、重量比率）

### AbilityFlag
- 拡張移動（壁蹴り等）はAbilityFlagで管理
- フラグは装備（武器+盾+コア+コンビネーション効果）から合算
- Section1ではフラグシステムのみ実装、壁蹴り装備はSection2

### 重量システム
- weightRatio = totalEquipWeight / maxWeightCapacity
- AnimationCurveで性能変動（回避速度、スタミナ回復、攻撃速度、回避距離）
- 過負荷（1.0超過）も可能だが大ペナルティ

### 戦闘
- ダメージ式: 各属性チャネルごとに (atk² × motionValue) / (atk + def) を計算し合算
- isAutoChain: 入力なしで自動的に次モーションへ遷移するフラグ
- maxHitCount: モーションごとの最大ヒット数
- justGuardResistance: ジャストガード時のアーマー削り軽減 (0-100)
- AttackFeature.JustGuardImmune: ジャストガード不可攻撃

### 属性システム（7属性統一）
- Element = Slash(斬撃)/Strike(打撃)/Pierce(刺突)/Fire(炎)/Thunder(雷)/Light(聖)/Dark(闇)
- [Flags] byte で複合属性を表現可能（例: 炎+斬撃の剣）
- WeaponPhysicalType は廃止。物理タイプ（斬撃/打撃/刺突）もElementに統合
- ElementalStatus: 7属性別のint値（slash, strike, pierce, fire, thunder, light, dark）
- CombatStats: ElementalStatus attack / ElementalStatus defense（属性別攻防）
- ダメージ計算は7属性チャネル別に (atk² × motionValue) / (atk + def) を算出し合算
- GuardStats: 属性別カット率（slashCut, strikeCut, pierceCut, fireCut, thunderCut, lightCut, darkCut）

### 能力値スケーリング
- 武器は STR/DEX/INT の AnimationCurve を持つ
- STR → 斬撃・聖、DEX → 刺突・闇、INT → 炎・雷
- レベルアップで能力値ポイントを振り分け（セーブポイントで）

### Assembly構成
- Game.Core ← Game.Character ← Game.Combat / Game.AI / Game.World / Game.Economy ← Game.UI
- 循環参照禁止。上位は下位を参照しない
- システム間通信はGameManager.Eventsの C# event を使用

### レベルストリーミング
- エリア境界にTrigger配置 → Additive Scene Loading
- GameScene（永続） + エリアシーン（Additive）構成
- プレイヤー・仲間・UIは永続シーンに属する

## Section 2 固有ルール（/design-systems section-2 で追記）

### 統一行動システム（ActionSlot）
- **全行動をActionSlotで統一**。旧ActionData（ActState+param）は廃止
- 敵AIも仲間AIも同じActionSlot/AIMode/ActionExecutorで動作
- 実行パターン5分類（ActionExecType）:
  - **Attack**: AttackMotionData参照、ヒットボックス・motionValue・コンボ管理
  - **Cast**: 詠唱→発動→ProjectileSystem（魔法、チャージ攻撃、武器スキル飛翔体）
  - **Instant**: アニメ1回再生（ワープ、回避、アイテム使用、環境物利用）
  - **Sustained**: 開始→Tick→終了条件（移動、ガード、追従、挟撃等）。reactionTriggerでカウンター系
  - **Broadcast**: 他キャラAI状態を操作（ターゲット指示、集合、挑発等）
- ActionBase基底クラス: 共通フィールド（mpCost, staminaCost, cooldown）+ CanExecute/Execute/Interrupt/Tick
- ActionExecutorはDictionary<ActionExecType, ActionBase>（switch文排除）

### AI判定システム（Architect/07_AI判定システム再設計.md 準拠）
- 3層判定: ターゲット切替 → 行動切替 → デフォルト行動（棒立ち防止）
- AIConditionType(12種) + CompareOp(6種) で全条件を統一表現
- AIRule.conditions は AND 結合、AIRule[] は優先度順（先勝ち = OR結合）
- ヘイトシステム廃止 → DamageScore（累積ダメージ×倍率+時間減衰）で代替
- TargetFilter のビット演算でCharacterFlagsを高速フィルタリング

### AIBrain
- AIBrainはMonoBehaviour。3層判定のEvaluate()を毎判定間隔で実行
- ConditionEvaluator, TargetSelector, ActionExecutor はピュアロジック（MonoBehaviour非依存）
- CompanionControllerはAIBrainを継承（モード手動切替+自動切替対応）

### 仲間AIカスタム（AIRuleBuilder）
- CompanionAIConfig: 最大4モード + モード自動切替条件 + ショートカット手動切替
- ActionSlotの行動タイプは探索報酬で段階的に解放（メトロイドヴァニアの新能力=新戦術）
- システムプリセット入手 = 完成済みパターン + 新ActionType解放（二重報酬）
- 手動切替が最優先、タイムアウトで自動切替に復帰

### 仲間MPシステム
- 仲間はHPを使用しない。被ダメージはバリア（盾判定 = ダメージ計算式準拠）でMP消費
- MP 0 → 消滅（ワープ退場）。死亡ではなく一時退場
- currentMP が maxMP の50%に回復したら復帰
- 消滅中: 連携使用不可、MP回復倍率 1.3x（CompanionMpSettings.vanishRecoveryMultiplier で設定可変）
- **二重MPプール**: currentMP は reserveMP から自然補充。reserveMP はアイテム/チェックポイントのみ回復
- **MP回復行動**: SustainedAction.MpRecover。停止してMP加速回復。怯みで中断、再開可

### 連携ボタンスキル（CoopAction）
- 連携 = 仲間への指示スキル。CoopActionBase継承で多様な連携を追加
- コンボ対応: 連打で最大N回連続発動（MP消費は初回のみ）
- 各コンボ段ごとにAITargetSelectでターゲット条件を個別設定
- 行動割り込み: 怯み中でなければ仲間の現在行動を中断→連携終了後に再開
- クールタイム消化済み→MP無料、未消化→MP消費（タイマーは変えない）

### 飛翔体システム（ProjectileSystem）
- 全飛翔体の共通基盤（魔法弾、スキル衝撃波、チャージ弾、敵遠距離攻撃）
- 弾丸はcasterHashのみ記録、命中時にコンテナから最新ステータス取得
- キャスター死亡→弾丸自動消滅

### 敵AI
- 行動パターンはAIInfo（ScriptableObject）のAIMode配列（ActionSlot[]含む）で定義
- DamageScoreTrackerで「最もダメージを与えてくる相手」をターゲットに選択
- スポーンはEnemySpawner（activateRange外は非アクティブ、休息でリスポーン）

### ゲートシステム
- GateType: Clear / Ability / Key / Elemental の4種
- 永続ゲート（ボスクリア等）はグローバルフラグ、一時ゲートはマップローカルフラグ
- ISaveable実装でSaveSystemと連携

## Section 3 固有ルール（/design-systems section-3 で追記）

### ボスシステム
- BossControllerはAIBrainをコンポジションで保持（継承ではない）
- フェーズ遷移: BossPhaseManagerがHP閾値/タイマー/行動回数で判定
- フェーズ遷移時にAIBrainのモード配列を差し替え（AIBrainコード変更なし）
- フェーズ遷移中は無敵時間（既存DamageSystemのinvincibleフラグ）
- アリーナロック: BossArenaManagerが出入口コライダー管理
- 撃破後: ClearGate永続開放 + DropTable報酬

### 召喚システム
- MagicType.Summon で召喚魔法を定義（既存MagicCasterのCast()フローに乗る）
- 召喚枠: 最大2枠（PartyManager.k_MaxSummonSlots）、パーティ最大4人
- 枠満杯時は最古の召喚獣を解除して入れ替え
- 召喚獣はSoAコンテナに通常キャラクターとして登録
- 追従ロジックはFollowBehaviorを再利用（コンポジション）
- SummonType: Combat（戦闘用）, Utility（足場/照明）, Decoy（囮/ヘイト集め）

### 混乱魔術
- 既存蓄積型状態異常モデルに統合（StatusEffectManager.AccumulateEffect）
- 蓄積閾値超過→CharacterFlagsの陣営フラグ反転（Faction.Enemy → Faction.Ally）
- AIの行動パターンは変更なし。TargetFilterの陣営条件だけ反転
- 混乱敵はパーティ枠外。同時最大3体（PartyManager.k_MaxConfusedEnemies）
- ボスは混乱耐性1.0（完全耐性）
- confusionBreakDamage: 味方誤爆で混乱解除

### 属性ゲート（環境パズル）
- GateTypeにElemental追加。ElementalRequirementで7属性対応
- 属性攻撃のヒット検知は専用（IDamageableではなく環境オブジェクトレイヤー）
- multiHitRequired対応（弱点を数回殴る等のギミック）
- 仲間の属性攻撃でも開放可能（連携パズル）

### バックトラック報酬
- AbilityFlag獲得時にBacktrackRewardManagerが全報酬を再評価
- 能力獲得前はマップマーカー非表示（ネタバレ防止）
- AbilityOrbを回収→新能力→更にバックトラック報酬が解放される連鎖
- BacktrackRewardTableはエリアごとのScriptableObject
- ISaveable実装で回収状態を永続化

### パーティ管理
- PartyManager静的クラスで枠管理
- 最大パーティ: 4人（プレイヤー1 + 常駐仲間1 + 召喚最大2）
- 混乱敵はパーティ枠外（別カウント、最大3体）

## Section 4 固有ルール（/design-systems section-4 で追記）

### チャレンジモード
- ChallengeRunner は純ロジック（MonoBehaviour非依存）。状態管理・タイマー・勝敗判定
- ボスラッシュは BossControllerLogic.StartEncounter() を順番に呼ぶ（新戦闘ロジック不要）
- スコア計算は ChallengeScoreCalculator（static class）に集約
- ChallengeManager は ISaveable でアンロック状態を永続化
- チャレンジイベントは GameEvents の R3 Subject パターンに統一

### AIテンプレート
- 既存 CompanionAIConfig（Section 2）をそのまま内包する AITemplateData で管理
- AITemplateManager は PresetManager を内部活用（重複構造を作らない）
- ImportExport は YAGNI で除外。将来のオンライン共有時に別途設計
- テンプレート適用後は GameEvents.FireCustomRulesChanged() で既存AI更新フローに乗せる
- Revert は直前1回分のみ保持（スタックにしない）

### リーダーボード
- LeaderboardManager は ISaveable で記録永続化
- 新記録時は GameEvents.FireNewRecord() でUI通知
- ローカルのみ（オンラインランキングは対象外）
