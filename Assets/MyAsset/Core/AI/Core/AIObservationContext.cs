using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Core
{
    /// <summary>AIが同じtickで読む値だけを捕捉する。SoAへの参照や戦闘定義全体を保持しない。</summary>
    public readonly struct AIActorObservation
    {
        public readonly ActorRef actor;
        public readonly Vector2 position;
        public readonly CharacterFlags flags;
        public readonly int currentHp;
        public readonly float hpPercent;
        public readonly float hpFraction;
        public readonly float mpPercent;
        public readonly float staminaPercent;
        public readonly float armorPercent;
        public readonly int attackPower;
        public readonly int defensePower;
        public readonly Element weakPoints;

        private AIActorObservation(ActorRef actor, in CharacterVitals vitals,
            in CombatStats combat, in CharacterFlags flags)
        {
            this.actor = actor;
            position = vitals.position;
            this.flags = flags;
            currentHp = vitals.currentHp;
            // 条件と並び替えは従来の演算順が違うため、相互変換せずそれぞれ捕捉する。
            hpPercent = vitals.maxHp > 0 ? 100f * vitals.currentHp / vitals.maxHp : 0f;
            hpFraction = vitals.maxHp > 0 ? (float)vitals.currentHp / vitals.maxHp : 0f;
            mpPercent = vitals.maxMp > 0 ? 100f * vitals.currentMp / vitals.maxMp : 0f;
            staminaPercent = vitals.maxStamina > 0 ? 100f * vitals.currentStamina / vitals.maxStamina : 0f;
            armorPercent = vitals.maxArmor > 0 ? 100f * vitals.currentArmor / vitals.maxArmor : 0f;
            attackPower = combat.attack.Total;
            defensePower = combat.defense.Total;
            // 弱点の現行契約は「指定属性の防御値が0以下」。作者の弱点設定との統合は別タスク。
            Element weak = default;
            if (combat.defense.slash <= 0) { weak |= Element.Slash; }
            if (combat.defense.strike <= 0) { weak |= Element.Strike; }
            if (combat.defense.pierce <= 0) { weak |= Element.Pierce; }
            if (combat.defense.fire <= 0) { weak |= Element.Fire; }
            if (combat.defense.thunder <= 0) { weak |= Element.Thunder; }
            if (combat.defense.light <= 0) { weak |= Element.Light; }
            if (combat.defense.dark <= 0) { weak |= Element.Dark; }
            weakPoints = weak;
        }

        /// <summary>現在値をAIの判定に必要な値だけへ投影し、次のsnapshotまで固定する。</summary>
        public static AIActorObservation Capture(ActorRef actor, in CharacterVitals vitals,
            in CombatStats combat, in CharacterFlags flags) => new AIActorObservation(actor, vitals, combat, flags);
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

        public bool Matches(in TargetFilter filter, int ownerHash, in AIActorObservation candidate)
        {
            Vector2 ownerPosition = default;
            if (filter.distanceRange.x > 0f || filter.distanceRange.y > 0f)
            {
                if (!TryGetActor(ownerHash, out AIActorObservation owner)) { return false; }
                ownerPosition = owner.position;
            }
            return Matches(filter, ownerHash, ownerPosition, candidate);
        }

        internal bool Matches(in TargetFilter filter, in AIActorObservation owner, in AIActorObservation candidate)
            => Matches(filter, owner.actor.Hash, owner.position, candidate);

        private static bool Matches(in TargetFilter filter, int ownerHash, Vector2 ownerPosition,
            in AIActorObservation candidate)
        {
            if (candidate.currentHp <= 0 || candidate.flags.ActState == ActState.Dead || candidate.flags.IsVanished) { return false; }
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
                if ((filter.weakPoint & candidate.weakPoints) == 0) { return false; }
            }
            if (filter.distanceRange.x > 0f || filter.distanceRange.y > 0f)
            {
                float distance = Vector2.Distance(ownerPosition, candidate.position);
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
                    if (condition.conditionType == AIConditionType.HpRatio) { value = actor.hpPercent; }
                    if (condition.conditionType == AIConditionType.MpRatio) { value = actor.mpPercent; }
                    if (condition.conditionType == AIConditionType.StaminaRatio) { value = actor.staminaPercent; }
                    if (condition.conditionType == AIConditionType.ArmorRatio) { value = actor.armorPercent; }
                    return true;
                case AIConditionType.Distance:
                    if (!TryGetActor(ownerHash, out AIActorObservation owner) || !TryGetActor(targetHash, out AIActorObservation target)) { return false; }
                    value = Vector2.Distance(owner.position, target.position);
                    return true;
                case AIConditionType.DamageScore:
                    return TryGetDamageScore(ownerHash, hash, out value);
                case AIConditionType.Count:
                    if (!TryGetActor(ownerHash, out AIActorObservation countOwner)) { return false; }
                    for (int i = 0; i < _hashes.Count; i++)
                    {
                        AIActorObservation candidate = _actors[_hashes[i]];
                        if (Matches(condition.filter, countOwner, candidate)) { value++; }
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
                        if (neighbor.actor.Hash != ownerHash && Matches(nearbyFilter, nearbyOwner, neighbor)) { value++; }
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
            TryGetActor(ownerHash, out AIActorObservation owner);
            return TryGetSortValue(key, owner, target, out value);
        }

        internal bool TryGetSortValue(TargetSortKey key, in AIActorObservation owner,
            in AIActorObservation target, out float value)
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
                    if (!_targetsAvailable) { return false; }
                    _targetCounts.TryGetValue(target.actor, out int count);
                    value = count; return true;
                case TargetSortKey.DamageScore:
                    if (!_damageScoresAvailable || !owner.actor.IsValid) { return false; }
                    _scores.TryGetValue((owner.actor, target.actor), out value);
                    return true;
                case TargetSortKey.LastAttacker:
                    if (!_damageScoresAvailable || !owner.actor.IsValid) { return false; }
                    if (!_lastAttackers.TryGetValue(owner.actor, out ActorRef lastAttacker)) { return false; }
                    value = lastAttacker.Equals(target.actor) ? 1f : 0f; return true;
                case TargetSortKey.Self: value = target.actor.Equals(owner.actor) ? 1f : 0f; return true;
                case TargetSortKey.Player: value = (target.flags.Feature & CharacterFeature.Player) != 0 ? 1f : 0f; return true;
                case TargetSortKey.Sister: value = (target.flags.Feature & CharacterFeature.Companion) != 0 ? 1f : 0f; return true;
                default: return false;
            }
        }
    }
}
