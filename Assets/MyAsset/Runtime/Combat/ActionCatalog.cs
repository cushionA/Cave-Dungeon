using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.Runtime
{
    internal sealed class ActionCatalog
    {
        internal sealed class Entry
        {
            public ActionDefinition asset;
            public MotionDefinition motionAsset;
            public MotionInfo motion;
            public AttackMotionData attack;
            public MagicDefinition magic;
            public bool projectile;
        }

        private readonly ActionExecutionSystem _execution;
        private readonly Dictionary<ActionDefinition, int> _actions = new Dictionary<ActionDefinition, int>();
        private readonly Dictionary<MotionDefinition, int> _motions = new Dictionary<MotionDefinition, int>();
        private readonly Dictionary<int, Entry> _entries = new Dictionary<int, Entry>();

        public ActionCatalog(ActionExecutionSystem execution) { _execution = execution; }

        public bool TryGet(int id, out Entry entry) => _entries.TryGetValue(id, out entry);

        public int Register(ActionDefinition asset)
        {
            if (asset == null) { throw new ArgumentNullException(nameof(asset)); }
            List<ActionDefinition> added = new List<ActionDefinition>();
            try
            {
                return RegisterGraph(asset, added);
            }
            catch
            {
                for (int i = 0; i < added.Count; i++)
                {
                    int id = _actions[added[i]];
                    _execution.UnregisterDefinition(id);
                    _entries.Remove(id);
                    _actions.Remove(added[i]);
                }
                throw;
            }
        }

        private int RegisterGraph(ActionDefinition asset, List<ActionDefinition> added)
        {
            if (asset == null) { throw new ArgumentNullException(nameof(asset)); }
            if (_actions.TryGetValue(asset, out int existing)) { return existing; }
            MotionInfo motion = asset.GetMotion();
            ActionDefinitionData data = new ActionDefinitionData
            {
                resources = asset.resources, priority = asset.priority,
                staminaCost = asset.staminaCost, mpCost = asset.mpCost, staminaPerSecond = asset.staminaPerSecond,
                anticipationDuration = motion.preMotionDuration, activeDuration = motion.activeMotionDuration,
                recoveryDuration = motion.recoveryDuration, cancelAfter = asset.cancelAfter,
                cancelPoint = asset.cancelPoint, endMode = asset.endMode, actState = asset.actState,
                durationMode = asset.durationMode, duration = asset.duration,
                gameplay = asset.gameplay, effects = asset.actionEffects,
                moveSpeed = asset.movement.speed, moveStartTime = asset.movement.startTime,
                moveDuration = asset.movement.duration, movementUsesInput = asset.movement.usesInput,
            };
            Entry entry = new Entry { asset = asset, motionAsset = asset.motion, motion = motion };
            if (asset.movement.speed != 0f) { data.gameplay |= ActionGameplayFlags.Move; }
            if (asset is AttackInfo attack)
            {
                data.resources |= ActionResources.Hands;
                entry.attack = CombatDataHelper.BuildMotionData(attack);
                entry.projectile = attack.projectileInfo.hasProjectile;
                data.gameplay |= entry.projectile ? ActionGameplayFlags.Cast : ActionGameplayFlags.Attack;
                entry.magic = new MagicDefinition
                {
                    magicName = attack.attackName, magicType = attack.magicType,
                    motionValue = attack.damageMultiplier, attackElement = attack.attackElement,
                    feature = attack.feature, guardDamage = attack.guardDamage,
                    armorBreakValue = attack.armorBreakValue, justGuardResistance = attack.justGuardResistance,
                    knockbackForce = attack.knockbackInfo.hasKnockback ? attack.knockbackInfo.force : default,
                    statusEffects = entry.attack.statusEffects, equipmentSource = attack.equipmentSource,
                    healAmount = attack.healAmount, bulletCount = Math.Max(1, attack.projectileInfo.projectileCount),
                    bulletProfile = new BulletProfile
                    {
                        moveType = attack.projectileInfo.trajectory, speed = attack.projectileInfo.speed,
                        lifeTime = attack.projectileInfo.lifetime, hitLimit = Math.Max(1, attack.projectileInfo.pierceCount + 1),
                        explodeRadius = attack.projectileInfo.aoeRadius,
                        homingStrength = attack.projectileInfo.homingStrength,
                    },
                };
            }
            int id = _execution.RegisterDefinition(data);
            _actions.Add(asset, id);
            added.Add(asset);
            _entries.Add(id, entry);
            ActionTransitionDefinition[] transitions = asset.transitions ?? Array.Empty<ActionTransitionDefinition>();
            data.id = id;
            data.transitions = new ActionTransition[transitions.Length];
            for (int i = 0; i < transitions.Length; i++)
            {
                ActionTransitionDefinition transition = transitions[i];
                if (transition.next == null) { throw new ArgumentException("An action transition needs a destination."); }
                data.transitions[i] = new ActionTransition
                {
                    trigger = transition.trigger, nextDefinitionId = RegisterGraph(transition.next, added),
                    signalId = transition.signalId, requiresHit = transition.requiresHit,
                    minElapsed = transition.minElapsed, onCompletion = transition.onCompletion,
                };
            }
            _execution.UpdateDefinition(id, data);
            return id;
        }

        public int Register(MotionDefinition asset)
        {
            if (asset == null) { throw new ArgumentNullException(nameof(asset)); }
            if (_motions.TryGetValue(asset, out int existing)) { return existing; }
            MotionInfo motion = asset.info;
            if (asset.Duration <= 0f && motion.activeClip != null) { motion.activeMotionDuration = motion.activeClip.length; }
            int id = _execution.RegisterDefinition(new ActionDefinitionData
            {
                motionOnly = true, resources = asset.resources,
                anticipationDuration = motion.preMotionDuration, activeDuration = motion.activeMotionDuration,
                recoveryDuration = motion.recoveryDuration, cancelAfter = 0f, cancelPoint = -1f,
                endMode = asset.loop ? ActionEndMode.Held : ActionEndMode.Timed,
            });
            _motions.Add(asset, id);
            _entries.Add(id, new Entry { motionAsset = asset, motion = motion });
            return id;
        }
    }
}
