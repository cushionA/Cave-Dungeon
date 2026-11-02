using System;

namespace Game.Core
{
    [Flags]
    public enum AttackTargetRelation : byte
    {
        None = 0,
        Hostile = 1 << 0,
        Friendly = 1 << 1,
        Self = 1 << 2,
        Neutral = 1 << 3,
        All = Hostile | Friendly | Self | Neutral,
    }

    /// <summary>作者入力。未指定は効果種別の既定を使い、明示指定では None も選べる。</summary>
    [Serializable]
    public struct AttackTargetPolicy
    {
        public bool overrideTargets;
        public AttackTargetRelation targets;

        public readonly AttackTargetRelation Resolve(MagicType type = MagicType.Attack)
        {
            AttackTargetRules.Validate(this);
            if (overrideTargets) { return targets; }
            return type == MagicType.Recover
                ? AttackTargetRelation.Friendly | AttackTargetRelation.Self
                : AttackTargetRelation.Hostile;
        }
    }

    /// <summary>接触・範囲・追尾で共有する、発生元から見た関係と対象条件の純判定。</summary>
    public static class AttackTargetRules
    {
        public static void Validate(AttackTargetPolicy policy)
        {
            Validate(policy.targets);
        }

        public static void Validate(AttackTargetRelation targets)
        {
            if ((targets & ~AttackTargetRelation.All) != AttackTargetRelation.None)
            {
                throw new ArgumentOutOfRangeException(nameof(targets), "Unknown attack target relation bits.");
            }
        }

        public static AttackTargetRelation GetRelation(CharacterBelong sourceBelong,
            CharacterBelong targetBelong, bool isSelf)
        {
            if (isSelf) { return AttackTargetRelation.Self; }
            CharacterBelong combatBelongs = CharacterBelong.Ally | CharacterBelong.Enemy;
            CharacterBelong sourceCombat = sourceBelong & combatBelongs;
            CharacterBelong targetCombat = targetBelong & combatBelongs;
            // 共有する戦闘陣営があれば友好。Neutral bit の共有だけでは友好にしない。
            if ((sourceCombat & targetCombat) != 0) { return AttackTargetRelation.Friendly; }
            if (sourceCombat != 0 && targetCombat != 0) { return AttackTargetRelation.Hostile; }
            return AttackTargetRelation.Neutral;
        }

        public static bool CanAffect(CharacterBelong sourceBelong, CharacterBelong targetBelong,
            bool isSelf, AttackTargetRelation targets)
        {
            if ((targets & ~AttackTargetRelation.All) != AttackTargetRelation.None) { return false; }
            return (targets & GetRelation(sourceBelong, targetBelong, isSelf)) != AttackTargetRelation.None;
        }

        public static bool CanAffect(ActorRef source, CharacterBelong sourceBelong,
            ActorRef target, CharacterBelong targetBelong, AttackTargetRelation targets)
        {
            return source.IsValid && target.IsValid
                && CanAffect(sourceBelong, targetBelong, source == target, targets);
        }

        public static bool CanAffect(in AttackContext context, ActorRef target, CharacterBelong targetBelong)
        {
            return context.IsCaptured && CanAffect(context.Owner, context.Belong,
                target, targetBelong, context.TargetRelations);
        }
    }
}
