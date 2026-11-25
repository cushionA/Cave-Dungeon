using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace Game.Runtime
{
    /// <summary>近接攻撃が指定コライダーに触れると、一度だけ周囲の敵へ火属性ダメージを与える。</summary>
    [DisallowMultipleComponent]
    public sealed class ExplosiveProp2D : MonoBehaviour
    {
        [Tooltip("このオブジェクト上で近接攻撃を受けるコライダー。")]
        [SerializeField] private Collider2D hitCollider;
        [Tooltip("未設定時はヒットコライダーの中心を使う。")]
        [SerializeField] private Transform blastOrigin;
        [SerializeField, Min(0.01f)] private float blastRadius = 2f;
        [Tooltip("対象の防御を適用する前の火属性ダメージ。")]
        [SerializeField, Min(1)] private int blastDamage = 30;
        [SerializeField] private UnityEvent onDetonated = new UnityEvent();

        private static readonly Dictionary<Collider2D, ExplosiveProp2D> s_Props = new();
        private Collider2D _registeredCollider;

        public bool HasDetonated { get; private set; }
        public UnityEvent OnDetonated => onDetonated;

        private void OnEnable()
        {
            UnregisterCollider();
            if (HasDetonated || hitCollider == null || hitCollider.gameObject != gameObject) { return; }
            if (s_Props.TryGetValue(hitCollider, out ExplosiveProp2D registered)
                && registered != null && registered.isActiveAndEnabled && !ReferenceEquals(registered, this))
            {
                Debug.LogWarning("[ExplosiveProp2D] コライダーは別の爆発物に登録済みです。", this);
                return;
            }
            s_Props[hitCollider] = this;
            _registeredCollider = hitCollider;
        }

        private void OnDisable() => UnregisterCollider();

        private void UnregisterCollider()
        {
            if (ReferenceEquals(_registeredCollider, null)) { return; }
            if (s_Props.TryGetValue(_registeredCollider, out ExplosiveProp2D registered)
                && ReferenceEquals(registered, this))
            {
                s_Props.Remove(_registeredCollider);
            }
            _registeredCollider = null;
        }

#if UNITY_INCLUDE_TESTS
        public void SetHitColliderForTest(Collider2D collider)
        {
            hitCollider = collider;
        }
#endif

        internal static void TryDetonate(Collider2D attackCollider, Collider2D contact,
            in AttackContext strike, long actionInstanceId)
        {
            if (contact == null || !s_Props.TryGetValue(contact, out ExplosiveProp2D prop)
                || prop == null || !prop.isActiveAndEnabled || prop.HasDetonated || prop.hitCollider != contact
                || !contact.isActiveAndEnabled || attackCollider == null || !attackCollider.isActiveAndEnabled
                || !strike.IsCaptured || actionInstanceId == 0)
            {
                return;
            }

            GameActionRuntime actions = GameManager.Actions;
            ActorRegistry actors = GameManager.Actors;
            SoACharaDataDic data = GameManager.Data;
            if (actions == null || actors == null || data == null || !actors.IsCurrent(strike.Owner)
                || !actions.Execution.TryGetInstance(actionInstanceId, out ActionExecution action)
                || action.ownerRef != strike.Owner || action.phase != AnimationPhase.Active
                || data.GetManaged(strike.Owner.Hash) is not BaseCharacter owner || owner == null || !owner.IsAlive)
            {
                return;
            }

            PhysicsScene2D world = contact.gameObject.scene.GetPhysicsScene2D();
            if (!world.IsValid() || attackCollider.gameObject.scene.GetPhysicsScene2D() != world
                || owner.gameObject.scene.GetPhysicsScene2D() != world)
            {
                return;
            }

            // 同じ剛体の別コライダーが通知を受けても、武器の判定形状が当たったときだけ起動する。
            ColliderDistance2D distance = contact.Distance(attackCollider);
            if (distance.isValid && distance.distance <= 0f) { prop.Detonate(strike, world, actors, data); }
        }

        private void Detonate(in AttackContext strike, PhysicsScene2D world, ActorRegistry actors, SoACharaDataDic data)
        {
            float radiusSquared = blastRadius * blastRadius;
            Vector2 center = blastOrigin != null ? (Vector2)blastOrigin.position : (Vector2)hitCollider.bounds.center;
            if (blastRadius <= 0f || !float.IsFinite(radiusSquared) || blastDamage <= 0
                || !float.IsFinite(center.x) || !float.IsFinite(center.y)) { return; }

            HasDetonated = true;
            hitCollider.enabled = false;
            var stats = new ElementalStatus { fire = blastDamage };
            AttackContext blast = AttackContext.Capture(strike.Owner, stats, 1f, strike.Belong, null);
            var hashes = new List<int>();
            var victims = new List<ActorRef>();
            data.GetAllHashes(hashes);
            foreach (int hash in hashes)
            {
                if (actors.TryGet(hash, out ActorRef target)
                    && TryGetVictim(target, blast, world, center, radiusSquared, actors, data, out _))
                {
                    victims.Add(target);
                }
            }

            // ダメージ通知で個体が入れ替わっても、新しい世代に命中を引き継がない。
            foreach (ActorRef target in victims)
            {
                if (!ReferenceEquals(GameManager.Data, data) || !ReferenceEquals(GameManager.Actors, actors)) { break; }
                if (!TryGetVictim(target, blast, world, center, radiusSquared, actors, data, out IDamageable receiver)) { continue; }
                var damage = new DamageData { motionValue = 1f, attackElement = Element.Fire };
                blast.ApplyTo(ref damage, target);
                try { receiver.ReceiveDamage(damage); }
                catch (Exception exception) { Debug.LogException(exception, this); }
            }

            if (this == null) { return; }
            try { onDetonated?.Invoke(); }
            catch (Exception exception) { Debug.LogException(exception, this); }
        }

        private static bool TryGetVictim(ActorRef target, in AttackContext blast, PhysicsScene2D world,
            Vector2 center, float radiusSquared, ActorRegistry actors, SoACharaDataDic data, out IDamageable receiver)
        {
            receiver = null;
            if (!CombatDataHelper.CanAffectTarget(blast, target, data, actors)
                || data.GetManaged(target.Hash) is not BaseCharacter actor || actor == null || !actor.gameObject.activeInHierarchy
                || actor.PhysicsBackend == null || actor.gameObject.scene.GetPhysicsScene2D() != world
                || !((actor.TargetingCenter - center).sqrMagnitude <= radiusSquared)) { return false; }
            receiver = actor.Damageable;
            return receiver != null && receiver.IsAlive;
        }
    }
}
