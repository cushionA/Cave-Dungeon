using UnityEngine;
using Game.Core;
using CharacterInfo = Game.Core.CharacterInfo;

namespace Game.Runtime
{
    /// <summary>
    /// GameManagerCoreのMonoBehaviourラッパー。
    /// Singleton。static Data/Eventsでゲーム全体からアクセスする。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class GameManager : MonoBehaviour
    {
        /// <summary>
        /// SoAコンテナ初期容量。同時登録キャラクター数（プレイヤー+仲間+敵+召喚+混乱+その他）を包含する。
        /// 自動生成コンテナは固定容量で超過時に InvalidOperationException を投げるため、
        /// ボス戦 + 弾幕 + 召喚獣の最大同時存在数を考慮して余裕を持たせる。
        /// </summary>
        private const int k_InitialContainerCapacity = 256;

        /// <summary>SoA 容量使用率警告閾値（開発ビルドのみ）。</summary>
        private const float k_CapacityWarningThreshold = 0.8f;

        /// <summary>警告発報済みフラグ（同一閾値で連続ログを出さない）。</summary>
        private bool _capacityWarningFired;

        private GameManagerCore _core;

        private ProjectileManager _projectileManager;
        private EnemySpawnerManager _enemySpawnerManager;
        private LevelStreamingController _levelStreamingController;

        public static GameManager Instance { get; private set; }
        public static SoACharaDataDic Data => Instance != null ? Instance._core.Data : null;
        public static GameEvents Events => Instance != null ? Instance._core.Events : null;
        public static ProjectileManager Projectiles => Instance != null ? Instance._projectileManager : null;
        public static EnemySpawnerManager EnemySpawner => Instance != null ? Instance._enemySpawnerManager : null;
        public static LevelStreamingController LevelStreaming => Instance != null ? Instance._levelStreamingController : null;

        /// <summary>
        /// SoAコンテナにキャラクターが存在するか検証する共通ヘルパー。
        /// "Data == null || !Data.TryGetValue" パターンの一元化。
        /// </summary>
        public static bool IsCharacterValid(int hash)
        {
            return Data != null && Data.TryGetValue(hash, out int _);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            _core = new GameManagerCore();
            _core.Initialize(k_InitialContainerCapacity);

            _projectileManager = GetComponentInChildren<ProjectileManager>();
            if (_projectileManager != null)
            {
                _projectileManager.Initialize();
            }

            _enemySpawnerManager = GetComponentInChildren<EnemySpawnerManager>();
            if (_enemySpawnerManager != null)
            {
                _enemySpawnerManager.Initialize();
            }

            _levelStreamingController = GetComponentInChildren<LevelStreamingController>();
            if (_levelStreamingController != null)
            {
                _levelStreamingController.Initialize();
            }
        }

        /// <summary>
        /// CharacterInfoからSoA構造体を生成してキャラクター登録する。
        /// BaseCharacter 参照を持たない呼び出し元（テスト等）向けのオーバーロード。
        /// このオーバーロードで登録したキャラは ManagedCharacter を持たないため、
        /// GetManaged(hash) が null を返す。飛翔体/ヒット判定で .Damageable が nullとなり
        /// ダメージ処理がスキップされる点に注意。ゲームコードでは必ず
        /// <see cref="RegisterCharacter(BaseCharacter, CharacterInfo)"/> を使用すること。
        /// </summary>
        [System.Obsolete("テスト専用。ゲームコードは RegisterCharacter(BaseCharacter, CharacterInfo) を使用してください。")]
        public int RegisterCharacter(int hash, CharacterInfo info)
        {
            return RegisterCharacterInternal(hash, info, null);
        }

        /// <summary>
        /// CharacterInfoからSoA構造体を生成してキャラクター登録する。
        /// </summary>
        public int RegisterCharacter(BaseCharacter chara, CharacterInfo info)
        {
            return RegisterCharacterInternal(chara.ObjectHash, info, chara);
        }

        private int RegisterCharacterInternal(int hash, CharacterInfo info, BaseCharacter chara)
        {
            float gravity = Physics2D.gravity.magnitude * GameConstants.k_GravityScale;
            float jumpForce = Mathf.Sqrt(2f * info.jumpHeight * gravity);

            CharacterVitals vitals = new CharacterVitals
            {
                currentHp = info.maxHp,
                maxHp = info.maxHp,
                currentMp = info.maxMp,
                maxMp = info.maxMp,
                currentStamina = info.maxStamina,
                maxStamina = info.maxStamina,
                currentArmor = info.maxArmor,
                maxArmor = info.maxArmor,
                staminaRecoveryRate = info.staminaRecoveryRate,
                staminaRecoveryDelay = info.staminaRecoveryDelay,
                level = 1
            };

            CombatStats combat = new CombatStats
            {
                attack = info.baseAttack,
                defense = info.baseDefense,
                criticalRate = GameConstants.k_DefaultCriticalRate,
                criticalMultiplier = GameConstants.k_DefaultCriticalMultiplier,
                knockbackResistance = info.knockbackResistance
            };

            CharacterFlags flags = CharacterFlags.Pack(
                info.belong, info.feature, info.initialActState, AbilityFlag.None);

            MoveParams move = new MoveParams
            {
                moveSpeed = info.moveSpeed,
                jumpForce = jumpForce,
                dashSpeed = info.dashSpeed,
                dashDuration = GameConstants.k_DefaultDashDuration,
                gravityScale = GameConstants.k_GravityScale,
                weightRatio = 0f,
                jumpStaminaCost = info.jumpStaminaCost,
                dodgeStaminaCost = info.dodgeStaminaCost,
                sprintStaminaPerSecond = info.sprintStaminaPerSecond
            };

            int result = _core.RegisterCharacter(hash, vitals, combat, flags, move, chara);
            WarnIfCapacityUsageHigh();
            return result;
        }

        public void UnregisterCharacter(int hash)
        {
            if (_core != null && _core.IsInitialized)
            {
                _core.UnregisterCharacter(hash);
                // 下回ったら再警告できるようフラグ復帰
                if (_capacityWarningFired && _core.Data != null
                    && (float)_core.Data.Count / k_InitialContainerCapacity < k_CapacityWarningThreshold)
                {
                    _capacityWarningFired = false;
                }
            }
        }

        /// <summary>
        /// SoA コンテナの使用率が閾値を超えていたら開発ビルドで警告を出す。
        /// 自動生成コンテナは固定容量で、超過すると InvalidOperationException を投げるため、
        /// 超過前にチューニング機会を与える目的。
        /// </summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private void WarnIfCapacityUsageHigh()
        {
            if (_capacityWarningFired || _core == null || _core.Data == null)
            {
                return;
            }

            int count = _core.Data.Count;
            float ratio = (float)count / k_InitialContainerCapacity;
            if (ratio >= k_CapacityWarningThreshold)
            {
                _capacityWarningFired = true;
                Debug.LogWarning(
                    $"[SoACharaDataDic] 容量逼迫: {count}/{k_InitialContainerCapacity} ({ratio * 100f:F0}%). 超過すると InvalidOperationException が発生します。k_InitialContainerCapacity の引き上げを検討してください。");
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                _core?.Dispose();
                Instance = null;
            }
        }
    }
}
