# ODC カスタムコンテナ提案

ODC新バージョンで導入済みの10種コンテナとは別に、
本プロジェクトのシステム設計書を横断分析して発見した追加カスタムコンテナの提案。

**ODCの設計思想との整合基準**:
- unmanaged 構造体のみ格納（GCフリー）
- objectHash をキーとした O(1) アクセス
- 配列ベースのキャッシュ効率重視
- アロケーションフリーの操作
- SoACharaDataDic と同じ密集配列 + スワップバック削除

---

## カスタムコンテナ一覧

| # | コンテナ名 | 分類 | 優先度 | 主な適用先 |
|---|-----------|------|--------|-----------|
| C1 | SparseSetContainer\<T\> | 汎用 | ★★★★★ | 投射物・状態異常・スポーン管理 |
| C2 | ThresholdAccumulatorContainer | ゲームロジック | ★★★★★ | EXP・状態異常蓄積・スコア |
| C3 | WeightedSamplerContainer\<T\> | 汎用 | ★★★★☆ | ドロップ・スポーン確率選択 |
| C4 | BitFlagTableContainer | 汎用 | ★★★★☆ | AbilityFlag・GateState・訪問済エリア |
| C5 | FixedSlotContainer\<T, N\> | 汎用 | ★★★☆☆ | ベルト・武器スロット・召喚枠 |
| C6 | FactionRelationContainer | ゲームロジック | ★★★☆☆ | 陣営間関係・混乱魔法 |

---

## C1. SparseSetContainer\<T\>

### 概要
「全キャラのうちの一部のみがアクティブな状態を持つ」ケースの高速管理。
通常の辞書やbool配列とは異なり、**アクティブ要素のみを連続メモリで保持**し、
フルスキャンなしで活性要素を一括走査できる。

ECSのSparse Setが原点。SoACharaDataDicのサブセット版に相当。

```
sparse[]   : hash → dense_index (非アクティブは-1)
dense[]    : 活性要素の値 T[]
denseHash[]: dense[i]に対応するhash  (逆引き用)
count      : 現在の活性数
```

### プロジェクトでの課題

| システム | 課題 |
|---------|------|
| MagicSystem | `NativeArray<bool> _active` で投射物のアクティブ判定 → 全N要素スキャンが毎フレーム |
| DamageSystem | `StatusEffectManager` が発症中エフェクトを `List<StatusEffect>` で管理 → GCアロケーション |
| ConfusionMagic | `Dictionary<int, ConfusionState> _confusedEntities` → 混乱中の敵だけをイテレートしたい |
| EnemySystem | アクティブ敵のみを更新ループ対象にしたい（非アクティブを飛ばしたい） |
| SummonSystem | アクティブ召喚スロットのみを毎Tickで更新したい |

### 設計案

```csharp
/// <summary>
/// 疎集合コンテナ。全キャラのサブセット（アクティブな部分集合）を
/// アロケーションなし・O(1)操作・連続メモリで管理する。
/// </summary>
public struct SparseSetContainer<T> : IDisposable
    where T : unmanaged
{
    // sparse[hash % capacity] → dense_index (-1 = 非登録)
    // dense[dense_index]      → T値
    // denseHash[dense_index]  → 元のhash（逆引き）

    /// <summary>ハッシュをキーに要素を登録/更新する。O(1)。</summary>
    public void Set(int hash, in T value);

    /// <summary>要素を削除する（スワップバック）。O(1)。</summary>
    public bool Remove(int hash);

    /// <summary>登録されているか確認。O(1)。</summary>
    public bool Contains(int hash);

    /// <summary>値を取得。O(1)。</summary>
    public bool TryGet(int hash, out T value);

    /// <summary>ref返しで直接書き換え。O(1)。</summary>
    public ref T GetRef(int hash);

    /// <summary>
    /// 全アクティブ要素を連続メモリで取得。GCアロケーションなし。
    /// ForEachループやJob Systemへ渡す際に使用。
    /// </summary>
    public ReadOnlySpan<T> ActiveValues { get; }
    public ReadOnlySpan<int> ActiveHashes { get; }
    public int Count { get; }
}
```

### 使用例

```csharp
// 投射物管理（MagicSystem）
SparseSetContainer<ProjectileData> _activeProjectiles;

// フレームごとに全活性投射物を一括処理
foreach (ref readonly ProjectileData proj in _activeProjectiles.ActiveValues)
{
    // GetComponent不要・GCなし・キャッシュ効率最良
}

// 混乱魔法（ConfusionMagic）
SparseSetContainer<ConfusionState> _confused;
_confused.Set(enemyHash, new ConfusionState { remainTime = 5f, originalFaction = ... });
// 混乱中の全敵だけを走査（全敵スキャン不要）
foreach (ref readonly ConfusionState c in _confused.ActiveValues) { ... }
```

### SoACharaDataDicとの違い

| 比較点 | SoACharaDataDic | SparseSetContainer |
|--------|----------------|-------------------|
| 対象 | 全キャラ（常駐） | 一部キャラ（一時的） |
| 用途 | キャラの恒常データ | 「今だけ」のサブセット状態 |
| 登録 | BaseCharacter生成時 | 条件発生時（状態異常発症など） |
| 削除 | BaseCharacter破棄時 | 条件消滅時（状態異常回復など） |

### 汎用性評価
汎用的。ECS・ゲームエンジン問わず広く使われる構造 ✓

---

## C2. ThresholdAccumulatorContainer

### 概要
「値を積み上げていき、閾値に達したら何かが起きる」パターンの汎用化。
EXP→レベルアップ・状態異常蓄積→発症・スコア→ランク達成など、
本プロジェクトでは**5システム以上で同じパターンが独立実装**されている。

