using UnityEngine;
using Game.Core;

namespace Game.Runtime
{
    /// <summary>
    /// 敵キャラクターMonoBehaviour（プロトタイプ版）。
    /// 簡易AI：プレイヤー検知 → 追跡 → 近距離で攻撃。
    /// </summary>
    public class EnemyCharacter : BaseCharacter
    {
        [Header("AI設定")]
        [SerializeField] private float _detectionRange = 10f;
        [SerializeField] private float _attackRange = 1.5f;
        [SerializeField] private float _attackCooldown = 1.5f;

        private DamageDealer _damageDealer;
        private float _attackTimer;
        private float _attackActiveTimer;
        private bool _isFacingRight = false;

        private const float k_AttackDuration = 0.3f;

        protected override void Awake()
        {
            base.Awake();
            _damageDealer = GetComponentInChildren<DamageDealer>();
        }

        protected override void Start()
        {
            base.Start();
            CharacterRegistry.RegisterEnemy(ObjectHash);
        }

        private void FixedUpdate()
        {
            if (!IsAlive || CharacterRegistry.PlayerHash == 0)
            {
                return;
            }

            UpdateGroundCheck();

            if (GameManager.Data == null || !GameManager.Data.TryGetValue(CharacterRegistry.PlayerHash, out int _))
            {
                return;
            }

            ref CharacterVitals playerVitals = ref GameManager.Data.GetVitals(CharacterRegistry.PlayerHash);
            Vector2 playerPos = playerVitals.position;
            Vector2 myPos = (Vector2)transform.position;
            float distance = Vector2.Distance(myPos, playerPos);

            // 攻撃クールダウン
            _attackTimer -= Time.fixedDeltaTime;
            _attackActiveTimer -= Time.fixedDeltaTime;

            if (_attackActiveTimer <= 0f && _damageDealer != null)
            {
                _damageDealer.Deactivate();
            }

            if (distance > _detectionRange)
            {
                // 範囲外 → 待機
                _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
                SyncPositionToData();
                return;
            }

            // 向き
            bool shouldFaceRight = playerPos.x > myPos.x;
            if (shouldFaceRight != _isFacingRight)
            {
                _isFacingRight = shouldFaceRight;
                Vector3 scale = transform.localScale;
                scale.x = _isFacingRight ? Mathf.Abs(scale.x) : -Mathf.Abs(scale.x);
                transform.localScale = scale;
            }

            if (distance <= _attackRange && _attackTimer <= 0f)
            {
                // 攻撃
                Attack();
            }
            else if (distance > _attackRange)
            {
                // 追跡
                ref MoveParams moveParams = ref GameManager.Data.GetMoveParams(ObjectHash);
                float dir = playerPos.x > myPos.x ? 1f : -1f;
                _rb.linearVelocity = new Vector2(dir * moveParams.moveSpeed * 0.6f, _rb.linearVelocity.y);
            }

            SyncPositionToData();
        }

        private void Attack()
        {
            if (_damageDealer == null)
            {
                return;
            }

            AttackMotionData motion = new AttackMotionData
            {
                motionValue = 0.8f,
                attackElement = Element.Strike,
                feature = AttackFeature.Light,
                knockbackForce = new Vector2(_isFacingRight ? 2f : -2f, 0.5f),
                armorBreakValue = 5f
            };

            _damageDealer.Activate(motion, ObjectHash);
            _attackTimer = _attackCooldown;
            _attackActiveTimer = k_AttackDuration;
        }

        protected override void OnDestroy()
        {
            CharacterRegistry.Unregister(ObjectHash);
            base.OnDestroy();
        }
    }
}
