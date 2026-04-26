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
            float actionArmor = 0f;

            // Act
            (int actualDamage, bool isKill, bool armorBroken) result =
                HpArmorLogic.ApplyDamage(ref currentHp, ref currentArmor, rawDamage, armorBreakValue, ref actionArmor);

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
            float actionArmor = 0f;

            // Act
            (int actualDamage, bool isKill, bool armorBroken) result =
                HpArmorLogic.ApplyDamage(ref currentHp, ref currentArmor, rawDamage, armorBreakValue, ref actionArmor);

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
            float actionArmor = 0f;

            // Act
            (int actualDamage, bool isKill, bool armorBroken) result =
                HpArmorLogic.ApplyDamage(ref currentHp, ref currentArmor, rawDamage, armorBreakValue, ref actionArmor);

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

        // ===== 行動アーマー優先消費テスト =====

        [Test]
        public void HpArmorLogic_ApplyDamage_ActionArmorConsumedFirst()
        {
            // Arrange
            int currentHp = 100;
            float currentArmor = 20f;
            float actionArmor = 15f;
            int rawDamage = 20;
            float armorBreakValue = 10f;

            // Act
            HpArmorLogic.ApplyDamage(ref currentHp, ref currentArmor, rawDamage, armorBreakValue, ref actionArmor);

            // Assert
            Assert.AreEqual(5f, actionArmor, 0.001f, "Action armor should be reduced by break value");
            Assert.AreEqual(20f, currentArmor, 0.001f, "Base armor should remain untouched when action armor absorbs all");
        }

        [Test]
        public void HpArmorLogic_ApplyDamage_ActionArmorOverflow_GoesToBaseArmor()
        {
            // Arrange
            int currentHp = 100;
            float currentArmor = 20f;
            float actionArmor = 5f;
            int rawDamage = 20;
            float armorBreakValue = 12f;

            // Act
            HpArmorLogic.ApplyDamage(ref currentHp, ref currentArmor, rawDamage, armorBreakValue, ref actionArmor);

            // Assert
            Assert.AreEqual(0f, actionArmor, 0.001f, "Action armor should be fully consumed");
            Assert.AreEqual(13f, currentArmor, 0.001f, "Base armor should absorb remaining break value (20 - 7 = 13)");
            Assert.IsFalse(currentArmor <= 0f, "Base armor should not be broken");
        }

        [Test]
        public void HpArmorLogic_ApplyDamage_BothArmorsBroken_BonusDamage()
        {
            // Arrange
            int currentHp = 100;
            float currentArmor = 3f;
            float actionArmor = 2f;
            int rawDamage = 20;
            float armorBreakValue = 10f;

            // Act
            (int actualDamage, bool isKill, bool armorBroken) result =
                HpArmorLogic.ApplyDamage(ref currentHp, ref currentArmor, rawDamage, armorBreakValue, ref actionArmor);

            // Assert
            Assert.IsTrue(result.armorBroken, "Both armors depleted should trigger armor break");
            int expectedDamage = Mathf.FloorToInt(rawDamage * HpArmorLogic.k_ArmorBreakBonusMult);
            Assert.AreEqual(expectedDamage, result.actualDamage, "Should apply armor break bonus");
        }

        [Test]
        public void HpArmorLogic_ApplyDamage_ActionArmorOnly_NoBreakWhenBaseRemains()
        {
            // Arrange
            int currentHp = 100;
            float currentArmor = 20f;
            float actionArmor = 3f;
            int rawDamage = 20;
            float armorBreakValue = 5f;

            // Act
            (int actualDamage, bool isKill, bool armorBroken) result =
                HpArmorLogic.ApplyDamage(ref currentHp, ref currentArmor, rawDamage, armorBreakValue, ref actionArmor);

            // Assert
            Assert.IsFalse(result.armorBroken, "Armor break should not trigger when base armor remains");
            Assert.AreEqual(20, result.actualDamage, "Damage should not have bonus multiplier");
        }

        [Test]
        public void HpArmorLogic_ApplyDamage_WhenAlreadyBroken_DoesNotApplyBonusAgain()
        {
            // Issue #80 L1: 既に armor=0 のキャラに再ヒットした場合、armorBreakValue > 0 だけで
            // 連続的に 1.3 倍ボーナスが適用されないことを保証する。
            int currentHp = 100;
            float currentArmor = 0f;
            float actionArmor = 0f;
            int rawDamage = 20;
            float armorBreakValue = 5f;

            (int actualDamage, bool isKill, bool armorBroken) result =
                HpArmorLogic.ApplyDamage(ref currentHp, ref currentArmor, rawDamage, armorBreakValue, ref actionArmor);

            Assert.IsFalse(result.armorBroken, "既にブレイク済みのキャラは再ブレイク扱いにならないはず");
            Assert.AreEqual(rawDamage, result.actualDamage, "再ブレイクのボーナス 1.3 倍が乗らないはず");
            Assert.AreEqual(80, currentHp, "ボーナスなしの rawDamage が HP に通るはず");
        }

        // ===== アーマー回復テスト =====

        [Test]
        public void HpArmorLogic_RecoverArmor_IncreasesArmor()
        {
            // Arrange
            float currentArmor = 10f;
            float maxArmor = 50f;
            float recoveryRate = 5f;
            float deltaTime = 1f;

            // Act
            HpArmorLogic.RecoverArmor(ref currentArmor, maxArmor, recoveryRate, deltaTime);

            // Assert
            Assert.AreEqual(15f, currentArmor, 0.001f, "Armor should increase by rate * deltaTime");
        }

        [Test]
        public void HpArmorLogic_RecoverArmor_ClampsToMax()
        {
            // Arrange
            float currentArmor = 48f;
            float maxArmor = 50f;
            float recoveryRate = 10f;
            float deltaTime = 1f;

            // Act
            HpArmorLogic.RecoverArmor(ref currentArmor, maxArmor, recoveryRate, deltaTime);

            // Assert
            Assert.AreEqual(50f, currentArmor, 0.001f, "Armor should clamp to max");
        }

        [Test]
        public void HpArmorLogic_RecoverArmor_WhenAlreadyMax_NoChange()
        {
            // Arrange
            float currentArmor = 50f;
            float maxArmor = 50f;
            float recoveryRate = 5f;
            float deltaTime = 1f;

            // Act
            HpArmorLogic.RecoverArmor(ref currentArmor, maxArmor, recoveryRate, deltaTime);

            // Assert
            Assert.AreEqual(50f, currentArmor, 0.001f, "Armor should not exceed max");
        }

        [Test]
        public void HpArmorLogic_RecoverArmor_ZeroRate_NoChange()
        {
            // Arrange
            float currentArmor = 10f;
            float maxArmor = 50f;
            float recoveryRate = 0f;
            float deltaTime = 1f;

            // Act
            HpArmorLogic.RecoverArmor(ref currentArmor, maxArmor, recoveryRate, deltaTime);

            // Assert
            Assert.AreEqual(10f, currentArmor, 0.001f, "Armor should not recover with zero rate");
        }
    }
}
