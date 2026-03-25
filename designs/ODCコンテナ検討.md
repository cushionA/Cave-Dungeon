# ObjectDataContainer 新バージョン コンテナ検討

## 1. 新ランタイムコンテナの活用検討

新バージョン(v1.0.0)で追加された10種のランタイムコンテナについて、本ゲームでの活用余地を検討する。

---

### 1-1. CooldownContainer — 活用度: ★★★★★（最優先）

**概要**: GameObject単位で複数の名前付きクールダウンを管理。O(1)アクセス。

**現状の課題**:
- `CoopCooldownTracker` が単一クールダウンを自前実装
- `ComboManager._inputWindowTimer` がタイマーを手動管理
- `JudgmentLoop` が `_targetJudgeTimer` / `_actionJudgeTimer` を個別管理
- `DamageReceiver._armorRecoveryTimer` も独自タイマー
- スキルクールダウン、回避クールダウン等、今後増える一方

**活用案**:
- アビリティ/スキルのクールダウン一元管理
- AI判定間隔タイマー（TargetJudge, ActionJudge）
- 入力バッファのタイムアウト管理
- アーマー回復ディレイ
- 無敵時間管理（被弾後の無敵フレーム）

---

### 1-2. StateMapContainer\<TState\> — 活用度: ★★★★★（最優先）

**概要**: GameObject単位のFSM。現在/前回の状態 + 経過時間を追跡。

**現状の課題**:
- `ModeController` がAIモード遷移を自前実装（_currentModeIndex, SetModes, SwitchMode）
- `CharacterFlags.ActState` がビットフラグで状態管理（6ビット = 最大64状態）
- 状態遷移の履歴（前回状態）や経過時間が取れない
- `ActionBase._isExecuting` もbooleanで状態管理

**活用案**:
- AIモード管理（Idle/Chase/Attack/Flee等）→ ModeControllerの置き換え
- キャラクター行動状態管理（ActState）の補完 — 経過時間付きで「スタン何秒経過」等が取れる
- アクション実行状態のFSM化（Idle/PreMotion/Active/Recovery）
- ボス戦フェーズ管理（Phase1/Phase2/Phase3 + 経過時間）

---

### 1-3. SpatialHashContainer2D\<T\> — 活用度: ★★★★☆（高）

**概要**: 2D空間ハッシュ。近傍クエリをO(1)〜O(k)で実行。

**現状の課題**:
- `SensorSystem.UpdateDetection()` が全ハッシュを線形スキャンして距離チェック → O(n)
- `TargetSelector.FilterCandidates()` も全候補に対してVector2.Distance → O(n)
- `ConditionEvaluator.NearbyFaction` が全味方をイテレートして距離計算
- `EnemySpawner.EvaluateSpawnPoints()` も距離チェックあり
- キャラクター数が増えると線形スキャンがボトルネックに

**活用案**:
- AI認識システム（SensorSystem）の近傍検索高速化
- ターゲット選択の距離フィルタリング高速化
- 近接味方カウント（NearbyFaction条件）の高速化
- 敵スポーンポイントの有効範囲判定
- 将来的なAoE（範囲攻撃）の対象取得

**注意**: 本ゲームは2Dサイドスクロールなので2D版が適切。XZ平面ではなくXY平面で動作するか確認が必要（現行はXZ平面操作）。

---

### 1-4. TimedDataContainer\<T\> / NotifyTimedDataContainer\<T\> — 活用度: ★★★★☆（高）

**概要**: 時限付きデータ格納。期限切れで自動削除＋コールバック。

**現状の課題**:
- `StatusEffectManager` が手動でタイマー管理 + swap-remove
- `ActionEffect` が `startTime + duration` で有効期間管理
- バフ/デバフの残り時間管理が各所に散在
- 今後、投射物（Projectile）の寿命管理も必要

**活用案**:
- バフ/デバフ管理 — 期限切れで自動解除 + UI通知コールバック
- 投射物の寿命管理
- 一時的な無敵状態（被弾後iフレーム）
- ステージギミックの一時効果（加速パッド、毒沼等）
- AI一時イベント（「助けて」シグナル等、一定時間で消失）

---

### 1-5. GroupContainer\<T\> — 活用度: ★★★★☆（高）

**概要**: グループ単位のオブジェクト管理。O(1)グループ移動、Span一括取得。

