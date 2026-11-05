using System;
using Game.Core;
using NUnit.Framework;
using R3;
using UnityEngine;
using CharacterInfo = Game.Core.CharacterInfo;

namespace Game.Tests.EditMode
{
    public class StatusDefenseAuthoringTests
    {
        private sealed class World : IEquipmentWorld
        {
            public EquipmentActorState Actor;
            public bool TryGetActor(int hash, out EquipmentActorState actor) { actor = Actor; return hash == 1; }
            public void CommitActor(int hash, in EquipmentActorState actor) { Actor = actor; }
        }

        private sealed class Item : IEquippable, IEquipmentStatsProvider
        {
            private readonly EquipmentStatModifiers _modifiers;
            public EquipSlot Slot { get; }
            public int Weight => 0;
            public AbilityFlag GrantedFlags => AbilityFlag.None;
            public StatusEffectApplicationData[] OnHitEffects => Array.Empty<StatusEffectApplicationData>();
            public EquipmentStatModifiers StatModifiers => _modifiers;
            public int EquipCalls { get; private set; }
            public int UnequipCalls { get; private set; }

            public Item(EquipSlot slot, StatusEffectDefenseProfile profile)
            {
                Slot = slot;
                _modifiers = new EquipmentStatModifiers { statusDefense = profile };
            }

            public void OnEquip(int hash) { EquipCalls++; }
            public void OnUnequip(int hash) { UnequipCalls++; }
        }

        private static CharacterInfo Info()
        {
            return ScriptableObject.CreateInstance<CharacterInfo>();
        }

        private static StatusEffectDefenseProfile Profile(float common, float poisonCut = 0f,
            float poisonThreshold = 0f, bool curseImmune = false, float thresholdMultiplierBonus = 0f)
        {
            return new StatusEffectDefenseProfile
            {
                commonReduction = common,
                thresholdMultiplierBonus = thresholdMultiplierBonus,
                poison = new StatusEffectDefense { accumulationReduction = poisonCut, threshold = poisonThreshold },
                curse = new StatusEffectDefense { immune = curseImmune },
            };
        }

        [Test]
        public void CharacterRegistration_LegacyResistance_ReachesAllEffectThresholdsWithoutCuttingAccumulation()
        {
            CharacterInfo info = Info();
            try
            {
                info.statusResistance = 0.35f;

                CharacterRegistration registration = info.CreateRegistration(9.81f);

                Assert.AreEqual(0.35f, registration.baseStats.combat.statusDefense.thresholdMultiplierBonus, 0.00001f);
                Assert.AreEqual(135f, registration.baseStats.combat.statusDefense.Resolve(StatusEffectId.Poison).threshold, 0.00001f);
                Assert.AreEqual(135f, registration.baseStats.combat.statusDefense.Resolve(StatusEffectId.Burn).threshold, 0.00001f);
                Assert.AreEqual(0f, registration.baseStats.combat.statusDefense.Resolve(StatusEffectId.Poison).accumulationReduction);
            }
            finally { UnityEngine.Object.DestroyImmediate(info); }
        }

        [Test]
        public void CharacterRegistration_NewAndLegacyInputs_CombineOnceWithoutMutatingAuthoring()
        {
            CharacterInfo info = Info();
            try
            {
                info.statusResistance = 0.2f;
                info.statusDefense = Profile(0.3f, 0.1f, 180f, true, 0.3f);

                CharacterRegistration first = info.CreateRegistration(9.81f);
                CharacterRegistration second = info.CreateRegistration(9.81f);

                Assert.AreEqual(0.3f, first.baseStats.combat.statusDefense.commonReduction, 0.00001f);
                Assert.AreEqual(0.5f, first.baseStats.combat.statusDefense.thresholdMultiplierBonus, 0.00001f);
                Assert.AreEqual(0.4f, first.baseStats.combat.statusDefense.Resolve(StatusEffectId.Poison).accumulationReduction, 0.00001f);
                Assert.AreEqual(270f, first.baseStats.combat.statusDefense.Resolve(StatusEffectId.Poison).threshold, 0.00001f);
                Assert.IsTrue(first.baseStats.combat.statusDefense.Resolve(StatusEffectId.Curse).immune);
                Assert.AreEqual(first.baseStats.combat.statusDefense, second.baseStats.combat.statusDefense);
                Assert.AreEqual(0.2f, info.statusResistance);
                Assert.AreEqual(0.3f, info.statusDefense.commonReduction);
            }
            finally { UnityEngine.Object.DestroyImmediate(info); }
        }

