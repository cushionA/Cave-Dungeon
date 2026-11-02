# System: MagicSystem
Section: 2 — AI・仲間・連携

## 責務
2つのサブシステムから構成:

1. **ProjectileSystem（飛翔体基盤）**: 全ての飛翔体（魔法弾、スキル衝撃波、チャージ攻撃弾、敵の遠距離攻撃）の共通基盤。発射→飛翔→ヒット判定を統一処理する。発射時の `AttackContext` と世代付き `ActorRef` を保持し、射手の消失・ID再利用後も元の攻撃入力で処理する。
2. **MagicAction（魔法アクション）**: 詠唱→発動モーション→ProjectileSystemで弾丸生成、という魔法固有のフローを管理する。プレイヤー・NPC共通。

WeaponSystemのスキル・チャージ攻撃もProjectileSystemを使う。魔法以外の飛翔体発射元はProjectileSystemを直接呼ぶ。

## 依存
- 入力: DataContainer（CharacterVitals: MP消費、CombatStats: 発射時の攻撃入力捕捉）、DamageSystem（ダメージ計算）、InputSystem（魔法入力）
- 出力: 弾丸生成・飛翔・ヒット判定、ダメージ/回復/バフの適用

## アーキテクチャ準拠
- SoAコンテナ経由でキャラクターデータにアクセス（GetComponent排除）
- 弾丸の移動・寿命計算は Core に集約し、固定tickから一括実行する。現実装はメインスレッドの managed pool。Jobs / Burst / Entities への移行は未実装
- 作者入力は `AttackInfo` / `ActionDefinition`。登録時に `ActionCatalog` が値を捕捉し、発射用の値型 `MagicDefinition` を作る

## コンポーネント構成

### ProjectileSystem（飛翔体基盤 — 全飛翔体共通）
| コンポーネント | 責務 | MonoBehaviour? |
|--------------|------|---------------|
| ProjectileManager | 発射、固定tick、Coreと表示の同時回収、Prefab別表示プール | Yes |
| ProjectileController | 1弾のUnity表示・Physics2D接触と移動区間の判定 | Yes |
| Projectile | 個別弾の状態・寿命・射手/対象の世代 | No |
| ProjectileMovement | 移動・遅延・寿命・重力を同じ時間幅で計算 | No |
| ProjectilePool | 世代付き貸出とCore状態のプール | No |
| BulletProfile | 弾丸挙動定義（速度・追尾・寿命・特性） | No (Serializable) |

### MagicAction（魔法固有フロー）
| コンポーネント | 責務 | MonoBehaviour? |
|--------------|------|---------------|
| GameActionRuntime / ActionExecutionSystem | 共通アクションの詠唱→Active発射 | Yes / No |
| ActionCatalog | 作者入力の検証と登録時snapshot | No |
| MagicDefinition | 発射用の値データ（BulletProfile・AttackContext含む） | No (struct) |

### 利用関係
```
AttackInfo / ActionDefinition → ActionCatalog → GameActionRuntime
                                              ↓ Active時に1回
                                      ProjectileManager
                                      ↙               ↘
                            Core Projectile      Unity Controller
```

---

## 現行の管理契約（2026-10-26）

