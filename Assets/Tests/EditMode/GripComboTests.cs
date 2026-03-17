using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class GripComboTests
    {
        [Test]
        public void GripManager_ToggleGrip_SwitchesBetweenModes()
        {
            // Arrange
            GripManager grip = new GripManager(GripMode.OneHanded);
            Assert.AreEqual(GripMode.OneHanded, grip.CurrentGrip);

            // Act & Assert: OneHanded → TwoHanded
            GripMode result1 = grip.ToggleGrip();
            Assert.AreEqual(GripMode.TwoHanded, result1);
            Assert.AreEqual(GripMode.TwoHanded, grip.CurrentGrip);

            // Act & Assert: TwoHanded → OneHanded
            GripMode result2 = grip.ToggleGrip();
            Assert.AreEqual(GripMode.OneHanded, result2);
            Assert.AreEqual(GripMode.OneHanded, grip.CurrentGrip);
        }

        [Test]
        public void GripManager_DetermineSkillSource_ThreePatterns()
        {
            // 両手持ち → 常にWeapon
            SkillSource twoHanded = GripManager.DetermineSkillSource(GripMode.TwoHanded, hasShield: true);
            Assert.AreEqual(SkillSource.Weapon, twoHanded);

            // 片手持ち + 盾あり → Shield
            SkillSource oneHandedWithShield = GripManager.DetermineSkillSource(GripMode.OneHanded, hasShield: true);
            Assert.AreEqual(SkillSource.Shield, oneHandedWithShield);

            // 片手持ち + 盾なし → Weapon
            SkillSource oneHandedNoShield = GripManager.DetermineSkillSource(GripMode.OneHanded, hasShield: false);
            Assert.AreEqual(SkillSource.Weapon, oneHandedNoShield);
        }

        [Test]
        public void GripManager_GetTwoHandedAttackBonus_ReturnsBonus()
        {
            // 両手持ち → 1.15f
            float twoHandedBonus = GripManager.GetTwoHandedAttackBonus(GripMode.TwoHanded);
            Assert.AreEqual(1.15f, twoHandedBonus, 0.001f);

            // 片手持ち → 1.0f
            float oneHandedBonus = GripManager.GetTwoHandedAttackBonus(GripMode.OneHanded);
            Assert.AreEqual(1.0f, oneHandedBonus, 0.001f);
        }
    }
}
