using System;
using Game.Core;
using NUnit.Framework;
using R3;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public class Integration_EquipmentRegistrationTests
    {
        private sealed class TestEquipment : IEquippable, IEquipmentStatsProvider
        {
            public EquipSlot Slot { get; set; } = EquipSlot.Core;
            public int Weight => 6;
            public AbilityFlag GrantedFlags => AbilityFlag.WallKick;
            public EquipmentStatModifiers StatModifiers { get; set; }
            public StatusEffectApplicationData[] OnHitEffects => Array.Empty<StatusEffectApplicationData>();
            public void OnEquip(int ownerHash) { }
            public void OnUnequip(int ownerHash) { }
        }

        private const int k_Owner = 11;

        private static CharacterRegistration Registration(AbilityFlag flags)
        {
            return new CharacterRegistration
            {
                baseStats = new CharacterBaseStats
                {
                    maxHp = 100, maxMp = 30, maxStamina = 50f,
                    abilityFlags = flags,
                    combat = new CombatStats { attack = new ElementalStatus { slash = 17 } },
                },
                growth = new CharacterGrowth { level = 1 },
                flags = CharacterFlags.Pack(CharacterBelong.Ally, CharacterFeature.Player, AbilityFlag.None),
            };
        }

        [Test]
        public void EquipmentRegistration_RegistrationNotification_SeesIntrinsicFlagsAndEmptyEquipment()
        {
            using GameManagerCore core = new GameManagerCore();
            core.Initialize(2);
            int notifications = 0;
            using IDisposable subscription = core.Events.OnCharacterRegistered.Subscribe(owner =>
            {
                Assert.AreEqual(AbilityFlag.DoubleJump, core.Data.GetFlags(owner).AbilityFlags);
                Assert.AreEqual(AbilityFlag.None, core.Data.GetEquipmentStatus(owner).activeFlags);
                Assert.AreEqual(0f, core.Data.GetEquipmentStatus(owner).totalWeight);
                Assert.AreEqual(17, core.Data.GetEquipmentStatus(owner).finalAttack.slash);
                Assert.AreEqual(core.Data.GetCombatStats(owner).attack, core.Data.GetEquipmentStatus(owner).finalAttack);
                Assert.AreEqual(100, core.Data.GetVitals(owner).currentHp);
                Assert.IsFalse(core.Equipment.GetHolder(owner).HasCore);
                notifications++;
            });
            core.RegisterCharacter(k_Owner, Registration(AbilityFlag.DoubleJump));
            Assert.AreEqual(1, notifications);
        }

        [Test]
        public void EquipmentRegistration_EquipAndGrantThenUnequip_PublishesSharedFlagsWithoutChangingCombatOrVitals()
        {
            using GameManagerCore core = new GameManagerCore();
            core.Initialize(2);
            core.RegisterCharacter(k_Owner, Registration(AbilityFlag.DoubleJump));
            core.Data.GetVitals(k_Owner).currentHp = 63;
            core.Equipment.Equip(k_Owner, new TestEquipment());
            Assert.AreEqual(AbilityFlag.DoubleJump | AbilityFlag.WallKick, core.Data.GetFlags(k_Owner).AbilityFlags);
            Assert.AreEqual(6f, core.Data.GetEquipmentStatus(k_Owner).totalWeight);
            core.Equipment.GrantAbilityFlags(k_Owner, AbilityFlag.WallKick);
            core.Equipment.Unequip(k_Owner, EquipSlot.Core);
            Assert.AreEqual(AbilityFlag.DoubleJump | AbilityFlag.WallKick, core.Data.GetBaseStats(k_Owner).abilityFlags);
            Assert.AreEqual(AbilityFlag.DoubleJump | AbilityFlag.WallKick, core.Data.GetFlags(k_Owner).AbilityFlags);
            Assert.AreEqual(AbilityFlag.None, core.Data.GetEquipmentStatus(k_Owner).activeFlags);
            Assert.AreEqual(0f, core.Data.GetEquipmentStatus(k_Owner).totalWeight);
            Assert.AreEqual(63, core.Data.GetVitals(k_Owner).currentHp);
            Assert.AreEqual(17, core.Data.GetCombatStats(k_Owner).attack.slash);
        }

        [Test]
        public void EquipmentRegistration_UnregisterAndReuseHash_DoesNotInheritOldEquipmentOrAcquiredFlags()
        {
            using GameManagerCore core = new GameManagerCore();
            core.Initialize(2);
            core.RegisterCharacter(k_Owner, Registration(AbilityFlag.DoubleJump));
            core.Equipment.Equip(k_Owner, new TestEquipment());
            core.Equipment.GrantAbilityFlags(k_Owner, AbilityFlag.WallCling);
            core.UnregisterCharacter(k_Owner);
            core.RegisterCharacter(k_Owner, Registration(AbilityFlag.None));
            Assert.AreEqual(AbilityFlag.None, core.Data.GetFlags(k_Owner).AbilityFlags);
            Assert.AreEqual(AbilityFlag.None, core.Data.GetBaseStats(k_Owner).abilityFlags);
            Assert.AreEqual(0f, core.Data.GetEquipmentStatus(k_Owner).totalWeight);
            Assert.IsFalse(core.Equipment.GetHolder(k_Owner).HasCore);
        }

        [Test]
        public void EquipmentRegistration_EquipAndRemove_UpdatesSharedStatsWithoutResettingLiveState()
        {
            using GameManagerCore core = new GameManagerCore();
            core.Initialize(2);
            CharacterRegistration registration = Registration(AbilityFlag.DoubleJump);
            registration.baseStats.maxArmor = 20f;
            registration.baseStats.attributes = new StatModifier { str = 10, dex = 2, intel = 3, vit = 4, mnd = 5, end = 6 };
            registration.baseStats.combat.defense = new ElementalStatus { fire = 7 };
            registration.baseStats.combat.guardStats = new GuardStats { guardStrength = 15f };
            core.RegisterCharacter(k_Owner, registration);
            core.Data.GetVitals(k_Owner).currentHp = 63;
            core.Data.GetVitals(k_Owner).currentMp = 17.5f;
            core.Data.GetVitals(k_Owner).currentStamina = 21.25f;
            core.Data.GetVitals(k_Owner).currentArmor = 4.5f;
            core.Data.GetVitals(k_Owner).position = new Vector2(2f, 3f);
            core.Data.GetFlags(k_Owner).ActState = ActState.Guarding;
            core.Data.GetDefenseState(k_Owner).isGuarding = true;
            core.Data.GetDefenseState(k_Owner).guardElapsedTime = 0.2f;
            core.Data.GetDefenseState(k_Owner).continuousJustGuardUntil = 9f;
            core.Data.GetDefenseState(k_Owner).armorRecoveryTimer = 0.8f;
            core.StatusEffects.Apply(k_Owner, new StatusEffectInfo
            {
                effect = StatusEffectId.AttackUp, duration = 10f, modifier = 0.5f,
            });
            int notifications = 0;
            using IDisposable subscription = core.Events.OnCharacterStatsChanged.Subscribe(ownerHash =>
            {
                notifications++;
                EquipmentStatus equipment = core.Data.GetEquipmentStatus(ownerHash);
                CombatStats combat = core.Data.GetCombatStats(ownerHash);
                Assert.AreEqual(combat.attack, equipment.finalAttack);
                Assert.AreEqual(combat.defense, equipment.finalDefense);
                Assert.AreEqual(combat.guardStats, equipment.finalGuardStats);
                Assert.AreEqual(core.Data.GetVitals(ownerHash).maxArmor, core.Data.GetDefenseState(ownerHash).maxArmor);
                Assert.AreEqual(1.5f, core.Data.GetStatusEffects(ownerHash).AttackMultiplier);
                Assert.AreEqual(ActState.Guarding, core.Data.GetFlags(ownerHash).ActState);
            });

            core.Equipment.Equip(k_Owner, new TestEquipment
            {
                Slot = EquipSlot.Shield,
                StatModifiers = new EquipmentStatModifiers
                {
                    attributes = new StatModifier { str = 40, dex = 1, intel = 2, vit = 1, mnd = 1, end = 3 },
                    attackBonus = new ElementalStatus { slash = 20 }, strengthScaling = 1f,
                    defenseBonus = new ElementalStatus { fire = 3 },
                    bonusHp = 5, bonusMp = 2, bonusStamina = 3f, bonusArmor = 4f,
                    overridesGuard = true, guardStats = new GuardStats { slashCut = 0.75f, guardStrength = 30f },
                },
            });

            Assert.AreEqual(47, CombatDataHelper.GetAttackStats(core.Data, k_Owner).slash);
            Assert.AreEqual(10, core.Data.GetCombatStats(k_Owner).defense.fire);
            Assert.AreEqual(30f, core.Data.GetCombatStats(k_Owner).guardStats.guardStrength);
            Assert.AreEqual(0.75f, core.Data.GetCombatStats(k_Owner).guardStats.slashCut);
            Assert.AreEqual(115, core.Data.GetVitals(k_Owner).maxHp);
            Assert.AreEqual(37, core.Data.GetVitals(k_Owner).maxMp);
            Assert.AreEqual(53f, core.Data.GetVitals(k_Owner).maxStamina);
            Assert.AreEqual(24f, core.Data.GetVitals(k_Owner).maxArmor);
            Assert.AreEqual(new StatModifier { str = 50, dex = 3, intel = 5, vit = 5, mnd = 6, end = 9 }, core.Data.GetAttributes(k_Owner));

            core.Equipment.Unequip(k_Owner, EquipSlot.Shield);

            Assert.AreEqual(2, notifications);
            Assert.AreEqual(17, core.Data.GetCombatStats(k_Owner).attack.slash);
            Assert.AreEqual(7, core.Data.GetCombatStats(k_Owner).defense.fire);
            Assert.AreEqual(15f, core.Data.GetCombatStats(k_Owner).guardStats.guardStrength);
            Assert.AreEqual(registration.baseStats.attributes, core.Data.GetAttributes(k_Owner));
            Assert.AreEqual(63, core.Data.GetVitals(k_Owner).currentHp);
            Assert.AreEqual(17.5f, core.Data.GetVitals(k_Owner).currentMp);
            Assert.AreEqual(21.25f, core.Data.GetVitals(k_Owner).currentStamina);
            Assert.AreEqual(4.5f, core.Data.GetVitals(k_Owner).currentArmor);
            Assert.AreEqual(new Vector2(2f, 3f), core.Data.GetVitals(k_Owner).position);
            Assert.IsTrue(core.Data.GetDefenseState(k_Owner).isGuarding);
            Assert.AreEqual(0.2f, core.Data.GetDefenseState(k_Owner).guardElapsedTime);
            Assert.AreEqual(9f, core.Data.GetDefenseState(k_Owner).continuousJustGuardUntil);
            Assert.AreEqual(0.8f, core.Data.GetDefenseState(k_Owner).armorRecoveryTimer);
            Assert.AreEqual(20f, core.Data.GetDefenseState(k_Owner).maxArmor);
            Assert.AreEqual(17, core.Data.GetBaseStats(k_Owner).combat.attack.slash);
            Assert.AreEqual(100, core.Data.GetBaseStats(k_Owner).maxHp);
        }

        [Test]
        public void EquipmentRegistration_GrowthWhileEquipped_PreservesResourcesWithinFinalMaxima()
        {
            using GameManagerCore core = new GameManagerCore();
            core.Initialize(2);
            core.RegisterCharacter(k_Owner, Registration(AbilityFlag.None));
            core.Equipment.Equip(k_Owner, new TestEquipment
            {
                StatModifiers = new EquipmentStatModifiers { bonusHp = 100, bonusMp = 100 },
            });
            core.Data.GetVitals(k_Owner).currentHp = 150;
            core.Data.GetVitals(k_Owner).currentMp = 70.5f;
            int notifications = 0;
            using IDisposable subscription = core.Events.OnCharacterStatsChanged.Subscribe(ownerHash =>
            {
                notifications++;
                Assert.AreEqual(210, core.Data.GetVitals(ownerHash).maxHp);
                Assert.AreEqual(135, core.Data.GetVitals(ownerHash).maxMp);
                Assert.AreEqual(150, core.Data.GetVitals(ownerHash).currentHp);
                Assert.AreEqual(70.5f, core.Data.GetVitals(ownerHash).currentMp);
                Assert.AreEqual(1, core.Data.GetAttributes(ownerHash).vit);
            });
            CharacterGrowth growth = new CharacterGrowth
            {
                level = 2, allocatedStats = new StatModifier { vit = 1, mnd = 1 },
            };

            core.ApplyCharacterGrowth(k_Owner, growth);
            core.ApplyCharacterGrowth(k_Owner, growth);

            Assert.AreEqual(2, notifications);
            Assert.AreEqual(100, core.Data.GetBaseStats(k_Owner).maxHp);
            Assert.AreEqual(30, core.Data.GetBaseStats(k_Owner).maxMp);
            Assert.AreEqual(71, core.Data.GetVitals(k_Owner).hpRatio);
            Assert.AreEqual(52, core.Data.GetVitals(k_Owner).mpRatio);
        }

        [Test]
        public void EquipmentRegistration_ExcessiveNegativeHpBonus_PreservesLifeWithoutHealingOnRemoval()
        {
            using GameManagerCore core = new GameManagerCore();
            core.Initialize(2);
            core.RegisterCharacter(k_Owner, Registration(AbilityFlag.None));
            core.Data.GetVitals(k_Owner).currentHp = 63;
            core.Data.GetFlags(k_Owner).ActState = ActState.Guarding;
            int deaths = 0;
            using IDisposable subscription = core.Events.OnCharacterDeath.Subscribe(_ => deaths++);

            core.Equipment.Equip(k_Owner, new TestEquipment
            {
                StatModifiers = new EquipmentStatModifiers { bonusHp = -1000 },
            });

            Assert.AreEqual(1, core.Data.GetVitals(k_Owner).maxHp);
            Assert.AreEqual(1, core.Data.GetVitals(k_Owner).currentHp);
            Assert.AreEqual(100, core.Data.GetVitals(k_Owner).hpRatio);
            Assert.AreEqual(ActState.Guarding, core.Data.GetFlags(k_Owner).ActState);
            Assert.AreEqual(0, deaths);

            core.Equipment.Unequip(k_Owner, EquipSlot.Core);

            Assert.AreEqual(100, core.Data.GetVitals(k_Owner).maxHp);
            Assert.AreEqual(1, core.Data.GetVitals(k_Owner).currentHp);
            Assert.AreEqual(1, core.Data.GetVitals(k_Owner).hpRatio);
            Assert.AreEqual(ActState.Guarding, core.Data.GetFlags(k_Owner).ActState);
            Assert.AreEqual(0, deaths);
        }
    }
}
