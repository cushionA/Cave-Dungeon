using Game.Core;
using NUnit.Framework;
using System.Collections.Generic;

namespace Game.Tests.EditMode
{
    public class Integration_CompanionSharedMpTests
    {
        private sealed class World : IExecutionWorld
        {
            public readonly Dictionary<int, ActionActorState> actors = new Dictionary<int, ActionActorState>();
            public bool TryGetActor(int hash, out ActionActorState actor) => actors.TryGetValue(hash, out actor);
            public void CommitActor(int hash, in ActionActorState actor) => actors[hash] = actor;
            public CharacterVitals GetVitals(int hash) => actors[hash].vitals;
            public void SetMp(int hash, float value)
            {
                ActionActorState actor = actors[hash];
                actor.vitals.currentMp = value;
                actors[hash] = actor;
            }
            public void SetMaxMp(int hash, int value)
            {
                ActionActorState actor = actors[hash];
                actor.vitals.maxMp = value;
                actors[hash] = actor;
            }
        }

        private const int k_Owner = 1;
        private const int k_Other = 2;
        private World _world;
        private ActionExecutionSystem _actions;
        private CompanionMpManager _mp;

        [SetUp]
        public void SetUp()
        {
            _world = new World();
            _actions = new ActionExecutionSystem(_world);
            Register(k_Owner, 80, 100);
            Register(k_Other, 37, 60);
            _mp = new CompanionMpManager(_world, k_Owner, 100, Settings());
        }

        [TearDown]
        public void TearDown()
        {
            _mp.Dispose();
            _actions.Dispose();
        }

        private void Register(int hash, int currentMp, int maxMp)
        {
            _world.actors.Add(hash, new ActionActorState
            {
                vitals = new CharacterVitals { currentHp = 100, maxHp = 100, currentMp = currentMp, maxMp = maxMp },
                flags = CharacterFlags.Pack(CharacterBelong.Ally, 0, ActState.Neutral, AbilityFlag.None),
            });
            _actions.RegisterActor(hash);
        }

        private static CompanionMpSettings Settings() => new CompanionMpSettings
        {
            baseRecoveryRate = 5f, mpRecoverActionRate = 10f, vanishRecoveryMultiplier = 1.3f,
            returnThresholdRatio = 0.5f, maxReserveMp = 100,
        };

        [Test]
        public void CompanionSharedMp_ActionCostAndRecovery_UseOneBalanceForManagerAndExecution()
        {
            Assert.AreEqual(80f, _mp.CurrentMp);
            Assert.AreEqual(100f, _mp.MaxMp);
            _world.SetMaxMp(k_Owner, 120);
            Assert.AreEqual(120f, _mp.MaxMp);
            Assert.AreEqual(80f, _mp.CurrentMp);
            _world.SetMaxMp(k_Owner, 100);
            int action = _actions.RegisterDefinition(new ActionDefinitionData
            {
                mpCost = 20, activeDuration = 1f, cancelAfter = 0f, cancelPoint = -1f,
                endMode = ActionEndMode.Timed, actState = ActState.Attacking, resources = ActionResources.Hands,
            });
            Assert.IsTrue(_actions.TryStart(new ActionRequest { ownerHash = k_Owner, definitionId = action }, out _));
            Assert.AreEqual(60f, _mp.CurrentMp);
            _mp.Tick(1f);
            Assert.AreEqual(65f, _world.GetVitals(k_Owner).currentMp);
            Assert.AreEqual(95, _mp.ReserveMp);
            Assert.AreEqual(37, _world.GetVitals(k_Other).currentMp);
        }

        [Test]
        public void CompanionSharedMp_FractionalBarrierDamage_ConsumesSharedMpAndReturnsAtThreshold()
        {
            Assert.AreEqual(0.5f, _mp.ConsumeMp(0.5f));
            Assert.AreEqual(79.5f, _world.GetVitals(k_Owner).currentMp);
            int vanished = 0;
            int returned = 0;
            _mp.OnVanish += () => vanished++;
            _mp.OnReturn += () => returned++;
            _mp.ApplyBarrierDamage(79.5f);
            Assert.IsTrue(_mp.IsVanished);
            Assert.AreEqual(0f, _world.GetVitals(k_Owner).currentMp);
            Assert.AreEqual(1, vanished);
            for (int i = 0; i < 80; i++) { _mp.Tick(0.1f); }
            Assert.IsFalse(_mp.IsVanished);
            Assert.GreaterOrEqual(_world.GetVitals(k_Owner).currentMp, 50f);
            Assert.AreEqual(1, returned);
            Assert.AreEqual(100f, _mp.CurrentMp + _mp.ReserveMp, 0.001f);
        }

        [Test]
        public void CompanionSharedMp_ExternalDepletion_IsObservedBeforeRecovery()
        {
            int vanished = 0;
            _mp.OnVanish += () => vanished++;
            _world.SetMp(k_Owner, 0f);
            _mp.Tick(0f);
            Assert.IsTrue(_mp.IsVanished);
            Assert.AreEqual(1, vanished);
            _mp.Tick(0f);
            Assert.AreEqual(1, vanished);
        }

        [Test]
        public void CompanionSharedMp_FractionalDeficit_RecoveryDoesNotExceedMaximumOrLoseReserve()
        {
            _world.SetMp(k_Owner, 99.5f);
            float initial = _mp.CurrentMp + _mp.ReserveMp;
            for (int i = 0; i < 30; i++) { _mp.Tick(0.1f); }
            Assert.LessOrEqual(_mp.CurrentMp, 100f);
            Assert.AreEqual(100f, _mp.CurrentMp, 0.001f);
            Assert.AreEqual(99.5f, _mp.ReserveMp, 0.001f);
            Assert.AreEqual(initial, _mp.CurrentMp + _mp.ReserveMp, 0.001f);
            Assert.AreEqual(37, _world.GetVitals(k_Other).currentMp);
        }

        [Test]
        public void CompanionSharedMp_RemovedOwner_DoesNotChangeOtherActorsOrRecoverIntoReusedOwner()
        {
            _actions.RemoveActor(k_Owner);
            _world.actors.Remove(k_Owner);
            Assert.DoesNotThrow(() => _mp.Tick(1f));
            Assert.AreEqual(0f, _mp.ConsumeMp(1f));
            Assert.AreEqual(37, _world.GetVitals(k_Other).currentMp);
            _mp.Dispose();
            Register(k_Owner, 12, 100);
            _mp.Tick(1f);
            Assert.AreEqual(0f, _mp.ConsumeMp(1f));
            Assert.AreEqual(12f, _world.GetVitals(k_Owner).currentMp);
        }
    }
}