**現状の課題**:
- `CharacterFlags.Belong` ビットフラグ(3ビット)で陣営管理（Ally/Enemy/Neutral等）
- `TargetSelector` / `ConditionEvaluator` が全キャラをイテレートしてBelongフィルタ
- 陣営ごとのキャラ一覧取得に毎回フルスキャンが必要
- 味方AI（CompanionController）がプレイヤーとの関係を暗黙的に管理

**活用案**:
- 陣営別キャラクター管理（Ally/Enemy/Neutral/Boss）
- ステージ別アクティブ敵管理（EnemySpawnerとの連携）
- AI対象グループ（攻撃対象グループ / 防御対象グループ）
- 寝返り・混乱魔法で `MoveToGroup()` による陣営変更（O(1)）

---

### 1-6. PriorityPoolContainer\<T\> — 活用度: ★★★☆☆（中）

**概要**: 優先度付きプール。容量超過時に低優先度を自動排出。

**現状の課題**:
- `EnemySpawner` が `_maxActive` で同時敵数を制限
- `ProjectilePool` が固定サイズプール管理
- `DamageScoreTracker` が脅威度スコアを管理（暗黙的な優先度）

**活用案**:
- 敵スポーン管理 — 最大数超過時に画面外の低優先敵を自動despawn
- エフェクトプール — パーティクル等の同時表示数制限
- ダメージポップアップの表示数制限（古いものから消す）
- AI認識リスト — 認識可能な最大ターゲット数を制限

---

### 1-7. RingBufferContainer\<T\> — 活用度: ★★★☆☆（中）

**概要**: 固定サイズ循環バッファ。ヒープアロケーション無し。

**現状の課題**:
- `InputBuffer` が1スロットのみ（将来的にコマンド入力が欲しい）
- `DamageScoreTracker` がダメージ履歴を減衰管理
- 入力履歴がないためリプレイやAI学習データ収集が困難

**活用案**:
- 入力履歴バッファ（直近N入力を記録 → コマンド技判定）
- ダメージ履歴（直近N回の被ダメージ記録 → AI判断材料）
- 位置履歴（直近Nフレームの位置 → 軌跡エフェクト、移動予測）
- デバッグログ（直近Nイベント記録 → AI行動ログ）

---

### 1-8. ComponentCache\<T\> — 活用度: ★★☆☆☆（低〜中）

**概要**: GetComponent結果のキャッシュ。自動クリーンアップ付き。

**現状の課題**:
- `HitBox.OnTriggerEnter2D()` で `other.GetComponent<DamageReceiver>()` を毎回呼出
- 設計上GetComponentは排除済み（SoAハッシュアクセス）だが、物理コールバックでは避けられない

**活用案**:
- 物理コールバック（OnTriggerEnter2D等）でのDamageReceiver/HitBoxキャッシュ
- ただし、本ゲームのアーキテクチャでは大部分をSoAコンテナが代替済み
- 限定的な用途（コライダー接触時のみ）

---

### 1-9. SpatialHashContainer3D\<T\> — 活用度: ★☆☆☆☆（低）

**概要**: 3D空間ハッシュ。

**判定**: 本ゲームは2Dサイドスクロールのため、3D版は不要。2D版で十分。

---

### 活用優先度まとめ

| 優先度 | コンテナ | 主な用途 |
|--------|---------|---------|
| ★★★★★ | CooldownContainer | アビリティCD、AIタイマー、無敵時間 |
| ★★★★★ | StateMapContainer | AIモード、行動状態、ボスフェーズ |
| ★★★★☆ | SpatialHashContainer2D | AI認識、ターゲット選択、範囲攻撃 |
| ★★★★☆ | TimedDataContainer / Notify版 | バフ/デバフ、投射物寿命、一時効果 |
| ★★★★☆ | GroupContainer | 陣営管理、ステージ敵管理 |
| ★★★☆☆ | PriorityPoolContainer | 敵スポーン制限、エフェクト制限 |
| ★★★☆☆ | RingBufferContainer | 入力履歴、ダメージ履歴、位置軌跡 |
| ★★☆☆☆ | ComponentCache | 物理コールバック時のみ |
| ★☆☆☆☆ | SpatialHashContainer3D | 不要（2Dゲーム） |

---

## 2. 追加してほしいデータコンテナ一覧

現在のSoACharaDataDicは `CharacterVitals`, `CombatStats`, `CharacterFlags`, `MoveParams`, `EquipmentStatus`, `CharacterStatusEffects` の6構造体を管理。以下は追加候補。

### 2-1. キャラクター関連（SoAコンテナ管理対象）

