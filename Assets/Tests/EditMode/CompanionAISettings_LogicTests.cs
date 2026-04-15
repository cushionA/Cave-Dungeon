using System.Collections.Generic;
using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// CompanionAISettingsLogic の単体テスト。
    /// タブ切替、編集バッファ、Dirty管理、保存/削除、影響範囲取得を検証する。
    /// </summary>
    [TestFixture]
    public class CompanionAISettings_LogicTests
    {
        private ModePresetRegistry _modeRegistry;
        private TacticalPresetRegistry _tacticalRegistry;
        private CompanionAISettingsLogic _logic;

        [SetUp]
        public void SetUp()
        {
            _modeRegistry = new ModePresetRegistry();
            _tacticalRegistry = new TacticalPresetRegistry(_modeRegistry);
            _logic = new CompanionAISettingsLogic(_modeRegistry, _tacticalRegistry);
        }

        [TearDown]
        public void TearDown()
        {
            _tacticalRegistry?.Dispose();
        }

        [Test]
        public void CompanionAISettingsLogic_Initial_State_IsClean()
        {
            Assert.IsFalse(_logic.IsDirty);
            Assert.IsNull(_logic.EditingConfigId);
            Assert.AreEqual(CompanionAISettingsLogic.TabId.TacticEdit, _logic.ActiveTab);
        }

        [Test]
        public void CompanionAISettingsLogic_SwitchTab_UpdatesActiveTab()
        {
            _logic.SwitchTab(CompanionAISettingsLogic.TabId.Shortcut);
            Assert.AreEqual(CompanionAISettingsLogic.TabId.Shortcut, _logic.ActiveTab);

            _logic.SwitchTab(CompanionAISettingsLogic.TabId.TacticEdit);
            Assert.AreEqual(CompanionAISettingsLogic.TabId.TacticEdit, _logic.ActiveTab);
        }

        [Test]
        public void CompanionAISettingsLogic_SetEditingName_MarksDirty()
        {
            Assert.IsFalse(_logic.IsDirty);
            _logic.SetEditingName("新しい名前");
            Assert.IsTrue(_logic.IsDirty);
            Assert.AreEqual("新しい名前", _logic.EditingBuffer.configName);
        }

        [Test]
        public void CompanionAISettingsLogic_SwitchEditingTarget_WithDirty_ReturnsUnsavedConfirm()
        {
            string id = _tacticalRegistry.Save("Preset", new CompanionAIConfig());
            _logic.SetEditingName("Dirty!");

            CompanionAISettingsLogic.SwitchResult result =
                _logic.SwitchEditingTarget(id, force: false);

            Assert.AreEqual(CompanionAISettingsLogic.SwitchResult.RequiresUnsavedConfirm, result);
            Assert.IsTrue(_logic.IsDirty);
        }

        [Test]
        public void CompanionAISettingsLogic_SwitchEditingTarget_ForceDiscardsDirtyBuffer()
        {
            string id = _tacticalRegistry.Save("Preset", new CompanionAIConfig { configName = "Preset" });
            _logic.SetEditingName("Dirty!");

            CompanionAISettingsLogic.SwitchResult result =
                _logic.SwitchEditingTarget(id, force: true);

            Assert.AreEqual(CompanionAISettingsLogic.SwitchResult.Succeeded, result);
            Assert.AreEqual(id, _logic.EditingConfigId);
            Assert.IsFalse(_logic.IsDirty);
            Assert.AreEqual("Preset", _logic.EditingBuffer.configName);
        }

        [Test]
        public void CompanionAISettingsLogic_SwitchEditingTarget_UnknownId_ReturnsNotFound()
        {
            CompanionAISettingsLogic.SwitchResult result =
                _logic.SwitchEditingTarget("nonexistent", force: true);

            Assert.AreEqual(CompanionAISettingsLogic.SwitchResult.NotFound, result);
        }

        [Test]
        public void CompanionAISettingsLogic_AddModeToBuffer_MarksDirty()
        {
            bool ok = _logic.AddModeToBuffer(new AIMode { modeName = "Combat" });

            Assert.IsTrue(ok);
            Assert.IsTrue(_logic.IsDirty);
            Assert.AreEqual(1, _logic.EditingBuffer.modes.Length);
            Assert.AreEqual("Combat", _logic.EditingBuffer.modes[0].modeName);
        }

        [Test]
        public void CompanionAISettingsLogic_AddModeToBuffer_ExceedsMaxMode_Rejected()
        {
            for (int i = 0; i < 4; i++)
            {
                _logic.AddModeToBuffer(new AIMode { modeName = "Mode" + i });
            }

            bool ok = _logic.AddModeToBuffer(new AIMode { modeName = "Overflow" });

            Assert.IsFalse(ok);
            Assert.AreEqual(4, _logic.EditingBuffer.modes.Length);
        }

        [Test]
        public void CompanionAISettingsLogic_RemoveModeFromBuffer_ShiftsRemaining()
        {
            _logic.AddModeToBuffer(new AIMode { modeName = "A" });
            _logic.AddModeToBuffer(new AIMode { modeName = "B" });
            _logic.AddModeToBuffer(new AIMode { modeName = "C" });

            bool ok = _logic.RemoveModeFromBuffer(1);

            Assert.IsTrue(ok);
            Assert.AreEqual(2, _logic.EditingBuffer.modes.Length);
            Assert.AreEqual("A", _logic.EditingBuffer.modes[0].modeName);
            Assert.AreEqual("C", _logic.EditingBuffer.modes[1].modeName);
        }

        [Test]
        public void CompanionAISettingsLogic_ReplaceModeFromPreset_AppliesPresetMode()
        {
            _logic.AddModeToBuffer(new AIMode { modeName = "Initial" });
            string presetId = _modeRegistry.Save(new AIMode { modeName = "PresetMode", defaultActionIndex = 3 });

            bool ok = _logic.ReplaceModeFromPreset(0, presetId);

            Assert.IsTrue(ok);
            Assert.AreEqual("PresetMode", _logic.EditingBuffer.modes[0].modeName);
            Assert.AreEqual(presetId, _logic.EditingBuffer.modes[0].modeId);
            Assert.AreEqual(3, _logic.EditingBuffer.modes[0].defaultActionIndex);
        }

        [Test]
        public void CompanionAISettingsLogic_ConvertModeToIndependent_ClearsModeId()
        {
            string presetId = _modeRegistry.Save(new AIMode { modeName = "Preset" });
            _logic.AddModeToBuffer(_modeRegistry.GetById(presetId).Value);
            Assert.AreEqual(presetId, _logic.EditingBuffer.modes[0].modeId);

            bool ok = _logic.ConvertModeToIndependent(0);

            Assert.IsTrue(ok);
            Assert.AreEqual("", _logic.EditingBuffer.modes[0].modeId);
            Assert.IsTrue(_logic.IsDirty);
        }

        [Test]
        public void CompanionAISettingsLogic_ConvertModeToIndependent_AlreadyIndependent_Rejected()
        {
            _logic.AddModeToBuffer(new AIMode { modeName = "Independent", modeId = "" });

            bool ok = _logic.ConvertModeToIndependent(0);

            Assert.IsFalse(ok);
        }

        [Test]
        public void CompanionAISettingsLogic_ApplyBufferToCurrentTactic_ClearsDirty()
        {
            _logic.SetEditingName("変更");
            _logic.AddModeToBuffer(new AIMode { modeName = "A" });

            _logic.ApplyBufferToCurrentTactic();

            Assert.IsFalse(_logic.IsDirty);
            Assert.AreEqual(1, _logic.CurrentTactic.modes.Length);
            Assert.AreEqual("現在の戦術", _logic.CurrentTactic.configName);
            Assert.IsNull(_logic.EditingConfigId);
        }

        [Test]
        public void CompanionAISettingsLogic_SaveBufferAsNewPreset_RegistersInTacticalRegistry()
        {
            _logic.AddModeToBuffer(new AIMode { modeName = "A" });

            string newId = _logic.SaveBufferAsNewPreset("新プリセット");

            Assert.IsNotNull(newId);
            Assert.AreEqual(newId, _logic.EditingConfigId);
            Assert.IsFalse(_logic.IsDirty);
            Assert.AreEqual(1, _tacticalRegistry.Count);
            CompanionAIConfig? saved = _tacticalRegistry.GetById(newId);
            Assert.IsNotNull(saved);
            Assert.AreEqual("新プリセット", saved.Value.configName);
        }

        [Test]
        public void CompanionAISettingsLogic_SaveBufferToEditingPreset_OverwritesExisting()
        {
            string id = _tacticalRegistry.Save("Old", new CompanionAIConfig { configName = "Old" });
            _logic.SwitchEditingTarget(id, force: true);

            _logic.SetEditingName("Updated");
            _logic.AddModeToBuffer(new AIMode { modeName = "Added" });

            bool ok = _logic.SaveBufferToEditingPreset();

            Assert.IsTrue(ok);
            Assert.IsFalse(_logic.IsDirty);
            CompanionAIConfig? stored = _tacticalRegistry.GetById(id);
            Assert.IsNotNull(stored);
            Assert.AreEqual("Updated", stored.Value.configName);
            Assert.AreEqual(1, stored.Value.modes.Length);
        }

        [Test]
        public void CompanionAISettingsLogic_DeletePreset_WhenOnlyOne_Rejected()
        {
            string id = _tacticalRegistry.Save("Only", new CompanionAIConfig());

            bool ok = _logic.DeletePreset(id);

            Assert.IsFalse(ok);
            Assert.AreEqual(1, _tacticalRegistry.Count);
        }

        [Test]
        public void CompanionAISettingsLogic_DeletePreset_RemovesEntry()
        {
            string idA = _tacticalRegistry.Save("A", new CompanionAIConfig());
            string idB = _tacticalRegistry.Save("B", new CompanionAIConfig());

            bool ok = _logic.DeletePreset(idA);

            Assert.IsTrue(ok);
            Assert.AreEqual(1, _tacticalRegistry.Count);
            Assert.IsNull(_tacticalRegistry.GetById(idA));
        }

        [Test]
        public void CompanionAISettingsLogic_DeleteEditingPreset_SwitchesToCurrentTactic()
        {
            string idA = _tacticalRegistry.Save("A", new CompanionAIConfig { configName = "A" });
            _tacticalRegistry.Save("B", new CompanionAIConfig { configName = "B" });
            _logic.SwitchEditingTarget(idA, force: true);
            Assert.AreEqual(idA, _logic.EditingConfigId);

            bool ok = _logic.DeletePreset(idA);

            Assert.IsTrue(ok);
            Assert.IsNull(_logic.EditingConfigId);
            Assert.IsFalse(_logic.IsDirty);
        }

        [Test]
        public void CompanionAISettingsLogic_GetModeReferenceCount_ReturnsReferencingConfigsCount()
        {
            string modeId = _modeRegistry.Save(new AIMode { modeName = "Shared" });
            AIMode shared = _modeRegistry.GetById(modeId).Value;

            _tacticalRegistry.Save("A", new CompanionAIConfig { modes = new AIMode[] { shared } });
            _tacticalRegistry.Save("B", new CompanionAIConfig { modes = new AIMode[] { shared } });
            _tacticalRegistry.Save("C", new CompanionAIConfig { modes = new AIMode[0] });

            int count = _logic.GetModeReferenceCount(modeId);

            Assert.AreEqual(2, count);
        }

        [Test]
        public void CompanionAISettingsLogic_GetModeReferencingConfigNames_ReturnsNames()
        {
            string modeId = _modeRegistry.Save(new AIMode { modeName = "Shared" });
            AIMode shared = _modeRegistry.GetById(modeId).Value;

            _tacticalRegistry.Save("TacticA", new CompanionAIConfig { modes = new AIMode[] { shared } });
            _tacticalRegistry.Save("TacticB", new CompanionAIConfig { modes = new AIMode[] { shared } });

            List<string> names = _logic.GetModeReferencingConfigNames(modeId);

            Assert.AreEqual(2, names.Count);
            Assert.Contains("TacticA", names);
            Assert.Contains("TacticB", names);
        }

        [Test]
        public void CompanionAISettingsLogic_SetShortcutBinding_UpdatesIndex()
        {
            bool ok = _logic.SetShortcutBinding(2, 5);

            Assert.IsTrue(ok);
            Assert.AreEqual(5, _logic.EditingBuffer.shortcutModeBindings[2]);
            Assert.IsTrue(_logic.IsDirty);
        }

        [Test]
        public void CompanionAISettingsLogic_SetShortcutBinding_OutOfRange_Rejected()
        {
            bool okLow = _logic.SetShortcutBinding(-1, 0);
            bool okHigh = _logic.SetShortcutBinding(4, 0);

            Assert.IsFalse(okLow);
            Assert.IsFalse(okHigh);
        }

        [Test]
        public void CompanionAISettingsLogic_ClearDirty_ResetsFlag()
        {
            _logic.SetEditingName("Dirty");
            Assert.IsTrue(_logic.IsDirty);

            _logic.ClearDirty();

            Assert.IsFalse(_logic.IsDirty);
        }

        [Test]
        public void CompanionAISettingsLogic_CascadeUpdate_WhenBufferIsSavedPreset_FromModeRegistry()
        {
            // シナリオ: モードプリセット→戦術プリセットで参照→モード上書き→
            //          戦術プリセットをロードしたらカスケード済みのモードが見える
            string modeId = _modeRegistry.Save(new AIMode { modeName = "Original" });
            AIMode referenced = _modeRegistry.GetById(modeId).Value;
            string tacticId = _tacticalRegistry.Save(
                "Tactic",
                new CompanionAIConfig { modes = new AIMode[] { referenced } });

            _modeRegistry.UpdateById(modeId, new AIMode { modeName = "Updated" });

            _logic.SwitchEditingTarget(tacticId, force: true);
            Assert.AreEqual("Updated", _logic.EditingBuffer.modes[0].modeName);
        }

        // =====================================================================
        // DuplicatePreset / ResolveDuplicateName
        // =====================================================================

        [Test]
        public void ResolveDuplicateName_NoCollision_AppendsFukusei()
        {
            string result = CompanionAISettingsLogic.ResolveDuplicateName("戦術A", new string[] { "戦術A" });
            Assert.AreEqual("戦術A (複製)", result);
        }

        [Test]
        public void ResolveDuplicateName_FirstCollision_AppendsIndexTwo()
        {
            string result = CompanionAISettingsLogic.ResolveDuplicateName(
                "戦術A",
                new string[] { "戦術A", "戦術A (複製)" });
            Assert.AreEqual("戦術A (複製 2)", result);
        }

        [Test]
        public void ResolveDuplicateName_MultipleCollisions_FindsNextFreeIndex()
        {
            string result = CompanionAISettingsLogic.ResolveDuplicateName(
                "戦術A",
                new string[] { "戦術A", "戦術A (複製)", "戦術A (複製 2)", "戦術A (複製 3)" });
            Assert.AreEqual("戦術A (複製 4)", result);
        }

        [Test]
        public void ResolveDuplicateName_FromFukuseiName_StripsSuffixBeforeNumbering()
        {
            // "戦術A (複製)" を複製する → ベース名は "戦術A" とみなして "(複製 2)" を返す
            string result = CompanionAISettingsLogic.ResolveDuplicateName(
                "戦術A (複製)",
                new string[] { "戦術A", "戦術A (複製)" });
            Assert.AreEqual("戦術A (複製 2)", result);
        }

        [Test]
        public void ResolveDuplicateName_FromNumberedFukuseiName_StripsBeforeNumbering()
        {
            // "戦術A (複製 5)" を複製する → ベース名は "戦術A" → 最初の空き番号
            string result = CompanionAISettingsLogic.ResolveDuplicateName(
                "戦術A (複製 5)",
                new string[] { "戦術A", "戦術A (複製 5)" });
            Assert.AreEqual("戦術A (複製)", result);
        }

        [Test]
        public void ResolveDuplicateName_EmptyName_FallsBackToUnnamed()
        {
            string result = CompanionAISettingsLogic.ResolveDuplicateName("", new string[0]);
            Assert.AreEqual("(無名) (複製)", result);
        }

        [Test]
        public void ResolveDuplicateName_NullName_FallsBackToUnnamed()
        {
            string result = CompanionAISettingsLogic.ResolveDuplicateName(null, new string[0]);
            Assert.AreEqual("(無名) (複製)", result);
        }

        [Test]
        public void ResolveDuplicateName_EmptyExisting_ReturnsFirstCandidate()
        {
            string result = CompanionAISettingsLogic.ResolveDuplicateName("戦術A", null);
            Assert.AreEqual("戦術A (複製)", result);
        }

        [Test]
        public void DuplicatePreset_NewName_AvoidsCollision()
        {
            string sourceId = _tacticalRegistry.Save("戦術A", new CompanionAIConfig { configName = "戦術A" });

            string dupId1 = _logic.DuplicatePreset(sourceId);
            string dupId2 = _logic.DuplicatePreset(sourceId);
            string dupId3 = _logic.DuplicatePreset(sourceId);

            Assert.IsNotNull(dupId1);
            Assert.IsNotNull(dupId2);
            Assert.IsNotNull(dupId3);
            Assert.AreNotEqual(sourceId, dupId1);
            Assert.AreNotEqual(dupId1, dupId2);
            Assert.AreNotEqual(dupId2, dupId3);

            Assert.AreEqual("戦術A (複製)", _tacticalRegistry.GetById(dupId1).Value.configName);
            Assert.AreEqual("戦術A (複製 2)", _tacticalRegistry.GetById(dupId2).Value.configName);
            Assert.AreEqual("戦術A (複製 3)", _tacticalRegistry.GetById(dupId3).Value.configName);
        }

        [Test]
        public void DuplicatePreset_DeepCopiesModesArray()
        {
            // 複製後にどちらかの modes 配列を書き換えても、もう片方に影響しないこと
            AIMode mode = new AIMode { modeName = "Original" };
            string sourceId = _tacticalRegistry.Save(
                "戦術A",
                new CompanionAIConfig { configName = "戦術A", modes = new AIMode[] { mode } });

            string dupId = _logic.DuplicatePreset(sourceId);
            Assert.IsNotNull(dupId);

            CompanionAIConfig sourceConfig = _tacticalRegistry.GetById(sourceId).Value;
            CompanionAIConfig dupConfig = _tacticalRegistry.GetById(dupId).Value;

            // 配列インスタンスが別物であること
            Assert.AreNotSame(sourceConfig.modes, dupConfig.modes);

            // 複製側のモード名を書き換えても元側は変わらないこと
            dupConfig.modes[0] = new AIMode { modeName = "Modified" };
            CompanionAIConfig sourceAfter = _tacticalRegistry.GetById(sourceId).Value;
            Assert.AreEqual("Original", sourceAfter.modes[0].modeName);
        }

        [Test]
        public void DuplicatePreset_UnknownId_ReturnsNull()
        {
            string result = _logic.DuplicatePreset("nonexistent");
            Assert.IsNull(result);
        }

        [Test]
        public void DuplicatePreset_FromAlreadyDuplicatedName_StripsSuffixAndAvoidsCollision()
        {
            // ユーザーが「戦術A (複製)」を選んで複製すると、末尾の (複製) を剥がして "戦術A (複製 2)" を生成する
            _tacticalRegistry.Save("戦術A", new CompanionAIConfig { configName = "戦術A" });
            string dup1Id = _tacticalRegistry.Save("戦術A (複製)", new CompanionAIConfig { configName = "戦術A (複製)" });

            string dup2Id = _logic.DuplicatePreset(dup1Id);

            Assert.IsNotNull(dup2Id);
            Assert.AreEqual("戦術A (複製 2)", _tacticalRegistry.GetById(dup2Id).Value.configName);
        }

        [Test]
        public void DuplicatePreset_WhenRegistryFull_ReturnsNull()
        {
            // 上限まで埋める
            string lastId = null;
            for (int i = 0; i < 20; i++)
            {
                lastId = _tacticalRegistry.Save("戦術" + i, new CompanionAIConfig { configName = "戦術" + i });
            }

            string result = _logic.DuplicatePreset(lastId);

            Assert.IsNull(result);
        }
    }
}
