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
        public readonly PhysicsScene2D physicsScene;
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
            in CombatStats combat, in CharacterFlags flags, PhysicsScene2D physicsScene)
        {
            this.actor = actor;
            this.physicsScene = physicsScene;
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
            weakPoints = GetWeakPoints(combat.defense);
        }

        internal static Element GetWeakPoints(in ElementalStatus defense)
        {
            // 弱点の現行契約は「指定属性の防御値が0以下」。作者の弱点設定との統合は別タスク。
            Element weak = default;
            if (defense.slash <= 0) { weak |= Element.Slash; }
            if (defense.strike <= 0) { weak |= Element.Strike; }
            if (defense.pierce <= 0) { weak |= Element.Pierce; }
            if (defense.fire <= 0) { weak |= Element.Fire; }
            if (defense.thunder <= 0) { weak |= Element.Thunder; }
            if (defense.light <= 0) { weak |= Element.Light; }
            if (defense.dark <= 0) { weak |= Element.Dark; }
            return weak;
        }

        internal AIActorObservation(ActorRef actor, Vector2 position, CharacterFlags flags, int currentHp,
            float hpPercent, float hpFraction, float mpPercent, float staminaPercent, float armorPercent,
            int attackPower, int defensePower, Element weakPoints, PhysicsScene2D physicsScene)
        {
            this.physicsScene = physicsScene;
            this.actor = actor; this.position = position; this.flags = flags; this.currentHp = currentHp;
            this.hpPercent = hpPercent; this.hpFraction = hpFraction; this.mpPercent = mpPercent;
            this.staminaPercent = staminaPercent; this.armorPercent = armorPercent;
            this.attackPower = attackPower; this.defensePower = defensePower; this.weakPoints = weakPoints;
        }

        /// <summary>現在値をAIの判定に必要な値だけへ投影し、次のsnapshotまで固定する。</summary>
        public static AIActorObservation Capture(ActorRef actor, in CharacterVitals vitals,
            in CombatStats combat, in CharacterFlags flags, PhysicsScene2D physicsScene = default) =>
            new AIActorObservation(actor, vitals, combat, flags, physicsScene);
    }

    /// <summary>同じ固定tickの判断器が共有する値snapshot。取得していない情報はfalseで返す。</summary>
    public sealed class AIObservationContext
    {
        private readonly Dictionary<int, AIActorObservation> _actors = new Dictionary<int, AIActorObservation>();
        private readonly List<int> _hashes = new List<int>();
        private readonly Dictionary<(ActorRef owner, ActorRef source), float> _scores = new Dictionary<(ActorRef, ActorRef), float>();
        private readonly Dictionary<ActorRef, ActorRef> _lastAttackers = new Dictionary<ActorRef, ActorRef>();
        private readonly Dictionary<ActorRef, ActorRef> _targets = new Dictionary<ActorRef, ActorRef>();
        private readonly Dictionary<ActorRef, float> _stunRemaining = new Dictionary<ActorRef, float>();
        private readonly Dictionary<(ActorRef owner, ActorRef target), bool> _targetLineOfSight = new();
        private readonly Dictionary<ActorRef, int> _targetCounts = new Dictionary<ActorRef, int>();
        private readonly List<AISceneGimmick> _gimmicks = new List<AISceneGimmick>();
        private readonly Dictionary<int, int> _gimmickIndices = new Dictionary<int, int>();
        private readonly Dictionary<ActorRef, Vector2> _gimmickVelocities = new Dictionary<ActorRef, Vector2>();
        private readonly HashSet<(ActorRef observer, int id, int generation)> _visibleGimmicks =
            new HashSet<(ActorRef, int, int)>();
        internal bool GimmicksAvailable { get; private set; }
        internal int GimmickCount => _gimmicks.Count;
        internal AISceneGimmick GimmickAt(int index) => _gimmicks[index];
        private bool _damageScoresAvailable;
        private bool _targetsAvailable;

        public ActorRef ProtectedPlayer { get; private set; }
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
            _actors.Clear(); _hashes.Clear(); _scores.Clear(); _lastAttackers.Clear();
            _targets.Clear(); _targetCounts.Clear(); _stunRemaining.Clear(); _targetLineOfSight.Clear();
            ProtectedPlayer = default;
            _gimmicks.Clear();
            _gimmickIndices.Clear();
            _visibleGimmicks.Clear();
            _gimmickVelocities.Clear();
            GimmicksAvailable = false;
            _damageScoresAvailable = false;
            _targetsAvailable = false;
        }

        /// <summary>このtickの装置照会を開始する。0件を観測済みとして返せる。</summary>
        public void BeginGimmickObservation()
        {
            _gimmicks.Clear();
            _gimmickIndices.Clear();
            _visibleGimmicks.Clear();
            GimmicksAvailable = true;
        }

        public void AddGimmick(in AISceneGimmick gimmick)
        {
            if (!GimmicksAvailable || !gimmick.IsValid) { return; }
            if (_gimmickIndices.TryGetValue(gimmick.id, out int index))
            {
                _gimmicks[index] = gimmick;
                return;
            }
            _gimmickIndices.Add(gimmick.id, _gimmicks.Count);
            _gimmicks.Add(gimmick);
        }

        public bool TryGetGimmick(int id, int generation, out AISceneGimmick gimmick)
        {
            if (_gimmickIndices.TryGetValue(id, out int index) && _gimmicks[index].generation == generation)
            {
                gimmick = _gimmicks[index];
                return true;
            }
            gimmick = default;
            return false;
        }

        public void SetGimmickOperationVisible(ActorRef observer, in AISceneGimmick gimmick)
        {
            if (TryGetActor(observer.Hash, out AIActorObservation actor) && actor.actor == observer
                && TryGetGimmick(gimmick.id, gimmick.generation, out AISceneGimmick captured)
                && captured.requiresClearOperation && captured.physicsScene == actor.physicsScene)
            {
                _visibleGimmicks.Add((observer, captured.id, captured.generation));
            }
        }

        public bool IsGimmickOperationVisible(ActorRef observer, in AISceneGimmick gimmick) =>
            TryGetActor(observer.Hash, out AIActorObservation actor) && actor.actor == observer
                && TryGetGimmick(gimmick.id, gimmick.generation, out AISceneGimmick captured)
                && captured.physicsScene == actor.physicsScene && (!captured.requiresClearOperation || _visibleGimmicks.Contains((observer, captured.id, captured.generation)));

        internal void CopyVisibleGimmicks(List<(int observerHash, int gimmickId)> output)
        {
            output.Clear();
            foreach (var pair in _visibleGimmicks)
            {
                // Native側の組キーはhash/idだけ。捕捉中に世代交代した組はここで除く。
                if (Contains(pair.observer) && TryGetGimmick(pair.id, pair.generation, out _))
                {
                    output.Add((pair.observer.Hash, pair.id));
                }
            }
        }

        public void SetGimmickVelocity(ActorRef actor, Vector2 velocity)
        {
            if (!Contains(actor)) { return; }
            if (float.IsNaN(velocity.x) || float.IsInfinity(velocity.x)
                || float.IsNaN(velocity.y) || float.IsInfinity(velocity.y))
            {
                _gimmickVelocities.Remove(actor);
                return;
            }
            _gimmickVelocities[actor] = velocity;
        }

        public bool TryGetGimmickVelocity(ActorRef actor, out Vector2 velocity)
        {
            velocity = default;
            return Contains(actor) && _gimmickVelocities.TryGetValue(actor, out velocity);
        }

        public void AddActor(in AIActorObservation actor)
        {
            if (!actor.actor.IsValid) { return; }
            if (!_actors.TryGetValue(actor.actor.Hash, out AIActorObservation previous))
            {
                _hashes.Add(actor.actor.Hash);
            }
            else if (previous.actor != actor.actor)
            {
                ForgetTargetOf(previous.actor);
                if (_targetCounts.Remove(previous.actor))
                {
                    foreach (int hash in _hashes)
                    {
                        ActorRef owner = _actors[hash].actor;
                        if (_targets.TryGetValue(owner, out ActorRef target) && target == previous.actor) { _targets.Remove(owner); }
                    }
                }
                _stunRemaining.Remove(previous.actor);
            }
            _actors[actor.actor.Hash] = actor;
            if (ProtectedPlayer.Hash == actor.actor.Hash && (ProtectedPlayer != actor.actor || !IsLiving(actor)))
            {
                ProtectedPlayer = default;
            }
        }

        public bool TryGetActor(int hash, out AIActorObservation actor) => _actors.TryGetValue(hash, out actor);
        public bool Contains(ActorRef actor) => actor.IsValid && _actors.TryGetValue(actor.Hash, out AIActorObservation value) && value.actor.Equals(actor);
        public void SetDamageScoresAvailable() => _damageScoresAvailable = true;
        public void SetTargetRelationsAvailable() => _targetsAvailable = true;

        public void SetProtectedPlayer(ActorRef player)
        {
            ProtectedPlayer = TryGetActor(player.Hash, out AIActorObservation actor) && actor.actor == player
                && IsLiving(actor) ? player : default;
        }

        private static bool IsLiving(in AIActorObservation actor) =>
            actor.currentHp > 0 && actor.flags.ActState != ActState.Dead && !actor.flags.IsVanished;

        public void SetStunRemaining(ActorRef actor, float seconds)
        {
            if (!Contains(actor)) { return; }
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f) { _stunRemaining.Remove(actor); }
            else { _stunRemaining[actor] = seconds; }
        }

        public bool TryGetStunRemaining(ActorRef actor, out float seconds)
        {
            seconds = 0f;
            return Contains(actor) && _stunRemaining.TryGetValue(actor, out seconds);
        }

        public void SetTargetLineOfSight(ActorRef owner, ActorRef target, bool visible)
        {
            if (!TryGetActor(owner.Hash, out AIActorObservation observer) || observer.actor != owner
                || !TryGetActor(target.Hash, out AIActorObservation candidate) || candidate.actor != target
                || observer.physicsScene != candidate.physicsScene) { return; }
            _targetLineOfSight[(owner, target)] = visible;
        }

        public bool TryGetTargetLineOfSight(ActorRef owner, ActorRef target, out bool visible)
        {
            visible = false;
            return Contains(owner) && Contains(target) && _targetLineOfSight.TryGetValue((owner, target), out visible);
        }

        internal void CopyTargetLineOfSight(List<KeyValuePair<(ActorRef owner, ActorRef target), bool>> output)
        {
            output.Clear();
            foreach (var pair in _targetLineOfSight)
            {
                if (Contains(pair.Key.owner) && Contains(pair.Key.target)) { output.Add(pair); }
            }
        }

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

        internal void BeginTargetRelations()
        {
            _targets.Clear();
            _targetCounts.Clear();
            SetTargetRelationsAvailable();
        }

        /// <summary>所有者ごとに1件を更新する。defaultは観測済みの「標的なし」、観測外の標的は不明。</summary>
        public void RecordTarget(ActorRef owner, ActorRef target)
        {
            if (!Contains(owner)) { return; }
            if (_targets.TryGetValue(owner, out ActorRef previous) && previous == target) { return; }
            ForgetTargetOf(owner);
            if (target != default && !Contains(target)) { return; }
            _targets[owner] = target;
            if (!target.IsValid) { return; }
            _targetCounts.TryGetValue(target, out int count);
            _targetCounts[target] = count + 1;
        }

        private void ForgetTargetOf(ActorRef owner)
        {
            if (!_targets.TryGetValue(owner, out ActorRef previous)) { return; }
            _targets.Remove(owner);
            if (!previous.IsValid) { return; }
            int count = _targetCounts[previous];
            if (count == 1) { _targetCounts.Remove(previous); }
            else { _targetCounts[previous] = count - 1; }
        }

        public bool TryGetTargetOf(ActorRef owner, out ActorRef target)
        {
            target = default;
            if (!_targetsAvailable || !Contains(owner) || !_targets.TryGetValue(owner, out ActorRef captured)) { return false; }
            target = captured;
            return true;
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
            if (!Contains(candidate.actor)) { return false; }
            AIManagedWorld world = new AIManagedWorld { observation = this };
            return AIQueryKernel.MatchesFilter(filter, ownerHash, candidate.actor.Hash, ref world);
        }

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
    }
}
