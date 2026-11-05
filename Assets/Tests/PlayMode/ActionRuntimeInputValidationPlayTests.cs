using System.Collections;
using System.Collections.Generic;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests.PlayMode
{
    public class ActionRuntimeInputValidationPlayTests
    {
        private GameObject _actor;
        private readonly List<ScriptableObject> _assets = new List<ScriptableObject>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            TestSceneHelper.CreateGameManager();
            GameManager.Instance.enabled = false;
            Game.Core.CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(maxHp: 1000);
            _assets.Add(info);
            _actor = TestSceneHelper.CreateBaseCharacterObject(info, Vector3.zero);
            _actor.GetComponent<BaseCharacter>().Rigidbody.gravityScale = 0f;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_actor != null) { Object.Destroy(_actor); }
            yield return null;
            for (int i = _assets.Count - 1; i >= 0; i--)
            {
                if (_assets[i] != null) { Object.Destroy(_assets[i]); }
            }
            _assets.Clear();
            TestSceneHelper.Cleanup();
            yield return null;
        }

        private ActionDefinition Action()
        {
            ActionDefinition action = ScriptableObject.CreateInstance<ActionDefinition>();
            _assets.Add(action);
            action.endMode = ActionEndMode.Manual;
            action.cancelAfter = 0f;
            return action;
        }

        [UnityTest]
        public IEnumerator ActionRuntime_InvalidLocomotion_PreservesAcceptedMovementAndGroup()
        {
            int owner = _actor.GetHashCode();
            Assert.IsTrue(GameManager.Actions.TrySetLocomotion(owner, Vector2.right, 3f, out long instance));
            Assert.IsTrue(GameManager.Actions.Execution.TryGetGroupId(instance, out long group));

            Assert.IsFalse(GameManager.Actions.TrySetLocomotion(owner, new Vector2(float.NaN, 0f), 4f, out _));
            Assert.IsFalse(GameManager.Actions.TrySetLocomotion(owner, Vector2.left, float.PositiveInfinity, out _));
            Assert.IsFalse(GameManager.Actions.TrySetLocomotion(owner, Vector2.left, -1f, out _));
            Assert.IsFalse(GameManager.Actions.TrySetLocomotion(owner, Vector2.left, 4f, out _, float.NaN));

            Assert.IsTrue(GameManager.Actions.Execution.IsGroupActive(group));
            Assert.IsTrue(GameManager.Actions.Execution.TryGetMovement(owner, true, out Vector2 velocity));
            Assert.AreEqual(Vector2.right * 3f, velocity);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ActionRuntime_InvalidMovementOverride_PreservesAcceptedOutput()
        {
            BaseCharacter actor = _actor.GetComponent<BaseCharacter>();
            ActionDefinition action = Action();
            action.resources = ActionResources.HorizontalMovement;
            action.movement = new ActionMovementProfile { speed = 2f };
            Assert.IsTrue(GameManager.Actions.Execute(actor.ObjectHash, action, 0, Vector2.right, out long instance));
            Assert.IsTrue(GameManager.Actions.SetMovementOutput(actor.ObjectHash, instance, new Vector2(4f, 0f)));

            Assert.IsFalse(GameManager.Actions.SetMovementOutput(actor.ObjectHash, instance,
                new Vector2(float.NaN, 0f)));
            Assert.IsFalse(GameManager.Actions.SetMovementOutput(actor.ObjectHash, instance,
                new Vector2(0f, float.NegativeInfinity)));
            actor.SetPhysicsVelocity(Vector2.zero);
            GameManager.Actions.ApplyMovement(actor.ObjectHash, actor);

            Assert.AreEqual(4f, actor.PhysicsVelocity.x);
            Assert.AreEqual(0f, actor.PhysicsVelocity.y);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ActionRuntime_InvalidDirection_DoesNotRegisterDefinitionOrConsumeResources()
        {
            int owner = _actor.GetHashCode();
            ActionDefinition action = Action();
            action.staminaCost = 9f;
            action.mpCost = 3;
            int definitions = GameManager.Actions.Execution.DefinitionCount;
            float stamina = GameManager.Data.GetVitals(owner).currentStamina;
            float mp = GameManager.Data.GetVitals(owner).currentMp;

            Assert.IsFalse(GameManager.Actions.Execute(owner, action, 0,
                new Vector2(float.PositiveInfinity, 0f), out long instance));

            Assert.AreEqual(0, instance);
            Assert.AreEqual(definitions, GameManager.Actions.Execution.DefinitionCount);
            Assert.AreEqual(stamina, GameManager.Data.GetVitals(owner).currentStamina);
            Assert.AreEqual(mp, GameManager.Data.GetVitals(owner).currentMp);
            yield return null;
        }
    }
}
