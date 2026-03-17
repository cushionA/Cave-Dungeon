using NUnit.Framework;
using Game.Core;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public class CommonSharedTypesTests
    {
        [Test]
        public void ElementalStatus_Total_ReturnsSumOfAllElements()
        {
            ElementalStatus status = new ElementalStatus
            {
                physical = 10,
                fire = 5,
                thunder = 3,
                light = 2,
                dark = 1
            };

            Assert.AreEqual(21, status.Total);
        }

        [Test]
        public void ElementalStatus_Get_ReturnsCorrectElementValue()
        {
            ElementalStatus status = new ElementalStatus
            {
                physical = 100,
                fire = 50,
                thunder = 30,
                light = 20,
                dark = 10
            };

            Assert.AreEqual(50, status.Get(Element.Fire));
            Assert.AreEqual(30, status.Get(Element.Thunder));
            Assert.AreEqual(20, status.Get(Element.Light));
            Assert.AreEqual(10, status.Get(Element.Dark));
            Assert.AreEqual(100, status.Get(Element.None));
        }

        [Test]
        public void AttackFeature_Flags_CombineCorrectly()
        {
            AttackFeature flags = AttackFeature.Heavy | AttackFeature.SuperArmor;

            Assert.IsTrue(flags.HasFlag(AttackFeature.Heavy));
            Assert.IsTrue(flags.HasFlag(AttackFeature.SuperArmor));
            Assert.IsFalse(flags.HasFlag(AttackFeature.Light));
            Assert.IsFalse(flags.HasFlag(AttackFeature.JustGuardImmune));
        }

        [Test]
        public void AbilityFlag_Flags_MergeAndCheckCorrectly()
        {
            AbilityFlag weaponFlags = AbilityFlag.WallKick;
            AbilityFlag coreFlags = AbilityFlag.DoubleJump | AbilityFlag.WallCling;
            AbilityFlag merged = weaponFlags | coreFlags;

            Assert.IsTrue(merged.HasFlag(AbilityFlag.WallKick));
            Assert.IsTrue(merged.HasFlag(AbilityFlag.DoubleJump));
            Assert.IsTrue(merged.HasFlag(AbilityFlag.WallCling));
            Assert.IsFalse(merged.HasFlag(AbilityFlag.AirDash));
            Assert.IsFalse(merged.HasFlag(AbilityFlag.Swim));
        }

        [Test]
        public void GuardStats_DefaultValues_AreZero()
        {
            GuardStats stats = default;

            Assert.AreEqual(0f, stats.physicalCut);
            Assert.AreEqual(0f, stats.fireCut);
            Assert.AreEqual(0f, stats.thunderCut);
            Assert.AreEqual(0f, stats.lightCut);
            Assert.AreEqual(0f, stats.darkCut);
            Assert.AreEqual(0f, stats.guardStrength);
            Assert.AreEqual(0f, stats.statusCut);
        }

        [Test]
        public void StatusEffectApply_DefaultValues_AreNone()
        {
            StatusEffectApply apply = default;

            Assert.AreEqual(StatusEffectId.None, apply.effectId);
            Assert.AreEqual(0f, apply.accumulateValue);
        }

        [Test]
        public void StatModifier_AllFields_StoredCorrectly()
        {
            StatModifier mod = new StatModifier
            {
                str = 5,
                dex = -2,
                intel = 10,
                vit = 0,
                mnd = 3,
                end = -1
            };

            Assert.AreEqual(5, mod.str);
            Assert.AreEqual(-2, mod.dex);
            Assert.AreEqual(10, mod.intel);
            Assert.AreEqual(0, mod.vit);
            Assert.AreEqual(3, mod.mnd);
            Assert.AreEqual(-1, mod.end);
        }
    }
}