#### A. CharacterColdLog（AI判定ログ）
```csharp
public struct CharacterColdLog  // 設計書に記載あり、未実装
{
    public int characterId;        // キャラクター種別ID
    public int objectHash;         // GameObjectハッシュ
    public byte currentModeIndex;  // 現在のAIモードインデックス
    public float lastTargetJudgeTime;  // 最後のターゲット判定時刻
    public float lastActionJudgeTime;  // 最後のアクション判定時刻
}
```
**理由**: AI判定間隔の管理。現在JudgmentLoopが個別にタイマーを持つが、SoAに入れればAIManagerから一括参照可能。

#### B. DamageScoreEntry（ダメージスコア / ヘイト管理）
```csharp
public struct DamageScoreEntry  // 設計書に記載あり、未実装
{
    public int attackerHash;      // 攻撃者のハッシュ
    public float score;           // 累積ダメージスコア（減衰付き）
    public float lastUpdateTime;  // 最終更新時刻
}
```
**理由**: ターゲット選択のDamageScoreソートキーに必要。現在DamageScoreTrackerがDictionaryで自前管理。

#### C. ProjectileData（投射物データ）
```csharp
public struct ProjectileData
{
    public int ownerHash;          // 発射者ハッシュ
    public byte moveType;          // BulletMoveType (Straight/Homing/Angle等)
    public float speed;            // 移動速度
    public float lifetime;         // 残り寿命
    public float homingStrength;   // ホーミング強度
    public byte pierceRemaining;   // 残り貫通回数
    public float aoeRadius;        // 着弾時AoE半径
    public int targetHash;         // ホーミング対象
    public ushort attackInfoId;    // AttackInfo参照ID
}
```
**理由**: 投射物システムは設計済み・未実装。ProjectilePoolを超えた高速な投射物管理が必要。

#### D. AnimationState（アニメーション状態）
```csharp
public struct AnimationState
{
    public int currentClipHash;    // 現在再生中のアニメーションハッシュ
    public float normalizedTime;   // 正規化時間 (0.0〜1.0)
    public float speed;            // 再生速度
    public byte layerIndex;        // レイヤーインデックス
    public bool isInTransition;    // 遷移中フラグ
}
```
**理由**: アニメーション状態をSoAで管理すれば、AI判定やアクション入力可否判定でAnimatorへのアクセスを減らせる。

#### E. KnockbackData（ノックバック/物理反応データ）
```csharp
public struct KnockbackData
{
    public Vector2 velocity;       // 現在のノックバック速度
    public float duration;         // 残りノックバック時間
    public float gravityScale;     // ノックバック中の重力スケール
    public byte hitStopFrames;     // ヒットストップ残りフレーム
    public bool isAirborne;        // ノックバックで空中かどうか
}
```
**理由**: 被弾リアクションの物理データ。DamageReceiverとBaseCharacterの間で共有が必要。

#### F. InputState（入力状態）
```csharp
public struct InputState
{
    public Vector2 moveInput;      // 移動入力ベクトル
    public ushort buttonDown;      // 今フレーム押されたボタン（ビットフラグ）
    public ushort buttonHeld;      // 押し続けているボタン
    public ushort buttonUp;        // 今フレーム離されたボタン
    public float chargeTime;       // チャージ攻撃の溜め時間
}
```
**理由**: AIとプレイヤーの入力を統一的にSoAで管理。AIBrainの出力もInputStateとして格納すれば、BaseCharacterは入力ソースを意識しない。

#### G. WeaponState（武器状態）
```csharp
public struct WeaponState
{
    public ushort rightWeaponId;     // 右手武器ID
    public ushort leftWeaponId;      // 左手武器ID
    public byte activeSlot;          // アクティブスロット (0=右, 1=左)
    public byte comboStep;           // 現在のコンボステップ
    public float comboWindowTimer;   // コンボ入力受付残り時間
    public byte chargeLevel;         // チャージレベル (0=なし)
    public float chargeElapsed;      // チャージ経過時間
}
```
**理由**: 武器/コンボ/チャージの状態をSoAで一元管理。ComboManagerとWeaponHolderの状態をまとめる。

#### H. ActionExecutionData（アクション実行データ）
```csharp
public struct ActionExecutionData
{
    public ushort currentActionId;   // 実行中アクションID（0=なし）
    public float elapsed;            // アクション経過時間
    public float totalDuration;      // アクション全体時間
    public byte phase;               // PreMotion/Active/Recovery/Complete
    public byte cancelFlags;         // キャンセル可能条件（ビットフラグ）
}
```
**理由**: アクション実行状態をSoAで管理。AI判断時に「今何をやっているか」「キャンセル可能か」を高速参照。

