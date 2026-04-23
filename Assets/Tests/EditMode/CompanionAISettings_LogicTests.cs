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
        public void CompanionAISettingsLogic_InitialShortcutBindings_AllUnassigned()
        {
            // 初期状態ではすべてのスロットが未割当(-1)である
            // 0埋めだと modes[0] を指すのと区別できないため -1 を既定とする
            int[] bindings = _logic.EditingBuffer.shortcutModeBindings;

            Assert.IsNotNull(bindings);
            Assert.AreEqual(4, bindings.Length);
            for (int i = 0; i < bindings.Length; i++)
            {
                Assert.AreEqual(-1, bindings[i]);
            }
        }

        [Test]
        public void CompanionAISettingsLogic_SetShortcutBinding_UpdatesModeIndex()
        {
            // slot 2 に modeIndex 1 を割り当てる
            bool ok = _logic.SetShortcutBinding(2, 1);

            Assert.IsTrue(ok);
            Assert.AreEqual(1, _logic.EditingBuffer.shortcutModeBindings[2]);
            Assert.IsTrue(_logic.IsDirty);
        }

        [Test]
        public void CompanionAISettingsLogic_SetShortcutBinding_UnassignedValue_Stored()
        {
            // 一度割り当ててから -1（未割当）に戻せる
            _logic.SetShortcutBinding(0, 2);
            bool ok = _logic.SetShortcutBinding(0, -1);

            Assert.IsTrue(ok);
            Assert.AreEqual(-1, _logic.EditingBuffer.shortcutModeBindings[0]);
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
        public void CompanionAISettingsLogic_SetShortcutBinding_NegativeModeIndex_NormalizedToMinusOne()
        {
            // -1 未満の modeIndex は未割当 (-1) に正規化され、不正値のまま保存されない
            bool ok = _logic.SetShortcutBinding(1, -7);

            Assert.IsTrue(ok);
            Assert.AreEqual(-1, _logic.EditingBuffer.shortcutModeBindings[1]);
        }

        [Test]
        public void CompanionAISettingsLogic_CreateDefaultShortcutBindings_ReturnsMinusOneFilled()
        {
            int[] bindings = CompanionAISettingsLogic.CreateDefaultShortcutBindings();

            Assert.AreEqual(4, bindings.Length);
            for (int i = 0; i < bindings.Length; i++)
            {
                Assert.AreEqual(-1, bindings[i]);
            }
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

        // =====================================================================
        // 行動ルール×ActionSlot 統合モデル用ヘルパー（Phase 5）
        // =====================================================================

        [Test]
        public void GcOrphanActionSlots_RemovesUnreferencedSlots_AndRemapsIndices()
        {
            AIMode mode = new AIMode
            {
                actions = new ActionSlot[]
                {
                    new ActionSlot { displayName = "A_unused" },
                    new ActionSlot { displayName = "B_usedByRule" },
                    new ActionSlot { displayName = "C_unused" },
                    new ActionSlot { displayName = "D_usedByDefault" },
                },
                actionRules = new AIRule[]
                {
                    new AIRule { actionIndex = 1, conditions = new AICondition[0], probability = 100 },
                },
                defaultActionIndex = 3,
            };

            AIMode result = CompanionAISettingsLogic.GcOrphanActionSlots(mode);

            Assert.AreEqual(2, result.actions.Length);
            Assert.AreEqual("B_usedByRule", result.actions[0].displayName);
            Assert.AreEqual("D_usedByDefault", result.actions[1].displayName);
            Assert.AreEqual(0, result.actionRules[0].actionIndex);
            Assert.AreEqual(1, result.defaultActionIndex);
        }

        [Test]
        public void GcOrphanActionSlots_NoOrphans_StructurePreserved()
        {
            AIMode mode = new AIMode
            {
                actions = new ActionSlot[]
                {
                    new ActionSlot { displayName = "X" },
                    new ActionSlot { displayName = "Y" },
                },
                actionRules = new AIRule[]
                {
                    new AIRule { actionIndex = 1, conditions = new AICondition[0], probability = 100 },
                    new AIRule { actionIndex = 0, conditions = new AICondition[0], probability = 100 },
                },
                defaultActionIndex = 0,
            };

            AIMode result = CompanionAISettingsLogic.GcOrphanActionSlots(mode);

            Assert.AreEqual(2, result.actions.Length);
            Assert.AreEqual("X", result.actions[0].displayName);
            Assert.AreEqual("Y", result.actions[1].displayName);
            Assert.AreEqual(1, result.actionRules[0].actionIndex);
            Assert.AreEqual(0, result.actionRules[1].actionIndex);
            Assert.AreEqual(0, result.defaultActionIndex);
        }

        [Test]
        public void GcOrphanActionSlots_EmptyActions_InsertsIdleDefault()
        {
            AIMode mode = new AIMode
            {
                actions = null,
                actionRules = null,
                defaultActionIndex = 5, // 不正値
            };

            AIMode result = CompanionAISettingsLogic.GcOrphanActionSlots(mode);

            // default を Idle スロット（"何もしない"）に正規化する。
            // actions が空のままだと EvaluateAction が defaultActionIndex を辿っても
            // 棒立ちするため、最低 1 個の Idle を差し込むことで no-op 遷移を保証する。
            Assert.IsNotNull(result.actions);
            Assert.AreEqual(1, result.actions.Length);
            Assert.AreEqual(ActionExecType.Sustained, result.actions[0].execType);
            Assert.AreEqual((int)SustainedAction.Idle, result.actions[0].paramId);
            Assert.IsNotNull(result.actionRules);
            Assert.AreEqual(0, result.actionRules.Length);
            Assert.AreEqual(0, result.defaultActionIndex);
        }

        [Test]
        public void AddActionRuleWithNewSlot_AppendsBothArrays()
        {
            AIMode mode = new AIMode
            {
                actions = new ActionSlot[0],
                actionRules = new AIRule[0],
                defaultActionIndex = 0,
            };
            ActionSlot slot = new ActionSlot { displayName = "Attack1" };

            AIMode result = CompanionAISettingsLogic.AddActionRuleWithNewSlot(
                mode, new AICondition[0], slot, 75);

            Assert.AreEqual(1, result.actions.Length);
            Assert.AreEqual("Attack1", result.actions[0].displayName);
            Assert.AreEqual(1, result.actionRules.Length);
            Assert.AreEqual(0, result.actionRules[0].actionIndex);
            Assert.AreEqual((byte)75, result.actionRules[0].probability);
        }

        [Test]
        public void AddActionRuleWithNewSlot_PreservesExistingArrays()
        {
            AIMode mode = new AIMode
            {
                actions = new ActionSlot[] { new ActionSlot { displayName = "Old" } },
                actionRules = new AIRule[]
                {
                    new AIRule { actionIndex = 0, conditions = new AICondition[0], probability = 50 },
                },
                defaultActionIndex = 0,
            };
            ActionSlot slot = new ActionSlot { displayName = "New" };

            AIMode result = CompanionAISettingsLogic.AddActionRuleWithNewSlot(
                mode, new AICondition[0], slot, 100);

            Assert.AreEqual(2, result.actions.Length);
            Assert.AreEqual("Old", result.actions[0].displayName);
            Assert.AreEqual("New", result.actions[1].displayName);
            Assert.AreEqual(2, result.actionRules.Length);
            Assert.AreEqual(0, result.actionRules[0].actionIndex);
            Assert.AreEqual(1, result.actionRules[1].actionIndex);
        }

        [Test]
        public void DuplicateActionRule_InsertsDuplicateSharingActionIndex()
        {
            AIMode mode = new AIMode
            {
                actions = new ActionSlot[] { new ActionSlot { displayName = "Shared" } },
                actionRules = new AIRule[]
                {
                    new AIRule { actionIndex = 0, conditions = new AICondition[0], probability = 80 },
                },
                defaultActionIndex = 0,
            };

            AIMode result = CompanionAISettingsLogic.DuplicateActionRule(mode, 0);

            Assert.AreEqual(2, result.actionRules.Length);
            Assert.AreEqual(0, result.actionRules[0].actionIndex);
            Assert.AreEqual(0, result.actionRules[1].actionIndex); // 共有
            Assert.AreEqual(1, result.actions.Length, "ActionSlot は複製されない");
        }

        [Test]
        public void DuplicateActionRule_DeepClonesConditionsArray()
        {
            AICondition[] origConds = new AICondition[]
            {
                new AICondition { conditionType = AIConditionType.HpRatio, operandA = 50 },
            };
            AIMode mode = new AIMode
            {
                actions = new ActionSlot[] { new ActionSlot { displayName = "A" } },
                actionRules = new AIRule[]
                {
                    new AIRule { actionIndex = 0, conditions = origConds, probability = 100 },
                },
                defaultActionIndex = 0,
            };

            AIMode result = CompanionAISettingsLogic.DuplicateActionRule(mode, 0);

            Assert.AreNotSame(origConds, result.actionRules[1].conditions, "conditions配列は別参照");
            // 複製側を書き換えても元配列に伝播しない
            result.actionRules[1].conditions[0].operandA = 99;
            Assert.AreEqual(50, origConds[0].operandA);
        }

        [Test]
        public void DuplicateActionRule_OutOfRange_NoChange()
        {
            AIMode mode = new AIMode
            {
                actions = new ActionSlot[] { new ActionSlot { displayName = "A" } },
                actionRules = new AIRule[]
                {
                    new AIRule { actionIndex = 0, conditions = new AICondition[0], probability = 100 },
                },
                defaultActionIndex = 0,
            };

            AIMode result = CompanionAISettingsLogic.DuplicateActionRule(mode, 5);

            Assert.AreEqual(1, result.actionRules.Length);
        }

        [Test]
        public void RemoveActionRule_RemovesOnlyRule_KeepsActionSlotAsOrphan()
        {
            AIMode mode = new AIMode
            {
                actions = new ActionSlot[]
                {
                    new ActionSlot { displayName = "A" },
                    new ActionSlot { displayName = "B" },
                },
                actionRules = new AIRule[]
                {
                    new AIRule { actionIndex = 0, conditions = new AICondition[0], probability = 100 },
                    new AIRule { actionIndex = 1, conditions = new AICondition[0], probability = 100 },
                },
                defaultActionIndex = 1,
            };

            AIMode result = CompanionAISettingsLogic.RemoveActionRule(mode, 0);

            Assert.AreEqual(1, result.actionRules.Length);
            Assert.AreEqual(1, result.actionRules[0].actionIndex);
            Assert.AreEqual(2, result.actions.Length, "ActionSlot は削除時点では GC しない");
        }

        [Test]
        public void RemoveThenGc_OrphanSlotIsCollected()
        {
            AIMode mode = new AIMode
            {
                actions = new ActionSlot[]
                {
                    new ActionSlot { displayName = "Keep" },
                    new ActionSlot { displayName = "Orphan" },
                },
                actionRules = new AIRule[]
                {
                    new AIRule { actionIndex = 0, conditions = new AICondition[0], probability = 100 },
                    new AIRule { actionIndex = 1, conditions = new AICondition[0], probability = 100 },
                },
                defaultActionIndex = 0,
            };

            AIMode afterRemove = CompanionAISettingsLogic.RemoveActionRule(mode, 1);
            AIMode afterGc = CompanionAISettingsLogic.GcOrphanActionSlots(afterRemove);

            Assert.AreEqual(1, afterGc.actions.Length);
            Assert.AreEqual("Keep", afterGc.actions[0].displayName);
            Assert.AreEqual(0, afterGc.actionRules[0].actionIndex);
            Assert.AreEqual(0, afterGc.defaultActionIndex);
        }

        [Test]
        public void ReplaceActionRuleSlot_SharedWithDefault_AppendsAndRepoints()
        {
            AIMode mode = new AIMode
            {
                actions = new ActionSlot[] { new ActionSlot { displayName = "Shared" } },
                actionRules = new AIRule[]
                {
                    new AIRule { actionIndex = 0, conditions = new AICondition[0], probability = 100 },
                },
                defaultActionIndex = 0, // ルールと default が同じ actions[0] を参照
            };
            ActionSlot newSlot = new ActionSlot { displayName = "New" };

            AIMode result = CompanionAISettingsLogic.ReplaceActionRuleSlot(mode, 0, newSlot);

            Assert.AreEqual(2, result.actions.Length);
            Assert.AreEqual("Shared", result.actions[0].displayName);
            Assert.AreEqual("New", result.actions[1].displayName);
            Assert.AreEqual(0, result.defaultActionIndex, "default の参照は変えない");
            Assert.AreEqual(1, result.actionRules[0].actionIndex, "rule だけ新 slot を指す");
        }

        [Test]
        public void ReplaceActionRuleSlot_SharedWithOtherRule_AppendsAndRepoints()
        {
            AIMode mode = new AIMode
            {
                actions = new ActionSlot[] { new ActionSlot { displayName = "Shared" } },
                actionRules = new AIRule[]
                {
                    new AIRule { actionIndex = 0, conditions = new AICondition[0], probability = 100 },
                    new AIRule { actionIndex = 0, conditions = new AICondition[0], probability = 50 },
                },
                defaultActionIndex = 0,
            };
            ActionSlot newSlot = new ActionSlot { displayName = "New" };

            AIMode result = CompanionAISettingsLogic.ReplaceActionRuleSlot(mode, 0, newSlot);

            Assert.AreEqual(2, result.actions.Length);
            Assert.AreEqual(1, result.actionRules[0].actionIndex);
            Assert.AreEqual(0, result.actionRules[1].actionIndex, "別ルールの参照は温存");
        }

        [Test]
        public void ReplaceActionRuleSlot_Unshared_UpdatesInPlace()
        {
            AIMode mode = new AIMode
            {
                actions = new ActionSlot[]
                {
                    new ActionSlot { displayName = "Unique" },
                    new ActionSlot { displayName = "DefaultSlot" },
                },
                actionRules = new AIRule[]
                {
                    new AIRule { actionIndex = 0, conditions = new AICondition[0], probability = 100 },
                },
                defaultActionIndex = 1,
            };
            ActionSlot newSlot = new ActionSlot { displayName = "Replaced" };

            AIMode result = CompanionAISettingsLogic.ReplaceActionRuleSlot(mode, 0, newSlot);

            Assert.AreEqual(2, result.actions.Length, "新スロットは追加されない");
            Assert.AreEqual("Replaced", result.actions[0].displayName);
            Assert.AreEqual(0, result.actionRules[0].actionIndex);
            Assert.AreEqual("DefaultSlot", result.actions[1].displayName);
            Assert.AreEqual(1, result.defaultActionIndex);
        }

        [Test]
        public void ReplaceDefaultActionSlot_SharedWithRule_AppendsAndRepointsDefault()
        {
            AIMode mode = new AIMode
            {
                actions = new ActionSlot[] { new ActionSlot { displayName = "Shared" } },
                actionRules = new AIRule[]
                {
                    new AIRule { actionIndex = 0, conditions = new AICondition[0], probability = 100 },
                },
                defaultActionIndex = 0,
            };
            ActionSlot newSlot = new ActionSlot { displayName = "NewDefault" };

            AIMode result = CompanionAISettingsLogic.ReplaceDefaultActionSlot(mode, newSlot);

            Assert.AreEqual(2, result.actions.Length);
            Assert.AreEqual("Shared", result.actions[0].displayName, "ルール側は不変");
            Assert.AreEqual("NewDefault", result.actions[1].displayName);
            Assert.AreEqual(0, result.actionRules[0].actionIndex, "ルールの参照は温存");
            Assert.AreEqual(1, result.defaultActionIndex);
        }

        [Test]
        public void ReplaceDefaultActionSlot_Unshared_UpdatesInPlace()
        {
            AIMode mode = new AIMode
            {
                actions = new ActionSlot[]
                {
                    new ActionSlot { displayName = "RuleSlot" },
                    new ActionSlot { displayName = "OldDefault" },
                },
                actionRules = new AIRule[]
                {
                    new AIRule { actionIndex = 0, conditions = new AICondition[0], probability = 100 },
                },
                defaultActionIndex = 1,
            };
            ActionSlot newSlot = new ActionSlot { displayName = "NewDefault" };

            AIMode result = CompanionAISettingsLogic.ReplaceDefaultActionSlot(mode, newSlot);

            Assert.AreEqual(2, result.actions.Length);
            Assert.AreEqual("NewDefault", result.actions[1].displayName);
            Assert.AreEqual(1, result.defaultActionIndex);
        }

        // =====================================================================
        // Idle デフォルト行動 (SustainedAction.Idle) の正規化 / 生成テスト
        // =====================================================================

        [Test]
        public void CreateDefaultIdleSlot_ProducesSustainedIdleNoOpSlot()
        {
            ActionSlot idle = CompanionAISettingsLogic.CreateDefaultIdleSlot();

            // SustainedActionHandler は paramId を保持するだけなので
            // Sustained + Idle + paramValue=0 (= 無制限) で no-op 待機になる
            Assert.AreEqual(ActionExecType.Sustained, idle.execType);
            Assert.AreEqual((int)SustainedAction.Idle, idle.paramId);
            Assert.AreEqual(0f, idle.paramValue);
            Assert.AreEqual("何もしない", idle.displayName);
        }

        [Test]
        public void CreateDefaultMode_HasIdleSlotAsDefault()
        {
            AIMode mode = CompanionAISettingsLogic.CreateDefaultMode();

            Assert.IsNotNull(mode.actions);
            Assert.AreEqual(1, mode.actions.Length,
                "新規 AIMode は Idle を default として 1 スロット持つべき");
            Assert.AreEqual(ActionExecType.Sustained, mode.actions[0].execType);
            Assert.AreEqual((int)SustainedAction.Idle, mode.actions[0].paramId);
            Assert.AreEqual(0, mode.defaultActionIndex,
                "defaultActionIndex は Idle スロット (index 0) を指すべき");
            Assert.IsNotNull(mode.actionRules);
            Assert.AreEqual(0, mode.actionRules.Length);
            Assert.IsNotNull(mode.targetRules);
            Assert.IsNotNull(mode.targetSelects);
        }

        [Test]
        public void CreateDefaultMode_WithName_RetainsModeName()
        {
            AIMode mode = CompanionAISettingsLogic.CreateDefaultMode("攻撃優先");

            Assert.AreEqual("攻撃優先", mode.modeName);
            Assert.AreEqual((int)SustainedAction.Idle, mode.actions[0].paramId);
        }

        [Test]
        public void GcOrphanActionSlots_AllOrphans_InsertsIdleAndPointsDefault()
        {
            // actions があるが actionRules が全て無効 index を指し、default も範囲外 → compact が空
            AIMode mode = new AIMode
            {
                actions = new ActionSlot[]
                {
                    new ActionSlot { displayName = "Orphan1", execType = ActionExecType.Attack },
                    new ActionSlot { displayName = "Orphan2", execType = ActionExecType.Cast },
                },
                actionRules = new AIRule[]
                {
                    new AIRule { actionIndex = 99, conditions = new AICondition[0], probability = 100 },
                },
                defaultActionIndex = 99, // 範囲外
            };

            AIMode result = CompanionAISettingsLogic.GcOrphanActionSlots(mode);

            // すべて孤児なので Idle が差し込まれる
            Assert.AreEqual(1, result.actions.Length);
            Assert.AreEqual(ActionExecType.Sustained, result.actions[0].execType);
            Assert.AreEqual((int)SustainedAction.Idle, result.actions[0].paramId);
            Assert.AreEqual(0, result.defaultActionIndex);
        }

        /// <summary>
        /// CloneConfig 経由（LoadPreset → EditingBuffer 反映）で
        /// manualOverrideTimeoutSeconds がコピーされることを検証する。
        /// このテストがなければ CloneConfig の伝搬漏れを検出できない。
        /// </summary>
        [Test]
        public void SwitchEditingTarget_PreservesManualOverrideTimeoutSecondsInEditingBuffer()
        {
            CompanionAIConfig source = new CompanionAIConfig
            {
                configName = "Tuned",
                manualOverrideTimeoutSeconds = 8.25f
            };
            string id = _tacticalRegistry.Save("Tuned", source);

            CompanionAISettingsLogic.SwitchResult result =
                _logic.SwitchEditingTarget(id, force: true);

            Assert.AreEqual(CompanionAISettingsLogic.SwitchResult.Succeeded, result);
            Assert.AreEqual(8.25f, _logic.EditingBuffer.manualOverrideTimeoutSeconds,
                "CloneConfig が manualOverrideTimeoutSeconds を伝搬していない可能性");
        }
    }
}
