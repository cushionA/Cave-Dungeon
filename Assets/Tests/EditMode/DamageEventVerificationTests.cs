using NUnit.Framework;
using Game.Core;
using R3;

namespace Game.Tests.EditMode
{
    [TestFixture]
    public class DamageEventVerificationTests
    {
        private GameEvents _events;

        [SetUp]
        public void SetUp()
        {
            _events = new GameEvents();
        }

        [TearDown]
        public void TearDown()
        {
            _events.Dispose();
        }

        // --- DamageResult構造体にクリティカル・状態異常が正しく含まれるか ---

        [Test]
        public void DamageResult_ContainsCriticalInfo()
        {
            DamageResult result = new DamageResult
            {
                totalDamage = 150,
                isCritical = true,
                appliedEffect = StatusEffectId.None,
                guardResult = GuardResult.NoGuard,
                hitReaction = HitReaction.Flinch,
                isKill = false
            };

            Assert.IsTrue(result.isCritical, "isCriticalが正しく設定されるべき");
            Assert.AreEqual(150, result.totalDamage);
        }

        [Test]
        public void DamageResult_ContainsStatusEffectInfo()
        {
            DamageResult result = new DamageResult
            {
                totalDamage = 80,
                isCritical = false,
                appliedEffect = StatusEffectId.Poison,
                guardResult = GuardResult.NoGuard,
                hitReaction = HitReaction.Flinch,
                isKill = false
            };

            Assert.AreEqual(StatusEffectId.Poison, result.appliedEffect,
                "appliedEffectが正しく設定されるべき");
        }

        // --- FireDamageDealtイベントでDamageResultが正しく伝達されるか ---

        [Test]
        public void FireDamageDealt_TransmitsCriticalFlag()
        {
            DamageResult receivedResult = default;
            int receivedAttacker = 0;
            int receivedDefender = 0;

            _events.OnDamageDealt.Subscribe(e =>
            {
                receivedResult = e.result;
                receivedAttacker = e.attackerHash;
                receivedDefender = e.defenderHash;
            });

            DamageResult firedResult = new DamageResult
            {
                totalDamage = 200,
                isCritical = true,
                appliedEffect = StatusEffectId.Burn,
                guardResult = GuardResult.NoGuard,
                hitReaction = HitReaction.Knockback,
                isKill = false,
                situationalBonus = SituationalBonus.Counter,
                armorDamage = 10f
            };

            _events.FireDamageDealt(firedResult, 42, 99);

            Assert.AreEqual(200, receivedResult.totalDamage);
            Assert.IsTrue(receivedResult.isCritical);
            Assert.AreEqual(StatusEffectId.Burn, receivedResult.appliedEffect);
            Assert.AreEqual(GuardResult.NoGuard, receivedResult.guardResult);
            Assert.AreEqual(HitReaction.Knockback, receivedResult.hitReaction);
            Assert.AreEqual(SituationalBonus.Counter, receivedResult.situationalBonus);
            Assert.AreEqual(42, receivedAttacker);
            Assert.AreEqual(99, receivedDefender);
        }

        // --- C# Standard Event互換 ---

        [Test]
        public void FireDamageDealt_AlsoFiresCSharpEvent()
        {
            DamageResult receivedResult = default;
            bool eventFired = false;

            _events.OnDamageDealtEvent += (result, attacker, defender) =>
            {
                receivedResult = result;
                eventFired = true;
            };

            DamageResult firedResult = new DamageResult
            {
                totalDamage = 100,
                isCritical = false,
                appliedEffect = StatusEffectId.None
            };

            _events.FireDamageDealt(firedResult, 1, 2);

            Assert.IsTrue(eventFired);
            Assert.AreEqual(100, receivedResult.totalDamage);
        }

        // --- 状態異常イベント ---

        [Test]
        public void FireStatusEffectApplied_TransmitsCorrectData()
        {
            int receivedTarget = 0;
            StatusEffectId receivedEffect = StatusEffectId.None;

            _events.OnStatusEffectApplied.Subscribe(e =>
            {
                receivedTarget = e.targetHash;
                receivedEffect = e.effectId;
            });

            _events.FireStatusEffectApplied(42, StatusEffectId.Stun);

            Assert.AreEqual(42, receivedTarget);
            Assert.AreEqual(StatusEffectId.Stun, receivedEffect);
        }
    }
}