### 2-2. システム用コンテナ（ランタイムコンテナで管理）

#### I. SpawnPointContainer（スポーンポイント管理）
```
用途: EnemySpawnerのスポーンポイント群を管理
型: TimedDataContainer<SpawnPointData> or PriorityPoolContainer<SpawnPointData>
```
**理由**: リスポーンタイマー、最大同時数、優先度を統合管理。

#### J. EventQueueContainer（AIイベントキュー）
```
用途: BrainEventContainerの実体。キャラクター間通信イベント
型: RingBufferContainer<BrainEvent>
```
**理由**: 設計書に記載の`BrainEventContainer`。「大ダメージを受けた」「仲間がやられた」等のイベントをリングバッファで管理。

#### K. HitRecordContainer（ヒット記録）
```
用途: HitBoxの二重ヒット防止記録 + ダメージ履歴
型: TimedDataContainer<HitRecord>
```
**理由**: 攻撃ごとのヒット済みターゲット記録。現在HitBoxが個別管理。

#### L. TweenHandleContainer（UIアニメーション管理）
```
用途: HudControllerのMotionHandle群の一元管理
型: TimedDataContainer<MotionHandle>
```
**理由**: LitMotionのハンドルリーク防止。期限切れで自動解放。

### 2-3. ゲームプレイ用コンテナ

#### M. InventoryContainer（インベントリ）
```csharp
public struct InventorySlot
{
    public ushort itemId;         // アイテムID
    public byte count;            // 所持数
    public byte slotType;         // 装備/消費/素材/キー
}
```
**理由**: GDDにインベントリシステムが記載。固定スロット数のコンテナ管理。

#### N. QuestFlagContainer（クエスト/フラグ管理）
```csharp
public struct QuestProgress
{
    public ushort questId;        // クエストID
    public byte state;            // NotStarted/Active/Complete/Failed
    public ushort progress;       // 進行度（討伐数等）
    public ushort target;         // 目標値
}
```
**理由**: GDDにグローバル/マップローカルフラグが設計済み。StateMapContainerで状態遷移管理。

#### O. DropTableContainer（ドロップテーブル）
```csharp
public struct DropEntry
{
    public ushort itemId;         // ドロップアイテムID
    public float probability;    // ドロップ確率
    public byte minCount;        // 最小数
    public byte maxCount;        // 最大数
    public ushort conditionFlag; // 条件フラグ（特定装備時ボーナス等）
}
```
**理由**: 敵撃破時のアイテムドロップ。PriorityPoolContainerで希少度管理も可能。

#### P. DialogueContainer（会話データ）
```
用途: イベントシーンの会話進行管理
型: RingBufferContainer<DialogueLine> （表示履歴のバックログ）
```

#### Q. CheckpointContainer（チェックポイント管理）
```
用途: セーブポイント/ファストトラベル地点の管理
型: GroupContainer<CheckpointData>（エリア別グループ化）
```

#### R. ParticleEffectPool（エフェクトプール）
```
用途: パーティクルエフェクトの同時表示数制限
型: PriorityPoolContainer<ParticleHandle>（低優先度から自動排出）
```

#### S. DamageNumberPool（ダメージ数値表示プール）
```
用途: ダメージポップアップの表示数管理
型: PriorityPoolContainer<DamagePopupData>（古い/小さいダメージから排出）
```

#### T. CameraShakeBuffer（カメラ揺れ管理）
```
用途: 複数の揺れソースの合成管理
型: TimedDataContainer<ShakeData>（時限付き、自動消失）
```

### 2-4. 全一覧サマリー