- `GameManager` の固定tickから更新する。tick開始時点の貸出世代を対象にし、その途中で生まれた子弾は次のtickから更新する。
- Core状態とControllerの返却はManagerの同じ経路で行う。返却予約とtick作業は貸出世代を照合し、以前の予約が再利用後の弾に作用しない。Core poolも二重返却・別poolへの返却を受け付けない。
- 遅延を跨ぐtickでは残り時間だけ移動・寿命を進める。RainとGravityの併用で重力を二重適用しない。
- Physics2Dを引き続き衝突境界に使う。移動区間のshape sweepと接触通知を同じ命中経路へ渡す。弾の表示Prefabは登録時に捕捉し、Prefabごとに再利用する。
- `pierceCount` は有限の追加ヒット数（総数は `pierceCount + 1`）。旧 `BulletFeature.Pierce` は寿命まで総数を無視する別の指定として維持する。キャラ別ヒット数は世代単位で数える。
- `aoeRadius > 0` の作者入力は爆発機能へ接続する。範囲対象は被弾通知前に世代付きで捕捉し、通知中の別爆発・全回収から保護する。
- 発射時の攻撃入力と既に受理したアクションの発射保証を維持する。新規発射は消失した射手から受け付けない。発射済み弾とその子弾は元の捕捉済み入力を使う。
- 2026-10-27: `AttackTargetPolicy` と共通 `AttackTargetRules` を近接・弾の接触/sweep・爆発・明示/自動追尾へ接続した。発射元から見た敵対・友好・自分・中立を指定し、発射元の陣営と対象条件は発射時に捕捉、相手の陣営は命中時に確認する。対象外は命中数の消費や爆発・子弾発動より前に除く。詳細と旧直接APIの互換境界は `DamageSystem.md` の「対象条件の共通契約」を参照。

ShotAnchor、appearDelay / moveStartDelayの二分化、複数Profile、未実装の特性は `docs/FUTURE_TASKS.md` の継続項目。これらを実装済みとして扱わない。

## 以下は初期設計案

以下の型定義・Job例・魔法専用フローは初期案を残したもの。現行実装の型・状態取得・更新経路は上記とソースを参照する。とくに `MagicDefinition : ScriptableObject`、命中時の射手ステータス読出し、Jobによる移動、再帰子弾は現行の契約ではない。

## 魔法データ定義（MagicDefinition）

```csharp
/// <summary>
/// 魔法1つの全データ。ScriptableObjectとしてエディタで設定。
/// </summary>
public class MagicDefinition : ScriptableObject
{
    [Header("基本")]
    public string magicName;
    public MagicType magicType;         // Attack, Recover, Support
    public int mpCost;
    public float cooldownDuration;

    [Header("詠唱・発動")]
    public CastType castType;           // None, Short, Normal, Long
    public float castTime;              // 詠唱時間（秒）
    public FireType fireType;           // None, Short, Normal, Swing, Special

    [Header("弾丸")]
    public int bulletCount;             // 一度に生成する弾丸数
    public BulletProfile bulletProfile; // 弾丸の挙動プロファイル

    [Header("攻撃")]
    public float motionValue;           // ダメージ倍率
    public Element attackElement;       // 攻撃属性
    public StatusEffectInfo statusEffect;  // 状態異常

    [Header("回復・支援")]
    public int healAmount;              // 回復量（Recover用）
    public StatModifier buffModifier;   // バフ効果（Support用）
    public float buffDuration;          // バフ持続時間

    [Header("サウンド")]
    public string castSfxAddress;       // 詠唱SE（Addressable）
    public string fireSfxAddress;       // 発動SE
    public string hitSfxAddress;        // ヒットSE
}
```

### Enum定義

```csharp
public enum MagicType : byte
{
    Attack,     // 攻撃魔法
    Recover,    // 回復魔法
    Support,    // バフ・デバフ
}

public enum CastType : byte
{
    None,       // 詠唱なし（即発動）
    Short,      // 短詠唱（0.3秒程度）
    Normal,     // 通常詠唱（1秒程度）
    Long,       // 長詠唱（2秒以上）
}

public enum FireType : byte
{
    None,       // 発動モーションなし
    Short,      // 短発動モーション
    Normal,     // 通常発動モーション
    Swing,      // 振り下ろし（近接魔法）
    Special,    // 専用モーション
}
```

---

## BulletProfile（弾丸挙動プロファイル）

