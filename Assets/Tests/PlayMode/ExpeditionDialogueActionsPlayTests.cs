using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests.PlayMode
{
    public sealed class ExpeditionDialogueActionsPlayTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();
        private ExpeditionSquadRules _rules;
        private Type _bridge;
        private MethodInfo _runLua;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            _bridge = FindType("Game.Runtime.ExpeditionDialogueActions");
            Type lua = FindType("PixelCrushers.DialogueSystem.Lua");
            _runLua = lua.GetMethod("Run", new[] { typeof(string) });
            Assert.That(_runLua, Is.Not.Null);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (int i = _objects.Count - 1; i >= 0; i--)
            {
                Object.Destroy(_objects[i]);
            }
            _objects.Clear();
            Object.Destroy(_rules);
            TestSceneHelper.Cleanup();
            yield return null;
        }

        [UnityTest]
        public IEnumerator DialogueLua_WhenBindingsOverlap_RoutesToTheNewestAndRestoresTheOlder()
        {
            Behaviour first = BindActions("First story scene", out UnityEvent<bool> firstResult);
            int firstRequests = 0;
            firstResult.AddListener(success => { Assert.That(success, Is.False); firstRequests++; });
            Assert.That(LuaBool("return ExpeditionTryDeployConfirmed()"), Is.False);
            Assert.That(firstRequests, Is.EqualTo(1));

            Behaviour second = BindActions("Second story scene", out UnityEvent<bool> secondResult);
            int secondRequests = 0;
            secondResult.AddListener(success => { Assert.That(success, Is.False); secondRequests++; });
            Assert.That(LuaBool("return ExpeditionTryDeployConfirmed()"), Is.False);
            Assert.That(firstRequests, Is.EqualTo(1));
            Assert.That(secondRequests, Is.EqualTo(1));

            first.enabled = false;
            Assert.That(LuaBool("return ExpeditionTryDeployConfirmed()"), Is.False);
            Assert.That(secondRequests, Is.EqualTo(2));
            first.enabled = true;
            Assert.That(LuaBool("return ExpeditionTryDeployConfirmed()"), Is.False);
            Assert.That(firstRequests, Is.EqualTo(2));

            first.enabled = false;
            Assert.That(LuaBool("return ExpeditionTryDeployConfirmed()"), Is.False);
            Assert.That(secondRequests, Is.EqualTo(3));
            second.enabled = false;
            Assert.That(LuaBool("return ExpeditionTryDeployConfirmed == nil"), Is.True);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DialogueLua_WhenDeploymentIsUnavailable_UsesCanonicalUnlocksAndLeavesConfirmedSquadUntouched()
        {
            ExpeditionSquad squad = CreateSquad();
            BindActions("Story actions", out UnityEvent<bool> result);
            List<bool> attempts = new List<bool>();
            result.AddListener(attempts.Add);
            Assert.That(LuaBool("return ExpeditionIsAidUnlocked('scout')"), Is.False);
            Assert.That(LuaBool("return ExpeditionHasConfirmedSquad()"), Is.False);
            Assert.That(LuaBool("return ExpeditionOpenSquadPanel()"), Is.False);

            Assert.That(GameManager.ExpeditionAid.TryComplete("scout.helped", new[]
            {
                new ExpeditionAidUnlocks.Completion("scout.helped", "scout")
            }), Is.True);
            Assert.That(LuaBool("return ExpeditionIsAidUnlocked('scout')"), Is.True);
            Assert.That(LuaBool("return ExpeditionIsAidUnlocked('unknown')"), Is.False);
            Assert.That(LuaBool("return ExpeditionHasConfirmedSquad()"), Is.False);
            Assert.That(squad.TryConfirm(new[] { new ExpeditionSquad.Selection("scout", 2) }), Is.True);

            IReadOnlyList<ExpeditionSquad.Selection> confirmed = squad.Confirmed;
            Assert.That(LuaBool("return ExpeditionHasConfirmedSquad()"), Is.True);
            Assert.That(LuaBool("return ExpeditionTryDeployConfirmed()"), Is.False);
            CollectionAssert.AreEqual(new[] { false }, attempts);
            Assert.That(squad.Confirmed, Is.SameAs(confirmed));
            Assert.That(squad.ConfirmedCount, Is.EqualTo(2));
            Assert.That(GameManager.Data.Count, Is.Zero);
            yield return null;
        }

        private ExpeditionSquad CreateSquad()
        {
            _rules = ScriptableObject.CreateInstance<ExpeditionSquadRules>();
            SetField(_rules, "maxCount", 2);
            SetField(_rules, "maxCost", 2);
            SetField(_rules, "species", new[] { new ExpeditionSquad.Species("scout", 1) });

            GameObject world = new GameObject("Dialogue test game manager");
            world.SetActive(false);
            ExpeditionSquadManager manager = world.AddComponent<ExpeditionSquadManager>();
            SetField(manager, "rules", _rules);
            world.AddComponent<GameManager>();
            world.SetActive(true);
            GameManager.Instance.enabled = false;
            Assert.That(manager.Squad, Is.Not.Null);
            return manager.Squad;
        }

        private Behaviour BindActions(string name, out UnityEvent<bool> result)
        {
            GameObject root = new GameObject(name);
            _objects.Add(root);
            Behaviour binding = (Behaviour)root.AddComponent(_bridge);
            FieldInfo field = _bridge.GetField("onDeploymentResult", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            result = (UnityEvent<bool>)field.GetValue(binding);
            Assert.That(result, Is.Not.Null);
            return binding;
        }

        private bool LuaBool(string expression)
        {
            object result = _runLua.Invoke(null, new object[] { expression });
            Assert.That(result, Is.Not.Null);
            Type resultType = result.GetType();
            Assert.That(resultType.GetProperty("hasReturnValue").GetValue(result), Is.EqualTo(true));
            return (bool)resultType.GetProperty("asBool").GetValue(result);
        }

        private static Type FindType(string name)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(name);
                if (type != null) { return type; }
            }
            throw new InvalidOperationException($"Integration type {name} is not loaded.");
        }

        private static void SetField(Object instance, string name, object value)
        {
            FieldInfo field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(instance, value);
        }
    }
}
