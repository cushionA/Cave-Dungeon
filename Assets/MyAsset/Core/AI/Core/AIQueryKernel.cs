using System.Collections.Generic;
using UnityEngine;

namespace Game.Core
{
    internal interface IAIReadWorld
    {
        bool TryGetActor(int hash, out AIActorObservation actor);
        ActorRef ProtectedPlayer { get; }
        bool TryGetTargetOf(ActorRef actor, out ActorRef target);
        bool TryGetStunRemaining(ActorRef actor, out float seconds);
        bool TryGetNearbyHostileProjectileCount(ActorRef observer, out int count);
        bool TryGetTargetLineOfSight(ActorRef owner, ActorRef target, out bool visible);
        int ActorCount { get; }
        int HashAt(int index);
        bool GimmicksAvailable { get; }
        int GimmickCount { get; }
        AISceneGimmick GimmickAt(int index);
        bool IsGimmickOperationVisible(ActorRef observer, in AISceneGimmick gimmick);
        bool TryGetGimmickVelocity(ActorRef actor, out Vector2 velocity);
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
        public PhysicsScene2D physicsScene;
    }

    internal struct AIManagedWorld : IAIReadWorld
    {
        public AIObservationContext observation;
        public ActorRef ProtectedPlayer => observation.ProtectedPlayer;
        public bool TryGetTargetOf(ActorRef actor, out ActorRef target) => observation.TryGetTargetOf(actor, out target);
        public bool TryGetStunRemaining(ActorRef actor, out float seconds) => observation.TryGetStunRemaining(actor, out seconds);
        public bool TryGetNearbyHostileProjectileCount(ActorRef observer, out int count) =>
            observation.TryGetNearbyHostileProjectileCount(observer, out count);
        public bool TryGetTargetLineOfSight(ActorRef owner, ActorRef target, out bool visible) =>
            observation.TryGetTargetLineOfSight(owner, target, out visible);
        public int ActorCount => observation.ActorHashes.Count;
        public int HashAt(int index) => observation.ActorHashes[index];
        public bool GimmicksAvailable => observation.GimmicksAvailable;
        public int GimmickCount => observation.GimmickCount;
        public AISceneGimmick GimmickAt(int index) => observation.GimmickAt(index);
        public bool IsGimmickOperationVisible(ActorRef observer, in AISceneGimmick gimmick) =>
            observation.IsGimmickOperationVisible(observer, gimmick);
        public bool TryGetGimmickVelocity(ActorRef actor, out Vector2 velocity) => observation.TryGetGimmickVelocity(actor, out velocity);
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
                        || !world.TryGetActor(targetHash, out AIActorObservation target) || target.physicsScene != owner.physicsScene) { return false; }
                    value = Vector2.Distance(owner.position, target.position);
                    return true;
                case AIConditionType.TargetLineOfSight:
                    if (!world.TryGetActor(ownerHash, out AIActorObservation observerOfTarget)
                        || !world.TryGetActor(targetHash, out AIActorObservation observedTarget)
                        || observerOfTarget.physicsScene != observedTarget.physicsScene
                        || !world.TryGetTargetLineOfSight(observerOfTarget.actor, observedTarget.actor, out bool visible))
                    {
                        return false;
                    }
                    value = visible ? 1f : 0f;
                    return true;
                case AIConditionType.DamageScore:
                    return world.TryGetDamageScore(ownerHash, hash, out value);
                case AIConditionType.Count:
                    if (!world.TryGetActor(ownerHash, out AIActorObservation countingOwner)) { return false; }
                    return TryCount(condition.filter, countingOwner, false, ref world, out value);
                case AIConditionType.NearbyFaction:
                    if (!world.TryGetActor(ownerHash, out AIActorObservation nearbyOwner)) { return false; }
                    TargetFilter filter = condition.filter;
                    if (filter.belong == 0) { filter.belong = nearbyOwner.flags.Belong; }
                    if (filter.distanceRange.y <= 0f) { filter.distanceRange.y = 10f; }
                    return TryCount(filter, nearbyOwner, true, ref world, out value);
                case AIConditionType.ProjectileNear:
                    return false;
                case AIConditionType.NearbyHostileProjectileCount:
                    if (!world.TryGetActor(ownerHash, out AIActorObservation projectileObserver)
                        || !world.TryGetNearbyHostileProjectileCount(projectileObserver.actor, out int nearbyProjectiles)) { return false; }
                    value = nearbyProjectiles;
                    return true;
                case AIConditionType.ObjectNearby:
                    if (!world.GimmicksAvailable || !world.TryGetActor(ownerHash, out AIActorObservation observer))
                    {
                        return false;
                    }
                    int objectMask = 0;
                    for (int i = 0; i < world.GimmickCount; i++)
                    {
                        AISceneGimmick gimmick = world.GimmickAt(i);
                        if (CanObserveOperation(gimmick, observer, ref world)) { objectMask |= gimmick.objectMask; }
                    }
                    value = objectMask;
                    return true;
                case AIConditionType.SelfActState:
                    if (!world.TryGetActor(ownerHash, out AIActorObservation self)) { return false; }
                    value = (int)self.flags.ActState;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryCount<TWorld>(in TargetFilter filter, in AIActorObservation owner,
            bool excludeOwner, ref TWorld world, out float value) where TWorld : struct, IAIReadWorld
        {
            value = 0f;
            int count = 0;
            for (int i = 0; i < world.ActorCount; i++)
            {
                int hash = world.HashAt(i);
                if (excludeOwner && hash == owner.actor.Hash) { continue; }
                if (!world.TryGetActor(hash, out AIActorObservation candidate)) { continue; }
                bool matches = MatchesFilter(filter, owner, candidate, ref world, out bool known);
                if (!known) { return false; }
                if (matches) { count++; }
            }
            value = count;
            return true;
        }

