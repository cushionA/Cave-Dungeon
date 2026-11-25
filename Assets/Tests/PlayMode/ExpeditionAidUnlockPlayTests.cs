using System;
using System.Collections;
using System.IO;
using System.Reflection;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests.PlayMode
{
    public sealed class ExpeditionAidUnlockPlayTests
    {
        private GameObject _bridgeObject;
        private ScriptableObject _questAction;
        private string _savePath;

        [UnityTest]
        public IEnumerator QuestCompletion_OnlyAuthorMessageUnlocksSpeciesAndSurvivesReload()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            GameObject manager = TestSceneHelper.CreateGameManager();
            _bridgeObject = new GameObject("Quest Machine integration");
            _bridgeObject.SetActive(false);

            // Pixel Crushers and its adapters are in Assembly-CSharp, which test asmdefs cannot reference.
            Type bridgeType = FindIntegrationType("Game.Runtime.QuestMachineBridge");
            Component bridge = _bridgeObject.AddComponent(bridgeType);
            FieldInfo completions = bridgeType.GetField("aidCompletions", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(completions);
            completions.SetValue(bridge, new[]
            {
                new ExpeditionAidUnlocks.Completion("engineer.helped", "engineer"),
                new ExpeditionAidUnlocks.Completion("sentinel.story.resolved", "sentinel")
            });
            _bridgeObject.SetActive(true);

            ActorRef ordinaryEnemy = GameManager.Actors.Register(123);
            GameManager.Events.FireCharacterDeath(ordinaryEnemy, default);
            MethodInfo notify = bridgeType.GetMethod("NotifyCustomEvent");
            Assert.IsNotNull(notify);
            notify.Invoke(bridge, new object[] { "EnemyDefeated", "engineer.helped" });
            notify.Invoke(bridge, new object[] { ExpeditionAidUnlocks.k_CompletionMessage, "unconfigured" });
            Assert.IsFalse(GameManager.ExpeditionAid.IsUnlocked("engineer"));

            Type actionType = FindIntegrationType("PixelCrushers.QuestMachine.MessageQuestAction");
            Type stringField = FindIntegrationType("PixelCrushers.StringField");
            _questAction = ScriptableObject.CreateInstance(actionType);
            actionType.GetProperty("message").SetValue(_questAction,
                Activator.CreateInstance(stringField, ExpeditionAidUnlocks.k_CompletionMessage));
            actionType.GetProperty("parameter").SetValue(_questAction,
                Activator.CreateInstance(stringField, "engineer.helped"));
            MethodInfo execute = actionType.GetMethod("Execute");
            execute.Invoke(_questAction, null);
            execute.Invoke(_questAction, null);
            Assert.IsTrue(GameManager.ExpeditionAid.IsUnlocked("engineer"));
            Assert.IsFalse(GameManager.ExpeditionAid.IsUnlocked("sentinel"));

            _bridgeObject.SetActive(false);
            notify.Invoke(bridge, new object[] { ExpeditionAidUnlocks.k_CompletionMessage, "sentinel.story.resolved" });
            Assert.IsFalse(GameManager.ExpeditionAid.IsUnlocked("sentinel"));
            _bridgeObject.SetActive(true);
            notify.Invoke(bridge, new object[] { ExpeditionAidUnlocks.k_CompletionMessage, "sentinel.story.resolved" });
            Assert.IsTrue(GameManager.ExpeditionAid.IsUnlocked("sentinel"));

            _savePath = Path.Combine(Path.GetTempPath(), "ExpeditionAid_" + Guid.NewGuid().ToString("N"));
            SaveDataStore store = new SaveDataStore(_savePath);
            store.WriteToDisk(GameManager.Saves.Save(0));
            Object.Destroy(manager);
            yield return null;
            TestSceneHelper.CreateGameManager();
            Assert.IsFalse(GameManager.ExpeditionAid.IsUnlocked("engineer"));
            GameManager.Saves.SetSlotData(0, store.ReadFromDisk(0));
            Assert.IsTrue(GameManager.Saves.Load(0));
            Assert.IsTrue(GameManager.ExpeditionAid.IsUnlocked("engineer"));
            Assert.IsTrue(GameManager.ExpeditionAid.IsUnlocked("sentinel"));
        }

        private static Type FindIntegrationType(string typeName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(typeName);
                if (type != null)
                {
                    return type;
                }
            }

            throw new InvalidOperationException($"Integration type {typeName} is not loaded.");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Object.Destroy(_bridgeObject);
            Object.Destroy(_questAction);
            TestSceneHelper.Cleanup();
            yield return null;
            if (Directory.Exists(_savePath))
            {
                Directory.Delete(_savePath, true);
            }
        }
    }
}
