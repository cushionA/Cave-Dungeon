using System;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    public class StatusEffectDefenseProfileTests
    {
        [Test]
        public void StatusDefense_Unspecified_PreservesZeroCutAndThreshold100()
        {
            StatusEffectDefense result = default(StatusEffectDefenseProfile).Resolve(StatusEffectId.Poison);
            Assert.AreEqual(0f, result.accumulationReduction);
            Assert.AreEqual(100f, result.threshold);
            Assert.IsFalse(result.immune);
        }

        [TestCase(StatusEffectId.Poison, 81f)]
        [TestCase(StatusEffectId.Burn, 82f)]
        [TestCase(StatusEffectId.Bleed, 83f)]
        [TestCase(StatusEffectId.Stun, 84f)]
        [TestCase(StatusEffectId.Freeze, 85f)]
        [TestCase(StatusEffectId.Paralyze, 86f)]
        [TestCase(StatusEffectId.Slow, 87f)]
        [TestCase(StatusEffectId.Blind, 88f)]
        [TestCase(StatusEffectId.Silence, 89f)]
        [TestCase(StatusEffectId.Weakness, 90f)]
        [TestCase(StatusEffectId.Curse, 91f)]
        [TestCase(StatusEffectId.Confusion, 92f)]
        [TestCase(StatusEffectId.AttackUp, 93f)]
        [TestCase(StatusEffectId.DefenseUp, 94f)]
        [TestCase(StatusEffectId.SpeedUp, 95f)]
        public void StatusDefense_EffectLookup_UsesItsOwnValueField(StatusEffectId id, float threshold)
        {
            StatusEffectDefenseProfile profile = new StatusEffectDefenseProfile
            {
                poison = new StatusEffectDefense { threshold = 81f },
                burn = new StatusEffectDefense { threshold = 82f },
                bleed = new StatusEffectDefense { threshold = 83f },
                stun = new StatusEffectDefense { threshold = 84f },
                freeze = new StatusEffectDefense { threshold = 85f },
                paralyze = new StatusEffectDefense { threshold = 86f },
                slow = new StatusEffectDefense { threshold = 87f },
                blind = new StatusEffectDefense { threshold = 88f },
                silence = new StatusEffectDefense { threshold = 89f },
                weakness = new StatusEffectDefense { threshold = 90f },
                curse = new StatusEffectDefense { threshold = 91f },
                confusion = new StatusEffectDefense { threshold = 92f },
                attackUp = new StatusEffectDefense { threshold = 93f },
                defenseUp = new StatusEffectDefense { threshold = 94f },
                speedUp = new StatusEffectDefense { threshold = 95f },
            };
            Assert.AreEqual(threshold, profile.Get(id).threshold);
            Assert.AreEqual(threshold, profile.Resolve(id).threshold);
        }

        [Test]
        public void StatusDefense_CommonSpecificAndLegacyCut_ComposeWithoutDoubleCounting()
        {
            StatusEffectDefenseProfile profile = new StatusEffectDefenseProfile
            {
                commonReduction = 0.2f,
                poison = new StatusEffectDefense { accumulationReduction = 0.3f, threshold = 150f, immune = true },
            };
            StatusEffectDefense poison = profile.Resolve(StatusEffectId.Poison, 0.4f);
            StatusEffectDefense burn = profile.Resolve(StatusEffectId.Burn, 0.4f);
            Assert.AreEqual(0.7f, poison.accumulationReduction, 0.00001f);
            Assert.AreEqual(150f, poison.threshold);
            Assert.IsTrue(poison.immune);
            Assert.AreEqual(0.52f, burn.accumulationReduction, 0.00001f);
            Assert.AreEqual(100f, burn.threshold);
            Assert.IsFalse(burn.immune);
        }

        [Test]
        public void StatusDefense_AllEquipment_ComposesFromBasisAndKeepsDefaultRepresentation()
        {
            StatusEffectDefenseProfile basis = new StatusEffectDefenseProfile
            {
                commonReduction = 0.1f,
                poison = new StatusEffectDefense { accumulationReduction = 0.2f },
            };
            StatusEffectDefenseProfile weapon = new StatusEffectDefenseProfile
            {
                commonReduction = 0.2f,
                poison = new StatusEffectDefense { accumulationReduction = 0.1f, threshold = 20f },
            };
            StatusEffectDefenseProfile shield = new StatusEffectDefenseProfile
            {
                commonReduction = 0.1f,
                poison = new StatusEffectDefense { threshold = -10f },
            };
            StatusEffectDefenseProfile core = new StatusEffectDefenseProfile
            {
                poison = new StatusEffectDefense { accumulationReduction = 0.1f, threshold = 30f, immune = true },
            };
            StatusEffectDefenseProfile result = StatusEffectDefenseProfile.Combine(basis, weapon, shield, core);
            Assert.AreEqual(0.8f, result.Resolve(StatusEffectId.Poison).accumulationReduction, 0.00001f);
            Assert.AreEqual(140f, result.Resolve(StatusEffectId.Poison).threshold);
            Assert.IsTrue(result.Resolve(StatusEffectId.Poison).immune);
            Assert.AreEqual(0f, result.Get(StatusEffectId.Burn).threshold);
            Assert.AreEqual(100f, result.Resolve(StatusEffectId.Burn).threshold);
            Assert.AreEqual(default(StatusEffectDefenseProfile),
                StatusEffectDefenseProfile.Combine(default, default, default, default));
        }

        [Test]
        public void StatusDefense_EquipmentRemoved_RestoresBasisWithoutAccumulatingDerivedValues()
        {
            StatusEffectDefenseProfile basis = new StatusEffectDefenseProfile { commonReduction = 0.2f };
            StatusEffectDefenseProfile item = new StatusEffectDefenseProfile
            {
                poison = new StatusEffectDefense { accumulationReduction = 0.3f, threshold = 50f, immune = true },
            };
            StatusEffectDefenseProfile first = StatusEffectDefenseProfile.Combine(basis, item, default, default);
            StatusEffectDefenseProfile again = StatusEffectDefenseProfile.Combine(basis, item, default, default);
            StatusEffectDefenseProfile removed = StatusEffectDefenseProfile.Combine(basis, default, default, default);
            Assert.AreEqual(first, again);
            Assert.AreEqual(150f, first.Resolve(StatusEffectId.Poison).threshold);
            Assert.IsTrue(first.Resolve(StatusEffectId.Poison).immune);
            Assert.AreEqual(basis, removed);
            Assert.AreEqual(100f, removed.Resolve(StatusEffectId.Poison).threshold);
            Assert.IsFalse(removed.Resolve(StatusEffectId.Poison).immune);
        }

        [Test]
        public void StatusDefense_SignedEquipmentModifiers_ClampOnlyAfterSummingAllSlots()
        {
            StatusEffectDefenseProfile basis = new StatusEffectDefenseProfile { commonReduction = 0.5f };
            StatusEffectDefenseProfile weapon = new StatusEffectDefenseProfile
            {
                commonReduction = -0.2f,
                poison = new StatusEffectDefense { accumulationReduction = 0.8f, threshold = -500f },
            };
            StatusEffectDefenseProfile core = new StatusEffectDefenseProfile
            {
                poison = new StatusEffectDefense { accumulationReduction = -0.3f },
            };
            StatusEffectDefenseProfile result = StatusEffectDefenseProfile.Combine(basis, weapon, default, core);
            Assert.AreEqual(0.8f, result.Resolve(StatusEffectId.Poison).accumulationReduction, 0.00001f);
            Assert.AreEqual(1f, result.Resolve(StatusEffectId.Poison).threshold);
        }

        [TestCase(-2f, 0.25f)]
        [TestCase(2f, 1f)]
        public void StatusDefense_LegacyCutOutOfRange_PreservesClamping(float statusCut, float expected)
        {
            StatusEffectDefenseProfile profile = new StatusEffectDefenseProfile { commonReduction = 0.25f };
            Assert.AreEqual(expected, profile.Resolve(StatusEffectId.Poison, statusCut).accumulationReduction);
        }

        [TestCase(StatusEffectId.None)]
        [TestCase(StatusEffectId.Count)]
        [TestCase((StatusEffectId)255)]
        public void StatusDefense_InvalidEffectLookup_ReturnsEmptyValue(StatusEffectId id)
        {
            StatusEffectDefenseProfile profile = new StatusEffectDefenseProfile
            {
                poison = new StatusEffectDefense { threshold = 150f, immune = true },
            };
            Assert.AreEqual(default(StatusEffectDefense), profile.Get(id));
        }

        [Test]
        public void StatusDefense_GlobalThresholdBonus_ChangesThresholdWithoutChangingAccumulationCut()
        {
            StatusEffectDefenseProfile profile = new StatusEffectDefenseProfile
            {
                thresholdMultiplierBonus = 0.5f,
                poison = new StatusEffectDefense { threshold = 200f },
            };
            Assert.AreEqual(300f, profile.Resolve(StatusEffectId.Poison).threshold);
            Assert.AreEqual(150f, profile.Resolve(StatusEffectId.Burn).threshold);
            Assert.AreEqual(0f, profile.Resolve(StatusEffectId.Poison).accumulationReduction);
        }

        [Test]
        public void StatusDefense_ThresholdBonuses_ComposeBeforeResolvingFlatThreshold()
        {
            StatusEffectDefenseProfile basis = new StatusEffectDefenseProfile { thresholdMultiplierBonus = 0.2f };
            StatusEffectDefenseProfile weapon = new StatusEffectDefenseProfile
            {
                thresholdMultiplierBonus = 0.3f,
                poison = new StatusEffectDefense { threshold = 20f },
            };
            StatusEffectDefenseProfile shield = new StatusEffectDefenseProfile { thresholdMultiplierBonus = -0.1f };
            StatusEffectDefenseProfile core = new StatusEffectDefenseProfile { thresholdMultiplierBonus = 0.1f };
            StatusEffectDefenseProfile result = StatusEffectDefenseProfile.Combine(basis, weapon, shield, core);
            Assert.AreEqual(180f, result.Resolve(StatusEffectId.Poison).threshold, 0.0001f);
            Assert.AreEqual(150f, result.Resolve(StatusEffectId.Burn).threshold, 0.0001f);
        }

        [Test]
        public void StatusDefense_NegativeThresholdMultiplier_ClampsResolvedThresholdToOne()
        {
            StatusEffectDefenseProfile profile = new StatusEffectDefenseProfile { thresholdMultiplierBonus = -2f };
            Assert.AreEqual(1f, profile.Resolve(StatusEffectId.Poison).threshold);
        }

        [Test]
        public void StatusDefense_FlatThresholdLowerBound_IsAppliedBeforeGlobalMultiplier()
        {
            StatusEffectDefenseProfile basis = new StatusEffectDefenseProfile { thresholdMultiplierBonus = 0.5f };
            StatusEffectDefenseProfile item = new StatusEffectDefenseProfile
            {
                poison = new StatusEffectDefense { threshold = -100f },
            };
            StatusEffectDefenseProfile result = StatusEffectDefenseProfile.Combine(basis, item, default, default);
            Assert.AreEqual(1f, result.Get(StatusEffectId.Poison).threshold);
            Assert.AreEqual(1.5f, result.Resolve(StatusEffectId.Poison).threshold);
        }

        [Test]
        public void StatusDefense_SignedCommonAndIndividualModifiers_ResolveAfterCrossChannelCancellation()
        {
            StatusEffectDefenseProfile basis = new StatusEffectDefenseProfile { commonReduction = 0.2f };
            StatusEffectDefenseProfile item = new StatusEffectDefenseProfile
            {
                commonReduction = -0.4f,
                poison = new StatusEffectDefense { accumulationReduction = 0.5f },
            };
            StatusEffectDefenseProfile result = StatusEffectDefenseProfile.Combine(basis, item, default, default);
            Assert.AreEqual(0.3f, result.Resolve(StatusEffectId.Poison).accumulationReduction, 0.00001f);
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void StatusDefense_NonFiniteInput_RejectsBeforeProducingRuntimeValues(float value)
        {
            StatusEffectDefenseProfile invalidCommon = new StatusEffectDefenseProfile { commonReduction = value };
            StatusEffectDefenseProfile invalidSpecific = new StatusEffectDefenseProfile
            {
                poison = new StatusEffectDefense { accumulationReduction = value },
            };
            StatusEffectDefenseProfile invalidThreshold = new StatusEffectDefenseProfile
            {
                poison = new StatusEffectDefense { threshold = value },
            };
            StatusEffectDefenseProfile invalidMultiplier = new StatusEffectDefenseProfile { thresholdMultiplierBonus = value };
            Assert.Throws<ArgumentException>(() => invalidCommon.Validate());
            Assert.Throws<ArgumentException>(() => invalidSpecific.Validate());
            Assert.Throws<ArgumentException>(() => invalidThreshold.Validate());
            Assert.Throws<ArgumentException>(() => invalidMultiplier.Validate());
            Assert.Throws<ArgumentException>(() => invalidCommon.Resolve(StatusEffectId.Poison));
            Assert.Throws<ArgumentException>(() => invalidSpecific.Resolve(StatusEffectId.Poison));
            Assert.Throws<ArgumentException>(() => invalidThreshold.Resolve(StatusEffectId.Poison));
            Assert.Throws<ArgumentException>(() => invalidMultiplier.Resolve(StatusEffectId.Poison));
            Assert.Throws<ArgumentException>(() => default(StatusEffectDefenseProfile).Resolve(StatusEffectId.Poison, value));
            Assert.Throws<ArgumentException>(() => StatusEffectDefenseProfile.Combine(default, invalidThreshold, default, default));
        }
    }
}