```csharp
/// <summary>
/// 弾丸の移動挙動を定義する。MagicDefinitionに埋め込み。
/// </summary>
[Serializable]
public class BulletProfile
{
    [Header("移動")]
    public BulletMoveType moveType;     // 直進, 追尾, 放物線, 設置, 停止
    public float speed;                 // 初速
    public float acceleration;          // 加速度（正=加速、負=減速）
    public float angle;                 // 発射角度オフセット（度）
    public float spreadAngle;           // 複数弾のばらけ角度
    public float homingStrength;        // 追尾強度（0=直進、1=即追尾）

    [Header("寿命")]
    public float lifeTime;              // 生存時間（秒）
    public int hitLimit;                // 最大ヒット数（-1=無限/貫通）
    public float emitInterval;          // 複数弾の発射間隔（秒）

    [Header("特性")]
    public BulletFeature feature;       // 貫通, 爆発, 設置, 反射等

    [Header("子弾")]
    public ChildBulletTrigger childTrigger;  // 子弾発生タイミング
    public BulletProfile childProfile;       // 子弾のプロファイル（再帰的）
    public int childCount;                   // 子弾の数

    [Header("エフェクト")]
    public string bulletVfxAddress;     // 弾丸本体VFX（Addressable）
    public string hitVfxAddress;        // ヒットVFX
    public float bulletScale;           // 弾丸スケール
}

public enum BulletMoveType : byte
{
    Straight,   // 直進
    Homing,     // ターゲット追尾
    Angle,      // 指定角度直進（ターゲット方向から）
    Rain,       // 放物線（上空から降らす）
    Set,        // 設置（動かない）
    Stop,       // 一定時間後に停止
}

[Flags]
public enum BulletFeature : ushort
{
    None          = 0,
    Pierce        = 1 << 0,   // 貫通（ヒットしても消えない）
    Explode       = 1 << 1,   // 着弾時に爆発（範囲ダメージ）
    Reflect       = 1 << 2,   // 壁で反射
    Gravity       = 1 << 3,   // 重力の影響を受ける
    Platform      = 1 << 4,   // 足場として機能
    Shield        = 1 << 5,   // 飛び道具を反射する盾
    AreaEffect    = 1 << 6,   // 設置型の持続範囲効果
    Knockback     = 1 << 7,   // 強ノックバック
}

public enum ChildBulletTrigger : byte
{
    None,           // 子弾なし
    OnActivate,     // 生成時に即子弾
    OnTimer,        // 一定時間後に子弾
    OnHit,          // ヒット時に子弾
    OnDestroy,      // 消滅時に子弾
}
```

---

## 魔法発動フロー

```
┌─────────────────────────────────────────────────────────────┐
│ 1. 発動要求                                                  │
│    MagicCaster.Cast(MagicDefinition, targetHash)            │
│    - MP残量チェック → 不足なら中断                            │
│    - クールタイムチェック → 未消化なら中断                     │
│    - MP消費                                                  │
│    - クールタイム開始                                         │
└─────────────────────────────────────────────────────────────┘
        ↓
┌─────────────────────────────────────────────────────────────┐
│ 2. 詠唱フェーズ                                              │
│    - CastType に応じた詠唱アニメーション再生                  │
│    - castTime 秒待機（async/コルーチン）                      │
│    - 詠唱中にダメージを受けると中断（怯み時）                 │
│    - 詠唱SE再生                                              │
└─────────────────────────────────────────────────────────────┘
        ↓
┌─────────────────────────────────────────────────────────────┐
│ 3. 発動モーション                                            │
│    - FireType に応じた発動アニメーション再生                  │
│    - 発動SE再生                                              │
│    - アニメーションイベントで弾丸生成タイミングを通知          │
└─────────────────────────────────────────────────────────────┘
        ↓
┌─────────────────────────────────────────────────────────────┐
│ 4. 弾丸生成（ProjectileController.Initialize）                   │
│    - ProjectilePool から弾丸オブジェクトを取得                    │
│    - 弾丸数分のBulletを初期化                                │
│    - Job用NativeArray確保                                    │
│    - 初期角度計算（ターゲット方向 + angle + spreadAngle）     │
│    - emitInterval に応じて順次発射（非同期遅延）             │
└─────────────────────────────────────────────────────────────┘
        ↓
┌─────────────────────────────────────────────────────────────┐
│ 5. 弾丸飛翔（毎FixedUpdate）                                 │
│    ProjectileController.UpdateBullets()                          │
│    - ProjectileMoveJob をスケジュール                            │
│      → moveType別の移動計算（直進/追尾/放物線等）            │
│      → 速度・加速度・回転計算                                │
│    - Job完了後、各BulletのRigidbody2D.velocityに反映        │
└─────────────────────────────────────────────────────────────┘
        ↓
┌─────────────────────────────────────────────────────────────┐
│ 6. ヒット判定（Bullet.OnTriggerEnter2D）                     │
│    - 自分自身除外                                            │
│    - 衝突リスト重複チェック（多段ヒット防止）                 │
│    - MagicType別の処理:                                      │
│      Attack  → DamageSystem.ApplyDamage(DamageData)         │
│      Recover → CharacterVitals.currentHp += healAmount      │
│      Support → StatModifier適用（バフ/デバフ）               │
│    - ヒットVFX・SE再生                                       │
│    - hitLimit-- → 0でBullet消滅（Pierce除く）               │
│    - 子弾トリガーチェック（OnHit）                            │
└─────────────────────────────────────────────────────────────┘
        ↓
┌─────────────────────────────────────────────────────────────┐
│ 7. 弾丸消滅                                                  │
│    - lifeTime経過 or hitLimit到達                            │
│    - 子弾トリガーチェック（OnDestroy）                        │
│    - ProjectilePool に返却                                       │
│    - 全弾丸消滅時: ProjectileController → NativeArray解放       │
└─────────────────────────────────────────────────────────────┘
```

