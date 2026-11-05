using NUnit.Framework;
using Game.Core;
using System.Collections.Generic;

namespace Game.Tests.EditMode
{
    public class CompanionAI_MpManagerTests
    {
        private sealed class World : IExecutionWorld
        {
            public readonly Dictionary<int, ActionActorState> actors = new Dictionary<int, ActionActorState>();
            public bool TryGetActor(int hash, out ActionActorState actor) => actors.TryGetValue(hash, out actor);
            public void CommitActor(int hash, in ActionActorState actor) => actors[hash] = actor;
        }

        private const int k_Owner = 1;
        private World _world;

        [SetUp]
        public void SetUp()
        {
            _world = new World();
            _world.actors.Add(k_Owner, new ActionActorState { vitals = new CharacterVitals { currentMp = 100, maxMp = 100 } });
        }

        private CompanionMpSettings DefaultSettings()
        {
            return new CompanionMpSettings
            {
                baseRecoveryRate = 5f,
                mpRecoverActionRate = 10f,
                vanishRecoveryMultiplier = 1.3f,
                returnThresholdRatio = 0.5f,
                maxReserveMp = 100
            };
        }

        [Test]
        public void MpManager_Initialize_FullMp()
        {
            CompanionMpManager mgr = new CompanionMpManager(_world, k_Owner, 50, DefaultSettings());

            Assert.AreEqual(100f, mgr.CurrentMp, 0.01f);
            Assert.AreEqual(100f, mgr.MaxMp, 0.01f);
            Assert.AreEqual(50, mgr.ReserveMp);
            Assert.IsFalse(mgr.IsVanished);
            Assert.IsFalse(mgr.IsRecovering);
        }

        [Test]
        public void MpManager_ConsumeMp_ReducesCurrent()
        {
            CompanionMpManager mgr = new CompanionMpManager(_world, k_Owner, 50, DefaultSettings());

            float consumed = mgr.ConsumeMp(30f);

            Assert.AreEqual(30f, consumed, 0.01f);
            Assert.AreEqual(70f, mgr.CurrentMp, 0.01f);
        }

        [TestCase(19.5f, 100, 0.25f, 19.25f, 19)]
        [TestCase(40f, 100, 100f, 0f, 0)]
        [TestCase(0f, 0, 1f, 0f, 0)]
        public void MpManager_ConsumeMp_UpdatesSharedRatio(float currentMp, int maxMp,
            float cost, float expectedMp, int expectedRatio)
        {
            ActionActorState actor = _world.actors[k_Owner];
            actor.vitals.currentMp = currentMp;
            actor.vitals.maxMp = maxMp;
            _world.actors[k_Owner] = actor;
            CompanionMpManager mgr = new CompanionMpManager(_world, k_Owner, 50, DefaultSettings());

            mgr.ConsumeMp(cost);

            Assert.AreEqual(expectedMp, _world.actors[k_Owner].vitals.currentMp);
            Assert.AreEqual(expectedRatio, _world.actors[k_Owner].vitals.mpRatio);
        }

        [Test]
        public void MpManager_VanishAndReturn_PublishSynchronizedSharedRatios()
        {
            ActionActorState actor = _world.actors[k_Owner];
            actor.vitals.currentMp = 0f;
            _world.actors[k_Owner] = actor;
            CompanionMpManager mgr = new CompanionMpManager(_world, k_Owner, 100, DefaultSettings());
            int vanished = 0;
            int returned = 0;
            mgr.OnVanish += () =>
            {
                vanished++;
                Assert.AreEqual(0, _world.actors[k_Owner].vitals.mpRatio);
            };
            mgr.OnReturn += () =>
            {
                returned++;
                Assert.AreEqual(52, _world.actors[k_Owner].vitals.mpRatio);
            };

            mgr.Tick(8f);

            Assert.AreEqual(1, vanished);
            Assert.AreEqual(1, returned);
            Assert.AreEqual(52f, mgr.CurrentMp);
            Assert.AreEqual(52, _world.actors[k_Owner].vitals.mpRatio);
            Assert.AreEqual(100f, mgr.CurrentMp + mgr.ReserveMp);
        }

        [Test]
        public void MpManager_ConsumeMp_WhenInsufficient_ConsumesRemaining()
        {
            CompanionMpManager mgr = new CompanionMpManager(_world, k_Owner, 50, DefaultSettings());
            mgr.ConsumeMp(80f);

            float consumed = mgr.ConsumeMp(30f);

            Assert.AreEqual(20f, consumed, 0.01f);
            Assert.AreEqual(0f, mgr.CurrentMp, 0.01f);
        }