```
accumulators[hash][key].current  : 現在の蓄積値
accumulators[hash][key].threshold: 発火閾値
accumulators[hash][key].overflow : 閾値超過分（次回持ち越し用）
```

### プロジェクトでの課題

| システム | 蓄積対象 | 閾値 | 発火アクション |
|---------|---------|------|--------------|
| LevelUpSystem | EXP | レベルテーブル参照値 | レベルアップ処理 |
| DamageSystem | 属性別蓄積ダメージ | 状態異常発症閾値 | 状態異常発症 |
| ChallengeMode | スコア | ランク境界値 | ランク変更通知 |
| BacktrackReward | 能力獲得トリガー | 報酬テーブル条件 | 報酬解放 |
| CoopAction | コンボゲージ | コンボ技発動閾値 | 連携技発動 |

### 設計案

```csharp
/// <summary>
/// 蓄積値が閾値に達したらコールバックを発火するコンテナ。
/// 複数のキー（ラベル）を1オブジェクトに持てる（EXP・HP蓄積・スタミナ蓄積等）。
/// objectHash × key で O(1) アクセス。
/// </summary>
public struct ThresholdAccumulatorContainer : IDisposable
{
    /// <summary>
    /// 蓄積値を加算する。閾値超過時はtrueを返す（コールバックが呼ばれる）。
    /// overflow（超過分）を次回に持ち越すかどうかをオプション指定。
    /// </summary>
    public bool Add(int hash, int key, float amount, bool carryOverflow = false);

    /// <summary>現在の蓄積値を取得。O(1)。</summary>
    public float Get(int hash, int key);

    /// <summary>進行率 (0.0〜1.0) を取得。UIバー表示に直結。</summary>
    public float GetNormalized(int hash, int key);

    /// <summary>閾値を変更する（レベルアップ後の次レベル要求EXP更新等）。</summary>
    public void SetThreshold(int hash, int key, float threshold);

    /// <summary>蓄積値をリセット（状態異常回復時等）。</summary>
    public void Reset(int hash, int key);

    /// <summary>閾値超過時コールバック。(hash, key, overflow) を受け取る。</summary>
    public Action<int, int, float> OnThresholdReached { get; set; }
}
```

### 使用例

```csharp
// LevelUpSystem
_accumulator.SetThreshold(playerHash, (int)AccumKey.EXP, expTable[currentLevel]);
bool leveledUp = _accumulator.Add(playerHash, (int)AccumKey.EXP, gainedExp, carryOverflow: true);

// DamageSystem（属性蓄積）
bool poisonProc = _accumulator.Add(enemyHash, (int)Element.Poison, poisonStack);
if (poisonProc) ApplyStatusEffect(enemyHash, StatusEffect.Poison);

// UIバー表示（進行率がそのままfillAmountに使える）
float expBarFill = _accumulator.GetNormalized(playerHash, (int)AccumKey.EXP);
```

### 汎用性評価
蓄積→閾値→発火はゲームを問わず汎用的なパターン ✓
carryOverflow オプションで「溢れたEXPを次レベルに持ち越す」ニュアンスも表現できる

---

## C3. WeightedSamplerContainer\<T\>

### 概要
重み付き確率テーブルから**O(1)でサンプリング**するコンテナ。
Walker's Alias Method を採用し、構築時O(n)・サンプリング時O(1)を実現。
`Random.Range` + 線形スキャンの O(n) アキュムレータパターンを置き換える。

### プロジェクトでの課題

| システム | 確率選択の用途 | 現状 |
|---------|--------------|------|
| EnemySystem | DropTable確率計算 | float[] cumulative + 線形スキャン |
| ShopSystem | ランダム商品生成 | 不明（未実装） |
| MagicSystem | 子弾エフェクト選択 | childProfile配列から選択 |
| EnemySystem | スポーンする敵種の重み付き選択 | 未設計 |
| LevelUpSystem | ドロップスキルのランダム選択 | 未設計 |

### 設計案

```csharp
/// <summary>
/// Walker's Alias Method による O(1) 重み付きサンプリングコンテナ。
/// 構築時に確率テーブルを事前計算し、以降のサンプリングをO(1)に。
/// </summary>
public struct WeightedSamplerContainer<T> : IDisposable
    where T : unmanaged
{
    /// <summary>
    /// (値, 重み) ペアのリストからテーブルを構築。O(n)。
    /// 一度構築したらランタイムでのサンプリングは全てO(1)。
    /// </summary>
    public static WeightedSamplerContainer<T> Build(
        ReadOnlySpan<T> items,
        ReadOnlySpan<float> weights);

    /// <summary>重み付き確率に従い1要素をサンプリング。O(1)。</summary>
    public T Sample(ref Unity.Mathematics.Random rng);

    /// <summary>重複なしでk個サンプリング（ドロップ複数品目等）。O(k)。</summary>
    public void SampleDistinct(ref Unity.Mathematics.Random rng, Span<T> results, int k);

    /// <summary>アイテム数。</summary>
    public int Count { get; }
}
```

### 使用例

```csharp
// EnemySystem: ドロップテーブル構築（敵初期化時に1回だけ）
DropEntry[] items = enemyDef.dropTable;
float[] weights = Array.ConvertAll(items, e => e.probability);
WeightedSamplerContainer<DropEntry> dropSampler =
    WeightedSamplerContainer<DropEntry>.Build(items, weights);

// 敵撃破時: O(1)でドロップ決定
DropEntry dropped = dropSampler.Sample(ref _rng);

// 複数ドロップ（重複なし3個）
Span<DropEntry> drops = stackalloc DropEntry[3];
dropSampler.SampleDistinct(ref _rng, drops, 3);
```

