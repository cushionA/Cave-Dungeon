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

            // アーキテクチャ準拠: 毎衝突でのGetComponentを避け、
            // GameObject.GetInstanceID → GameManager.Data.GetManaged で IDamageable 逆引き。
            int targetHash = other.gameObject.GetInstanceID();

            // 自分自身にはダメージを与えない
            if (targetHash == _ownerHash)
            {
                return;
            }

            // 同一攻撃で同じターゲットに多重ヒットしない
            if (!_hitTargets.Add(targetHash))
            {
                return;
            }

            if (GameManager.Data == null)
            {
                return;
            }

            IDamageable receiver = GameManager.Data.GetManaged(targetHash);
            if (receiver == null)
            {
                // 非キャラクター(地形等)との接触はスキップ
                return;
            }

            // DamageData生成
            ElementalStatus attackStats = CombatDataHelper.GetAttackStats(GameManager.Data, _ownerHash);
            DamageData data = CombatDataHelper.BuildDamageData(
                _ownerHash, targetHash, _currentMotion, attackStats);

            receiver.ReceiveDamage(data);
        }
    }
}
