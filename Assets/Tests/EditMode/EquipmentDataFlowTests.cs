using System;
using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;
using R3;

namespace Game.Tests.EditMode
{
    public class EquipmentDataFlowTests
    {
        private sealed class FakeWorld : IEquipmentWorld
        {
            public readonly Dictionary<int, EquipmentActorState> Actors = new Dictionary<int, EquipmentActorState>();
            public bool TryGetActor(int ownerHash, out EquipmentActorState actor) => Actors.TryGetValue(ownerHash, out actor);
            public void CommitActor(int ownerHash, in EquipmentActorState actor) => Actors[ownerHash] = actor;
        }

        private sealed class TestEquipment : IEquippable, IEquipmentStatsProvider
        {
            public EquipSlot Slot { get; set; }
            public int Weight { get; set; }
            public AbilityFlag GrantedFlags { get; set; }
            public EquipmentStatModifiers StatModifiers { get; set; }
            public StatusEffectApplicationData[] OnHitEffects => Array.Empty<StatusEffectApplicationData>();
            public void OnEquip(int ownerHash) { }
            public void OnUnequip(int ownerHash) { }
        }

        private const int k_Owner = 7;

        private static FakeWorld CreateWorld(AbilityFlag intrinsic = AbilityFlag.None)
        {
            FakeWorld world = new FakeWorld();
            world.Actors.Add(k_Owner, new EquipmentActorState
            {
                intrinsicFlags = intrinsic,
                publishedFlags = intrinsic,
                basis = new CharacterBaseStats
                {
                    maxHp = 100, maxMp = 30, maxStamina = 50f, maxArmor = 20f,
                    attributes = new StatModifier { str = 10 },
                    combat = new CombatStats
                    {
                        attack = new ElementalStatus { slash = 43 },
                        defense = new ElementalStatus { fire = 7 },
                        guardStats = new GuardStats { guardStrength = 15f },
                    },
                },
                growth = new CharacterGrowth { level = 1 },
                vitals = new CharacterVitals
                {
                    currentHp = 63, currentMp = 17.5f, currentStamina = 21.25f, currentArmor = 4.5f,
                    position = new UnityEngine.Vector2(2f, 3f),
                },
                equipment = new EquipmentStatus
                {
                    gripMode = GripMode.TwoHanded,
                    finalAttack = new ElementalStatus { slash = 999 },
                    maxWeightCapacity = 100f,
                    weightRatio = 0.25f,
                },
            });
            return world;
        }

        [Test]
        public void EquipmentDataFlow_RepeatedReplacementAndRemoval_RecalculatesWithoutAccumulating()
        {
            FakeWorld world = CreateWorld(AbilityFlag.DoubleJump);
            using EquipmentStore equipment = new EquipmentStore(world: world);
            equipment.Register(k_Owner);
            TestEquipment first = new TestEquipment
            {
                Slot = EquipSlot.Weapon, Weight = 5, GrantedFlags = AbilityFlag.WallKick,
                StatModifiers = new EquipmentStatModifiers
                {
                    attributes = new StatModifier { str = 40, vit = 1 },
                    attackBonus = new ElementalStatus { slash = 20 }, strengthScaling = 1f,
                },
            };
            TestEquipment second = new TestEquipment
            {
                Slot = EquipSlot.Weapon, Weight = 8, GrantedFlags = AbilityFlag.WallCling,
                StatModifiers = new EquipmentStatModifiers
                {
                    attributes = new StatModifier { str = 90 },
                    attackBonus = new ElementalStatus { slash = 10 }, strengthScaling = 1f,
                },
            };
            for (int i = 0; i < 4; i++)
            {
                equipment.Equip(k_Owner, first);
                Assert.AreEqual(5f, world.Actors[k_Owner].equipment.totalWeight);
                Assert.AreEqual(73, world.Actors[k_Owner].combat.attack.slash);
                Assert.AreEqual(110, world.Actors[k_Owner].vitals.maxHp);
                Assert.AreEqual(63, world.Actors[k_Owner].vitals.currentHp);
                equipment.Equip(k_Owner, second);
                Assert.AreEqual(8f, world.Actors[k_Owner].equipment.totalWeight);
                Assert.AreEqual(63, world.Actors[k_Owner].combat.attack.slash);
                Assert.AreEqual(100, world.Actors[k_Owner].vitals.maxHp);
                Assert.AreEqual(AbilityFlag.DoubleJump | AbilityFlag.WallCling, world.Actors[k_Owner].publishedFlags);
                equipment.Unequip(k_Owner, EquipSlot.Weapon);
                Assert.AreEqual(0f, world.Actors[k_Owner].equipment.totalWeight);
                Assert.AreEqual(43, world.Actors[k_Owner].combat.attack.slash);
                Assert.AreEqual(default(StatModifier), world.Actors[k_Owner].equipment.attributeModifiers);
                Assert.AreEqual(AbilityFlag.DoubleJump, world.Actors[k_Owner].publishedFlags);
            }
            EquipmentStatus status = world.Actors[k_Owner].equipment;
            Assert.AreEqual(GripMode.TwoHanded, status.gripMode);
            Assert.AreEqual(43, status.finalAttack.slash);
            Assert.AreEqual(100f, status.maxWeightCapacity);
            Assert.AreEqual(0.25f, status.weightRatio);
        }