### Walker's Alias Method の原理
```
構築: O(n)でprobability[]とalias[]テーブルを生成
サンプリング:
  1. i = rng.NextInt(0, n)           → どのバケットか
  2. p = rng.NextFloat()
  3. return p < probability[i] ? items[i] : alias[i]
  → 常に2回の乱数生成のみ: O(1)
```

### 汎用性評価
Walker's Alias Method は有名なアルゴリズムで汎用性が高い ✓
`T : unmanaged` 制約でSoAと同等の型安全性を保てる

---

## C4. BitFlagTableContainer

### 概要
「objectHash ごとに複数のbooleanフラグをビット演算で管理」するコンテナ。
SoACharaDataDicの `CharacterFlags` (ulong) が「1キャラ固定フラグ」向けなのに対し、
こちらは「**可変個の対象×多数のフラグ**」を一元管理するテーブルとして機能する。

アビリティ解放状態・ゲートの開閉・マップ訪問済み管理に適用する。

```
rows[hash → index].flags : ulong (64ビット) = 64フラグまで
大規模フラグ: ulong[N] で拡張可
```

### プロジェクトでの課題

| システム | フラグ用途 | 現状の問題 |
|---------|-----------|-----------|
| BacktrackReward | AbilityFlag（能力獲得済み判定） | `Dictionary<string, bool>`（文字列キー・GC） |
| GateSystem | GateRegistry（ゲート開閉状態） | `Dictionary<string, bool>` |
| MapSystem | 訪問済みエリア記録 | 未設計（bool[]想定） |
| PlayerMovement | 解放済みアビリティ判定 | AbilityFlag ulong（直接ビット操作） |
| EquipmentSystem | 装備由来AbilityFlag合算 | 各装備のフラグをOR演算で合算 |
| AIRuleBuilder | ActionUnlockRegistry（アクション解放済み） | `HashSet<(ActionExecType, int)>` |

### 設計案

```csharp
/// <summary>
/// ハッシュキー × ビットフラグのテーブルコンテナ。
/// 対象ごとに最大64フラグ（ulong）をO(1)で操作できる。
/// GateRegistry / AbilityFlag / 訪問済みエリアなどに汎用的に使える。
/// </summary>
public struct BitFlagTableContainer : IDisposable
{
    /// <summary>フラグをセット。O(1)。</summary>
    public void Set(int hash, ulong flagMask);

    /// <summary>フラグをクリア。O(1)。</summary>
    public void Clear(int hash, ulong flagMask);

    /// <summary>フラグをトグル。O(1)。</summary>
    public void Toggle(int hash, ulong flagMask);

    /// <summary>指定フラグが全てセットされているか。O(1)。</summary>
    public bool HasAll(int hash, ulong flagMask);

    /// <summary>指定フラグのいずれかがセットされているか。O(1)。</summary>
    public bool HasAny(int hash, ulong flagMask);

    /// <summary>フラグを全取得（他システムとのOR合算用）。O(1)。</summary>
    public ulong GetFlags(int hash);

    /// <summary>全フラグをOR合算して返す（装備由来AbilityFlag合算等）。O(n)。</summary>
    public ulong AggregateAll();

    /// <summary>フラグが立っているエントリのhashを列挙。O(n)。</summary>
    public ReadOnlySpan<int> QueryHashes(ulong flagMask);
}
```

### 使用例

```csharp
// GateSystem: ゲート状態管理（string keyを排除してintハッシュ化）
int gateHash = gateId.GetHashCode();
_gateFlags.Set(gateHash, (ulong)GateFlag.Open);
bool isOpen = _gateFlags.HasAll(gateHash, (ulong)GateFlag.Open);

// BacktrackReward: 能力獲得フラグ
_abilityFlags.Set(playerHash, (ulong)AbilityFlag.DoubleJump);
bool canDoubleJump = _abilityFlags.HasAll(playerHash, (ulong)AbilityFlag.DoubleJump);

// EquipmentSystem: 装備由来フラグの合算
ulong totalFlags = _equipAbilityFlags.AggregateAll();
// → 全装備の AbilityFlag を OR 合算 → CharacterFlags に反映
```

### 汎用性評価
ビットフラグテーブルは汎用的。ゲームを問わずフラグ管理に使える ✓
64ビットを超えるケースは `ulong[]` 拡張版で対応可能

---

## C5. FixedSlotContainer\<T, N\>

### 概要
**固定個数のスロット**（武器2本・ベルト4枠・召喚2枠など）を統一的に管理するコンテナ。
「スロット番号でアクセス」「アクティブスロットのカーソル回転」「空きスロット検索」
「スロット間スワップ」といった操作を毎回再実装せずに済む。

固定サイズのためヒープアロケーション不要。`stackalloc` 互換を目指す設計。

### プロジェクトでの課題

| システム | スロット構成 | 現状の問題 |
|---------|------------|-----------|
| InventorySystem | BeltShortcut (4枠×2種) | 配列 + currentIndex カーソルを自前管理 |
| WeaponSystem | 右手/左手武器スロット (2枠) | `rightWeaponId` / `leftWeaponId` 別フィールド |
| SummonSystem | 召喚スロット (2枠固定) | `_slots[]` + 最古置換ロジックを自前実装 |
| ParryGuardSystem | ガードスロット（盾装備有無） | bool + 参照の組み合わせ |
| UISystem | CompanionStatusBar複数 | 固定2〜4本のバー参照を別々に保持 |

### 設計案