| # | コンテナ名 | 種別 | 優先度 | 用途 |
|---|-----------|------|--------|------|
| A | CharacterColdLog | SoA構造体 | ★★★★★ | AI判定ログ |
| B | DamageScoreEntry | SoA構造体 | ★★★★★ | ヘイト/脅威度 |
| C | ProjectileData | SoA構造体 | ★★★★☆ | 投射物管理 |
| D | AnimationState | SoA構造体 | ★★★☆☆ | アニメ状態キャッシュ |
| E | KnockbackData | SoA構造体 | ★★★★☆ | 被弾リアクション |
| F | InputState | SoA構造体 | ★★★★★ | 統一入力管理 |
| G | WeaponState | SoA構造体 | ★★★★☆ | 武器/コンボ状態 |
| H | ActionExecutionData | SoA構造体 | ★★★★☆ | アクション実行状態 |
| I | SpawnPointContainer | ランタイム | ★★★☆☆ | スポーン管理 |
| J | EventQueueContainer | ランタイム | ★★★★☆ | AIイベント通信 |
| K | HitRecordContainer | ランタイム | ★★★☆☆ | ヒット記録 |
| L | TweenHandleContainer | ランタイム | ★★☆☆☆ | UIアニメ管理 |
| M | InventoryContainer | ゲームプレイ | ★★★★☆ | インベントリ |
| N | QuestFlagContainer | ゲームプレイ | ★★★☆☆ | クエスト進行 |
| O | DropTableContainer | ゲームプレイ | ★★★☆☆ | ドロップテーブル |
| P | DialogueContainer | ゲームプレイ | ★★☆☆☆ | 会話バックログ |
| Q | CheckpointContainer | ゲームプレイ | ★★☆☆☆ | チェックポイント |
| R | ParticleEffectPool | エフェクト | ★★★☆☆ | エフェクト数制限 |
| S | DamageNumberPool | UI | ★★★☆☆ | ダメージ表示制限 |
| T | CameraShakeBuffer | 演出 | ★★☆☆☆ | カメラ揺れ合成 |

---

## 3. ODCパッケージへの改修要望

各コンテナの活用検討で判明した、パッケージ側に要望したい改修点。
**方針**: パッケージの汎用性を損なう改変は要望しない（プロジェクト固有の処理はゲーム側で吸収する）。

---

### 3-1. SpatialHashContainer2D — XY平面モード対応【重要度: 高】

**問題**:
現行の `SpatialHashContainer2D` はXZ平面（3D想定）での座標操作ベースと見られる。
本ゲームは2Dサイドスクロールで、座標はXY平面を使用する。
このままでは近傍クエリの軸が噛み合わず、正しく機能しない恐れがある。

**要望**:
- コンストラクタ引数でXY/XZの平面モードを切り替えられるようにする
- または `SpatialHashContainer2D` の2D版として純粋なXY平面専用クラスを提供する

```csharp
// 案1: 引数で軸指定
new SpatialHashContainer2D<T>(cellSize, plane: Plane.XY);

// 案2: 専用クラス（XY決め打ち）
new SpatialHashContainerXY<T>(cellSize);
```

**汎用性評価**: 設定可能な構成であり、汎用性を損なわない ✓

---

### 3-2. TimedDataContainer — OnExpiredコールバックへのデータ受け渡し【重要度: 高】

**問題**:
`TimedDataContainer<T>` の期限切れコールバックに「何が期限切れになったか」のデータが渡らない場合、
バフ解除時にUIへの通知や「消滅したデバフに応じたSE再生」ができない。

**要望**:
期限切れコールバックをデータ付きの `Action<T>` 形式で受け取れるようにする。
既存の `Action`（引数なし）との両方をサポートするオーバーロードが望ましい。

```csharp
// 現状（想定）
new TimedDataContainer<BuffData>(onExpired: () => { ... });

// 要望
new TimedDataContainer<BuffData>(onExpired: (data) => { ApplyBuffRemoval(data); });
```

**汎用性評価**: 汎用的な改善。期限切れデータの受け渡しは一般的なユースケース ✓

---

### 3-3. CooldownContainer — 進行率(Normalized)取得メソッド【重要度: 中】

**問題**:
スキルクールダウンUIの表示に「残り時間 / 最大時間」の正規化値(0.0〜1.0)が必要だが、
現状は残り時間と初期値を別途管理して手動計算が必要。

**要望**:
`GetNormalized(key)` メソッドで正規化された進行率(完了に向かって0→1)を直接取得できるようにする。

```csharp
float fillAmount = cooldowns.GetNormalized("skill_fireball"); // 0.0=完了, 1.0=リセット直後
```

**汎用性評価**: UIとの連携に汎用的に使える便利メソッド ✓

---

### 3-4. RingBufferContainer — ReadOnlySpan一括取得【重要度: 中】

**問題**:
コマンド入力判定（例: ↓↓攻撃 = 特殊技）のために直近N件の入力履歴を一括で参照したい。
個別インデックスアクセスを繰り返すよりも `ReadOnlySpan<T>` での一括取得が安全かつ高速。

