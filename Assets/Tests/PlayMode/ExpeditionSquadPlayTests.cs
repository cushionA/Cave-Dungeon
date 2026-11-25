using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.PlayMode
{
    public class ExpeditionSquadPlayTests
    {
        private ExpeditionSquadRules _rules;
        private string _savePath;

        [UnityTest]
        public IEnumerator ExpeditionSquad_AuthoredRulesAndQuestUnlock_ConfirmThroughRuntimeManager()
        {
            TestSceneHelper.Cleanup();
            yield return null;

            _rules = ScriptableObject.CreateInstance<ExpeditionSquadRules>();
            SetSerializedField(_rules, "maxCount", 3);
            SetSerializedField(_rules, "maxCost", 4);
            SetSerializedField(_rules, "species", new[]
            {
                new ExpeditionSquad.Species("scout", 1),
                new ExpeditionSquad.Species("guard", 2)
            });

            ExpeditionSquadManager manager = CreateSession();
            Assert.That(GameManager.GetSubManager<ExpeditionSquadManager>(), Is.SameAs(manager));
            ExpeditionSquad squad = manager.Squad;
            Assert.That(squad, Is.Not.Null);
            Assert.That(squad.GetUnlockedSpecies(), Is.Empty);
            Assert.That(squad.MaxCount, Is.EqualTo(3));
            Assert.That(squad.MaxCost, Is.EqualTo(4));

            ExpeditionAidUnlocks.Completion[] completions =
            {
                new ExpeditionAidUnlocks.Completion("scout.helped", "scout"),
                new ExpeditionAidUnlocks.Completion("guard.helped", "guard")
            };
            Assert.That(GameManager.ExpeditionAid.TryComplete("scout.helped", completions), Is.True);
            Assert.That(GameManager.ExpeditionAid.TryComplete("guard.helped", completions), Is.True);
            CollectionAssert.AreEqual(new[] { "scout", "guard" }, squad.GetUnlockedSpecies().Select(species => species.SpeciesId));

            Assert.That(squad.TryConfirm(new[]
            {
                new ExpeditionSquad.Selection("scout", 2),
                new ExpeditionSquad.Selection("guard", 1)
            }), Is.True);
            Assert.That(squad.ConfirmedCost, Is.EqualTo(4));
            Assert.That(squad.TryConfirm(new[] { new ExpeditionSquad.Selection("guard", 3) }), Is.False);
            Assert.That(squad.Confirmed[0].Count, Is.EqualTo(2));
            Assert.That(squad.ConfirmedCost, Is.EqualTo(4));
        }

        [UnityTest]
        public IEnumerator ExpeditionSquad_DiskRoundTrip_RestoresSpeciesCountsAndClearsMissingLegacyEntry()
        {
            TestSceneHelper.Cleanup();
            yield return null;

            _rules = ScriptableObject.CreateInstance<ExpeditionSquadRules>();
            SetSerializedField(_rules, "maxCount", 3);
            SetSerializedField(_rules, "maxCost", 4);
            SetSerializedField(_rules, "species", new[]
            {
                new ExpeditionSquad.Species("scout", 1),
                new ExpeditionSquad.Species("guard", 2)
            });
            ExpeditionSquadManager initial = CreateSession();
            ExpeditionAidUnlocks.Completion[] completions =
            {
                new ExpeditionAidUnlocks.Completion("scout.helped", "scout"),
                new ExpeditionAidUnlocks.Completion("guard.helped", "guard")
            };
            Assert.That(GameManager.ExpeditionAid.TryComplete("scout.helped", completions), Is.True);
            Assert.That(GameManager.ExpeditionAid.TryComplete("guard.helped", completions), Is.True);
            Assert.That(initial.Squad.TryConfirm(new[]
            {
                new ExpeditionSquad.Selection("guard", 1),
                new ExpeditionSquad.Selection("scout", 2)
            }), Is.True);

            _savePath = Path.Combine(Path.GetTempPath(), "ExpeditionSquad_" + System.Guid.NewGuid().ToString("N"));
            SaveDataStore store = new SaveDataStore(_savePath);
            store.WriteToDisk(GameManager.Saves.Save(0));
            store.WriteToDisk(new SaveSlotData(1));
            Object.Destroy(initial.gameObject);
            yield return null;

            SetSerializedField(_rules, "maxCount", 5);
            SetSerializedField(_rules, "maxCost", 8);
            SetSerializedField(_rules, "species", new[]
            {
                new ExpeditionSquad.Species("scout", 2),
                new ExpeditionSquad.Species("guard", 2)
            });
            ExpeditionSquad restored = CreateSession().Squad;
            Assert.That(GameManager.ExpeditionAid.IsUnlocked("scout"), Is.False);
            Assert.That(restored.Confirmed, Is.Empty);

            GameManager.Saves.SetSlotData(0, store.ReadFromDisk(0));
            Assert.That(GameManager.Saves.Load(0), Is.True);
            Assert.That(GameManager.ExpeditionAid.IsUnlocked("scout"), Is.True);
            Assert.That(GameManager.ExpeditionAid.IsUnlocked("guard"), Is.True);
            Assert.That(restored.Confirmed[0].SpeciesId, Is.EqualTo("guard"));
            Assert.That(restored.Confirmed[1].SpeciesId, Is.EqualTo("scout"));
            Assert.That(restored.Confirmed[1].Count, Is.EqualTo(2));
            Assert.That(restored.ConfirmedCount, Is.EqualTo(3));
            Assert.That(restored.ConfirmedCost, Is.EqualTo(6));
            Assert.That(restored.MaxCount, Is.EqualTo(5));
            Assert.That(restored.MaxCost, Is.EqualTo(8));

            GameManager.Saves.SetSlotData(1, store.ReadFromDisk(1));
            Assert.That(GameManager.Saves.Load(1), Is.True);
            Assert.That(GameManager.ExpeditionAid.IsUnlocked("scout"), Is.False);
            Assert.That(restored.Confirmed, Is.Empty);
            Assert.That(restored.ConfirmedCost, Is.Zero);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Object.Destroy(_rules);
            TestSceneHelper.Cleanup();
            yield return null;
            if (Directory.Exists(_savePath))
            {
                Directory.Delete(_savePath, true);
            }
        }

        private ExpeditionSquadManager CreateSession()
        {
            GameObject world = new GameObject("Expedition Session");
            world.SetActive(false);
            ExpeditionSquadManager manager = world.AddComponent<ExpeditionSquadManager>();
            SetSerializedField(manager, "rules", _rules);
            world.AddComponent<GameManager>();
            world.SetActive(true);
            return manager;
        }

        private static void SetSerializedField(Object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }
    }
}
