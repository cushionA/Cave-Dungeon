using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Game.Tests.PlayMode
{
    public class PlayerInputSamplingTests : InputTestFixture
    {
        private GameObject _actor;
        private InputActionAsset _actions;
        private PlayerInputHandler _input;
        private Gamepad _gamepad;
        private float _startTime;

        [SetUp]
        public void CreateInputActor()
        {
            _gamepad = InputSystem.AddDevice<Gamepad>();
            _actions = ScriptableObject.CreateInstance<InputActionAsset>();
            InputActionMap map = new InputActionMap("Gameplay");
            map.AddAction("Move", InputActionType.Value, "<Gamepad>/leftStick");
            map.AddAction("Jump", InputActionType.Button, "<Gamepad>/buttonWest");
            map.AddAction("Attack", InputActionType.Button, "<Gamepad>/buttonSouth");
            map.AddAction("HeavyAttack", InputActionType.Button, "<Gamepad>/buttonNorth");
            map.AddAction("Sprint", InputActionType.Button, "<Gamepad>/buttonEast");
            _actions.AddActionMap(map);
            _actor = new GameObject("InputSamplingActor");
            _actor.SetActive(false);
            PlayerInput playerInput = _actor.AddComponent<PlayerInput>();
            playerInput.actions = _actions;
            playerInput.defaultActionMap = "Gameplay";
            playerInput.notificationBehavior = PlayerNotifications.SendMessages;
            _input = _actor.AddComponent<PlayerInputHandler>();
            _actor.SetActive(true);
            _startTime = Time.time;
        }

        [TearDown]
        public void DestroyInputActor()
        {
            Object.DestroyImmediate(_actor);
            Object.DestroyImmediate(_actions);
        }

        [Test]
        public void InputSampling_CallbackBeforeMonoUpdate_IsVisibleInTheSameSimulationTick()
        {
            Set(_gamepad.leftStick, Vector2.right);

            _input.PrepareSimulationInput(_startTime);

            Assert.AreEqual(Vector2.right, _input.CurrentInput.moveDirection);
        }

        [Test]
        public void InputSampling_JumpRejectedThenAccepted_PreservesBufferUntilConsumedAndKeepsHold()
        {
            Press(_gamepad.buttonWest);
            _input.PrepareSimulationInput(_startTime + 0.01f);
            Assert.IsTrue(_input.CurrentInput.jumpPressed);
            Assert.IsTrue(_input.CurrentInput.jumpHeld);
            _input.PrepareSimulationInput(_startTime + 0.02f);
            Assert.IsTrue(_input.CurrentInput.jumpPressed);

            _input.ConsumeJumpInput();
            _input.PrepareSimulationInput(_startTime + 0.03f);

            Assert.IsFalse(_input.CurrentInput.jumpPressed);
            Assert.IsTrue(_input.CurrentInput.jumpHeld);
        }

        [Test]
        public void InputSampling_ConsumeOverriddenJump_ClearsOnlyTheEdge()
        {
            _input.SetOverrideInput(new MovementInfo
            {
                jumpPressed = true, jumpHeld = true, moveDirection = Vector2.right,
            });

            _input.ConsumeJumpInput();
            _input.PrepareSimulationInput(_startTime);

            Assert.IsFalse(_input.CurrentInput.jumpPressed);
            Assert.IsTrue(_input.CurrentInput.jumpHeld);
            Assert.AreEqual(Vector2.right, _input.CurrentInput.moveDirection);
        }

        [Test]
        public void InputSampling_DisabledHandlerReceivesCallbacks_DoesNotReplayThemWhenEnabled()
        {
            _input.enabled = false;
            Set(_gamepad.leftStick, Vector2.right);
            Press(_gamepad.buttonWest);
            _input.enabled = true;

            _input.PrepareSimulationInput(_startTime);

            Assert.AreEqual(Vector2.zero, _input.CurrentInput.moveDirection);
            Assert.IsFalse(_input.CurrentInput.jumpPressed);
            Assert.IsFalse(_input.CurrentInput.jumpHeld);
        }

        [Test]
        public void InputSampling_AcceptedAttack_IsNotPublishedAgainAcrossRepeatedOrOlderFixedSamples()
        {
            Press(_gamepad.buttonSouth);
            _input.PrepareSimulationInput(_startTime + 0.02f);
            Assert.IsNotNull(_input.CurrentInput.attackInput);
            _input.ConsumeAttackInput();

            _input.PrepareSimulationInput(_startTime + 0.02f);
            _input.PrepareSimulationInput(_startTime + 0.01f);
            _input.PrepareSimulationInput(_startTime + 0.04f);

            Assert.IsNull(_input.CurrentInput.attackInput);
        }

        [Test]
        public void InputSampling_HeavyChargeAcrossUpdateAndFixedSamples_UsesOneElapsedDuration()
        {
            Press(_gamepad.buttonNorth);
            _input.PrepareSimulationInput(_startTime + 0.8f);
            _input.PrepareSimulationInput(_startTime + 0.8f);
            _input.PrepareSimulationInput(_startTime + 0.6f);
            Release(_gamepad.buttonNorth);
            _input.PrepareSimulationInput(_startTime + 0.8f);

            Assert.IsNotNull(_input.CurrentInput.attackInput);
            Assert.AreEqual(ChargeAttackLogic.k_ChargeLevel1Multiplier, _input.CurrentInput.chargeMultiplier);
            _input.ConsumeAttackInput();
            _input.PrepareSimulationInput(_startTime + 0.8f);
            Assert.IsNull(_input.CurrentInput.attackInput);
        }

        [Test]
        public void InputSampling_RejectedSprintTap_RemainsBufferedUntilAcceptedThenClears()
        {
            Press(_gamepad.buttonEast);
            Release(_gamepad.buttonEast);
            _input.PrepareSimulationInput(_startTime + 0.01f);
            Assert.IsTrue(_input.CurrentInput.dodgePressed);
            _input.PrepareSimulationInput(_startTime + 0.02f);
            Assert.IsTrue(_input.CurrentInput.dodgePressed);

            _input.ConsumeDodgeInput();
            _input.PrepareSimulationInput(_startTime + 0.03f);

            Assert.IsFalse(_input.CurrentInput.dodgePressed);
        }

        [Test]
        public void InputSampling_PressAndReleaseInOneInputUpdate_PreservesTheTap()
        {
            InputSystem.QueueStateEvent(_gamepad, new GamepadState().WithButton(GamepadButton.South));
            InputSystem.QueueStateEvent(_gamepad, new GamepadState());
            InputSystem.Update();

            _input.PrepareSimulationInput(_startTime);

            Assert.IsNotNull(_input.CurrentInput.attackInput);
        }

        [Test]
        public void InputSampling_FixedInputMode_PublishesWithoutReadingActionsInTheSimulationEntry()
        {
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInFixedUpdate;
            InputSystem.QueueStateEvent(_gamepad, new GamepadState().WithButton(GamepadButton.South));
            InputSystem.Update(InputUpdateType.Fixed);

            _input.PrepareSimulationInput(_startTime + 0.02f);
            _input.PrepareSimulationInput(_startTime + 0.02f);

            Assert.IsNotNull(_input.CurrentInput.attackInput);
        }
    }
}
