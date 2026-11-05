using System.Collections.Generic;
using UnityEngine;

namespace Game.Core
{
    internal interface IAIReadWorld
    {
        bool TryGetActor(int hash, out AIActorObservation actor);
        int ActorCount { get; }
        int HashAt(int index);
        bool TryGetDamageScore(int owner, int source, out float value);
        bool TryGetTargetingCount(int target, out int count);
        bool TryGetLastAttacker(int owner, out ActorRef attacker);
    }

    internal interface IAICandidates
    {
        int Count { get; }
        bool TryGetHash(int index, out int hash);
    }

    internal interface IAIProgram
    {
        AIProgramRanges Ranges { get; }
        AIRuleRange GetRule(int index);
        AICondition GetCondition(int index);
        AITargetSelect GetTargetSelect(int index);
    }

    internal struct AICombatValues
    {
        public int attack;
        public int defense;
        public Element weaknesses;
    }

    internal struct AIManagedWorld : IAIReadWorld
    {
        public AIObservationContext observation;
        public int ActorCount => observation.ActorHashes.Count;
        public int HashAt(int index) => observation.ActorHashes[index];
        public bool TryGetActor(int hash, out AIActorObservation actor) => observation.TryGetActor(hash, out actor);
        public bool TryGetDamageScore(int owner, int source, out float value) => observation.TryGetDamageScore(owner, source, out value);
        public bool TryGetTargetingCount(int target, out int count) => observation.TryGetTargetingCount(target, out count);
        public bool TryGetLastAttacker(int owner, out ActorRef attacker) => observation.TryGetLastAttacker(owner, out attacker);
    }

    internal struct AIManagedCandidates : IAICandidates
    {
        public IReadOnlyList<int> hashes;
        public int Count => hashes.Count;
        public bool TryGetHash(int index, out int hash)
        {
            hash = hashes[index];
            return true;
        }
    }

    internal static class AIQueryKernel
    {
        public static bool TryConditionValue<TWorld>(in AICondition condition, int ownerHash,
            int targetHash, ref TWorld world, out float value) where TWorld : struct, IAIReadWorld
        {
            value = 0f;
            int hash = condition.filter.includeSelf ? ownerHash : targetHash;
            switch (condition.conditionType)
            {
                case AIConditionType.HpRatio:
                case AIConditionType.MpRatio:
                case AIConditionType.StaminaRatio:
                case AIConditionType.ArmorRatio:
                    if (!world.TryGetActor(hash, out AIActorObservation actor)) { return false; }
                    if (condition.conditionType == AIConditionType.HpRatio) { value = actor.hpPercent; }
                    if (condition.conditionType == AIConditionType.MpRatio) { value = actor.mpPercent; }
                    if (condition.conditionType == AIConditionType.StaminaRatio) { value = actor.staminaPercent; }
                    if (condition.conditionType == AIConditionType.ArmorRatio) { value = actor.armorPercent; }
                    return true;
                case AIConditionType.Distance:
                    if (!world.TryGetActor(ownerHash, out AIActorObservation owner)
                        || !world.TryGetActor(targetHash, out AIActorObservation target)) { return false; }
                    value = Vector2.Distance(owner.position, target.position);
                    return true;
                case AIConditionType.DamageScore:
                    return world.TryGetDamageScore(ownerHash, hash, out value);
                case AIConditionType.Count:
                    if (!world.TryGetActor(ownerHash, out _)) { return false; }
                    for (int i = 0; i < world.ActorCount; i++)
                    {
                        if (MatchesFilter(condition.filter, ownerHash, world.HashAt(i), ref world)) { value++; }
                    }
                    return true;
                case AIConditionType.NearbyFaction:
                    if (!world.TryGetActor(ownerHash, out AIActorObservation nearbyOwner)) { return false; }
                    TargetFilter filter = condition.filter;
                    if (filter.belong == 0) { filter.belong = nearbyOwner.flags.Belong; }
                    if (filter.distanceRange.y <= 0f) { filter.distanceRange.y = 10f; }
                    for (int i = 0; i < world.ActorCount; i++)
                    {
                        int candidate = world.HashAt(i);
                        if (candidate != ownerHash && MatchesFilter(filter, ownerHash, candidate, ref world)) { value++; }
                    }
                    return true;
                case AIConditionType.SelfActState:
                    if (!world.TryGetActor(ownerHash, out AIActorObservation self)) { return false; }
                    value = (int)self.flags.ActState;
                    return true;
                default:
                    return false;
            }
        }

        public static bool MatchesFilter<TWorld>(in TargetFilter filter, int ownerHash, int hash,
            ref TWorld world) where TWorld : struct, IAIReadWorld
        {
            if (!world.TryGetActor(hash, out AIActorObservation candidate)
                || candidate.currentHp <= 0 || candidate.flags.ActState == ActState.Dead || candidate.flags.IsVanished
                || hash == ownerHash && !filter.includeSelf) { return false; }
            if (filter.belong != 0 && ((filter.filterFlags & FilterBitFlag.BelongAnd) != 0
                ? (candidate.flags.Belong & filter.belong) != filter.belong : (candidate.flags.Belong & filter.belong) == 0)) { return false; }
            if (filter.feature != 0 && ((filter.filterFlags & FilterBitFlag.FeatureAnd) != 0
                ? (candidate.flags.Feature & filter.feature) != filter.feature : (candidate.flags.Feature & filter.feature) == 0)) { return false; }
            if (filter.weakPoint != 0 && (candidate.weakPoints & filter.weakPoint) == 0) { return false; }
            if (filter.distanceRange.x > 0f || filter.distanceRange.y > 0f)
            {
                if (!world.TryGetActor(ownerHash, out AIActorObservation owner)) { return false; }
                float distance = Vector2.Distance(owner.position, candidate.position);
                if (filter.distanceRange.x > 0f && distance < filter.distanceRange.x
                    || filter.distanceRange.y > 0f && distance > filter.distanceRange.y) { return false; }
            }
            return true;
        }