        [Test]
        public void EquipmentDataFlow_GrowthAndRecalculation_UsesBasisAndCurrentResources()
        {
            FakeWorld world = CreateWorld();
            using EquipmentStore equipment = new EquipmentStore(world: world);
            equipment.Register(k_Owner);
            equipment.Equip(k_Owner, new TestEquipment
            {
                Slot = EquipSlot.Weapon,
                StatModifiers = new EquipmentStatModifiers
                {
                    attributes = new StatModifier { str = 40, vit = 1, mnd = 1 },
                    attackBonus = new ElementalStatus { slash = 20 }, strengthScaling = 1f,
                    defenseBonus = new ElementalStatus { fire = 3 },
                    bonusHp = 5, bonusMp = 2, bonusStamina = 3f, bonusArmor = 4f,
                },
            });
            EquipmentActorState actor = world.Actors[k_Owner];
            actor.growth = new CharacterGrowth
            {
                level = 3, allocatedStats = new StatModifier { str = 50, vit = 2, mnd = 3 },
            };
            world.Actors[k_Owner] = actor;

            equipment.Recalculate(k_Owner);
            equipment.Recalculate(k_Owner);

            actor = world.Actors[k_Owner];
            Assert.AreEqual(83, actor.combat.attack.slash);
            Assert.AreEqual(10, actor.combat.defense.fire);
            Assert.AreEqual(135, actor.vitals.maxHp);
            Assert.AreEqual(52, actor.vitals.maxMp);
            Assert.AreEqual(53f, actor.vitals.maxStamina);
            Assert.AreEqual(24f, actor.vitals.maxArmor);
            Assert.AreEqual(63, actor.vitals.currentHp);
            Assert.AreEqual(17.5f, actor.vitals.currentMp);
            Assert.AreEqual(21.25f, actor.vitals.currentStamina);
            Assert.AreEqual(4.5f, actor.vitals.currentArmor);
            Assert.AreEqual(new UnityEngine.Vector2(2f, 3f), actor.vitals.position);
            Assert.AreEqual(100, actor.basis.maxHp);
            Assert.AreEqual(43, actor.basis.combat.attack.slash);
            Assert.AreEqual(40, actor.equipment.attributeModifiers.str);
            Assert.AreEqual(actor.combat.attack, actor.equipment.finalAttack);
            Assert.AreEqual(actor.combat.defense, actor.equipment.finalDefense);
        }

