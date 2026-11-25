using UnityEngine;
using Game.Core;

namespace Game.Runtime
{
    /// <summary>
    /// 攻撃ヒットボックスMonoBehaviour。
    /// 接触したDamageReceiverにダメージを適用する。
    /// </summary>
    [RequireComponent(typeof(BoxCollider2D))]
    public class DamageDealer : MonoBehaviour
    {
        private BoxCollider2D _triggerCollider;
        private AttackMotionData _currentMotion;
        private int _ownerHash;
        private bool _isActive;
        private AttackHitTracker _hits;
        private AttackContext _attackContext;

        private void Awake()
        {
            _triggerCollider = GetComponent<BoxCollider2D>();
            _triggerCollider.isTrigger = true;
            _triggerCollider.enabled = false;
            _hits = new AttackHitTracker(int.MaxValue, default);
        }

        private void OnDisable()
        {
            if (_isActive && _currentMotion.repeatHits.IsEnabled) { Deactivate(); }
        }

        /// <summary>
        /// ヒットボックスを有効化する。
        /// </summary>
        public void Activate(in AttackMotionData motion, int ownerHash)
        {
            _currentMotion = motion;
            _ownerHash = ownerHash;
            _attackContext = default;
            _isActive = (!motion.repeatHits.IsEnabled || isActiveAndEnabled)
                && motion.selectedTargets?.Count != 0 && CombatDataHelper.TryCaptureAttack(ownerHash, motion,
                GameManager.Data, GameManager.Actors, out _attackContext);
            _hits.Reset(int.MaxValue, motion.repeatHits);
            _triggerCollider.enabled = _isActive;
        }

        /// <summary>
        /// ヒットボックスを無効化する。
        /// </summary>
        public void Deactivate()
        {
            _isActive = false;
            _attackContext = default;
            _triggerCollider.enabled = false;
        }

        private void OnTriggerEnter2D(Collider2D other) => TryHit(other);

        private void OnTriggerStay2D(Collider2D other)
        {
            if (_currentMotion.repeatHits.IsEnabled) { TryHit(other); }
        }

        private void TryHit(Collider2D other)
        {
            if (!_isActive)
            {
                return;
            }

            // アーキテクチャ準拠: 毎衝突でのGetComponentを避け、
            // GameObject.GetHashCode → GameManager.Data.GetManaged で IDamageable 逆引き。
            if (other == null || GameManager.Actors == null) { return; }
            bool found = GameManager.Actors.TryGet(other.gameObject.GetHashCode(), out ActorRef target);
            if (!found && other.attachedRigidbody != null)
            {
                found = GameManager.Actors.TryGet(other.attachedRigidbody.gameObject.GetHashCode(), out target);
            }
            if (!found || GameManager.Data.Uses3DPhysics(target.Hash) || _currentMotion.selectedTargets?.Contains(target) == false
                || !CombatDataHelper.CanAffectTarget(_attackContext, target,
                GameManager.Data, GameManager.Actors)) { return; }
            int targetHash = target.Hash;

            IDamageable receiver = GameManager.Data.GetManaged(targetHash)?.Damageable;
            if (receiver == null || !receiver.IsAlive || !_hits.TryRegisterHit(target, Time.fixedTimeAsDouble))
            {
                return;
            }

            // DamageData生成
            DamageData data = CombatDataHelper.BuildDamageData(
                _ownerHash, targetHash, _currentMotion, _attackContext.AttackStats);
            _attackContext.ApplyTo(ref data, target);

            receiver.ReceiveDamage(data);
        }
    }
}
