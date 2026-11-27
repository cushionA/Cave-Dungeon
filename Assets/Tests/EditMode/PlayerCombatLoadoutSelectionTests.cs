using System;
using System.IO;
using Game.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    public sealed class PlayerCombatLoadoutSelectionTests
    {
        private const int k_Weapon = 7101;
        private const int k_Core = 7201;
        private const int k_Spell = 7301;
        private const int k_Cooperation = 7401;

        [Test]
        public void Selection_WrongCategoryAndInvalidSlot_DoNotChangeLoadout()
        {
            using var world = new GameManagerCore();
            InventoryManager inventory = world.Inventory;
            PlayerCombatLoadoutSelection loadout = world.CombatLoadout;
            inventory.Add(k_Weapon, ItemCategory.Consumable, 1, 1);
            inventory.Add(k_Core, ItemCategory.Weapon, 1, 1);
            inventory.Add(k_Spell, ItemCategory.CompanionMagic, 1, 1);
            inventory.Add(k_Cooperation, ItemCategory.PlayerMagic, 1, 1);

            Assert.IsFalse(loadout.TryEquipWeapon(k_Weapon));
            Assert.IsFalse(loadout.TryEquipCore(k_Core));
            Assert.IsFalse(loadout.TryAssignSpell(0, k_Spell));
            Assert.IsFalse(loadout.TryEquipCooperation(PlayerCombatLoadoutSelection.k_EmptyItem));
            Assert.IsFalse(loadout.TryEquipCooperation(k_Cooperation));
            Assert.AreEqual(PlayerCombatLoadoutSelection.k_EmptyItem, loadout.CooperationId);
            inventory.Add(k_Cooperation, ItemCategory.CompanionMagic, 1, 1);
            Assert.IsTrue(loadout.TryEquipCooperation(k_Cooperation));
            Assert.AreEqual(k_Cooperation, loadout.CooperationId);
            Assert.AreEqual(1, inventory.GetCount(k_Cooperation, ItemCategory.CompanionMagic));
            Assert.IsFalse(loadout.TryInitializeCooperation());
            Assert.IsFalse(loadout.IsInitialized);
            inventory.Add(k_Spell, ItemCategory.PlayerMagic, 1, 1);
            Assert.IsFalse(loadout.TryAssignSpell(-1, k_Spell));
            Assert.IsFalse(loadout.TryAssignSpell(PlayerCombatLoadoutSelection.k_SpellSlotCount, k_Spell));
            Assert.IsFalse(loadout.TrySelectSpellSlot(-1));
            Assert.IsFalse(loadout.TryClearSpell(3));
            Assert.AreEqual(0, loadout.ActiveSpellSlot);
            Assert.AreEqual(-1, loadout.GetSpellId(0));
            Assert.IsTrue(loadout.TryAssignSpell(0, k_Spell));
            Assert.AreEqual(1, inventory.GetCount(k_Spell, ItemCategory.PlayerMagic));
        }

        [TestCase(k_Cooperation)]
        [TestCase(0)]
        public void Save_DiskRoundTrip_RestoresOwnedEquipmentSpellsAndCooperation(int cooperationId)
        {
            string directory = Path.Combine(Path.GetTempPath(), "PlayerLoadout_" + Guid.NewGuid().ToString("N"));
            try
            {
                using var source = new GameManagerCore();
                source.Inventory.Add(k_Weapon, ItemCategory.Weapon, 1, 1);
                source.Inventory.Add(k_Core, ItemCategory.Core, 1, 1);
                source.Inventory.Add(k_Spell, ItemCategory.PlayerMagic, 1, 1);
                Assert.AreEqual(1, source.Inventory.Add(cooperationId, ItemCategory.CompanionMagic, 1, 1));
                Assert.IsTrue(source.CombatLoadout.TryEquipWeapon(k_Weapon));
                Assert.IsTrue(source.CombatLoadout.TryEquipCore(k_Core));
                Assert.IsTrue(source.CombatLoadout.TryAssignSpell(2, k_Spell));
                Assert.IsTrue(source.CombatLoadout.TrySelectSpellSlot(2));
                Assert.IsTrue(source.CombatLoadout.TryEquipCooperation(cooperationId));
                SaveDataStore store = new SaveDataStore(directory);
                Assert.IsTrue(store.TryWriteToDisk(source.Saves.Save(1)));

                using var restored = new GameManagerCore();
                SaveSlotData slot = store.ReadFromDisk(1);
                Assert.IsNotNull(slot);
                restored.Saves.SetSlotData(1, slot);
                Assert.IsTrue(restored.Saves.Load(1));
                Assert.IsFalse(restored.CombatLoadout.TryInitialize());
                Assert.AreEqual(k_Weapon, restored.CombatLoadout.WeaponId);
                Assert.AreEqual(k_Core, restored.CombatLoadout.CoreId);
                Assert.AreEqual(2, restored.CombatLoadout.ActiveSpellSlot);
                Assert.AreEqual(k_Spell, restored.CombatLoadout.GetSpellId(2));
                Assert.AreEqual(-1, restored.CombatLoadout.GetSpellId(0));
                Assert.AreEqual(cooperationId, restored.CombatLoadout.CooperationId);
                Assert.IsFalse(restored.CombatLoadout.TryInitializeCooperation());
            }
            finally
            {
                if (Directory.Exists(directory)) { Directory.Delete(directory, true); }
            }
        }

        [Test]
        public void Save_ExplicitEmptyAndLegacyMissing_DistinguishStarterEligibility()
        {
            using var world = new GameManagerCore();
            PlayerCombatLoadoutSelection loadout = world.CombatLoadout;
            Assert.IsTrue(loadout.TryInitialize());
            Assert.IsFalse(loadout.TryInitialize());
            Assert.IsTrue(loadout.TryInitializeCooperation());
            world.Saves.Save(0);
            world.Saves.SetSlotData(1, new SaveSlotData(1));

            Assert.IsTrue(world.Saves.Load(1));
            Assert.AreEqual(-1, loadout.GetSpellId(0));
            Assert.IsTrue(loadout.TryInitialize());
            Assert.IsTrue(loadout.TryInitializeCooperation());
            Assert.IsTrue(world.Saves.Load(0));
            Assert.IsFalse(loadout.TryInitialize());
            Assert.IsFalse(loadout.TryInitializeCooperation());
            Assert.AreEqual(-1, loadout.WeaponId);
            Assert.AreEqual(-1, loadout.GetSpellId(0));
            Assert.AreEqual(PlayerCombatLoadoutSelection.k_EmptyItem, loadout.CooperationId);
        }

        [Test]
        public void Save_LegacyLoadoutWithoutCooperation_InitializesOnlyTheNewSlotOnce()
        {
            using GameManagerCore world = new GameManagerCore();
            PlayerCombatLoadoutSelection loadout = world.CombatLoadout;
            world.Inventory.Add(k_Weapon, ItemCategory.Weapon, 1, 1);
            world.Inventory.Add(k_Core, ItemCategory.Core, 1, 1);
            world.Inventory.Add(k_Spell, ItemCategory.PlayerMagic, 1, 1);
            world.Inventory.Add(k_Cooperation, ItemCategory.CompanionMagic, 1, 1);
            Assert.AreEqual(1, world.Inventory.Add(0, ItemCategory.CompanionMagic, 1, 1));
            Assert.IsTrue(loadout.TryEquipCooperation(k_Cooperation));
            SaveSlotData legacy = world.Saves.Save(0);
            legacy.entries[loadout.SaveId] = new JObject
            {
                ["initialized"] = true,
                ["weaponId"] = k_Weapon,
                ["coreId"] = k_Core,
                ["activeSpellSlot"] = 1,
                ["spellIds"] = new JArray(PlayerCombatLoadoutSelection.k_EmptyItem, k_Spell),
            };

            Assert.IsTrue(world.Saves.Load(0));
            Assert.AreEqual(PlayerCombatLoadoutSelection.k_EmptyItem, loadout.CooperationId);
            Assert.IsTrue(loadout.TryInitializeCooperation());
            Assert.IsFalse(loadout.TryInitializeCooperation());
            Assert.IsTrue(loadout.TryEquipCooperation(k_Cooperation));
            Assert.IsTrue(loadout.IsInitialized);
            Assert.AreEqual(k_Weapon, loadout.WeaponId);
            Assert.AreEqual(k_Core, loadout.CoreId);
            Assert.AreEqual(1, loadout.ActiveSpellSlot);
            Assert.AreEqual(k_Spell, loadout.GetSpellId(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Save_CooperationIdZeroWithoutInitialization_DoesNotEquipOwnedItem(bool includesInitializationMarker)
        {
            using GameManagerCore world = new GameManagerCore();
            PlayerCombatLoadoutSelection loadout = world.CombatLoadout;
            Assert.AreEqual(1, world.Inventory.Add(0, ItemCategory.CompanionMagic, 1, 1));
            Assert.IsTrue(loadout.TryEquipCooperation(0));
            SaveSlotData slot = world.Saves.Save(0);
            JObject snapshot = new JObject { ["cooperationId"] = 0 };
            if (includesInitializationMarker) { snapshot["cooperationInitialized"] = false; }
            slot.entries[loadout.SaveId] = snapshot;

            Assert.IsTrue(world.Saves.Load(0));
            Assert.AreEqual(1, world.Inventory.GetCount(0, ItemCategory.CompanionMagic));
            Assert.AreEqual(PlayerCombatLoadoutSelection.k_EmptyItem, loadout.CooperationId);
            Assert.IsFalse(loadout.IsCooperationInitialized);
            Assert.IsTrue(loadout.TryInitializeCooperation());
        }

        [Test]
        public void Save_SelectionWithoutMatchingInventory_RestoresEmptySlotsOnly()
        {
            using var world = new GameManagerCore();
            world.Inventory.Add(k_Weapon, ItemCategory.Weapon, 1, 1);
            world.Inventory.Add(k_Spell, ItemCategory.PlayerMagic, 1, 1);
            world.Inventory.Add(k_Cooperation, ItemCategory.CompanionMagic, 1, 1);
            PlayerCombatLoadoutSelection loadout = world.CombatLoadout;
            Assert.IsTrue(loadout.TryEquipWeapon(k_Weapon));
            Assert.IsTrue(loadout.TryAssignSpell(2, k_Spell));
            Assert.IsTrue(loadout.TrySelectSpellSlot(2));
            Assert.IsTrue(loadout.TryEquipCooperation(k_Cooperation));
            SaveSlotData slot = world.Saves.Save(0);
            slot.entries.Remove(world.Inventory.SaveId);

            Assert.IsTrue(world.Saves.Load(0));
            Assert.AreEqual(-1, loadout.WeaponId);
            Assert.AreEqual(-1, loadout.GetSpellId(2));
            Assert.AreEqual(2, loadout.ActiveSpellSlot);
            Assert.IsFalse(loadout.TryInitialize());
            Assert.AreEqual(PlayerCombatLoadoutSelection.k_EmptyItem, loadout.CooperationId);
            Assert.IsFalse(loadout.TryInitializeCooperation());
        }
    }
}
