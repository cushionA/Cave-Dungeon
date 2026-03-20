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

        private int _objectHash;
        private bool _isGrounded;
        private bool _isRegistered;

        public int ObjectHash => _objectHash;
        public bool IsGrounded => _isGrounded;
        public bool IsAlive
        {
            get
            {
                if (GameManager.Data == null || !GameManager.Data.TryGetValue(_objectHash, out int _))
                {
                    return false;
                }
                return GameManager.Data.GetVitals(_objectHash).currentHp > 0;
            }
        }

        [Header("接地判定")]
        [SerializeField] private LayerMask _groundLayer = 1 << 6; // Layer 6 = Ground

        protected virtual void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _collider = GetComponent<BoxCollider2D>();
            _objectHash = gameObject.GetInstanceID();

            // 物理設定
            _rb.gravityScale = 3.0f;
            _rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        protected virtual void Start()
        {
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

            // アーマー回復パラメータをDamageReceiverに設定
            DamageReceiver receiver = GetComponent<DamageReceiver>();
            if (receiver != null)
            {
                receiver.SetArmorRecoveryParams(
                    _characterInfo.maxArmor,
                    _characterInfo.armorRecoveryRate,
                    _characterInfo.armorRecoveryDelay);
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
        /// SoAコンテナの位置情報を同期する。
        /// </summary>
        protected void SyncPositionToData()
        {
            if (GameManager.Data == null || !GameManager.Data.TryGetValue(_objectHash, out int _))
            {
                return;
            }
            ref CharacterVitals vitals = ref GameManager.Data.GetVitals(_objectHash);
            vitals.position = (Vector2)transform.position;
        }
    }
}
