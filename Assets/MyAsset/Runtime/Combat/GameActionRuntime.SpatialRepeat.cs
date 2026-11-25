using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Runtime
{
    public sealed partial class GameActionRuntime
    {
        private sealed class SpatialRepeat
        {
            public readonly AttackContext context;
            public readonly AttackHitTracker hits;
            public readonly PhysicsScene world;
            public readonly List<int> hashes;
            public readonly List<ActorRef> eligible;
            public float sampledPhaseTime;

            public SpatialRepeat(in AttackContext context, PhysicsScene world, HitRepeatSettings settings,
                int targetLimit, bool ground, float phaseTime)
            {
                this.context = context;
                this.world = world;
                hits = new AttackHitTracker(targetLimit, settings);
                if (ground)
                {
                    hashes = new List<int>();
                    eligible = new List<ActorRef>();
                }
                sampledPhaseTime = phaseTime;
            }
        }

        private readonly HashSet<long> _repeatingSpatialActions = new HashSet<long>();
        private readonly List<long> _repeatTickSnapshot = new List<long>();
        private bool _tickingSpatialRepeats;

        private void BeginSpatialRepeat(CharacterActor3D owner, in ActionExecution current, CombatExecution combat,
            in AttackContext context)
        {
            if (!combat.attack.repeatHits.IsEnabled) { return; }

            bool ground = combat.attack3D.shape == AttackShape3D.GroundArea;
            int targetLimit = ground ? int.MaxValue : combat.multiLockRange > 0f ? combat.multiLockTargets.Count : 1;
            if (targetLimit == 0) { return; }

            combat.repeat3D = new SpatialRepeat(context, owner.gameObject.scene.GetPhysicsScene(),
                combat.attack.repeatHits, targetLimit, ground, current.phaseElapsed);
            _repeatingSpatialActions.Add(current.instanceId);
            Physics.SyncTransforms();
        }

        private void TickSpatialRepeats()
        {
            if (_repeatingSpatialActions.Count == 0 || _tickingSpatialRepeats) { return; }

            _tickingSpatialRepeats = true;
            _repeatTickSnapshot.AddRange(_repeatingSpatialActions);
            _repeatTickSnapshot.Sort();
            bool physicsSynced = false;
            try
            {
                foreach (long instanceId in _repeatTickSnapshot)
                {
                    if (!_combat.TryGetValue(instanceId, out CombatExecution combat)
                        || !Execution.TryGetInstance(instanceId, out ActionExecution current)
                        || !_data.TryGetValue(current.ownerHash, out _)
                        || _data.GetManaged(current.ownerHash) is not CharacterActor3D owner
                        || !CanContinueSpatialRepeat(owner, combat)
                        || combat.repeat3D.hits.IsExhausted)
                    {
                        EndSpatialRepeat(instanceId, combat);
                        continue;
                    }

                    bool ground = combat.attack3D.shape == AttackShape3D.GroundArea;
                    if (ground && !combat.groundAttack3D.BelongsTo(combat.repeat3D.world))
                    {
                        EndSpatialRepeat(instanceId, combat);
                        continue;
                    }

                    if (current.phaseElapsed <= combat.repeat3D.sampledPhaseTime) { continue; }
                    combat.repeat3D.sampledPhaseTime = current.phaseElapsed;
                    if (!physicsSynced)
                    {
                        Physics.SyncTransforms();
                        physicsSynced = true;
                    }

                    if (ground)
                    {
                        ResolveGroundAttack3D(owner, combat, combat.repeat3D.context, current.phaseElapsed);
                    }
                    else
                    {
                        ResolveMelee3D(owner, combat, combat.repeat3D.context, current.phaseElapsed);
                    }
                }
            }
            finally
            {
                _repeatTickSnapshot.Clear();
                _tickingSpatialRepeats = false;
            }
        }

        private void EndSpatialRepeat(long instanceId, CombatExecution combat)
        {
            _repeatingSpatialActions.Remove(instanceId);
            if (combat != null) { ClearGroundWarning(combat); }
        }

        private bool CanContinueSpatialRepeat(CharacterActor3D owner, CombatExecution combat)
        {
            long instanceId = combat.attack.actionInstanceId;
            return combat.repeat3D != null && _repeatingSpatialActions.Contains(instanceId) && owner != null && owner.IsAlive
                && owner.Actor == combat.repeat3D.context.Owner
                && owner.gameObject.scene.GetPhysicsScene() == combat.repeat3D.world
                && Execution.TryGetInstance(instanceId, out ActionExecution current)
                && current.phase == AnimationPhase.Active && Execution.CanApplyExecutionEffects(instanceId);
        }

        private static bool CanRegisterSpatialHit(CombatExecution combat, ActorRef target, float phaseTime) =>
            combat.repeat3D == null || combat.repeat3D.hits.CanRegisterHit(target, phaseTime);

        private void ApplySpatialHit(CharacterActor3D owner, CharacterActor3D target, CombatExecution combat,
            in AttackContext context, Vector3 origin, float phaseTime)
        {
            if (combat.repeat3D != null && (!CanContinueSpatialRepeat(owner, combat)
                || !combat.repeat3D.hits.TryRegisterHit(target.Actor, phaseTime)))
            {
                return;
            }
            ApplyHit3D(target, combat.attack, context, origin);
        }
    }
}
