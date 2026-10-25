using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public class CharacterStatCalculatorTests
    {
        private static CharacterBaseStats Basis() => new CharacterBaseStats
        {
            maxHp = 100, maxMp = 50, maxStamina = 80f, maxArmor = 30f,
            staminaRecoveryRate = 12f, staminaRecoveryDelay = 0.6f,
            attributes = new StatModifier { str = 4, dex = 5, intel = 6, vit = 7, mnd = 8, end = 9 },
            combat = new CombatStats { attack = new ElementalStatus { slash = 15, fire = 4 } },
        };

        [Test]
        public void CharacterStats_Registration_FillsDerivedMaximaWithoutApplyingBaseAttributesTwice()
        {
            CharacterBaseStats basis = Basis();
            CharacterGrowth growth = new CharacterGrowth
            {
                level = 4, allocatedStats = new StatModifier { vit = 2, mnd = 3, end = 10, intel = 20 },
            };

            CharacterVitals vitals = CharacterStatCalculator.CreateVitals(basis, growth);

            Assert.AreEqual(120, vitals.maxHp);
            Assert.AreEqual(65, vitals.maxMp);
            Assert.AreEqual(120, vitals.currentHp);
            Assert.AreEqual(65f, vitals.currentMp);
            Assert.AreEqual(80f, vitals.currentStamina);
            Assert.AreEqual(30f, vitals.currentArmor);
            Assert.AreEqual(12f, vitals.staminaRecoveryRate);
            Assert.AreEqual(0.6f, vitals.staminaRecoveryDelay);
            Assert.AreEqual(4, vitals.level);
            Assert.AreEqual(100, vitals.hpRatio);
            Assert.AreEqual(100, vitals.mpRatio);
            Assert.AreEqual(100, basis.maxHp);
            Assert.AreEqual(15, basis.combat.attack.slash);
        }

        [Test]
        public void CharacterStats_RepeatedGrowth_UsesBasisAndPreservesCurrentResources()
        {
            CharacterBaseStats basis = Basis();
            CharacterGrowth growth = new CharacterGrowth { level = 5, allocatedStats = new StatModifier { vit = 2, mnd = 2 } };
            CharacterVitals current = new CharacterVitals
            {
                currentHp = 23, currentMp = 17.5f, currentStamina = 31.25f, currentArmor = 8.5f,
                maxHp = 110, maxMp = 55, maxStamina = 80f, maxArmor = 30f,
                position = new Vector2(3f, -2f),
            };

            CharacterVitals first = CharacterStatCalculator.RecalculateVitals(basis, growth, current);
            CharacterVitals second = CharacterStatCalculator.RecalculateVitals(basis, growth, first);

            Assert.AreEqual(120, second.maxHp);
            Assert.AreEqual(60, second.maxMp);
            Assert.AreEqual(23, second.currentHp);
            Assert.AreEqual(17.5f, second.currentMp);
            Assert.AreEqual(31.25f, second.currentStamina);
            Assert.AreEqual(8.5f, second.currentArmor);
            Assert.AreEqual(current.position, second.position);
            Assert.AreEqual(5, second.level);
            Assert.AreEqual(first.hpRatio, second.hpRatio);
            Assert.AreEqual(first.mpRatio, second.mpRatio);
        }

        [Test]
        public void CharacterStats_DecreasedMaxima_ClampsResourcesAndKeepsDeadActorsDead()
        {
            CharacterBaseStats basis = Basis();
            CharacterGrowth growth = new CharacterGrowth { level = 1 };
            CharacterVitals current = new CharacterVitals
            {
                currentHp = 120, currentMp = 65.5f, currentStamina = 99f, currentArmor = 44f,
                maxHp = 120, maxMp = 70, maxStamina = 100f, maxArmor = 50f,
            };

            CharacterVitals result = CharacterStatCalculator.RecalculateVitals(basis, growth, current);

            Assert.AreEqual(100, result.currentHp);
            Assert.AreEqual(50f, result.currentMp);
            Assert.AreEqual(80f, result.currentStamina);
            Assert.AreEqual(30f, result.currentArmor);
            current.currentHp = 0;
            current.currentMp = 0;
            basis.maxMp = 0;
            result = CharacterStatCalculator.RecalculateVitals(basis, growth, current);
            Assert.AreEqual(0, result.currentHp);
            Assert.AreEqual(0f, result.currentMp);
            Assert.AreEqual(0, result.hpRatio);
            Assert.AreEqual(0, result.mpRatio);
        }

        [Test]
        public void CharacterStats_AttributeProjection_AddsAllSixBaseAndAllocatedValues()
        {
            CharacterBaseStats basis = Basis();
            CharacterGrowth growth = new CharacterGrowth
            {
                level = 3, allocatedStats = new StatModifier { str = 1, dex = 2, intel = 3, vit = 4, mnd = 5, end = 6 },
            };

            StatModifier attributes = CharacterStatCalculator.CalculateAttributes(basis, growth);

            Assert.AreEqual(5, attributes.str);
            Assert.AreEqual(7, attributes.dex);
            Assert.AreEqual(9, attributes.intel);
            Assert.AreEqual(11, attributes.vit);
            Assert.AreEqual(13, attributes.mnd);
            Assert.AreEqual(15, attributes.end);
            Assert.AreEqual(7, basis.attributes.vit);
            Assert.AreEqual(4, growth.allocatedStats.vit);
        }

        [Test]
        public void CharacterStats_LevelUpSnapshot_PreservesExistingVitalFormula()
        {
            LevelUpLogic logic = new LevelUpLogic();
            logic.AddExp(100);
            Assert.IsTrue(logic.AllocatePoint(StatType.Vit));
            Assert.IsTrue(logic.AllocatePoint(StatType.Mnd));
            Assert.IsTrue(logic.AllocatePoint(StatType.Str));
            CharacterBaseStats basis = Basis();
            CharacterVitals initial = CharacterStatCalculator.CreateVitals(basis, new CharacterGrowth { level = 1 });

            CharacterVitals fromSnapshot = CharacterStatCalculator.RecalculateVitals(basis, logic.Growth, initial);
            CharacterVitals fromLogicApi = logic.RecalculateVitals(basis, initial);
            fromLogicApi = logic.RecalculateVitals(basis, fromLogicApi);

            Assert.AreEqual(2, logic.Growth.level);
            Assert.AreEqual(1, logic.Growth.allocatedStats.str);
            Assert.AreEqual(110, fromSnapshot.maxHp);
            Assert.AreEqual(55, fromSnapshot.maxMp);
            Assert.AreEqual(fromLogicApi.maxHp, fromSnapshot.maxHp);
            Assert.AreEqual(fromLogicApi.maxMp, fromSnapshot.maxMp);
            Assert.AreEqual(fromLogicApi.level, fromSnapshot.level);
        }
    }
}
