# System: SummonSystem
Section: 3 — 世界の広がり

## 責務
召喚した魔物を個体ごとに管理し、編成、召喚後の寿命、解除を扱う。

## 召喚と編成の設計方針

- 骸骨騎士とシスターが3Dダンジョンで出会ったNPC魔物の依頼や物語を進めると、シスターがその本人や仲間を召喚できる。解放する種類はイベントが指定する。マップの野生の魔物は通常の戦闘相手。
- 召喚魔物の行動は作者が定義する。プレイヤーは指揮官を通じて簡単な指示を出す。プレイヤーによる詳細なAI設定はシスター専用とする。
- 編成コストは呼び出した1体ごとに数える。同じ種類の魔物を2体呼ぶと、その魔物2体分のコストを使う。
- 通常探索は少人数で進める。魔物溜まりや指定ボス戦では一時的に人数とコストの上限を増やし、戦闘終了・撤退・退出時に探索編成へ戻す。二周目以降は、条件を満たした一部のボスも召喚候補に加わる。
- 個体ごとのコスト、通常時と増援時の上限、戦闘中に呼び直せるタイミングや補充方法は試作で決める。

## 3D遠征での同行

`ExpeditionSummonDeployment3D` の `Exploration Leader` に騎士を設定すると、通常編成で出撃した魔物が同じ物理空間で騎士に歩いて追従する。停止距離は `Follow Distance` で設定する。集団戦の固定更新で移動した個体は、そのステップの追従を省く。遭遇が固定ステップの途中で始まるか終わる場合も、移動と重力はモーターで一度だけ適用する。戦闘専用の増援は遭遇側で管理する。[遠征の出撃と同行](../../Assets/MyAsset/Runtime/AI/ExpeditionSummonDeployment3D.cs)・[3D移動](../../Assets/MyAsset/Runtime/Character/CharacterMotor3D.cs)

## 召喚魔法の固定枠

`SummonManager` は同時召喚2体、`PartyManager` はパーティ上限4人を定義する。満員時は `TrySummon` が失敗し、`TrySummonWithReplace` では残り時間が最も短い1体を解除して入れ替える。以下はこの固定枠を前提にしたセクション3の設計。[SummonManager](../../Assets/MyAsset/Core/AI/Summon/SummonManager.cs) · [PartyManager](../../Assets/MyAsset/Core/Common/Section3Utilities.cs) · [SummonMagicBridge](../../Assets/MyAsset/Core/AI/Summon/SummonMagicBridge.cs)

## 依存
- 入力: MagicSystem（MagicCaster, MagicDefinition）、CompanionAI_Basic（FollowBehavior再利用）、AICore（AIBrain）、PartyManager
- 出力: OnSummonCreated, OnSummonDismissed イベント → UISystem（召喚枠表示）、AICore（召喚獣AI）

## コンポーネント構成

| コンポーネント | 責務 | MonoBehaviour? |
|--------------|------|---------------|
| SummonManager | 召喚枠管理（最大2枠）、寿命Tick、全解除 | No |
| SummonedCharacterController | 召喚されたキャラのAI制御。AIBrainベース+寿命制限 | Yes |
| SummonDefinition | 召喚獣のデータ定義（AIMode、寿命、タイプ等） | No (ScriptableObject) |
| SummonSpawner | 召喚獣のインスタンス生成・初期化・プール管理 | No |

## インタフェース

### MagicCaster → SummonManager
- MagicType.Summon のMagicDefinition発動時にSummonManager.TrySummon()を呼ぶ
- 既存MagicCasterのCast()フローに乗る（MP消費、キャスト時間、クールダウン）

### SummonManager → PartyManager
- 召喚前にPartyManager.CanSummon()で枠チェック
- 枠満杯時は最も古い召喚獣を解除して新しいものと入れ替え

### SummonedCharacterController → CompanionAI FollowBehavior
- 追従ロジックはFollowBehaviorを再利用（コンポジション）
- 追従対象はデフォルトでプレイヤー、SummonType.Combatは敵へ向かう

### SummonManager → GameManager.Events
```csharp
GameManager.Events.OnSummonCreated?.Invoke(summonerHash, summonHash);
GameManager.Events.OnSummonDismissed?.Invoke(summonerHash, summonHash);
```

## データフロー

