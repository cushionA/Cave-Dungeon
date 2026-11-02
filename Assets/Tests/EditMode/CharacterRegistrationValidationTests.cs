using System;
using Game.Core;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Tests.EditMode
{
    public class CharacterRegistrationValidationTests
    {
        private const int k_Owner = 71;

        private sealed class ManagedActor : ManagedCharacter
        {
            public override int ObjectHash => k_Owner;
            public override IDamageable Damageable => null;
        }

        private sealed class Equipment : IEquippable, IEquipmentStatsProvider
        {
            public EquipSlot Slot => EquipSlot.Core;
            public int Weight => 3;
            public AbilityFlag GrantedFlags => AbilityFlag.DoubleJump;
            public StatusEffectApplicationData[] OnHitEffects => Array.Empty<StatusEffectApplicationData>();
            public EquipmentStatModifiers StatModifiers => new EquipmentStatModifiers
            {
                attackBonus = new ElementalStatus { slash = 5 },
            };
            public void OnEquip(int hash) { }
            public void OnUnequip(int hash) { }
        }

        [Test]
        public void CharacterRegistration_DuplicateHash_RejectsBeforeChangingAnyLiveActorData()
        {
            GameObject actorObject = new GameObject("RegistrationValidationActor");
            ManagedActor managed = actorObject.AddComponent<ManagedActor>();
            using GameManagerCore core = new GameManagerCore();
            core.Initialize(4);
            try
            {
                core.RegisterCharacter(k_Owner,
                    new CharacterVitals { currentHp = 70, maxHp = 100, currentMp = 12, maxMp = 20 },
                    new CombatStats { attack = new ElementalStatus { slash = 10 } },
                    CharacterFlags.Pack(CharacterBelong.Ally, CharacterFeature.Player, ActState.Neutral, AbilityFlag.None),
                    new MoveParams { moveSpeed = 4f }, managed);
                Equipment item = new Equipment();
                core.Equipment.Equip(k_Owner, item);
                Assert.IsTrue(core.Actors.TryGet(k_Owner, out ActorRef originalActor));
                int definition = core.Actions.RegisterDefinition(new ActionDefinitionData
                {
                    resources = ActionResources.Hands, activeDuration = 1f,
                    endMode = ActionEndMode.Timed, actState = ActState.Attacking,
                    gameplay = ActionGameplayFlags.Attack, cancelPoint = -1f,
                });
                Assert.IsTrue(core.Actions.TryStart(new ActionRequest
                {
                    ownerHash = k_Owner, ownerRef = originalActor,
                    definitionId = definition, direction = Vector2.right, speedMultiplier = 1f,
                }, out long execution));
                CharacterVitals vitals = core.Data.GetVitals(k_Owner);
                CombatStats combat = core.Data.GetCombatStats(k_Owner);
                CharacterFlags flags = core.Data.GetFlags(k_Owner);
                CharacterBaseStats basis = core.Data.GetBaseStats(k_Owner);
                MoveParams movement = core.Data.GetMoveParams(k_Owner);

                Assert.Throws<ArgumentException>(() => core.RegisterCharacter(k_Owner,
                    new CharacterVitals { currentHp = 900, maxHp = 900 },
                    new CombatStats { attack = new ElementalStatus { slash = 999 } },
                    CharacterFlags.Pack(CharacterBelong.Enemy, CharacterFeature.Boss, ActState.Dead, AbilityFlag.None),
                    new MoveParams { moveSpeed = 99f }));

                Assert.AreEqual(1, core.Data.Count);
                Assert.AreEqual(vitals, core.Data.GetVitals(k_Owner));
                Assert.AreEqual(combat, core.Data.GetCombatStats(k_Owner));
                Assert.AreEqual(flags, core.Data.GetFlags(k_Owner));
                Assert.AreEqual(basis, core.Data.GetBaseStats(k_Owner));
                Assert.AreEqual(movement, core.Data.GetMoveParams(k_Owner));
                Assert.AreSame(managed, core.Data.GetManaged(k_Owner));
                Assert.AreSame(item, core.Equipment.GetHolder(k_Owner).Core);
                Assert.IsTrue(core.Actors.IsCurrent(originalActor));
                Assert.IsTrue(core.Actions.TryGetInstance(execution, out ActionExecution active));
                Assert.AreEqual(originalActor, active.ownerRef);
            }
            finally { Object.DestroyImmediate(actorObject); }
        }

        [Test]
        public void CharacterRegistration_InvalidDefense_RejectsBeforePublishingAnyActorOrDataRow()
        {
            using GameManagerCore core = new GameManagerCore();
            core.Initialize(2);
            CharacterRegistration invalid = new CharacterRegistration
            {
                baseStats = new CharacterBaseStats
                {
                    maxHp = 100,
                    combat = new CombatStats
                    {
                        statusDefense = new StatusEffectDefenseProfile { commonReduction = float.NaN },
                    },
                },
                growth = new CharacterGrowth { level = 1 },
            };

            Assert.Throws<ArgumentException>(() => core.RegisterCharacter(k_Owner, invalid));

            Assert.AreEqual(0, core.Data.Count);
            Assert.IsFalse(core.Data.TryGetValue(k_Owner, out _));
            Assert.IsFalse(core.Actors.TryGet(k_Owner, out _));
            Assert.AreEqual(0, core.Actions.ActiveCount);
            Assert.AreEqual(0, core.StatusEffects.Store.Count);
        }
    }
}
