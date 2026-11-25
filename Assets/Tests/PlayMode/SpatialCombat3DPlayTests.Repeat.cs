using System;
using System.Collections;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.PlayMode
{
    public sealed partial class SpatialCombat3DPlayTests
    {
        [UnityTest]
        public IEnumerator RepeatingMelee3D_KeepsOneTargetAndHonorsIntervalAndQuota()
        {
            CharacterActor3D attacker = CreateActor(_room, "Knight", new Vector3(0f, 0.1f, 0f), CharacterBelong.Ally);
            CharacterActor3D first = CreateActor(_room, "First target", new Vector3(-0.6f, 0.1f, 2f));
            CharacterActor3D entrant = CreateActor(_room, "Nearer entrant", new Vector3(3f, 0.1f, 1f));
            yield return null;
            SynchronizeWorld(_room);

            AttackInfo attack = CreateRepeatAttack(AttackShape3D.Melee);
            Assert.That(GameManager.Actions.Execute3D(attacker, attack, out long id), Is.True);
            Step(0.22f);
            AssertSingleHit(attacker, first, id);

            entrant.transform.position = new Vector3(0.6f, 0.1f, 1f);
            SynchronizeWorld(_room);
            Step(0.05f);
            Assert.That(_hits, Has.Count.EqualTo(1));
            Step(0.06f);
            Assert.That(_hits, Has.Count.EqualTo(2));
            AssertRepeatHit(1, attacker, first, id);

            Step(0.2f);
            Step(0.4f);
            Assert.That(_hits, Has.Count.EqualTo(2));
            AssertHealthy(entrant);
        }

        [UnityTest]
        public IEnumerator RepeatingMarkedMelee3D_StopsDispatchWhenDamageCallbackInterruptsCurrentAction()
        {
            CharacterActor3D attacker = CreateActor(_room, "Knight", new Vector3(0f, 0.1f, 0f), CharacterBelong.Ally);
            CharacterActor3D first = CreateActor(_room, "First mark", new Vector3(-0.7f, 0.1f, 1.8f));
            CharacterActor3D second = CreateActor(_room, "Second mark", new Vector3(0.7f, 0.1f, 1.8f));
            CharacterActor3D unmarked = CreateActor(_room, "Unmarked", new Vector3(0f, 0.1f, 0.9f));
            yield return null;
            SynchronizeWorld(_room);

            AttackInfo attack = CreateRepeatAttack(AttackShape3D.Melee);
            attack.multiLockRange = 5f;
            ActorRef[] marks = { first.Actor, second.Actor };
            Assert.That(GameManager.Actions.Execute3D(attacker, attack, marks, out long id), Is.True);
            Step(0.22f);
            Assert.That(_hits, Has.Count.EqualTo(2));
            AssertRepeatHit(0, attacker, first, id);
            AssertRepeatHit(1, attacker, second, id);

            bool interrupted = false;
            Action<DamageResult, int, int> cancelOnHit = (result, _, _) =>
            {
                if (result.actionInstanceId == id && result.defenderRef == first.Actor)
                {
                    interrupted = GameManager.Actions.Execution.Cancel(id, force: true);
                }
            };
            _events.OnDamageDealtEvent += cancelOnHit;
            try
            {
                Step(0.11f);
            }
            finally
            {
                _events.OnDamageDealtEvent -= cancelOnHit;
            }
            Assert.That(interrupted, Is.True);
            Assert.That(_hits, Has.Count.EqualTo(3));
            AssertRepeatHit(2, attacker, first, id);
            Step(0.3f);
            Assert.That(_hits, Has.Count.EqualTo(3));

            Assert.That(GameManager.Actions.Execute3D(attacker, attack, marks, out long nextId), Is.True);
            Assert.That(nextId, Is.Not.EqualTo(id));
            Step(0.22f);
            Assert.That(_hits, Has.Count.EqualTo(5));
            AssertRepeatHit(3, attacker, first, nextId);
            AssertRepeatHit(4, attacker, second, nextId);
            AssertHealthy(unmarked);
        }

        [UnityTest]
        public IEnumerator RepeatingGroundArea3D_AdmitsNewBridgeOccupantsAndClearsWarningAtRecovery()
        {
            BoxCollider lower = CreateAttackFloor("Lower floor", 0f);
            BoxCollider bridge = CreateAttackFloor("Bridge", k_BridgeHeight);
            CharacterActor3D attacker = CreateActor(_room, "Caster", new Vector3(0f, 4.12f, 0f));
            CharacterActor3D first = CreateActor(_room, "On bridge", new Vector3(1f, 4.12f, 0f), CharacterBelong.Ally);
            CharacterActor3D entrant = CreateActor(_room, "Approaching", new Vector3(-4f, 4.12f, 0f), CharacterBelong.Ally);
            CharacterActor3D below = CreateActor(_room, "Below", new Vector3(1f, 0.12f, 0f), CharacterBelong.Ally);
            yield return null;
            Settle(attacker, first, entrant, below);
            Assert.That(first.Motor.Ground.Surface, Is.SameAs(bridge));
            Assert.That(below.Motor.Ground.Surface, Is.SameAs(lower));

            AttackInfo attack = CreateRepeatAttack(AttackShape3D.GroundArea);
            Assert.That(GameManager.Actions.Execute3D(attacker, attack, out long id), Is.True);
            GameObject warning = VisibleWarning();
            Assert.That(warning, Is.Not.Null);
            Step(0.22f);
            AssertSingleHit(attacker, first, id);
            Assert.That(VisibleWarning(), Is.SameAs(warning));

            entrant.transform.position = new Vector3(-1f, 4.12f, 0f);
            Settle(entrant);
            Assert.That(entrant.Motor.Ground.Surface, Is.SameAs(bridge));
            Step(0.11f);
            Assert.That(_hits, Has.Count.EqualTo(3));
            Assert.That(_hits.FindAll(hit => hit.result.defenderRef == first.Actor), Has.Count.EqualTo(2));
            Assert.That(_hits.FindAll(hit => hit.result.defenderRef == entrant.Actor), Has.Count.EqualTo(1));
            Assert.That(_hits.FindAll(hit => hit.result.actionInstanceId == id), Has.Count.EqualTo(3));
            Assert.That(VisibleWarning(), Is.SameAs(warning));

            Step(0.11f);
            Assert.That(_hits, Has.Count.EqualTo(4));
            AssertRepeatHit(3, attacker, entrant, id);
            Step(0.44f);
            Assert.That(GameManager.Actions.Execution.TryGetInstance(id, out ActionExecution current), Is.True);
            Assert.That(current.phase, Is.EqualTo(AnimationPhase.Recovery));
            Assert.That(VisibleWarning(), Is.Null);
            Assert.That(_hits, Has.Count.EqualTo(4));
            AssertHealthy(below);
        }

        private AttackInfo CreateRepeatAttack(AttackShape3D shape)
        {
            AttackInfo attack = CreateAttack(shape);
            attack.motion.info.activeMotionDuration = 0.65f;
            attack.repeatHits = new HitRepeatSettings { maxHitsPerTarget = 2, interval = 0.1f };
            return attack;
        }

        private void AssertRepeatHit(int index, CharacterActor3D attacker, CharacterActor3D defender, long id)
        {
            DamageResult result = _hits[index].result;
            Assert.That(result.actionInstanceId, Is.EqualTo(id));
            Assert.That(result.attackerRef, Is.EqualTo(attacker.Actor));
            Assert.That(result.defenderRef, Is.EqualTo(defender.Actor));
            Assert.That(result.totalDamage, Is.GreaterThan(0));
        }
    }
}
