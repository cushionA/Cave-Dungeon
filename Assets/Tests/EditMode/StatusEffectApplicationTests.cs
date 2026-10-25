using System;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    public class StatusEffectApplicationTests
    {
        private const int k_SourceHash = 10;
        private const int k_TargetHash = 20;

        [Test]
        public void StatusEffectApplication_AttackAndEquipmentEffects_ApplyBothWithoutModifyingSnapshots()
        {
            StatusEffectStore store = new StatusEffectStore();
            StatusEffectApplicationData[] attack = { Application(StatusEffectId.Burn) };
            StatusEffectApplicationData[] equipment = { Application(StatusEffectId.Poison) };

            StatusEffectId first = StatusEffectApplicationLogic.Apply(attack, k_SourceHash,
                k_TargetHash, GuardResult.NoGuard, 0f, store, equipment);

            Assert.AreEqual(StatusEffectId.Burn, first);
            Assert.IsTrue(store.IsActive(k_TargetHash, StatusEffectId.Burn));
            Assert.IsTrue(store.IsActive(k_TargetHash, StatusEffectId.Poison));
            Assert.AreEqual(1, attack.Length);
            Assert.AreEqual(1, equipment.Length);
        }

        private static StatusEffectApplicationData Application(StatusEffectId id,
            StatusEffectApplicationMode mode = StatusEffectApplicationMode.Immediate,
            StatusEffectTarget target = StatusEffectTarget.HitTarget,
            StatusEffectGuardRule guardRule = StatusEffectGuardRule.Block,
            float accumulation = 100f)
        {
            return new StatusEffectApplicationData
            {
                info = new StatusEffectInfo
                {
                    effect = id, duration = 10f, modifier = 0.5f,
                    accumulateValue = accumulation, tickInterval = 1f
                },
                mode = mode,
                target = target,
                guardRule = guardRule
            };
        }

        [Test]
        public void StatusEffectApplication_MultipleEffects_AppliesAllAndReturnsFirstSuccessfulId()
        {
            StatusEffectStore store = new StatusEffectStore();
            StatusEffectApplicationData[] applications =
            {
                Application(StatusEffectId.AttackUp), Application(StatusEffectId.Slow)
            };

            StatusEffectId first = StatusEffectApplicationLogic.Apply(applications,
                k_SourceHash, k_TargetHash, GuardResult.NoGuard, 0f, store);

            Assert.AreEqual(StatusEffectId.AttackUp, first);
            Assert.AreEqual(2, store.GetActiveCount(k_TargetHash));
            Assert.IsTrue(store.TryGetEffect(k_TargetHash, StatusEffectId.Slow, out ActiveEffect slow));
            Assert.AreEqual(k_SourceHash, slow.sourceHash);
            Assert.AreEqual(0.5f, store.GetSummary(k_TargetHash).MoveSpeedMultiplier);
        }

        [Test]
        public void StatusEffectApplication_Accumulation_RespectsResistanceAcrossHits()
        {
            StatusEffectStore store = new StatusEffectStore();
            StatusEffectApplicationData[] applications =
            {
                Application(StatusEffectId.Poison, StatusEffectApplicationMode.Accumulate, accumulation: 60f)
            };

            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(StatusEffectId.None, StatusEffectApplicationLogic.Apply(applications,
                    k_SourceHash, k_TargetHash, GuardResult.NoGuard, 0.5f, store));
            }
            Assert.AreEqual(90f, store.GetAccumulation(k_TargetHash, StatusEffectId.Poison));
            Assert.AreEqual(StatusEffectId.Poison, StatusEffectApplicationLogic.Apply(applications,
                k_SourceHash, k_TargetHash, GuardResult.NoGuard, 0.5f, store));
            Assert.IsTrue(store.IsActive(k_TargetHash, StatusEffectId.Poison));
        }

        [Test]
        public void StatusEffectApplication_Immediate_UsesExplicitModeInsteadOfAccumulationResistance()
        {
            StatusEffectStore store = new StatusEffectStore();

            StatusEffectApplicationLogic.Apply(new[] { Application(StatusEffectId.DefenseUp) },
                k_SourceHash, k_TargetHash, GuardResult.NoGuard, 1f, store);

            Assert.IsTrue(store.IsActive(k_TargetHash, StatusEffectId.DefenseUp));
        }

        [Test]
        public void StatusEffectApplication_SourceTarget_AppliesToSourceWithoutTargetsResistance()
        {
            StatusEffectStore store = new StatusEffectStore();
            StatusEffectApplicationData application = Application(StatusEffectId.SpeedUp,
                StatusEffectApplicationMode.Accumulate, StatusEffectTarget.Source);

            StatusEffectApplicationLogic.Apply(new[] { application },
                k_SourceHash, k_TargetHash, GuardResult.NoGuard, 1f, store);

            Assert.IsTrue(store.IsActive(k_SourceHash, StatusEffectId.SpeedUp));
            Assert.IsFalse(store.IsActive(k_TargetHash, StatusEffectId.SpeedUp));
        }

        [TestCase(GuardResult.Guarded)]
        [TestCase(GuardResult.JustGuard)]
        public void StatusEffectApplication_GuardBlockedEntry_DoesNotAccumulateOrApply(GuardResult guard)
        {
            StatusEffectStore store = new StatusEffectStore();
            StatusEffectApplicationData[] applications =
            {
                Application(StatusEffectId.Poison, StatusEffectApplicationMode.Accumulate),
                Application(StatusEffectId.AttackUp, target: StatusEffectTarget.Source)
            };

            Assert.AreEqual(StatusEffectId.None, StatusEffectApplicationLogic.Apply(applications,
                k_SourceHash, k_TargetHash, guard, 0f, store));
            Assert.AreEqual(0, store.Count);
        }

        [Test]
        public void StatusEffectApplication_GuardIgnoredEntry_AppliesOnlyExplicitlyAllowedEntry()
        {
            StatusEffectStore store = new StatusEffectStore();
            StatusEffectApplicationData[] applications =
            {
                Application(StatusEffectId.Poison),
                Application(StatusEffectId.AttackUp, target: StatusEffectTarget.Source,
                    guardRule: StatusEffectGuardRule.Ignore)
            };

            Assert.AreEqual(StatusEffectId.AttackUp, StatusEffectApplicationLogic.Apply(applications,
                k_SourceHash, k_TargetHash, GuardResult.Guarded, 0f, store));
            Assert.IsTrue(store.IsActive(k_SourceHash, StatusEffectId.AttackUp));
            Assert.IsFalse(store.IsActive(k_TargetHash, StatusEffectId.Poison));
        }

        [Test]
        public void StatusEffectApplication_NoEntries_ReturnsNoneWithoutCreatingState()
        {
            StatusEffectStore store = new StatusEffectStore();

            Assert.AreEqual(StatusEffectId.None, StatusEffectApplicationLogic.Apply(null,
                k_SourceHash, k_TargetHash, GuardResult.NoGuard, 0f, store));
            Assert.AreEqual(StatusEffectId.None, StatusEffectApplicationLogic.Apply(
                Array.Empty<StatusEffectApplicationData>(), k_SourceHash, k_TargetHash,
                GuardResult.NoGuard, 0f, store));
            Assert.AreEqual(0, store.Count);
            Assert.IsEmpty(StatusEffectApplicationLogic.Resolve(null));
            Assert.IsEmpty(StatusEffectApplicationLogic.Resolve(Array.Empty<StatusEffectApplication>()));
        }

        [Test]
        public void StatusEffectApplication_MissingDefinition_RejectsInvalidAuthoringEntry()
        {
            Assert.Throws<ArgumentException>(() => StatusEffectApplicationLogic.Resolve(
                new[] { new StatusEffectApplication() }));
        }

        [Test]
        public void StatusEffectApplication_ChildMagic_InheritsResolvedApplications()
        {
            StatusEffectApplicationData[] applications =
            {
                Application(StatusEffectId.Burn), Application(StatusEffectId.Slow)
            };
            StatusEffectApplicationData[] equipment = { Application(StatusEffectId.Poison) };
            MagicDefinition parent = new MagicDefinition
            {
                statusEffects = applications, equipmentStatusEffects = equipment
            };

            MagicDefinition child = ChildBulletHelper.CreateChildMagic(parent, new ChildBulletConfig());

            Assert.AreSame(applications, child.statusEffects);
            Assert.AreSame(equipment, child.equipmentStatusEffects);
            StatusEffectStore store = new StatusEffectStore();
            StatusEffectApplicationLogic.Apply(child.statusEffects, k_SourceHash,
                k_TargetHash, GuardResult.NoGuard, 0f, store, child.equipmentStatusEffects);
            Assert.IsTrue(store.IsActive(k_TargetHash, StatusEffectId.Burn));
            Assert.IsTrue(store.IsActive(k_TargetHash, StatusEffectId.Slow));
            Assert.IsTrue(store.IsActive(k_TargetHash, StatusEffectId.Poison));
        }
    }
}