---

## MagicCaster

```csharp
/// <summary>
/// キャラクターに付くMonoBehaviour。魔法の発動を統括する。
/// プレイヤー・NPC・仲間全員が持つ。
/// </summary>
public class MagicCaster : MonoBehaviour
{
    private int _objectHash;
    private CancellationTokenSource _castToken;

    /// <summary>魔法を詠唱→発動する</summary>
    public async UniTask<bool> Cast(MagicDefinition magic, int targetHash)
    {
        ref CharacterVitals vitals = ref GameManager.Data.GetVitals(_objectHash);

        // MP・クールタイムチェック
        if (vitals.currentMp < magic.mpCost) return false;
        if (!CooldownManager.IsReady(magic)) return false;

        // MP消費・クールタイム開始
        vitals.currentMp -= (short)magic.mpCost;
        CooldownManager.Start(magic);

        // 詠唱
        _castToken = new CancellationTokenSource();
        if (magic.castTime > 0f)
        {
            // 詠唱アニメーション開始
            AnimationBridge.PlayCast(magic.castType);
            bool completed = await WaitCast(magic.castTime, _castToken.Token);
            if (!completed) return false; // 詠唱中断（怯み等）
        }

        // 発動モーション
        AnimationBridge.PlayFire(magic.fireType);

        // 弾丸生成（アニメーションイベントから呼ばれることもある）
        SpawnBullets(magic, targetHash);
        return true;
    }

    /// <summary>詠唱中断（怯み時に呼ぶ）</summary>
    public void InterruptCast()
    {
        _castToken?.Cancel();
    }

    private void SpawnBullets(MagicDefinition magic, int targetHash)
    {
        ProjectileController controller = ProjectilePool.GetController();
        controller.Initialize(magic, _objectHash, targetHash);
    }
}
```

---

## ProjectileController