**要望**:
バッファの現在の全内容を時系列順に `ReadOnlySpan<T>` で返すメソッドを追加。

```csharp
ReadOnlySpan<InputRecord> history = inputBuffer.AsSpan(); // 古い順に並んだスパン
// → LINQ不要でコマンドパターンマッチング可能
```

**汎用性評価**: NativeCollections的な安全アクセスパターンで汎用的 ✓

---

### 3-5. StateMapContainer — OnEnter/OnExitコールバック【重要度: 中】

**問題**:
AIモード遷移時（Idle→Chase等）にアニメーション切り替え・SE再生・内部状態リセットなどの
副作用処理が必要。現状は外部で `currentState` の変化を毎フレームポーリングするしかない。

**要望**:
状態ごとに `OnEnter` / `OnExit` コールバックを登録できるオプショナルなフック機構。

```csharp
stateMap.RegisterCallbacks(AiMode.Chase,
    onEnter: () => animator.SetTrigger("StartChase"),
    onExit:  () => animator.SetTrigger("StopChase")
);
```

**汎用性評価**: FSMの標準的な機能。オプショナル登録であれば複雑さは増さない ✓
ただし過度な機能追加になるようであれば、プロジェクト側でラッパーを作り吸収する。

---

### 3-6. GroupContainer — グループ移動コールバック【重要度: 低〜中】

**問題**:
混乱魔法・寝返りで `MoveToGroup()` を呼んだ後、AI状態のリセットやターゲット再選択などの
後処理が必要。移動完了のコールバックがないと毎フレームのポーリングが必要になる。

**要望**:
`MoveToGroup()` 実行時に `OnGroupChanged` コールバックを発火するオプション。

```csharp
groupContainer.OnGroupChanged += (item, fromGroup, toGroup) => {
    ResetAiState(item);
};
```

**汎用性評価**: グループ移動の通知は汎用的なユースケース ✓

---

### 3-7. 見送り（汎用性を損なうため対象外）

以下の改変はプロジェクト固有の要件であり、パッケージへの要望としない。
ゲーム側でラッパークラス / 拡張メソッドを実装して吸収する。

| 見送り要望 | 理由 |
|-----------|------|
| SoACharaDataDicとの直接統合 | プロジェクト固有すぎる |
| Unity `Time.time` / `Physics2D` との直接結合 | 特定エンジン依存になる |
| SourceGeneratorとのコード生成連携 | ODCパッケージの責務を超える |
| キャラ特化のヘイト計算ロジック組み込み | ゲームルール依存 |

---

## 4. 機能提案（統合版）

コンテナ活用・改修要望・プロジェクト全体の未着手課題を統合した実装優先度付き機能提案。

---

### フェーズ1: コアシステムへのコンテナ統合（最優先）

#### 機能提案 F-01: クールダウン一元管理システム
**使用コンテナ**: `CooldownContainer`
**現状の問題**: CD管理が6箇所以上に分散（CoopCooldownTracker / ComboManager / JudgmentLoop / DamageReceiver等）
**実装内容**:
- `AbilityHolder` 内に `CooldownContainer` を1つ配置
- 全アビリティCD・無敵時間・アーマー回復ディレイをここで一元管理
- `GetNormalized()` でUI（スキルアイコン）の充填率を直接取得（改修要望 3-3 が実現すれば）

**改変範囲**: `CoopCooldownTracker` 削除、`JudgmentLoop` のタイマー変数削除、`DamageReceiver._armorRecoveryTimer` 削除

---

#### 機能提案 F-02: AIモードFSM統合
**使用コンテナ**: `StateMapContainer<AiMode>`
**現状の問題**: `ModeController` が独自FSMを自前実装（_currentModeIndex, SetModes, SwitchMode）
**実装内容**:
- `ModeController` を `StateMapContainer<AiMode>` ベースに置き換え
- OnEnter/OnExitで状態遷移副作用を記述（改修要望 3-5 が実現すれば）
- 「Chaseモードに入って何秒経過」を `ElapsedTime` で直接取得 → 時間依存行動が簡潔に

---

#### 機能提案 F-03: 陣営管理システム刷新
**使用コンテナ**: `GroupContainer<int>` (objectHash単位)
**現状の問題**: `CharacterFlags.Belong` ビットフラグで陣営管理。全陣営スキャンがO(n)
**実装内容**:
- `GameManager` に `GroupContainer` を配置、Ally/Enemy/Neutral/Bossグループを管理
- `TargetSelector` / `ConditionEvaluator` のBelongフィルタをGroupContainer経由に変更
- 混乱魔法で `MoveToGroup()` を呼ぶだけで陣営変更が完結（O(1)）