        [Test]
        public void MpManager_SharedVanishState_IsCommittedBeforeVanishAndReturnNotifications()
        {
            ActionActorState actor = _world.actors[k_Owner];
            actor.vitals.currentMp = 0f;
            _world.actors[k_Owner] = actor;
            using CompanionMpManager manager = new CompanionMpManager(_world, k_Owner, 100, DefaultSettings());
            int vanished = 0;
            int returned = 0;
            manager.OnVanish += () =>
            {
                Assert.IsTrue(_world.actors[k_Owner].flags.IsVanished);
                vanished++;
            };
            manager.OnReturn += () =>
            {
                Assert.IsFalse(_world.actors[k_Owner].flags.IsVanished);
                returned++;
            };

            manager.Tick(0f);
            Assert.IsTrue(manager.IsVanished);
            manager.Tick(8f);

            Assert.IsFalse(manager.IsVanished);
            Assert.AreEqual(1, vanished);
            Assert.AreEqual(1, returned);
        }

        [Test]
        public void MpManager_ExternalSharedVanishState_IsTheOnlyPresenceAuthority()
        {
            using CompanionMpManager manager = new CompanionMpManager(_world, k_Owner, 50, DefaultSettings());
            ActionActorState actor = _world.actors[k_Owner];
            actor.flags.IsVanished = true;
            _world.actors[k_Owner] = actor;
            Assert.IsTrue(manager.IsVanished);
            Assert.AreEqual(0f, manager.ConsumeMp(10f));

            actor.flags.IsVanished = false;
            _world.actors[k_Owner] = actor;
            Assert.IsFalse(manager.IsVanished);
            Assert.AreEqual(10f, manager.ConsumeMp(10f));
        }

        [Test]
        public void MpManager_ConsumeMp_ZeroOrNegative_ReturnsZero()
        {
            ActionActorState actor = _world.actors[k_Owner];
            _world.actors[k_Owner] = actor;
            CompanionMpManager mgr = new CompanionMpManager(_world, k_Owner, 50, DefaultSettings());

            Assert.AreEqual(0f, mgr.ConsumeMp(0f));
            Assert.AreEqual(0f, mgr.ConsumeMp(-5f));
            Assert.AreEqual(100f, mgr.CurrentMp, 0.01f);
            Assert.AreEqual(100, _world.actors[k_Owner].vitals.mpRatio);
        }

        [Test]
        public void MpManager_ApplyBarrierDamage_TriggersVanish_WhenMpZero()
        {
            CompanionMpManager mgr = new CompanionMpManager(_world, k_Owner, 50, DefaultSettings());
            bool vanished = false;
            mgr.OnVanish += () => vanished = true;

            mgr.ApplyBarrierDamage(100f);

            Assert.IsTrue(vanished);
            Assert.IsTrue(mgr.IsVanished);
            Assert.AreEqual(0f, mgr.CurrentMp, 0.01f);
        }

        [Test]
        public void MpManager_ApplyBarrierDamage_NoVanish_WhenMpRemains()
        {
            CompanionMpManager mgr = new CompanionMpManager(_world, k_Owner, 50, DefaultSettings());
            bool vanished = false;
            mgr.OnVanish += () => vanished = true;

            mgr.ApplyBarrierDamage(50f);

            Assert.IsFalse(vanished);
            Assert.IsFalse(mgr.IsVanished);
            Assert.AreEqual(50f, mgr.CurrentMp, 0.01f);
        }

        [Test]
        public void MpManager_Tick_RecoversMp_FromReserve()
        {
            CompanionMpManager mgr = new CompanionMpManager(_world, k_Owner, 50, DefaultSettings());
            mgr.ConsumeMp(50f); // currentMP = 50

            mgr.Tick(1f); // baseRecoveryRate=5 → +5

            Assert.AreEqual(55f, mgr.CurrentMp, 0.5f);
            Assert.IsTrue(mgr.ReserveMp < 50); // reserve consumed
        }

        [Test]
        public void MpManager_Tick_NoRecovery_WhenReserveEmpty()
        {
            CompanionMpManager mgr = new CompanionMpManager(_world, k_Owner, 0, DefaultSettings());
            mgr.ConsumeMp(50f);

            mgr.Tick(1f);

            Assert.AreEqual(50f, mgr.CurrentMp, 0.01f);
        }

        [Test]
        public void MpManager_MpRecoveryAction_IncreasesRate()
        {
            CompanionMpManager mgr = new CompanionMpManager(_world, k_Owner, 100, DefaultSettings());
            mgr.ConsumeMp(80f); // currentMP = 20

            mgr.StartMpRecovery();
            Assert.IsTrue(mgr.IsRecovering);

            mgr.Tick(1f); // (5 + 10) * 1 = 15

            Assert.AreEqual(35f, mgr.CurrentMp, 0.5f);
        }

