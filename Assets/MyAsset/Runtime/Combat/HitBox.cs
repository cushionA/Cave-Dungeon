using UnityEngine;
using Game.Core;

namespace Game.Runtime
{
    /// <summary>
    /// ヒットボックスMonoBehaviour。HitboxLogicを内部で使用し、
    /// ヒット数上限管理・同一ターゲット重複防止を提供する。
    /// Trigger検出→TryRegisterHit→DamageData生成→ReceiveDamage呼び出し。
    /// </summary>
    [RequireComponent(typeof(BoxCollider2D))]
    public class HitBox : MonoBehaviour
    {
        private BoxCollider2D _triggerCollider;
        private HitboxLogic _logic;
        private AttackMotionData _currentMotion;
        private int _ownerHash;
        private bool _isActive;

        public bool IsActive => _isActive;
        public bool IsExhausted => _logic != null && _logic.IsExhausted;
        public int HitCount => _logic != null ? _logic.HitCount : 0;

        private void Awake()
        {
            _triggerCollider = GetComponent<BoxCollider2D>();
            _triggerCollider.isTrigger = true;
            _triggerCollider.enabled = false;
        }

        /// <summary>
        /// ヒットボックスを有効化する。HitboxLogicを初期化してヒット管理開始。
        /// </summary>
        public void Activate(AttackMotionData motion, int ownerHash)
        {
            _currentMotion = motion;
            _ownerHash = ownerHash;
            _isActive = true;
            _logic = new HitboxLogic(motion.maxHitCount);
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

        /// <summary>
        /// HitboxLogicをリセットして新しい攻撃モーション用に再初期化する。
        /// </summary>
        public void ResetForNewAttack(AttackMotionData motion, int ownerHash)
        {
            _currentMotion = motion;
            _ownerHash = ownerHash;
            _isActive = true;

            if (_logic != null)
            {
                _logic.Reset(motion.maxHitCount);
            }
            else
            {
                _logic = new HitboxLogic(motion.maxHitCount);
            }

            _triggerCollider.enabled = true;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!_isActive || _logic == null)
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

            // HitboxLogicでヒット登録（重複防止+上限管理）
            if (!_logic.TryRegisterHit(receiver.ObjectHash))
            {
                return;
            }

            // DamageData生成
            DamageData data = BuildDamageData(receiver.ObjectHash);

            receiver.ReceiveDamage(data);

            // 上限到達で自動無効化
            if (_logic.IsExhausted)
            {
                Deactivate();
            }
        }

        private DamageData BuildDamageData(int defenderHash)
        {
            return new DamageData
            {
                attackerHash = _ownerHash,
                defenderHash = defenderHash,
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
