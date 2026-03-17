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
- ダメージ式: (atk² × motionValue) / (atk + def) × 倍率
- isAutoChain: 入力なしで自動的に次モーションへ遷移するフラグ
- maxHitCount: モーションごとの最大ヒット数
- justGuardResistance: ジャストガード時のアーマー削り軽減 (0-100)
- AttackFeature.JustGuardImmune: ジャストガード不可攻撃

### 能力値スケーリング
- 武器は STR/DEX/INT の AnimationCurve を持つ
- STR → 物理・光、DEX → 物理・闇、INT → 火・雷
- レベルアップで能力値ポイントを振り分け（セーブポイントで）

### Assembly構成
- Game.Core ← Game.Character ← Game.Combat / Game.AI / Game.World / Game.Economy ← Game.UI
- 循環参照禁止。上位は下位を参照しない
- システム間通信はGameManager.Eventsの C# event を使用

### レベルストリーミング
- エリア境界にTrigger配置 → Additive Scene Loading
- GameScene（永続） + エリアシーン（Additive）構成
- プレイヤー・仲間・UIは永続シーンに属する
