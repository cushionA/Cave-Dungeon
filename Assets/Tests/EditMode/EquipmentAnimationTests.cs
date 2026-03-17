using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class EquipmentAnimationTests
    {
        private const int k_AnimIdSword = 100;
        private const int k_AnimIdAxe = 200;
        private const int k_SpriteIdWoodShield = 300;
        private const int k_SpriteIdIronShield = 400;

        [Test]
        public void EquipmentVisualState_SetWeaponAnim_SetsDirtyFlag()
        {
            // Arrange
            EquipmentVisualState state = new EquipmentVisualState();

            // Act: 新しいIDを設定
            state.SetWeaponAnim(k_AnimIdSword);

            // Assert: dirtyフラグが立つ
            Assert.IsTrue(state.IsWeaponAnimDirty);
            Assert.AreEqual(k_AnimIdSword, state.CurrentWeaponAnimId);

            // Act: dirtyクリア後に同じIDを設定
            state.ClearDirtyFlags();
            state.SetWeaponAnim(k_AnimIdSword);

            // Assert: 同じIDなのでdirtyは立たない
            Assert.IsFalse(state.IsWeaponAnimDirty);

            // Act: 異なるIDを設定
            state.SetWeaponAnim(k_AnimIdAxe);

            // Assert: 異なるIDなのでdirtyが立つ
            Assert.IsTrue(state.IsWeaponAnimDirty);
            Assert.AreEqual(k_AnimIdAxe, state.CurrentWeaponAnimId);
        }

        [Test]
        public void EquipmentVisualState_SetShieldSprite_SetsDirtyFlag()
        {
            // Arrange
            EquipmentVisualState state = new EquipmentVisualState();

            // Act: 新しいIDを設定
            state.SetShieldSprite(k_SpriteIdWoodShield);

            // Assert: dirtyフラグが立つ
            Assert.IsTrue(state.IsShieldSpriteDirty);
            Assert.AreEqual(k_SpriteIdWoodShield, state.CurrentShieldSpriteId);

            // Act: ClearDirtyFlagsでクリア
            state.ClearDirtyFlags();

            // Assert: dirtyフラグがクリアされた
            Assert.IsFalse(state.IsShieldSpriteDirty);

            // Act: 異なるIDを設定
            state.SetShieldSprite(k_SpriteIdIronShield);

            // Assert: 異なるIDなのでdirtyが立つ
            Assert.IsTrue(state.IsShieldSpriteDirty);
            Assert.AreEqual(k_SpriteIdIronShield, state.CurrentShieldSpriteId);
        }
    }
}
