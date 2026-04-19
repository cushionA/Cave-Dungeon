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

            // アーキテクチャ準拠: 毎衝突でのGetComponentを避け、
            // GameObject.GetInstanceID → GameManager.Data.GetManaged で IDamageable 逆引き。
            int targetHash = other.gameObject.GetInstanceID();

            // 自分自身にはダメージを与えない
            if (targetHash == _ownerHash)
            {
                return;
            }

            if (GameManager.Data == null)
            {
                return;
            }

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

            // 味方同士はダメージを与えない（陣営チェック）
            if (GameManager.IsCharacterValid(_ownerHash) && GameManager.IsCharacterValid(targetHash))
            {
                CharacterBelong ownerBelong = GameManager.Data.GetFlags(_ownerHash).Belong;
                CharacterBelong targetBelong = GameManager.Data.GetFlags(targetHash).Belong;
                if (ownerBelong == targetBelong)
                {
                    return;
                }
            }

            // HitboxLogicでヒット登録（重複防止+上限管理）
            if (!_logic.TryRegisterHit(targetHash))
            {
                return;
            }

            // DamageData生成
            DamageData data = BuildDamageData(targetHash);

            receiver.ReceiveDamage(data);

            // 上限到達で自動無効化
            if (_logic.IsExhausted)
            {
                Deactivate();
            }
        }

        private DamageData BuildDamageData(int defenderHash)
        {
            ElementalStatus attackStats = CombatDataHelper.GetAttackStats(GameManager.Data, _ownerHash);
            return CombatDataHelper.BuildDamageData(
                _ownerHash, defenderHash, _currentMotion, attackStats);
        }
    }
}