```csharp
/// <summary>
/// N個の固定スロットを管理する汎用コンテナ。
/// アクティブスロットのカーソル・空き検索・スワップを提供。
/// stackalloc 互換（ヒープアロケーションなし）。
/// </summary>
public struct FixedSlotContainer<T, N> : IDisposable
    where T : unmanaged
    where N : unmanaged, INativeCount  // 容量をコンパイル時定数で指定
{
    /// <summary>スロット番号で直接アクセス。O(1)。</summary>
    public ref T this[int slotIndex] { get; }

    /// <summary>スロットが空かどうか（デフォルト値との比較）。O(1)。</summary>
    public bool IsEmpty(int slotIndex);

    /// <summary>空きスロットの最初のインデックスを返す。O(N)（Nは固定小数）。</summary>
    public int FindEmpty();

    /// <summary>2スロット間の値をスワップ。O(1)。</summary>
    public void Swap(int slotA, int slotB);

    /// <summary>現在のアクティブスロットインデックス。</summary>
    public int ActiveIndex { get; set; }

    /// <summary>アクティブスロットを次に進める（ループ）。</summary>
    public void RotateNext();

    /// <summary>アクティブスロットの値への参照。</summary>
    public ref T Active { get; }

    /// <summary>全スロットをSpanで取得（一括処理用）。</summary>
    public Span<T> AsSpan();
}
```

### 使用例

```csharp
// InventorySystem: ベルトスロット
FixedSlotContainer<ConsumableSlot, N4> _consumableBelt;
_consumableBelt.RotateNext();                     // 次のスロットへ
ref ConsumableSlot active = ref _consumableBelt.Active; // アクティブアイテム取得

// SummonSystem: 召喚枠（最古置換）
FixedSlotContainer<SummonSlot, N2> _summonSlots;
int oldest = FindOldestSlot(_summonSlots);        // 最古スロット検索
_summonSlots[oldest] = newSummon;                 // 置換

// WeaponSystem: 武器スロット
FixedSlotContainer<WeaponSlot, N2> _weaponSlots; // [0]=右手, [1]=左手
ref WeaponSlot right = ref _weaponSlots[0];
ref WeaponSlot left  = ref _weaponSlots[1];
_weaponSlots.Swap(0, 1);                          // 武器持ち替え
```

### 汎用性評価
固定スロット管理はRPG・アクション問わず広く使われる ✓
コンパイル時定数N（N2/N4/N8等）で型安全性を保てる

---

## C6. FactionRelationContainer

### 概要
陣営ID × 陣営ID の関係（Hostile/Ally/Neutral）を**O(1)で双方向参照**するコンテナ。
現状の `CharacterFlags.Belong` ビットフラグでは
「この2体が敵対しているか」を毎回計算しており、
混乱魔法やボス連携で陣営関係が動的に変化した場合の対応が複雑になる。

```
relations[factionA][factionB] = Relation (Hostile/Ally/Neutral)
対角対称を保証（AがBに敵対 = BがAに敵対）
```

### プロジェクトでの課題

| システム | 陣営関係の用途 | 現状の問題 |
|---------|--------------|-----------|
| AICore | 攻撃対象フィルタ（TargetSelector） | Belongビット比較を毎フレーム全キャラで実行 |
| ConfusionMagic | 混乱中は味方を攻撃・敵を守る（関係反転） | 一時的な関係反転の管理が困難 |
| CompanionAI | 連携可能なキャラを判定 | Belongビットの同一性チェック |
| SummonSystem | 召喚キャラの陣営帰属 | 召喚者の陣営を継承する処理が手動 |
| BossSystem | ボス第2フェーズで中立→敵対化 | フェーズ遷移時の陣営変更が散在 |

### 設計案

```csharp
public enum Relation : byte
{
    Neutral = 0,
    Allied  = 1,
    Hostile = 2,
}

/// <summary>
/// 陣営間の関係を管理するコンテナ。
/// 最大N陣営（byte: 0〜255）の関係をO(1)で参照・更新できる。
/// 混乱魔法等の一時的な関係上書きをスタック管理で実現。
/// </summary>
public struct FactionRelationContainer : IDisposable
{
    /// <summary>2陣営間の関係を取得。O(1)。</summary>
    public Relation Get(byte factionA, byte factionB);

    /// <summary>2陣営間の関係を設定（対称性を自動保証）。O(1)。</summary>
    public void Set(byte factionA, byte factionB, Relation relation);

    /// <summary>一時的な関係上書き（混乱魔法用）。Resetで元に戻る。</summary>
    public void SetTemporary(byte factionA, byte factionB, Relation relation, float duration);

    /// <summary>指定陣営が敵対する全陣営リストを取得。O(N)。</summary>
    public ReadOnlySpan<byte> GetHostile(byte faction);

    /// <summary>指定陣営が同盟する全陣営リストを取得。O(N)。</summary>
    public ReadOnlySpan<byte> GetAllied(byte faction);

    /// <summary>現在のフレームで一時上書きを更新（期限切れを元に戻す）。</summary>
    public void Tick(float deltaTime);
}
```

### 使用例

```csharp
// 初期化（ゲーム開始時）
_factions.Set(Faction.Player, Faction.Enemy,   Relation.Hostile);
_factions.Set(Faction.Player, Faction.Neutral, Relation.Neutral);
_factions.Set(Faction.Player, Faction.Ally,    Relation.Allied);
_factions.Set(Faction.Enemy,  Faction.Ally,    Relation.Hostile);

// TargetSelector: 攻撃対象フィルタ（O(1)判定）
bool isTarget = _factions.Get(attacker.faction, candidate.faction) == Relation.Hostile;

// ConfusionMagic: 15秒間陣営関係を反転
_factions.SetTemporary(Faction.Enemy, Faction.Enemy, Relation.Hostile, 15f);
// ← 混乱中の敵同士が戦い始める。15秒後に自動で元に戻る

// BossSystem: フェーズ2で中立NPCが敵対化
_factions.Set(Faction.Neutral, Faction.Player, Relation.Hostile);
```

