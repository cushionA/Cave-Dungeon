using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    [TestFixture]
    public class StatusEffectWiringTests
    {
        private StatusEffectManager _manager;

        [SetUp]
        public void SetUp()
        {
            _manager = new StatusEffectManager();
        }

        // --- 蓄積→発症 ---

        [Test]
        public void StatusEffect_AccumulateToThreshold_TriggersEffect()
        {
            StatusEffectInfo info = new StatusEffectInfo
            {
                effect = StatusEffectId.Poison,
                accumulateValue = StatusEffectManager.k_DefaultThreshold,
                duration = 10f,
                tickDamage = 5f,
                tickInterval = 2f,
                modifier = 0f,
                maxStack = 0
            };

            bool triggered = _manager.Accumulate(info, 0f);

            Assert.IsTrue(triggered, "しきい値到達で発症するべき");
            Assert.IsTrue(_manager.IsActive(StatusEffectId.Poison));
            Assert.AreEqual(1, _manager.ActiveCount);
        }

        [Test]
        public void StatusEffect_BelowThreshold_DoesNotTrigger()
        {
            StatusEffectInfo info = new StatusEffectInfo
            {
                effect = StatusEffectId.Poison,
                accumulateValue = 50f, // しきい値100の半分
                duration = 10f,
                tickDamage = 5f,
                tickInterval = 2f,
                modifier = 0f,
                maxStack = 0
            };

            bool triggered = _manager.Accumulate(info, 0f);

            Assert.IsFalse(triggered, "しきい値未満では発症しない");
            Assert.IsFalse(_manager.IsActive(StatusEffectId.Poison));
        }

        // --- statusCutによる軽減 ---

        [Test]
        public void StatusEffect_StatusCut_ReducesAccumulation()
        {
            // statusCut=0.5 → 蓄積値が半分に
            StatusEffectInfo info = new StatusEffectInfo
            {
                effect = StatusEffectId.Burn,
                accumulateValue = StatusEffectManager.k_DefaultThreshold,
                duration = 10f,
                tickDamage = 5f,
                tickInterval = 2f,
                modifier = 0f,
                maxStack = 0
            };

            bool triggered = _manager.Accumulate(info, 0.5f);

            Assert.IsFalse(triggered, "statusCut=0.5で蓄積半減、しきい値未到達");
            Assert.IsFalse(_manager.IsActive(StatusEffectId.Burn));
        }

        [Test]
        public void StatusEffect_FullStatusCut_NoAccumulation()
        {
            // statusCut=1.0 → 蓄積値ゼロ
            StatusEffectInfo info = new StatusEffectInfo
            {
                effect = StatusEffectId.Stun,
                accumulateValue = 200f,
                duration = 10f,
                tickDamage = 0f,
                tickInterval = 1f,
                modifier = 0f,
                maxStack = 0
            };

            bool triggered = _manager.Accumulate(info, 1.0f);

            Assert.IsFalse(triggered, "statusCut=1.0で蓄積ゼロ");
        }

        // --- None効果は無視 ---

        [Test]
        public void StatusEffect_NoneEffect_DoesNotAccumulate()
        {
            StatusEffectInfo info = new StatusEffectInfo
            {
                effect = StatusEffectId.None,
                accumulateValue = 200f,
                duration = 10f,
                tickDamage = 0f,
                tickInterval = 1f,
                modifier = 0f,
                maxStack = 0
            };

            bool triggered = _manager.Accumulate(info, 0f);

            Assert.IsFalse(triggered);
            Assert.AreEqual(0, _manager.ActiveCount);
        }

        // --- DamageResult.appliedEffectの検証 ---

        [Test]
        public void DamageResult_AppliedEffectField_CanStoreStatusId()
        {
            DamageResult result = new DamageResult
            {
                appliedEffect = StatusEffectId.Poison
            };

            Assert.AreEqual(StatusEffectId.Poison, result.appliedEffect);
        }
    }
}
