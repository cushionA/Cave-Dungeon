using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Pool;
using UnityEngine.SceneManagement;

namespace Game.Runtime
{
    /// <summary>指定した形状への攻撃で一度だけ起爆し、同じ物理シーンの敵へ火属性ダメージを与える。</summary>
    /// <remarks>配置した爆発物は、エリアシーンを読み直すと未起爆の初期状態に戻る。</remarks>
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

        // 同距離の接触は、シーンとHierarchyの上から順に選ぶ。
        internal static int CompareContactOrder(Collider first, Collider second)
        {
            Scene firstScene = first.gameObject.scene;
            Scene secondScene = second.gameObject.scene;
            bool sameScene = firstScene == secondScene;
            if (!sameScene)
            {
                int sceneOrder = string.CompareOrdinal(firstScene.path, secondScene.path);
                if (sceneOrder == 0) { sceneOrder = string.CompareOrdinal(firstScene.name, secondScene.name); }
                if (sceneOrder != 0) { return sceneOrder; }
            }

            int hierarchyOrder = CompareHierarchy(first.transform, second.transform);
            if (hierarchyOrder != 0 || sameScene) { return hierarchyOrder; }

            // 同じシーンを複数回ロードした場合、同じ階層のオブジェクトはロード順で決める。
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene == firstScene) { return -1; }
                if (scene == secondScene) { return 1; }
            }
            return 0;
        }

        private static int CompareHierarchy(Transform first, Transform second)
        {
            int firstDepth = Depth(first);
            int secondDepth = Depth(second);
            for (int depth = firstDepth; depth > secondDepth; depth--) { first = first.parent; }
            for (int depth = secondDepth; depth > firstDepth; depth--) { second = second.parent; }
            if (first == second) { return firstDepth.CompareTo(secondDepth); }

            while (first.parent != second.parent)
            {
                first = first.parent;
                second = second.parent;
            }
            return first.GetSiblingIndex().CompareTo(second.GetSiblingIndex());
        }

        private static int Depth(Transform node)
        {
            int depth = 0;
            while (node.parent != null)
            {
                depth++;
                node = node.parent;
            }
            return depth;
        }

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
            // 爆風は同じ物理シーンの球範囲に届く。壁の先も範囲内なら対象になる。
            using var victimLease = ListPool<ActorRef>.Get(out List<ActorRef> victims);
            using (ListPool<int>.Get(out List<int> hashes))
            {
                data.GetAllHashes(hashes);
                foreach (int hash in hashes)
                {
                    if (actors.TryGet(hash, out ActorRef target)
                        && FindVictim(target, blast, world, center, radiusSquared, actors, data) != null)
                    {
                        victims.Add(target);
                    }
                }
            }

            // 被弾通知から別の起爆があっても、この起爆の対象リストは処理を終えるまで返さない。
            // 登録し直した個体へ旧世代の爆風を引き継がない。
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
