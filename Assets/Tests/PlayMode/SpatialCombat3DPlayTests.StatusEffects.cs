using System.Collections;
using System.Reflection;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.PlayMode
{
    public sealed partial class SpatialCombat3DPlayTests
    {
        [UnityTest]
        public IEnumerator Melee3D_AuthoredTechniqueAndEquippedWeapon_BuildUpSeparateEffectsOnContact()
        {
            CharacterActor3D knight = CreateActor(_room, "Knight", new Vector3(0f, 0.1f, 0f), CharacterBelong.Ally);
            CharacterActor3D enemy = CreateActor(_room, "Enemy", new Vector3(0f, 0.1f, 1.8f));
            yield return null;
            SynchronizeWorld(_room);

            StatusEffectApplication techniqueBurn = BuildUp(StatusEffectId.Burn, 60f);
            StatusEffectApplication weaponPoison = BuildUp(StatusEffectId.Poison, 65f);
            GameManager.Equipment.Equip(knight.Actor.Hash, CreateEquipment(EquipSlot.Weapon, onHit: new[] { weaponPoison }));
            AttackInfo independent = CreateAttack(AttackShape3D.Melee);
            independent.equipmentSource = AttackEquipmentSource.None;
            independent.statusEffects = new[] { techniqueBurn };
            AttackInfo weapon = CreateAttack(AttackShape3D.Melee);
            weapon.equipmentSource = AttackEquipmentSource.Weapon;
            weapon.statusEffects = new[] { techniqueBurn };

            StatusEffectStore effects = GameManager.StatusEffects.Store;
            int target = enemy.Actor.Hash;
            StrikeWithStatus(knight, enemy, independent);
            Assert.AreEqual(60f, effects.GetAccumulation(target, StatusEffectId.Burn));
            Assert.AreEqual(0f, effects.GetAccumulation(target, StatusEffectId.Poison));
            Assert.IsFalse(effects.IsActive(target, StatusEffectId.Burn));

            StrikeWithStatus(knight, enemy, weapon);
            Assert.IsTrue(effects.TryGetEffect(target, StatusEffectId.Burn, out ActiveEffect burn));
            Assert.AreEqual(knight.Actor, burn.sourceRef);
            Assert.AreEqual(enemy.Actor, burn.targetRef);
            Assert.AreEqual(0f, effects.GetAccumulation(target, StatusEffectId.Burn));
            Assert.AreEqual(65f, effects.GetAccumulation(target, StatusEffectId.Poison));
            Assert.IsFalse(effects.IsActive(target, StatusEffectId.Poison));

            StrikeWithStatus(knight, enemy, weapon);
            Assert.IsTrue(effects.TryGetEffect(target, StatusEffectId.Poison, out ActiveEffect poison));
            Assert.AreEqual(knight.Actor, poison.sourceRef);
            Assert.AreEqual(enemy.Actor, poison.targetRef);
            Assert.AreEqual(0f, effects.GetAccumulation(target, StatusEffectId.Poison));
        }

        [UnityTest]
        public IEnumerator Melee3D_RemovingTemporaryAttackUp_PreservesTheEquippedPassiveUntilUnequipped()
        {
            CharacterActor3D knight = CreateActor(_room, "Knight", new Vector3(0f, 0.1f, 0f), CharacterBelong.Ally);
            CharacterActor3D enemy = CreateActor(_room, "Enemy", new Vector3(0f, 0.1f, 1.8f));
            yield return null;
            SynchronizeWorld(_room);

            AttackInfo attack = CreateAttack(AttackShape3D.Melee);
            int knightHash = knight.Actor.Hash;
            int baseAttack = GameManager.Data.GetCombatStats(knightHash).attack.slash;
            int baseDamage = StrikeWithStatus(knight, enemy, attack);
            EquipmentDefinition passive = CreateEquipment(EquipSlot.Core, new EquipmentStatModifiers
            { attackBonus = new ElementalStatus { slash = 10 } });
            GameManager.Equipment.Equip(knightHash, passive);
            Assert.AreEqual(baseAttack + 10, GameManager.Data.GetCombatStats(knightHash).attack.slash);
            int equippedDamage = StrikeWithStatus(knight, enemy, attack);
            Assert.That(equippedDamage, Is.GreaterThan(baseDamage));

            Assert.IsTrue(GameManager.StatusEffects.Apply(knightHash,
                new StatusEffectInfo { effect = StatusEffectId.AttackUp, modifier = 0.5f, duration = 5f }, knightHash));
            Assert.That(StrikeWithStatus(knight, enemy, attack), Is.GreaterThan(equippedDamage));

            Assert.IsTrue(GameManager.StatusEffects.Remove(knightHash, StatusEffectId.AttackUp));
            Assert.IsFalse(GameManager.StatusEffects.Store.IsActive(knightHash, StatusEffectId.AttackUp));
            Assert.AreSame(passive, GameManager.Equipment.GetHolder(knightHash).Core);
            Assert.AreEqual(baseAttack + 10, GameManager.Data.GetCombatStats(knightHash).attack.slash);
            Assert.AreEqual(equippedDamage, StrikeWithStatus(knight, enemy, attack));

            Assert.AreSame(passive, GameManager.Equipment.Unequip(knightHash, EquipSlot.Core));
            Assert.AreEqual(baseAttack, GameManager.Data.GetCombatStats(knightHash).attack.slash);
            Assert.AreEqual(baseDamage, StrikeWithStatus(knight, enemy, attack));
        }

        private int StrikeWithStatus(CharacterActor3D source, CharacterActor3D target, AttackInfo attack)
        {
            int hitIndex = _hits.Count;
            Assert.That(GameManager.Actions.Execute3D(source, attack, out long instance), Is.True);
            Step(0.25f);
            Assert.That(_hits, Has.Count.EqualTo(hitIndex + 1));
            DamageResult hit = _hits[hitIndex].result;
            Assert.AreEqual(instance, hit.actionInstanceId);
            Assert.AreEqual(target.Actor, hit.defenderRef);
            Assert.That(hit.totalDamage, Is.Positive);
            Step(0.4f);
            return hit.totalDamage;
        }

        private StatusEffectApplication BuildUp(StatusEffectId effect, float amount)
        {
            var definition = ScriptableObject.CreateInstance<StatusEffectDefinition>();
            definition.effect = effect;
            _assets.Add(definition);
            return new StatusEffectApplication
            {
                definition = definition,
                mode = StatusEffectApplicationMode.Accumulate,
                accumulateValue = amount,
            };
        }

        private EquipmentDefinition CreateEquipment(EquipSlot slot, EquipmentStatModifiers modifiers = default,
            StatusEffectApplication[] onHit = null)
        {
            var definition = ScriptableObject.CreateInstance<EquipmentDefinition>();
            _assets.Add(definition);
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(EquipmentDefinition).GetField("_slot", fields).SetValue(definition, slot);
            typeof(EquipmentDefinition).GetField("_statModifiers", fields).SetValue(definition, modifiers);
            if (onHit != null) { typeof(EquipmentDefinition).GetField("_onHitStatusEffects", fields).SetValue(definition, onHit); }
            return definition;
        }
    }
}
