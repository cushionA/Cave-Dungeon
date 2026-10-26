using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public class EquipmentDerivedStatsTests
    {
        private static CharacterBaseStats Basis() => new CharacterBaseStats
        {
            maxHp = 100, maxMp = 50, maxStamina = 60f, maxArmor = 40f,
            staminaRecoveryRate = 7f, staminaRecoveryDelay = 0.75f,
            attributes = new StatModifier { str = 10, dex = 10, intel = 4, vit = 40, mnd = 50, end = 60 },
            combat = new CombatStats
            {
                attack = new ElementalStatus { slash = 7, fire = 3 },
                defense = new ElementalStatus { slash = 11, fire = 5 },
                guardStats = new GuardStats { slashCut = 0.2f, guardStrength = 8f, guardDirection = GuardDirection.Front },
                knockbackResistance = 0.35f,
            },
        };

        [Test]
        public void EquipmentDerivedStats_NoModifiers_PreservesExistingGrowthAndCombatContract()
        {
            CharacterBaseStats basis = Basis();
            CharacterGrowth growth = new CharacterGrowth { level = 4, allocatedStats = new StatModifier { vit = 2, mnd = 3 } };
            CharacterVitals current = new CharacterVitals
            {
                currentHp = 35, currentMp = 17.5f, currentStamina = 21f, currentArmor = 15f,
                position = new Vector2(3f, -4f),
            };
            CharacterVitals expected = CharacterStatCalculator.RecalculateVitals(basis, growth, current);
            EquipmentDerivedStats result = EquipmentStatCalculator.Calculate(basis, growth, current, default, default, default);
            Assert.AreEqual(expected, result.vitals);
            Assert.AreEqual(basis.combat, result.combat);
            Assert.AreEqual(default(StatModifier), result.attributeModifiers);
        }

        [Test]
        public void EquipmentDerivedStats_AllChannelsAndSlots_AddsEachBonusToTheBasis()
        {
            CharacterBaseStats basis = Basis();
            basis.combat.attack = new ElementalStatus { slash = 1000, strike = 2000, pierce = 3000, fire = 4000, thunder = 5000, light = 6000, dark = 7000 };
            basis.combat.defense = new ElementalStatus { slash = 40, strike = 40, pierce = 40, fire = 40, thunder = 40, light = 40, dark = 40 };
            EquipmentStatModifiers weapon = new EquipmentStatModifiers
            {
                attackBonus = new ElementalStatus { slash = 1, strike = 2, pierce = 3, fire = 4, thunder = 5, light = 6, dark = 7 },
                defenseBonus = new ElementalStatus { slash = -4, strike = -8, pierce = -12, fire = -16, thunder = -20, light = -24, dark = -28 },
            };
            EquipmentStatModifiers shield = new EquipmentStatModifiers
            {
                attackBonus = new ElementalStatus { slash = 10, strike = 20, pierce = 30, fire = 40, thunder = 50, light = 60, dark = 70 },
                defenseBonus = new ElementalStatus { slash = 3, strike = 6, pierce = 9, fire = 12, thunder = 15, light = 18, dark = 21 },
            };
            EquipmentStatModifiers core = new EquipmentStatModifiers
            {
                attackBonus = new ElementalStatus { slash = 100, strike = 200, pierce = 300, fire = 400, thunder = 500, light = 600, dark = 700 },
                defenseBonus = new ElementalStatus { slash = 2, strike = 4, pierce = 6, fire = 8, thunder = 10, light = 12, dark = 14 },
            };
            EquipmentDerivedStats result = EquipmentStatCalculator.Calculate(basis, default, default, weapon, shield, core);
            Element[] elements = { Element.Slash, Element.Strike, Element.Pierce, Element.Fire, Element.Thunder, Element.Light, Element.Dark };
            for (int i = 0; i < elements.Length; i++)
            {
                Assert.AreEqual(1111 * (i + 1), result.combat.attack.Get(elements[i]));
                Assert.AreEqual(41 + i, result.combat.defense.Get(elements[i]));
            }
        }

        [Test]
        public void EquipmentDerivedStats_AllAttributeSources_ScaleEachItemUsingCombinedAttributes()
        {
            CharacterBaseStats basis = Basis();
            CharacterGrowth growth = new CharacterGrowth { allocatedStats = new StatModifier { str = 10, dex = 2, intel = 1 } };
            EquipmentStatModifiers weapon = new EquipmentStatModifiers
            {
                attributes = new StatModifier { str = 10, dex = 3, intel = 2, vit = 1, mnd = 2, end = 3 },
                attackBonus = new ElementalStatus { slash = 100 },
                strengthScaling = 0.5f, dexterityScaling = 0.75f, intelligenceScaling = 1f,
            };
            EquipmentStatModifiers shield = new EquipmentStatModifiers
            {
                attributes = new StatModifier { str = 10, dex = 3, intel = 2, vit = 2, mnd = 3, end = 4 },
                attackBonus = new ElementalStatus { slash = 10 },
            };
            EquipmentStatModifiers core = new EquipmentStatModifiers
            {
                attributes = new StatModifier { str = 10, dex = 2, intel = 1, vit = 3, mnd = 4, end = 5 },
                attackBonus = new ElementalStatus { slash = 20 },
            };
            EquipmentDerivedStats result = EquipmentStatCalculator.Calculate(basis, growth, default, weapon, shield, core);
            Assert.AreEqual(new StatModifier { str = 30, dex = 8, intel = 5, vit = 6, mnd = 9, end = 12 }, result.attributeModifiers);
            // 最終STR=50, DEX=20, INT=10: 100*(1+(25+15+10)/100)=150。
            Assert.AreEqual(187, result.combat.attack.slash);
            Assert.AreEqual(3, result.combat.attack.fire);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void EquipmentDerivedStats_SingleScalingSource_PreservesExistingRounding(int source)
        {
            CharacterBaseStats basis = Basis();
            basis.attributes = new StatModifier { str = 50, dex = 50, intel = 50 };
            EquipmentStatModifiers weapon = new EquipmentStatModifiers { attackBonus = new ElementalStatus { slash = 100 } };
            if (source == 0) { weapon.strengthScaling = 0.8f; }
            if (source == 1) { weapon.dexterityScaling = 0.8f; }
            if (source == 2) { weapon.intelligenceScaling = 0.8f; }
            EquipmentDerivedStats result = EquipmentStatCalculator.Calculate(basis, default, default, weapon, default, default);
            Assert.AreEqual(7 + EquipmentStatCalculator.CalculateScaledAttack(100, 0.8f, 50), result.combat.attack.slash);
        }

        [Test]
        public void EquipmentDerivedStats_AttributeAndExplicitResourceBonuses_PreserveCurrentValuesAndPosition()
        {
            CharacterBaseStats basis = Basis();
            CharacterGrowth growth = new CharacterGrowth { level = 8, allocatedStats = new StatModifier { vit = 2, mnd = 3 } };
            CharacterVitals current = new CharacterVitals
            {
                currentHp = 80, currentMp = 22.5f, currentStamina = 30f, currentArmor = 10f,
                position = new Vector2(-6f, 12f),
            };
            EquipmentStatModifiers weapon = new EquipmentStatModifiers
            {
                attributes = new StatModifier { vit = 3, mnd = 4, end = 500 },
                bonusHp = 10, bonusMp = 20, bonusStamina = 7f, bonusArmor = 8f,
            };
            EquipmentStatModifiers shield = new EquipmentStatModifiers { bonusHp = 15, bonusMp = 5, bonusStamina = 3f, bonusArmor = 2f };
            EquipmentStatModifiers core = new EquipmentStatModifiers
            {
                attributes = new StatModifier { vit = 1, mnd = 2 },
                bonusHp = 5, bonusMp = 10, bonusStamina = 5f, bonusArmor = 5f,
            };
            CharacterVitals result = EquipmentStatCalculator.Calculate(basis, growth, current, weapon, shield, core).vitals;
            Assert.AreEqual(190, result.maxHp);
            Assert.AreEqual(130, result.maxMp);
            Assert.AreEqual(75f, result.maxStamina);
            Assert.AreEqual(55f, result.maxArmor);
            Assert.AreEqual(80, result.currentHp);
            Assert.AreEqual(22.5f, result.currentMp);
            Assert.AreEqual(30f, result.currentStamina);
            Assert.AreEqual(10f, result.currentArmor);
            Assert.AreEqual(current.position, result.position);
            Assert.AreEqual(42, result.hpRatio);
            Assert.AreEqual(17, result.mpRatio);
            Assert.AreEqual(7f, result.staminaRecoveryRate);
            Assert.AreEqual(0.75f, result.staminaRecoveryDelay);
            Assert.AreEqual(8, result.level);
        }

        [Test]
        public void EquipmentDerivedStats_ReducedMaxima_ClampCurrentResourcesAndUpdateRatios()
        {
            CharacterBaseStats basis = Basis();
            CharacterVitals current = new CharacterVitals { currentHp = 90, currentMp = 40f, currentStamina = 50f, currentArmor = 30f };
            EquipmentStatModifiers core = new EquipmentStatModifiers { bonusHp = -90, bonusMp = -45, bonusStamina = -55f, bonusArmor = -38f };
            CharacterVitals result = EquipmentStatCalculator.Calculate(basis, default, current, default, default, core).vitals;
            Assert.AreEqual(10, result.maxHp);
            Assert.AreEqual(10, result.currentHp);
            Assert.AreEqual(5, result.maxMp);
            Assert.AreEqual(5f, result.currentMp);
            Assert.AreEqual(5f, result.currentStamina);
            Assert.AreEqual(2f, result.currentArmor);
            Assert.AreEqual(100, result.hpRatio);
            Assert.AreEqual(100, result.mpRatio);
        }

        [Test]
        public void EquipmentDerivedStats_AmountsAboveBasisCaps_UseOnlyFinalEquipmentCapsWithoutReviving()
        {
            CharacterBaseStats basis = Basis();
            CharacterVitals current = new CharacterVitals { currentHp = 150, currentMp = 80f, currentStamina = 90f, currentArmor = 70f };
            EquipmentStatModifiers core = new EquipmentStatModifiers { bonusHp = 100, bonusMp = 50, bonusStamina = 60f, bonusArmor = 40f };
            CharacterVitals result = EquipmentStatCalculator.Calculate(basis, default, current, default, default, core).vitals;
            Assert.AreEqual(150, result.currentHp);
            Assert.AreEqual(80f, result.currentMp);
            Assert.AreEqual(90f, result.currentStamina);
            Assert.AreEqual(70f, result.currentArmor);
            current.currentHp = 0;
            result = EquipmentStatCalculator.Calculate(basis, default, current, default, default, core).vitals;
            Assert.AreEqual(0, result.currentHp);
        }

        [Test]
        public void EquipmentDerivedStats_NegativeItemAttackBonus_OffsetsOtherItemsBeforeFinalClamp()
        {
            CharacterBaseStats basis = Basis();
            EquipmentStatModifiers weapon = new EquipmentStatModifiers { attackBonus = new ElementalStatus { slash = -20, fire = -10 } };
            EquipmentStatModifiers shield = new EquipmentStatModifiers { attackBonus = new ElementalStatus { slash = 30, fire = 10 } };
            EquipmentDerivedStats result = EquipmentStatCalculator.Calculate(basis, default, default, weapon, shield, default);
            Assert.AreEqual(17, result.combat.attack.slash);
            Assert.AreEqual(3, result.combat.attack.fire);
        }

        [Test]
        public void EquipmentDerivedStats_NegativeFinalValues_KeepLivingHpAndClampOtherMaximaAndCombatToZero()
        {
            CharacterBaseStats basis = Basis();
            CharacterVitals current = new CharacterVitals { currentHp = 90, currentMp = 40f, currentStamina = 50f, currentArmor = 30f };
            EquipmentStatModifiers core = new EquipmentStatModifiers
            {
                bonusHp = -1000, bonusMp = -1000, bonusStamina = -1000f, bonusArmor = -1000f,
                attackBonus = new ElementalStatus { slash = -100, fire = -100 },
                defenseBonus = new ElementalStatus { slash = -100, fire = -100 },
            };
            EquipmentDerivedStats result = EquipmentStatCalculator.Calculate(basis, default, current, default, default, core);
            Assert.AreEqual(1, result.vitals.maxHp);
            Assert.AreEqual(0, result.vitals.maxMp);
            Assert.AreEqual(0f, result.vitals.maxStamina);
            Assert.AreEqual(0f, result.vitals.maxArmor);
            Assert.AreEqual(1, result.vitals.currentHp);
            Assert.AreEqual(0f, result.vitals.currentMp);
            Assert.AreEqual(0f, result.vitals.currentStamina);
            Assert.AreEqual(0f, result.vitals.currentArmor);
            Assert.AreEqual(100, result.vitals.hpRatio);
            Assert.AreEqual(0, result.vitals.mpRatio);
            Assert.AreEqual(0, result.combat.attack.slash);
            Assert.AreEqual(0, result.combat.attack.fire);
            Assert.AreEqual(0, result.combat.defense.slash);
            Assert.AreEqual(0, result.combat.defense.fire);
        }

        [TestCase(100, 0, 0, 1, 0)]
        [TestCase(0, 100, 0, 0, 0)]
        [TestCase(0, 0, 1, 1, 1)]
        [TestCase(0, 0, 0, 0, 0)]
        public void EquipmentDerivedStats_HpMinimum_PreservesExistingHpContractAndDoesNotRevive(
            int baseMaxHp, int currentMaxHp, int currentHp, int expectedMaxHp, int expectedCurrentHp)
        {
            CharacterBaseStats basis = new CharacterBaseStats { maxHp = baseMaxHp };
            CharacterVitals current = new CharacterVitals { maxHp = currentMaxHp, currentHp = currentHp };
            EquipmentStatModifiers core = new EquipmentStatModifiers { bonusHp = -1000 };
            CharacterVitals result = EquipmentStatCalculator.Calculate(basis, default, current, default, default, core).vitals;
            Assert.AreEqual(expectedMaxHp, result.maxHp);
            Assert.AreEqual(expectedCurrentHp, result.currentHp);
        }

        [Test]
        public void EquipmentDerivedStats_GrowthDefinesHp_KeepsMinimumOneWithoutReviving()
        {
            CharacterGrowth growth = new CharacterGrowth { allocatedStats = new StatModifier { vit = 1 } };
            EquipmentStatModifiers core = new EquipmentStatModifiers { bonusHp = -1000 };
            CharacterVitals result = EquipmentStatCalculator.Calculate(default, growth, default, default, default, core).vitals;
            Assert.AreEqual(1, result.maxHp);
            Assert.AreEqual(0, result.currentHp);
        }

        [Test]
        public void EquipmentDerivedStats_HpUnsetActorEquipsAndRemovesHpBonus_ReturnsToZeroWithoutHistory()
        {
            EquipmentStatModifiers core = new EquipmentStatModifiers { bonusHp = 100 };
            CharacterVitals equipped = EquipmentStatCalculator.Calculate(default, default, default, default, default, core).vitals;
            Assert.AreEqual(100, equipped.maxHp);
            Assert.AreEqual(0, equipped.currentHp);
            CharacterVitals removed = EquipmentStatCalculator.Calculate(default, default, equipped, default, default, default).vitals;
            Assert.AreEqual(0, removed.maxHp);
            Assert.AreEqual(0, removed.currentHp);
        }

        [Test]
        public void EquipmentDerivedStats_RepeatedRecalculation_LeavesBasisUnchangedAndDoesNotAccumulate()
        {
            CharacterBaseStats basis = Basis();
            CharacterGrowth growth = new CharacterGrowth { level = 2, allocatedStats = new StatModifier { vit = 2 } };
            EquipmentStatModifiers core = new EquipmentStatModifiers { bonusHp = 50, attackBonus = new ElementalStatus { slash = 20 } };
            CharacterVitals current = new CharacterVitals { currentHp = 25 };
            for (int i = 0; i < 5; i++)
            {
                EquipmentDerivedStats equipped = EquipmentStatCalculator.Calculate(basis, growth, current, default, default, core);
                Assert.AreEqual(170, equipped.vitals.maxHp);
                Assert.AreEqual(27, equipped.combat.attack.slash);
                EquipmentDerivedStats removed = EquipmentStatCalculator.Calculate(basis, growth, equipped.vitals, default, default, default);
                Assert.AreEqual(120, removed.vitals.maxHp);
                Assert.AreEqual(7, removed.combat.attack.slash);
                current = removed.vitals;
            }
            Assert.AreEqual(100, basis.maxHp);
            Assert.AreEqual(7, basis.combat.attack.slash);
            Assert.AreEqual(40, basis.attributes.vit);
            Assert.AreEqual(2, growth.allocatedStats.vit);
            Assert.AreEqual(25, current.currentHp);
        }

        [Test]
        public void EquipmentDerivedStats_GuardOverride_OnlyAcceptsOptedInShield()
        {
            CharacterBaseStats basis = Basis();
            EquipmentStatModifiers weapon = new EquipmentStatModifiers { overridesGuard = true, guardStats = new GuardStats { slashCut = 0.9f } };
            EquipmentStatModifiers shield = new EquipmentStatModifiers
            {
                guardStats = new GuardStats { slashCut = 0.7f, guardStrength = 27f, guardDirection = GuardDirection.Both },
            };
            EquipmentStatModifiers core = new EquipmentStatModifiers { overridesGuard = true, guardStats = new GuardStats { slashCut = 1f } };
            EquipmentDerivedStats result = EquipmentStatCalculator.Calculate(basis, default, default, weapon, shield, core);
            Assert.AreEqual(basis.combat.guardStats, result.combat.guardStats);
            shield.overridesGuard = true;
            result = EquipmentStatCalculator.Calculate(basis, default, default, weapon, shield, core);
            Assert.AreEqual(shield.guardStats, result.combat.guardStats);
            Assert.AreEqual(0.35f, result.combat.knockbackResistance);
        }
    }
}