---

#### 機能提案 F-04: バフ/デバフ管理リプレース
**使用コンテナ**: `TimedDataContainer<BuffEntry>` または `NotifyTimedDataContainer<BuffEntry>`
**現状の問題**: `StatusEffectManager` が手動タイマー管理 + swap-remove
**実装内容**:
- バフスロットを `TimedDataContainer` に移行
- 期限切れ時コールバックでUI通知・SE再生（改修要望 3-2 が実現すれば）
- `CharacterStatusEffects` 構造体のSoAデータと連携

---

#### 機能提案 F-05: 入力システムのSoA統合（AI/Player統一入力）
**使用コンテナ**: SoA構造体 `InputState`（追加コンテナ F）
**現状の問題**: AIとプレイヤー入力のパスが分離。BaseCharacterが入力ソースを意識
**実装内容**:
- `SoACharaDataDic` に `InputState` 構造体を追加
- `InputHandler`（プレイヤー）と `AIBrain`（AI）がともに `InputState` に書き込む
- `BaseCharacter` は `InputState` を読むだけ → 入力ソース非依存

---

### フェーズ2: パフォーマンス改善系

#### 機能提案 F-06: AI近傍検索の空間ハッシュ化
**使用コンテナ**: `SpatialHashContainer2D<int>` (objectHash格納)
**前提条件**: 改修要望 3-1（XY平面対応）の実現、またはラッパーでXY対応を吸収
**実装内容**:
- `SensorSystem.UpdateDetection()` の線形スキャンを空間ハッシュクエリに置換
- `TargetSelector.FilterCandidates()` の距離計算高速化
- キャラ移動時に `Update()` で位置を更新 → 近傍クエリO(1)〜O(k)

**注意**: XY平面への対応確認が必須。未対応の場合は独自ラッパーを実装して吸収する。

---

#### 機能提案 F-07: 投射物システム実装
**使用コンテナ**: SoA構造体 `ProjectileData`（追加コンテナ C）+ `TimedDataContainer`
**現状**: 設計書に記載があるが未実装
**実装内容**:
- `ProjectileData` をSoAに追加し投射物を一元管理
- 寿命管理を `TimedDataContainer` で行い、期限切れで自動プール返却
- ホーミング・貫通・AoE爆発を `moveType` / `pierceRemaining` / `aoeRadius` で制御

---

#### 機能提案 F-08: コマンド入力判定（格闘ゲーム風特殊技）
**使用コンテナ**: `RingBufferContainer<InputRecord>`（追加コンテナ J相当）
**実装内容**:
- `InputBuffer` を `RingBufferContainer` に拡張し直近16〜32入力を記録
- `AsSpan()` で履歴を一括取得してパターンマッチング（↓→攻撃 = 特殊技発動等）
- AIの行動ログにも流用（デバッグ可視化）

---

#### 機能提案 F-09: 敵スポーン優先度管理
**使用コンテナ**: `PriorityPoolContainer<EnemySpawnEntry>`
**実装内容**:
- `EnemySpawner` の最大同時数制限を `PriorityPoolContainer` に移行
- 容量超過時に画面外・低脅威度の敵を自動despawn
- ボスエリア接近時にボス関連スポーンを高優先度で保護

---

### フェーズ3: ゲームプレイ拡張系

#### 機能提案 F-10: ヒット記録の統合管理
**使用コンテナ**: `TimedDataContainer<HitRecord>`
**実装内容**:
- `HitBox` の二重ヒット防止記録を `TimedDataContainer` に移行
- 攻撃モーション終了で自動クリア（タイムアウト）
- 複数HitBoxが同一Attackerから管理されるよう統合

---

#### 機能提案 F-11: インベントリシステム
**使用コンテナ**: SoA構造体 `InventorySlot`（追加コンテナ M）
**現状**: GDD記載済み・未実装
**実装内容**:
- 固定スロット数の `InventorySlot[]` をSoAで管理
- アイテム使用・装備変更・ドロップ取得のAPIを整備
- UI（インベントリ画面）との連携

---

