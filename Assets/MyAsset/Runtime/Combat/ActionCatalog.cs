using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.Runtime
{
    /// <summary>編集用SOを検証し、実行と表示が共有する不変定義へ変換する。</summary>
    internal sealed class ActionCatalog
    {
        internal sealed class Entry
        {
            public readonly ActionDefinitionSnapshot definition;
            public readonly MotionPresentationData presentation;
            public readonly AttackExecutionSettings attackSettings;
            public readonly bool isAttack;
            public readonly bool projectile;
            public readonly ActionVariant variant;
            public readonly AttackEquipmentSource equipmentSource;
            public readonly AttackContactType contactType;
            public readonly float spreadAngle;
            private readonly AttackMotionData _attack;
            private readonly MagicDefinition _magic;

            public Entry(ActionDefinitionData definition, MotionPresentationData presentation,
                AttackExecutionSettings attackSettings = default, AttackMotionData attack = default,
                MagicDefinition magic = default, bool isAttack = false, bool projectile = false,
                ActionVariant variant = ActionVariant.Default, AttackEquipmentSource equipmentSource = default,
                AttackContactType contactType = default, float spreadAngle = 0f)
            {
                this.definition = new ActionDefinitionSnapshot(definition);
                this.presentation = presentation;
                this.attackSettings = attackSettings;
                this.isAttack = isAttack;
                this.projectile = projectile;
                this.variant = variant;
                this.equipmentSource = equipmentSource;
                this.contactType = contactType;
                this.spreadAngle = spreadAngle;
                _attack = attack;
                _magic = magic;
            }

            public Entry WithDefinition(ActionDefinitionData data, ActionVariant variant = ActionVariant.Default)
                => new Entry(data, presentation, attackSettings, _attack, _magic, isAttack, projectile,
                    variant, equipmentSource, contactType, spreadAngle);

            // packetは実行ごとに書換える。カタログ内部の配列は渡さない。
            public AttackMotionData CreateAttack()
            {
                AttackMotionData packet = _attack;
                packet.statusEffects = Copy(_attack.statusEffects);
                packet.equipmentStatusEffects = Copy(_attack.equipmentStatusEffects);
                return packet;
            }

            public MagicDefinition CreateMagic()
            {
                MagicDefinition packet = _magic;
                packet.statusEffects = Copy(_magic.statusEffects);
                packet.equipmentStatusEffects = Copy(_magic.equipmentStatusEffects);
                return packet;
            }

            private static StatusEffectApplicationData[] Copy(StatusEffectApplicationData[] values)
                => values != null && values.Length > 0 ? (StatusEffectApplicationData[])values.Clone()
                    : Array.Empty<StatusEffectApplicationData>();
        }

        private sealed class Draft
        {
            public ActionDefinition source;
            public Entry entry;
            public ActionTransitionDefinition[] transitions;
        }

        private readonly ActionExecutionSystem _execution;
        private readonly Dictionary<ActionDefinition, int> _actions = new Dictionary<ActionDefinition, int>();
        private readonly Dictionary<MotionDefinition, int> _motions = new Dictionary<MotionDefinition, int>();
        private readonly Dictionary<int, Entry> _entries = new Dictionary<int, Entry>();
        private readonly Dictionary<(ActionDefinition asset, ActionVariant variant), int> _variants = new();
        private readonly Dictionary<int, int> _landingRecovery = new();

        public ActionCatalog(ActionExecutionSystem execution)
        {
            _execution = execution ?? throw new ArgumentNullException(nameof(execution));
        }

        public bool TryGet(int id, out Entry entry) => _entries.TryGetValue(id, out entry);

        /// <summary>SOは登録済み定義の識別子としてだけ使い、値を再読しない。</summary>
        public bool TryGetRegistered(ActionDefinition asset, ActionVariant variant, out Entry entry)
        {
            entry = null;
            if (ReferenceEquals(asset, null)) { return false; }
            bool found = variant == ActionVariant.Default
                ? _actions.TryGetValue(asset, out int id)
                : _variants.TryGetValue((asset, variant), out id);
            return found && _entries.TryGetValue(id, out entry);
        }

        public int Register(ActionDefinition asset)
        {
            if (asset == null) { throw new ArgumentNullException(nameof(asset)); }
            if (_actions.TryGetValue(asset, out int existing)) { return existing; }

            // 全到達先を先に検証・捕捉する。失敗したgraphを共有実行器へ途中公開しない。
            Dictionary<ActionDefinition, Draft> drafts = new Dictionary<ActionDefinition, Draft>();
            List<Draft> ordered = new List<Draft>();
            CaptureGraph(asset, drafts, ordered);
            Dictionary<ActionDefinition, int> created = new Dictionary<ActionDefinition, int>();
            try
            {
                for (int i = 0; i < ordered.Count; i++)
                {
                    Draft draft = ordered[i];
                    ActionDefinitionData data = draft.entry.definition.ToData();
                    data.transitions = Array.Empty<ActionTransition>();
                    created.Add(draft.source, _execution.RegisterDefinition(data));
                }
                for (int i = 0; i < ordered.Count; i++)
                {
                    Draft draft = ordered[i];
                    int id = created[draft.source];
                    ActionDefinitionData data = draft.entry.definition.ToData();
                    data.id = id;
                    for (int j = 0; j < draft.transitions.Length; j++)
                    {
                        ActionDefinition destination = draft.transitions[j].next;
                        data.transitions[j].nextDefinitionId = created.TryGetValue(destination, out int next)
                            ? next : _actions[destination];
                    }
                    _execution.UpdateDefinition(id, data);
                    draft.entry = draft.entry.WithDefinition(data);
                }
                for (int i = 0; i < ordered.Count; i++)
                {
                    Draft draft = ordered[i];
                    int id = created[draft.source];
                    _actions.Add(draft.source, id);
                    _entries.Add(id, draft.entry);
                }
                return created[asset];
            }
            catch
            {
                foreach (KeyValuePair<ActionDefinition, int> action in created)
                {
                    _execution.UnregisterDefinition(action.Value);
                    _entries.Remove(action.Value);
                    _actions.Remove(action.Key);
                }
                throw;
            }
        }

        private void CaptureGraph(ActionDefinition asset, Dictionary<ActionDefinition, Draft> drafts, List<Draft> ordered)
        {
            if (_actions.ContainsKey(asset) || drafts.ContainsKey(asset)) { return; }
            ActionTransitionDefinition[] transitions = asset.transitions != null
                ? (ActionTransitionDefinition[])asset.transitions.Clone() : Array.Empty<ActionTransitionDefinition>();
            Draft draft = new Draft { source = asset, entry = Capture(asset, transitions), transitions = transitions };
            drafts.Add(asset, draft);
            ordered.Add(draft);
            for (int i = 0; i < transitions.Length; i++)
            {
                if (transitions[i].next == null) { throw new ArgumentException("An action transition needs a destination.", nameof(asset)); }
                CaptureGraph(transitions[i].next, drafts, ordered);
            }
        }

        private static Entry Capture(ActionDefinition asset, ActionTransitionDefinition[] transitions)
        {
            MotionInfo motion = asset.GetMotion();
            ValidatePresentation(asset.motion);
            ActionDefinitionData data = new ActionDefinitionData
            {
                resources = asset.resources, priority = asset.priority,
                staminaCost = asset.staminaCost, mpCost = asset.mpCost, staminaPerSecond = asset.staminaPerSecond,
                anticipationDuration = motion.preMotionDuration, activeDuration = motion.activeMotionDuration,
                recoveryDuration = motion.recoveryDuration, cancelAfter = asset.cancelAfter,
                cancelPoint = asset.cancelPoint, endMode = asset.endMode, actState = asset.actState,
                durationMode = asset.durationMode, duration = asset.duration, gameplay = asset.gameplay,
                effects = asset.actionEffects, moveSpeed = asset.movement.speed, moveStartTime = asset.movement.startTime,
                moveDuration = asset.movement.duration, movementUsesInput = asset.movement.usesInput,
                transitions = new ActionTransition[transitions.Length],
            };
            for (int i = 0; i < transitions.Length; i++)
            {
                ActionTransitionDefinition transition = transitions[i];
                data.transitions[i] = new ActionTransition
                {
                    trigger = transition.trigger, nextDefinitionId = 1, signalId = transition.signalId,
                    requiresHit = transition.requiresHit, minElapsed = transition.minElapsed,
                    onCompletion = transition.onCompletion, detached = transition.detached,
                };
            }
            if (asset.movement.speed != 0f) { data.gameplay |= ActionGameplayFlags.Move; }
            MotionPresentationData presentation = MotionPresentationData.Capture(asset.motion, motion);
            if (!(asset is AttackInfo attack))
            {
                ActionDefinitionValidator.Validate(data);
                return new Entry(data, presentation);
            }

            ValidateAttack(attack);
            data.resources |= ActionResources.Hands;
            bool projectile = attack.projectileInfo.hasProjectile;
            data.gameplay |= projectile ? ActionGameplayFlags.Cast : ActionGameplayFlags.Attack;
            ActionDefinitionValidator.Validate(data);
            AttackMotionData hit = CombatDataHelper.BuildMotionData(attack);
            MagicDefinition magic = new MagicDefinition
            {
                magicName = attack.attackName, magicType = attack.magicType,
                motionValue = attack.damageMultiplier, attackElement = attack.attackElement,
                feature = attack.feature, guardDamage = attack.guardDamage,
                armorBreakValue = attack.armorBreakValue, justGuardResistance = attack.justGuardResistance,
                knockbackForce = attack.knockbackInfo.hasKnockback ? attack.knockbackInfo.force : default,
                statusEffects = hit.statusEffects, equipmentSource = attack.equipmentSource,
                healAmount = attack.healAmount, bulletCount = Math.Max(1, attack.projectileInfo.projectileCount),
                bulletProfile = new BulletProfile
                {
                    moveType = attack.projectileInfo.trajectory, speed = attack.projectileInfo.speed,
                    lifeTime = attack.projectileInfo.lifetime, hitLimit = Math.Max(1, attack.projectileInfo.pierceCount + 1),
                    explodeRadius = attack.projectileInfo.aoeRadius, homingStrength = attack.projectileInfo.homingStrength,
                },
            };
            return new Entry(data, presentation, AttackExecutionSettings.Capture(attack), hit, magic,
                true, projectile, equipmentSource: attack.equipmentSource, contactType: attack.contactType,
                spreadAngle: attack.projectileInfo.spreadAngle);
        }

        public int Register(ActionDefinition asset, ActionVariant variant)
        {
            if (!Enum.IsDefined(typeof(ActionVariant), variant)) { throw new ArgumentOutOfRangeException(nameof(variant)); }
            int source = Register(asset);
            if (variant == ActionVariant.Default) { return source; }
            if (_variants.TryGetValue((asset, variant), out int existing)) { return existing; }
            Entry original = _entries[source];
            ActionDefinitionData data = original.definition.ToData();
            data.id = 0;
            data.gameplay |= ActionGameplayFlags.Move;
            if (variant == ActionVariant.Diving)
            {
                data.endMode = ActionEndMode.Manual;
                data.resources |= ActionResources.HorizontalMovement | ActionResources.VerticalMovement | ActionResources.Hands;
            }
            int id = _execution.RegisterDefinition(data);
            data.id = id;
            _entries.Add(id, original.WithDefinition(data, variant));
            _variants.Add((asset, variant), id);
            return id;
        }

        public int RegisterLandingRecovery(int sourceDefinition)
        {
            if (_landingRecovery.TryGetValue(sourceDefinition, out int existing)) { return existing; }
            Entry source = _entries[sourceDefinition];
            ActionDefinitionData data = new ActionDefinitionData
            {
                resources = ActionResources.All, priority = source.definition.priority,
                actState = ActState.Attacking, gameplay = ActionGameplayFlags.Move,
                endMode = ActionEndMode.Timed, durationMode = ActionDurationMode.Motion,
                recoveryDuration = source.attackSettings.DivingLandingRecoveryDuration, cancelAfter = -1f, cancelPoint = -1f,
            };
            int id = _execution.RegisterDefinition(data);
            data.id = id;
            _entries.Add(id, new Entry(data, source.presentation));
            _landingRecovery.Add(sourceDefinition, id);
            return id;
        }

        public int Register(MotionDefinition asset)
        {
            if (asset == null) { throw new ArgumentNullException(nameof(asset)); }
            if (_motions.TryGetValue(asset, out int existing)) { return existing; }
            ValidatePresentation(asset);
            MotionInfo motion = asset.info;
            if (asset.Duration <= 0f && motion.activeClip != null) { motion.activeMotionDuration = motion.activeClip.length; }
            ActionDefinitionData data = new ActionDefinitionData
            {
                motionOnly = true, resources = asset.resources,
                anticipationDuration = motion.preMotionDuration, activeDuration = motion.activeMotionDuration,
                recoveryDuration = motion.recoveryDuration, cancelAfter = 0f, cancelPoint = -1f,
                endMode = asset.loop ? ActionEndMode.Held : ActionEndMode.Timed,
            };
            int id = _execution.RegisterDefinition(data);
            data.id = id;
            _motions.Add(asset, id);
            _entries.Add(id, new Entry(data, MotionPresentationData.Capture(asset, motion)));
            return id;
        }

        private static void ValidatePresentation(MotionDefinition motion)
        {
            if (motion != null && (motion.animatorLayer < 0 || !NonNegative(motion.blendDuration)
                || !NonNegative(motion.info.preMotionDuration) || !NonNegative(motion.info.activeMotionDuration)
                || !NonNegative(motion.info.recoveryDuration) || !Finite(motion.Duration)))
            {
                throw new ArgumentException("Motion requires a nonnegative layer and finite nonnegative timing.", nameof(motion));
            }
        }

        private static void ValidateAttack(AttackInfo attack)
        {
            if (!NonNegative(attack.damageMultiplier) || !NonNegative(attack.guardDamage)
                || !NonNegative(attack.armorBreakValue) || !NonNegative(attack.justGuardResistance)
                || attack.justGuardResistance > 100f || attack.healAmount < 0
                || !NonNegative(attack.inputWindow) || !NonNegative(attack.airHangDuration)
                || !NonNegative(attack.airHangGravityScale) || !NonNegative(attack.aerialMoveSpeedDefault)
                || !NonNegative(attack.divingGravityMultiplier) || !NonNegative(attack.divingForwardSpeed)
                || !NonNegative(attack.divingLandingRecoveryDuration)
                || !Finite(GameConstants.k_GravityScale * attack.divingGravityMultiplier)
                || !Finite(attack.movement.speed * attack.movement.duration)
                || !Finite(attack.aerialMoveDirection.x) || !Finite(attack.aerialMoveDirection.y)
                || !Finite(attack.knockbackInfo.force.x) || !Finite(attack.knockbackInfo.force.y)
                || !Enum.IsDefined(typeof(AttackEquipmentSource), attack.equipmentSource)
                || !Enum.IsDefined(typeof(AttackContactType), attack.contactType)
                || !Enum.IsDefined(typeof(MagicType), attack.magicType))
            {
                throw new ArgumentException("Invalid attack timing, damage or movement settings.", nameof(attack));
            }
            ProjectileInfo projectile = attack.projectileInfo;
            if (projectile.hasProjectile && (!NonNegative(projectile.speed) || !NonNegative(projectile.lifetime)
                || !NonNegative(projectile.aoeRadius) || !NonNegative(projectile.homingStrength)
                || !NonNegative(projectile.spreadAngle) || projectile.spreadAngle > 360f
                || projectile.pierceCount < 0 || projectile.pierceCount == int.MaxValue || projectile.projectileCount < 0
                || !Enum.IsDefined(typeof(BulletMoveType), projectile.trajectory)))
            {
                throw new ArgumentException("Invalid projectile settings.", nameof(attack));
            }
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool NonNegative(float value) => value >= 0f && !float.IsInfinity(value);
    }
}