```csharp
/// <summary>
/// 1回の魔法発動で生成される弾丸群を統括する。
/// Job Systemで複数弾丸の移動を並列計算。
/// </summary>
public class ProjectileController : MonoBehaviour
{
    private Bullet[] _bullets;
    private TransformAccessArray _transforms;
    private NativeArray<float3> _velocities;
    private NativeArray<bool> _active;
    private MagicDefinition _magic;
    private int _casterHash;
    private int _targetHash;
    private int _activeBulletCount;

    public void Initialize(MagicDefinition magic, int casterHash, int targetHash)
    {
        _magic = magic;
        _casterHash = casterHash;
        _targetHash = targetHash;

        int count = magic.bulletCount;
        _bullets = new Bullet[count];
        _transforms = new TransformAccessArray(count);
        _velocities = new NativeArray<float3>(count, Allocator.Persistent);
        _active = new NativeArray<bool>(count, Allocator.Persistent);
        _activeBulletCount = count;

        // 初期角度計算
        float baseAngle = CalculateBaseAngle(casterHash, targetHash, magic.bulletProfile);

        // 弾丸生成
        for (int i = 0; i < count; i++)
        {
            float angle = baseAngle + CalculateSpreadAngle(i, count, magic.bulletProfile);
            SpawnBulletAsync(i, angle, magic.bulletProfile).Forget();
        }
    }

    void FixedUpdate()
    {
        if (_activeBulletCount <= 0) return;

        // ターゲット位置更新
        float2 targetPos = float2.zero;
        if (GameManager.Data.TryGetValue(_targetHash, out int _))
        {
            targetPos = GameManager.Data.GetVitals(_targetHash).position;
        }

        // Job実行
        ProjectileMoveJob job = new ProjectileMoveJob
        {
            profile = _magic.bulletProfile.ToNative(),
            targetPosition = targetPos,
            deltaTime = Time.fixedDeltaTime,
            active = _active,
            velocities = _velocities,
        };
        JobHandle handle = job.Schedule(_transforms);
        handle.Complete();

        // 速度反映
        for (int i = 0; i < _bullets.Length; i++)
        {
            if (_active[i])
            {
                _bullets[i].SetVelocity(_velocities[i]);
            }
        }
    }

    public void OnBulletDestroyed(int index)
    {
        _active[index] = false;
        _activeBulletCount--;
        if (_activeBulletCount <= 0)
        {
            Release();
        }
    }

    private void Release()
    {
        if (_transforms.isCreated) _transforms.Dispose();
        if (_velocities.IsCreated) _velocities.Dispose();
        if (_active.IsCreated) _active.Dispose();
        ProjectilePool.ReturnController(this);
    }
}
```

---

## Projectile（個別弾丸）

### 設計方針: ヒット時にコンテナ経由でステータス取得
弾丸は発射時に**キャスターのGameObject（hash）と残りヒット回数のみ記録**する。
命中時にcasterHashからコンテナ経由でCombatStats等の最新ステータスを取得してダメージ/効果を計算する。

**メリット:**
- 弾丸にステータスのコピーを持たせない → SoAの一元管理と一致
- 発射後にバフがかかっても命中時に反映される（ゲームプレイ上の利点）
- 弾丸が持つデータが最小限（hash + remainingHits + magic参照のみ）

**キャスター死亡時:** casterHashがコンテナに存在しない → 弾丸消滅（シンプル）