### N陣営の実装

N=8 陣営として `byte[8][8]` = 64バイトで全関係を保持できる。
キャッシュライン1本（64バイト）に収まり、参照コストが極小。

### 汎用性評価
陣営関係マトリックスはRTS・RPG・対戦ゲームで汎用的 ✓
一時上書き機能はプロジェクト固有寄りだが、`SetTemporary` をオプショナルにすれば汎用性を損なわない

---

## 全提案サマリーと実装優先度

### 優先度マトリックス

| # | コンテナ | 解決する問題数 | ODC設計適合度 | 汎用性 | 実装難易度 | 総合 |
|---|---------|-------------|-------------|--------|----------|------|
| C1 | SparseSetContainer | 5システム | ◎ | ◎ | 中 | ★★★★★ |
| C2 | ThresholdAccumulator | 5システム | ◎ | ◎ | 低 | ★★★★★ |
| C3 | WeightedSampler | 4システム | ○ | ◎ | 中 | ★★★★☆ |
| C4 | BitFlagTable | 6システム | ◎ | ◎ | 低 | ★★★★☆ |
| C5 | FixedSlot | 5システム | ○ | ○ | 低 | ★★★☆☆ |
| C6 | FactionRelation | 5システム | ○ | ○ | 中 | ★★★☆☆ |

### ODCパッケージとの責務分担

```
ODC新バージョン既存コンテナ
  ├── 時間系: CooldownContainer, TimedDataContainer, StateMapContainer
  ├── 空間系: SpatialHashContainer2D
  ├── プール系: PriorityPoolContainer, RingBufferContainer, GroupContainer
  └── キャッシュ系: ComponentCache

カスタムコンテナ（本提案）
  ├── 部分集合反復: C1 SparseSetContainer      ← ODCに存在しないECS的パターン
  ├── 蓄積トリガー: C2 ThresholdAccumulator     ← ゲーム固有だが汎用的なパターン
  ├── 確率選択:    C3 WeightedSampler          ← アルゴリズム特化
  ├── フラグ管理:  C4 BitFlagTable             ← SoAと相補的なビット集合
  ├── スロット管理: C5 FixedSlot               ← UIとロジックをつなぐ固定配列
  └── 関係管理:   C6 FactionRelation          ← ゲームルール層に近い

SoACharaDataDic（プロジェクト専用）
  └── キャラ恒常データの O(1) ハッシュアクセス
```

### 実装順序（推奨）

```
Phase A（コアシステム実装前に用意すべき）
  C2 ThresholdAccumulator → DamageSystem・LevelUpSystemと同時に実装
  C4 BitFlagTable         → GateSystem・BacktrackReward導入時に用意

Phase B（戦闘・AIシステム拡張時）
  C1 SparseSetContainer   → 投射物システム・ConfusionMagic実装時
  C6 FactionRelation      → ConfusionMagic・BossSystemフェーズ実装時

Phase C（コンテンツ拡充時）
  C3 WeightedSampler      → DropTable・スポーンシステム拡充時
  C5 FixedSlot            → UI整備・インベントリシステム実装時
```

---

## SisterGameの課題から発想した汎用コンテナ提案

SisterGameのシステム設計を分析する過程で見えてきた課題を起点に、
**他のプロジェクトでも普通に使えるODCパッケージ候補**として設計し直したコンテナ群。
各コンテナの「SisterGameでの動機」と「汎用適用例」を両方示す。



---

### G1. HitDeduplicationContainer（ヒット重複排除）

**SisterGameでの動機**: WeaponSystemの `HitBox` が `Dictionary<int, bool>` で
「今回の攻撃でヒット済みターゲット」を管理。攻撃1回ごとに Dictionary をnewしてGCが発生し、
複数HitBoxが独立管理しているため貫通ヒットの重複防止が困難。

**抽象化すると**: 「あるイベントインスタンス（攻撃・爆発・AoE等）に対して、
ターゲットごとの"処理済み"状態をアロケーションなしで追跡する」コンテナ。

**他プロジェクトでの用途**:
- AoEスペル・チェインライトニングの複数ヒット防止
- 範囲ピックアップ（コインが複数同時衝突する場合）
- マルチヒット投射物（貫通矢・散弾）
- トリガーゾーンの重複イベント防止

```csharp
/// <summary>
/// イベントインスタンス単位でのターゲット処理重複を防止するコンテナ。
/// Dictionary を使わず、ビットセットまたはSparseSetで GCフリーを実現する。
/// eventHash（攻撃・スペル等のインスタンスID）× targetHash で O(1) 判定。
/// </summary>
public struct HitDeduplicationContainer : IDisposable
{
    /// <summary>
    /// ターゲットへのヒットを試みる。
    /// 初回はtrue（処理を進める）、既記録はfalse（スキップ）を返す。O(1)。
    /// </summary>
    public bool TryRecord(int eventHash, int targetHash);

    /// <summary>
    /// 貫通残回数付きのヒット試行。
    /// pierceRemaining == 0 の場合は無制限貫通。O(1)。
    /// </summary>
    public bool TryRecord(int eventHash, int targetHash, ref byte pierceRemaining);

    /// <summary>
    /// イベント終了時にヒット記録を一括解放。O(k)（k=ヒット数）。
    /// 攻撃モーション終了・スペル消滅・爆発後に呼ぶ。
    /// </summary>
    public void Release(int eventHash);

    /// <summary>eventHashに対するヒット済みターゲット数。</summary>
    public int GetHitCount(int eventHash);
}
```

