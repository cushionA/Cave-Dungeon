using UnityEngine;
using Game.Core;

namespace Game.Runtime
{
    /// <summary>
    /// テストシーン用の簡易敵AI。
    /// プレイヤーが一定距離内に入ると追従し、近接で攻撃する。
    /// </summary>
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public class SimpleEnemyBrain : MonoBehaviour
    {
        [Header("Detection")]
        [SerializeField] private float _detectionRange = 8f;
        [SerializeField] private float _attackRange = 1.2f;

        [Header("Movement")]
        [SerializeField] private float _moveSpeed = 3f;

        [Header("Attack")]
        [SerializeField] private float _attackCooldown = 2f;
        [SerializeField] private float _attackDuration = 0.3f;

        private EnemyCharacter _enemy;
        private Rigidbody2D _rb;
        private DamageDealer _damageDealer;
        private float _attackCooldownTimer;
        private float _attackTimer;

        private void Awake()
        {
            _enemy = GetComponent<EnemyCharacter>();
            _rb = GetComponent<Rigidbody2D>();
            _damageDealer = GetComponentInChildren<DamageDealer>();
        }

        private void FixedUpdate()
        {
            if (_enemy == null || !_enemy.IsAlive)
            {
                _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
                return;
            }

            int playerHash = CharacterRegistry.PlayerHash;
            if (playerHash == 0 || !GameManager.IsCharacterValid(playerHash))
            {
                return;
            }

            ref CharacterVitals playerVitals = ref GameManager.Data.GetVitals(playerHash);
            Vector2 playerPos = playerVitals.position;
            Vector2 myPos = (Vector2)transform.position;
            float distX = playerPos.x - myPos.x;
            float absDist = Mathf.Abs(distX);

            // 攻撃タイマー
            _attackCooldownTimer -= Time.fixedDeltaTime;
            _attackTimer -= Time.fixedDeltaTime;

            if (_attackTimer <= 0f && _damageDealer != null)
            {
                _damageDealer.Deactivate();
            }

            // 検出範囲外
            if (absDist > _detectionRange)
            {
                _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
                return;
            }

            // 向き設定
            bool facingRight = distX > 0f;
            Vector3 scale = transform.localScale;
            scale.x = facingRight ? Mathf.Abs(scale.x) : -Mathf.Abs(scale.x);
            transform.localScale = scale;

            // 攻撃範囲内
            if (absDist <= _attackRange)
            {
                _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);

                if (_attackCooldownTimer <= 0f && _damageDealer != null)
                {
                    AttackMotionData motion = new AttackMotionData
                    {
                        motionValue = 0.8f,
                        attackElement = Element.Strike,
                        feature = AttackFeature.Light,
                        knockbackForce = new Vector2(facingRight ? 2f : -2f, 0.5f),
                        armorBreakValue = 5f,
                        maxHitCount = 1
                    };

                    _damageDealer.Activate(motion, _enemy.ObjectHash);
                    _attackTimer = _attackDuration;
                    _attackCooldownTimer = _attackCooldown;
                }
                return;
            }

            // 追従
            float dir = distX > 0f ? 1f : -1f;
            _rb.linearVelocity = new Vector2(dir * _moveSpeed, _rb.linearVelocity.y);
        }
    }
#else
    // リリースビルドではコンパイルされない
    public class SimpleEnemyBrain : MonoBehaviour { }
#endif
}
