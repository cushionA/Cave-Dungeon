using NUnit.Framework;
using UnityEngine;
using Game.Core;
using System.Collections.Generic;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// CombatDataHelper の AttackInfo → AttackMotionData / DamageData 変換を検証。
    /// </summary>
    public class CombatDataHelperTests
    {
        [Test]
        public void CombatDataHelper_DistinctGuardAndArmorDamage_PreservesBothAcrossMotionAndPacket()
        {
            AttackInfo info = CreateAttackInfo();
            info.guardDamage = 7f;
            info.armorBreakValue = 31f;

            AttackMotionData motion = CombatDataHelper.BuildMotionData(info);
            DamageData packet = CombatDataHelper.BuildDamageData(1, 2, motion, default);

            Assert.AreEqual(7f, motion.guardDamage);
            Assert.AreEqual(31f, motion.armorBreakValue);
            Assert.AreEqual(7f, packet.guardDamage);
            Assert.AreEqual(31f, packet.armorBreakValue);
        }

        private readonly List<ScriptableObject> _created = new List<ScriptableObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; i++)
            {
                Object.DestroyImmediate(_created[i]);
            }
            _created.Clear();
        }

        private AttackInfo CreateAttackInfo(float justGuardResistance = 0f)
        {
            AttackInfo info = ScriptableObject.CreateInstance<AttackInfo>();
            _created.Add(info);
            info.attackName = "TestAttack";
            info.damageMultiplier = 1.5f;
            info.attackElement = Element.Fire | Element.Slash;
            info.feature = AttackFeature.Heavy;
            info.guardDamage = 20f;
            info.armorBreakValue = 20f;
            info.staminaCost = 10f;
            info.mpCost = 5;
            info.movement = new ActionMovementProfile { speed = 2f / 0.3f, duration = 0.3f };
            info.contactType = AttackContactType.StopOnHit;
            info.isAutoChain = true;
            info.isChainEndPoint = false;
            info.inputWindow = 0.4f;
            info.justGuardResistance = justGuardResistance;
            info.knockbackInfo = new KnockbackInfo
            {
                hasKnockback = true,
                force = new Vector2(5f, 0f)
            };
            StatusEffectDefinition burn = ScriptableObject.CreateInstance<StatusEffectDefinition>();
            _created.Add(burn);
            burn.effect = StatusEffectId.Burn;
            burn.duration = 5f;
            burn.tickDamage = 2f;
            burn.tickInterval = 1f;
            info.statusEffects = new[]
            {
                new StatusEffectApplication
                {
                    definition = burn, accumulateValue = 30f,
                    mode = StatusEffectApplicationMode.Accumulate,
                    target = StatusEffectTarget.HitTarget,
                    guardRule = StatusEffectGuardRule.Block
                }
            };
            return info;
        }

        [Test]
        public void CombatDataHelper_BuildMotionData_CopiesJustGuardResistance()
        {
            AttackInfo info = CreateAttackInfo(justGuardResistance: 75f);
            AttackMotionData motion = CombatDataHelper.BuildMotionData(info);
            Assert.AreEqual(75f, motion.justGuardResistance, 0.001f);
        }

        [Test]
        public void CombatDataHelper_BuildMotionData_CopiesZeroJustGuardResistance()
        {
            AttackInfo info = CreateAttackInfo(justGuardResistance: 0f);
            AttackMotionData motion = CombatDataHelper.BuildMotionData(info);
            Assert.AreEqual(0f, motion.justGuardResistance, 0.001f);
        }

        [Test]
        public void CombatDataHelper_BuildMotionData_CopiesAllScalarFields()
        {
            AttackInfo info = CreateAttackInfo(justGuardResistance: 50f);
            AttackMotionData motion = CombatDataHelper.BuildMotionData(info, maxHitCount: 3);

            Assert.AreEqual("TestAttack", motion.actionName);
            Assert.AreEqual(1.5f, motion.motionValue, 0.001f);
            Assert.AreEqual(Element.Fire | Element.Slash, motion.attackElement);
            Assert.AreEqual(AttackFeature.Heavy, motion.feature);
            Assert.AreEqual(20f, motion.armorBreakValue, 0.001f);
            Assert.AreEqual(3, motion.maxHitCount);
            Assert.AreEqual(10f, motion.staminaCost, 0.001f);
            Assert.AreEqual(5, motion.mpCost);
            Assert.AreEqual(2f, motion.attackMoveDistance, 0.001f);
            Assert.AreEqual(0.3f, motion.attackMoveDuration, 0.001f);
            Assert.AreEqual(AttackContactType.StopOnHit, motion.contactType);
            Assert.IsTrue(motion.isAutoChain);
            Assert.IsFalse(motion.isChainEndPoint);
            Assert.AreEqual(0.4f, motion.inputWindow, 0.001f);
            Assert.AreEqual(50f, motion.justGuardResistance, 0.001f);
            Assert.AreEqual(new Vector2(5f, 0f), motion.knockbackForce);
            Assert.AreEqual(1, motion.statusEffects.Length);
            Assert.AreEqual(StatusEffectId.Burn, motion.statusEffects[0].info.effect);
            Assert.AreEqual(30f, motion.statusEffects[0].info.accumulateValue);
            Assert.AreEqual(StatusEffectApplicationMode.Accumulate, motion.statusEffects[0].mode);
            Assert.AreEqual(StatusEffectTarget.HitTarget, motion.statusEffects[0].target);
            Assert.AreEqual(StatusEffectGuardRule.Block, motion.statusEffects[0].guardRule);
        }

        [Test]
        public void CombatDataHelper_BuildMotionData_SnapshotsDefinitionAndPropagatesAllApplications()
        {
            AttackInfo info = CreateAttackInfo();
            StatusEffectDefinition speed = ScriptableObject.CreateInstance<StatusEffectDefinition>();
            _created.Add(speed);
            speed.effect = StatusEffectId.SpeedUp;
            speed.duration = 8f;
            speed.modifier = 0.5f;
            info.statusEffects = new[]
            {
                info.statusEffects[0],
                new StatusEffectApplication
                {
                    definition = speed,
                    mode = StatusEffectApplicationMode.Immediate,
                    target = StatusEffectTarget.Source,
                    guardRule = StatusEffectGuardRule.Ignore
                }
            };

            AttackMotionData motion = CombatDataHelper.BuildMotionData(info);
            speed.duration = 20f;
            DamageData damage = CombatDataHelper.BuildDamageData(1, 2, motion, default);

            Assert.AreEqual(2, motion.statusEffects.Length);
            Assert.AreEqual(8f, motion.statusEffects[1].info.duration);
            Assert.AreEqual(StatusEffectApplicationMode.Immediate, motion.statusEffects[1].mode);
            Assert.AreEqual(StatusEffectTarget.Source, motion.statusEffects[1].target);
            Assert.AreEqual(StatusEffectGuardRule.Ignore, motion.statusEffects[1].guardRule);
            Assert.AreSame(motion.statusEffects, damage.statusEffects);
        }

        [Test]
        public void CombatDataHelper_BuildMotionData_KnockbackDisabled_ReturnsZeroForce()
        {
            AttackInfo info = CreateAttackInfo();
            info.knockbackInfo = new KnockbackInfo
            {
                hasKnockback = false,
                force = new Vector2(10f, 0f)
            };
            AttackMotionData motion = CombatDataHelper.BuildMotionData(info);
            Assert.AreEqual(Vector2.zero, motion.knockbackForce,
                "hasKnockback=false の場合は knockbackForce=Vector2.zero");
        }

        [Test]
        public void CombatDataHelper_BuildDamageData_UsesMotionJustGuardResistance()
        {
            AttackMotionData motion = new AttackMotionData
            {
                justGuardResistance = 60f,
                guardDamage = 15f,
                armorBreakValue = 15f,
                attackElement = Element.Fire,
                motionValue = 1.2f,
                knockbackForce = new Vector2(3f, 0f)
            };
            ElementalStatus attackStats = new ElementalStatus { fire = 30 };

            DamageData damage = CombatDataHelper.BuildDamageData(
                attackerHash: 1, defenderHash: 2, motion: motion, attackStats: attackStats);

            Assert.AreEqual(60f, damage.justGuardResistance, 0.001f);
            Assert.AreEqual(15f, damage.armorBreakValue, 0.001f);
            Assert.AreEqual(Element.Fire, damage.attackElement);
            Assert.AreEqual(1.2f, damage.motionValue, 0.001f);
            Assert.IsFalse(damage.isProjectile);
        }

        [Test]
        public void CombatDataHelper_BuildDamageData_SetsIsProjectileFlag()
        {
            AttackMotionData motion = new AttackMotionData { justGuardResistance = 10f };
            DamageData damage = CombatDataHelper.BuildDamageData(
                attackerHash: 1, defenderHash: 2, motion: motion,
                attackStats: default, isProjectile: true);

            Assert.IsTrue(damage.isProjectile);
        }

        [Test]
        public void CombatDataHelper_GetAttackStats_ReturnsCombatStatsAttack()
        {
            SoACharaDataDic data = new SoACharaDataDic();
            ElementalStatus expected = new ElementalStatus { fire = 42, slash = 17 };
            data.Add(100,
                new CharacterVitals(),
                new CombatStats { attack = expected },
                default, default);

            ElementalStatus actual = CombatDataHelper.GetAttackStats(data, 100);
            Assert.AreEqual(42, actual.fire);
            Assert.AreEqual(17, actual.slash);

            data.Dispose();
        }

        [Test]
        public void CombatDataHelper_GetAttackStats_InvalidHash_ReturnsDefault()
        {
            SoACharaDataDic data = new SoACharaDataDic();
            ElementalStatus actual = CombatDataHelper.GetAttackStats(data, 9999);
            Assert.AreEqual(default(ElementalStatus), actual);
            data.Dispose();
        }
    }
}
