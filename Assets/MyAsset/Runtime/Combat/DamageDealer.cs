using UnityEngine;
using Game.Core;
using System.Collections.Generic;

namespace Game.Runtime
{
    /// <summary>
    /// 攻撃ヒットボックスMonoBehaviour。
    /// OnTriggerEnter2Dで接触したDamageReceiverにダメージを適用する。
    /// </summary>
    [RequireComponent(typeof(BoxCollider2D))]
    public class DamageDealer : MonoBehaviour
    {
        private BoxCollider2D _triggerCollider;
        private AttackMotionData _currentMotion;
        private int _ownerHash;
        private bool _isActive;
        private HashSet<int> _hitTargets;

        private void Awake()
        {
            _triggerCollider = GetComponent<BoxCollider2D>();
            _triggerCollider.isTrigger = true;
            _triggerCollider.enabled = false;
            _hitTargets = new HashSet<int>();
        }

        /// <summary>
        /// ヒットボックスを有効化する。
        /// </summary>
        public void Activate(AttackMotionData motion, int ownerHash)
        {
            _currentMotion = motion;
            _ownerHash = ownerHash;
            _isActive = true;
            _hitTargets.Clear();
            _triggerCollider.enabled = true;
        }

        /// <summary>
        /// ヒットボックスを無効化する。
        /// </summary>
        public void Deactivate()
        {
            _isActive = false;
            _triggerCollider.enabled = false;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!_isActive)
            {
                return;
            }

            DamageReceiver receiver = other.GetComponent<DamageReceiver>();
            if (receiver == null)
            {
                return;
            }

            // 自分自身にはダメージを与えない
            if (receiver.ObjectHash == _ownerHash)
            {
                return;
            }

            // 同一攻撃で同じターゲットに多重ヒットしない
            if (!_hitTargets.Add(receiver.ObjectHash))
            {
                return;
            }

            // DamageData生成
            DamageData data = new DamageData
            {
                attackerHash = _ownerHash,
                defenderHash = receiver.ObjectHash,
                damage = GetOwnerAttack(),
                motionValue = _currentMotion.motionValue,
                knockbackForce = _currentMotion.knockbackForce,
                attackElement = _currentMotion.attackElement,
                statusEffectInfo = _currentMotion.statusEffect,
                feature = _currentMotion.feature,
                armorBreakValue = _currentMotion.armorBreakValue,
                justGuardResistance = _currentMotion.justGuardResistance,
                isProjectile = false
            };

            receiver.ReceiveDamage(data);
        }

        private ElementalStatus GetOwnerAttack()
        {
            if (GameManager.Data == null || !GameManager.Data.TryGetValue(_ownerHash, out int _))
            {
                return default;
            }
            return GameManager.Data.GetCombatStats(_ownerHash).attack;
        }
    }
}