        [Test]
        public void MpManager_StopMpRecovery_StopsAcceleration()
        {
            CompanionMpManager mgr = new CompanionMpManager(_world, k_Owner, 100, DefaultSettings());
            mgr.ConsumeMp(80f);
            mgr.StartMpRecovery();
            mgr.StopMpRecovery();

            Assert.IsFalse(mgr.IsRecovering);

            mgr.Tick(1f); // baseRecoveryRate=5 only

            Assert.AreEqual(25f, mgr.CurrentMp, 0.5f);
        }

        [Test]
        public void MpManager_VanishState_SkipsConsume()
        {
            CompanionMpManager mgr = new CompanionMpManager(_world, k_Owner, 50, DefaultSettings());
            mgr.ApplyBarrierDamage(100f);
            Assert.IsTrue(mgr.IsVanished);

            float consumed = mgr.ConsumeMp(10f);

            Assert.AreEqual(0f, consumed);
        }

        [Test]
        public void MpManager_VanishState_RecoveriesWithMultiplier()
        {
            CompanionMpSettings settings = DefaultSettings();
            CompanionMpManager mgr = new CompanionMpManager(_world, k_Owner, 100, settings);
            mgr.ApplyBarrierDamage(100f);

            mgr.Tick(1f); // 5 * 1.3 = 6.5

            Assert.AreEqual(6.5f, mgr.CurrentMp, 0.5f);
        }

        [Test]
        public void MpManager_Return_WhenThresholdReached()
        {
            CompanionMpSettings settings = DefaultSettings();
            CompanionMpManager mgr = new CompanionMpManager(_world, k_Owner, 100, settings);
            mgr.ApplyBarrierDamage(100f);

            bool returned = false;
            mgr.OnReturn += () => returned = true;

            // threshold = 50%. Recovery = 5*1.3 = 6.5/s. Need 50 MP → ~7.7s
            for (int i = 0; i < 80; i++)
            {
                mgr.Tick(0.1f); // 80 * 0.1 = 8s total
            }

            Assert.IsTrue(returned);
            Assert.IsFalse(mgr.IsVanished);
            Assert.GreaterOrEqual(mgr.CurrentMp, 50f * 0.99f);
        }

        [Test]
        public void MpManager_RestoreReserveMp_ClampsToMax()
        {
            CompanionMpManager mgr = new CompanionMpManager(_world, k_Owner, 80, DefaultSettings());

            mgr.RestoreReserveMp(50);

            Assert.AreEqual(100, mgr.ReserveMp);
        }

        [Test]
        public void MpManager_RestoreReserveMp_NegativeAmount_NoEffect()
        {
            CompanionMpManager mgr = new CompanionMpManager(_world, k_Owner, 50, DefaultSettings());

            mgr.RestoreReserveMp(-10);

            Assert.AreEqual(50, mgr.ReserveMp);
        }

        [Test]
        public void MpManager_VanishState_StopsMpRecoveryAction()
        {
            CompanionMpManager mgr = new CompanionMpManager(_world, k_Owner, 50, DefaultSettings());
            mgr.StartMpRecovery();
            Assert.IsTrue(mgr.IsRecovering);

            mgr.ApplyBarrierDamage(100f);

            Assert.IsFalse(mgr.IsRecovering);
        }

        [Test]
        public void MpManager_StartMpRecovery_WhenVanished_DoesNotStart()
        {
            CompanionMpManager mgr = new CompanionMpManager(_world, k_Owner, 50, DefaultSettings());
            mgr.ApplyBarrierDamage(100f);

            mgr.StartMpRecovery();

            Assert.IsFalse(mgr.IsRecovering);
        }

        [Test]
        public void MpManager_Tick_ReserveToCurrentTransfer_PreservesTotalMp()
        {
            CompanionMpManager mgr = new CompanionMpManager(_world, k_Owner, 50, DefaultSettings());
            mgr.ConsumeMp(50f); // currentMP=50, reserveMP=50, total=100

            float initialTotal = mgr.CurrentMp + mgr.ReserveMp;
            Assert.AreEqual(100f, initialTotal, 0.01f);

            for (int i = 0; i < 500; i++)
            {
                mgr.Tick(0.01f);
            }

            float finalTotal = mgr.CurrentMp + mgr.ReserveMp;
            Assert.That(finalTotal, Is.EqualTo(initialTotal).Within(0.001f),
                "細かいTickで reserve→current 転送時に MP 総和が消失してはならない");
        }
    }
}
