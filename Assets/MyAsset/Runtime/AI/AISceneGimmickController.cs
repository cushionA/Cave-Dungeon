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
        [SerializeField] private UnityEvent onActivated = new UnityEvent();

        private static readonly Dictionary<int, AISceneGimmickController> registry =
            new Dictionary<int, AISceneGimmickController>();
        private static int nextId;
        private int id;
        private int generation;
        private bool consumed;

        public bool IsAvailable => isActiveAndEnabled && !consumed;
        public UnityEvent OnActivated => onActivated;
        public AISceneGimmick Snapshot => new AISceneGimmick
        {
            id = id, generation = generation, objectMask = objectMask, physicsScene = gameObject.scene.GetPhysicsScene2D(),
            operationPosition = operationAnchor != null ? operationAnchor.position : transform.position,
            operationRadius = operationRadius, requiresClearOperation = operationBlockers.value != 0,
            effectPosition = effectAnchor != null ? effectAnchor.position : transform.position,
            effectRadius = effectRadius, effectDelay = effectDelay,
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
        }

        private void OnEnable()
        {
            if (id == 0) { id = checked(++nextId); }
            generation = checked(generation + 1);
            consumed = false;
            registry.Add(id, this);
        }

        private void OnDisable() => registry.Remove(id);

        internal static bool Capture(AIObservationContext observation)
        {
            observation.BeginGimmickObservation();
            bool transformsSynced = false;
            bool needsVelocity = false;
            foreach (AISceneGimmickController source in registry.Values)
            {
                if (!source.IsAvailable) { continue; }
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

        internal static bool TryActivate(in ActionCommand command, PhysicsScene2D world, List<int> liveActors)
        {
            AIObservationContext observation = GameManager.Observation;
            if (observation == null || observation.Tick != command.observationTick
                || !observation.Contains(command.gimmickOwner) || !observation.Contains(command.gimmickTarget)
                || !GameManager.Actors.IsCurrent(command.gimmickOwner) || !GameManager.Actors.IsCurrent(command.gimmickTarget)
                || !registry.TryGetValue(command.gimmick.id, out AISceneGimmickController source)
                || !source.IsAvailable || source.generation != command.gimmick.generation
                || source.gameObject.scene.GetPhysicsScene2D() != world) { return false; }

            AISceneGimmick current = source.Snapshot;
            if (!current.IsValid || current.objectMask != command.gimmick.objectMask
                || !observation.TryGetGimmick(current.id, current.generation, out AISceneGimmick captured)
                || captured.physicsScene != world || !observation.IsGimmickOperationVisible(command.gimmickOwner, captured)
                || !TryActor(command.gimmickOwner.Hash, world, out BaseCharacter owner)
                || !TryActor(command.gimmickTarget.Hash, world, out BaseCharacter target)) { return false; }

            // An earlier activation callback in this tick may have moved a blocker.
            if (current.requiresClearOperation) { Physics2D.SyncTransforms(); }
            SoACharaDataDic data = GameManager.Data;
            CharacterBelong ownerBelong = data.GetFlags(command.gimmickOwner.Hash).Belong;
            CharacterBelong targetBelong = data.GetFlags(command.gimmickTarget.Hash).Belong;
            if (!AttackTargetRules.CanAffect(command.gimmickOwner, ownerBelong, command.gimmickTarget, targetBelong,
                    AttackTargetRelation.Hostile)
                || !TryGetPosition(command.gimmickOwner.Hash, owner, out Vector2 ownerPosition)
                || !current.CanOperateFrom(ownerPosition)
                || !source.HasClearOperation(world, ownerPosition, current.operationPosition)
                || !TryGetPosition(command.gimmickTarget.Hash, target, out Vector2 targetPosition)
                || !TryGetVelocity(current.effectDelay, target, out Vector2 targetVelocity)
                || !current.WillAffectTarget(targetPosition, targetVelocity))
            {
                return false;
            }

            liveActors.Clear();
            data.GetAllHashes(liveActors);
            for (int i = 0; i < liveActors.Count; i++)
            {
                int hash = liveActors[i];
                AttackTargetRelation relation = AttackTargetRules.GetRelation(ownerBelong, data.GetFlags(hash).Belong,
                    hash == command.gimmickOwner.Hash);
                if (relation != AttackTargetRelation.Self && relation != AttackTargetRelation.Friendly) { continue; }
                if (TryActor(hash, world, out BaseCharacter other)
                    && (!TryGetPosition(hash, other, out Vector2 position)
                        || !TryGetVelocity(current.effectDelay, other, out Vector2 velocity)
                        || current.ThreatensFriendly(position, velocity))) { return false; }
            }

            // Mark consumed before callbacks. A listener can dispatch again or replace the scene.
            source.consumed = true;
            source.onActivated.Invoke();
            return true;
        }

        private static bool TryActor(int hash, PhysicsScene2D world, out BaseCharacter character)
        {
            SoACharaDataDic data = GameManager.Data;
            ManagedCharacter managed = data.GetManaged(hash);
            character = managed as BaseCharacter;
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
