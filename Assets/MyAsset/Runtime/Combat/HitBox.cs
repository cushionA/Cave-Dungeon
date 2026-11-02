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
        private AttackContext _attackContext;
        private uint _activationVersion;
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
        public void Activate(in AttackMotionData motion, int ownerHash)
        {
            ResetForNewAttack(motion, ownerHash);
        }

        /// <summary>
        /// ヒットボックスを無効化する。
        /// </summary>
        public void Deactivate()
        {
            _isActive = false;
            _activationVersion++;
            _attackContext = default;
            _triggerCollider.enabled = false;
        }

        /// <summary>
        /// HitboxLogicをリセットして新しい攻撃モーション用に再初期化する。
        /// </summary>
        public void ResetForNewAttack(in AttackMotionData motion, int ownerHash)
        {
            _currentMotion = motion;
            _ownerHash = ownerHash;
            _activationVersion++;
            _isActive = CombatDataHelper.TryCaptureAttack(ownerHash, motion,
                GameManager.Data, GameManager.Actors, out _attackContext);

            if (_logic != null)
            {
                _logic.Reset(motion.maxHitCount);
            }
            else
            {
                _logic = new HitboxLogic(motion.maxHitCount);
            }

            _triggerCollider.enabled = _isActive;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!_isActive || _logic == null)
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
            if (!found || !CombatDataHelper.CanAffectTarget(_attackContext, target,
                GameManager.Data, GameManager.Actors)) { return; }
            int targetHash = target.Hash;

            IDamageable receiver = GameManager.Data.GetManaged(targetHash)?.Damageable;
            if (receiver == null)
            {
                // 非キャラクター(地形等)との接触はスキップ
                return;
            }

            // 死亡キャラにはダメージを与えない
            if (!receiver.IsAlive)
            {
                return;
            }

            // HitboxLogicでヒット登録（重複防止+上限管理）
            if (!_logic.TryRegisterHit(target))
            {
                return;
            }

            // DamageData生成
            DamageData data = BuildDamageData(target);
            uint activationVersion = _activationVersion;
            receiver.ReceiveDamage(data);

            // 上限到達で自動無効化
            if (this != null && _activationVersion == activationVersion && _logic.IsExhausted)
            {
                Deactivate();
            }
        }

        private DamageData BuildDamageData(ActorRef defender)
        {
            DamageData damage = CombatDataHelper.BuildDamageData(
                _ownerHash, defender.Hash, _currentMotion, _attackContext.AttackStats);
            _attackContext.ApplyTo(ref damage, defender);
            return damage;
        }
    }
}
