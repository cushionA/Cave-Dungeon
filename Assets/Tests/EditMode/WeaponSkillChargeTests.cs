using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class WeaponSkillChargeTests
    {
        [Test]
        public void ChargeAttackLogic_GetChargeLevel_ReturnsCorrectLevel()
        {
            // Arrange
            ChargeAttackLogic charge = new ChargeAttackLogic();

            // Act & Assert — no charge → level 0
            Assert.AreEqual(0, charge.GetChargeLevel());

            // Act — charge to level 1
            charge.StartCharge();
            charge.UpdateCharge(ChargeAttackLogic.k_ChargeLevel1Time);

            // Assert — level 1
            Assert.AreEqual(1, charge.GetChargeLevel());

            // Act — charge to level 2
            charge.UpdateCharge(ChargeAttackLogic.k_ChargeLevel2Time - ChargeAttackLogic.k_ChargeLevel1Time);

            // Assert — level 2
            Assert.AreEqual(2, charge.GetChargeLevel());
        }

        [Test]
        public void ChargeAttackLogic_ReleaseCharge_ReturnsMultiplier()
        {
            // Arrange & Act — no charge release
            ChargeAttackLogic chargeNone = new ChargeAttackLogic();
            float multiplierNone = chargeNone.ReleaseCharge();

            // Assert — no charge → 1.0
            Assert.AreEqual(1.0f, multiplierNone);

            // Arrange & Act — level 1 release
            ChargeAttackLogic chargeLv1 = new ChargeAttackLogic();
            chargeLv1.StartCharge();
            chargeLv1.UpdateCharge(ChargeAttackLogic.k_ChargeLevel1Time);
            float multiplierLv1 = chargeLv1.ReleaseCharge();

            // Assert — level 1 → 1.5x
            Assert.AreEqual(ChargeAttackLogic.k_ChargeLevel1Multiplier, multiplierLv1);
            Assert.IsFalse(chargeLv1.IsCharging);

            // Arrange & Act — level 2 release
            ChargeAttackLogic chargeLv2 = new ChargeAttackLogic();
            chargeLv2.StartCharge();
            chargeLv2.UpdateCharge(ChargeAttackLogic.k_ChargeLevel2Time);
            float multiplierLv2 = chargeLv2.ReleaseCharge();

            // Assert — level 2 → 2.5x
            Assert.AreEqual(ChargeAttackLogic.k_ChargeLevel2Multiplier, multiplierLv2);
            Assert.IsFalse(chargeLv2.IsCharging);
        }

        [Test]
        public void SkillConditionLogic_CanAerialAttack_RequiresAirborne()
        {
            // grounded → false
            Assert.IsFalse(SkillConditionLogic.CanAerialAttack(isGrounded: true, isAlive: true));

            // airborne + alive → true
            Assert.IsTrue(SkillConditionLogic.CanAerialAttack(isGrounded: false, isAlive: true));

            // airborne + dead → false
            Assert.IsFalse(SkillConditionLogic.CanAerialAttack(isGrounded: false, isAlive: false));
        }

        [Test]
        public void SkillConditionLogic_CanUseSkill_ChecksResources()
        {
            // sufficient MP and stamina → true
            Assert.IsTrue(SkillConditionLogic.CanUseSkill(
                currentMp: 50f, mpCost: 30f,
                currentStamina: 40f, staminaCost: 20f));

            // exact MP and stamina → true
            Assert.IsTrue(SkillConditionLogic.CanUseSkill(
                currentMp: 30f, mpCost: 30f,
                currentStamina: 20f, staminaCost: 20f));

            // insufficient MP → false
            Assert.IsFalse(SkillConditionLogic.CanUseSkill(
                currentMp: 10f, mpCost: 30f,
                currentStamina: 40f, staminaCost: 20f));

            // insufficient stamina → false
            Assert.IsFalse(SkillConditionLogic.CanUseSkill(
                currentMp: 50f, mpCost: 30f,
                currentStamina: 10f, staminaCost: 20f));

            // both insufficient → false
            Assert.IsFalse(SkillConditionLogic.CanUseSkill(
                currentMp: 5f, mpCost: 30f,
                currentStamina: 5f, staminaCost: 20f));
        }
    }
}
