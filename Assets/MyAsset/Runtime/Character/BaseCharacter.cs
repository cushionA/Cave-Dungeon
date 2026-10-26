using System.Collections.Generic;
using UnityEngine;
using Game.Core;
using CharacterInfo = Game.Core.CharacterInfo;

namespace Game.Runtime
{
    /// <summary>
    /// キャラクター基底MonoBehaviour。
    /// ObjectHash保持、SoAコンテナ登録、接地判定を担う。
    /// MonoBehaviour層は薄くし、ロジックは純ロジック層に委譲する。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(BoxCollider2D))]
    public partial class BaseCharacter : ManagedCharacter
    {
        [SerializeField] protected CharacterInfo _characterInfo;

        protected Rigidbody2D _rb;
        protected BoxCollider2D _collider;
        protected DamageReceiver _damageReceiver;
        protected CharacterAnimationController _animationController;
        private CharacterCollisionController _collisionController;
        private ActionExecutorController _actionController;

        private int _objectHash;
        private bool _isGrounded;
        private GroundInfo _groundInfo;
        private bool _isRegistered;

        public override int ObjectHash => _objectHash;
        public bool IsGrounded => _isGrounded;
        public GroundInfo GroundInfo => _groundInfo;
        // Unity bool 変換で破棄済み DamageReceiver (fake null) を C# native null に変換してから IDamageable に格納する。
        // 呼び出し側で `if (receiver == null)` が interface 経由でも正しく動作する (Issue #73)。
        public override IDamageable Damageable => _damageReceiver != null ? (IDamageable)_damageReceiver : null;
        public DamageReceiver DamageReceiver => _damageReceiver;
        public CharacterAnimationController AnimationController => _animationController;
        public Rigidbody2D Rigidbody => _rb;
        public CharacterCollisionController CollisionController => _collisionController;
        public CharacterInfo CharacterInfoRef => _characterInfo;
        public bool IsAlive
        {
            get
            {
                if (!GameManager.IsCharacterValid(_objectHash))
                {
                    return false;
                }
                ref CharacterVitals vitals = ref GameManager.Data.GetVitals(_objectHash);
                return vitals.currentHp > 0;
            }
        }


#if UNITY_INCLUDE_TESTS
        /// <summary>テスト専用: CharacterInfoを設定する。リフレクション回避用。</summary>
        public void SetCharacterInfoForTest(CharacterInfo info) { _characterInfo = info; }
#endif

        [Header("接地判定")]
        [SerializeField] protected LayerMask _groundLayer = 1 << 6; // Layer 6 = Ground

        protected virtual void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _collider = GetComponent<BoxCollider2D>();
            _damageReceiver = GetComponent<DamageReceiver>();
            _animationController = GetComponent<CharacterAnimationController>();
            _collisionController = GetComponent<CharacterCollisionController>();
            _actionController = GetComponent<ActionExecutorController>();
            _objectHash = gameObject.GetHashCode();

            InitializeCharacterPhysics();
        }

        protected virtual void Start()
        {
            if (_isRegistered)
            {
                return; // OnPoolAcquireで先行登録済み
            }

            if (GameManager.Instance == null)
            {
                Debug.LogError($"[BaseCharacter] GameManager.Instance is null. Cannot register {gameObject.name}");
                return;
            }

            if (_characterInfo == null)
            {
                Debug.LogError($"[BaseCharacter] CharacterInfo is null on {gameObject.name}");
                return;
            }

            GameManager.Instance.RegisterCharacter(this, _characterInfo);
            _isRegistered = true;

            // 名前→ハッシュのマッピングを登録（DialogueSystem等の外部連携用）
            CharacterRegistry.RegisterName(_characterInfo.name, _objectHash);

            if (_damageReceiver != null)
            {
                _damageReceiver.SetArmorRecoveryParams(
                    _characterInfo.maxArmor,
                    _characterInfo.armorRecoveryRate,
                    _characterInfo.armorRecoveryDelay);
            }

            if (_animationController != null)
            {
                _animationController.Initialize(_objectHash);
            }
        }

        protected virtual void OnDestroy()
        {
            GameManager.Actions?.StopActor(_objectHash);
            if (_isRegistered && GameManager.Instance != null)
            {
                GameManager.Instance.UnregisterCharacter(_objectHash);
            }
            _physicsBackend?.Dispose();
        }

