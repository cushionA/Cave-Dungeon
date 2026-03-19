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
        private GameManagerCore _core;

        public static GameManager Instance { get; private set; }
        public static SoACharaDataDic Data => Instance != null ? Instance._core.Data : null;
        public static GameEvents Events => Instance != null ? Instance._core.Events : null;

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
            _core.Initialize(64);
        }

        /// <summary>
        /// CharacterInfoからSoA構造体を生成してキャラクター登録する。
        /// </summary>
        public int RegisterCharacter(int hash, CharacterInfo info)
        {
            float gravity = Physics2D.gravity.magnitude * 3.0f; // gravityScale=3.0
            float jumpForce = Mathf.Sqrt(2f * info.jumpHeight * gravity);

            CharacterVitals vitals = new CharacterVitals
            {
                currentHp = info.maxHp,
                maxHp = info.maxHp,
                currentMp = info.maxMp,
                maxMp = info.maxMp,
                currentStamina = info.maxStamina,
                maxStamina = info.maxStamina,
                currentArmor = 100f,
                maxArmor = 100f,
                staminaRecoveryRate = info.staminaRecoveryRate,
                staminaRecoveryDelay = info.staminaRecoveryDelay,
                level = 1
            };

            CombatStats combat = new CombatStats
            {
                attack = info.baseAttack,
                defense = info.baseDefense,
                criticalRate = 0.05f,
                criticalMultiplier = 1.5f
            };

            CharacterFlags flags = CharacterFlags.Pack(
                info.belong, info.feature, info.initialActState, AbilityFlag.None);

            MoveParams move = new MoveParams
            {
                moveSpeed = info.moveSpeed,
                jumpForce = jumpForce,
                dashSpeed = info.dashSpeed,
                dashDuration = 0.25f,
                gravityScale = 3.0f,
                weightRatio = 0f
            };

            return _core.RegisterCharacter(hash, vitals, combat, flags, move);
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
