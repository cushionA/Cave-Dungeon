using System;
using Game.Core;
using NUnit.Framework;
using R3;

namespace Game.Tests.EditMode
{
    public class EquipmentEffectTests
    {
        private sealed class Item : IEquippable
        {
            public EquipSlot Slot { get; set; }
            public int Weight => 0;
            public AbilityFlag GrantedFlags => AbilityFlag.None;
            public StatusEffectApplicationData[] OnHitEffects { get; set; } = Array.Empty<StatusEffectApplicationData>();
            public int UnequipCount { get; private set; }
            public void OnEquip(int ownerHash) { }
            public void OnUnequip(int ownerHash) { UnequipCount++; }
        }

        private static StatusEffectApplicationData Effect(StatusEffectId id)
        {
            return new StatusEffectApplicationData
            {
                info = new StatusEffectInfo { effect = id, duration = 10f, modifier = 0.5f },
                mode = StatusEffectApplicationMode.Immediate,
            };
        }

        [Test]
        public void EquipmentEffects_WeaponAttack_CombinesWeaponAndCoreWithAttackEffects()
        {
            EquipmentStore equipment = new EquipmentStore();
            equipment.Register(1);
            equipment.Equip(1, new Item { Slot = EquipSlot.Weapon, OnHitEffects = new[] { Effect(StatusEffectId.Poison) } });
            equipment.Equip(1, new Item { Slot = EquipSlot.Core, OnHitEffects = new[] { Effect(StatusEffectId.Slow) } });
            StatusEffectStore effects = new StatusEffectStore();

            StatusEffectApplicationLogic.Apply(new[] { Effect(StatusEffectId.Burn) }, 1, 2,
                GuardResult.NoGuard, 0f, effects);
            StatusEffectApplicationLogic.Apply(equipment.GetOnHitEffects(1, AttackEquipmentSource.Weapon),
                1, 2, GuardResult.NoGuard, 0f, effects);

            Assert.IsTrue(effects.IsActive(2, StatusEffectId.Burn));
            Assert.IsTrue(effects.IsActive(2, StatusEffectId.Poison));
            Assert.IsTrue(effects.IsActive(2, StatusEffectId.Slow));
            Assert.AreEqual(3, effects.ActiveCount);
        }

        [Test]
        public void EquipmentEffects_ShieldAndIndependentAttack_DoNotInheritWeaponEffects()
        {
            EquipmentHolder holder = new EquipmentHolder(1);
            holder.Equip(new Item { Slot = EquipSlot.Weapon, OnHitEffects = new[] { Effect(StatusEffectId.Poison) } });
            holder.Equip(new Item { Slot = EquipSlot.Shield, OnHitEffects = new[] { Effect(StatusEffectId.Stun) } });
            holder.Equip(new Item { Slot = EquipSlot.Core, OnHitEffects = new[] { Effect(StatusEffectId.Weakness) } });

            StatusEffectApplicationData[] shield = holder.GetOnHitEffects(AttackEquipmentSource.Shield);
            StatusEffectApplicationData[] independent = holder.GetOnHitEffects(AttackEquipmentSource.None);

            Assert.AreEqual(2, shield.Length);
            Assert.AreEqual(StatusEffectId.Stun, shield[0].info.effect);
            Assert.AreEqual(StatusEffectId.Weakness, shield[1].info.effect);
            Assert.AreEqual(1, independent.Length);
            Assert.AreEqual(StatusEffectId.Weakness, independent[0].info.effect);
        }

        [Test]
        public void EquipmentEffects_Replacement_KeepsOldProjectileSnapshotAndCachesCurrentSnapshot()
        {
            EquipmentHolder holder = new EquipmentHolder(1);
            Item first = new Item { Slot = EquipSlot.Weapon, OnHitEffects = new[] { Effect(StatusEffectId.Poison) } };
            holder.Equip(first);
            StatusEffectApplicationData[] snapshot = holder.GetOnHitEffects(AttackEquipmentSource.Weapon);
            first.OnHitEffects[0] = Effect(StatusEffectId.Curse);
            holder.Equip(new Item { Slot = EquipSlot.Weapon, OnHitEffects = new[] { Effect(StatusEffectId.Burn) } });

            StatusEffectApplicationData[] current = holder.GetOnHitEffects(AttackEquipmentSource.Weapon);

            Assert.AreEqual(StatusEffectId.Poison, snapshot[0].info.effect);
            Assert.AreEqual(StatusEffectId.Burn, current[0].info.effect);
            Assert.AreNotSame(snapshot, current);
            Assert.AreSame(current, holder.GetOnHitEffects(AttackEquipmentSource.Weapon));
            Assert.AreEqual(1, first.UnequipCount);
        }