        protected MoveParams GetEffectiveMoveParams(float fallbackSpeed = 0f)
        {
            MoveParams move = GameManager.Data.GetMoveParams(_objectHash);
            CharacterStatusEffects effects = GameManager.Data.GetStatusEffects(_objectHash);
            float multiplier = effects.BlocksMovement ? 0f : effects.MoveSpeedMultiplier;
            move.moveSpeed = (move.moveSpeed > 0f ? move.moveSpeed : fallbackSpeed) * multiplier;
            move.dashSpeed *= multiplier;
            return move;
        }

        /// <summary>
        /// プールに返却される前に呼ぶ。SoAコンテナから登録解除する。
        /// SetActive(false)ではOnDestroyが呼ばれないため、明示的に解除が必要。
        /// </summary>
        public virtual void OnPoolReturn()
        {
            GameManager.Actions?.StopActor(_objectHash);
            ResetPhysicsTransientState();
            if (_isRegistered && GameManager.Instance != null)
            {
                GameManager.Instance.UnregisterCharacter(_objectHash);
                _isRegistered = false;
            }
        }

        /// <summary>
        /// プールから取得した後に呼ぶ。SoAコンテナに再登録し、ステータスをリセットする。
        /// SetActive(true)ではStart()が再実行されないため、明示的に登録が必要。
        /// </summary>
        public virtual void OnPoolAcquire()
        {
            if (_isRegistered || _characterInfo == null || GameManager.Instance == null)
            {
                return;
            }

            GameManager.Instance.RegisterCharacter(this, _characterInfo);
            _isRegistered = true;

            CharacterRegistry.RegisterName(_characterInfo.name, _objectHash);

            if (_damageReceiver != null)
            {
                // 前キャラの連続JG窓・ガード経過時間・行動特殊効果が残らないようクリア
                _damageReceiver.ResetInternalState();
                _damageReceiver.SetArmorRecoveryParams(
                    _characterInfo.maxArmor,
                    _characterInfo.armorRecoveryRate,
                    _characterInfo.armorRecoveryDelay);
            }
        }

        /// <summary>
        /// 指定方向への壁接触を判定する。接触していればコライダーのハッシュも返す。
        /// 壁蹴り / 壁張り付き判定の共通モジュール。敵・仲間・プレイヤー全キャラで使用可能。
        /// </summary>
        /// <param name="facingDir">1=右、-1=左</param>
        /// <param name="wallColliderId">接触壁のGetHashCode、未接触時は AdvancedMovementLogic.k_NoWallId</param>
        /// <returns>壁に接触しているか</returns>
        protected bool CheckWallContact(float facingDir, out int wallColliderId)
        {
            wallColliderId = AdvancedMovementLogic.k_NoWallId;
            return _physicsBackend != null && _physicsBackend.QueryWall(facingDir, _groundLayer, out wallColliderId);
        }

        /// <summary>接地情報を物理境界から一度取得する。</summary>
        public void UpdateGroundCheck()
        {
            _groundInfo = _physicsBackend != null ? _physicsBackend.QueryGround(_groundLayer) : GroundInfo.NotGrounded;
            _isGrounded = _groundInfo.isGrounded;
        }

        protected static void BridgeAIActionToExecutor(
            ActionExecutor aiExecutor, ActionExecutorController monoExecutor)
        {
            if (aiExecutor != null && monoExecutor != null) { aiExecutor.SetExecutionBackend(monoExecutor); }
        }

        protected static void BridgeAIActionForJudgmentLoop(
            JudgmentLoop loop, ActionExecutorController actionExecutorController)
        {
            if (actionExecutorController == null || loop == null)
            {
                return;
            }

            BridgeAIActionToExecutor(loop.Executor, actionExecutorController);
        }