        public static bool TrySortValue<TWorld>(TargetSortKey key, int ownerHash, int hash,
            ref TWorld world, out float value) where TWorld : struct, IAIReadWorld
        {
            value = 0f;
            if (!world.TryGetActor(hash, out AIActorObservation target)) { return false; }
            world.TryGetActor(ownerHash, out AIActorObservation owner);
            switch (key)
            {
                case TargetSortKey.Distance:
                    if (!owner.actor.IsValid) { return false; }
                    value = Vector2.Distance(owner.position, target.position); return true;
                case TargetSortKey.HpRatio: value = target.hpFraction; return true;
                case TargetSortKey.HpValue: value = target.currentHp; return true;
                case TargetSortKey.AttackPower: value = target.attackPower; return true;
                case TargetSortKey.DefensePower: value = target.defensePower; return true;
                case TargetSortKey.TargetingCount:
                    if (!world.TryGetTargetingCount(hash, out int count)) { return false; }
                    value = count; return true;
                case TargetSortKey.DamageScore:
                    return world.TryGetDamageScore(ownerHash, hash, out value);
                case TargetSortKey.LastAttacker:
                    if (!world.TryGetLastAttacker(ownerHash, out ActorRef attacker)) { return false; }
                    value = attacker == target.actor ? 1f : 0f; return true;
                case TargetSortKey.Self: value = target.actor == owner.actor ? 1f : 0f; return true;
                case TargetSortKey.Player: value = (target.flags.Feature & CharacterFeature.Player) != 0 ? 1f : 0f; return true;
                case TargetSortKey.Sister: value = (target.flags.Feature & CharacterFeature.Companion) != 0 ? 1f : 0f; return true;
                default: return false;
            }
        }

        public static int SelectTarget<TWorld, TCandidates>(in AITargetSelect select, int ownerHash,
            ref TWorld world, ref TCandidates candidates)
            where TWorld : struct, IAIReadWorld where TCandidates : struct, IAICandidates
        {
            if (!world.TryGetActor(ownerHash, out _)) { return 0; }
            int bestHash = 0;
            float bestValue = 0f;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (!candidates.TryGetHash(i, out int hash)
                    || !MatchesFilter(select.filter, ownerHash, hash, ref world)
                    || !TrySortValue(select.sortKey, ownerHash, hash, ref world, out float value)) { continue; }
                if (bestHash == 0 || (select.isDescending ? value > bestValue : value < bestValue))
                {
                    bestHash = hash;
                    bestValue = value;
                }
            }
            return bestHash;
        }

        public static AIDecisionResult Decide<TWorld, TProgram, TCandidates>(int ownerHash,
            int targetHash, bool selectTarget, bool selectAction,
            ref TWorld world, ref TProgram program, ref TCandidates candidates)
            where TWorld : struct, IAIReadWorld where TProgram : struct, IAIProgram
            where TCandidates : struct, IAICandidates
        {
            AIProgramRanges ranges = program.Ranges;
            if (selectTarget && ranges.hasTargetRules)
            {
                for (int i = 0; i < ranges.targetRuleCount; i++)
                {
                    AIRuleRange rule = program.GetRule(ranges.targetRuleStart + i);
                    if (!MatchesRule(rule, ownerHash, targetHash, ref world, ref program)) { continue; }
                    if (rule.actionIndex >= 0 && rule.actionIndex < ranges.selectCount)
                    {
                        targetHash = SelectTarget(program.GetTargetSelect(ranges.selectStart + rule.actionIndex),
                            ownerHash, ref world, ref candidates);
                    }
                    break;
                }
            }
            int actionIndex = -1;
            if (selectAction && ranges.hasActionRules)
            {
                bool matched = false;
                for (int i = 0; i < ranges.actionRuleCount; i++)
                {
                    AIRuleRange rule = program.GetRule(ranges.actionRuleStart + i);
                    if (MatchesRule(rule, ownerHash, targetHash, ref world, ref program))
                    {
                        actionIndex = rule.actionIndex;
                        matched = true;
                        break;
                    }
                }
                if (!matched) { actionIndex = ranges.defaultActionIndex; }
                if (actionIndex < 0 || actionIndex >= ranges.actionCount) { actionIndex = -1; }
            }
            return new AIDecisionResult { targetHash = targetHash, actionIndex = actionIndex };
        }

        private static bool MatchesRule<TWorld, TProgram>(AIRuleRange rule, int ownerHash,
            int targetHash, ref TWorld world, ref TProgram program)
            where TWorld : struct, IAIReadWorld where TProgram : struct, IAIProgram
        {
            for (int i = 0; i < rule.conditionCount; i++)
            {
                AICondition condition = program.GetCondition(rule.conditionStart + i);
                if (!TryConditionValue(condition, ownerHash, targetHash, ref world, out float value)
                    || !ConditionEvaluator.Compare(value, condition.compareOp, condition.operandA, condition.operandB)) { return false; }
            }
            return true;
        }
    }
}