```
プレイヤー/仲間が召喚魔法を発動
    → MagicCaster.Cast(MagicDefinition[type=Summon])
    → MP消費 + キャスト時間
    → SummonManager.TrySummon(summonDefinition, casterHash, position)
        → PartyManager.CanSummon() チェック
            → 枠満杯 → 最古の召喚獣を Dismiss()
        → SummonSpawner.Spawn(summonDefinition, position)
        → SummonedCharacterController.Initialize(casterHash, duration, definition)
        → SoAコンテナにデータ登録（HP/MP/CombatStats等）
        → GameManager.Events.OnSummonCreated

召喚中（毎フレーム）:
    → SummonManager.Tick(deltaTime)
    → 各召喚枠の remainingTime 減算
    → remainingTime <= 0 → Dismiss()

召喚獣HP=0:
    → DamageSystem → OnCharacterDeath
    → SummonManager.OnSummonDeath(hash) → 枠解放

プレイヤー死亡/エリア遷移:
    → SummonManager.DismissAll()
```

## データ構造

### SummonDefinition (ScriptableObject)
```csharp
[CreateAssetMenu(menuName = "Game/Summon/SummonDefinition")]
public class SummonDefinition : ScriptableObject
{
    [Header("基本情報")]
    public string summonName;
    public SummonType summonType;     // Combat, Utility, Decoy

    [Header("ステータス")]
    public CharacterInfo characterInfo;  // 既存の情報クラスScriptableObject
    public AIInfo aiInfo;                // AI行動定義（既存）

    [Header("召喚パラメータ")]
    public float duration;            // 召喚持続時間（秒）。0 = 無制限（手動解除のみ）
    public float spawnOffset;         // 召喚者からの出現距離

    [Header("追従")]
    public float followDistance;      // 追従距離（FollowBehavior用）
    public float maxLeashDistance;    // テレポート距離
}
```

### SummonManager (Pure Logic)
```csharp
public class SummonManager
{
    private SummonSlot[] _slots;  // 固定長2枠

    // 召喚試行（枠チェック→生成→登録）
    public bool TrySummon(SummonDefinition definition, int casterHash, Vector2 position);

    // 毎フレーム寿命更新
    public void Tick(float deltaTime);

    // 指定召喚獣を解除
    public void Dismiss(int summonHash);

    // 全解除
    public void DismissAll();

    // 現在の召喚枠情報
    public ReadOnlySpan<SummonSlot> GetActiveSlots();

    // 枠に空きがあるか
    public bool HasEmptySlot();

    // 召喚獣死亡時の枠解放
    public void OnSummonDeath(int summonHash);
}
```

### SummonedCharacterController (MonoBehaviour)
```csharp
public class SummonedCharacterController : MonoBehaviour, ISummonable
{
    [SerializeField] private FollowBehavior _followBehavior;  // 再利用

    private int _summonerHash;
    private float _remainingDuration;
    private SummonDefinition _definition;

    // ISummonable実装
    public int SummonerId => _summonerHash;
    public float RemainingDuration => _remainingDuration;

    public void Initialize(int summonerHash, float duration, SummonDefinition definition);
    public void Dismiss();
    public event System.Action<ISummonable> OnDismissed;
}
```

### SummonSpawner (Pure Logic)
```csharp
public class SummonSpawner
{
    // プレハブからインスタンス生成（将来的にはAddressable + プール）
    public SummonedCharacterController Spawn(SummonDefinition definition, Vector2 position);

    // インスタンス回収
    public void Despawn(SummonedCharacterController controller);
}
```

## MagicType拡張

既存の `MagicType` enum に `Summon` を追加:
```csharp
public enum MagicType : byte
{
    Attack,    // 攻撃魔法
    Recover,   // 回復魔法
    Support,   // バフ魔法
    Summon     // 召喚魔法 ← 追加
}
```

MagicCasterでMagicType.Summon分岐を追加:
- Projectile生成の代わりにSummonManager.TrySummon()を呼ぶ

## 機能分解

| 機能名 | 説明 | テスト種別 | 優先度 |
|--------|------|-----------|--------|
| SummonSystem_Manager | 召喚枠管理（2枠制限、追加/解除/全解除） | EditMode | High |
| SummonSystem_Lifetime | 寿命Tick、時間切れ自動解除、死亡時枠解放 | EditMode | High |
| SummonSystem_Controller | SummonedCharacterControllerの初期化とAI制御 | EditMode | High |
| SummonSystem_MagicIntegration | MagicCasterからの召喚フロー統合 | EditMode | Medium |
| SummonSystem_PartyLimit | パーティサイズ制限（最古置換ロジック） | EditMode | Medium |

## 設計メモ
- 召喚獣のAIは既存AIBrainをそのまま使用。SummonDefinitionのAIInfoにAIMode配列が定義されている
- 召喚獣はSoAコンテナに通常キャラクターと同じ方法で登録される。ハッシュアクセスでO(1)データ取得
- セクション3の初期構成はプレイヤー1人、常駐仲間1人、一時召喚0〜2体。
- Decoyタイプの召喚獣はDamageScoreTrackerのスコアを高く設定して敵ヘイトを集める
- Utilityタイプは足場や照明として機能（ISummonableのDismiss()で消滅時に足場も消える）