**使用例（SisterGame）**:
```csharp
// HitBox.OnTriggerEnter2D
if (!GameManager.HitDedup.TryRecord(_attackHash, targetHash)) return;
// → 同じ攻撃で同じターゲットに2回当たってもスキップ

// 貫通武器（残り2回まで貫通可）
byte pierce = 2;
if (!GameManager.HitDedup.TryRecord(_attackHash, targetHash, ref pierce)) return;

// アクション終了時
GameManager.HitDedup.Release(_attackHash);
```

---

### G2. FlagComboLookupContainer\<TEffect\>（フラグ組み合わせ効果テーブル）

**SisterGameでの動機**: DamageSystem + ElementalGate で
「Fire+Ice=蒸気爆発」のような属性組み合わせ判定を毎回
`if(flags.HasFlag(Fire) && flags.HasFlag(Ice))` と書く必要があり、
条件分岐がコード全体に散乱する。

**抽象化すると**: 「ビットフラグの組み合わせ（ulong）をキーに、
事前登録したエフェクトをO(1)で引く読み取り専用テーブル」。
属性・状態異常・スキルシナジー・クラフトレシピなど、
「フラグの重なりで何かが起きる」全ての場面に適用できる。

**他プロジェクトでの用途**:
- 属性相性・弱点テーブル（JRPG）
- 装備セットボーナス（複数部位装備で追加効果）
- スキルシナジー（A+BのスキルでCの効果が発動）
- クラフトレシピ（素材の組み合わせ → 成果物）
- 環境インタラクション（水+電撃=感電地形）

```csharp
/// <summary>
/// ulong ビットフラグの組み合わせをキーに、事前登録したエフェクトをO(1)で取得する
/// 読み取り専用テーブルコンテナ。
/// 構築時 O(n)、サンプリング時 O(1)（完全ハッシュテーブル）。
/// TEffect は unmanaged 構造体で自由に定義する。
/// </summary>
public struct FlagComboLookupContainer<TEffect> : IDisposable
    where TEffect : unmanaged
{
    /// <summary>
    /// (フラグ組み合わせ, エフェクト) ペアリストからテーブルを構築する。
    /// requiredAll=true: 指定フラグを全て含む場合にマッチ。
    /// requiredAll=false: いずれか含む場合にマッチ（OR検索）。
    /// </summary>
    public static FlagComboLookupContainer<TEffect> Build(
        ReadOnlySpan<(ulong flagCombo, TEffect effect)> entries,
        bool requiredAll = true);

    /// <summary>
    /// 入力フラグに対してマッチするエフェクトを返す。O(1)。
    /// 複数マッチ時は登録順で最初にマッチしたものを返す。
    /// </summary>
    public bool TryGet(ulong inputFlags, out TEffect effect);

    /// <summary>
    /// 入力フラグにマッチする全エフェクトを列挙する。O(n)。
    /// （クラフトで「このフラグ組み合わせで作れる全レシピ」等に使用）
    /// </summary>
    public int GetAll(ulong inputFlags, Span<TEffect> results);
}
```

**使用例（SisterGame）**:
```csharp
// 構築（ゲーム起動時1回）
var resonanceTable = FlagComboLookupContainer<ResonanceEffect>.Build(new (ulong, ResonanceEffect)[]
{
    ((ulong)(Element.Fire | Element.Ice),     new ResonanceEffect { damageMultiplier = 1.5f, aoeRadius = 3 }),
    ((ulong)(Element.Thunder | Element.Ice),  new ResonanceEffect { damageMultiplier = 1.3f, procStatus = Status.Paralyze }),
});

// ダメージ計算時（O(1)）
ulong comboKey = (ulong)(attackElement | targetAccumulated);
if (resonanceTable.TryGet(comboKey, out ResonanceEffect resonance))
    damage *= resonance.damageMultiplier;
```

---

### G3. ScoredCandidateBuffer\<T\>（スコア付き候補バッファ）

**SisterGameでの動機**: AICore の3層判定（Target判定 / Action判定）で、
「複数の候補を評価してスコアを付け、最高スコアのものを選ぶ」処理を
毎フレーム・全AIキャラ分行う。現状は中間配列の確保が分散しGCを生む。

**抽象化すると**: 「ownerHash単位で複数の候補(T)を提出しスコアを付け、
最高スコアのものを選択・取得するフレームバッファ」。
AIに限らず「評価→選択」パターン全般に使える。

**他プロジェクトでの用途**:
- AIの行動選択・ターゲット選択（あらゆるゲームのAI）
- ナビゲーションのウェイポイント選択
- UIフォーカス管理（複数UI候補から最適なものを選択）
- クエスト優先度管理（複数クエストからプレイヤー向け最優先を決定）
- オートバトルの攻撃先/スキル選択

```csharp
/// <summary>
/// ownerHash ごとに T 型の候補を提出・スコアリングし、
/// 最高スコアの候補を選択するフレームバッファ。
/// 毎フレームの評価サイクルを GCフリーでサポートする。
/// </summary>
public struct ScoredCandidateBuffer<T> : IDisposable
    where T : unmanaged
{
    /// <summary>
    /// 評価サイクルを開始する（前回の候補をクリア）。
    /// 毎判定サイクルの先頭で呼ぶ。
    /// </summary>
    public void BeginEvaluation(int ownerHash);

    /// <summary>
    /// スコア付きで候補を提出する。O(1)。
    /// 容量上限（maxCandidates）超過時は最低スコアの候補を自動排出。
    /// </summary>
    public void Submit(int ownerHash, in T candidate, float score);

    /// <summary>最高スコアの候補を取得。O(1)。</summary>
    public bool TryGetBest(int ownerHash, out T best, out float bestScore);

    /// <summary>
    /// スコア上位k件を取得（複数選択・表示用）。O(k log k)。
    /// </summary>
    public int GetTopK(int ownerHash, Span<T> results, int k);

    /// <summary>現在の候補数。</summary>
    public int GetCount(int ownerHash);
}
```

