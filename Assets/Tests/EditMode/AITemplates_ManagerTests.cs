using System.Collections.Generic;
using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    [TestFixture]
    public class AITemplates_ManagerTests
    {
        private AITemplateManager _manager;

        [SetUp]
        public void SetUp()
        {
            _manager = new AITemplateManager();
        }

        // ===== SaveTemplate =====

        [Test]
        public void SaveTemplate_WhenValid_ShouldStoreTemplate()
        {
            AITemplateData template = CreateTemplate("t1", "TestTemplate", "User1", AITemplateCategory.General);

            bool result = _manager.SaveTemplate(template);

            Assert.IsTrue(result);
            AITemplateData? loaded = _manager.GetTemplate("t1");
            Assert.IsTrue(loaded.HasValue);
            Assert.AreEqual("TestTemplate", loaded.Value.templateName);
            Assert.AreEqual("User1", loaded.Value.authorName);
        }

        [Test]
        public void SaveTemplate_WhenDuplicateId_ShouldOverwrite()
        {
            AITemplateData original = CreateTemplate("t1", "Original", "User1", AITemplateCategory.General);
            AITemplateData updated = CreateTemplate("t1", "Updated", "User1", AITemplateCategory.BossFight);

            _manager.SaveTemplate(original);
            bool result = _manager.SaveTemplate(updated);

            Assert.IsTrue(result);
            AITemplateData? loaded = _manager.GetTemplate("t1");
            Assert.IsTrue(loaded.HasValue);
            Assert.AreEqual("Updated", loaded.Value.templateName);
            Assert.AreEqual(AITemplateCategory.BossFight, loaded.Value.category);

            // Overwrite should not increase count
            AITemplateData[] all = _manager.GetTemplates();
            Assert.AreEqual(1, all.Length);
        }

        [Test]
        public void SaveTemplate_WhenExceedsMax_ShouldReturnFalse()
        {
            // Fill up to max (30)
            for (int i = 0; i < 30; i++)
            {
                AITemplateData template = CreateTemplate(
                    "t" + i, "Template" + i, "User1", AITemplateCategory.General);
                bool added = _manager.SaveTemplate(template);
                Assert.IsTrue(added, "Failed to add template at index " + i);
            }

            // 31st should fail
            AITemplateData overflow = CreateTemplate("t_overflow", "Overflow", "User1", AITemplateCategory.General);
            bool result = _manager.SaveTemplate(overflow);

            Assert.IsFalse(result);
            Assert.IsNull(_manager.GetTemplate("t_overflow"));
        }

        [Test]
        public void SaveTemplate_WhenExceedsMaxButDuplicate_ShouldOverwrite()
        {
            // Fill to max
            for (int i = 0; i < 30; i++)
            {
                _manager.SaveTemplate(CreateTemplate(
                    "t" + i, "Template" + i, "User1", AITemplateCategory.General));
            }

            // Overwriting an existing one should succeed even at max
            AITemplateData overwrite = CreateTemplate("t0", "OverwrittenAtMax", "User1", AITemplateCategory.BossFight);
            bool result = _manager.SaveTemplate(overwrite);

            Assert.IsTrue(result);
            AITemplateData? loaded = _manager.GetTemplate("t0");
            Assert.AreEqual("OverwrittenAtMax", loaded.Value.templateName);
        }

        // ===== GetTemplates =====

        [Test]
        public void GetTemplates_WithNullCategory_ShouldReturnAll()
        {
            _manager.SaveTemplate(CreateTemplate("t1", "A", "User1", AITemplateCategory.General));
            _manager.SaveTemplate(CreateTemplate("t2", "B", "User1", AITemplateCategory.BossFight));
            _manager.SaveTemplate(CreateTemplate("t3", "C", "User1", AITemplateCategory.Defensive));

            AITemplateData[] all = _manager.GetTemplates(null);

            Assert.AreEqual(3, all.Length);
        }

        [Test]
        public void GetTemplates_WithCategoryFilter_ShouldReturnFilteredResults()
        {
            _manager.SaveTemplate(CreateTemplate("t1", "A", "User1", AITemplateCategory.General));
            _manager.SaveTemplate(CreateTemplate("t2", "B", "User1", AITemplateCategory.BossFight));
            _manager.SaveTemplate(CreateTemplate("t3", "C", "User1", AITemplateCategory.BossFight));
            _manager.SaveTemplate(CreateTemplate("t4", "D", "User1", AITemplateCategory.Defensive));

            AITemplateData[] bossFightTemplates = _manager.GetTemplates(AITemplateCategory.BossFight);

            Assert.AreEqual(2, bossFightTemplates.Length);
            Assert.AreEqual("B", bossFightTemplates[0].templateName);
            Assert.AreEqual("C", bossFightTemplates[1].templateName);
        }

        [Test]
        public void GetTemplates_WhenEmpty_ShouldReturnEmptyArray()
        {
            AITemplateData[] all = _manager.GetTemplates();

            Assert.IsNotNull(all);
            Assert.AreEqual(0, all.Length);
        }

        // ===== GetTemplate =====

        [Test]
        public void GetTemplate_WhenNotFound_ShouldReturnNull()
        {
            AITemplateData? result = _manager.GetTemplate("nonexistent");

            Assert.IsNull(result);
        }

        // ===== DeleteTemplate =====

        [Test]
        public void DeleteTemplate_WhenSystemPreset_ShouldReturnFalse()
        {
            AITemplateData systemTemplate = CreateTemplate("sys1", "SystemPreset", "System", AITemplateCategory.General);
            _manager.SaveTemplate(systemTemplate);

            bool result = _manager.DeleteTemplate("sys1");

            Assert.IsFalse(result);
            // Template should still exist
            Assert.IsTrue(_manager.GetTemplate("sys1").HasValue);
        }

        [Test]
        public void DeleteTemplate_WhenUserTemplate_ShouldRemove()
        {
            AITemplateData userTemplate = CreateTemplate("u1", "UserTemplate", "Player", AITemplateCategory.Custom);
            _manager.SaveTemplate(userTemplate);

            bool result = _manager.DeleteTemplate("u1");

            Assert.IsTrue(result);
            Assert.IsNull(_manager.GetTemplate("u1"));
        }

        [Test]
        public void DeleteTemplate_WhenNotFound_ShouldReturnFalse()
        {
            bool result = _manager.DeleteTemplate("nonexistent");

            Assert.IsFalse(result);
        }

        // ===== ISaveable =====

        [Test]
        public void Serialize_ShouldReturnTemplateList()
        {
            _manager.SaveTemplate(CreateTemplate("t1", "A", "User1", AITemplateCategory.General));
            _manager.SaveTemplate(CreateTemplate("t2", "B", "System", AITemplateCategory.BossFight));

            object serialized = _manager.Serialize();

            Assert.IsInstanceOf<List<AITemplateData>>(serialized);
            List<AITemplateData> list = (List<AITemplateData>)serialized;
            Assert.AreEqual(2, list.Count);
        }

        [Test]
        public void Deserialize_ShouldRestoreTemplates()
        {
            List<AITemplateData> data = new List<AITemplateData>
            {
                CreateTemplate("t1", "Restored1", "User1", AITemplateCategory.General),
                CreateTemplate("t2", "Restored2", "System", AITemplateCategory.BossFight)
            };

            _manager.Deserialize(data);

            AITemplateData[] all = _manager.GetTemplates();
            Assert.AreEqual(2, all.Length);
            Assert.AreEqual("Restored1", _manager.GetTemplate("t1").Value.templateName);
            Assert.AreEqual("Restored2", _manager.GetTemplate("t2").Value.templateName);
        }

        [Test]
        public void Deserialize_ShouldClearExistingData()
        {
            _manager.SaveTemplate(CreateTemplate("old", "OldData", "User1", AITemplateCategory.General));

            List<AITemplateData> newData = new List<AITemplateData>
            {
                CreateTemplate("new", "NewData", "User1", AITemplateCategory.Custom)
            };
            _manager.Deserialize(newData);

            Assert.IsNull(_manager.GetTemplate("old"));
            Assert.IsTrue(_manager.GetTemplate("new").HasValue);
            Assert.AreEqual(1, _manager.GetTemplates().Length);
        }

        [Test]
        public void SaveId_ShouldReturnExpectedValue()
        {
            Assert.AreEqual("AITemplateManager", _manager.SaveId);
        }

        // ===== Helper =====

        private AITemplateData CreateTemplate(
            string id, string name, string author, AITemplateCategory category)
        {
            return new AITemplateData
            {
                templateId = id,
                templateName = name,
                authorName = author,
                category = category,
                description = "",
                config = new CompanionAIConfig(),
                tags = new string[0]
            };
        }
    }
}
