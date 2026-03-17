using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class EquipmentSystemTests
    {
        private class MockEquippable : IEquippable
        {
            public EquipSlot Slot { get; set; }
            public int Weight { get; set; }
            public AbilityFlag GrantedFlags { get; set; }
            public bool WasEquipped { get; private set; }
            public bool WasUnequipped { get; private set; }
            public int EquippedOwnerHash { get; private set; }
            public int UnequippedOwnerHash { get; private set; }

            public void OnEquip(int ownerHash)
            {
                WasEquipped = true;
                EquippedOwnerHash = ownerHash;
            }

            public void OnUnequip(int ownerHash)
            {
                WasUnequipped = true;
                UnequippedOwnerHash = ownerHash;
            }
        }

        private const int k_OwnerHash = 12345;

        [Test]
        public void EquipmentHolder_Equip_SetsSlotAndCallsOnEquip()
        {
            // Arrange
            EquipmentHolder holder = new EquipmentHolder(k_OwnerHash);
            MockEquippable weapon = new MockEquippable
            {
                Slot = EquipSlot.Weapon,
                Weight = 5,
                GrantedFlags = AbilityFlag.None,
            };

            // Act
            IEquippable previous = holder.Equip(weapon);

            // Assert
            Assert.IsNull(previous);
            Assert.IsTrue(holder.HasWeapon);
            Assert.AreEqual(weapon, holder.Weapon);
            Assert.IsTrue(weapon.WasEquipped);
            Assert.AreEqual(k_OwnerHash, weapon.EquippedOwnerHash);
        }

        [Test]
        public void EquipmentHolder_Unequip_ClearsSlotAndCallsOnUnequip()
        {
            // Arrange
            EquipmentHolder holder = new EquipmentHolder(k_OwnerHash);
            MockEquippable shield = new MockEquippable
            {
                Slot = EquipSlot.Shield,
                Weight = 8,
                GrantedFlags = AbilityFlag.None,
            };
            holder.Equip(shield);

            // Act
            IEquippable removed = holder.Unequip(EquipSlot.Shield);

            // Assert
            Assert.AreEqual(shield, removed);
            Assert.IsFalse(holder.HasShield);
            Assert.IsNull(holder.Shield);
            Assert.IsTrue(shield.WasUnequipped);
            Assert.AreEqual(k_OwnerHash, shield.UnequippedOwnerHash);
        }

        [Test]
        public void EquipmentHolder_GetCombinedAbilityFlags_MergesAllSlots()
        {
            // Arrange
            EquipmentHolder holder = new EquipmentHolder(k_OwnerHash);
            MockEquippable weapon = new MockEquippable
            {
                Slot = EquipSlot.Weapon,
                Weight = 5,
                GrantedFlags = AbilityFlag.WallKick,
            };
            MockEquippable shield = new MockEquippable
            {
                Slot = EquipSlot.Shield,
                Weight = 8,
                GrantedFlags = AbilityFlag.WallCling,
            };
            MockEquippable core = new MockEquippable
            {
                Slot = EquipSlot.Core,
                Weight = 3,
                GrantedFlags = AbilityFlag.DoubleJump,
            };
            holder.Equip(weapon);
            holder.Equip(shield);
            holder.Equip(core);

            // Act
            AbilityFlag combined = holder.GetCombinedAbilityFlags();

            // Assert
            AbilityFlag expected = AbilityFlag.WallKick | AbilityFlag.WallCling | AbilityFlag.DoubleJump;
            Assert.AreEqual(expected, combined);
        }

        [Test]
        public void EquipmentHolder_Equip_ReplacesAndReturnsOld()
        {
            // Arrange
            EquipmentHolder holder = new EquipmentHolder(k_OwnerHash);
            MockEquippable oldWeapon = new MockEquippable
            {
                Slot = EquipSlot.Weapon,
                Weight = 5,
                GrantedFlags = AbilityFlag.WallKick,
            };
            MockEquippable newWeapon = new MockEquippable
            {
                Slot = EquipSlot.Weapon,
                Weight = 7,
                GrantedFlags = AbilityFlag.AirDash,
            };
            holder.Equip(oldWeapon);

            // Act
            IEquippable returned = holder.Equip(newWeapon);

            // Assert
            Assert.AreEqual(oldWeapon, returned);
            Assert.AreEqual(newWeapon, holder.Weapon);
            Assert.IsTrue(oldWeapon.WasUnequipped);
            Assert.IsTrue(newWeapon.WasEquipped);
        }
    }
}
