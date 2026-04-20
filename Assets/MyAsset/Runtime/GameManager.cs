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
                criticalMultiplier = GameConstants.k_DefaultCriticalMultiplier
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

            return _core.RegisterCharacter(hash, vitals, combat, flags, move, chara);
        }

        public void UnregisterCharacter(int hash)
        {
            if (_core != null && _core.IsInitialized)
            {
                _core.UnregisterCharacter(hash);
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
