using System;
using Game.Core;
using NUnit.Framework;
using R3;

namespace Game.Tests.EditMode
{
    public class CharacterStatsSystemTests
    {
        private sealed class World : IEquipmentWorld
        {
            public EquipmentActorState Actor;
            public bool TryGetActor(int hash, out EquipmentActorState actor) { actor = Actor; return hash == 1; }
            public void CommitActor(int hash, in EquipmentActorState actor) { Actor = actor; }
        }

        private sealed class Item : IEquippable, IEquipmentStatsProvider
        {
            public EquipSlot Slot => EquipSlot.Core;
            public int Weight => 2;
            public AbilityFlag GrantedFlags => AbilityFlag.WallKick;
            public StatusEffectApplicationData[] OnHitEffects => Array.Empty<StatusEffectApplicationData>();
            public EquipmentStatModifiers StatModifiers => new EquipmentStatModifiers
            {
                bonusHp = 100, attackBonus = new ElementalStatus { slash = 10 },
            };
            public void OnEquip(int hash) { }
            public void OnUnequip(int hash) { }
        }

        private static World CreateWorld()
        {
            return new World
            {
                Actor = new EquipmentActorState
                {
                    basis = new CharacterBaseStats
                    {
                        maxHp = 100, maxMp = 30, maxStamina = 80f,
                        combat = new CombatStats { attack = new ElementalStatus { slash = 20 } },
                    },
                    growth = new CharacterGrowth { level = 1 },
                    vitals = new CharacterVitals { currentHp = 80, maxHp = 100, currentMp = 17.5f, maxMp = 30 },
                },
            };
        }

        [Test]
        public void CharacterStats_GrowthUpdate_ReusesEquipmentInputsWithoutIntermediateClamp()
        {
            World world = CreateWorld();
            using EquipmentStore equipment = new EquipmentStore(world: world);
            equipment.Register(1);
            equipment.Equip(1, new Item());
            world.Actor.vitals.currentHp = 150;
            CharacterGrowth growth = new CharacterGrowth { level = 2, allocatedStats = new StatModifier { vit = 1 } };
            Assert.IsTrue(equipment.Stats.ApplyGrowth(1, growth));
            Assert.IsTrue(equipment.Stats.Recalculate(1));

            Assert.AreEqual(210, world.Actor.vitals.maxHp);
            Assert.AreEqual(150, world.Actor.vitals.currentHp);
            Assert.AreEqual(17.5f, world.Actor.vitals.currentMp);
            Assert.AreEqual(2, world.Actor.growth.level);
            Assert.AreEqual(30, world.Actor.combat.attack.slash);
            equipment.Unequip(1, EquipSlot.Core);
            Assert.AreEqual(110, world.Actor.vitals.maxHp);
            Assert.AreEqual(110, world.Actor.vitals.currentHp);
            Assert.AreEqual(20, world.Actor.combat.attack.slash);
        }

        [Test]
        public void CharacterStats_PermanentAbility_StaysAfterEquipmentRemovalAndNotifiesCommittedValues()
        {
            World world = CreateWorld();
            using GameEvents events = new GameEvents();
            using EquipmentStore equipment = new EquipmentStore(events, world);
            equipment.Register(1);
            equipment.Equip(1, new Item());
            int notifications = 0;
            using IDisposable subscription = events.OnCharacterStatsChanged.Subscribe(owner =>
            {
                Assert.AreEqual(1, owner);
                Assert.AreEqual(AbilityFlag.WallKick, world.Actor.publishedFlags);
                Assert.AreEqual(world.Actor.combat.attack, world.Actor.equipment.finalAttack);
                notifications++;
            });

            Assert.IsTrue(equipment.Stats.GrantAbilityFlags(1, AbilityFlag.WallKick));
            equipment.Unequip(1, EquipSlot.Core);

            Assert.AreEqual(AbilityFlag.WallKick, world.Actor.intrinsicFlags);
            Assert.AreEqual(AbilityFlag.WallKick, world.Actor.publishedFlags);
            Assert.AreEqual(2, notifications);
            IReadOnlyEquipmentHolder view = equipment.GetHolder(1);
            Assert.IsFalse(view.HasCore);
            Assert.AreEqual(0, view.GetTotalWeight());
        }
    }
}
