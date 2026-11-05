using System;
using System.Collections;
using System.Collections.Generic;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests.PlayMode
{
    public sealed class GameLoopProbeCharacter : BaseCharacter
    {
        public static readonly List<string> Calls = new List<string>();
        public Action Preparing;

        public override void PrepareAIDecisionTick(float deltaTime, float currentTime, AIDecisionSystem decisions)
        {
            Calls.Add("prepare:" + name);
            Preparing?.Invoke();
        }

        public override void SimulateCharacterTick(float deltaTime, float currentTime)
        {
            Calls.Add("apply:" + name);
        }
    }

    public class CharacterGameLoopTests
    {
        private sealed class ForeignPhase { }

        private readonly List<GameObject> _objects = new List<GameObject>();
        private readonly List<Object> _assets = new List<Object>();
        private GameManager _manager;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            GameObject managerObject = new GameObject("CharacterGameLoopManager");
            _manager = managerObject.AddComponent<GameManager>();
            _manager.enabled = false;
            _objects.Add(managerObject);
            GameLoopProbeCharacter.Calls.Clear();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (int i = _objects.Count - 1; i >= 0; i--)
            {
                if (_objects[i] != null) { Object.Destroy(_objects[i]); }
            }
            _objects.Clear();
            yield return null;
            for (int i = 0; i < _assets.Count; i++)
            {
                if (_assets[i] != null) { Object.Destroy(_assets[i]); }
            }
            _assets.Clear();
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            RemoveForeignPhase(ref loop);
            PlayerLoop.SetPlayerLoop(loop);
            GameLoopProbeCharacter.Calls.Clear();
            TestSceneHelper.Cleanup();
            yield return null;
        }

        private GameLoopProbeCharacter CreateCharacter(string characterName)
        {
            Game.Core.CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo();
            _assets.Add(info);
            GameObject actor = new GameObject(characterName);
            actor.SetActive(false);
            GameLoopProbeCharacter character = actor.AddComponent<GameLoopProbeCharacter>();
            character.SetCharacterInfoForTest(info);
            _objects.Add(actor);
            actor.SetActive(true);
            character.Rigidbody.gravityScale = 0f;
            return character;
        }

        [UnityTest]
        public IEnumerator GameLoop_FixedTick_PreparesEveryCharacterBeforeApplyingAny()
        {
            CreateCharacter("first");
            CreateCharacter("second");
            yield return null;

            _manager.Loop.TickFixed(0.02f, 1f);

            Assert.AreEqual(4, GameLoopProbeCharacter.Calls.Count);
            StringAssert.StartsWith("prepare:", GameLoopProbeCharacter.Calls[0]);
            StringAssert.StartsWith("prepare:", GameLoopProbeCharacter.Calls[1]);
            StringAssert.StartsWith("apply:", GameLoopProbeCharacter.Calls[2]);
            StringAssert.StartsWith("apply:", GameLoopProbeCharacter.Calls[3]);
        }

        [UnityTest]
        public IEnumerator GameLoop_PoolReturnAndReacquireDuringPrepare_SkipsOldRegistrationUntilNextTick()
        {
            GameLoopProbeCharacter first = CreateCharacter("first");
            GameLoopProbeCharacter second = CreateCharacter("second");
            yield return null;
            first.Preparing = () =>
            {
                second.OnPoolReturn();
                second.OnPoolAcquire();
            };

            _manager.Loop.TickFixed(0.02f, 1f);

            CollectionAssert.AreEqual(new[] { "prepare:first", "apply:first" }, GameLoopProbeCharacter.Calls);
            first.Preparing = null;
            GameLoopProbeCharacter.Calls.Clear();
            _manager.Loop.TickFixed(0.02f, 2f);
            Assert.AreEqual(4, GameLoopProbeCharacter.Calls.Count);
            CollectionAssert.Contains(GameLoopProbeCharacter.Calls, "prepare:second");
            CollectionAssert.Contains(GameLoopProbeCharacter.Calls, "apply:second");
        }

        [UnityTest]
        public IEnumerator GameLoop_DisabledOrDestroyedCharacter_SkipsControlAndKeepsFixedRecovery()
        {
            GameLoopProbeCharacter disabled = CreateCharacter("disabled");
            GameLoopProbeCharacter destroyed = CreateCharacter("destroyed");
            DamageReceiver receiver = disabled.gameObject.AddComponent<DamageReceiver>();
            yield return null;
            receiver.SetArmorRecoveryParams(10f, 0f);
            GameManager.Data.GetVitals(disabled.ObjectHash).maxArmor = 10f;
            GameManager.Data.GetVitals(disabled.ObjectHash).currentArmor = 0f;
            disabled.enabled = false;
            Object.DestroyImmediate(destroyed.gameObject);

            _manager.Loop.TickFixed(0.25f, 1f);

            Assert.IsEmpty(GameLoopProbeCharacter.Calls);
            Assert.AreEqual(2.5f, GameManager.Data.GetVitals(disabled.ObjectHash).currentArmor);
            disabled.enabled = true;
            _manager.Loop.TickFixed(0.25f, 2f);
            CollectionAssert.AreEqual(new[] { "prepare:disabled", "apply:disabled" }, GameLoopProbeCharacter.Calls);
            Assert.AreEqual(5f, GameManager.Data.GetVitals(disabled.ObjectHash).currentArmor);
        }

        [Test]
        public void GameLoop_InstalledTwice_HasSinglePhaseBeforePhysics()
        {
            _manager.Loop.Install();
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            PlayerLoopSystem fixedLoop = FindPhase(loop, typeof(UnityEngine.PlayerLoop.FixedUpdate));
            int scriptIndex = IndexOf(fixedLoop, typeof(UnityEngine.PlayerLoop.FixedUpdate.ScriptRunBehaviourFixedUpdate));
            int physicsIndex = IndexOf(fixedLoop, typeof(UnityEngine.PlayerLoop.FixedUpdate.Physics2DFixedUpdate));
            int customIndex = -1;
            int count = 0;
            for (int i = 0; i < fixedLoop.subSystemList.Length; i++)
            {
                Type type = fixedLoop.subSystemList[i].type;
                if (type != null && type.DeclaringType == typeof(GameLoop))
                {
                    customIndex = i;
                    count++;
                }
            }
            Assert.AreEqual(1, count);
            Assert.Greater(customIndex, scriptIndex);
            Assert.Less(customIndex, physicsIndex);
        }

        [UnityTest]
        public IEnumerator GameLoop_ComponentsAddedAfterRegistration_UpdateAndRespectEnabledState()
        {
            GameLoopProbeCharacter character = CreateCharacter("lateComponents");
            yield return null;
            DamageReceiver receiver = character.gameObject.AddComponent<DamageReceiver>();
            CharacterAnimationController animation = character.gameObject.AddComponent<CharacterAnimationController>();
            yield return null;
            receiver.SetArmorRecoveryParams(10f, 0f);
            GameManager.Data.GetVitals(character.ObjectHash).maxArmor = 10f;
            GameManager.Data.GetVitals(character.ObjectHash).currentArmor = 0f;
            animation.Bridge.SetFloat(123, 2f);

            _manager.Loop.TickFixed(0.25f, 1f);
            _manager.Loop.TickFrame();

            Assert.AreEqual(2.5f, GameManager.Data.GetVitals(character.ObjectHash).currentArmor);
            Assert.IsFalse(animation.Bridge.IsDirty);
            receiver.enabled = false;
            animation.enabled = false;
            GameManager.Data.GetVitals(character.ObjectHash).currentArmor = 0f;
            animation.Bridge.SetFloat(123, 3f);
            _manager.Loop.TickFixed(0.25f, 2f);
            _manager.Loop.TickFrame();
            Assert.AreEqual(0f, GameManager.Data.GetVitals(character.ObjectHash).currentArmor);
            Assert.IsTrue(animation.Bridge.IsDirty);
            receiver.enabled = true;
            animation.enabled = true;
            _manager.Loop.TickFixed(0.25f, 3f);
            _manager.Loop.TickFrame();
            Assert.AreEqual(2.5f, GameManager.Data.GetVitals(character.ObjectHash).currentArmor);
            Assert.IsFalse(animation.Bridge.IsDirty);
        }

        [UnityTest]
        public IEnumerator GameLoop_StandaloneAnimationWithoutManager_UpdatesAndRemovesItsPhaseOnDestroy()
        {
            Object.DestroyImmediate(_manager.gameObject);
            GameObject actor = new GameObject("StandaloneAnimation");
            _objects.Add(actor);
            CharacterAnimationController animation = actor.AddComponent<CharacterAnimationController>();
            yield return null;
            animation.Bridge.SetFloat(123, 2f);
            yield return null;
            yield return null;
            Assert.IsFalse(animation.Bridge.IsDirty);
            animation.enabled = false;
            animation.Bridge.SetFloat(123, 3f);
            yield return null;
            Assert.IsTrue(animation.Bridge.IsDirty);
            animation.enabled = true;
            yield return null;
            yield return null;
            Assert.IsFalse(animation.Bridge.IsDirty);

            Object.DestroyImmediate(actor);

            Assert.AreEqual(0, CountGamePhases(PlayerLoop.GetCurrentPlayerLoop()));
        }

        [Test]
        public void GameLoop_Disposed_RemovesOwnPhasesAndPreservesOtherSystems()
        {
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            List<PlayerLoopSystem> children = new List<PlayerLoopSystem>(loop.subSystemList);
            children.Add(new PlayerLoopSystem { type = typeof(ForeignPhase) });
            loop.subSystemList = children.ToArray();
            PlayerLoop.SetPlayerLoop(loop);

            _manager.Loop.Dispose();

            loop = PlayerLoop.GetCurrentPlayerLoop();
            Assert.AreEqual(typeof(ForeignPhase), FindPhase(loop, typeof(ForeignPhase)).type);
            Assert.AreEqual(0, CountGamePhases(loop));
            Object.DestroyImmediate(_manager.gameObject);
            GameObject replacement = new GameObject("ReplacementGameLoopManager");
            _objects.Add(replacement);
            _manager = replacement.AddComponent<GameManager>();
            _manager.enabled = false;
            loop = PlayerLoop.GetCurrentPlayerLoop();
            Assert.AreEqual(3, CountGamePhases(loop));
            Assert.AreEqual(typeof(ForeignPhase), FindPhase(loop, typeof(ForeignPhase)).type);
        }

        private static PlayerLoopSystem FindPhase(PlayerLoopSystem loop, Type type)
        {
            if (loop.type == type) { return loop; }
            if (loop.subSystemList != null)
            {
                for (int i = 0; i < loop.subSystemList.Length; i++)
                {
                    PlayerLoopSystem found = FindPhase(loop.subSystemList[i], type);
                    if (found.type != null) { return found; }
                }
            }
            return default;
        }

        private static int IndexOf(PlayerLoopSystem loop, Type type)
        {
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                if (loop.subSystemList[i].type == type) { return i; }
            }
            return -1;
        }

        private static int CountGamePhases(PlayerLoopSystem loop)
        {
            int count = loop.type != null && loop.type.DeclaringType == typeof(GameLoop) ? 1 : 0;
            if (loop.subSystemList != null)
            {
                for (int i = 0; i < loop.subSystemList.Length; i++)
                {
                    count += CountGamePhases(loop.subSystemList[i]);
                }
            }
            return count;
        }

        private static void RemoveForeignPhase(ref PlayerLoopSystem loop)
        {
            if (loop.subSystemList == null) { return; }
            List<PlayerLoopSystem> children = new List<PlayerLoopSystem>();
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                PlayerLoopSystem child = loop.subSystemList[i];
                if (child.type == typeof(ForeignPhase)) { continue; }
                RemoveForeignPhase(ref child);
                children.Add(child);
            }
            loop.subSystemList = children.ToArray();
        }
    }
}
