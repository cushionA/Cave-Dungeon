using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Game.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.EditMode
{
    public class FlagManagerTests
    {
        private FlagManager _flagManager;

        [SetUp]
        public void SetUp()
        {
            _flagManager = new FlagManager();
        }

        // ===== ISaveable =====

        [Test]
        public void FlagManager_ImplementsISaveable()
        {
            Assert.IsInstanceOf<ISaveable>(_flagManager);
        }

        [Test]
        public void FlagManager_SaveId_ReturnsFlagManager()
        {
            Assert.AreEqual("FlagManager", ((ISaveable)_flagManager).SaveId);
        }

        // ===== グローバルフラグ =====

        [Test]
        public void FlagManager_SetGlobalFlag_GetReturnsTrue()
        {
            _flagManager.SetGlobalFlag("story_chapter1_complete", true);

            Assert.IsTrue(_flagManager.GetGlobalFlag("story_chapter1_complete"));
        }

        [Test]
        public void FlagManager_GetGlobalFlag_NotSet_ReturnsFalse()
        {
            Assert.IsFalse(_flagManager.GetGlobalFlag("nonexistent"));
        }

        [Test]
        public void FlagManager_SetGlobalFlag_ToggleOff()
        {
            _flagManager.SetGlobalFlag("flag1", true);
            _flagManager.SetGlobalFlag("flag1", false);

            Assert.IsFalse(_flagManager.GetGlobalFlag("flag1"));
        }

        // ===== マップローカルフラグ =====

        [Test]
        public void FlagManager_SetLocalFlag_GetReturnsTrue()
        {
            _flagManager.SwitchMap("map_forest");
            _flagManager.SetLocalFlag("chest_opened", true);

            Assert.IsTrue(_flagManager.GetLocalFlag("chest_opened"));
        }

        [Test]
        public void FlagManager_GetLocalFlag_NoMapSwitched_ReturnsFalse()
        {
            Assert.IsFalse(_flagManager.GetLocalFlag("any_flag"));
        }

        [Test]
        public void FlagManager_SwitchMap_LocalFlagsSwitched()
        {
            _flagManager.SwitchMap("map_forest");
            _flagManager.SetLocalFlag("event_played", true);

            _flagManager.SwitchMap("map_cave");
            _flagManager.SetLocalFlag("boss_defeated", true);

            // forestに戻るとforestのフラグが見える
            _flagManager.SwitchMap("map_forest");
            Assert.IsTrue(_flagManager.GetLocalFlag("event_played"));
            Assert.IsFalse(_flagManager.GetLocalFlag("boss_defeated"));

            // caveに戻るとcaveのフラグが見える
            _flagManager.SwitchMap("map_cave");
            Assert.IsTrue(_flagManager.GetLocalFlag("boss_defeated"));
            Assert.IsFalse(_flagManager.GetLocalFlag("event_played"));
        }

        [Test]
        public void FlagManager_SwitchMap_Null_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _flagManager.SwitchMap(null));
        }

        // ===== Serialize/Deserialize =====

        [Test]
        public void FlagManager_SerializeDeserialize_GlobalFlags_Preserved()
        {
            _flagManager.SetGlobalFlag("story_flag_1", true);
            _flagManager.SetGlobalFlag("achievement_1", true);
            ISaveable saveable = _flagManager;

            object data = saveable.Serialize();

            FlagManager restored = new FlagManager();
            ((ISaveable)restored).Deserialize(data);

            Assert.IsTrue(restored.GetGlobalFlag("story_flag_1"));
            Assert.IsTrue(restored.GetGlobalFlag("achievement_1"));
            Assert.IsFalse(restored.GetGlobalFlag("nonexistent"));
        }

        [Test]
        public void FlagManager_SerializeDeserialize_LocalFlags_Preserved()
        {
            _flagManager.SwitchMap("map_a");
            _flagManager.SetLocalFlag("chest_1", true);
            _flagManager.SwitchMap("map_b");
            _flagManager.SetLocalFlag("event_1", true);
            ISaveable saveable = _flagManager;

            object data = saveable.Serialize();

            FlagManager restored = new FlagManager();
            ((ISaveable)restored).Deserialize(data);

            restored.SwitchMap("map_a");
            Assert.IsTrue(restored.GetLocalFlag("chest_1"));

            restored.SwitchMap("map_b");
            Assert.IsTrue(restored.GetLocalFlag("event_1"));
        }

        [Test]
        public void FlagManager_SerializeDeserialize_RoundTrip_CompleteState()
        {
            _flagManager.SetGlobalFlag("global_1", true);
            _flagManager.SwitchMap("map_x");
            _flagManager.SetLocalFlag("local_1", true);
            _flagManager.SetLocalFlag("local_2", false);
            ISaveable saveable = _flagManager;

            object data = saveable.Serialize();

            FlagManager restored = new FlagManager();
            ((ISaveable)restored).Deserialize(data);

            Assert.IsTrue(restored.GetGlobalFlag("global_1"));
            restored.SwitchMap("map_x");
            Assert.IsTrue(restored.GetLocalFlag("local_1"));
            Assert.IsFalse(restored.GetLocalFlag("local_2"));
        }

        [Test]
        public void FlagManager_LoadingLegacySlotWithoutFlags_DoesNotInheritFlagsFromAnotherSlot()
        {
            SaveManager saves = new SaveManager();
            saves.Register(_flagManager);
            _flagManager.SwitchMap("mine");
            _flagManager.SetGlobalFlag("boss_defeated", true);
            _flagManager.SetLocalFlag("reward_claimed", true);
            saves.Save(0);
            saves.SetSlotData(1, new SaveSlotData(1));

            Assert.IsTrue(saves.Load(1));
            Assert.IsNull(_flagManager.CurrentMapId);
            _flagManager.SwitchMap("mine");
            Assert.IsFalse(_flagManager.GetGlobalFlag("boss_defeated"));
            Assert.IsFalse(_flagManager.GetLocalFlag("reward_claimed"));

            Assert.IsTrue(saves.Load(0));
            Assert.IsTrue(_flagManager.GetGlobalFlag("boss_defeated"));
            Assert.IsTrue(_flagManager.GetLocalFlag("reward_claimed"));
        }

        [Test]
        public void FlagManager_Deserialize_RejectsMalformedDataWithoutOverwritingLiveFlags()
        {
            _flagManager.SetGlobalFlag("boss_defeated", true);
            _flagManager.SwitchMap("mine");
            _flagManager.SetLocalFlag("reward_claimed", true);

            ISaveable saveable = _flagManager;
            Assert.Throws<ArgumentException>(() => saveable.Deserialize(null));
            Assert.Throws<ArgumentException>(() => saveable.Deserialize(42));
            Assert.Throws<ArgumentException>(() => saveable.Deserialize(new FlagSaveData
            {
                mapLocalFlags = new Dictionary<string, Dictionary<string, bool>> { { "mine", null } },
            }));

            Assert.AreEqual("mine", _flagManager.CurrentMapId);
            Assert.IsTrue(_flagManager.GetGlobalFlag("boss_defeated"));
            Assert.IsTrue(_flagManager.GetLocalFlag("reward_claimed"));
        }

        [Test]
        public void FlagManager_Load_WithMalformedJson_RejectsBeforeEarlierSaveablesApply()
        {
            var saves = new SaveManager();
            var earlier = new TrackingSaveable { Value = 12 };
            saves.Register(earlier);
            saves.Register(_flagManager);
            _flagManager.SwitchMap("mine");
            _flagManager.SetGlobalFlag("boss_defeated", true);
            _flagManager.SetLocalFlag("reward_claimed", true);
            SaveSlotData current = saves.Save(0);
            Assert.IsTrue(saves.Load(0));
            ulong revision = saves.LoadRevision;
            int applied = earlier.ApplyCount;
            earlier.Value = 13;

            var rejected = new SaveSlotData(1);
            rejected.entries[earlier.SaveId] = 34;
            rejected.entries[_flagManager.SaveId] = JObject.Parse(@"{""mapLocalFlags"":{""mine"":null}}");
            saves.SetSlotData(1, rejected);
            int notifications = 0;
            saves.Loaded += _ => notifications++;

            LogAssert.Expect(LogType.Error,
                new Regex(@"\[SaveManager\].*スロットの事前検証に失敗しました.*マップのフラグデータ"));
            Assert.IsFalse(saves.Load(1));

            Assert.AreEqual(applied, earlier.ApplyCount);
            Assert.AreEqual(13, earlier.Value);
            Assert.AreEqual("mine", _flagManager.CurrentMapId);
            Assert.IsTrue(_flagManager.GetGlobalFlag("boss_defeated"));
            Assert.IsTrue(_flagManager.GetLocalFlag("reward_claimed"));
            Assert.AreEqual(0, saves.ActiveSlotIndex);
            Assert.AreSame(current, saves.LastLoadedSlot);
            Assert.AreEqual(revision, saves.LoadRevision);
            Assert.AreEqual(0, notifications);
        }

        [Test]
        public void FlagManager_Load_WithLegacyJsonMissingLocalFlags_RestoresAndAllowsWritesInCurrentMap()
        {
            var saves = new SaveManager();
            saves.Register(_flagManager);
            var legacy = new SaveSlotData(1);
            legacy.entries[_flagManager.SaveId] = JObject.Parse(
                @"{""globalFlags"":{""boss_defeated"":true},""currentMapId"":""mine""}");
            saves.SetSlotData(1, legacy);

            Assert.IsTrue(saves.Load(1));
            Assert.AreEqual("mine", _flagManager.CurrentMapId);
            Assert.IsTrue(_flagManager.GetGlobalFlag("boss_defeated"));
            Assert.IsFalse(_flagManager.GetLocalFlag("reward_claimed"));
            _flagManager.SetLocalFlag("reward_claimed", true);
            Assert.IsTrue(_flagManager.GetLocalFlag("reward_claimed"));
        }

        // ===== CurrentMapId =====

        [Test]
        public void FlagManager_CurrentMapId_ReturnsCurrentMap()
        {
            Assert.IsNull(_flagManager.CurrentMapId);

            _flagManager.SwitchMap("map_forest");
            Assert.AreEqual("map_forest", _flagManager.CurrentMapId);
        }

        private sealed class TrackingSaveable : ISaveable
        {
            public string SaveId => "earlier";
            public int Value { get; set; }
            public int ApplyCount { get; private set; }

            public object Serialize() => Value;

            public void Deserialize(object data)
            {
                ApplyCount++;
                Value = (int)data;
            }
        }
    }
}
