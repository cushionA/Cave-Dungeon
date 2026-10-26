using System.Collections;
using System.Collections.Generic;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.PlayMode
{
    public class PlayerActionCancellationTests
    {
        private readonly List<Object> _created = new List<Object>();
        private PlayerCharacter _player;
        private GameObject _ground;
        private PlayerInputHandler _input;
        private ActionExecutorController _controller;
        private AttackInfo[] _attacks;
        private int _owner;
        private float _simulationTime;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            TestSceneHelper.CreateGameManager();
            GameManager.Instance.enabled = false;
            _ground = TestSceneHelper.CreateGround();
            _created.Add(_ground);

            Game.Core.CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo();
            info.dodgeStaminaCost = 10f;
            info.staminaRecoveryRate = 0f;
            _created.Add(info);
            GameObject actor = new GameObject("PlayerActionCancellationActor");
            actor.SetActive(false);
            actor.transform.position = new Vector3(0f, 0.5f, 0f);
            _created.Add(actor);
            actor.AddComponent<Rigidbody2D>();
            actor.AddComponent<BoxCollider2D>().size = new Vector2(0.6f, 0.9f);
            _input = actor.AddComponent<PlayerInputHandler>();
            _controller = actor.AddComponent<ActionExecutorController>();
            _player = actor.AddComponent<PlayerCharacter>();
            TestSceneHelper.SetCharacterInfo(_player, info);
            _attacks = new[] { CreateAttack(7f), CreateAttack(9f), CreateAttack(13f) };
            _controller.SetAttackInfosForTest(_attacks);
            _player.SetLightAttacksForTest(_attacks);
            actor.SetActive(true);
            _player.Rigidbody.gravityScale = 0f;
            _player.Rigidbody.constraints = RigidbodyConstraints2D.FreezeRotation | RigidbodyConstraints2D.FreezePositionY;
            yield return null;
            _owner = _player.ObjectHash;
            _simulationTime = Time.fixedTime;
            Physics2D.SyncTransforms();
            _player.UpdateGroundCheck();
            Assert.IsTrue(_player.IsGrounded);
        }

        private IEnumerator FixedTick()
        {
            // GameManagerが所有する一連の固定tickを一度だけ進め、その後に実Physics2Dを待つ。
            _simulationTime += Time.fixedDeltaTime;
            GameManager.Instance.SimulationTick(Time.fixedDeltaTime, _simulationTime);
            yield return new WaitForFixedUpdate();
        }

        private AttackInfo CreateAttack(float staminaCost)
        {
            MotionDefinition motion = ScriptableObject.CreateInstance<MotionDefinition>();
            motion.info = new MotionInfo { preMotionDuration = 0.1f, activeMotionDuration = 0.1f, recoveryDuration = 1.8f };
            _created.Add(motion);
            AttackInfo attack = ScriptableObject.CreateInstance<AttackInfo>();
            attack.motion = motion;
            attack.cancelAfter = 0.5f;
            attack.staminaCost = staminaCost;
            attack.inputWindow = 2f;
            _created.Add(attack);
            return attack;
        }

        private long CurrentAttack()
        {
            Assert.IsTrue(GameManager.Actions.Execution.GetPrimary(_owner, out ActionExecution action));
            return action.instanceId;
        }

        [UnityTest]
        public IEnumerator PlayerInput_RecoveryDodgeCancel_UsesRequestedDirectionAndConsumesOnce()
        {
            Assert.IsTrue(_controller.ExecuteAction(new ActionSlot { execType = ActionExecType.Attack }));
            long attack = CurrentAttack();
            GameManager.Actions.Execution.Tick(0.6f);
            Assert.IsTrue(GameManager.Actions.Execution.TryGetInstance(attack, out ActionExecution recovery));
            Assert.AreEqual(AnimationPhase.Recovery, recovery.phase);

            _input.SetOverrideInput(new MovementInfo { dodgePressed = true, moveDirection = Vector2.left });
            yield return FixedTick();

            Assert.IsFalse(GameManager.Actions.Execution.TryGetInstance(attack, out _));
            Assert.IsTrue(GameManager.Actions.IsDodging(_owner));
            Assert.Less(_player.Rigidbody.linearVelocity.x, 0f);
            Assert.AreEqual(83f, GameManager.Data.GetVitals(_owner).currentStamina);
            Assert.IsFalse(_input.CurrentInput.dodgePressed);
            GameManager.Actions.Execution.Tick(1f);
            yield return FixedTick();
            Assert.IsFalse(GameManager.Actions.IsDodging(_owner));
            Assert.AreEqual(83f, GameManager.Data.GetVitals(_owner).currentStamina);
        }

        [UnityTest]
        public IEnumerator PlayerInput_RejectedDodge_RetainsRequestWithoutInterruptingOrCharging()
        {
            Assert.IsTrue(_controller.ExecuteAction(new ActionSlot { execType = ActionExecType.Attack }));
            long attack = CurrentAttack();
            _input.SetOverrideInput(new MovementInfo { dodgePressed = true, moveDirection = Vector2.left });
            yield return FixedTick();

            Assert.AreEqual(attack, CurrentAttack());
            Assert.IsFalse(GameManager.Actions.IsDodging(_owner));
            Assert.AreEqual(93f, GameManager.Data.GetVitals(_owner).currentStamina);
            Assert.IsTrue(_input.CurrentInput.dodgePressed);
            Assert.AreEqual(0f, _player.Rigidbody.linearVelocity.x);

            GameManager.Actions.Execution.Tick(0.6f);
            yield return FixedTick();
            Assert.IsTrue(GameManager.Actions.IsDodging(_owner));
            Assert.AreEqual(83f, GameManager.Data.GetVitals(_owner).currentStamina);
            Assert.IsFalse(_input.CurrentInput.dodgePressed);
        }

        [UnityTest]
        public IEnumerator PlayerInput_QueuedCombo_CancelsWhenSharedWindowOpensBeforeCompletion()
        {
            _input.SetOverrideInput(new MovementInfo { attackInput = AttackInputType.LightAttack });
            yield return FixedTick();
            long first = CurrentAttack();
            _input.SetOverrideInput(new MovementInfo { attackInput = AttackInputType.ChargeLight, chargeMultiplier = 2.5f });
            yield return FixedTick();
            Assert.AreEqual(first, CurrentAttack());
            Assert.AreEqual(93f, GameManager.Data.GetVitals(_owner).currentStamina);
            Assert.AreEqual(AttackInputType.ChargeLight, _input.CurrentInput.attackInput);

            _input.SetOverrideInput(default);
            GameManager.Actions.Execution.Tick(0.6f);
            yield return FixedTick();

            Assert.IsFalse(GameManager.Actions.Execution.TryGetInstance(first, out _));
            Assert.IsTrue(GameManager.Actions.Execution.TryGetInstance(CurrentAttack(), out ActionExecution next));
            Assert.AreEqual(GameManager.Actions.RegisterDefinition(_attacks[1]), next.definitionId);
            Assert.AreEqual(2.5f, next.chargeMultiplier);
            Assert.AreEqual(84f, GameManager.Data.GetVitals(_owner).currentStamina);
        }

        [UnityTest]
        public IEnumerator PlayerInput_HeavyCancelWithQueuedLight_SubmitsOnlyTheExplicitHeavyRequest()
        {
            _input.SetOverrideInput(new MovementInfo { attackInput = AttackInputType.LightAttack });
            yield return FixedTick();
            long first = CurrentAttack();
            _input.SetOverrideInput(new MovementInfo { attackInput = AttackInputType.LightAttack });
            yield return FixedTick();
            GameManager.Actions.Execution.Tick(0.6f);
            _input.SetOverrideInput(new MovementInfo { attackInput = AttackInputType.HeavyAttack });
            yield return FixedTick();

            Assert.IsFalse(GameManager.Actions.Execution.TryGetInstance(first, out _));
            Assert.IsTrue(GameManager.Actions.Execution.TryGetInstance(CurrentAttack(), out ActionExecution heavy));
            Assert.AreEqual(GameManager.Actions.RegisterDefinition(_attacks[2]), heavy.definitionId);
            Assert.AreEqual(80f, GameManager.Data.GetVitals(_owner).currentStamina);
            Assert.IsNull(_input.CurrentInput.attackInput);
        }

        [UnityTest]
        public IEnumerator PlayerInput_AcceptedAttack_DoesNotReplayTheSnapshotAcrossFixedUpdates()
        {
            _attacks[0].cancelAfter = 0f;
            _input.SetOverrideInput(new MovementInfo { attackInput = AttackInputType.LightAttack });
            yield return FixedTick();
            long first = CurrentAttack();
            Assert.IsNull(_input.CurrentInput.attackInput);
            yield return FixedTick();
            Assert.AreEqual(first, CurrentAttack());
            Assert.AreEqual(93f, GameManager.Data.GetVitals(_owner).currentStamina);

            GameManager.Actions.Execution.Tick(2.1f);
            yield return FixedTick();
            Assert.IsFalse(GameManager.Actions.IsPrimaryExecuting(_owner));
            Assert.AreEqual(93f, GameManager.Data.GetVitals(_owner).currentStamina);
        }

        [UnityTest]
        public IEnumerator PlayerInput_AcceptedJump_DoesNotReplayTheSnapshotAcrossFixedTicks()
        {
            ref MoveParams move = ref GameManager.Data.GetMoveParams(_owner);
            move.jumpForce = 10f;
            move.jumpStaminaCost = 7f;
            _input.SetOverrideInput(new MovementInfo { jumpPressed = true, jumpHeld = true });

            yield return FixedTick();

            Assert.AreEqual(93f, GameManager.Data.GetVitals(_owner).currentStamina);
            Assert.IsFalse(_input.CurrentInput.jumpPressed);
            Assert.IsTrue(_input.CurrentInput.jumpHeld);

            // SetupでY位置を固定しているため接地が続く。押下を再公開すると再ジャンプになる。
            yield return FixedTick();
            yield return FixedTick();

            Assert.AreEqual(93f, GameManager.Data.GetVitals(_owner).currentStamina);
            Assert.IsFalse(_input.CurrentInput.jumpPressed);
            Assert.IsTrue(_input.CurrentInput.jumpHeld);
        }

        [UnityTest]
        public IEnumerator PlayerInput_AcceptedDropThrough_ConsumesJumpWithoutStartingNormalJumpInTheSameTick()
        {
            _ground.AddComponent<DropThroughPlatform>();
            ref MoveParams move = ref GameManager.Data.GetMoveParams(_owner);
            move.jumpForce = 10f;
            move.jumpStaminaCost = 7f;
            _input.SetOverrideInput(new MovementInfo
            {
                jumpPressed = true, jumpHeld = true, moveDirection = Vector2.down,
            });

            yield return FixedTick();

            Assert.IsTrue(_player.IsDropThrough);
            Assert.IsFalse(_input.CurrentInput.jumpPressed);
            Assert.IsTrue(_input.CurrentInput.jumpHeld);
            Assert.AreEqual(100f, GameManager.Data.GetVitals(_owner).currentStamina);
        }

        private void MoveToAir()
        {
            _player.SetPhysicsPosition(new Vector2(0f, 5f));
            _player.Rigidbody.linearVelocity = Vector2.zero;
            Physics2D.SyncTransforms();
            _player.UpdateGroundCheck();
            Assert.IsFalse(_player.IsGrounded);
        }

        [UnityTest]
        public IEnumerator PlayerAction_RegisteredComboWindow_IgnoresLaterAuthoringChange()
        {
            GameManager.Actions.RegisterDefinition(_attacks[0]);
            _attacks[0].inputWindow = 0.001f;
            _input.SetOverrideInput(new MovementInfo { attackInput = AttackInputType.LightAttack });
            yield return FixedTick();
            long first = CurrentAttack();
            GameManager.Actions.Execution.Tick(0.6f);

            _input.SetOverrideInput(new MovementInfo { attackInput = AttackInputType.LightAttack });
            yield return FixedTick();

            Assert.IsFalse(GameManager.Actions.Execution.TryGetInstance(first, out _));
            Assert.IsTrue(GameManager.Actions.Execution.TryGetInstance(CurrentAttack(), out ActionExecution next));
            Assert.AreEqual(GameManager.Actions.RegisterDefinition(_attacks[1]), next.definitionId);
            Assert.AreEqual(84f, GameManager.Data.GetVitals(_owner).currentStamina);
        }

        [UnityTest]
        public IEnumerator PlayerAction_ZeroDurationAttack_PreservesRegisteredCustomComboWindow()
        {
            _attacks[0].durationMode = ActionDurationMode.Fixed;
            _attacks[0].duration = 0f;
            _attacks[0].inputWindow = 0.04f;
            _input.SetOverrideInput(new MovementInfo { attackInput = AttackInputType.LightAttack });
            yield return FixedTick();
            Assert.IsFalse(GameManager.Actions.IsPrimaryExecuting(_owner));
            Assert.AreEqual(93f, GameManager.Data.GetVitals(_owner).currentStamina);

            // 完了済みinstanceから設定を失い0.6秒へfallbackすると次段が選ばれてしまう。
            for (int i = 0; i < 4; i++) { yield return FixedTick(); }
            _input.SetOverrideInput(new MovementInfo { attackInput = AttackInputType.LightAttack });
            yield return FixedTick();

            Assert.IsFalse(GameManager.Actions.IsPrimaryExecuting(_owner));
            Assert.AreEqual(86f, GameManager.Data.GetVitals(_owner).currentStamina);
        }

        [UnityTest]
        public IEnumerator PlayerAction_RegisteredAerialPhysics_IgnoresLaterAuthoringChange()
        {
            _attacks[0].airHangDuration = 0.3f;
            _attacks[0].airHangGravityScale = 0.15f;
            GameManager.Actions.RegisterDefinition(_attacks[0]);
            _attacks[0].airHangDuration = 5f;
            _attacks[0].airHangGravityScale = 0.9f;
            MoveToAir();

            _input.SetOverrideInput(new MovementInfo { attackInput = AttackInputType.AerialLight });
            yield return FixedTick();
            long instanceId = CurrentAttack();
            Assert.AreEqual(0.15f, _player.Rigidbody.gravityScale);

            GameManager.Actions.Execution.Tick(0.31f);
            yield return FixedTick();

            Assert.IsTrue(GameManager.Actions.Execution.TryGetInstance(instanceId, out _));
            Assert.AreEqual(GameConstants.k_GravityScale, _player.Rigidbody.gravityScale);
        }

        [UnityTest]
        public IEnumerator PlayerAction_AerialExecutionEndsBeforeHangDuration_RestoresGravityImmediately()
        {
            MoveToAir();
            _attacks[0].durationMode = ActionDurationMode.Fixed;
            _attacks[0].duration = 0.2f;
            _attacks[0].airHangDuration = 2f;
            _input.SetOverrideInput(new MovementInfo { attackInput = AttackInputType.AerialLight });
            yield return FixedTick();
            Assert.AreEqual(_attacks[0].airHangGravityScale, _player.Rigidbody.gravityScale);
            Assert.IsTrue(GameManager.Actions.Execution.TryGetInstance(CurrentAttack(), out ActionExecution action));

            GameManager.Actions.Execution.Tick(0.21f);

            Assert.IsFalse(GameManager.Actions.IsGroupActive(action.groupId));
            Assert.AreEqual(GameConstants.k_GravityScale, _player.Rigidbody.gravityScale);
            Assert.AreEqual(ActState.Neutral, GameManager.Data.GetFlags(_owner).ActState);
            yield return FixedTick();
            Assert.IsFalse(GameManager.Actions.IsMovementBlocked(_owner));
        }

        [UnityTest]
        public IEnumerator PlayerAction_AerialHangReadsSharedElapsed_EndsWhileAttackContinues()
        {
            MoveToAir();
            _attacks[0].airHangDuration = 0.3f;
            _input.SetOverrideInput(new MovementInfo { attackInput = AttackInputType.AerialLight });
            yield return FixedTick();
            long instanceId = CurrentAttack();
            Assert.AreEqual(_attacks[0].airHangGravityScale, _player.Rigidbody.gravityScale);

            GameManager.Actions.Execution.Tick(0.31f);
            yield return FixedTick();

            Assert.IsTrue(GameManager.Actions.Execution.TryGetInstance(instanceId, out _));
            Assert.AreEqual(GameConstants.k_GravityScale, _player.Rigidbody.gravityScale);
        }

        [UnityTest]
        public IEnumerator PlayerAction_DiveGroundContact_StartsSharedRecoveryWithoutNeutralGap()
        {
            MoveToAir();
            _attacks[2].divingLandingRecoveryDuration = 0.4f;
            _input.SetOverrideInput(new MovementInfo { attackInput = AttackInputType.AerialHeavy, chargeMultiplier = 1.5f });
            yield return FixedTick();
            Assert.IsTrue(GameManager.Actions.Execution.TryGetInstance(CurrentAttack(), out ActionExecution dive));
            Assert.AreEqual(ActionEndMode.Manual, dive.definition.endMode);
            Assert.AreEqual(1.5f, dive.chargeMultiplier);
            Assert.AreEqual(GameConstants.k_GravityScale * _attacks[2].divingGravityMultiplier, _player.Rigidbody.gravityScale);
            GameManager.Actions.Execution.Tick(3f);
            Assert.IsTrue(GameManager.Actions.Execution.TryGetInstance(dive.instanceId, out _));

            _player.SetPhysicsPosition(new Vector2(0f, 0.5f));
            _player.Rigidbody.linearVelocity = Vector2.zero;
            Physics2D.SyncTransforms();
            yield return FixedTick();

            Assert.IsFalse(GameManager.Actions.Execution.TryGetInstance(dive.instanceId, out _));
            Assert.IsTrue(GameManager.Actions.TryGetGroupPrimary(dive.groupId, out ActionExecution landing));
            Assert.AreEqual(AnimationPhase.Recovery, landing.phase);
            Assert.AreEqual(ActState.AttackRecovery, GameManager.Data.GetFlags(_owner).ActState);
            Assert.IsTrue(GameManager.Actions.IsMovementBlocked(_owner));
            Assert.IsTrue(_controller.IsExecuting);
            Assert.AreEqual(GameConstants.k_GravityScale, _player.Rigidbody.gravityScale);

            GameManager.Actions.Execution.Tick(0.41f);
            Assert.IsFalse(GameManager.Actions.IsGroupActive(dive.groupId));
            Assert.AreEqual(ActState.Neutral, GameManager.Data.GetFlags(_owner).ActState);
            _input.SetOverrideInput(new MovementInfo { moveDirection = Vector2.right });
            yield return FixedTick();
            Assert.Greater(_player.Rigidbody.linearVelocity.x, 0f);
        }

        [UnityTest]
        public IEnumerator PlayerAction_DiveInterruptedByReaction_ClearsPhysicsWithoutLaterLanding()
        {
            MoveToAir();
            _input.SetOverrideInput(new MovementInfo { attackInput = AttackInputType.AerialHeavy });
            yield return FixedTick();
            Assert.IsTrue(GameManager.Actions.Execution.TryGetInstance(CurrentAttack(), out ActionExecution dive));

            GameManager.Actions.React(_owner, HitReaction.Flinch);

            Assert.IsFalse(GameManager.Actions.IsGroupActive(dive.groupId));
            Assert.AreEqual(GameConstants.k_GravityScale, _player.Rigidbody.gravityScale);
            Assert.AreEqual(ActState.Flinch, GameManager.Data.GetFlags(_owner).ActState);
            GameManager.Actions.Execution.Tick(HitReactionLogic.k_FlinchDuration + 0.01f);
            _player.SetPhysicsPosition(new Vector2(0f, 0.5f));
            Physics2D.SyncTransforms();
            yield return FixedTick();
            Assert.AreEqual(ActState.Neutral, GameManager.Data.GetFlags(_owner).ActState);
            Assert.IsFalse(GameManager.Actions.IsPrimaryExecuting(_owner));
        }

        [UnityTest]
        public IEnumerator PlayerAction_DisablePoolAndDeath_EndSharedLifetimeBeforeReuse()
        {
            MoveToAir();
            _input.SetOverrideInput(new MovementInfo { attackInput = AttackInputType.AerialHeavy });
            yield return FixedTick();
            Assert.IsTrue(GameManager.Actions.Execution.TryGetInstance(CurrentAttack(), out ActionExecution first));

            _player.enabled = false;
            Assert.IsFalse(GameManager.Actions.IsGroupActive(first.groupId));
            Assert.AreEqual(GameConstants.k_GravityScale, _player.Rigidbody.gravityScale);
            _player.enabled = true;
            _input.SetOverrideInput(new MovementInfo { attackInput = AttackInputType.AerialHeavy });
            yield return FixedTick();
            Assert.IsTrue(GameManager.Actions.Execution.TryGetInstance(CurrentAttack(), out ActionExecution pooled));
            Assert.AreNotEqual(first.groupId, pooled.groupId);

            _player.OnPoolReturn();
            Assert.IsFalse(GameManager.Actions.IsGroupActive(pooled.groupId));
            Assert.AreEqual(GameConstants.k_GravityScale, _player.Rigidbody.gravityScale);
            _player.OnPoolAcquire();
            _input.SetOverrideInput(new MovementInfo { attackInput = AttackInputType.AerialHeavy });
            yield return FixedTick();
            Assert.IsTrue(GameManager.Actions.Execution.TryGetInstance(CurrentAttack(), out ActionExecution dying));
            GameManager.Data.GetVitals(_owner).currentHp = 0;
            GameManager.Actions.Execution.Tick(0f);
            Assert.IsFalse(GameManager.Actions.IsGroupActive(dying.groupId));
            Assert.AreEqual(GameConstants.k_GravityScale, _player.Rigidbody.gravityScale);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_player != null) { Object.Destroy(_player.gameObject); }
            yield return null;
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null) { Object.Destroy(_created[i]); }
            }
            _created.Clear();
            yield return null;
            TestSceneHelper.Cleanup();
            yield return null;
        }
    }
}