        [Test]
        public void EquipmentDataFlow_StatsNotification_ObservesDerivedValuesAfterEquipmentAndGrant()
        {
            FakeWorld world = CreateWorld();
            using GameEvents events = new GameEvents();
            using EquipmentStore equipment = new EquipmentStore(events, world);
            equipment.Register(k_Owner);
            int notifications = 0;
            using IDisposable subscription = events.OnCharacterStatsChanged.Subscribe(ownerHash =>
            {
                notifications++;
                EquipmentActorState actor = world.Actors[ownerHash];
                Assert.AreEqual(50, actor.combat.attack.slash);
                Assert.AreEqual(actor.combat.attack, actor.equipment.finalAttack);
                Assert.AreEqual(110, actor.vitals.maxHp);
                Assert.AreEqual(1, actor.equipment.attributeModifiers.vit);
            });

            equipment.Equip(k_Owner, new TestEquipment
            {
                Slot = EquipSlot.Core,
                StatModifiers = new EquipmentStatModifiers
                {
                    attributes = new StatModifier { vit = 1 }, attackBonus = new ElementalStatus { slash = 7 },
                },
            });
            Assert.IsTrue(equipment.GrantAbilityFlags(k_Owner, AbilityFlag.DoubleJump));

            Assert.AreEqual(2, notifications);
        }

        [Test]
        public void EquipmentDataFlow_DuplicateEquipmentFlags_RemainUntilLastGrantIsRemoved()
        {
            FakeWorld world = CreateWorld();
            using EquipmentStore equipment = new EquipmentStore(world: world);
            equipment.Register(k_Owner);
            equipment.Equip(k_Owner, new TestEquipment { Slot = EquipSlot.Weapon, Weight = 3, GrantedFlags = AbilityFlag.WallKick });
            equipment.Equip(k_Owner, new TestEquipment { Slot = EquipSlot.Core, Weight = 2, GrantedFlags = AbilityFlag.WallKick });
            Assert.AreEqual(5f, world.Actors[k_Owner].equipment.totalWeight);
            equipment.Unequip(k_Owner, EquipSlot.Weapon);
            Assert.AreEqual(AbilityFlag.WallKick, world.Actors[k_Owner].publishedFlags);
            Assert.AreEqual(2f, world.Actors[k_Owner].equipment.totalWeight);
            equipment.Unequip(k_Owner, EquipSlot.Core);
            Assert.AreEqual(AbilityFlag.None, world.Actors[k_Owner].publishedFlags);
        }

        [Test]
        public void EquipmentDataFlow_ProgressionGrantsSameFlagAsEquipment_PreservesIntrinsicAndAcquiredFlags()
        {
            FakeWorld world = CreateWorld(AbilityFlag.DoubleJump);
            using EquipmentStore equipment = new EquipmentStore(world: world);
            equipment.Register(k_Owner);
            equipment.Equip(k_Owner, new TestEquipment { Slot = EquipSlot.Weapon, GrantedFlags = AbilityFlag.WallKick });
            Assert.IsTrue(equipment.GrantAbilityFlags(k_Owner, AbilityFlag.WallKick | AbilityFlag.WallCling));
            equipment.Unequip(k_Owner, EquipSlot.Weapon);
            AbilityFlag expected = AbilityFlag.DoubleJump | AbilityFlag.WallKick | AbilityFlag.WallCling;
            Assert.AreEqual(expected, world.Actors[k_Owner].intrinsicFlags);
            Assert.AreEqual(expected, world.Actors[k_Owner].publishedFlags);
            Assert.AreEqual(AbilityFlag.None, world.Actors[k_Owner].equipment.activeFlags);
        }

