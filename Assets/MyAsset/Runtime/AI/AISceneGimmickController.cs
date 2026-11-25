using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace Game.Runtime
{
    /// <summary>シーン上の操作点・効果範囲をAIへ公開し、一度だけ作者の起動イベントを実行する。</summary>
    [DisallowMultipleComponent]
    public sealed class AISceneGimmickController : MonoBehaviour
    {
        private const int GroundMask = 1 << 6;

        [Tooltip("ObjectNearby の認識ビット。初期値は危険物。")]
        [SerializeField] private int objectMask = 1 << 4;
        [SerializeField] private Transform operationAnchor;
        [Min(0f)] [SerializeField] private float operationRadius = 2f;
        [Tooltip("操作点との間で操作を遮るレイヤー。トリガーは無視する。")]
        [SerializeField] private LayerMask operationBlockers = GroundMask;
        [SerializeField] private Transform effectAnchor;
        [Min(0f)] [SerializeField] private float effectRadius = 1f;
        [Tooltip("起動イベントから効果が発生するまでの秒数")]
        [Min(0f)] [SerializeField] private float effectDelay;
        [Tooltip("有効にすると即時起動の代わりに、選んだ敵が板に触れるまで待つ。効果遅延は0にする。")]
        [SerializeField] private bool requireTargetContact;
        [Tooltip("このオブジェクト上の接触板。Collider2Dを有効にし、Is Triggerを設定する。")]
        [SerializeField] private Collider2D contactTrigger;
        [Min(0.02f)] [SerializeField] private float contactTimeout = 3f;
        [Tooltip("接触板専用。誘い役が板を通って退避する同じ高さの地点。空なら通常の接触板として使う。")]
        [SerializeField] private Transform lureExitAnchor;
        [SerializeField] private UnityEvent onActivated = new UnityEvent();

        private struct ContactArm
        {
            public ActorRef owner;
            public ActorRef target;
            public PhysicsScene2D world;
            public int generation;
            public double expiresAt;
            public ulong token;
            public bool luring;
            public Vector2 lureExit;
        }

        internal readonly struct LureReservation
        {
            private readonly AISceneGimmickController _source;
            private readonly ulong _token;
            internal readonly float Duration;

            internal LureReservation(AISceneGimmickController source, ulong token)
            {
                _source = source;
                _token = token;
                Duration = source.contactTimeout;
            }

            internal bool IsAssigned => _token != 0;

            internal bool TryGetExit(out Vector2 exit)
            {
                exit = default;
                if (_source == null || _source._contactArm.token != _token || !_source.TryGetArmedContact(out _, out _)) { return false; }
                exit = _source._contactArm.lureExit;
                return true;
            }

            internal void Release()
            {
                if (_source != null && _source._contactArm.token == _token) { _source._contactArm = default; }
            }
        }

        private static readonly Dictionary<int, AISceneGimmickController> registry =
            new Dictionary<int, AISceneGimmickController>();
        private static int nextId;
        private int id;
        private int generation;
        private bool consumed;
        private ContactArm _contactArm;
        private ulong _nextContactToken;
        private readonly List<int> _contactActors = new();
        private readonly RaycastHit2D[] _lureCast = new RaycastHit2D[1];

        public bool IsAvailable => isActiveAndEnabled && !consumed
            && (!requireTargetContact || (HasUsableContactTrigger && !IsContactArmed));
        public bool IsContactArmed => _contactArm.target.IsValid;
        public UnityEvent OnActivated => onActivated;
        public AISceneGimmick Snapshot => new AISceneGimmick
        {
            id = id, generation = generation, objectMask = objectMask, physicsScene = gameObject.scene.GetPhysicsScene2D(),
            operationPosition = operationAnchor != null ? operationAnchor.position : transform.position,
            operationRadius = operationRadius, requiresClearOperation = operationBlockers.value != 0,
            effectPosition = effectAnchor != null ? effectAnchor.position : transform.position,
            effectRadius = effectRadius, effectDelay = effectDelay,
            requiresLure = HasLureExit,
            lureExit = HasLureExit && lureExitAnchor != null ? (Vector2)lureExitAnchor.position : default,
            lurePlateX = HasLureExit && contactTrigger != null ? contactTrigger.bounds.center.x : 0f,
        };

        public void Configure(int mask, Transform operation, float useRadius, Transform effect, float hitRadius,
            float delay = 0f, int blockers = GroundMask)
        {
            objectMask = mask;
            operationAnchor = operation;
            operationRadius = useRadius;
            operationBlockers = blockers;
            effectAnchor = effect;
            effectRadius = hitRadius;
            effectDelay = delay;
            generation = checked(generation + 1);
            _contactArm = default;
        }

#if UNITY_INCLUDE_TESTS
        public void SetContactTriggerForTest(Collider2D trigger, float timeout = 3f)
        {
            requireTargetContact = true;
            contactTrigger = trigger;
            contactTimeout = timeout;
            generation = checked(generation + 1);
            _contactArm = default;
        }

        internal void SetLureExitForTest(Transform exit)
        {
            lureExitAnchor = exit;
            generation = checked(generation + 1);
            _contactArm = default;
        }
#endif

        private bool HasLureExit => requireTargetContact && !ReferenceEquals(lureExitAnchor, null);

        private bool HasUsableContactTrigger => requireTargetContact && effectDelay == 0f
            && float.IsFinite(contactTimeout) && contactTimeout > 0f && contactTrigger != null
            && contactTrigger.gameObject == gameObject && contactTrigger.isActiveAndEnabled && contactTrigger.isTrigger
            && (!HasLureExit || lureExitAnchor != null && lureExitAnchor.gameObject.activeInHierarchy
                && lureExitAnchor.gameObject.scene == gameObject.scene);

        private void OnEnable()
        {
            if (id == 0) { id = checked(++nextId); }
            generation = checked(generation + 1);
            consumed = false;
            registry.Add(id, this);
        }

        private void OnDisable()
        {
            registry.Remove(id);
            _contactArm = default;
        }

        internal static bool Capture(AIObservationContext observation)
        {
            observation.BeginGimmickObservation();
            bool transformsSynced = false;
            bool needsVelocity = false;
            foreach (AISceneGimmickController source in registry.Values)
            {
                if (source.IsContactArmed) { source.TryGetArmedContact(out _, out _); }
                if (!source.IsAvailable) { continue; }
                if (source.HasLureExit && !transformsSynced)
                {
                    Physics2D.SyncTransforms();
                    transformsSynced = true;
                }
                AISceneGimmick value = source.Snapshot;
                if (!value.IsValid || !value.physicsScene.IsValid()) { continue; }
                observation.AddGimmick(value);
                if (value.requiresClearOperation) { source.CaptureVisibility(observation, value, ref transformsSynced); }
                needsVelocity |= value.effectDelay > 0f;
            }
            return needsVelocity;
        }

        private void CaptureVisibility(AIObservationContext observation, in AISceneGimmick gimmick, ref bool transformsSynced)
        {
            for (int i = 0; i < observation.ActorHashes.Count; i++)
            {
                int hash = observation.ActorHashes[i];
                if (!observation.TryGetActor(hash, out AIActorObservation actor) || actor.physicsScene != gimmick.physicsScene
                    || !gimmick.CanOperateFrom(actor.position))
                {
                    continue;
                }
                // Auto Sync Transforms is off. Flush wall edits once, for the first observer in reach.
                if (!transformsSynced)
                {
                    Physics2D.SyncTransforms();
                    transformsSynced = true;
                }
                if (HasClearOperation(gimmick.physicsScene, actor.position, gimmick.operationPosition))
                {
                    observation.SetGimmickOperationVisible(actor.actor, gimmick);
                }
            }
        }

        private bool HasClearOperation(PhysicsScene2D world, Vector2 start, Vector2 end)
        {
            if (operationBlockers.value == 0) { return true; }
            Vector2 segment = end - start;
            float distance = segment.magnitude;
            if (distance == 0f) { return true; }
            ContactFilter2D filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(operationBlockers);
            return !world.Raycast(start, segment / distance, distance, filter);
        }

        internal static bool TryActivate(in ActionCommand command, PhysicsScene2D world, List<int> liveActors, out LureReservation lure)
        {
            lure = default;
            AIObservationContext observation = GameManager.Observation;
            if (observation == null || observation.Tick != command.observationTick
                || !observation.Contains(command.gimmickOwner) || !observation.Contains(command.gimmickTarget)
                || !GameManager.Actors.IsCurrent(command.gimmickOwner) || !GameManager.Actors.IsCurrent(command.gimmickTarget)
                || !registry.TryGetValue(command.gimmick.id, out AISceneGimmickController source)
                || !source.IsAvailable || source.generation != command.gimmick.generation
                || source.gameObject.scene.GetPhysicsScene2D() != world) { return false; }

            if (source.HasLureExit) { Physics2D.SyncTransforms(); }
            AISceneGimmick current = source.Snapshot;
            if (!current.IsValid || current.objectMask != command.gimmick.objectMask
                || current.requiresLure != command.gimmick.requiresLure
                || !observation.TryGetGimmick(current.id, current.generation, out AISceneGimmick captured)
                || captured.physicsScene != world || captured.requiresLure != current.requiresLure
                || !observation.IsGimmickOperationVisible(command.gimmickOwner, captured)
                || !TryActor(command.gimmickOwner.Hash, world, out BaseCharacter owner)
                || !TryActor(command.gimmickTarget.Hash, world, out BaseCharacter target)) { return false; }

            // An earlier activation callback in this tick may have moved a blocker.
            if (current.requiresClearOperation && !current.requiresLure) { Physics2D.SyncTransforms(); }
            if (!IsHostile(command.gimmickOwner, command.gimmickTarget, out CharacterBelong ownerBelong)
                || !TryGetPosition(command.gimmickOwner.Hash, owner, out Vector2 ownerPosition)
                || !current.CanOperateFrom(ownerPosition)
                || !source.HasClearOperation(world, ownerPosition, current.operationPosition)
                || !TryGetPosition(command.gimmickTarget.Hash, target, out Vector2 targetPosition)
                || !HasSafeEffect(current, command.gimmickOwner, ownerBelong, liveActors))
            {
                return false;
            }

            if (current.requiresLure)
            {
                if (owner == null || target == null
                    || !observation.TryGetTargetOf(command.gimmickTarget, out ActorRef pursued) || pursued != command.gimmickOwner
                    || !IsPursuing(command.gimmickTarget, command.gimmickOwner)
                    || !current.CanLureFrom(ownerPosition, targetPosition)
                    || !source.HasClearLureLane(world, owner, target, current.lureExit)) { return false; }
            }
            else if (!TryGetVelocity(current.effectDelay, target, out Vector2 targetVelocity)
                || !current.WillAffectTarget(targetPosition, targetVelocity)) { return false; }

            if (source.requireTargetContact)
            {
                if (target == null || owner == null) { return false; }
                ulong token = checked(++source._nextContactToken);
                source._contactArm = new ContactArm
                {
                    owner = command.gimmickOwner,
                    target = command.gimmickTarget,
                    world = world,
                    generation = current.generation,
                    expiresAt = Time.fixedTimeAsDouble + source.contactTimeout,
                    token = token,
                    luring = current.requiresLure,
                    lureExit = current.lureExit,
                };
                if (current.requiresLure) { lure = new LureReservation(source, token); }
            }
            else { source.Activate(); }
            return true;
        }

        private bool HasClearLureLane(PhysicsScene2D world, BaseCharacter owner, BaseCharacter target, Vector2 exit)
        {
            if (!owner.TryGetComponent(out BoxCollider2D ownerBody) || !target.TryGetComponent(out BoxCollider2D targetBody)
                || !ownerBody.enabled || !targetBody.enabled) { return false; }
            const float floorProbeSpacing = 0.25f;
            const float floorMargin = 0.08f;
            ContactFilter2D floor = new ContactFilter2D { useTriggers = false };
            floor.SetLayerMask(owner.GroundLayers.value & target.GroundLayers.value);
            Bounds ownerBounds = ownerBody.bounds;
            Bounds targetBounds = targetBody.bounds;
            float floorY = ownerBounds.min.y;
            if (Mathf.Abs(targetBounds.min.y - floorY) > AISceneGimmick.k_LureHeightTolerance) { return false; }

            Vector2 origin = new Vector2(targetBounds.center.x, floorY + floorMargin);
            float dx = exit.x - origin.x;
            int samples = Mathf.CeilToInt(Mathf.Abs(dx) / floorProbeSpacing);
            if (samples < 1 || samples > 50) { return false; }
            for (int i = 0; i <= samples; i++)
            {
                Vector2 probe = origin + Vector2.right * (dx * i / samples);
                RaycastHit2D hit = world.Raycast(probe, Vector2.down, floorMargin * 2f, floor);
                if (hit.collider == null || hit.normal.y < 0.95f) { return false; }
            }

            // 箱の下を少し持ち上げ、床を遮蔽物として数えず両方の身体幅を確認する。
            Vector2 size = Vector2.Max(ownerBounds.size, targetBounds.size);
            Vector2 center = origin + Vector2.up * (size.y * 0.5f);
            ContactFilter2D blockers = new ContactFilter2D { useTriggers = false };
            blockers.SetLayerMask(owner.GroundLayers.value | target.GroundLayers.value | operationBlockers.value);
            bool isClear = world.BoxCast(center, size, 0f, Vector2.right * Mathf.Sign(dx), Mathf.Abs(dx), blockers, _lureCast) == 0;
            _lureCast[0] = default;
            return isClear;
        }

        private void OnTriggerEnter2D(Collider2D other) => TryTargetContact(other);

        private void OnTriggerStay2D(Collider2D other) => TryTargetContact(other);

        private void TryTargetContact(Collider2D other)
        {
            if (other == null || other.isTrigger
                || !TryGetArmedContact(out BaseCharacter target, out CharacterBelong ownerBelong)
                || (other.gameObject != target.gameObject
                    && (other.attachedRigidbody == null || other.attachedRigidbody != target.Rigidbody)))
            {
                return;
            }

            ColliderDistance2D distance = contactTrigger.Distance(other);
            AISceneGimmick current = Snapshot;
            if (!distance.isValid || !(distance.distance <= 0f) || !current.IsValid
                || !TryGetPosition(_contactArm.target.Hash, target, out Vector2 position) || !current.ContainsEffect(position)
                || !HasSafeEffect(current, _contactArm.owner, ownerBelong, _contactActors))
            {
                return;
            }

            Activate();
        }

        private bool TryGetArmedContact(out BaseCharacter target, out CharacterBelong ownerBelong)
        {
            target = null;
            ownerBelong = default;
            if (!IsContactArmed) { return false; }
            if (isActiveAndEnabled && !consumed && HasUsableContactTrigger && _contactArm.generation == generation
                && Time.fixedTimeAsDouble < _contactArm.expiresAt
                && _contactArm.world.IsValid() && gameObject.scene.GetPhysicsScene2D() == _contactArm.world
                && GameManager.Actors is ActorRegistry actors && actors.IsCurrent(_contactArm.owner)
                && actors.IsCurrent(_contactArm.target)
                && TryActor(_contactArm.owner.Hash, _contactArm.world, out BaseCharacter owner) && owner != null
                && TryActor(_contactArm.target.Hash, _contactArm.world, out target) && target != null
                && IsHostile(_contactArm.owner, _contactArm.target, out ownerBelong)
                && (!_contactArm.luring || lureExitAnchor != null && IsPursuing(_contactArm.target, _contactArm.owner)
                    && (Vector2)lureExitAnchor.position == _contactArm.lureExit
                    && Mathf.Abs(owner.PhysicsPosition.y - _contactArm.lureExit.y) <= AISceneGimmick.k_LureHeightTolerance))
            {
                return true;
            }

            _contactArm = default;
            return false;
        }

        private static bool IsPursuing(ActorRef target, ActorRef owner) => GameManager.TargetRelations != null
            && GameManager.TargetRelations.TryGetTarget(target, out ActorRef pursued) && pursued == owner;

        private void Activate()
        {
            // Mark consumed before callbacks. A listener can dispatch again or replace the scene.
            consumed = true;
            _contactArm = default;
            onActivated.Invoke();
        }

        private static bool IsHostile(ActorRef owner, ActorRef target, out CharacterBelong ownerBelong)
        {
            SoACharaDataDic data = GameManager.Data;
            ownerBelong = data.GetFlags(owner.Hash).Belong;
            return AttackTargetRules.CanAffect(owner, ownerBelong, target, data.GetFlags(target.Hash).Belong,
                AttackTargetRelation.Hostile);
        }

        private static bool HasSafeEffect(in AISceneGimmick gimmick, ActorRef owner, CharacterBelong ownerBelong,
            List<int> liveActors)
        {
            SoACharaDataDic data = GameManager.Data;
            liveActors.Clear();
            data.GetAllHashes(liveActors);
            for (int i = 0; i < liveActors.Count; i++)
            {
                int hash = liveActors[i];
                AttackTargetRelation relation = AttackTargetRules.GetRelation(ownerBelong, data.GetFlags(hash).Belong,
                    hash == owner.Hash);
                if (relation != AttackTargetRelation.Self && relation != AttackTargetRelation.Friendly) { continue; }
                if (TryActor(hash, gimmick.physicsScene, out BaseCharacter other)
                    && (!TryGetPosition(hash, other, out Vector2 position)
                        || !TryGetVelocity(gimmick.effectDelay, other, out Vector2 velocity)
                        || gimmick.ThreatensFriendly(position, velocity))) { return false; }
            }
            return true;
        }

        private static bool TryActor(int hash, PhysicsScene2D world, out BaseCharacter character)
        {
            SoACharaDataDic data = GameManager.Data;
            ManagedCharacter managed = data.GetManaged(hash);
            character = managed as BaseCharacter;
            if (managed?.Uses3DPhysics == true) { return false; }
            CharacterFlags flags = data.GetFlags(hash);
            return data.GetVitals(hash).currentHp > 0 && flags.ActState != ActState.Dead && !flags.IsVanished
                && (managed == null ? world == Physics2D.defaultPhysicsScene
                    : managed.gameObject.activeInHierarchy && managed.gameObject.scene.GetPhysicsScene2D() == world);
        }

        private static bool TryGetPosition(int hash, BaseCharacter character, out Vector2 position)
        {
            position = default;
            if (character != null && character.PhysicsBackend == null) { return false; }
            position = character != null ? character.PhysicsPosition : GameManager.Data.GetVitals(hash).position;
            return float.IsFinite(position.x) && float.IsFinite(position.y);
        }

        private static bool TryGetVelocity(float delay, BaseCharacter character, out Vector2 velocity)
        {
            velocity = default;
            if (delay == 0f) { return true; }
            if (character == null || character.PhysicsBackend == null) { return false; }
            velocity = character.PhysicsVelocity;
            return float.IsFinite(velocity.x) && float.IsFinite(velocity.y);
        }
    }
}
