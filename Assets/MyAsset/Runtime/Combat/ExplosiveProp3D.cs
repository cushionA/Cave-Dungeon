using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace Game.Runtime
{
    /// <summary>指定した形状への攻撃で一度だけ起爆し、同じ物理シーンの敵へ火属性ダメージを与える。</summary>
    [DisallowMultipleComponent]
    public sealed class ExplosiveProp3D : MonoBehaviour
    {
        [SerializeField, Tooltip("このオブジェクト上で攻撃を受ける、Triggerではないコライダー。")]
        private Collider hitCollider;
        [SerializeField, Tooltip("未設定時は指定コライダーの中心を使う。")]
        private Transform blastOrigin;
        [SerializeField, Min(0.01f)] private float blastRadius = 2f;
        [SerializeField, Min(1)] private int blastDamage = 30;
        [SerializeField] private UnityEvent onDetonated = new UnityEvent();

        public bool HasDetonated { get; private set; }
        public UnityEvent OnDetonated => onDetonated;
        internal Collider HitCollider => hitCollider;

        private bool HasLiveCollider => isActiveAndEnabled && !HasDetonated && hitCollider != null
            && hitCollider.gameObject == gameObject && hitCollider.enabled && !hitCollider.isTrigger;

        internal static ExplosiveProp3D FindLiveContact(Collider contact) => contact != null
            && contact.TryGetComponent(out ExplosiveProp3D prop) && prop.HitCollider == contact
            && prop.TryGetBlast(out _, out _) ? prop : null;

        internal bool TryDetonate(in AttackContext strike, PhysicsScene world)
        {
            ActorRegistry actors = GameManager.Actors;
            SoACharaDataDic data = GameManager.Data;
            if (!strike.IsCaptured || actors == null || data == null
                || !world.IsValid() || gameObject.scene.GetPhysicsScene() != world
                || !TryGetBlast(out Vector3 center, out float radiusSquared))
            {
                return false;
            }

            HasDetonated = true;
            hitCollider.enabled = false;
            ElementalStatus stats = new ElementalStatus { fire = blastDamage };
            AttackContext blast = AttackContext.Capture(strike.Owner, stats, 1f, strike.Belong, null);
            DamageVictims(blast, world, center, radiusSquared, actors, data);

            if (this != null)
            {
                try { onDetonated?.Invoke(); }
                catch (Exception exception) { Debug.LogException(exception, this); }
            }
            return true;
        }

        private bool TryGetBlast(out Vector3 center, out float radiusSquared)
        {
            center = default;
            radiusSquared = blastRadius * blastRadius;
            if (!HasLiveCollider || blastDamage <= 0 || !(blastRadius > 0f) || !float.IsFinite(radiusSquared)
                || (blastOrigin != null && blastOrigin.gameObject.scene.GetPhysicsScene() != gameObject.scene.GetPhysicsScene()))
            {
                return false;
            }

            center = blastOrigin != null ? blastOrigin.position : hitCollider.bounds.center;
            return float.IsFinite(center.x) && float.IsFinite(center.y) && float.IsFinite(center.z);
        }

        private void DamageVictims(in AttackContext blast, PhysicsScene world, Vector3 center, float radiusSquared,
            ActorRegistry actors, SoACharaDataDic data)
        {
            List<int> hashes = new List<int>();
            List<ActorRef> victims = new List<ActorRef>();
            data.GetAllHashes(hashes);
            foreach (int hash in hashes)
            {
                if (actors.TryGet(hash, out ActorRef target)
                    && FindVictim(target, blast, world, center, radiusSquared, actors, data) != null)
                {
                    victims.Add(target);
                }
            }

            // 被弾通知中に登録し直した個体へ旧世代の爆風を引き継がない。
            foreach (ActorRef target in victims)
            {
                if (!ReferenceEquals(GameManager.Data, data) || !ReferenceEquals(GameManager.Actors, actors)) { break; }
                DamageReceiver receiver = FindVictim(target, blast, world, center, radiusSquared, actors, data);
                if (receiver == null) { continue; }
                DamageData damage = new DamageData { motionValue = 1f, attackElement = Element.Fire };
                blast.ApplyTo(ref damage, target);
                try { receiver.ReceiveExplosionDamage3D(damage, center, world); }
                catch (Exception exception) { Debug.LogException(exception, this); }
            }
        }

        private static DamageReceiver FindVictim(ActorRef target, in AttackContext blast, PhysicsScene world,
            Vector3 center, float radiusSquared, ActorRegistry actors, SoACharaDataDic data)
        {
            if (!CombatDataHelper.CanAffectTarget(blast, target, data, actors)
                || data.GetManaged(target.Hash) is not CharacterActor3D actor || actor == null || !actor.IsAlive
                || actor.Body == null || !actor.Body.enabled || actor.gameObject.scene.GetPhysicsScene() != world
                || !((actor.Body.bounds.center - center).sqrMagnitude <= radiusSquared))
            {
                return null;
            }
            return actor.Damageable as DamageReceiver;
        }
    }
}