```csharp
/// <summary>
/// 個別弾丸のMonoBehaviour。衝突検出、ヒット処理、ライフタイム管理。
/// 弾丸自体にはステータスのコピーを持たない。
/// 命中時にcasterHashからコンテナ経由で最新ステータスを取得する。
/// </summary>
public class Projectile : MonoBehaviour
{
    private ProjectileController _controller;
    private MagicDefinition _magic;
    private int _casterHash;           // 発射者のhash（コンテナアクセス用）
    private int _bulletIndex;
    private int _remainingHits;        // 残り有効ヒット回数
    private Rigidbody2D _rb;
    private HashSet<int> _hitTargets;  // 多段ヒット防止

    public void Initialize(ProjectileController controller, MagicDefinition magic,
                           int casterHash, int bulletIndex)
    {
        _controller = controller;
        _magic = magic;
        _casterHash = casterHash;
        _bulletIndex = bulletIndex;
        _remainingHits = magic.bulletProfile.hitLimit;
        _hitTargets = new HashSet<int>();

        // ライフタイム開始
        LifeTimeAsync(magic.bulletProfile.lifeTime).Forget();
    }

    public void SetVelocity(float3 velocity)
    {
        _rb.linearVelocity = new Vector2(velocity.x, velocity.y);
    }

    void FixedUpdate()
    {
        // キャスターが死亡（コンテナから消えた）→ 弾丸消滅
        if (!GameManager.Data.TryGetValue(_casterHash, out int _))
        {
            Despawn();
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        int targetHash = other.gameObject.GetHashCode();

        // 自分自身除外
        if (targetHash == _casterHash) return;

        // 多段ヒット防止
        if (_hitTargets.Contains(targetHash)) return;
        _hitTargets.Add(targetHash);

        // MagicType別処理（ヒット時にコンテナから最新ステータス取得）
        ProcessHit(targetHash);

        // ヒットVFX・SE

        // remainingHits管理
        if (_remainingHits > 0 &&
            !_magic.bulletProfile.feature.HasFlag(BulletFeature.Pierce))
        {
            _remainingHits--;
            if (_remainingHits <= 0)
            {
                CheckChildBullet(ChildBulletTrigger.OnHit);
                Despawn();
            }
        }
    }

    private void ProcessHit(int targetHash)
    {
        if (!GameManager.Data.TryGetValue(targetHash, out int _)) return;
        if (!GameManager.Data.TryGetValue(_casterHash, out int _)) return;

        // キャスターの最新ステータスをコンテナから取得
        ref CombatStats casterStats = ref GameManager.Data.GetCombatStats(_casterHash);
        ref CharacterFlags casterFlags = ref GameManager.Data.GetFlags(_casterHash);

        switch (_magic.magicType)
        {
            case MagicType.Attack:
                // 攻撃力はコンテナの最新値を使う（バフ反映済み）
                DamageData damage = new DamageData
                {
                    attackerHash = _casterHash,
                    defenderHash = targetHash,
                    damage = casterStats.attack,          // 最新の攻撃力
                    motionValue = _magic.motionValue,
                    attackElement = Element.Strike,            // 魔法はデフォルト打撃属性（魔法定義で上書き可）
                    statusEffect = _magic.statusEffect,
                    feature = AttackFeature.None,
                };
                DamageSystem.ApplyDamage(damage);
                break;

            case MagicType.Recover:
                ref CharacterVitals targetVitals = ref GameManager.Data.GetVitals(targetHash);
                // 回復量もキャスターのステータスでスケーリング可能
                targetVitals.currentHp = (short)Math.Min(
                    targetVitals.currentHp + _magic.healAmount,
                    targetVitals.maxHp);
                targetVitals.UpdateRatios();
                break;

            case MagicType.Support:
                // バフ/デバフ適用
                ApplyBuff(targetHash, _magic.buffModifier, _magic.buffDuration);
                break;
        }
    }

    private void Despawn()
    {
        CheckChildBullet(ChildBulletTrigger.OnDestroy);
        _controller.OnBulletDestroyed(_bulletIndex);
        ProjectilePool.Return(this);
    }
}
```

---

## ProjectileMoveJob

```csharp
/// <summary>
/// 弾丸移動のJob System実装。
/// 複数弾丸の速度を並列計算し、メインスレッドでRigidbodyに反映する。
/// </summary>
[BurstCompile]
public struct ProjectileMoveJob : IJobParallelForTransform
{
    [ReadOnly] public NativeBulletProfile profile;
    [ReadOnly] public float2 targetPosition;
    [ReadOnly] public float deltaTime;
    [ReadOnly] public NativeArray<bool> active;

    public NativeArray<float3> velocities;

    public void Execute(int index, TransformAccess transform)
    {
        if (!active[index]) return;

        float2 currentPos = new float2(transform.position.x, transform.position.y);
        float2 velocity;

        switch (profile.moveType)
        {
            case BulletMoveType.Straight:
                velocity = CalculateStraight(transform, profile);
                break;

            case BulletMoveType.Homing:
                velocity = CalculateHoming(currentPos, targetPosition, transform, profile);
                break;

            case BulletMoveType.Rain:
                velocity = CalculateRain(currentPos, targetPosition, profile, deltaTime);
                break;

            case BulletMoveType.Angle:
                velocity = CalculateAngle(currentPos, targetPosition, transform, profile);
                break;

            default:
                velocity = float2.zero;
                break;
        }

        // 加速度適用
        float currentSpeed = math.length(velocity);
        float newSpeed = currentSpeed + profile.acceleration * deltaTime;
        if (newSpeed > 0 && currentSpeed > 0)
        {
            velocity = math.normalize(velocity) * newSpeed;
        }

        velocities[index] = new float3(velocity.x, velocity.y, 0);
    }
}
```

