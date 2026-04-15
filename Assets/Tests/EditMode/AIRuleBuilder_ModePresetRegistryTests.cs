using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// ModePresetRegistry の単体テスト。
    /// GUIDベースの保存/更新/削除/カスケード通知を検証する。
    /// </summary>
    [TestFixture]
    public class AIRuleBuilder_ModePresetRegistryTests
    {
        [Test]
        public void ModePresetRegistry_Save_AssignsGuidAndStoresEntry()
        {
            ModePresetRegistry registry = new ModePresetRegistry();
            AIMode mode = new AIMode { modeName = "Combat" };

            string modeId = registry.Save(mode);

            Assert.IsNotNull(modeId);
            Assert.IsNotEmpty(modeId);
            Assert.AreEqual(1, registry.Count);
        }

        [Test]
        public void ModePresetRegistry_Save_WritesIdBackOnStoredEntry()
        {
            ModePresetRegistry registry = new ModePresetRegistry();
            AIMode mode = new AIMode { modeName = "Combat" };

            string modeId = registry.Save(mode);
            AIMode? stored = registry.GetById(modeId);

            Assert.IsNotNull(stored);
            Assert.AreEqual(modeId, stored.Value.modeId);
        }

        [Test]
        public void ModePresetRegistry_Save_GeneratesUniqueIds()
        {
            ModePresetRegistry registry = new ModePresetRegistry();

            string idA = registry.Save(new AIMode { modeName = "A" });
            string idB = registry.Save(new AIMode { modeName = "B" });

            Assert.AreNotEqual(idA, idB);
            Assert.AreEqual(2, registry.Count);
        }

        [Test]
        public void ModePresetRegistry_UpdateById_ExistingId_ReturnsTrueAndReplacesEntry()
        {
            ModePresetRegistry registry = new ModePresetRegistry();
            string modeId = registry.Save(new AIMode { modeName = "Old" });

            bool result = registry.UpdateById(modeId, new AIMode { modeName = "New" });
            AIMode? stored = registry.GetById(modeId);

            Assert.IsTrue(result);
            Assert.IsNotNull(stored);
            Assert.AreEqual("New", stored.Value.modeName);
            Assert.AreEqual(modeId, stored.Value.modeId);
        }

        [Test]
        public void ModePresetRegistry_UpdateById_UnknownId_ReturnsFalse()
        {
            ModePresetRegistry registry = new ModePresetRegistry();

            bool result = registry.UpdateById("nonexistent", new AIMode { modeName = "X" });

            Assert.IsFalse(result);
            Assert.AreEqual(0, registry.Count);
        }

        [Test]
        public void ModePresetRegistry_UpdateById_FiresOnModeUpdated()
        {
            ModePresetRegistry registry = new ModePresetRegistry();
            string modeId = registry.Save(new AIMode { modeName = "Old" });

            string firedId = null;
            AIMode firedMode = default;
            registry.OnModeUpdated += (id, mode) =>
            {
                firedId = id;
                firedMode = mode;
            };

            registry.UpdateById(modeId, new AIMode { modeName = "New" });

            Assert.AreEqual(modeId, firedId);
            Assert.AreEqual("New", firedMode.modeName);
        }

        [Test]
        public void ModePresetRegistry_GetById_UnknownId_ReturnsNull()
        {
            ModePresetRegistry registry = new ModePresetRegistry();

            AIMode? result = registry.GetById("nonexistent");

            Assert.IsNull(result);
        }

        [Test]
        public void ModePresetRegistry_GetById_NullOrEmpty_ReturnsNull()
        {
            ModePresetRegistry registry = new ModePresetRegistry();

            AIMode? resultNull = registry.GetById(null);
            AIMode? resultEmpty = registry.GetById("");

            Assert.IsNull(resultNull);
            Assert.IsNull(resultEmpty);
        }

        [Test]
        public void ModePresetRegistry_Delete_ExistingId_RemovesEntry()
        {
            ModePresetRegistry registry = new ModePresetRegistry();
            string modeId = registry.Save(new AIMode { modeName = "ToDelete" });

            bool result = registry.Delete(modeId);

            Assert.IsTrue(result);
            Assert.AreEqual(0, registry.Count);
            Assert.IsNull(registry.GetById(modeId));
        }

        [Test]
        public void ModePresetRegistry_Delete_UnknownId_ReturnsFalse()
        {
            ModePresetRegistry registry = new ModePresetRegistry();

            bool result = registry.Delete("nonexistent");

            Assert.IsFalse(result);
        }

        [Test]
        public void ModePresetRegistry_GetAll_ReturnsAllStoredEntries()
        {
            ModePresetRegistry registry = new ModePresetRegistry();
            registry.Save(new AIMode { modeName = "A" });
            registry.Save(new AIMode { modeName = "B" });
            registry.Save(new AIMode { modeName = "C" });

            AIMode[] all = registry.GetAll();

            Assert.AreEqual(3, all.Length);
        }

        [Test]
        public void ModePresetRegistry_Save_ExceedsMaxPresets_ReturnsNull()
        {
            ModePresetRegistry registry = new ModePresetRegistry();
            for (int i = 0; i < 40; i++)
            {
                registry.Save(new AIMode { modeName = "Mode" + i });
            }

            string overflowId = registry.Save(new AIMode { modeName = "Overflow" });

            Assert.IsNull(overflowId);
            Assert.AreEqual(40, registry.Count);
        }
    }
}