**使用例（SisterGame）**:
```csharp
// JudgmentLoop内（AIターゲット選択）
_candidateBuffer.BeginEvaluation(actorHash);
foreach (int candidateHash in _sensor.Detected)
{
    float score = CalcThreat(actorHash, candidateHash);
    _candidateBuffer.Submit(actorHash, candidateHash, score);
}
_candidateBuffer.TryGetBest(actorHash, out int bestTarget, out float _);

// AIアクション選択も同じパターン
_candidateBuffer.BeginEvaluation(actorHash);
foreach (AIRule rule in aiRules)
    _candidateBuffer.Submit(actorHash, rule.actionId, EvaluateRule(rule));
_candidateBuffer.TryGetBest(actorHash, out ushort selectedAction, out float _);
```

---

### G4. MultiPartySequenceContainer\<TState\>（複数参加者のシーケンス管理）

**SisterGameでの動機**: CoopAction設計書の「プレイヤー+仲間が特定タイミングで
連携技を発動する」仕組みでは、複数キャラ間で「今どのステップか」「入力猶予が残っているか」
という状態を共有する必要がある。個々のSoAデータでは表現できない**グループ共有状態**。

**抽象化すると**: 「複数の参加者エンティティが順番に条件を満たすことで
ステップが進行し、全ステップ完了で何かが起きる、タイムアウト付きシーケンス管理」。

**他プロジェクトでの用途**:
- 協力パズル（複数プレイヤーが順番にスイッチを押す）
- コンボシステム（複数の入力が順番に成立すると技発動）
- マルチプレイヤーのトレード（双方がAcceptして成立）
- NPCとの連携イベント（プレイヤー→NPC→プレイヤーの順で行動して発動）
- ボスのギミック（複数プレイヤーが同時/順番に特定位置に立つ）

```csharp
/// <summary>
/// 複数参加者が順番にステップをクリアするシーケンスを管理するコンテナ。
/// 各ステップには時間ウィンドウがあり、超過するとシーケンスが失敗/リセットされる。
/// TState は各ステップの共有状態（進行度・スコア等）を格納する unmanaged 構造体。
/// </summary>
public struct MultiPartySequenceContainer<TState> : IDisposable
    where TState : unmanaged
{
    /// <summary>
    /// 新しいシーケンスを開始する。
    /// participants: 参加者の hash 配列（固定、最大8名）。
    /// stepCount: 総ステップ数。
    /// windowPerStep: 各ステップの入力猶予時間（秒）。
    /// </summary>
    public int Begin(ReadOnlySpan<int> participantHashes,
                     byte stepCount,
                     float windowPerStep,
                     in TState initialState);

    /// <summary>
    /// 参加者がステップをクリアしたことを報告する。
    /// シーケンスが次ステップに進んだ場合 true を返す。O(1)。
    /// </summary>
    public bool Advance(int sequenceId, int participantHash, in TState newState);

    /// <summary>このエンティティが参加しているシーケンスを検索。O(1)。</summary>
    public bool TryGetSequence(int participantHash, out int sequenceId,
                                out byte currentStep, out TState state);

    /// <summary>シーケンスが全ステップ完了しているか。O(1)。</summary>
    public bool IsCompleted(int sequenceId);

    /// <summary>シーケンスを明示的に終了（成功/失敗）。</summary>
    public void End(int sequenceId);

    /// <summary>時間ウィンドウ更新。タイムアウトしたシーケンスを自動終了。</summary>
    public void Tick(float deltaTime);
}
```

**使用例（SisterGame）**:
```csharp
// CoopAction: 連携技シーケンス開始
int seqId = _sequence.Begin(
    new[] { playerHash, companionHash },
    stepCount: 3,
    windowPerStep: 1.5f,
    initialState: new CoopState { comboId = 42 }
);

// 各キャラからのステップ報告
if (playerInputsComboA)
    _sequence.Advance(seqId, playerHash, new CoopState { comboId = 42, step1Done = true });

// 毎フレーム更新（タイムアウト管理）
_sequence.Tick(Time.deltaTime);

// シーケンス完了チェック
if (_sequence.IsCompleted(seqId))
    TriggerCoopAttack(seqId);
```

---

### G5. StackableEffectContainer\<TEffect\>（スタック可能な時限エフェクトスロット）

**SisterGameでの動機**: DamageSystem の状態異常管理では
「蓄積値が閾値を超えると発症し、一定時間継続してDoTを与え、スタック可能で、
回復すると即時解除される」という複合的な状態を管理する必要がある。
`CharacterStatusEffects` 構造体での固定配列管理と Tick処理が複雑化している。

**抽象化すると**: 「エンティティごとに固定N枠のエフェクトスロットを持ち、
各スロットがスタック数・継続時間・Tick処理を管理する汎用バフ/デバフコンテナ」。

**他プロジェクトでの用途**:
- あらゆるRPG/アクションゲームのバフ/デバフ管理
- DoT（毒・燃焼・出血等）の時間処理
- シールド/バリア層の重ね掛け管理
- スタックバフ（強化が10回積めるシステム）
- チャージシステム（ゲージが最大スタックで技発動）