        [Test]
        public void EquipmentEffects_ReequipAndUnequip_DoNotDuplicateOrLeaveRemovedEffects()
        {
            EquipmentHolder holder = new EquipmentHolder(1);
            Item weapon = new Item { Slot = EquipSlot.Weapon, OnHitEffects = new[] { Effect(StatusEffectId.Poison) } };
            holder.Equip(weapon);
            holder.Equip(weapon);

            Assert.AreEqual(1, holder.GetOnHitEffects(AttackEquipmentSource.Weapon).Length);
            holder.Unequip(EquipSlot.Weapon);
            Assert.IsEmpty(holder.GetOnHitEffects(AttackEquipmentSource.Weapon));
        }

        [Test]
        public void EquipmentEffects_UnregisterAndReuseOwner_DropsPreviousEquipmentAndCallsUnequip()
        {
            EquipmentStore equipment = new EquipmentStore();
            equipment.Register(1);
            Item weapon = new Item { Slot = EquipSlot.Weapon, OnHitEffects = new[] { Effect(StatusEffectId.Poison) } };
            equipment.Equip(1, weapon);
            equipment.Unregister(1);
            equipment.Register(1);

            Assert.IsEmpty(equipment.GetOnHitEffects(1, AttackEquipmentSource.Weapon));
            Assert.AreEqual(1, weapon.UnequipCount);
            Assert.IsFalse(equipment.GetHolder(1).HasWeapon);
            Assert.IsEmpty(equipment.GetOnHitEffects(999, AttackEquipmentSource.Weapon));
        }

        [Test]
        public void EquipmentEffects_CoreReplacement_RefreshesAllCachedAttackSources()
        {
            EquipmentHolder holder = new EquipmentHolder(1);
            holder.Equip(new Item { Slot = EquipSlot.Core, OnHitEffects = new[] { Effect(StatusEffectId.Slow) } });
            StatusEffectApplicationData[] old = holder.GetOnHitEffects(AttackEquipmentSource.Weapon);
            holder.Equip(new Item { Slot = EquipSlot.Core, OnHitEffects = new[] { Effect(StatusEffectId.Burn) } });

            foreach (AttackEquipmentSource source in Enum.GetValues(typeof(AttackEquipmentSource)))
            {
                Assert.AreEqual(StatusEffectId.Burn, holder.GetOnHitEffects(source)[0].info.effect);
            }
            Assert.AreEqual(StatusEffectId.Slow, old[0].info.effect);
        }

        [Test]
        public void EquipmentEffects_ChangeNotification_ObservesUpdatedSnapshotAndSkipsEmptyUnequip()
        {
            using GameEvents events = new GameEvents();
            using EquipmentStore equipment = new EquipmentStore(events);
            equipment.Register(1);
            int notifications = 0;
            using IDisposable subscription = events.OnEquipmentChanged.Subscribe(change =>
            {
                Assert.AreEqual(1, change.ownerHash);
                Assert.AreEqual(EquipSlot.Weapon, change.slot);
                int count = equipment.GetOnHitEffects(1, AttackEquipmentSource.Weapon).Length;
                Assert.AreEqual(notifications == 0 ? 1 : 0, count);
                notifications++;
            });

            equipment.Equip(1, new Item { Slot = EquipSlot.Weapon, OnHitEffects = new[] { Effect(StatusEffectId.Poison) } });
            equipment.Unequip(1, EquipSlot.Weapon);
            equipment.Unequip(1, EquipSlot.Weapon);

            Assert.AreEqual(2, notifications);
        }

        [Test]
        public void EquipmentEffects_Dispose_UnequipsAllOwnersOnceAndDropsSnapshots()
        {
            EquipmentStore equipment = new EquipmentStore();
            Item weapon = new Item { Slot = EquipSlot.Weapon, OnHitEffects = new[] { Effect(StatusEffectId.Poison) } };
            Item core = new Item { Slot = EquipSlot.Core, OnHitEffects = new[] { Effect(StatusEffectId.Slow) } };
            equipment.Register(1);
            equipment.Register(2);
            equipment.Equip(1, weapon);
            equipment.Equip(2, core);

            equipment.Dispose();
            equipment.Dispose();

            Assert.AreEqual(1, weapon.UnequipCount);
            Assert.AreEqual(1, core.UnequipCount);
            Assert.IsEmpty(equipment.GetOnHitEffects(1, AttackEquipmentSource.Weapon));
            Assert.IsEmpty(equipment.GetOnHitEffects(2, AttackEquipmentSource.None));
        }
    }
}
