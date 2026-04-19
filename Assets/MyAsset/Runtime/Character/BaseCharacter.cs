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
    public class BaseCharacter : MonoBehaviour
    {
        private const float k_GroundCheckDistance = 0.1f;
        private const float k_GroundCheckWidthRatio = 0.9f;

        [SerializeField] protected CharacterInfo _characterInfo;

        protected Rigidbody2D _rb;
        protected BoxCollider2D _collider;
        protected DamageReceiver _damageReceiver;

        private int _objectHash;
        private bool _isGrounded;
        private bool _isRegistered;

        public int ObjectHash => _objectHash;
        public bool IsGrounded => _isGrounded;
        /// <summary>
        /// DamageReceiver を IDamageable として外部公開。
        /// SoA登録は <see cref="GameManager.Data"/>.SetManaged 経由で行われるため
        /// 通常のダメージパイプラインでは GameManager.Data.GetManaged(hash) を使うこと。
        /// このプロパティは GetComponent を避けてキャラ内部から直接参照したい場合の補助。
        /// </summary>
        public IDamageable Damageable => _damageReceiver;
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
        [SerializeField] private LayerMask _groundLayer = 1 << 6; // Layer 6 = Ground

        protected virtual void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _collider = GetComponent<BoxCollider2D>();
            _damageReceiver = GetComponent<DamageReceiver>();
            _objectHash = gameObject.GetInstanceID();

            // 物理設定
            _rb.gravityScale = GameConstants.k_GravityScale;
            _rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
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

            GameManager.Instance.RegisterCharacter(_objectHash, _characterInfo);
            _isRegistered = true;

            // 名前→ハッシュのマッピングを登録（DialogueSystem等の外部連携用）
            CharacterRegistry.RegisterName(_characterInfo.name, _objectHash);

            // DamageReceiverをSoAに登録し、アーマー回復パラメータを設定
            // Awake時点でDamageReceiverが未Addだった場合に備え、null ならここで再取得
            if (_damageReceiver == null)
            {
                _damageReceiver = GetComponent<DamageReceiver>();
            }
            if (_damageReceiver != null)
            {
                GameManager.Data.SetManaged(_objectHash, _damageReceiver);
                _damageReceiver.SetArmorRecoveryParams(
                    _characterInfo.maxArmor,
                    _characterInfo.armorRecoveryRate,
                    _characterInfo.armorRecoveryDelay);
            }

            // CharacterAnimationControllerの初期化（ownerHashとの紐付け）
            CharacterAnimationController animController = GetComponent<CharacterAnimationController>();
            if (animController != null)
            {
                animController.Initialize(_objectHash);
            }
        }

        protected virtual void OnDestroy()
        {
            if (_isRegistered && GameManager.Instance != null)
            {
                GameManager.Instance.UnregisterCharacter(_objectHash);
            }
        }

        /// <summary>
        /// プールに返却される前に呼ぶ。SoAコンテナから登録解除する。
        /// SetActive(false)ではOnDestroyが呼ばれないため、明示的に解除が必要。
        /// </summary>
        public virtual void OnPoolReturn()
        {
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

            GameManager.Instance.RegisterCharacter(_objectHash, _characterInfo);
            _isRegistered = true;

            CharacterRegistry.RegisterName(_characterInfo.name, _objectHash);

            if (_damageReceiver == null)
            {
                _damageReceiver = GetComponent<DamageReceiver>();
            }
            if (_damageReceiver != null)
            {
                // 前キャラの連続JG窓・ガード経過時間・行動特殊効果が残らないようクリア
                _damageReceiver.ResetInternalState();
                GameManager.Data.SetManaged(_objectHash, _damageReceiver);
                _damageReceiver.SetArmorRecoveryParams(
                    _characterInfo.maxArmor,
                    _characterInfo.armorRecoveryRate,
                    _characterInfo.armorRecoveryDelay);
            }
        }

        /// <summary>
        /// BoxCast接地判定。FixedUpdateで呼ぶ。
        /// </summary>
        public void UpdateGroundCheck()
        {
            Vector2 origin = (Vector2)transform.position + _collider.offset;
            origin.y -= _collider.size.y * 0.5f;

            Vector2 size = new Vector2(
                _collider.size.x * k_GroundCheckWidthRatio,
                0.05f);

            RaycastHit2D hit = Physics2D.BoxCast(
                origin, size, 0f, Vector2.down, k_GroundCheckDistance, _groundLayer);

            _isGrounded = hit.collider != null;
        }

        /// <summary>
        /// AI内部ExecutorのAttack/Castアクションを
        /// ActionExecutorController（MonoBehaviour側）に橋渡しする共通ヘルパー。
        /// 橋渡し成功後にAI側のアクションをForceCompleteして二重実行を防ぐ。
        /// </summary>
        protected static void BridgeAIActionToExecutor(
            ActionExecutor aiExecutor, ActionExecutorController monoExecutor, int targetHash)
        {
            if (aiExecutor == null || !aiExecutor.IsExecuting)
            {
                return;
            }

            if (monoExecutor.IsExecuting)
            {
                return;
            }

            ActionBase current = aiExecutor.CurrentAction;
            if (current == null)
            {
                return;
            }

            ActionSlot slot;
            if (current is AttackActionHandler attackHandler)
            {
                slot = new ActionSlot
                {
                    execType = ActionExecType.Attack,
                    paramId = attackHandler.LastParamId,
                    paramValue = 1f
                };
            }
            else if (current is CastActionHandler castHandler)
            {
                slot = new ActionSlot
                {
                    execType = ActionExecType.Cast,
                    paramId = castHandler.LastParamId,
                    paramValue = 1f
                };
            }
            else
            {
                return;
            }

            bool result = monoExecutor.ExecuteAction(slot, targetHash);
            if (result)
            {
                current.ForceComplete();
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
            scale.x = _isFacingRight ? Mathf.Abs(scale.x) : -Mathf.Abs(scale.x);
            transform.localScale = scale;
        }

        /// <summary>
        /// SoAコンテナの位置情報を同期する。
        /// </summary>
        protected void SyncPositionToData()
        {
            if (!GameManager.IsCharacterValid(_objectHash))
            {
                return;
            }
            ref CharacterVitals vitals = ref GameManager.Data.GetVitals(_objectHash);
            vitals.position = (Vector2)transform.position;
        }
    }
}
