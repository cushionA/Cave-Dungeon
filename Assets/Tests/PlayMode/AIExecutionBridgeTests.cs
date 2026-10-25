using System.Collections;
using System.Collections.Generic;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.PlayMode
{
    public class AIExecutionBridgeTests
    {
        private readonly List<Object> _created = new List<Object>();
        private ActionExecutor _executor;
        private ActionExecutorController _controller;
        private int _owner;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            TestSceneHelper.CreateGameManager();
            Game.Core.CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo();
            _created.Add(info);
            GameObject actor = TestSceneHelper.CreateBaseCharacterObject(info, new Vector3(0f, 2f, 0f));
            _created.Add(actor);
            GameObject hitbox = new GameObject("Hitbox");
            hitbox.transform.SetParent(actor.transform);
            hitbox.AddComponent<DamageDealer>();
            _controller = actor.AddComponent<ActionExecutorController>();
            yield return null;
            _owner = actor.GetComponent<BaseCharacter>().ObjectHash;
            _executor = new ActionExecutor();
            _executor.Register(new InstantActionHandler());
            _executor.Register(new SustainedActionHandler());
            _executor.SetExecutionBackend(_controller);
        }

        private AttackInfo CreateAttack(float duration)
        {
            AttackInfo attack = ScriptableObject.CreateInstance<AttackInfo>();
            attack.durationMode = ActionDurationMode.Fixed;
            attack.duration = duration;
            _created.Add(attack);
            return attack;
        }

        [UnityTest]
        public IEnumerator AIExecution_TargetMovement_RefreshesDirectionAndSharedSpeedEffects()
        {
            Game.Core.CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo();
            _created.Add(info);
            GameObject target = TestSceneHelper.CreateBaseCharacterObject(info, new Vector3(10f, 2f, 0f));
            _created.Add(target);
            yield return null;
            int targetHash = target.GetComponent<BaseCharacter>().ObjectHash;
            GameManager.Data.GetVitals(_owner).position = Vector2.zero;
            GameManager.Data.GetVitals(targetHash).position = new Vector2(10f, 0f);
            float baseSpeed = GameManager.Data.GetMoveParams(_owner).moveSpeed;
            GameManager.StatusEffects.Apply(_owner, new StatusEffectInfo { effect = StatusEffectId.Slow, duration = 10f, modifier = 0.5f });

            Assert.IsTrue(_executor.Execute(_owner, targetHash, new ActionSlot
            {
                execType = ActionExecType.Sustained, paramId = (int)SustainedAction.Follow,
            }));
            Assert.IsTrue(GameManager.Actions.Execution.TryGetMovement(_owner, true, out Vector2 initial));
            Assert.AreEqual(baseSpeed * 0.5f, initial.x, 0.001f);
            GameManager.StatusEffects.Remove(_owner, StatusEffectId.Slow);
            GameManager.StatusEffects.Apply(_owner, new StatusEffectInfo { effect = StatusEffectId.SpeedUp, duration = 10f, modifier = 0.5f });
            GameManager.Data.GetVitals(targetHash).position = new Vector2(-10f, 0f);

            _executor.Tick(0f);

            Assert.IsTrue(GameManager.Actions.Execution.TryGetMovement(_owner, true, out Vector2 updated));
            Assert.AreEqual(baseSpeed * -1.5f, updated.x, 0.001f);
            GameManager.StatusEffects.Apply(_owner, new StatusEffectInfo { effect = StatusEffectId.Freeze, duration = 10f });
            _executor.Tick(0f);
            Assert.IsTrue(GameManager.Actions.Execution.TryGetMovement(_owner, true, out Vector2 blocked));
            Assert.AreEqual(Vector2.zero, blocked);
        }

        [UnityTest]
        public IEnumerator AIExecution_QueuedContinuation_RemainsBusyUntilChildCompletes()
        {
            AttackInfo parent = CreateAttack(0.1f);
            AttackInfo child = CreateAttack(0.2f);
            parent.transitions = new[] { new ActionTransitionDefinition { trigger = ActionSignalKind.Completed, next = child } };
            _controller.SetAttackInfosForTest(new[] { parent });
            int completed = 0;
            _executor.OnActionCompleted += () => completed++;
            Assert.IsTrue(_executor.Execute(_owner, 0, new ActionSlot { execType = ActionExecType.Attack }));

            GameManager.Actions.Execution.Tick(0.11f);

            Assert.IsTrue(_executor.IsExecuting);
            Assert.AreEqual(0, completed);
            _executor.Tick(0f);
            Assert.AreEqual(0, completed);
            GameManager.Actions.Execution.Tick(0f);
            Assert.IsTrue(_executor.IsExecuting);
            GameManager.Actions.Execution.Tick(0.21f);
            Assert.IsFalse(_executor.IsExecuting);
            Assert.AreEqual(1, completed);
            yield break;
        }

        [UnityTest]
        public IEnumerator AIExecution_CancelBetweenParentAndChild_RemovesQueuedContinuation()
        {
            AttackInfo parent = CreateAttack(0.1f);
            AttackInfo child = CreateAttack(0.2f);
            parent.transitions = new[] { new ActionTransitionDefinition { trigger = ActionSignalKind.Completed, next = child } };
            _controller.SetAttackInfosForTest(new[] { parent });
            Assert.IsTrue(_executor.Execute(_owner, 0, new ActionSlot { execType = ActionExecType.Attack }));
            GameManager.Actions.Execution.Tick(0.11f);

            _executor.CancelCurrent();
            GameManager.Actions.Execution.Tick(0f);

            Assert.IsFalse(_executor.IsExecuting);
            Assert.IsFalse(GameManager.Actions.IsPrimaryExecuting(_owner));
            yield break;
        }

        [UnityTest]
        public IEnumerator AIExecution_HitConfirmedTransition_TracksChildWithoutEarlyCompletion()
        {
            AttackInfo parent = CreateAttack(1f);
            parent.cancelAfter = 0f;
            AttackInfo child = CreateAttack(0.2f);
            parent.transitions = new[]
            {
                new ActionTransitionDefinition { trigger = ActionSignalKind.HitConfirmed, requiresHit = true, next = child },
            };
            _controller.SetAttackInfosForTest(new[] { parent });
            int completed = 0;
            _executor.OnActionCompleted += () => completed++;
            Assert.IsTrue(_executor.Execute(_owner, 0, new ActionSlot { execType = ActionExecType.Attack }));
            long parentId = _controller.CurrentInstanceId;
            Assert.IsTrue(GameManager.Actions.Execution.Signal(parentId, ActionSignalKind.HitConfirmed));
            GameManager.Actions.Execution.Tick(0f);
            Assert.IsTrue(_executor.IsExecuting);
            Assert.AreEqual(0, completed);
            Assert.AreNotEqual(parentId, _controller.CurrentInstanceId);
            GameManager.Actions.Execution.Tick(0.21f);
            Assert.IsFalse(_executor.IsExecuting);
            Assert.AreEqual(1, completed);
            yield break;
        }

        [UnityTest]
        public IEnumerator ActionController_CancelAfterParentCompletion_RemovesQueuedContinuation()
        {
            AttackInfo parent = CreateAttack(0.1f);
            AttackInfo child = CreateAttack(0.2f);
            parent.transitions = new[] { new ActionTransitionDefinition { trigger = ActionSignalKind.Completed, next = child } };
            _controller.SetAttackInfosForTest(new[] { parent });
            Assert.IsTrue(_controller.ExecuteAction(new ActionSlot { execType = ActionExecType.Attack }));
            GameManager.Actions.Execution.Tick(0.11f);
            _controller.CancelAction();
            GameManager.Actions.Execution.Tick(0f);
            Assert.IsFalse(GameManager.Actions.IsPrimaryExecuting(_owner));
            yield break;
        }

        [UnityTest]
        public IEnumerator AIExecution_RejectedQueuedChild_NotifiesCompletionWhenObserved()
        {
            AttackInfo parent = CreateAttack(0.1f);
            AttackInfo child = CreateAttack(0.2f);
            child.staminaCost = 1000f;
            parent.transitions = new[] { new ActionTransitionDefinition { trigger = ActionSignalKind.Completed, next = child } };
            _controller.SetAttackInfosForTest(new[] { parent });
            int completed = 0;
            _executor.OnActionCompleted += () => completed++;
            Assert.IsTrue(_executor.Execute(_owner, 0, new ActionSlot { execType = ActionExecType.Attack }));
            GameManager.Actions.Execution.Tick(0.11f);
            Assert.IsTrue(_executor.IsExecuting);

            GameManager.Actions.Execution.Tick(0f);

            Assert.IsFalse(_executor.IsExecuting);
            Assert.AreEqual(0, completed);
            _executor.Tick(0f);
            Assert.AreEqual(1, completed);
            _executor.Tick(0f);
            Assert.AreEqual(1, completed);
            yield break;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _executor?.CancelCurrent();
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null) { Object.Destroy(_created[i]); }
            }
            _created.Clear();
            yield return null;
            TestSceneHelper.Cleanup();
            yield return null;
        }

        [UnityTest]
        public IEnumerator AIExecution_Dodge_UsesSharedLifetimeAndChargesOnce()
        {
            MoveParams move = GameManager.Data.GetMoveParams(_owner);
            move.dodgeDuration = 0.2f;
            move.dodgeStaminaCost = 10f;
            move.dodgeSpeedMultiplier = 2f;
            GameManager.Data.GetMoveParams(_owner) = move;
            float stamina = GameManager.Data.GetVitals(_owner).currentStamina;
            int completed = 0;
            _executor.OnActionCompleted += () => completed++;

            Assert.IsTrue(_executor.Execute(_owner, 0, new ActionSlot
            {
                execType = ActionExecType.Instant, paramId = (int)InstantAction.Dodge,
            }));

            Assert.IsTrue(_executor.IsExecuting);
            Assert.AreEqual(stamina - 10f, GameManager.Data.GetVitals(_owner).currentStamina);
            Assert.AreEqual(0, completed);
            GameManager.Actions.Execution.Tick(0.21f);
            Assert.IsFalse(_executor.IsExecuting);
            Assert.AreEqual(1, completed);
            yield break;
        }

        [UnityTest]
        public IEnumerator AIExecution_UnsupportedRequest_RejectsWithoutEndingGuard()
        {
            Assert.IsTrue(_executor.Execute(_owner, 0, new ActionSlot
            {
                execType = ActionExecType.Sustained, paramId = (int)SustainedAction.Guard,
            }));
            int completed = 0;
            _executor.OnActionCompleted += () => completed++;

            Assert.IsFalse(_executor.Execute(_owner, 0, new ActionSlot
            {
                execType = ActionExecType.Instant, paramId = (int)InstantAction.BodySlam,
            }));

            Assert.IsTrue(_executor.IsExecuting);
            Assert.IsTrue(GameManager.Data.GetDefenseState(_owner).isGuarding);
            Assert.AreEqual(0, completed);
            yield break;
        }

        [UnityTest]
        public IEnumerator AIExecution_Idle_OwnsMovementUntilSharedDurationEnds()
        {
            Assert.IsTrue(_executor.Execute(_owner, 0, new ActionSlot
            {
                execType = ActionExecType.Sustained, paramId = (int)SustainedAction.Idle, paramValue = 0.25f,
            }));

            Assert.IsTrue(_executor.IsExecuting);
            Assert.IsTrue(_controller.HasMovementRequest);
            Assert.IsTrue(GameManager.Actions.Execution.TryGetMovement(_owner, true, out Vector2 movement));
            Assert.AreEqual(Vector2.zero, movement);
            _executor.Tick(10f);
            Assert.IsTrue(_executor.IsExecuting);
            GameManager.Actions.Execution.Tick(0.3f);
            Assert.IsFalse(_executor.IsExecuting);
            Assert.IsFalse(_controller.HasMovementRequest);
            yield break;
        }
    }
}