        [Test]
        public void EquipmentDataFlow_ChangeNotifications_ObserveCommittedFlagsAndWeight()
        {
            FakeWorld world = CreateWorld(AbilityFlag.DoubleJump);
            using GameEvents events = new GameEvents();
            using EquipmentStore equipment = new EquipmentStore(events, world);
            equipment.Register(k_Owner);
            int equipmentNotifications = 0;
            int flagNotifications = 0;
            using IDisposable equipmentSubscription = events.OnEquipmentChanged.Subscribe(change =>
            {
                Assert.AreEqual(k_Owner, change.ownerHash);
                Assert.AreEqual(AbilityFlag.DoubleJump | AbilityFlag.WallKick, world.Actors[k_Owner].publishedFlags);
                Assert.AreEqual(AbilityFlag.WallKick, world.Actors[k_Owner].equipment.activeFlags);
                Assert.AreEqual(9f, world.Actors[k_Owner].equipment.totalWeight);
                equipmentNotifications++;
            });
            using IDisposable flagSubscription = events.OnAbilityFlagsChanged.Subscribe(change =>
            {
                Assert.AreEqual(world.Actors[k_Owner].publishedFlags, change.newFlags);
                Assert.AreEqual(9f, world.Actors[k_Owner].equipment.totalWeight);
                flagNotifications++;
            });
            equipment.Equip(k_Owner, new TestEquipment { Slot = EquipSlot.Shield, Weight = 9, GrantedFlags = AbilityFlag.WallKick });
            Assert.AreEqual(1, equipmentNotifications);
            Assert.AreEqual(1, flagNotifications);
            Assert.IsFalse(equipment.GrantAbilityFlags(k_Owner, AbilityFlag.DoubleJump));
            Assert.IsTrue(equipment.GrantAbilityFlags(k_Owner, AbilityFlag.WallKick));
            Assert.AreEqual(1, flagNotifications);
            Assert.IsTrue(equipment.GrantAbilityFlags(k_Owner, AbilityFlag.WallCling));
            Assert.AreEqual(2, flagNotifications);
        }

        [Test]
        public void EquipmentDataFlow_UnregisterAndRegisterSameHash_DropsPreviousEquipment()
        {
            FakeWorld world = CreateWorld();
            using EquipmentStore equipment = new EquipmentStore(world: world);
            equipment.Register(k_Owner);
            equipment.Equip(k_Owner, new TestEquipment { Slot = EquipSlot.Weapon, Weight = 8, GrantedFlags = AbilityFlag.WallKick });
            equipment.Unregister(k_Owner);
            Assert.AreEqual(0f, world.Actors[k_Owner].equipment.totalWeight);
            Assert.AreEqual(AbilityFlag.None, world.Actors[k_Owner].publishedFlags);
            world.Actors[k_Owner] = new EquipmentActorState { intrinsicFlags = AbilityFlag.DoubleJump };
            equipment.Register(k_Owner);
            Assert.IsFalse(equipment.GetHolder(k_Owner).HasWeapon);
            Assert.AreEqual(AbilityFlag.DoubleJump, world.Actors[k_Owner].publishedFlags);
            Assert.AreEqual(AbilityFlag.None, world.Actors[k_Owner].equipment.activeFlags);
        }

        [Test]
        public void EquipmentDataFlow_Dispose_ClearsAllEquipmentAndPreservesIntrinsicFlags()
        {
            FakeWorld world = CreateWorld(AbilityFlag.DoubleJump);
            EquipmentStore equipment = new EquipmentStore(world: world);
            equipment.Register(k_Owner);
            equipment.Equip(k_Owner, new TestEquipment { Slot = EquipSlot.Core, Weight = 6, GrantedFlags = AbilityFlag.WallKick });
            equipment.Dispose();
            equipment.Dispose();
            Assert.AreEqual(AbilityFlag.DoubleJump, world.Actors[k_Owner].publishedFlags);
            Assert.AreEqual(0f, world.Actors[k_Owner].equipment.totalWeight);
            Assert.IsFalse(equipment.GrantAbilityFlags(k_Owner, AbilityFlag.WallCling));
            Assert.Throws<KeyNotFoundException>(() => equipment.GetHolder(k_Owner));
        }

