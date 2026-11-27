using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>発射時の攻撃入力。射手の消失・装備変更・再登録後も再解決しない。</summary>
    public readonly struct AttackContext
    {
        private readonly StatusEffectApplicationData[] _equipmentEffects;
        private readonly StatusEffectApplicationData[] _actionEffects;
        public ActorRef Owner { get; }
        public ElementalStatus AttackStats { get; }
        public float AttackMultiplier { get; }
        public CharacterBelong Belong { get; }
        public AttackTargetRelation TargetRelations { get; }
        public bool IsCaptured => Owner.IsValid;
        public bool HasStatusEffects => (_actionEffects != null && _actionEffects.Length > 0)
            || (_equipmentEffects != null && _equipmentEffects.Length > 0);

        public static bool CanFinishAcceptedCast(ActorRef owner, in ActionExecution execution)
            => owner.IsValid && execution.ownerRef == owner && execution.ownerHash == owner.Hash
                && execution.instanceId != 0 && execution.groupId != 0 && execution.phase == AnimationPhase.Active
                && !execution.definition.motionOnly && (execution.definition.gameplay & ActionGameplayFlags.Cast) != 0;

        private AttackContext(ActorRef owner, in ElementalStatus stats, float multiplier,
            CharacterBelong belong, IReadOnlyList<StatusEffectApplicationData> effects, StatusEffectApplicationData[] actionEffects,
            AttackTargetRelation targetRelations)
        {
            Owner = owner;
            AttackStats = stats;
            AttackMultiplier = multiplier;
            Belong = belong;
            TargetRelations = targetRelations;
            _equipmentEffects = CopyEquipmentEffects(effects);
            _actionEffects = actionEffects == null || actionEffects.Length == 0
                ? Array.Empty<StatusEffectApplicationData>() : (StatusEffectApplicationData[])actionEffects.Clone();
        }

        private AttackContext(in AttackContext original, ActorRef reflector, CharacterBelong belong)
        {
            Owner = reflector;
            AttackStats = original.AttackStats;
            AttackMultiplier = original.AttackMultiplier;
            Belong = belong;
            TargetRelations = AttackTargetRelation.Hostile;
            _equipmentEffects = original._equipmentEffects;
            _actionEffects = original._actionEffects;
        }

        public static AttackContext Capture(ActorRef owner, in ElementalStatus stats, float multiplier,
            CharacterBelong belong, IReadOnlyList<StatusEffectApplicationData> equipmentEffects,
            StatusEffectApplicationData[] actionEffects = null,
            AttackTargetRelation targetRelations = AttackTargetRelation.Hostile)
        {
            if (!owner.IsValid) { throw new ArgumentException("A captured attack requires an actor generation.", nameof(owner)); }
            AttackTargetRules.Validate(targetRelations);
            return new AttackContext(owner, stats, multiplier, belong, equipmentEffects, actionEffects, targetRelations);
        }

        /// <summary>発射時に確定した威力・効果を保ち、反射した個体の敵対弾として扱う。</summary>
        public AttackContext Reflect(ActorRef reflector, CharacterBelong belong)
        {
            if (!IsCaptured || !reflector.IsValid)
            {
                throw new ArgumentException("Reflection requires captured and reflecting actor generations.");
            }
            return new AttackContext(this, reflector, belong);
        }

        private static StatusEffectApplicationData[] CopyEquipmentEffects(IReadOnlyList<StatusEffectApplicationData> effects)
        {
            if (effects == null || effects.Count == 0) { return Array.Empty<StatusEffectApplicationData>(); }
            StatusEffectApplicationData[] copy = new StatusEffectApplicationData[effects.Count];
            for (int i = 0; i < copy.Length; i++) { copy[i] = effects[i]; }
            return copy;
        }

        public void ApplyTo(ref DamageData damage, ActorRef defender)
        {
            damage.attackContext = this;
            damage.attackerRef = Owner;
            damage.defenderRef = defender;
            damage.attackerHash = Owner.Hash;
            damage.defenderHash = defender.Hash;
            damage.damage = AttackStats;
            damage.targetPolicy = new AttackTargetPolicy { overrideTargets = true, targets = TargetRelations };
            // 可変配列をパケットへ露出すると、別ヒットや子弾のsnapshotも変更されてしまう。
            damage.statusEffects = null;
            damage.equipmentStatusEffects = null;
        }

        public StatusEffectId ApplyStatusEffects<TWriter>(int liveSourceHash, int targetHash,
            GuardResult guard, float statusCut, TWriter writer) where TWriter : IStatusEffectWriter
            => StatusEffectApplicationLogic.Apply(_actionEffects, liveSourceHash, targetHash,
                guard, statusCut, writer, _equipmentEffects);

        /// <summary>旧hash通知の互換値。既に別個体なら0にして誤帰属を防ぐ。</summary>
        public int GetLiveSourceHash(ActorRegistry actors) => actors != null && actors.IsCurrent(Owner) ? Owner.Hash : 0;
    }
}
