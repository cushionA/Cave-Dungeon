using System;
using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    public class EquipmentReadOnlyEffectTests
    {
        private sealed class Item : IEquippable
        {
            public EquipSlot Slot { get; set; }
            public int Weight => 0;
            public AbilityFlag GrantedFlags => AbilityFlag.None;
            public StatusEffectApplicationData[] OnHitEffects { get; set; }
            public void OnEquip(int ownerHash) { }
            public void OnUnequip(int ownerHash) { }
        }

        private static Item EffectItem(EquipSlot slot, StatusEffectId id)
            => new Item
            {
                Slot = slot,
                OnHitEffects = new[]
                {
                    new StatusEffectApplicationData
                    {
                        info = new StatusEffectInfo { effect = id, duration = 5f },
                        mode = StatusEffectApplicationMode.Immediate,
                    },
                },
            };

        [TestCase(AttackEquipmentSource.Weapon)]
        [TestCase(AttackEquipmentSource.Shield)]
        [TestCase(AttackEquipmentSource.None)]
        public void EquipmentEffects_PublicView_RejectsMutationWithoutChangingFutureEffects(AttackEquipmentSource source)
        {
            using EquipmentStore equipment = new EquipmentStore();
            equipment.Register(1);
            equipment.Equip(1, EffectItem(EquipSlot.Weapon, StatusEffectId.Poison));
            equipment.Equip(1, EffectItem(EquipSlot.Shield, StatusEffectId.Burn));
            equipment.Equip(1, EffectItem(EquipSlot.Core, StatusEffectId.Slow));
            IReadOnlyList<StatusEffectApplicationData> view = equipment.GetOnHitEffects(1, source);
            StatusEffectId expected = view[0].info.effect;
            IList<StatusEffectApplicationData> collection = view as IList<StatusEffectApplicationData>;
            Assert.IsNotNull(collection);

            Assert.Throws<NotSupportedException>(() => collection[0] = new StatusEffectApplicationData());

            Assert.AreEqual(expected, equipment.GetOnHitEffects(1, source)[0].info.effect);
        }

        [Test]
        public void EquipmentEffects_PublicView_IsStableUntilEquipmentChanges()
        {
            EquipmentHolder holder = new EquipmentHolder(1);
            holder.Equip(EffectItem(EquipSlot.Weapon, StatusEffectId.Poison));
            IReadOnlyList<StatusEffectApplicationData> first = holder.GetOnHitEffects(AttackEquipmentSource.Weapon);
            Assert.AreSame(first, holder.GetOnHitEffects(AttackEquipmentSource.Weapon));

            holder.Equip(EffectItem(EquipSlot.Weapon, StatusEffectId.Burn));
            IReadOnlyList<StatusEffectApplicationData> second = holder.GetOnHitEffects(AttackEquipmentSource.Weapon);

            Assert.AreNotSame(first, second);
            Assert.AreEqual(StatusEffectId.Poison, first[0].info.effect);
            Assert.AreEqual(StatusEffectId.Burn, second[0].info.effect);
        }
    }
}