#### 機能提案 F-12: ドロップテーブル管理
**使用コンテナ**: SoA構造体 `DropEntry`（追加コンテナ O）+ `PriorityPoolContainer`
**実装内容**:
- 敵定義に `DropEntry[]` を紐付け
- `PriorityPoolContainer` で希少アイテムの優先度管理（ボーナス条件フラグ）
- 乱数シード固定でデバッグ再現性を確保

---

#### 機能提案 F-13: UIアニメーションハンドル管理
**使用コンテナ**: `TimedDataContainer<MotionHandle>`
**実装内容**:
- `HudController` のLitMotionハンドルを `TimedDataContainer` で一元管理
- 前回ハンドルの自動キャンセル（連続呼び出し時のリーク防止）
- HPバー・スタミナバー等のTweenを統一インターフェースで管理

---

### フェーズ4: 演出・デバッグ系

#### 機能提案 F-14: カメラ揺れ合成システム
**使用コンテナ**: `TimedDataContainer<ShakeData>`
**実装内容**:
- 複数ソース（爆発・被弾・着地）の揺れを合成して最終カメラオフセットを計算
- 各揺れソースに減衰カーブと優先度を設定
- 期限切れで自動消去、強い揺れが弱い揺れをマスク

---

#### 機能提案 F-15: ダメージポップアップ管理
**使用コンテナ**: `PriorityPoolContainer<DamagePopupData>`
**実装内容**:
- 同時表示数を制限（画面が数字で埋まるのを防止）
- 小ダメージを低優先度として大ダメージ発生時に自動排出
- クリティカル/属性等によるフォントスタイル切り替え

---

### 全機能提案サマリー

| # | 機能名 | フェーズ | 主コンテナ | 優先度 | 備考 |
|---|--------|---------|-----------|--------|------|
| F-01 | クールダウン一元管理 | 1 | CooldownContainer | ★★★★★ | 6箇所の分散CD解消 |
| F-02 | AIモードFSM統合 | 1 | StateMapContainer | ★★★★★ | ModeController置き換え |
| F-03 | 陣営管理刷新 | 1 | GroupContainer | ★★★★☆ | O(n)→O(1)スキャン |
| F-04 | バフ/デバフ管理 | 1 | TimedDataContainer | ★★★★☆ | StatusEffectManager置き換え |
| F-05 | 統一入力管理 | 1 | SoA:InputState | ★★★★★ | AI/Player入力統一 |
| F-06 | AI近傍検索高速化 | 2 | SpatialHash2D | ★★★★☆ | XY平面対応要確認 |
| F-07 | 投射物システム | 2 | SoA:ProjectileData | ★★★★☆ | 未実装システム |
| F-08 | コマンド入力 | 2 | RingBufferContainer | ★★★☆☆ | 特殊技・Span取得要望 |
| F-09 | 敵スポーン優先度 | 2 | PriorityPoolContainer | ★★★☆☆ | 自動despawn |
| F-10 | ヒット記録統合 | 3 | TimedDataContainer | ★★★☆☆ | 二重ヒット防止 |
| F-11 | インベントリ | 3 | SoA:InventorySlot | ★★★★☆ | GDD記載済み未実装 |
| F-12 | ドロップテーブル | 3 | SoA:DropEntry | ★★★☆☆ | 敵撃破アイテム |
| F-13 | UIアニメ管理 | 3 | TimedDataContainer | ★★☆☆☆ | ハンドルリーク防止 |
| F-14 | カメラ揺れ合成 | 4 | TimedDataContainer | ★★☆☆☆ | 演出品質向上 |
| F-15 | ダメージポップアップ | 4 | PriorityPoolContainer | ★★★☆☆ | 表示数制限 |

---

### ODC改修要望と機能提案の対応マップ

| 改修要望 | 影響する機能提案 | 未実現時の代替 |
|---------|----------------|--------------|
| 3-1 XY平面対応 | F-06（AI近傍検索） | ゲーム側ラッパーでXY変換を吸収 |
| 3-2 OnExpired引数付き | F-04（バフ管理）, F-10（ヒット記録） | ゲーム側でIDを別管理してコールバックで引く |
| 3-3 Normalizedメソッド | F-01（CD管理） | `(remaining / total)` をゲーム側で計算 |
| 3-4 RingBuffer Span取得 | F-08（コマンド入力） | ゲーム側で配列コピーして走査 |
| 3-5 OnEnter/OnExit | F-02（AI FSM） | ゲーム側でStateMapをラップしてポーリング |
| 3-6 グループ移動CB | F-03（陣営管理） | MoveToGroup後に明示的なリセット関数を呼ぶ |
