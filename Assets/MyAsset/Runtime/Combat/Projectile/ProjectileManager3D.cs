using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Runtime
{
    /// <summary>3Dの物理ワールドで、飛翔体の経路・命中・明示ロックを管理する。</summary>
    public sealed partial class ProjectileManager3D : MonoBehaviour, IGameSubManager
    {
        private const int k_InitOrder = 310;

        internal readonly struct ExplosiveContact
        {
            private readonly bool _restricted;
            private readonly Collider _allowed;

            // 既定値は通常の着弾。AIは許可した装置だけを起爆し、未指定なら着弾だけ行う。
            internal ExplosiveContact(Collider allowed)
            {
                _restricted = true;
                _allowed = allowed;
            }

            internal bool Allows(Collider contact) => !_restricted || contact == _allowed;
        }

        private sealed class Shot
        {
            public ulong leaseId;
            public Scene scene;
            public PhysicsScene world;
            public GameObject visual;
            public Vector3 position;
            public Vector3 direction;
            public float elapsed;
            public float delay;
            public bool active;
            public AttackProfile3D shape;
            public BulletProfile profile;
            public AttackMotionData attack;
            public AttackContext context;
            public ActorRef assignedTarget;
            public ActorRef explicitTarget;
            public GroundAttack3D? groundImpact;
            public GameObject warning;
            public ExplosiveContact explosiveContact;

            public float Speed => Mathf.Max(0f, profile.speed + profile.acceleration * elapsed);
        }

        private struct Contact
        {
            public bool found;
            public float distance;
            public ActorRef actor;
            public Collider propCollider;
        }

        private readonly struct ContactTrace
        {
            public readonly PhysicsScene world;
            public readonly Vector3 position;
            public readonly Vector3 direction;
            public readonly AttackProfile3D shape;
            public readonly AttackContext context;
            public readonly ActorRef assignedTarget;

            public ContactTrace(PhysicsScene world, Vector3 position, Vector3 direction, in AttackProfile3D shape,
                in AttackContext context, ActorRef assignedTarget = default)
            {
                this.world = world;
                this.position = position;
                this.direction = direction;
                this.shape = shape;
                this.context = context;
                this.assignedTarget = assignedTarget;
            }
        }

        private readonly List<Shot> _active = new List<Shot>(16);
        private readonly List<Shot> _tick = new List<Shot>(16);
        private readonly ProjectileSpatialIndex<Shot, PhysicsScene> _spatial = new ProjectileSpatialIndex<Shot, PhysicsScene>(SampleFlight);
        private readonly List<Shot> _nearby = new List<Shot>(16);
        private RaycastHit[] _sweepHits = new RaycastHit[16];
        private Collider[] _overlaps = new Collider[16];
        private SoACharaDataDic _data;
        private ulong _nextLeaseId;
        private bool _isTicking;
        private bool _isClearing;

        public int InitOrder => k_InitOrder;
        public int ActiveCount => _active.Count;

        void IGameSubManager.Initialize(SoACharaDataDic data, GameEvents events) => _data = data;

        void IGameSubManager.Dispose()
        {
            _data = null;
            ClearAll();
        }

        private void OnDisable() => ClearAll();

        internal bool Spawn(Scene scene, Vector3 position, Vector3 direction, in AttackProfile3D shape,
            in MagicDefinition magic, in AttackMotionData attack, in AttackContext context, ActorRef explicitTarget,
            ExplosiveContact explosiveContact = default)
        {
            Shot shot = CreateShot(scene, position, direction, shape, magic, attack, context, explicitTarget);
            if (shot == null) { return false; }
            shot.explosiveContact = explosiveContact;
            GameObject warning = null;
            return ActivateShot(shot, magic.projectilePrefab, ref warning);
        }

        private Shot CreateShot(Scene scene, Vector3 position, Vector3 direction, in AttackProfile3D shape,
            in MagicDefinition magic, in AttackMotionData attack, in AttackContext context, ActorRef explicitTarget,
            GroundAttack3D? groundImpact = null)
        {
            BulletProfile profile = magic.bulletProfile;
            if (_data == null || !isActiveAndEnabled || _isClearing || !scene.isLoaded
                || !IsFinite(position) || !IsFinite(direction) || !float.IsFinite(direction.sqrMagnitude)
                || !PositiveFinite(shape.projectileRadius) || !PositiveFinite(profile.speed)
                || !PositiveFinite(profile.lifeTime) || (profile.moveType != BulletMoveType.Straight
                    && profile.moveType != BulletMoveType.Homing)
                || !float.IsFinite(profile.spawnDelay) || !float.IsFinite(profile.acceleration)
                || !float.IsFinite(profile.homingStrength) || !float.IsFinite(profile.homingAcceleration)
                || magic.magicType != MagicType.Attack
                || !context.IsCaptured || GameManager.Actors == null || !GameManager.Actors.IsCurrent(context.Owner))
            {
                return null;
            }

            PhysicsScene world = scene.GetPhysicsScene();
            if (!world.IsValid() || _data.GetManaged(context.Owner.Hash) is not CharacterActor3D owner
                || owner == null || !owner.IsAlive || owner.gameObject.scene.GetPhysicsScene() != world)
            {
                return null;
            }

            Vector3 heading = direction.normalized;
            if (heading == Vector3.zero) { return null; }

            Shot shot = new Shot
            {
                leaseId = ++_nextLeaseId,
                scene = scene,
                world = world,
                position = position,
                direction = heading,
                delay = Mathf.Max(0f, profile.spawnDelay),
                shape = shape,
                profile = profile,
                attack = attack,
                context = context,
                assignedTarget = explicitTarget,
                explicitTarget = explicitTarget,
                groundImpact = groundImpact,
            };
            if (explicitTarget.IsValid && GetTarget(shot, explicitTarget) == null)
            {
                if (!groundImpact.HasValue) { return null; }
                shot.explicitTarget = default;
            }
            return shot;
        }

        private bool ActivateShot(Shot shot, GameObject prefab, ref GameObject warning)
        {
            shot.visual = CreateVisual(shot, prefab);
            shot.warning = warning;
            warning = null;
            shot.active = true;
            _active.Add(shot);
            _spatial.Invalidate();
            try
            {
                if (shot.delay == 0f) { shot.visual.SetActive(true); }
            }
            catch
            {
                Retire(shot);
                throw;
            }
            return shot.active;
        }

        /// <summary>この弾の明示ロックを外し、残りの飛行は現在の方向を保つ。</summary>
        internal bool ReleaseExplicitLock(ulong leaseId)
        {
            foreach (Shot shot in _active)
            {
                if (shot.leaseId != leaseId || !shot.explicitTarget.IsValid) { continue; }
                shot.explicitTarget = default;
                return true;
            }
            return false;
        }

        /// <summary>同じ3D物理空間で、観測者に作用し得る近くの敵対弾を数える。</summary>
        public int CountNearbyHostileProjectiles(CharacterActor3D observer, float radius)
        {
            if (_data == null || observer == null || !PositiveFinite(radius)) { return 0; }
            ActorRef target = observer.Actor;
            if (!MultiLockTargeting3D.TryGetActor(target, out CharacterActor3D current) || current != observer) { return 0; }

            CharacterBelong faction = _data.GetFlags(target.Hash).Belong;
            _spatial.Collect(_active, observer.gameObject.scene.GetPhysicsScene(), observer.Body.bounds.center, radius,
                _nearby, canCache: !_isTicking);
            int count = 0;
            try
            {
                foreach (Shot shot in _nearby)
                {
                    if (!CanSimulate(shot) || shot.delay > 0f
                        || !AttackTargetRules.CanAffect(shot.context.Owner, shot.context.Belong,
                            target, faction, AttackTargetRelation.Hostile)) { continue; }
                    CharacterActor3D eligible = shot.groundImpact.HasValue ? GetEligibleActor(shot, target) : GetTarget(shot, target);
                    if (eligible == observer) { count++; }
                }
            }
            finally { _nearby.Clear(); }
            return count;
        }

        internal void CollectLockWarnings(ActorRef target, List<ProjectileLockWarning> warnings,
            float radius = ProjectileSpatialIndex.DefaultQueryRadius)
        {
            warnings.Clear();
            if (_data == null || !PositiveFinite(radius)
                || !MultiLockTargeting3D.TryGetActor(target, out CharacterActor3D defender)) { return; }

            Vector3 center = defender.Body.bounds.center;
            _spatial.Collect(_active, defender.gameObject.scene.GetPhysicsScene(), center, radius,
                _nearby, canCache: !_isTicking);
            try
            {
                foreach (Shot shot in _nearby)
                {
                    if (shot.explicitTarget != target || !CanSimulate(shot) || shot.delay > 0f || shot.Speed <= 0f) { continue; }
                    if (GetTarget(shot, target) != defender) { continue; }

                    Vector3 fromTarget = shot.position - center;
                    if (Vector3.Dot(fromTarget, shot.direction) < 0f)
                    {
                        warnings.Add(new ProjectileLockWarning(shot.leaseId, fromTarget));
                    }
                }
            }
            finally { _nearby.Clear(); }
        }

        internal void SimulationTick(float deltaTime)
        {
            if (_data == null || !isActiveAndEnabled || _isTicking || _isClearing
                || !PositiveFinite(deltaTime)) { return; }

            _spatial.BeginTick();
            if (_active.Count == 0) { return; }
            _isTicking = true;
            _tick.AddRange(_active);
            try
            {
                Physics.SyncTransforms();
                foreach (Shot shot in _tick)
                {
                    if (!shot.active) { continue; }
                    if (!CanSimulate(shot)) { Retire(shot); continue; }
                    CharacterActor3D target = GetTarget(shot, shot.explicitTarget);
                    if (target == null) { shot.explicitTarget = default; }

                    float delay = Mathf.Min(deltaTime, shot.delay);
                    shot.delay -= delay;
                    if (shot.delay > 0f) { continue; }
                    if (!shot.visual.activeSelf) { shot.visual.SetActive(true); }
                    if (!CanSimulate(shot)) { Retire(shot); continue; }
                    float remaining = shot.profile.lifeTime - shot.elapsed;
                    float duration = Mathf.Min(deltaTime - delay, remaining);
                    if (duration <= 0f) { continue; }
                    Advance(shot, target, duration);
                    shot.elapsed += duration;
                    if (duration >= remaining) { Retire(shot); }
                }
            }
            finally
            {
                _tick.Clear();
                _isTicking = false;
            }
        }

        public void ClearAll()
        {
            if (_isClearing) { return; }
            _isClearing = true;
            _spatial.Invalidate();
            try
            {
                while (_active.Count != 0) { Retire(_active[_active.Count - 1]); }
            }
            finally
            {
                _isClearing = false;
            }
        }

        private void Advance(Shot shot, CharacterActor3D target, float duration)
        {
            if (shot.groundImpact is GroundAttack3D impact)
            {
                AdvanceGroundImpact(shot, impact, duration);
                return;
            }
            if (target != null && shot.profile.moveType == BulletMoveType.Homing)
            {
                Vector3 toTarget = target.Body.bounds.center - shot.position;
                if (toTarget.sqrMagnitude > 0.0001f)
                {
                    float strength = shot.profile.GetEffectiveHomingStrength()
                        + shot.profile.homingAcceleration * shot.elapsed;
                    float angle = Vector3.Angle(shot.direction, toTarget) * Mathf.Deg2Rad;
                    shot.direction = Vector3.RotateTowards(shot.direction, toTarget.normalized,
                        angle * Mathf.Clamp01(strength * duration), 0f);
                }
            }

            Vector3 origin = shot.position;
            float distance = shot.Speed * duration;
            if (!float.IsFinite(distance))
            {
                Retire(shot);
                return;
            }
            Contact contact = FindFirstContact(shot, distance);
            if (contact.found)
            {
                Retire(shot);
                if (contact.propCollider != null)
                {
                    if (shot.explosiveContact.Allows(contact.propCollider))
                    {
                        TryDetonateContact(shot, distance, contact);
                    }
                }
                else
                {
                    CharacterActor3D defender = GetTarget(shot, contact.actor);
                    ApplyDamage(shot, defender, contact.actor, origin);
                }
                return;
            }

            MoveShot(shot, shot.position + shot.direction * distance);
        }

        private void TryDetonateContact(Shot shot, float distance, in Contact contact)
        {
            // ビジュアルを退場させる通知で装置や遮蔽物が動いた場合、古い接触を使わない。
            if (this == null || !isActiveAndEnabled || _data == null || !ReferenceEquals(_data, GameManager.Data)
                || !shot.scene.isLoaded || !shot.world.IsValid()) { return; }
            Physics.SyncTransforms();
            Contact current = FindFirstContact(shot, distance);
            if (current.propCollider != contact.propCollider || !Mathf.Approximately(current.distance, contact.distance)) { return; }
            ExplosiveProp3D prop = ExplosiveProp3D.FindLiveContact(current.propCollider);
            if (prop != null) { prop.TryDetonate(shot.context, shot.world); }
        }

        private static void ApplyDamage(Shot shot, CharacterActor3D defender, ActorRef actor, Vector3 origin)
        {
            if (defender == null || defender.Damageable is not DamageReceiver receiver) { return; }
            DamageData damage = CombatDataHelper.BuildDamageData(shot.context.Owner.Hash, actor.Hash,
                shot.attack, shot.context.AttackStats, isProjectile: true);
            shot.context.ApplyTo(ref damage, actor);
            receiver.ReceiveProjectileDamage3D(damage, origin, shot.world);
        }

        private static ProjectileSpatialSample<PhysicsScene> SampleFlight(Shot shot)
            => shot.active && shot.delay <= 0f ? new ProjectileSpatialSample<PhysicsScene>(shot.world, shot.position) : default;

        private static void MoveShot(Shot shot, Vector3 position)
        {
            shot.position = position;
            if (shot.visual != null)
            {
                shot.visual.transform.SetPositionAndRotation(shot.position, Quaternion.LookRotation(shot.direction));
            }
        }

        private Contact FindFirstContact(Shot shot, float distance)
            => FindFirstContact(new ContactTrace(shot.world, shot.position, shot.direction, shot.shape,
                shot.context, shot.assignedTarget), distance);

        /// <summary>AIが作動させる装置まで、発射と同じ初回接触を確認する。</summary>
        internal bool CanHitExplosiveFirst(CharacterActor3D owner, ExplosiveProp3D prop, Vector3 origin,
            Vector3 direction, float distance, in AttackProfile3D shape, in AttackContext context)
        {
            if (_data == null || !isActiveAndEnabled || _isClearing || owner == null || !owner.IsAlive
                || !context.IsCaptured || owner.Actor != context.Owner || prop == null
                || shape.shape != AttackShape3D.Projectile || !PositiveFinite(shape.projectileRadius)
                || !PositiveFinite(distance) || !IsFinite(origin) || !IsFinite(direction)
                || !PositiveFinite(direction.sqrMagnitude)) { return false; }
            Collider collider = prop.HitCollider;
            Scene scene = owner.gameObject.scene;
            PhysicsScene world = scene.GetPhysicsScene();
            if (!scene.isLoaded || !world.IsValid() || ExplosiveProp3D.FindLiveContact(collider) != prop
                || prop.gameObject.scene.GetPhysicsScene() != world) { return false; }

            Vector3 heading = direction.normalized;
            if (heading == Vector3.zero) { return false; }
            var trace = new ContactTrace(world, origin, heading, shape, context);
            return FindFirstContact(trace, distance).propCollider == collider;
        }

        private Contact FindFirstContact(in ContactTrace trace, float distance)
        {
            Contact contact = default;
            int layers = trace.shape.targetLayers.value | trace.shape.obstructionLayers.value;
            int count = QueryOverlaps(trace.world, trace.position, trace.shape.projectileRadius, layers);
            for (int i = 0; i < count; i++)
            {
                ConsiderContact(trace, _overlaps[i], 0f, ref contact);
                _overlaps[i] = null;
            }
            if (contact.found || distance <= 0f) { return contact; }

            count = QuerySweep(trace.world, trace.position, trace.shape.projectileRadius, trace.direction, distance, layers);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _sweepHits[i];
                ConsiderContact(trace, hit.collider, hit.distance, ref contact);
            }
            return contact;
        }

        private int QueryOverlaps(PhysicsScene world, Vector3 position, float radius, int layers)
        {
            while (true)
            {
                int count = world.OverlapSphere(position, radius, _overlaps, layers, QueryTriggerInteraction.Ignore);
                if (count < _overlaps.Length) { return count; }
                Array.Resize(ref _overlaps, _overlaps.Length * 2);
            }
        }

        private int QuerySweep(PhysicsScene world, Vector3 position, float radius, Vector3 direction,
            float distance, int layers)
        {
            while (true)
            {
                int count = world.SphereCast(position, radius, direction, _sweepHits, distance,
                    layers, QueryTriggerInteraction.Ignore);
                if (count < _sweepHits.Length) { return count; }
                Array.Resize(ref _sweepHits, _sweepHits.Length * 2);
            }
        }

        private void ConsiderContact(in ContactTrace trace, Collider collider, float distance, ref Contact contact)
        {
            if (collider == null || (contact.found && distance > contact.distance)) { return; }

            ActorRef actor = default;
            Collider propCollider = null;
            bool tie = contact.found && distance == contact.distance;
            int layer = 1 << collider.gameObject.layer;
            if ((trace.shape.targetLayers.value & layer) != 0 && ExplosiveProp3D.FindLiveContact(collider) != null)
            {
                if (tie && (contact.propCollider == null
                    || ExplosiveProp3D.CompareContactOrder(collider, contact.propCollider) >= 0)) { return; }
                propCollider = collider;
            }
            else
            {
                CharacterActor3D body = collider.GetComponentInParent<CharacterActor3D>();
                if (body != null)
                {
                    actor = body.Actor;
                    if (GetTarget(trace.world, trace.shape, trace.context, trace.assignedTarget, actor) != body) { return; }
                    // 同距離は遮蔽物、個体、爆発物の順。個体同士はHashで決める。
                    if (tie && contact.propCollider == null
                        && (!contact.actor.IsValid || actor.Hash >= contact.actor.Hash)) { return; }
                }
                else if ((trace.shape.obstructionLayers.value & layer) == 0) { return; }
            }

            contact = new Contact { found = true, distance = distance, actor = actor, propCollider = propCollider };
        }

        private CharacterActor3D GetTarget(Shot shot, ActorRef actor)
            => GetTarget(shot.world, shot.shape, shot.context, shot.assignedTarget, actor);

        private CharacterActor3D GetTarget(PhysicsScene world, in AttackProfile3D shape, in AttackContext context,
            ActorRef assignedTarget, ActorRef actor)
            => actor.Hash == context.Owner.Hash || (actor.Hash == assignedTarget.Hash && actor != assignedTarget)
                ? null : GetEligibleActor(world, shape, context, actor);

        private CharacterActor3D GetEligibleActor(Shot shot, ActorRef actor)
            => GetEligibleActor(shot.world, shot.shape, shot.context, actor);

        private CharacterActor3D GetEligibleActor(PhysicsScene world, in AttackProfile3D shape, in AttackContext context,
            ActorRef actor)
        {
            if (!actor.IsValid
                || !CombatDataHelper.CanAffectTarget(context, actor, _data, GameManager.Actors)
                || _data.GetManaged(actor.Hash) is not CharacterActor3D body || body == null
                || body.Actor != actor || !body.IsAlive || _data.GetFlags(actor.Hash).ActState == ActState.Dead || !body.Body.enabled
                || (shape.targetLayers.value & (1 << body.Body.gameObject.layer)) == 0
                || body.gameObject.scene.GetPhysicsScene() != world)
            {
                return null;
            }
            return body;
        }

        private static bool CanSimulate(Shot shot) =>
            shot.active && shot.visual != null && shot.scene.isLoaded && shot.world.IsValid()
                && (!shot.groundImpact.HasValue || shot.groundImpact.Value.BelongsTo(shot.world));

        private void Retire(Shot shot)
        {
            if (!shot.active) { return; }
            shot.active = false;
            _active.Remove(shot);
            _spatial.Invalidate();
            GameObject warning = shot.warning;
            shot.warning = null;
            AttackWarningPresentation.HideGroundImpact(warning);
            if (shot.visual != null)
            {
                shot.visual.SetActive(false);
                if (shot.visual != null) { Destroy(shot.visual); }
            }
        }

        private static GameObject CreateVisual(Shot shot, GameObject prefab)
        {
            GameObject root = new GameObject("[Projectile 3D]");
            root.SetActive(false);
            SceneManager.MoveGameObjectToScene(root, shot.scene);
            root.transform.SetPositionAndRotation(shot.position, Quaternion.LookRotation(shot.direction));
            if (prefab == null)
            {
                GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.GetComponent<Collider>().enabled = false;
                sphere.transform.SetParent(root.transform, false);
                sphere.transform.localScale = Vector3.one * (2f * shot.shape.projectileRadius);
                return root;
            }

            GameObject visual = Instantiate(prefab, root.transform, false);
            // このPrefabは表示専用。接触は発射時に記録したワールドでのスイープに任せる。
            foreach (Collider collider in visual.GetComponentsInChildren<Collider>(true)) { collider.enabled = false; }
            foreach (Collider2D collider in visual.GetComponentsInChildren<Collider2D>(true)) { collider.enabled = false; }
            foreach (Rigidbody body in visual.GetComponentsInChildren<Rigidbody>(true))
            {
                body.isKinematic = true;
                body.detectCollisions = false;
            }
            foreach (Rigidbody2D body in visual.GetComponentsInChildren<Rigidbody2D>(true)) { body.simulated = false; }
            return root;
        }

        private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        private static bool PositiveFinite(float value) => value > 0f && float.IsFinite(value);
    }
}
