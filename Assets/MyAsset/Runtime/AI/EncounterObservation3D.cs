using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Runtime
{
    /// <summary>コーディネーターが指定する参加個体の目撃だけを固定し、観測側の陣営で分ける。</summary>
    internal sealed class EncounterObservation3D
    {
        private readonly struct Participant
        {
            public readonly ActorRef actor;
            public readonly CharacterBelong faction;
            public readonly PhysicsScene world;
            public readonly CharacterController body;
            public readonly Vector3 bodyCenter;
            public readonly Vector3 position;

            public Participant(ActorRef actor, CharacterBelong faction, PhysicsScene world, CharacterActor3D character)
            {
                this.actor = actor;
                this.faction = faction;
                this.world = world;
                body = character.Body;
                bodyCenter = body.bounds.center;
                position = character.Motor.FeetPosition;
            }
        }

        private static readonly Comparison<Participant> s_CompareParticipants = CompareParticipants;
        private readonly List<Participant> _participants = new List<Participant>(24);
        private readonly HashSet<ActorRef> _seenParticipants = new HashSet<ActorRef>();
        private readonly HashSet<(ActorRef observer, ActorRef target)> _visiblePairs = new HashSet<(ActorRef, ActorRef)>();
        private readonly List<EncounterSighting3D> _allySightings = new List<EncounterSighting3D>(24);
        private readonly List<EncounterSighting3D> _enemySightings = new List<EncounterSighting3D>(24);
        private readonly IReadOnlyList<EncounterSighting3D> _allyView;
        private readonly IReadOnlyList<EncounterSighting3D> _enemyView;

        public EncounterObservation3D()
        {
            _allyView = _allySightings.AsReadOnly();
            _enemyView = _enemySightings.AsReadOnly();
        }

        /// <summary>同じ物理空間内の相手を、胴体間の距離と地形の遮蔽で確認する。</summary>
        public void Capture(IReadOnlyList<ActorRef> participants, float sightRange, LayerMask obstructionLayers)
        {
            _participants.Clear();
            _seenParticipants.Clear();
            _visiblePairs.Clear();
            _allySightings.Clear();
            _enemySightings.Clear();
            if (participants == null || participants.Count == 0 || !(sightRange > 0f) || !float.IsFinite(sightRange))
            {
                return;
            }

            Physics.SyncTransforms();
            for (int i = 0; i < participants.Count; i++)
            {
                ActorRef actor = participants[i];
                if (!_seenParticipants.Add(actor) || !MultiLockTargeting3D.TryGetActor(actor, out CharacterActor3D character))
                {
                    continue;
                }

                CharacterBelong faction = BattleFaction(GameManager.Data.GetFlags(actor.Hash).Belong);
                PhysicsScene world = character.gameObject.scene.GetPhysicsScene();
                if ((faction == CharacterBelong.Ally || faction == CharacterBelong.Enemy) && world.IsValid())
                {
                    _participants.Add(new Participant(actor, faction, world, character));
                }
            }

            _participants.Sort(s_CompareParticipants);
            float rangeSquared = sightRange * sightRange;
            foreach (Participant observer in _participants)
            {
                List<EncounterSighting3D> sightings = observer.faction == CharacterBelong.Ally ? _allySightings : _enemySightings;
                foreach (Participant target in _participants)
                {
                    if (observer.faction == target.faction || observer.world != target.world
                        || !HasSight(observer, target, rangeSquared, obstructionLayers))
                    {
                        continue;
                    }

                    sightings.Add(new EncounterSighting3D(observer.actor, target.actor, target.position));
                    _visiblePairs.Add((observer.actor, target.actor));
                }
            }
        }

        /// <summary>指定陣営が最後の観測で得た目撃。登録識別子の順で列挙する。</summary>
        public IReadOnlyList<EncounterSighting3D> SightingsFor(CharacterBelong faction)
        {
            switch (BattleFaction(faction))
            {
                case CharacterBelong.Ally: return _allyView;
                case CharacterBelong.Enemy: return _enemyView;
                default: return Array.Empty<EncounterSighting3D>();
            }
        }

        /// <summary>最後に固定した観測を、その時点の世代付き参照で確認する。</summary>
        public bool CanSee(ActorRef observer, ActorRef target) => _visiblePairs.Contains((observer, target));

        private static bool HasSight(in Participant observer, in Participant target, float rangeSquared, LayerMask obstructionLayers)
        {
            Vector3 offset = target.bodyCenter - observer.bodyCenter;
            float distanceSquared = offset.sqrMagnitude;
            if (!float.IsFinite(distanceSquared) || distanceSquared > rangeSquared) { return false; }
            if (distanceSquared <= 0f) { return true; }

            float distance = Mathf.Sqrt(distanceSquared);
            return !observer.world.Raycast(observer.bodyCenter, offset / distance, out RaycastHit hit, distance,
                obstructionLayers, QueryTriggerInteraction.Ignore) || hit.collider == target.body;
        }

        private static CharacterBelong BattleFaction(CharacterBelong belong) => belong & (CharacterBelong.Ally | CharacterBelong.Enemy);

        private static int CompareParticipants(Participant left, Participant right)
        {
            int hashOrder = left.actor.Hash.CompareTo(right.actor.Hash);
            return hashOrder != 0 ? hashOrder : left.actor.Generation.CompareTo(right.actor.Generation);
        }
    }
}