        public static bool MatchesFilter<TWorld>(in TargetFilter filter, int ownerHash, int hash,
            ref TWorld world) where TWorld : struct, IAIReadWorld
        {
            return world.TryGetActor(ownerHash, out AIActorObservation owner)
                && world.TryGetActor(hash, out AIActorObservation candidate)
                && MatchesFilter(filter, owner, candidate, ref world, out _);
        }

        private static bool MatchesFilter<TWorld>(in TargetFilter filter, in AIActorObservation owner,
            in AIActorObservation candidate, ref TWorld world, out bool known) where TWorld : struct, IAIReadWorld
        {
            known = true;
            if (candidate.physicsScene != owner.physicsScene
                || candidate.currentHp <= 0 || candidate.flags.ActState == ActState.Dead || candidate.flags.IsVanished
                || candidate.actor.Hash == owner.actor.Hash && !filter.includeSelf) { return false; }
            if (filter.belong != 0 && ((filter.filterFlags & FilterBitFlag.BelongAnd) != 0
                ? (candidate.flags.Belong & filter.belong) != filter.belong : (candidate.flags.Belong & filter.belong) == 0)) { return false; }
            if (filter.feature != 0 && ((filter.filterFlags & FilterBitFlag.FeatureAnd) != 0
                ? (candidate.flags.Feature & filter.feature) != filter.feature : (candidate.flags.Feature & filter.feature) == 0)) { return false; }
            if (!filter.MatchesWeaknesses(candidate.weakPoints)) { return false; }
            if (filter.distanceRange.x > 0f || filter.distanceRange.y > 0f)
            {
                float distance = Vector2.Distance(owner.position, candidate.position);
                if (filter.distanceRange.x > 0f && distance < filter.distanceRange.x
                    || filter.distanceRange.y > 0f && distance > filter.distanceRange.y) { return false; }
            }
            if (filter.targetsProtectedPlayer && !TargetsProtectedPlayer(owner, candidate, ref world, out known) && known)
            {
                return false;
            }
            if (filter.stunMustLastBeyondSeconds != 0f)
            {
                if (!(filter.stunMustLastBeyondSeconds > 0f))
                {
                    known = true;
                    return false;
                }
                if (!world.TryGetStunRemaining(candidate.actor, out float remaining)) { known = false; }
                else if (!(remaining > filter.stunMustLastBeyondSeconds))
                {
                    known = true;
                    return false;
                }
            }
            return known;
        }

