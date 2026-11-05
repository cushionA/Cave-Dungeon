using System.Collections.Generic;
using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    [TestFixture]
    public class StatusEffectWiringTests
    {
        [Test]
        public void StatusEffect_Stun_RejectsMovementAndCombatInputWithoutConsumingMenuInput()
        {
            StatusEffectManager manager = new StatusEffectManager();
            manager.Apply(new StatusEffectInfo { effect = StatusEffectId.Stun, duration = 5f });
            MovementInfo input = new MovementInfo
            {
                jumpPressed = true, jumpHeld = true, dodgePressed = true, sprintHeld = true,
                attackInput = AttackInputType.LightAttack, guardHeld = true, menuPressed = true,
                moveDirection = UnityEngine.Vector2.right
            };

            MovementInfo restricted = StatusEffectSystem.RestrictInput(input, manager.GetSummary());

            Assert.AreEqual(UnityEngine.Vector2.zero, restricted.moveDirection);
            Assert.IsFalse(restricted.jumpPressed);
            Assert.IsFalse(restricted.jumpHeld);
            Assert.IsFalse(restricted.dodgePressed);
            Assert.IsFalse(restricted.sprintHeld);
            Assert.IsFalse(restricted.guardHeld);
            Assert.IsNull(restricted.attackInput);
            Assert.IsTrue(restricted.menuPressed);
        }

        private StatusEffectManager _manager;

        [SetUp]
        public void SetUp()
        {
            _manager = new StatusEffectManager();
        }

        [Test]
        public void StatusEffect_BoundManager_UsesSharedStoreAsSourceOfTruth()
        {
            StatusEffectStore store = new StatusEffectStore();
            StatusEffectManager bound = new StatusEffectManager(store, 100);
            bound.Apply(new StatusEffectInfo
            {
                effect = StatusEffectId.AttackUp,
                duration = 3f,
                modifier = 0.5f,
            }, 200);

            Assert.IsTrue(store.TryGetEffect(100, StatusEffectId.AttackUp, out ActiveEffect active));
            Assert.AreEqual(200, active.sourceHash);
            Assert.AreEqual(100, active.targetHash);
            Assert.AreEqual(1.5f, bound.GetSummary().AttackMultiplier);
            Assert.IsTrue(store.RemoveInstance(active.instanceId));
            Assert.AreEqual(0, bound.ActiveCount);
            Assert.AreEqual(1f, bound.AttackMultiplier);
        }

        [Test]
        public void StatusEffect_Changes_ArePublishedAfterStateMutation()
        {
            int changes = 0;
            _manager.Store.Changed += (targetHash, id, kind) =>
            {
                changes++;
                Assert.AreEqual(kind == StatusEffectChangeKind.Applied || kind == StatusEffectChangeKind.Refreshed,
                    _manager.IsActive(id));
            };
            StatusEffectInfo info = new StatusEffectInfo { effect = StatusEffectId.SpeedUp, duration = 1f };
            _manager.Apply(info);
            _manager.Apply(info);
            _manager.Tick(1f);
            _manager.Apply(info);
            _manager.Remove(info.effect);
            _manager.Apply(info);
            _manager.ClearAll();

            Assert.AreEqual(7, changes);
        }

        [Test]
        public void StatusEffect_ClearAll_ClearsActiveAndAccumulationWithoutLeakingModifiers()
        {
            _manager.Accumulate(StatusEffectId.Poison, 50f, 0f);
            _manager.Apply(new StatusEffectInfo
            {
                effect = StatusEffectId.DefenseUp,
                duration = 2f,
                modifier = 0.25f,
            });
            Assert.AreEqual(1.25f, _manager.DefenseMultiplier);

            _manager.ClearAll();

            Assert.AreEqual(0, _manager.ActiveCount);
            Assert.AreEqual(0f, _manager.GetAccumulation(StatusEffectId.Poison));
            Assert.AreEqual(1f, _manager.DefenseMultiplier);
            Assert.IsFalse(_manager.Accumulate(StatusEffectId.Poison, 50f, 0f));
        }

        [Test]
        public void StatusEffect_Modifiers_ComposeAndExpireWithoutChangingBaseValues()
        {
            _manager.Apply(new StatusEffectInfo
            {
                effect = StatusEffectId.Slow,
                duration = 2f,
                modifier = 0.5f,
            });
            _manager.Apply(new StatusEffectInfo
            {
                effect = StatusEffectId.SpeedUp,
                duration = 3f,
                modifier = 0.2f,
            });
            _manager.Apply(new StatusEffectInfo
            {
                effect = StatusEffectId.Weakness,
                duration = 2f,
                modifier = 0.3f,
            });

            Assert.AreEqual(0.6f, _manager.MoveSpeedMultiplier, 0.0001f);
            Assert.AreEqual(1.3f, _manager.DamageTakenMultiplier, 0.0001f);
            _manager.Tick(2f);
            Assert.AreEqual(1.2f, _manager.MoveSpeedMultiplier, 0.0001f);
            Assert.AreEqual(1f, _manager.DamageTakenMultiplier);
        }

        [Test]
        public void StatusEffect_SharedState_ExposesActionLocksAndClearsThemAtExpiration()
        {
            StatusEffectStore store = new StatusEffectStore();
            StatusEffectManager bound = new StatusEffectManager(store, 100);
            bound.Apply(new StatusEffectInfo { effect = StatusEffectId.Silence, duration = 3f });
            CharacterStatusEffects state = store.GetSummary(100);
            Assert.IsTrue(state.BlocksCasting);
            Assert.IsFalse(state.BlocksActions);
            Assert.IsFalse(state.BlocksMovement);

            bound.Apply(new StatusEffectInfo { effect = StatusEffectId.Freeze, duration = 1f });
            state = store.GetSummary(100);
            Assert.IsTrue(state.BlocksActions);
            Assert.IsTrue(state.BlocksMovement);
            bound.Tick(1f);
            state = store.GetSummary(100);
            Assert.IsFalse(state.BlocksActions);
            Assert.IsFalse(state.BlocksMovement);
            Assert.IsTrue(state.BlocksCasting);
            bound.Tick(2f);
            state = store.GetSummary(100);
            Assert.IsFalse(state.BlocksCasting);
        }

        [Test]
        public void StatusEffect_IndependentSources_KeepSeparateTimesAndStableInstanceIds()
        {
            StatusEffectStore store = new StatusEffectStore(1);
            StatusEffectInfo info = new StatusEffectInfo { effect = StatusEffectId.AttackUp, duration = 2f, modifier = 0.2f };
            store.Apply(100, info, 1);
            int firstId = store.GetActiveEffect(100, 0).instanceId;
            info.duration = 5f;
            store.Apply(100, info, 2);
            Assert.IsTrue(store.TryGetEffect(100, info.effect, out ActiveEffect longest));
            Assert.AreEqual(2, longest.sourceHash);
            Assert.AreEqual(5f, store.GetRemainingTime(100, info.effect));
            Assert.AreEqual(2, store.GetActiveCount(100));

            store.Tick(100, 2f);

            Assert.IsFalse(store.TryGetInstance(firstId, out _));
            Assert.IsTrue(store.TryGetInstance(longest.instanceId, out ActiveEffect remaining));
            Assert.AreEqual(3f, remaining.remainTime);
            Assert.AreEqual(1.2f, store.GetSummary(100).AttackMultiplier, 0.0001f);
            Assert.IsTrue(store.RemoveInstance(longest.instanceId));
            Assert.IsFalse(store.IsActive(100, info.effect));
        }

        [Test]
        public void StatusEffect_TwoTargets_ExpireAndClearIndependently()
        {
            StatusEffectStore store = new StatusEffectStore();
            StatusEffectInfo info = new StatusEffectInfo { effect = StatusEffectId.Slow, duration = 4f, modifier = 0.5f };
            store.Apply(100, info, 1);
            store.Apply(200, info, 1);
            store.Tick(100, 1f);
            Assert.AreEqual(3f, store.GetRemainingTime(100, info.effect));
            Assert.AreEqual(4f, store.GetRemainingTime(200, info.effect));
            store.ClearAll(100);
            Assert.IsFalse(store.IsActive(100, info.effect));
            Assert.IsTrue(store.IsActive(200, info.effect));
        }

        [Test]
        public void StatusEffect_AccumulationFromDifferentSources_CombinesForTheSameTargetAndEffect()
        {
            StatusEffectStore store = new StatusEffectStore();
            StatusEffectInfo info = new StatusEffectInfo
            {
                effect = StatusEffectId.Poison,
                duration = 5f,
                accumulateValue = 50f,
            };
            Assert.IsFalse(store.Accumulate(100, info, 0f, 1));
            Assert.IsTrue(store.Accumulate(100, info, 0f, 2));
            Assert.AreEqual(0f, store.GetAccumulation(100, info.effect));
            Assert.IsTrue(store.TryGetEffect(100, info.effect, out ActiveEffect active));
            Assert.AreEqual(2, active.sourceHash);
        }

        [Test]
        public void StatusEffect_CompletedAccumulations_DoNotRetainHistoricalSourceRows()
        {
            StatusEffectStore store = new StatusEffectStore();
            StatusEffectInfo info = new StatusEffectInfo
            {
                effect = StatusEffectId.Poison,
                duration = 1f,
                accumulateValue = 50f,
            };
            for (int i = 0; i < 16; i++)
            {
                Assert.IsFalse(store.Accumulate(100, info, 0f, i * 2 + 1));
                Assert.AreEqual(1, store.Count);
                Assert.AreEqual(0, store.ActiveCount);
                Assert.IsTrue(store.Accumulate(100, info, 0f, i * 2 + 2));
                Assert.AreEqual(1, store.Count);
                Assert.AreEqual(1, store.ActiveCount);
                store.Tick(100, 1f);
                Assert.AreEqual(0, store.Count);
            }
        }

        [Test]
        public void StatusEffect_AccumulationCleanup_PreservesUnrelatedPendingRowsAndStableInstances()
        {
            StatusEffectStore store = new StatusEffectStore();
            StatusEffectInfo info = new StatusEffectInfo
            {
                effect = StatusEffectId.Poison,
                duration = 3f,
                accumulateValue = 50f,
            };
            store.Accumulate(100, info, 0f, 1);
            store.Accumulate(200, info, 0f, 9);
            store.Apply(300, new StatusEffectInfo { effect = StatusEffectId.AttackUp, duration = 4f }, 77);
            int preservedId = store.GetActiveEffect(300, 0).instanceId;

            Assert.IsTrue(store.Accumulate(100, info, 0f, 2));

            Assert.AreEqual(3, store.Count);
            Assert.AreEqual(2, store.ActiveCount);
            Assert.AreEqual(50f, store.GetAccumulation(200, info.effect));
            Assert.IsTrue(store.TryGetInstance(preservedId, out ActiveEffect preserved));
            Assert.AreEqual(300, preserved.targetHash);
            Assert.AreEqual(77, preserved.sourceHash);
            Assert.IsTrue(store.TryGetEffect(100, info.effect, out ActiveEffect active));
            Assert.AreEqual(2, active.sourceHash);
        }

        [Test]
        public void StatusEffect_TickAll_AttributesDamageAndPublishesAfterAllStateUpdates()
        {
            StatusEffectStore store = new StatusEffectStore();
            StatusEffectInfo info = new StatusEffectInfo
            {
                effect = StatusEffectId.Poison,
                duration = 2f,
                tickDamage = 5f,
                tickInterval = 1f,
            };
            store.Apply(100, info, 1);
            store.Apply(200, info, 2);
            int notifications = 0;
            store.Changed += (targetHash, id, kind) =>
            {
                notifications++;
                Assert.AreEqual(StatusEffectChangeKind.Expired, kind);
                Assert.AreEqual(0, store.ActiveCount);
            };
            List<StatusEffectTick> ticks = new List<StatusEffectTick>();

            store.TickAll(10f, ticks);

            Assert.AreEqual(2, ticks.Count);
            Assert.AreEqual(2, notifications);
            Assert.AreEqual(10, ticks[0].damage);
            Assert.AreEqual(10, ticks[1].damage);
            Assert.AreNotEqual(ticks[0].targetHash, ticks[1].targetHash);
            for (int i = 0; i < ticks.Count; i++)
            {
                Assert.AreEqual(ticks[i].targetHash == 100 ? 1 : 2, ticks[i].sourceHash);
            }
            store.TickAll(1f, ticks);
            Assert.AreEqual(0, ticks.Count);
        }

        // --- 蓄積→発症 ---

        [Test]
        public void StatusEffect_AccumulateToThreshold_TriggersEffect()
        {
            StatusEffectInfo info = new StatusEffectInfo
            {
                effect = StatusEffectId.Poison,
                accumulateValue = StatusEffectManager.k_DefaultThreshold,
                duration = 10f,
                tickDamage = 5f,
                tickInterval = 2f,
                modifier = 0f,
                maxStack = 0
            };

            bool triggered = _manager.Accumulate(info, 0f);

            Assert.IsTrue(triggered, "しきい値到達で発症するべき");
            Assert.IsTrue(_manager.IsActive(StatusEffectId.Poison));
            Assert.AreEqual(1, _manager.ActiveCount);
        }

        [Test]
        public void StatusEffect_BelowThreshold_DoesNotTrigger()
        {
            StatusEffectInfo info = new StatusEffectInfo
            {
                effect = StatusEffectId.Poison,
                accumulateValue = 50f, // しきい値100の半分
                duration = 10f,
                tickDamage = 5f,
                tickInterval = 2f,
                modifier = 0f,
                maxStack = 0
            };

            bool triggered = _manager.Accumulate(info, 0f);

            Assert.IsFalse(triggered, "しきい値未満では発症しない");
            Assert.IsFalse(_manager.IsActive(StatusEffectId.Poison));
        }

        // --- statusCutによる軽減 ---

        [Test]
        public void StatusEffect_StatusCut_ReducesAccumulation()
        {
            // statusCut=0.5 → 蓄積値が半分に
            StatusEffectInfo info = new StatusEffectInfo
            {
                effect = StatusEffectId.Burn,
                accumulateValue = StatusEffectManager.k_DefaultThreshold,
                duration = 10f,
                tickDamage = 5f,
                tickInterval = 2f,
                modifier = 0f,
                maxStack = 0
            };

            bool triggered = _manager.Accumulate(info, 0.5f);

            Assert.IsFalse(triggered, "statusCut=0.5で蓄積半減、しきい値未到達");
            Assert.IsFalse(_manager.IsActive(StatusEffectId.Burn));
        }

        [Test]
        public void StatusEffect_FullStatusCut_NoAccumulation()
        {
            // statusCut=1.0 → 蓄積値ゼロ
            StatusEffectInfo info = new StatusEffectInfo
            {
                effect = StatusEffectId.Stun,
                accumulateValue = 200f,
                duration = 10f,
                tickDamage = 0f,
                tickInterval = 1f,
                modifier = 0f,
                maxStack = 0
            };

            bool triggered = _manager.Accumulate(info, 1.0f);

            Assert.IsFalse(triggered, "statusCut=1.0で蓄積ゼロ");
        }

        // --- None効果は無視 ---

        [Test]
        public void StatusEffect_NoneEffect_DoesNotAccumulate()
        {
            StatusEffectInfo info = new StatusEffectInfo
            {
                effect = StatusEffectId.None,
                accumulateValue = 200f,
                duration = 10f,
                tickDamage = 0f,
                tickInterval = 1f,
                modifier = 0f,
                maxStack = 0
            };

            bool triggered = _manager.Accumulate(info, 0f);

            Assert.IsFalse(triggered);
            Assert.AreEqual(0, _manager.ActiveCount);
        }

        // --- DamageResult.appliedEffectの検証 ---

        [Test]
        public void DamageResult_AppliedEffectField_CanStoreStatusId()
        {
            DamageResult result = new DamageResult
            {
                appliedEffect = StatusEffectId.Poison
            };

            Assert.AreEqual(StatusEffectId.Poison, result.appliedEffect);
        }
    }
}
