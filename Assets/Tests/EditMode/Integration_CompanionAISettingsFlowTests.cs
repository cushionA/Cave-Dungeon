using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Game.Core;
using Game.Runtime;

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

        // =========================================================================
        // UI-level 削除フローの回帰テスト
        // RebuildActionSlotList / RebuildActionRuleList が struct 値渡しで削除を
        // 伝播できていなかったバグの回帰テスト。UI 相当の closure 方式で
        // 削除ボタンを模倣する。
        // =========================================================================

        /// <summary>
        /// RebuildActionSlotList で使う getter/setter closure パターンを模倣して、
        /// 削除操作が呼び出し元の working.actions にも反映されることを確認する。
        /// </summary>
        [Test]
        public void CompanionAISettings_UILike_ActionSlotRemoval_PropagatesThroughSetter()
        {
            AIMode working = new AIMode
            {
                modeName = "UI模倣",
                actions = new ActionSlot[]
                {
                    new ActionSlot { execType = ActionExecType.Attack, paramId = 0, displayName = "A" },
                    new ActionSlot { execType = ActionExecType.Attack, paramId = 1, displayName = "B" },
                    new ActionSlot { execType = ActionExecType.Attack, paramId = 2, displayName = "C" },
                },
                actionRules = new AIRule[0],
                targetRules = new AIRule[0],
                targetSelects = new AITargetSelect[0],
            };

            // UI 側で RebuildActionSlotList が使うのと同じ getter/setter closure を再現
            System.Func<ActionSlot[]> getActions = () => working.actions;
            System.Action<ActionSlot[]> setActions = arr => working.actions = arr;

            // インデックス 1 (B) を削除するクロージャ (削除ボタンの RegisterCallback 相当)
            System.Action remove1 = () =>
            {
                ActionSlot[] arr = getActions();
                ActionSlot[] newArr = new ActionSlot[arr.Length - 1];
                int dst = 0;
                for (int k = 0; k < arr.Length; k++)
                {
                    if (k == 1)
                    {
                        continue;
                    }
                    newArr[dst++] = arr[k];
                }
                setActions(newArr);
            };

            Assert.AreEqual(3, working.actions.Length, "初期状態: 3個");
            remove1();
            Assert.AreEqual(2, working.actions.Length, "削除後は 2個");
            Assert.AreEqual("A", working.actions[0].displayName);
            Assert.AreEqual("C", working.actions[1].displayName);
        }

        /// <summary>
        /// RebuildActionRuleList の getter/setter 化の回帰テスト。
        /// 削除が呼び出し元の actionRules へ伝播することを確認。
        /// </summary>
        [Test]
        public void CompanionAISettings_UILike_ActionRuleRemoval_PropagatesThroughSetter()
        {
            AIMode working = new AIMode
            {
                modeName = "ルール削除検証",
                actions = new ActionSlot[0],
                actionRules = new AIRule[]
                {
                    new AIRule { actionIndex = 0, probability = 100 },
                    new AIRule { actionIndex = 1, probability = 80 },
                    new AIRule { actionIndex = 2, probability = 50 },
                },
                targetRules = new AIRule[0],
                targetSelects = new AITargetSelect[0],
            };

            System.Func<AIRule[]> getRules = () => working.actionRules;
            System.Action<AIRule[]> setRules = arr => working.actionRules = arr;

            // インデックス 0 を削除
            System.Action removeFirst = () =>
            {
                AIRule[] arr = getRules();
                AIRule[] newArr = new AIRule[arr.Length - 1];
                for (int i = 1; i < arr.Length; i++)
                {
                    newArr[i - 1] = arr[i];
                }
                setRules(newArr);
            };

            Assert.AreEqual(3, working.actionRules.Length);
            removeFirst();
            Assert.AreEqual(2, working.actionRules.Length);
            Assert.AreEqual(1, working.actionRules[0].actionIndex);
            Assert.AreEqual((byte)80, working.actionRules[0].probability);
            Assert.AreEqual(2, working.actionRules[1].actionIndex);
        }

        /// <summary>
        /// Ratio + InRange は UI がスライダーベースで 2値入力できないため非対応。
        /// Logic 層で保存可能でも、UI を通して読み戻す時には矯正される設計 (NormalizeCompareOp)。
        /// このテストでは直接的には Logic レベルでのラウンドトリップのみ確認する
        /// (UI-level の矯正は ConditionTypeMetadata_Ratio_DoesNotSupportInRange_BySpec 側で別途検証)。
        /// </summary>
        [Test]
        public void CompanionAISettings_RatioInRange_StoredAsIsAtLogicLayer_UINormalizesLater()
        {
            // Logic 層は InRange を弾かない（UI 層の責務）
            _logic.AddModeToBuffer(new AIMode
            {
                modeName = "Ratio+InRange検証",
                actions = new ActionSlot[]
                {
                    new ActionSlot { execType = ActionExecType.Attack, paramId = 0, displayName = "攻撃" },
                },
                actionRules = new AIRule[]
                {
                    new AIRule
                    {
                        actionIndex = 0,
                        probability = 100,
                        conditions = new AICondition[]
                        {
                            new AICondition
                            {
                                conditionType = AIConditionType.HpRatio,
                                compareOp = CompareOp.InRange,  // 通常UIでは選択不可、Logic保存のみ
                                operandA = 20,
                                operandB = 80,
                            },
                        },
                    },
                },
                targetRules = new AIRule[0],
                targetSelects = new AITargetSelect[0],
            });

            _logic.ApplyBufferToCurrentTactic();

            // Logic 層では値そのまま
            AICondition stored = _logic.EditingBuffer.modes[0].actionRules[0].conditions[0];
            Assert.AreEqual(CompareOp.InRange, stored.compareOp,
                "Logic 層は制限しない(UI 層の NormalizeCompareOp で描画時に矯正される)");
        }

        /// <summary>
        /// InRange CompareOp を使った条件が保存→再読込でラウンドトリップできることを確認 (Integer の場合)。
        /// Ratio ではなく Distance (Integer) で検証する。
        /// </summary>
        [Test]
        public void CompanionAISettings_InRangeCondition_RoundtripsCorrectly()
        {
            _logic.AddModeToBuffer(new AIMode
            {
                modeName = "InRange検証",
                actions = new ActionSlot[]
                {
                    new ActionSlot { execType = ActionExecType.Attack, paramId = 0, displayName = "攻撃" },
                },
                actionRules = new AIRule[]
                {
                    new AIRule
                    {
                        actionIndex = 0,
                        probability = 100,
                        conditions = new AICondition[]
                        {
                            new AICondition
                            {
                                conditionType = AIConditionType.Distance,
                                compareOp = CompareOp.InRange,  // 5m <= 距離 <= 15m
                                operandA = 5,
                                operandB = 15,
                                filter = new TargetFilter { includeSelf = false },
                            },
                        },
                    },
                },
                targetRules = new AIRule[0],
                targetSelects = new AITargetSelect[0],
            });

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

            AICondition reloaded = _logic.EditingBuffer.modes[0].actionRules[0].conditions[0];
            Assert.AreEqual(CompareOp.InRange, reloaded.compareOp, "InRange が保持される");
            Assert.AreEqual(5, reloaded.operandA, "下限");
            Assert.AreEqual(15, reloaded.operandB, "上限");
        }

        /// <summary>
        /// targetRules / targetSelects が UI で編集後、保存 → 別プリセット切替 → 戻す
        /// の流れでも全フィールドが永続化されることを確認する。
        /// </summary>
        [Test]
        public void CompanionAISettings_EditMode_TargetRulesAndSelects_Roundtrip()
        {
            _logic.AddModeToBuffer(new AIMode
            {
                modeName = "ターゲット戦術",
                judgeInterval = new Vector2(0.3f, 0.7f),
                actions = new ActionSlot[0],
                actionRules = new AIRule[0],
                targetRules = new AIRule[0],
                targetSelects = new AITargetSelect[0],
            });

            AIMode edited = new AIMode
            {
                modeName = "ターゲット戦術(編集済み)",
                judgeInterval = new Vector2(0.3f, 0.7f),
                defaultActionIndex = 0,
                actions = new ActionSlot[0],
                actionRules = new AIRule[0],
                targetSelects = new AITargetSelect[]
                {
                    new AITargetSelect
                    {
                        sortKey = TargetSortKey.Distance,
                        isDescending = false,
                        filter = new TargetFilter
                        {
                            belong = CharacterBelong.Enemy,
                            includeSelf = false,
                        },
                    },
                    new AITargetSelect
                    {
                        sortKey = TargetSortKey.HpRatio,
                        elementFilter = Element.Fire | Element.Thunder,
                        isDescending = false,
                        filter = new TargetFilter
                        {
                            belong = CharacterBelong.Enemy,
                            feature = CharacterFeature.Boss,
                            weakPoint = Element.Fire,
                            distanceRange = new Vector2(0f, 20f),
                            filterFlags = FilterBitFlag.FeatureAnd,
                            includeSelf = false,
                        },
                    },
                },
                targetRules = new AIRule[]
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
                                operandA = 30,
                                operandB = 0,
                                filter = new TargetFilter { includeSelf = true },
                            },
                        },
                    },
                    new AIRule
                    {
                        actionIndex = 0,
                        probability = 100,
                        conditions = new AICondition[0],
                    },
                },
            };
            Assert.IsTrue(_logic.UpdateModeInBuffer(0, edited));
            _logic.ApplyBufferToCurrentTactic();

            // 別プリセットへ切替 → 戻すで再読み込みを再現
            string otherId = _tacticalRegistry.Save("別戦術", new CompanionAIConfig
            {
                configName = "別戦術",
                modes = new AIMode[0],
                modeTransitionRules = new ModeTransitionRule[0],
                shortcutModeBindings = new int[4],
            });
            _logic.SwitchEditingTarget(otherId, force: false);
            _logic.SwitchEditingTarget(null, force: false);

            AIMode reloaded = _logic.EditingBuffer.modes[0];

            // targetSelects 全フィールド
            Assert.AreEqual(2, reloaded.targetSelects.Length);

            Assert.AreEqual(TargetSortKey.Distance, reloaded.targetSelects[0].sortKey);
            Assert.IsFalse(reloaded.targetSelects[0].isDescending);
            Assert.AreEqual(CharacterBelong.Enemy, reloaded.targetSelects[0].filter.belong);
            Assert.IsFalse(reloaded.targetSelects[0].filter.includeSelf);

            Assert.AreEqual(TargetSortKey.HpRatio, reloaded.targetSelects[1].sortKey);
            Assert.AreEqual(Element.Fire | Element.Thunder, reloaded.targetSelects[1].elementFilter);
            Assert.AreEqual(CharacterFeature.Boss, reloaded.targetSelects[1].filter.feature);
            Assert.AreEqual(Element.Fire, reloaded.targetSelects[1].filter.weakPoint);
            Assert.AreEqual(new Vector2(0f, 20f), reloaded.targetSelects[1].filter.distanceRange);
            Assert.IsTrue((reloaded.targetSelects[1].filter.filterFlags & FilterBitFlag.FeatureAnd) != 0);

            // targetRules 全フィールド
            Assert.AreEqual(2, reloaded.targetRules.Length);

            AIRule tRule0 = reloaded.targetRules[0];
            Assert.AreEqual(1, tRule0.actionIndex);
            Assert.AreEqual((byte)80, tRule0.probability);
            Assert.AreEqual(1, tRule0.conditions.Length);
            Assert.AreEqual(AIConditionType.HpRatio, tRule0.conditions[0].conditionType);
            Assert.AreEqual(CompareOp.LessEqual, tRule0.conditions[0].compareOp);
            Assert.AreEqual(30, tRule0.conditions[0].operandA);

            AIRule tRule1 = reloaded.targetRules[1];
            Assert.AreEqual(0, tRule1.actionIndex);
            Assert.AreEqual((byte)100, tRule1.probability);
            Assert.AreEqual(0, tRule1.conditions.Length);
        }

        /// <summary>
        /// CompanionAISettingsLogic.AdjustTargetRulesForRemovedSelect を直接検証する。
        /// UI の RebuildTargetSelectList 削除ボタンは実装本体としてこのヘルパーを呼んでおり、
        /// ロジックを自前再現するのではなく純粋関数として直接テストする。
        /// </summary>
        [Test]
        public void AdjustTargetRulesForRemovedSelect_DiscardsReferencingRules_AndDecrementsHigherIndexes()
        {
            AIRule[] rules = new AIRule[]
            {
                new AIRule { actionIndex = 0, probability = 100, conditions = new AICondition[0] },
                new AIRule { actionIndex = 1, probability = 50,  conditions = new AICondition[0] },
                new AIRule { actionIndex = 2, probability = 80,  conditions = new AICondition[0] },
                new AIRule { actionIndex = 3, probability = 70,  conditions = new AICondition[0] },
            };

            AIRule[] adjusted = CompanionAISettingsLogic.AdjustTargetRulesForRemovedSelect(rules, 1);

            Assert.AreEqual(3, adjusted.Length, "actionIndex=1 を参照していたルールは破棄");
            Assert.AreEqual(0, adjusted[0].actionIndex, "元 actionIndex=0 は据え置き");
            Assert.AreEqual(1, adjusted[1].actionIndex, "元 actionIndex=2 は decrement されて 1");
            Assert.AreEqual(2, adjusted[2].actionIndex, "元 actionIndex=3 は decrement されて 2");

            // probability など他フィールドが保持されていること
            Assert.AreEqual((byte)100, adjusted[0].probability);
            Assert.AreEqual((byte)80, adjusted[1].probability);
            Assert.AreEqual((byte)70, adjusted[2].probability);
        }

        /// <summary>
        /// null / 空配列 / 参照ルール全滅 の境界ケース。
        /// </summary>
        [Test]
        public void AdjustTargetRulesForRemovedSelect_HandlesEdgeCases()
        {
            // null 入力 → 空配列
            AIRule[] fromNull = CompanionAISettingsLogic.AdjustTargetRulesForRemovedSelect(null, 0);
            Assert.IsNotNull(fromNull);
            Assert.AreEqual(0, fromNull.Length);

            // 空配列 → 空配列
            AIRule[] fromEmpty = CompanionAISettingsLogic.AdjustTargetRulesForRemovedSelect(new AIRule[0], 0);
            Assert.AreEqual(0, fromEmpty.Length);

            // 全ルールが削除 idx を参照 → 全部破棄で空配列
            AIRule[] allReferencing = new AIRule[]
            {
                new AIRule { actionIndex = 2, probability = 100, conditions = new AICondition[0] },
                new AIRule { actionIndex = 2, probability = 50,  conditions = new AICondition[0] },
            };
            AIRule[] afterAllRemoved = CompanionAISettingsLogic.AdjustTargetRulesForRemovedSelect(allReferencing, 2);
            Assert.AreEqual(0, afterAllRemoved.Length);

            // 削除 idx より小さい index のルールのみ → 変更なし
            AIRule[] smallerOnly = new AIRule[]
            {
                new AIRule { actionIndex = 0, probability = 100, conditions = new AICondition[0] },
            };
            AIRule[] afterSmallerOnly = CompanionAISettingsLogic.AdjustTargetRulesForRemovedSelect(smallerOnly, 5);
            Assert.AreEqual(1, afterSmallerOnly.Length);
            Assert.AreEqual(0, afterSmallerOnly[0].actionIndex);
        }

        // =========================================================================
        // F2: TargetFilter 自動クリア回帰テスト
        //
        // BuildTargetFilterSection は IsSelf / IsPlayer ビットを UI 上扱わない方針で、
        // 描画タイミングで必ずクリアする。古い（bit が立ったまま保存されていた）Config
        // を読み込んだ直後の描画で自動クリアされ、保存後に残らないことを検証する。
        // =========================================================================

        /// <summary>
        /// BuildTargetFilterSection のクリア相当のロジック（filterFlags から IsSelf/IsPlayer を落とす）。
        /// UI Toolkit の VisualElement を構築しないロジックコア部だけ再現する。
        /// </summary>
        private static TargetFilter SimulateBuildTargetFilterSection_AutoClear(TargetFilter initial, out bool notifiedChange)
        {
            TargetFilter current = initial;
            FilterBitFlag beforeClear = current.filterFlags;
            current.filterFlags &= ~(FilterBitFlag.IsSelf | FilterBitFlag.IsPlayer);
            notifiedChange = beforeClear != current.filterFlags;
            return current;
        }

        [Test]
        public void TargetFilter_WithIsSelfBit_AutoClearedOnFoldoutBuild()
        {
            TargetFilter filter = new TargetFilter
            {
                belong = CharacterBelong.Ally,
                filterFlags = FilterBitFlag.IsSelf | FilterBitFlag.FeatureAnd,
            };

            TargetFilter cleared = SimulateBuildTargetFilterSection_AutoClear(filter, out bool notified);

            Assert.IsTrue(notified, "bit が立っていた場合は onChanged 相当が通知される");
            Assert.AreEqual(FilterBitFlag.FeatureAnd, cleared.filterFlags & FilterBitFlag.FeatureAnd);
            Assert.AreEqual(FilterBitFlag.None, cleared.filterFlags & FilterBitFlag.IsSelf);
            Assert.AreEqual(FilterBitFlag.None, cleared.filterFlags & FilterBitFlag.IsPlayer);
        }

        [Test]
        public void TargetFilter_WithIsPlayerBit_AutoClearedOnFoldoutBuild()
        {
            TargetFilter filter = new TargetFilter
            {
                filterFlags = FilterBitFlag.IsPlayer | FilterBitFlag.BelongAnd,
            };

            TargetFilter cleared = SimulateBuildTargetFilterSection_AutoClear(filter, out bool notified);

            Assert.IsTrue(notified);
            Assert.AreEqual(FilterBitFlag.None, cleared.filterFlags & FilterBitFlag.IsPlayer);
            Assert.IsTrue((cleared.filterFlags & FilterBitFlag.BelongAnd) != 0, "他ビットは保持される");
        }

        [Test]
        public void TargetFilter_WithBothIsSelfAndIsPlayer_AutoClearedOnFoldoutBuild()
        {
            TargetFilter filter = new TargetFilter
            {
                filterFlags = FilterBitFlag.IsSelf | FilterBitFlag.IsPlayer | FilterBitFlag.WeakPointAnd,
            };

            TargetFilter cleared = SimulateBuildTargetFilterSection_AutoClear(filter, out bool notified);

            Assert.IsTrue(notified);
            Assert.AreEqual(FilterBitFlag.None, cleared.filterFlags & FilterBitFlag.IsSelf);
            Assert.AreEqual(FilterBitFlag.None, cleared.filterFlags & FilterBitFlag.IsPlayer);
            Assert.IsTrue((cleared.filterFlags & FilterBitFlag.WeakPointAnd) != 0);
        }

        [Test]
        public void TargetFilter_WithoutIsSelfOrIsPlayer_DoesNotMarkDirty()
        {
            TargetFilter filter = new TargetFilter
            {
                filterFlags = FilterBitFlag.BelongAnd | FilterBitFlag.FeatureAnd,
            };

            TargetFilter cleared = SimulateBuildTargetFilterSection_AutoClear(filter, out bool notified);

            Assert.IsFalse(notified, "既にクリーンなデータでは不要な Dirty 化を避ける（onChanged 不呼び出し）");
            Assert.AreEqual(filter.filterFlags, cleared.filterFlags, "他ビットが変わらない");
        }

        [Test]
        public void TargetFilter_AfterRoundtripThroughRegistry_BitsCleared()
        {
            // 古い Config: IsSelf bit が立ったまま保存されていたケースを模倣
            _logic.AddModeToBuffer(new AIMode
            {
                modeName = "旧データ",
                actions = new ActionSlot[0],
                actionRules = new AIRule[]
                {
                    new AIRule
                    {
                        actionIndex = 0,
                        probability = 100,
                        conditions = new AICondition[]
                        {
                            new AICondition
                            {
                                conditionType = AIConditionType.Distance,
                                compareOp = CompareOp.LessEqual,
                                operandA = 10,
                                filter = new TargetFilter
                                {
                                    belong = CharacterBelong.Enemy,
                                    filterFlags = FilterBitFlag.IsSelf | FilterBitFlag.IsPlayer | FilterBitFlag.BelongAnd,
                                },
                            },
                        },
                    },
                },
                targetRules = new AIRule[0],
                targetSelects = new AITargetSelect[0],
            });

            // 「UI で Foldout が構築された」相当のシミュレーション:
            // BuildTargetFilterSection と同じロジックでビットをクリアし、条件のフィルターを差し替えて保存
            AIMode working = _logic.EditingBuffer.modes[0];
            AICondition cond = working.actionRules[0].conditions[0];
            cond.filter = SimulateBuildTargetFilterSection_AutoClear(cond.filter, out _);
            working.actionRules[0].conditions[0] = cond;
            Assert.IsTrue(_logic.UpdateModeInBuffer(0, working));

            _logic.ApplyBufferToCurrentTactic();

            // 別プリセット→戻すで再読み込み
            string otherId = _tacticalRegistry.Save("別", new CompanionAIConfig
            {
                configName = "別",
                modes = new AIMode[0],
                modeTransitionRules = new ModeTransitionRule[0],
                shortcutModeBindings = new int[4],
            });
            _logic.SwitchEditingTarget(otherId, force: false);
            _logic.SwitchEditingTarget(null, force: false);

            TargetFilter reloaded = _logic.EditingBuffer.modes[0].actionRules[0].conditions[0].filter;
            Assert.AreEqual(FilterBitFlag.None, reloaded.filterFlags & FilterBitFlag.IsSelf,
                "IsSelf bit は保存後に残らない");
            Assert.AreEqual(FilterBitFlag.None, reloaded.filterFlags & FilterBitFlag.IsPlayer,
                "IsPlayer bit は保存後に残らない");
            Assert.IsTrue((reloaded.filterFlags & FilterBitFlag.BelongAnd) != 0,
                "他ビットは保持される");
        }

        // =========================================================================
        // F3: CompanionAISettingsController.RebuildUnifiedActionList 直接呼び出し
        //
        // InternalsVisibleTo により internal になった RebuildUnifiedActionList を
        // VisualElement コンテナ + getter/setter closure 経由で直接呼ぶ。
        // 既存 (MonoBehaviour) インスタンスを生成し、UI 層ヘルパーの挙動を
        // 実コード経路で実行できることの最小回帰テスト。
        // =========================================================================

        [Test]
        public void RebuildUnifiedActionList_WithRules_PopulatesContainer()
        {
            GameObject go = new GameObject("CompanionAISettingsControllerForTest");
            go.AddComponent<UnityEngine.UIElements.UIDocument>();
            CompanionAISettingsController controller = go.AddComponent<CompanionAISettingsController>();

            try
            {
                AIMode working = new AIMode
                {
                    modeName = "RebuildUnified直接呼び出し",
                    actions = new ActionSlot[]
                    {
                        new ActionSlot { execType = ActionExecType.Attack, paramId = 0, displayName = "攻撃A" },
                    },
                    actionRules = new AIRule[]
                    {
                        new AIRule { actionIndex = 0, probability = 100, conditions = new AICondition[0] },
                        new AIRule { actionIndex = 0, probability = 60,  conditions = new AICondition[0] },
                    },
                    targetRules = new AIRule[0],
                    targetSelects = new AITargetSelect[0],
                };

                VisualElement container = new VisualElement();
                System.Action rebuild = null;
                rebuild = () => controller.RebuildUnifiedActionList(
                    container,
                    () => working,
                    m => working = m,
                    rebuild);

                rebuild();

                // ルール行(2) + デフォルト行(1) で少なくとも 3 子要素が描画される
                Assert.GreaterOrEqual(container.childCount, 3,
                    "ルール 2 件 + デフォルト行で 3 要素以上が追加される");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void RebuildUnifiedActionList_WithEmptyRulesAndActions_ShowsEmptyLabel()
        {
            GameObject go = new GameObject("CompanionAISettingsControllerForTest_Empty");
            go.AddComponent<UnityEngine.UIElements.UIDocument>();
            CompanionAISettingsController controller = go.AddComponent<CompanionAISettingsController>();

            try
            {
                AIMode working = new AIMode
                {
                    modeName = "空状態",
                    actions = new ActionSlot[0],
                    actionRules = new AIRule[0],
                    targetRules = new AIRule[0],
                    targetSelects = new AITargetSelect[0],
                };

                VisualElement container = new VisualElement();
                System.Action rebuild = null;
                rebuild = () => controller.RebuildUnifiedActionList(
                    container,
                    () => working,
                    m => working = m,
                    rebuild);

                rebuild();

                // 空時は empty label のみが追加される
                Assert.AreEqual(1, container.childCount, "空時は empty label 1 個");
                Assert.IsInstanceOf<Label>(container[0], "empty state は Label");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void RebuildUnifiedActionList_AfterDeletionViaSetter_RedrawsShorterList()
        {
            // getter/setter closure 経由で actionRules を書き換え、再描画で件数が反映されるか
            GameObject go = new GameObject("CompanionAISettingsControllerForTest_Delete");
            go.AddComponent<UnityEngine.UIElements.UIDocument>();
            CompanionAISettingsController controller = go.AddComponent<CompanionAISettingsController>();

            try
            {
                AIMode working = new AIMode
                {
                    modeName = "削除シミュ",
                    actions = new ActionSlot[]
                    {
                        new ActionSlot { execType = ActionExecType.Attack, paramId = 0, displayName = "A" },
                    },
                    actionRules = new AIRule[]
                    {
                        new AIRule { actionIndex = 0, probability = 100, conditions = new AICondition[0] },
                        new AIRule { actionIndex = 0, probability = 50,  conditions = new AICondition[0] },
                        new AIRule { actionIndex = 0, probability = 80,  conditions = new AICondition[0] },
                    },
                    targetRules = new AIRule[0],
                    targetSelects = new AITargetSelect[0],
                };

                VisualElement container = new VisualElement();
                System.Action rebuild = null;
                rebuild = () => controller.RebuildUnifiedActionList(
                    container,
                    () => working,
                    m => working = m,
                    rebuild);

                rebuild();
                int initialCount = container.childCount;
                Assert.GreaterOrEqual(initialCount, 4,
                    "ルール 3 + デフォルト行で 4 要素以上");

                // setter 経由で 1 件削除（Button.clickable シミュレート相当）
                AIRule[] shorter = new AIRule[]
                {
                    working.actionRules[0],
                    working.actionRules[2],
                };
                working.actionRules = shorter;

                rebuild();
                // 描画後は ルール 2 + デフォルト行 = 3 要素以上
                Assert.GreaterOrEqual(container.childCount, 3);
                Assert.Less(container.childCount, initialCount,
                    "削除後は要素数が減少する");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
