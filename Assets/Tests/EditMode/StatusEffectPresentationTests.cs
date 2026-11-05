using System.Reflection;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Tests.EditMode
{
    public class StatusEffectPresentationTests
    {
        private SoACharaDataDic _data;
        private StatusEffectStore _store;
        private GameObject _actor;
        private GameObject _prefab;
        private StatusEffectCatalog _catalog;
        private StatusEffectPresentationController _presentation;

        [SetUp]
        public void SetUp()
        {
            _data = new SoACharaDataDic(4);
            _store = new StatusEffectStore();
            _actor = new GameObject("Actor");
            PresentationTestCharacter character = _actor.AddComponent<PresentationTestCharacter>();
            _data.Add(1, new CharacterVitals { currentHp = 100, maxHp = 100 }, default, default, default,
                managed: character);
            _prefab = new GameObject("StatusLoop");
            _prefab.SetActive(false);
            _catalog = ScriptableObject.CreateInstance<StatusEffectCatalog>();
            typeof(StatusEffectCatalog).GetField("_entries", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(_catalog, new[]
                {
                    new StatusEffectPresentation
                    {
                        id = StatusEffectId.Poison,
                        displayName = "毒",
                        tint = Color.green,
                        loopPrefab = _prefab,
                        localOffset = new Vector3(0f, 2f, 0f)
                    }
                });
            _presentation = new StatusEffectPresentationController(_data, _store, _catalog);
        }

        [TearDown]
        public void TearDown()
        {
            _presentation.Dispose();
            Object.DestroyImmediate(_actor);
            Object.DestroyImmediate(_prefab);
            Object.DestroyImmediate(_catalog);
            _data.Dispose();
        }

        [Test]
        public void StatusIcons_SharedStateChanges_ShowTimeStackAndClearRemovedEffects()
        {
            StatusEffectManager manager = new StatusEffectManager(_store, 1);
            StatusEffectInfo info = new StatusEffectInfo
            {
                effect = StatusEffectId.Poison, duration = 10f, maxStack = 3
            };
            manager.Apply(info);
            manager.Apply(info);
            VisualElement root = new VisualElement();
            StatusEffectIconStrip strip = new StatusEffectIconStrip(root);
            strip.Sync(_store, 1, _catalog);

            Assert.AreEqual(1, root.childCount);
            Assert.AreEqual("毒", root[0].tooltip);
            Assert.AreEqual("10", root.Q<Label>("effect-time").text);
            Assert.AreEqual("2", root.Q<Label>("effect-stack").text);

            manager.Apply(new StatusEffectInfo { effect = StatusEffectId.Poison, duration = 4f }, 2);
            strip.Sync(_store, 1, _catalog);
            Assert.AreEqual(2, root.childCount);

            manager.Tick(3f);
            strip.Sync(_store, 1, _catalog);
            Assert.AreEqual("7", root.Q<Label>("effect-time").text);
            Assert.AreEqual("1", root[1].Q<Label>("effect-time").text);

            manager.Tick(8f);
            strip.Sync(_store, 1, _catalog);
            Assert.AreEqual(0, root.childCount);
        }

        [Test]
        public void StatusIcons_NoCatalog_UseReadablePlaceholderAndClearOnTargetRemoval()
        {
            StatusEffectManager manager = new StatusEffectManager(_store, 1);
            manager.Apply(new StatusEffectInfo { effect = StatusEffectId.Poison, duration = 5f });
            new StatusEffectManager(_store, 2).Apply(
                new StatusEffectInfo { effect = StatusEffectId.Burn, duration = 10f });
            VisualElement root = new VisualElement();
            StatusEffectIconStrip strip = new StatusEffectIconStrip(root);
            strip.Sync(_store, 1, null);

            Assert.AreEqual(1, root.childCount);
            Assert.AreEqual("PSN", root.Q<Label>("effect-short-name").text);
            Assert.IsNotEmpty(root[0].tooltip);
            strip.Clear();
            Assert.AreEqual(0, root.childCount);
        }

        [Test]
        public void StatusFx_RepeatedSync_CreatesOneLoopAndRemovesItAtExpiry()
        {
            StatusEffectManager manager = new StatusEffectManager(_store, 1);
            manager.Apply(new StatusEffectInfo { effect = StatusEffectId.Poison, duration = 2f }, 10);
            _presentation.Sync();
            _presentation.Sync();

            Assert.AreEqual(1, _actor.transform.childCount);
            Assert.AreEqual(new Vector3(0f, 2f, 0f), _actor.transform.GetChild(0).localPosition);
            Assert.IsTrue(_actor.transform.GetChild(0).gameObject.activeSelf);

            manager.Apply(new StatusEffectInfo { effect = StatusEffectId.Poison, duration = 5f }, 20);
            _presentation.Sync();
            Assert.AreEqual(2, _actor.transform.childCount);
            manager.Tick(3f);
            _presentation.Sync();
            Assert.AreEqual(1, _actor.transform.childCount);
            manager.Tick(3f);
            _presentation.Sync();
            Assert.AreEqual(0, _actor.transform.childCount);
        }

        [Test]
        public void StatusFx_DeathOrUnregister_RemovesExistingLoop()
        {
            StatusEffectManager manager = new StatusEffectManager(_store, 1);
            manager.Apply(new StatusEffectInfo { effect = StatusEffectId.Poison, duration = 5f });
            _presentation.Sync();
            _data.GetVitals(1).currentHp = 0;
            _presentation.Sync();
            Assert.AreEqual(0, _actor.transform.childCount);

            _data.GetVitals(1).currentHp = 100;
            _presentation.Sync();
            Assert.AreEqual(1, _actor.transform.childCount);
            _data.Remove(1);
            _presentation.Sync();
            Assert.AreEqual(0, _actor.transform.childCount);
        }

        [Test]
        public void StatusFx_Dispose_RemovesAllLoopObjects()
        {
            new StatusEffectManager(_store, 1).Apply(
                new StatusEffectInfo { effect = StatusEffectId.Poison, duration = 5f });
            _presentation.Sync();
            _presentation.Dispose();
            Assert.AreEqual(0, _actor.transform.childCount);
        }
    }

    public class PresentationTestCharacter : ManagedCharacter
    {
        public override int ObjectHash => 1;
        public override IDamageable Damageable => null;
    }
}
