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
        private HashSet<ActorRef> _hitTargets;
        private AttackContext _attackContext;

        private void Awake()
        {
            _triggerCollider = GetComponent<BoxCollider2D>();
            _triggerCollider.isTrigger = true;
            _triggerCollider.enabled = false;
            _hitTargets = new HashSet<ActorRef>();
        }

        /// <summary>
        /// ヒットボックスを有効化する。
        /// </summary>
        public void Activate(in AttackMotionData motion, int ownerHash)
        {
            _currentMotion = motion;
            _ownerHash = ownerHash;
            _isActive = CombatDataHelper.TryCaptureAttack(ownerHash, motion,
                GameManager.Data, GameManager.Actors, out _attackContext);
            _hitTargets.Clear();
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

        private void OnTriggerEnter2D(Collider2D other)
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
            if (!found || !CombatDataHelper.CanAffectTarget(_attackContext, target,
                GameManager.Data, GameManager.Actors)) { return; }
            int targetHash = target.Hash;

            // 非キャラクター(地形等)との接触は _hitTargets 登録前にスキップして汚染を防ぐ
            IDamageable receiver = GameManager.Data.GetManaged(targetHash)?.Damageable;
            if (receiver == null)
            {
                return;
            }

            // 同一攻撃で同じターゲットに多重ヒットしない (キャラ衝突のみ登録)
            if (!receiver.IsAlive || !_hitTargets.Add(target))
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