```csharp
/// <summary>
/// エンティティごとに N 枠の時限スタックエフェクトスロットを管理するコンテナ。
/// スタック加算・継続時間・周期Tick・期限切れ解放を一元管理する。
/// TEffect は unmanaged 構造体で効果の定義を自由に格納する。
/// </summary>
public struct StackableEffectContainer<TEffect> : IDisposable
    where TEffect : unmanaged
{
    /// <summary>
    /// エフェクトを適用する。
    /// 同一 effectKey のスロットが存在する場合はスタックを追加し継続時間を更新。
    /// 存在しない場合は空きスロットに新規登録。O(N)（N=スロット数=固定小）。
    /// </summary>
    public bool Apply(int entityHash, int effectKey, in TEffect effectData,
                      byte addStacks = 1, float duration = 0f,
                      float tickInterval = 0f);

    /// <summary>指定エフェクトを即時解除。O(N)。</summary>
    public bool Remove(int entityHash, int effectKey);

    /// <summary>指定エフェクトが発動中か。O(N)。</summary>
    public bool IsActive(int entityHash, int effectKey);

    /// <summary>スタック数を取得。O(N)。</summary>
    public byte GetStacks(int entityHash, int effectKey);

    /// <summary>
    /// 1エンティティのエフェクトを時間更新する。
    /// Tick対象エフェクトの処理用コールバックを受け取る。
    /// 期限切れスロットを自動解放。O(N)。
    /// </summary>
    public void Tick(int entityHash, float deltaTime,
                     TickCallback<TEffect> onTick = null,
                     ExpireCallback<TEffect> onExpire = null);

    /// <summary>全エンティティを一括Tick（毎フレーム呼ぶ版）。O(M×N)。</summary>
    public void TickAll(float deltaTime,
                        TickCallback<TEffect> onTick = null,
                        ExpireCallback<TEffect> onExpire = null);

    /// <summary>
    /// エンティティのアクティブエフェクトを全て列挙する。
    /// results に (effectKey, effectData, stacks, remainingTime) を書き込み、件数を返す。O(N)。
    /// UI表示・デバッグ用途を想定。GCフリー（Span で受け取る）。
    /// </summary>
    public int GetActiveEffects(int entityHash,
        Span<(int effectKey, TEffect effectData, byte stacks, float remainingTime)> results);

    /// <summary>アクティブスロット数（現在かかっているエフェクトの種類数）。O(1)。</summary>
    public int GetActiveCount(int entityHash);

    public delegate void TickCallback<T>(int entityHash, int effectKey, in T effect, byte stacks)
        where T : unmanaged;
    public delegate void ExpireCallback<T>(int entityHash, int effectKey, in T effect)
        where T : unmanaged;
}
```

**使用例（SisterGame）**:
```csharp
// 毒状態の定義（unmanaged構造体 = どんなゲームでも自由に定義）
public struct PoisonEffect : unmanaged
{
    public float damagePerTick;
    public byte  elementType;
}

// 発症（蓄積閾値超過時）
_effects.Apply(targetHash, effectKey: (int)Status.Poison,
    effectData: new PoisonEffect { damagePerTick = 10f, elementType = (byte)Element.Dark },
    addStacks: 1, duration: 10f, tickInterval: 0.5f);

// 毎フレームTick（DoTダメージをDamageSystemに渡す）
_effects.TickAll(Time.deltaTime,
    onTick: (hash, key, effect, stacks) => {
        DamageSystem.ApplyDot(hash, effect.damagePerTick * stacks);
    },
    onExpire: (hash, key, effect) => {
        UISystem.RemoveStatusIcon(hash, key);
    });

// 解毒アイテム使用
_effects.Remove(targetHash, (int)Status.Poison);
```

---

### SisterGameの課題から発想した汎用コンテナ サマリー

| # | コンテナ | SisterGameの動機 | 汎用パターン |
|---|---------|----------------|------------|
| G1 | `HitDeduplicationContainer` | HitBoxの二重ヒット防止 | イベントインスタンス×ターゲットの処理済み管理 |
| G2 | `FlagComboLookupContainer<T>` | 属性組み合わせ効果テーブル | フラグ組み合わせ→エフェクトのO(1)引き当て |
| G3 | `ScoredCandidateBuffer<T>` | AI三層判定の候補評価 | 評価→スコアリング→最適選択のフレームバッファ |
| G4 | `MultiPartySequenceContainer<T>` | 連携アクションのステップ進行 | 複数エンティティ×ステップ×タイムウィンドウ |
| G5 | `StackableEffectContainer<T>` | 状態異常スロット管理 | スタック可能・時限・Tick付きエフェクトスロット |

### 全コンテナの関係図（C1〜C6 + G1〜G5）

```
汎用基盤層（ODCパッケージ）
├── C1 SparseSetContainer     ← G1の内部実装に活用
├── C2 ThresholdAccumulator   ← G5と組み合わせて「蓄積→発症」パイプライン
├── C3 WeightedSampler        ← ドロップ・スポーン確率選択
├── C4 BitFlagTable           ← G2のフラグキーに活用
├── C5 FixedSlotContainer     ← G5の内部スロット実装
└── C6 FactionRelation        ← G3のターゲット評価スコアに陣営係数を加算

SisterGameの課題から発想した汎用層（ODCパッケージ候補）
├── G1 HitDeduplicationContainer     ← アクション/スペル系ゲーム全般
├── G2 FlagComboLookupContainer      ← 属性/シナジー/クラフト系ゲーム全般
├── G3 ScoredCandidateBuffer         ← AI搭載ゲーム全般
├── G4 MultiPartySequenceContainer   ← 協力プレイ/コンボ系ゲーム全般
└── G5 StackableEffectContainer      ← RPG/アクション全般（最も汎用）

組み合わせ活用例（SisterGame）
  C2 ThresholdAccumulator.OnThreshold → G5 StackableEffectContainer.Apply
  G3 ScoredCandidateBuffer.TryGetBest → C6 FactionRelation でスコア補正
  G1 HitDeduplication + G5 StackableEffect → 一撃で状態異常スタックを複数付与しない制御
```