        [Test]
        public void CharacterRegistration_SourceEditedLater_PreservesRegisteredDefenseValue()
        {
            CharacterInfo info = Info();
            try
            {
                info.statusDefense = Profile(0.2f, 0.1f, 160f, true);
                CharacterRegistration registration = info.CreateRegistration(9.81f);
                info.statusDefense = Profile(0.9f, 0.9f, 900f, false);
                info.statusResistance = 0.8f;

                Assert.AreEqual(0.2f, registration.baseStats.combat.statusDefense.commonReduction);
                Assert.AreEqual(160f, registration.baseStats.combat.statusDefense.Resolve(StatusEffectId.Poison).threshold);
                Assert.IsTrue(registration.baseStats.combat.statusDefense.Resolve(StatusEffectId.Curse).immune);
            }
            finally { UnityEngine.Object.DestroyImmediate(info); }
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void CharacterRegistration_NonFiniteLegacyResistance_RejectsInvalidAuthoring(float value)
        {
            CharacterInfo info = Info();
            try
            {
                info.statusResistance = value;
                Assert.Catch<ArgumentException>(() => info.CreateRegistration(9.81f));
            }
            finally { UnityEngine.Object.DestroyImmediate(info); }
        }

        [Test]
        public void DerivedDefense_AllEquipmentSlots_AddsToBasisAndKeepsGuardCutSeparate()
        {
            CharacterBaseStats basis = new CharacterBaseStats
            {
                combat = new CombatStats { statusDefense = Profile(0.1f, 0.1f, 120f, false, 0.1f) },
            };
            EquipmentStatModifiers weapon = new EquipmentStatModifiers { statusDefense = Profile(0.2f, 0.05f, 20f, false, 0.2f) };
            EquipmentStatModifiers shield = new EquipmentStatModifiers
            {
                statusDefense = Profile(0.15f, 0.1f, 30f, false, 0.15f), overridesGuard = true,
                guardStats = new GuardStats { statusCut = 0.4f },
            };
            EquipmentStatModifiers core = new EquipmentStatModifiers { statusDefense = Profile(0.05f, 0.1f, 40f, true, 0.05f) };

            EquipmentDerivedStats result = CharacterStatCalculator.CalculateDerived(basis, default,
                default, weapon, shield, core);

            Assert.AreEqual(0.5f, result.combat.statusDefense.commonReduction, 0.00001f);
            Assert.AreEqual(0.85f, result.combat.statusDefense.Resolve(StatusEffectId.Poison).accumulationReduction, 0.00001f);
            Assert.AreEqual(0.5f, result.combat.statusDefense.thresholdMultiplierBonus, 0.00001f);
            Assert.AreEqual(315f, result.combat.statusDefense.Resolve(StatusEffectId.Poison).threshold, 0.00001f);
            Assert.IsTrue(result.combat.statusDefense.Resolve(StatusEffectId.Curse).immune);
            Assert.AreEqual(0.4f, result.combat.guardStats.statusCut);
            Assert.AreEqual(0.1f, basis.combat.statusDefense.commonReduction);
            Assert.AreEqual(132f, basis.combat.statusDefense.Resolve(StatusEffectId.Poison).threshold, 0.00001f);
        }

        [Test]
        public void DerivedDefense_ZeroModifiers_PreservesRawDefaultThresholdAndValueEquality()
        {
            CharacterBaseStats basis = new CharacterBaseStats
            {
                combat = new CombatStats { statusDefense = Profile(0.2f) },
            };

            EquipmentDerivedStats result = CharacterStatCalculator.CalculateDerived(basis, default,
                default, default, default, default);

            Assert.AreEqual(basis.combat, result.combat);
            Assert.AreEqual(0f, result.combat.statusDefense.poison.threshold);
            Assert.AreEqual(100f, result.combat.statusDefense.Resolve(StatusEffectId.Poison).threshold);
        }

        [Test]
        public void CharacterStats_EquipRecalculateAndRemove_CommitsDefenseBeforeNotificationWithoutHistory()
        {
            CharacterBaseStats basis = new CharacterBaseStats
            {
                maxHp = 100, maxMp = 30, maxStamina = 50f,
                combat = new CombatStats { statusDefense = Profile(0.1f, 0.1f, 120f) },
            };
            World world = new World
            {
                Actor = new EquipmentActorState
                {
                    basis = basis, growth = new CharacterGrowth { level = 1 },
                    vitals = new CharacterVitals { currentHp = 75, maxHp = 100, currentMp = 12.5f, maxMp = 30 },
                },
            };
            using GameEvents events = new GameEvents();
            using EquipmentStore equipment = new EquipmentStore(events, world);
            equipment.Register(1);
            int notifications = 0;
            float expectedCommon = 0.1f;
            using IDisposable subscription = events.OnCharacterStatsChanged.Subscribe(hash =>
            {
                Assert.AreEqual(1, hash);
                Assert.AreEqual(expectedCommon, world.Actor.combat.statusDefense.commonReduction, 0.00001f);
                notifications++;
            });
            expectedCommon = 0.3f;
            equipment.Equip(1, new Item(EquipSlot.Weapon, Profile(0.2f, 0.05f, 20f)));
            expectedCommon = 0.45f;
            equipment.Equip(1, new Item(EquipSlot.Shield, Profile(0.15f, 0.1f, 30f)));
            expectedCommon = 0.5f;
            equipment.Equip(1, new Item(EquipSlot.Core, Profile(0.05f, 0.1f, 40f, true)));
            for (int i = 0; i < 3; i++)
            {
                Assert.IsTrue(equipment.Stats.Recalculate(1));
                Assert.AreEqual(210f, world.Actor.combat.statusDefense.Resolve(StatusEffectId.Poison).threshold);
            }
            expectedCommon = 0.35f;
            equipment.Unequip(1, EquipSlot.Shield);
            Assert.AreEqual(180f, world.Actor.combat.statusDefense.Resolve(StatusEffectId.Poison).threshold);
            Assert.IsTrue(world.Actor.combat.statusDefense.Resolve(StatusEffectId.Curse).immune);
            expectedCommon = 0.3f;
            equipment.Unequip(1, EquipSlot.Core);
            Assert.IsFalse(world.Actor.combat.statusDefense.Resolve(StatusEffectId.Curse).immune);
            expectedCommon = 0.1f;
            equipment.Unequip(1, EquipSlot.Weapon);

            Assert.AreEqual(9, notifications);
            Assert.AreEqual(basis.combat.statusDefense, world.Actor.combat.statusDefense);
            Assert.AreEqual(75, world.Actor.vitals.currentHp);
            Assert.AreEqual(12.5f, world.Actor.vitals.currentMp);
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void Equipment_InvalidDefense_IsRejectedBeforeCallbacksHolderAndWorldMutation(float invalidValue)
        {
            World world = new World
            {
                Actor = new EquipmentActorState
                {
                    basis = new CharacterBaseStats
                    {
                        maxHp = 100,
                        combat = new CombatStats { statusDefense = Profile(0.1f) },
                    },
                    vitals = new CharacterVitals { currentHp = 75, maxHp = 100 },
                },
            };
            using GameEvents events = new GameEvents();
            using EquipmentStore equipment = new EquipmentStore(events, world);
            equipment.Register(1);
            Item previous = new Item(EquipSlot.Weapon, Profile(0.2f));
            equipment.Equip(1, previous);
            CombatStats beforeCombat = world.Actor.combat;
            CharacterVitals beforeVitals = world.Actor.vitals;
            Item invalid = new Item(EquipSlot.Weapon, Profile(invalidValue));
            int notifications = 0;
            using IDisposable subscription = events.OnCharacterStatsChanged.Subscribe(hash => notifications++);

            Assert.Catch<ArgumentException>(() => equipment.Equip(1, invalid));

            Assert.AreSame(previous, equipment.GetHolder(1).Weapon);
            Assert.AreEqual(1, previous.EquipCalls);
            Assert.AreEqual(0, previous.UnequipCalls);
            Assert.AreEqual(0, invalid.EquipCalls);
            Assert.AreEqual(beforeCombat, world.Actor.combat);
            Assert.AreEqual(beforeVitals, world.Actor.vitals);
            Assert.AreEqual(0, notifications);
        }
    }
}
