using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// 仲間AI設定UIが編集したデータが、実際にレジストリへ書き戻され、
    /// 再読み込み後も同じ内容で復元できることを end-to-end で検証する統合テスト。
    ///
    /// 観点:
    ///  1. UI 層（Dialogs.cs）と同等の操作を Logic API 経由で再現
    ///  2. AIMode / ActionSlot / AIRule / AICondition / TargetFilter / ModeTransitionRule の全フィールドが永続化されるか
    ///  3. 保存→別プリセットへ切替→戻る、の流れで全データが一致するか
    /// </summary>
    [TestFixture]
    public class Integration_CompanionAISettingsFlowTests
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

        /// <summary>
        /// UI がモード詳細で編集した内容（名前・判定間隔・行動スロット・行動ルール）が
        /// 「現在の戦術」として保存され、再読み込み後も同じ内容で取得できることを確認する。
        /// </summary>
        [Test]
        public void CompanionAISettings_EditMode_ApplyToCurrent_PreservesAllFields()
        {
            // 1. モードを追加（UI の "+モード追加" に相当）
            _logic.AddModeToBuffer(new AIMode
            {
                modeName = "バランス型",
                judgeInterval = new Vector2(0.4f, 0.8f),
                defaultActionIndex = 0,
                actions = new ActionSlot[0],
                actionRules = new AIRule[0],
                targetRules = new AIRule[0],
                targetSelects = new AITargetSelect[0],
            });

            // 2. そのモードを詳細編集（UI の "モード詳細を編集" 保存ボタンと同じフロー）
            AIMode edited = new AIMode
            {
                modeName = "バランス型(編集済み)",
                judgeInterval = new Vector2(0.3f, 0.9f),
                defaultActionIndex = 1,
                actions = new ActionSlot[]
                {
                    new ActionSlot
                    {
                        execType = ActionExecType.Attack,
                        paramId = 3,
                        paramValue = 0f,
                        displayName = "通常斬り",
                    },
                    new ActionSlot
                    {
                        execType = ActionExecType.Instant,
                        paramId = (int)InstantAction.UseItem,
                        paramValue = 42f,  // itemId=42 (HP回復薬想定)
                        displayName = "HP回復薬",
                    },
                    new ActionSlot
                    {
                        execType = ActionExecType.Sustained,
                        paramId = (int)SustainedAction.Guard,
                        paramValue = 3.5f,
                        displayName = "ガード",
                    },
                },
                actionRules = new AIRule[]
                {
                    new AIRule
                    {
                        actionIndex = 1,
                        probability = 80,
                        conditions = new AICondition[]
                        {
                            new AICondition
                            {
                                conditionType = AIConditionType.HpRatio,
                                compareOp = CompareOp.LessEqual,
                                operandA = 30,  // 0-100% 内部表現
                                operandB = 0,
                                filter = new TargetFilter
                                {
                                    belong = CharacterBelong.Ally,
                                    feature = CharacterFeature.Player,
                                    weakPoint = Element.Fire | Element.Thunder,
                                    distanceRange = new Vector2(0f, 10f),
                                    includeSelf = true,
                                    filterFlags = FilterBitFlag.FeatureAnd | FilterBitFlag.WeakPointAnd,
                                },
                            },
                        },
                    },
                    new AIRule
                    {
                        actionIndex = 2,
                        probability = 100,
                        conditions = new AICondition[]
                        {
                            new AICondition
                            {
                                conditionType = AIConditionType.SelfActState,
                                compareOp = CompareOp.Equal,
                                operandA = (int)ActState.Attacking,
                                operandB = 0,
                                filter = new TargetFilter { includeSelf = true },
                            },
                            new AICondition
                            {
                                conditionType = AIConditionType.EventFired,
                                compareOp = CompareOp.HasAny,
                                operandA = 0b00000011, // bit0|bit1
                                operandB = 0,
                                filter = new TargetFilter { includeSelf = true },
                            },
                        },
                    },
                },
                targetRules = new AIRule[0],
                targetSelects = new AITargetSelect[0],
            };
            Assert.IsTrue(_logic.UpdateModeInBuffer(0, edited));

            // 3. 遷移ルールを追加（UI の "遷移ルールを追加"）
            ModeTransitionRule[] rules = new ModeTransitionRule[]
            {
                new ModeTransitionRule
                {
                    sourceModeIndex = -1,
                    targetModeIndex = 0,
                    conditions = new AICondition[]
                    {
                        new AICondition
                        {
                            conditionType = AIConditionType.Distance,
                            compareOp = CompareOp.Greater,
                            operandA = 15,
                            operandB = 0,
                            filter = new TargetFilter
                            {
                                belong = CharacterBelong.Enemy,
                                distanceRange = new Vector2(0f, 30f),
                            },
                        },
                    },
                },
            };
            _logic.SetTransitionRulesInBuffer(rules);

            // 4. 「保存」（現在の戦術に反映）
            Assert.IsTrue(_logic.IsDirty, "編集後は Dirty");
            _logic.ApplyBufferToCurrentTactic();
            Assert.IsFalse(_logic.IsDirty, "保存後は Dirty クリア");

            // 5. 別プリセットを作って編集対象を切替 → 戻す（UI 上のプリセット切替操作）
            string otherId = _tacticalRegistry.Save("別戦術", new CompanionAIConfig
            {
                configName = "別戦術",
                modes = new AIMode[0],
                modeTransitionRules = new ModeTransitionRule[0],
                shortcutModeBindings = new int[4],
            });
            CompanionAISettingsLogic.SwitchResult switched = _logic.SwitchEditingTarget(otherId, force: false);
            Assert.AreEqual(CompanionAISettingsLogic.SwitchResult.Succeeded, switched);
            _logic.SwitchEditingTarget(null, force: false);  // 現在の戦術へ戻す

            // 6. 再読み込み後のバッファを検証
            CompanionAIConfig reloaded = _logic.EditingBuffer;
            Assert.AreEqual(1, reloaded.modes.Length);

            AIMode reloadedMode = reloaded.modes[0];
            Assert.AreEqual("バランス型(編集済み)", reloadedMode.modeName);
            Assert.AreEqual(new Vector2(0.3f, 0.9f), reloadedMode.judgeInterval);
            Assert.AreEqual(1, reloadedMode.defaultActionIndex);

            // 行動スロット (3種の execType を横断検証)
            Assert.AreEqual(3, reloadedMode.actions.Length);
            Assert.AreEqual(ActionExecType.Attack, reloadedMode.actions[0].execType);
            Assert.AreEqual(3, reloadedMode.actions[0].paramId);
            Assert.AreEqual("通常斬り", reloadedMode.actions[0].displayName);

            Assert.AreEqual(ActionExecType.Instant, reloadedMode.actions[1].execType);
            Assert.AreEqual((int)InstantAction.UseItem, reloadedMode.actions[1].paramId);
            Assert.AreEqual(42f, reloadedMode.actions[1].paramValue, "UseItem の itemId は paramValue で保持");
            Assert.AreEqual("HP回復薬", reloadedMode.actions[1].displayName);

            Assert.AreEqual(ActionExecType.Sustained, reloadedMode.actions[2].execType);
            Assert.AreEqual((int)SustainedAction.Guard, reloadedMode.actions[2].paramId);
            Assert.AreEqual(3.5f, reloadedMode.actions[2].paramValue, "Sustained の timeout も paramValue");

            // 行動ルール（AICondition / TargetFilter の全フィールドが保持されるか）
            Assert.AreEqual(2, reloadedMode.actionRules.Length);

            AIRule rule0 = reloadedMode.actionRules[0];
            Assert.AreEqual(1, rule0.actionIndex);
            Assert.AreEqual((byte)80, rule0.probability);
            Assert.AreEqual(1, rule0.conditions.Length);
            AICondition cond0 = rule0.conditions[0];
            Assert.AreEqual(AIConditionType.HpRatio, cond0.conditionType);
            Assert.AreEqual(CompareOp.LessEqual, cond0.compareOp);
            Assert.AreEqual(30, cond0.operandA);
            Assert.AreEqual(CharacterBelong.Ally, cond0.filter.belong);
            Assert.AreEqual(CharacterFeature.Player, cond0.filter.feature);
            Assert.AreEqual(Element.Fire | Element.Thunder, cond0.filter.weakPoint);
            Assert.AreEqual(new Vector2(0f, 10f), cond0.filter.distanceRange);
            Assert.IsTrue(cond0.filter.includeSelf);
            Assert.IsTrue((cond0.filter.filterFlags & FilterBitFlag.FeatureAnd) != 0);
            Assert.IsTrue((cond0.filter.filterFlags & FilterBitFlag.WeakPointAnd) != 0);

            AIRule rule1 = reloadedMode.actionRules[1];
            Assert.AreEqual(2, rule1.conditions.Length);
            Assert.AreEqual(AIConditionType.SelfActState, rule1.conditions[0].conditionType);
            Assert.AreEqual(CompareOp.Equal, rule1.conditions[0].compareOp);
            Assert.AreEqual((int)ActState.Attacking, rule1.conditions[0].operandA);
            Assert.AreEqual(AIConditionType.EventFired, rule1.conditions[1].conditionType);
            Assert.AreEqual(CompareOp.HasAny, rule1.conditions[1].compareOp);
            Assert.AreEqual(0b00000011, rule1.conditions[1].operandA);

            // 遷移ルール
            Assert.AreEqual(1, reloaded.modeTransitionRules.Length);
            ModeTransitionRule reloadedTrans = reloaded.modeTransitionRules[0];
            Assert.AreEqual(-1, reloadedTrans.sourceModeIndex, "'(任意のモード)' = -1");
            Assert.AreEqual(0, reloadedTrans.targetModeIndex);
            Assert.AreEqual(1, reloadedTrans.conditions.Length);
            Assert.AreEqual(AIConditionType.Distance, reloadedTrans.conditions[0].conditionType);
            Assert.AreEqual(CompareOp.Greater, reloadedTrans.conditions[0].compareOp);
            Assert.AreEqual(15, reloadedTrans.conditions[0].operandA);
            Assert.AreEqual(CharacterBelong.Enemy, reloadedTrans.conditions[0].filter.belong);
        }

        /// <summary>
        /// UI で新規プリセット保存→再選択した時の end-to-end フロー。
        /// SaveBufferAsNewPreset で登録された内容が _tacticalRegistry に永続化され、
        /// 別プリセット切替後に再選択しても同一データが戻ってくることを確認する。
        /// </summary>
        [Test]
        public void CompanionAISettings_SaveAsPreset_ReloadPreset_RoundtripsCorrectly()
        {
            // モードを1個とショートカット1個設定
            _logic.AddModeToBuffer(new AIMode
            {
                modeName = "プリセット元",
                judgeInterval = new Vector2(0.5f, 1.0f),
                defaultActionIndex = 0,
                actions = new ActionSlot[]
                {
                    new ActionSlot { execType = ActionExecType.Attack, paramId = 1, displayName = "攻撃A" },
                },
                actionRules = new AIRule[0],
                targetRules = new AIRule[0],
                targetSelects = new AITargetSelect[0],
            });

            // プリセットとして保存
            string presetId = _logic.SaveBufferAsNewPreset("お気に入り戦術");
            Assert.IsNotNull(presetId);
            Assert.IsFalse(_logic.IsDirty, "保存後は Dirty クリア");
            Assert.AreEqual(presetId, _logic.EditingConfigId);

            // 別プリセットを作って一旦そちらを編集
            string otherId = _tacticalRegistry.Save("他の戦術", new CompanionAIConfig
            {
                configName = "他の戦術",
                modes = new AIMode[0],
                modeTransitionRules = new ModeTransitionRule[0],
                shortcutModeBindings = new int[4],
            });
            _logic.SwitchEditingTarget(otherId, force: true);
            Assert.AreEqual(otherId, _logic.EditingConfigId);

            // 元のプリセットに戻って内容検証
            _logic.SwitchEditingTarget(presetId, force: false);
            CompanionAIConfig reloaded = _logic.EditingBuffer;
            Assert.AreEqual("お気に入り戦術", reloaded.configName);
            Assert.AreEqual(1, reloaded.modes.Length);
            Assert.AreEqual("プリセット元", reloaded.modes[0].modeName);
            Assert.AreEqual(1, reloaded.modes[0].actions.Length);
            Assert.AreEqual("攻撃A", reloaded.modes[0].actions[0].displayName);
        }

        /// <summary>
        /// 編集バッファで削除した行動/条件が実際にデータから消えることを確認する
        /// （RebuildConditionList の getter/setter 方式で修正した箇所の後段検証）。
        /// </summary>
        [Test]
        public void CompanionAISettings_RemoveConditionAndAction_ActuallyRemovesFromData()
        {
            _logic.AddModeToBuffer(new AIMode
            {
                modeName = "削除検証",
                judgeInterval = new Vector2(0.5f, 1.0f),
                actions = new ActionSlot[]
                {
                    new ActionSlot { execType = ActionExecType.Attack, paramId = 0, displayName = "A" },
                    new ActionSlot { execType = ActionExecType.Attack, paramId = 1, displayName = "B" },
                    new ActionSlot { execType = ActionExecType.Attack, paramId = 2, displayName = "C" },
                },
                actionRules = new AIRule[0],
                targetRules = new AIRule[0],
                targetSelects = new AITargetSelect[0],
            });

            // 行動を1個削除した状態を UpdateModeInBuffer で反映
            AIMode working = _logic.EditingBuffer.modes[0];
            ActionSlot[] shorter = new ActionSlot[]
            {
                working.actions[0],
                working.actions[2],
            };
            working.actions = shorter;
            Assert.IsTrue(_logic.UpdateModeInBuffer(0, working));

            // 保存 → 切替 → 戻す
            _logic.ApplyBufferToCurrentTactic();
            string otherId = _tacticalRegistry.Save("他", new CompanionAIConfig
            {
                configName = "他",
                modes = new AIMode[0],
                modeTransitionRules = new ModeTransitionRule[0],
                shortcutModeBindings = new int[4],
            });
            _logic.SwitchEditingTarget(otherId, force: true);
            _logic.SwitchEditingTarget(null, force: false);

            AIMode reloaded = _logic.EditingBuffer.modes[0];
            Assert.AreEqual(2, reloaded.actions.Length);
            Assert.AreEqual("A", reloaded.actions[0].displayName);
            Assert.AreEqual("C", reloaded.actions[1].displayName);
        }
    }
}
