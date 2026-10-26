using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Core
{
    public struct AIActorObservation
    {
        public ActorRef actor;
        public CharacterVitals vitals;
        public CombatStats combat;
        public CharacterFlags flags;
    }

    /// <summary>同じ固定tickの判断器が共有する値snapshot。取得していない情報はfalseで返す。</summary>
    public sealed class AIObservationContext
    {
        private const float k_DefaultNearbyRadius = 10f;
        private readonly Dictionary<int, AIActorObservation> _actors = new Dictionary<int, AIActorObservation>();
        private readonly List<int> _hashes = new List<int>();
        private readonly Dictionary<(ActorRef owner, ActorRef source), float> _scores = new Dictionary<(ActorRef, ActorRef), float>();
        private readonly Dictionary<ActorRef, ActorRef> _lastAttackers = new Dictionary<ActorRef, ActorRef>();
        private readonly Dictionary<ActorRef, int> _targetCounts = new Dictionary<ActorRef, int>();
        private bool _damageScoresAvailable;
        private bool _targetsAvailable;

        public long Tick { get; private set; }
        public float Time { get; private set; }
        public IReadOnlyList<int> ActorHashes => _hashes;

        public void BeginSnapshot(long tick, float time)
        {
            Tick = tick;
            Time = time;
            _actors.Clear(); _hashes.Clear(); _scores.Clear(); _lastAttackers.Clear(); _targetCounts.Clear();
            _damageScoresAvailable = false;
            _targetsAvailable = false;
        }

        public void AddActor(in AIActorObservation actor)
        {
            if (!actor.actor.IsValid) { return; }
            if (!_actors.ContainsKey(actor.actor.Hash)) { _hashes.Add(actor.actor.Hash); }
            _actors[actor.actor.Hash] = actor;
        }

        public bool TryGetActor(int hash, out AIActorObservation actor) => _actors.TryGetValue(hash, out actor);
        public bool Contains(ActorRef actor) => actor.IsValid && _actors.TryGetValue(actor.Hash, out AIActorObservation value) && value.actor.Equals(actor);
        public void SetDamageScoresAvailable() => _damageScoresAvailable = true;
        public void SetTargetRelationsAvailable() => _targetsAvailable = true;

        public void RecordDamageScore(ActorRef owner, ActorRef source, float score)
        {
            if (Contains(owner) && Contains(source)) { _scores[(owner, source)] = score; }
        }

        public void RecordLastAttacker(ActorRef owner, ActorRef source)
        {
            if (Contains(owner) && Contains(source)) { _lastAttackers[owner] = source; }
        }

        public bool TryGetDamageScore(int ownerHash, int sourceHash, out float score)
        {
            score = 0f;
            if (!_damageScoresAvailable || !TryGetActor(ownerHash, out AIActorObservation owner)
                || !TryGetActor(sourceHash, out AIActorObservation source)) { return false; }
            _scores.TryGetValue((owner.actor, source.actor), out score);
            return true;
        }

        public void RecordTarget(ActorRef owner, ActorRef target)
        {
            if (!Contains(owner) || !Contains(target)) { return; }
            _targetCounts.TryGetValue(target, out int count);
            _targetCounts[target] = count + 1;
        }

        public bool TryGetTargetingCount(int targetHash, out int count)
        {
            count = 0;
            if (!_targetsAvailable || !TryGetActor(targetHash, out AIActorObservation target)) { return false; }
            _targetCounts.TryGetValue(target.actor, out count);
            return true;
        }

        public bool Matches(TargetFilter filter, int ownerHash, in AIActorObservation candidate)
        {
            if (candidate.vitals.currentHp <= 0 || candidate.flags.ActState == ActState.Dead || candidate.flags.IsVanished) { return false; }
            if (candidate.actor.Hash == ownerHash && !filter.includeSelf) { return false; }
            if (filter.belong != 0)
            {
                bool match = (filter.filterFlags & FilterBitFlag.BelongAnd) != 0
                    ? (candidate.flags.Belong & filter.belong) == filter.belong
                    : (candidate.flags.Belong & filter.belong) != 0;
                if (!match) { return false; }
            }
            if (filter.feature != 0)
            {
                bool match = (filter.filterFlags & FilterBitFlag.FeatureAnd) != 0
                    ? (candidate.flags.Feature & filter.feature) == filter.feature
                    : (candidate.flags.Feature & filter.feature) != 0;
                if (!match) { return false; }
            }
            if (filter.weakPoint != 0)
            {
                bool found = false;
                for (int e = 0; e < 7; e++)
                {
                    Element element = (Element)(1 << e);
                    if ((filter.weakPoint & element) != 0 && candidate.combat.defense.Get(element) <= 0) { found = true; break; }
                }
                if (!found) { return false; }
            }
            if (filter.distanceRange.x > 0f || filter.distanceRange.y > 0f)
            {
                if (!TryGetActor(ownerHash, out AIActorObservation owner)) { return false; }
                float distance = Vector2.Distance(owner.vitals.position, candidate.vitals.position);
                if (filter.distanceRange.x > 0f && distance < filter.distanceRange.x) { return false; }
                if (filter.distanceRange.y > 0f && distance > filter.distanceRange.y) { return false; }
            }
            return true;
        }

        public bool TryGetConditionValue(in AICondition condition, int ownerHash, int targetHash, out float value)
        {
            value = 0f;
            int hash = condition.filter.includeSelf ? ownerHash : targetHash;
            switch (condition.conditionType)
            {
                case AIConditionType.HpRatio:
                case AIConditionType.MpRatio:
                case AIConditionType.StaminaRatio:
                case AIConditionType.ArmorRatio:
                    if (!TryGetActor(hash, out AIActorObservation actor)) { return false; }
                    if (condition.conditionType == AIConditionType.HpRatio) { value = actor.vitals.maxHp > 0 ? 100f * actor.vitals.currentHp / actor.vitals.maxHp : 0f; }
                    if (condition.conditionType == AIConditionType.MpRatio) { value = actor.vitals.maxMp > 0 ? 100f * actor.vitals.currentMp / actor.vitals.maxMp : 0f; }
                    if (condition.conditionType == AIConditionType.StaminaRatio) { value = actor.vitals.maxStamina > 0 ? 100f * actor.vitals.currentStamina / actor.vitals.maxStamina : 0f; }
                    if (condition.conditionType == AIConditionType.ArmorRatio) { value = actor.vitals.maxArmor > 0 ? 100f * actor.vitals.currentArmor / actor.vitals.maxArmor : 0f; }
                    return true;
                case AIConditionType.Distance:
                    if (!TryGetActor(ownerHash, out AIActorObservation owner) || !TryGetActor(targetHash, out AIActorObservation target)) { return false; }
                    value = Vector2.Distance(owner.vitals.position, target.vitals.position);
                    return true;
                case AIConditionType.DamageScore:
                    return TryGetDamageScore(ownerHash, hash, out value);
                case AIConditionType.Count:
                    if (!TryGetActor(ownerHash, out _)) { return false; }
                    for (int i = 0; i < _hashes.Count; i++)
                    {
                        if (Matches(condition.filter, ownerHash, _actors[_hashes[i]])) { value++; }
                    }
                    return true;
                case AIConditionType.NearbyFaction:
                    if (!TryGetActor(ownerHash, out AIActorObservation nearbyOwner)) { return false; }
                    TargetFilter nearbyFilter = condition.filter;
                    if (nearbyFilter.belong == 0) { nearbyFilter.belong = nearbyOwner.flags.Belong; }
                    if (nearbyFilter.distanceRange.y <= 0f) { nearbyFilter.distanceRange.y = k_DefaultNearbyRadius; }
                    for (int i = 0; i < _hashes.Count; i++)
                    {
                        AIActorObservation neighbor = _actors[_hashes[i]];
                        if (neighbor.actor.Hash != ownerHash && Matches(nearbyFilter, ownerHash, neighbor)) { value++; }
                    }
                    return true;
                case AIConditionType.SelfActState:
                    if (!TryGetActor(ownerHash, out AIActorObservation self)) { return false; }
                    value = (int)self.flags.ActState;
                    return true;
                // 知覚・地形・イベント履歴の情報源が接続されるまではunknown。
                case AIConditionType.ProjectileNear:
                case AIConditionType.ObjectNearby:
                case AIConditionType.EventFired:
                default:
                    return false;
            }
        }

        public bool TryGetSortValue(TargetSortKey key, int ownerHash, int targetHash, out float value)
        {
            value = 0f;
            if (!TryGetActor(targetHash, out AIActorObservation target)) { return false; }
            switch (key)
            {
                case TargetSortKey.Distance:
                    if (!TryGetActor(ownerHash, out AIActorObservation owner)) { return false; }
                    value = Vector2.Distance(owner.vitals.position, target.vitals.position); return true;
                case TargetSortKey.HpRatio: value = target.vitals.maxHp > 0 ? (float)target.vitals.currentHp / target.vitals.maxHp : 0f; return true;
                case TargetSortKey.HpValue: value = target.vitals.currentHp; return true;
                case TargetSortKey.AttackPower: value = target.combat.attack.Total; return true;
                case TargetSortKey.DefensePower: value = target.combat.defense.Total; return true;
                case TargetSortKey.TargetingCount:
                    if (!TryGetTargetingCount(targetHash, out int count)) { return false; }
                    value = count; return true;
                case TargetSortKey.DamageScore: return TryGetDamageScore(ownerHash, targetHash, out value);
                case TargetSortKey.LastAttacker:
                    if (!_damageScoresAvailable || !TryGetActor(ownerHash, out AIActorObservation scoreOwner)) { return false; }
                    if (!_lastAttackers.TryGetValue(scoreOwner.actor, out ActorRef lastAttacker)) { return false; }
                    value = lastAttacker.Equals(target.actor) ? 1f : 0f; return true;
                case TargetSortKey.Self: value = targetHash == ownerHash ? 1f : 0f; return true;
                case TargetSortKey.Player: value = (target.flags.Feature & CharacterFeature.Player) != 0 ? 1f : 0f; return true;
                case TargetSortKey.Sister: value = (target.flags.Feature & CharacterFeature.Companion) != 0 ? 1f : 0f; return true;
                default: return false;
            }
        }
    }
}