        private static bool TargetsProtectedPlayer<TWorld>(in AIActorObservation owner, in AIActorObservation candidate,
            ref TWorld world, out bool known) where TWorld : struct, IAIReadWorld
        {
            known = true;
            if (owner.actor == candidate.actor || !IsLiving(owner)) { return false; }
            if (!world.TryGetTargetOf(candidate.actor, out ActorRef recipient)) { known = false; return false; }
            if (!recipient.IsValid) { return false; }
            ActorRef ward = world.ProtectedPlayer;
            if (!ward.IsValid) { known = false; return false; }
            if (recipient != ward || owner.actor == ward) { return false; }
            if (!world.TryGetActor(ward.Hash, out AIActorObservation player) || player.actor != ward)
            {
                known = false;
                return false;
            }
            return IsLiving(player) && player.physicsScene == owner.physicsScene
                && AttackTargetRules.CanAffect(owner.actor, owner.flags.Belong, ward, player.flags.Belong, AttackTargetRelation.Friendly)
                && AttackTargetRules.CanAffect(candidate.actor, candidate.flags.Belong, ward, player.flags.Belong, AttackTargetRelation.Hostile);
        }

        public static bool TrySortValue<TWorld>(TargetSortKey key, int ownerHash, int hash,
            ref TWorld world, out float value) where TWorld : struct, IAIReadWorld
        {
            value = 0f;
            if (!world.TryGetActor(hash, out AIActorObservation target)) { return false; }
            world.TryGetActor(ownerHash, out AIActorObservation owner);
            return TrySortValue(key, ownerHash, owner, target, ref world, out value);
        }