        [Test]
        public void EquipmentDataFlow_EquipmentNotificationUnequips_ReentrantChangeDoesNotPublishStaleFlags()
        {
            FakeWorld world = CreateWorld(AbilityFlag.DoubleJump);
            using GameEvents events = new GameEvents();
            using EquipmentStore equipment = new EquipmentStore(events, world);
            equipment.Register(k_Owner);
            List<AbilityFlag> notified = new List<AbilityFlag>();
            List<int> notifiedAttack = new List<int>();
            using IDisposable flagsSubscription = events.OnAbilityFlagsChanged.Subscribe(change => notified.Add(change.newFlags));
            using IDisposable statsSubscription = events.OnCharacterStatsChanged.Subscribe(owner =>
                notifiedAttack.Add(world.Actors[owner].combat.attack.slash));
            using IDisposable equipmentSubscription = events.OnEquipmentChanged.Subscribe(change =>
            {
                if (equipment.GetHolder(change.ownerHash).HasWeapon)
                {
                    equipment.Unequip(change.ownerHash, EquipSlot.Weapon);
                }
            });
            equipment.Equip(k_Owner, new TestEquipment
            {
                Slot = EquipSlot.Weapon, Weight = 8, GrantedFlags = AbilityFlag.WallKick,
                StatModifiers = new EquipmentStatModifiers { attackBonus = new ElementalStatus { slash = 20 } },
            });
            Assert.AreEqual(AbilityFlag.DoubleJump, world.Actors[k_Owner].publishedFlags);
            Assert.AreEqual(0f, world.Actors[k_Owner].equipment.totalWeight);
            CollectionAssert.DoesNotContain(notified, AbilityFlag.DoubleJump | AbilityFlag.WallKick);
            CollectionAssert.AreEqual(new[] { 43, 43 }, notifiedAttack);
        }

        [Test]
        public void EquipmentDataFlow_EquipmentNotificationAddsSameFlag_PublishesUnnotifiedChangeOnce()
        {
            FakeWorld world = CreateWorld();
            using GameEvents events = new GameEvents();
            using EquipmentStore equipment = new EquipmentStore(events, world);
            equipment.Register(k_Owner);
            List<AbilityFlag> notified = new List<AbilityFlag>();
            using IDisposable flagsSubscription = events.OnAbilityFlagsChanged.Subscribe(change => notified.Add(change.newFlags));
            using IDisposable equipmentSubscription = events.OnEquipmentChanged.Subscribe(change =>
            {
                if (change.slot == EquipSlot.Weapon)
                {
                    equipment.Equip(change.ownerHash, new TestEquipment { Slot = EquipSlot.Core, Weight = 2, GrantedFlags = AbilityFlag.WallKick });
                }
            });
            equipment.Equip(k_Owner, new TestEquipment { Slot = EquipSlot.Weapon, Weight = 3, GrantedFlags = AbilityFlag.WallKick });
            CollectionAssert.AreEqual(new[] { AbilityFlag.WallKick }, notified);
            Assert.AreEqual(AbilityFlag.WallKick, world.Actors[k_Owner].publishedFlags);
            Assert.AreEqual(5f, world.Actors[k_Owner].equipment.totalWeight);
        }

        [Test]
        public void EquipmentDataFlow_EquipmentNotificationReusesOwner_DoesNotPublishPreviousRegistrationFlags()
        {
            FakeWorld world = CreateWorld();
            using GameEvents events = new GameEvents();
            using EquipmentStore equipment = new EquipmentStore(events, world);
            equipment.Register(k_Owner);
            List<AbilityFlag> notified = new List<AbilityFlag>();
            int statsNotifications = 0;
            using IDisposable flagsSubscription = events.OnAbilityFlagsChanged.Subscribe(change => notified.Add(change.newFlags));
            using IDisposable statsSubscription = events.OnCharacterStatsChanged.Subscribe(_ => statsNotifications++);
            using IDisposable equipmentSubscription = events.OnEquipmentChanged.Subscribe(change =>
            {
                equipment.Unregister(change.ownerHash);
                world.Actors[change.ownerHash] = new EquipmentActorState { intrinsicFlags = AbilityFlag.WallCling };
                equipment.Register(change.ownerHash);
            });
            equipment.Equip(k_Owner, new TestEquipment { Slot = EquipSlot.Weapon, Weight = 3, GrantedFlags = AbilityFlag.WallKick });
            Assert.AreEqual(AbilityFlag.WallCling, world.Actors[k_Owner].publishedFlags);
            Assert.AreEqual(0f, world.Actors[k_Owner].equipment.totalWeight);
            Assert.IsEmpty(notified);
            Assert.AreEqual(0, statsNotifications);
        }
    }
}