        /// <summary>
        /// AI ターゲット候補リストを CharacterRegistry から構築する共通ヘルパー。
        /// EnemyCharacter / CompanionCharacter の毎 FixedUpdate 重複を集約 (Issue #79 HIGH-Reuse-1)。
        /// </summary>
        /// <param name="candidates">出力先リスト。Clear から実行する。</param>
        /// <param name="targetAllyFaction">true = 敵 AI が味方陣営を狙う場合 (Player + Allies)、false = 仲間 AI が敵陣営を狙う場合 (Enemies)。</param>
        protected static void PopulateAITargetCandidates(List<int> candidates, bool targetAllyFaction)
        {
            candidates.Clear();
            if (targetAllyFaction)
            {
                // 敵 AI 視点: プレイヤー + 味方
                // 注: CharacterRegistry.RegisterPlayer は AllyHashes にも playerHash を入れるため、
                // 既存挙動 (PlayerHash 単独 Add → AllyHashes 全 Add) は player を二重に candidates へ入れる場合がある。
                // 動作変更を避けるためここでは旧ロジックをそのまま再現する。
                int playerHash = CharacterRegistry.PlayerHash;
                if (playerHash != 0)
                {
                    candidates.Add(playerHash);
                }
                List<int> allies = CharacterRegistry.AllyHashes;
                if (allies != null)
                {
                    for (int i = 0; i < allies.Count; i++)
                    {
                        candidates.Add(allies[i]);
                    }
                }
            }
            else
            {
                // 仲間 AI 視点: 敵
                List<int> enemies = CharacterRegistry.EnemyHashes;
                if (enemies != null)
                {
                    for (int i = 0; i < enemies.Count; i++)
                    {
                        candidates.Add(enemies[i]);
                    }
                }
            }
        }

        /// <summary>
        /// キャラクターの向きを設定する。localScale.xの符号で方向を表現。
        /// </summary>
        protected bool _isFacingRight = true;

        protected void SetFacing(bool facingRight)
        {
            if (facingRight == _isFacingRight)
            {
                return;
            }
            _isFacingRight = facingRight;
            Vector3 scale = transform.localScale;
            // Issue #80 L4: scale.x が 0 だと Mathf.Abs(0)=0 で向き情報が恒久消失する。
            // プレハブの初期 scale ミスで永続化される事故を防ぐため、0 の場合は 1 をデフォルトとして補う。
            float magnitude = Mathf.Abs(scale.x);
            if (magnitude <= 0f)
            {
                magnitude = 1f;
            }
            scale.x = _isFacingRight ? magnitude : -magnitude;
            transform.localScale = scale;
        }

        /// <summary>
        /// SoAコンテナの位置情報を同期する。
        /// </summary>
        public void PublishPositionForObservation()
        {
            if (GameManager.IsCharacterValid(_objectHash))
            {
                GameManager.Data.GetVitals(_objectHash).position = PhysicsPosition;
            }
        }

        /// <summary>共有固定更新が、全キャラクターの判断より前に入力を確定する。</summary>
        public virtual void PrepareSimulationInput(float currentTime) { }

        /// <summary>共有固定更新が一度だけ呼ぶキャラクター制御。</summary>
        public virtual void SimulateCharacterTick(float deltaTime, float currentTime) { }

        public virtual void PrepareObservationTick(float deltaTime)
        {
            if (_damageReceiver != null && _damageReceiver.isActiveAndEnabled) { _damageReceiver.TickRecovery(deltaTime); }
        }

        /// <summary>前の物理ステップで確定した位置と接地を、全キャラの判断前に公開する。</summary>
        public virtual void CaptureSimulationPhysics()
        {
            _hasSimulationVelocity = false;
            PublishPositionForObservation();
            UpdateGroundCheck();
        }

        /// <summary>全キャラの判断後、同じUnity物理ステップに間に合う位置で適用する。</summary>
        public virtual void ApplySimulationPhysics() => SyncPositionToData();

        protected void SyncPositionToData()
        {
            if (!GameManager.IsCharacterValid(_objectHash))
            {
                return;
            }
            if (GameManager.Actions != null)
            {
                if (_hasSimulationVelocity && !GameManager.Actions.IsMovementBlocked(_objectHash)
                    && !(_actionController != null && _actionController.HasMovementRequest))
                {
                    float speed = Mathf.Abs(PhysicsVelocity.x);
                    GameManager.Actions.SetLocomotion(_objectHash, new Vector2(Mathf.Sign(PhysicsVelocity.x), 0f), speed);
                }
                GameManager.Actions.ApplyMovement(_objectHash, this);
                if (_collisionController != null)
                {
                    _collisionController.SetInvincible(GameManager.Actions.Execution.EvaluateEffects(_objectHash).isInvincible);
                }
            }
            ref CharacterVitals vitals = ref GameManager.Data.GetVitals(_objectHash);
            vitals.position = PhysicsPosition;
        }
    }
}