        private static bool TrySortValue<TWorld>(TargetSortKey key, int ownerHash, in AIActorObservation owner,
            in AIActorObservation target, ref TWorld world, out float value) where TWorld : struct, IAIReadWorld
        {
            value = 0f;
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
                    if (!world.TryGetTargetingCount(target.actor.Hash, out int count)) { return false; }
                    value = count; return true;
                case TargetSortKey.DamageScore:
                    return world.TryGetDamageScore(ownerHash, target.actor.Hash, out value);
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
            if (!world.TryGetActor(ownerHash, out AIActorObservation owner)) { return 0; }
            TargetFilter filter = select.EffectiveFilter;
            int bestHash = 0;
            float bestValue = 0f;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (!candidates.TryGetHash(i, out int hash)
                    || !world.TryGetActor(hash, out AIActorObservation candidate)
                    || !MatchesFilter(filter, owner, candidate, ref world, out _)
                    || !TrySortValue(select.sortKey, ownerHash, owner, candidate, ref world, out float value)) { continue; }
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
                int previousTarget = targetHash;
                for (int i = 0; i < ranges.targetRuleCount; i++)
                {
                    AIRuleRange rule = program.GetRule(ranges.targetRuleStart + i);
                    if (!MatchesRule(rule, ownerHash, previousTarget, ref world, ref program)) { continue; }
                    if (rule.actionIndex >= 0 && rule.actionIndex < ranges.selectCount)
                    {
                        AITargetSelect select = program.GetTargetSelect(ranges.selectStart + rule.actionIndex);
                        targetHash = SelectTarget(select, ownerHash, ref world, ref candidates);
                        if (targetHash == 0 && select.continueIfEmpty) { continue; }
                    }
                    break;
                }
            }
            int actionIndex = -1;
            AISceneGimmick gimmick = default;
            if (selectAction && ranges.hasActionRules)
            {
                bool matched = false;
                for (int i = 0; i < ranges.actionRuleCount; i++)
                {
                    AIRuleRange rule = program.GetRule(ranges.actionRuleStart + i);
                    if (!MatchesRule(rule, ownerHash, targetHash, ref world, ref program)) { continue; }
                    if (rule.interactsWithGimmick
                        && !TryChooseGimmick(rule, ownerHash, targetHash, ref world, ref program, out gimmick)) { continue; }
                    actionIndex = rule.actionIndex;
                    matched = true;
                    break;
                }
                if (!matched && (!ranges.defaultInteractsWithGimmick
                    || TryChooseGimmick(default, ownerHash, targetHash, ref world, ref program, out gimmick)))
                {
                    actionIndex = ranges.defaultActionIndex;
                }
                if (actionIndex < 0 || actionIndex >= ranges.actionCount) { actionIndex = -1; }
            }
            return new AIDecisionResult { targetHash = targetHash, actionIndex = actionIndex, gimmick = gimmick };
        }

        private static bool TryChooseGimmick<TWorld, TProgram>(in AIRuleRange rule,
            int ownerHash, int targetHash, ref TWorld world, ref TProgram program, out AISceneGimmick best)
            where TWorld : struct, IAIReadWorld where TProgram : struct, IAIProgram
        {
            best = default;
            if (!world.GimmicksAvailable || world.GimmickCount == 0
                || !world.TryGetActor(ownerHash, out AIActorObservation owner) || !IsLiving(owner)
                || !world.TryGetActor(targetHash, out AIActorObservation target) || !IsLiving(target)
                || owner.physicsScene != target.physicsScene
                || !AttackTargetRules.CanAffect(owner.actor, owner.flags.Belong,
                    target.actor, target.flags.Belong, AttackTargetRelation.Hostile)) { return false; }

            float bestDistance = 0f;
            bool bestInReach = false;
            for (int i = 0; i < world.GimmickCount; i++)
            {
                AISceneGimmick candidate = world.GimmickAt(i);
                if (!CanObserveOperation(candidate, owner, ref world)) { continue; }
                if (candidate.requiresLure)
                {
                    if (!world.TryGetTargetOf(target.actor, out ActorRef pursued) || pursued != owner.actor
                        || !candidate.CanLureFrom(owner.position, target.position)) { continue; }
                }
                else
                {
                    Vector2 targetVelocity = default;
                    if (candidate.effectDelay > 0f && !world.TryGetGimmickVelocity(target.actor, out targetVelocity)) { continue; }
                    if (!candidate.WillAffectTarget(target.position, targetVelocity)) { continue; }
                }
                bool inReach = candidate.CanOperateFrom(owner.position);
                float distance = (owner.position - candidate.operationPosition).sqrMagnitude;
                if (best.id != 0 && (bestInReach && !inReach || bestInReach == inReach
                    && (distance > bestDistance || distance == bestDistance && candidate.id >= best.id))) { continue; }
                if (!MatchesObjectType(rule, candidate.objectMask, ref program) || HasFriendlyInEffect(candidate, owner, ref world))
                {
                    continue;
                }
                best = candidate;
                bestDistance = distance;
                bestInReach = inReach;
            }
            return best.id != 0;
        }

        private static bool CanObserveOperation<TWorld>(in AISceneGimmick gimmick,
            in AIActorObservation owner, ref TWorld world) where TWorld : struct, IAIReadWorld
        {
            if (gimmick.physicsScene != owner.physicsScene) { return false; }
            if (gimmick.CanOperateFrom(owner.position))
            {
                return !gimmick.requiresClearOperation || world.IsGimmickOperationVisible(owner.actor, gimmick);
            }
            return gimmick.requiresClearOperation && gimmick.TryGetApproachDestination(owner.position, out _)
                && world.IsGimmickOperationVisible(owner.actor, gimmick);
        }

        private static bool MatchesObjectType<TProgram>(in AIRuleRange rule, int objectMask, ref TProgram program)
            where TProgram : struct, IAIProgram
        {
            // 周囲の別々の装置が作ったORマスクを、選んだ1個の型として使わない。
            for (int i = 0; i < rule.conditionCount; i++)
            {
                AICondition condition = program.GetCondition(rule.conditionStart + i);
                if (condition.conditionType == AIConditionType.ObjectNearby
                    && (condition.compareOp == CompareOp.HasAny || condition.compareOp == CompareOp.HasFlag)
                    && !ConditionEvaluator.Compare(objectMask, condition.compareOp, condition.operandA, condition.operandB))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool HasFriendlyInEffect<TWorld>(in AISceneGimmick gimmick, in AIActorObservation owner, ref TWorld world)
            where TWorld : struct, IAIReadWorld
        {
            for (int i = 0; i < world.ActorCount; i++)
            {
                if (!world.TryGetActor(world.HashAt(i), out AIActorObservation actor) || !IsLiving(actor)
                    || actor.physicsScene != gimmick.physicsScene
                    || !AttackTargetRules.CanAffect(owner.actor, owner.flags.Belong, actor.actor, actor.flags.Belong,
                        AttackTargetRelation.Self | AttackTargetRelation.Friendly)) { continue; }
                Vector2 velocity = default;
                if (gimmick.effectDelay > 0f && !world.TryGetGimmickVelocity(actor.actor, out velocity)) { return true; }
                if (gimmick.ThreatensFriendly(actor.position, velocity)) { return true; }
            }
            return false;
        }

        private static bool IsLiving(in AIActorObservation actor) =>
            actor.currentHp > 0 && actor.flags.ActState != ActState.Dead && !actor.flags.IsVanished;

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
