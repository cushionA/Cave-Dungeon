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

        internal AIActorObservation(ActorRef actor, Vector2 position, CharacterFlags flags, int currentHp,
            float hpPercent, float hpFraction, float mpPercent, float staminaPercent, float armorPercent,
            int attackPower, int defensePower, Element weakPoints)
        {
            this.actor = actor; this.position = position; this.flags = flags; this.currentHp = currentHp;
            this.hpPercent = hpPercent; this.hpFraction = hpFraction; this.mpPercent = mpPercent;
            this.staminaPercent = staminaPercent; this.armorPercent = armorPercent;
            this.attackPower = attackPower; this.defensePower = defensePower; this.weakPoints = weakPoints;
        }

        /// <summary>現在値をAIの判定に必要な値だけへ投影し、次のsnapshotまで固定する。</summary>
        public static AIActorObservation Capture(ActorRef actor, in CharacterVitals vitals,
            in CombatStats combat, in CharacterFlags flags) => new AIActorObservation(actor, vitals, combat, flags);
    }

    /// <summary>同じ固定tickの判断器が共有する値snapshot。取得していない情報はfalseで返す。</summary>
    public sealed class AIObservationContext
    {
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

        internal bool DamageScoresAvailable => _damageScoresAvailable;
        internal bool TargetRelationsAvailable => _targetsAvailable;

        internal bool TryGetLastAttacker(int ownerHash, out ActorRef attacker)
        {
            attacker = default;
            return _damageScoresAvailable && TryGetActor(ownerHash, out AIActorObservation owner)
                && _lastAttackers.TryGetValue(owner.actor, out attacker);
        }

        internal void CopyDamageScores(List<KeyValuePair<(ActorRef owner, ActorRef source), float>> output)
        {
            output.Clear();
            foreach (KeyValuePair<(ActorRef owner, ActorRef source), float> score in _scores)
            {
                output.Add(score);
            }
        }

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
            AIManagedWorld world = new AIManagedWorld { observation = this };
            return AIQueryKernel.MatchesFilter(filter, ownerHash, candidate.actor.Hash, ref world);
        }

        internal bool Matches(in TargetFilter filter, in AIActorObservation owner, in AIActorObservation candidate)
            => Matches(filter, owner.actor.Hash, candidate);

        public bool TryGetConditionValue(in AICondition condition, int ownerHash, int targetHash, out float value)
        {
            AIManagedWorld world = new AIManagedWorld { observation = this };
            return AIQueryKernel.TryConditionValue(condition, ownerHash, targetHash, ref world, out value);
        }

        public bool TryGetSortValue(TargetSortKey key, int ownerHash, int targetHash, out float value)
        {
            AIManagedWorld world = new AIManagedWorld { observation = this };
            return AIQueryKernel.TrySortValue(key, ownerHash, targetHash, ref world, out value);
        }

        internal bool TryGetSortValue(TargetSortKey key, in AIActorObservation owner,
            in AIActorObservation target, out float value)
        {
            return TryGetSortValue(key, owner.actor.Hash, target.actor.Hash, out value);
        }
    }
}
