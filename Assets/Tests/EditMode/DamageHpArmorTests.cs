using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class DamageHpArmorTests
    {
        [Test]
        public void HpArmorLogic_ApplyDamage_ReducesHp()
        {
            // Arrange
            int currentHp = 100;
            float currentArmor = 0f;
            int rawDamage = 30;
            float armorBreakValue = 0f;

            // Act
            (int actualDamage, bool isKill, bool armorBroken) result =
                HpArmorLogic.ApplyDamage(ref currentHp, ref currentArmor, rawDamage, armorBreakValue);

            // Assert
            Assert.AreEqual(70, currentHp, "HP should be reduced by raw damage");
            Assert.AreEqual(30, result.actualDamage, "Actual damage should equal raw damage");
            Assert.IsFalse(result.isKill, "Should not be killed with HP remaining");
            Assert.IsFalse(result.armorBroken, "Armor should not break when no armor break value");
        }

        [Test]
        public void HpArmorLogic_ApplyDamage_WhenHpZero_IsKill()
        {
            // Arrange
            int currentHp = 20;
            float currentArmor = 0f;
            int rawDamage = 30;
            float armorBreakValue = 0f;

            // Act
            (int actualDamage, bool isKill, bool armorBroken) result =
                HpArmorLogic.ApplyDamage(ref currentHp, ref currentArmor, rawDamage, armorBreakValue);

            // Assert
            Assert.AreEqual(0, currentHp, "HP should clamp to 0");
            Assert.IsTrue(result.isKill, "Should be killed when HP reaches 0");
        }

        [Test]
        public void HpArmorLogic_ApplyDamage_ArmorBreak_BonusDamage()
        {
            // Arrange
            int currentHp = 100;
            float currentArmor = 10f;
            int rawDamage = 20;
            float armorBreakValue = 15f;

            // Act
            (int actualDamage, bool isKill, bool armorBroken) result =
                HpArmorLogic.ApplyDamage(ref currentHp, ref currentArmor, rawDamage, armorBreakValue);

            // Assert
            Assert.IsTrue(result.armorBroken, "Armor should be broken when armorBreakValue exceeds currentArmor");
            Assert.LessOrEqual(currentArmor, 0f, "Armor should be 0 or less after break");

            int expectedDamage = Mathf.FloorToInt(rawDamage * HpArmorLogic.k_ArmorBreakBonusMult);
            Assert.AreEqual(expectedDamage, result.actualDamage,
                "Damage should be multiplied by armor break bonus");
            Assert.AreEqual(100 - expectedDamage, currentHp,
                "HP should reflect bonus damage from armor break");
        }

        [Test]
        public void HpArmorLogic_CalculateKnockback_AppliesResistance()
        {
            // Arrange
            Vector2 baseForce = new Vector2(10f, 5f);
            float knockbackResistance = 0.3f;

            // Act
            Vector2 knockback = HpArmorLogic.CalculateKnockback(baseForce, knockbackResistance);

            // Assert
            Assert.AreEqual(7f, knockback.x, 0.001f,
                "Knockback X should be reduced by resistance");
            Assert.AreEqual(3.5f, knockback.y, 0.001f,
                "Knockback Y should be reduced by resistance");
        }
    }
}