---

## 連携アクションとの統合

CoopAction で使う支援魔法もMagicSystemを通じて発動する:

```
CoopActionManager.TryActivate()
    → CoopActionBase.ExecuteCombo()
    → MagicCaster.Cast(healMagic, targetHash)  // 回復魔法の場合
    or
    → 独自処理（ワープ等、MagicSystem不使用のものもある）
```

連携が魔法を使う場合:
- MP消費はCoopAction側で管理（無料判定あり）
- MagicCaster.CastにはMP消費をスキップするオプションを用意

```csharp
public async UniTask<bool> Cast(MagicDefinition magic, int targetHash,
                                 bool skipMpCost = false)
```

---

## インタフェース
- `MagicCaster.Cast()` → 外部からの魔法発動要求
- `MagicCaster.InterruptCast()` → 怯み時の詠唱中断
- `DamageSystem.ApplyDamage()` → 攻撃魔法のダメージ処理
- `GameManager.Events.OnMagicCast` → UIフィードバック（MP減少表示等）
- `ProjectilePool` → 弾丸オブジェクトのプーリング

## 機能分解

### ProjectileSystem（飛翔体基盤 — 全発射元共通）
| 機能名 | 説明 | テスト種別 | 優先度 |
|--------|------|-----------|--------|
| Projectile_Movement | BulletMoveType別の移動計算（直進/追尾/放物線） | EditMode | High |
| Projectile_HitDetection | 命中判定、casterHash→コンテナ経由でステータス取得、remainingHits管理 | EditMode | High |
| Projectile_Lifecycle | 生成・消滅・キャスター死亡時の自動消滅 | EditMode | High |
| Projectile_MoveJob | Job Systemでの並列移動計算 | EditMode | Medium |
| Projectile_ChildSpawn | 子弾の再帰的生成 | EditMode | Medium |
| Projectile_Pool | 弾丸オブジェクトのプーリング | EditMode | Medium |

### MagicAction（魔法固有フロー）
| 機能名 | 説明 | テスト種別 | 優先度 |
|--------|------|-----------|--------|
| Magic_CastFlow | 詠唱→発動モーション→ProjectileSystem呼び出し | EditMode | High |
| Magic_HitProcessing | MagicType別のヒット処理（攻撃/回復/バフ） | EditMode | High |
| Magic_CastInterruption | 詠唱中断（怯み時） | EditMode | Medium |

## 設計メモ
- **ProjectileSystemは全飛翔体の共通基盤**。魔法弾、スキル衝撃波、チャージ弾、敵遠距離攻撃が全て同じ仕組み
- 参考コードの責務分離を継承しつつリネーム（BulletController→ProjectileController等）
- **弾丸はcasterHashのみ記録**。命中時にコンテナから最新ステータスを取得してダメージ計算
- プレイヤーもNPCも同じMagicCasterを使う（発動トリガーが入力かAI判定かの違いだけ）
- WeaponSystemのスキル・チャージ攻撃はMagicCasterを経由せずProjectileControllerを直接呼ぶ
- BulletProfileの子弾は再帰的定義（子弾のプロファイルが子弾を持てる）
- 連携アクションはskipMpCostオプションでMagicCasterを再利用可能
- Section1のAttackMotionDataは近接攻撃モーション用。飛翔体を伴う攻撃はBulletProfile経由
