using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Game.Core;

namespace Game.Runtime
{
    /// <summary>
    /// CompanionAISettingsController の partial。
    /// モード詳細ダイアログ / モード遷移ダイアログ / 行動ピッカーポップアップ を実装。
    /// Dialog 本体は UXML に事前定義せず、すべてコード側で構築する（既存ダイアログと同じパターン）。
    /// </summary>
    public partial class CompanionAISettingsController
    {
        // =========================================================================
        // Mode Detail Dialog
        // =========================================================================

        /// <summary>
        /// モード詳細編集ダイアログを表示する。
        /// 編集用のローカルバッファを保持し、保存時に CompanionAISettingsLogic.UpdateModeInBuffer へ書き戻す。
        /// </summary>
        private void ShowModeDetailDialog(int slotIndex)
        {
            AIMode[] modes = _logic.EditingBuffer.modes;
            if (modes == null || slotIndex < 0 || slotIndex >= modes.Length)
            {
                return;
            }

            // 編集用ローカルバッファ（保存するまで実バッファには反映しない）
            AIMode working = CloneMode(modes[slotIndex]);

            VisualElement dialog = BuildModalDialog("モード詳細: " + (string.IsNullOrEmpty(working.modeName) ? "(無名)" : working.modeName), null);
            dialog.AddToClassList("mode-detail-dialog");

            // スクロール可能なコンテナ
            ScrollView scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("mode-detail-scroll");
            dialog.Add(scroll);

            // === 基本設定 ===
            VisualElement basicSection = BuildDetailSection("基本設定");
            scroll.Add(basicSection);

            TextField nameField = new TextField("モード名");
            nameField.value = working.modeName ?? "";
            nameField.AddToClassList("mode-detail-field");
            nameField.RegisterValueChangedCallback(evt => working.modeName = evt.newValue);
            basicSection.Add(nameField);

            // 判定間隔 min/max
            VisualElement intervalRow = new VisualElement();
            intervalRow.AddToClassList("mode-detail-inline-row");

            FloatField intervalMin = new FloatField("判定間隔 最小(秒)");
            intervalMin.value = working.judgeInterval.x;
            intervalMin.tooltip = "AI判定を行う最短インターバル";
            intervalMin.RegisterValueChangedCallback(evt =>
            {
                Vector2 v = working.judgeInterval;
                v.x = Mathf.Max(0f, evt.newValue);
                working.judgeInterval = v;
            });
            intervalRow.Add(intervalMin);

            FloatField intervalMax = new FloatField("判定間隔 最大(秒)");
            intervalMax.value = working.judgeInterval.y;
            intervalMax.tooltip = "AI判定を行う最長インターバル（ゆらぎ用）";
            intervalMax.RegisterValueChangedCallback(evt =>
            {
                Vector2 v = working.judgeInterval;
                v.y = Mathf.Max(v.x, evt.newValue);
                working.judgeInterval = v;
            });
            intervalRow.Add(intervalMax);
            basicSection.Add(intervalRow);

            IntegerField defaultIndexField = new IntegerField("デフォルト行動index");
            defaultIndexField.value = working.defaultActionIndex;
            defaultIndexField.tooltip = "どのルールにも合致しない場合に使用する行動のインデックス";
            defaultIndexField.RegisterValueChangedCallback(evt =>
            {
                int clamped = working.actions != null ? Mathf.Clamp(evt.newValue, 0, Mathf.Max(0, working.actions.Length - 1)) : 0;
                working.defaultActionIndex = clamped;
            });
            basicSection.Add(defaultIndexField);

            // === 行動スロット ===
            VisualElement actionsSection = BuildDetailSection("行動スロット");
            scroll.Add(actionsSection);

            VisualElement actionsList = new VisualElement();
            actionsList.AddToClassList("mode-detail-list");
            actionsSection.Add(actionsList);

            // struct 値渡しによるコピー問題を避けるため getter/setter closure を渡す。
            Action rebuildActions = null;
            rebuildActions = () => RebuildActionSlotList(
                actionsList,
                () => working.actions,
                arr => working.actions = arr,
                rebuildActions);
            rebuildActions();

            Button addActionButton = new Button(() =>
            {
                ShowActionPickerDialog(PickerTabId.Attack, picked =>
                {
                    int newCount = (working.actions != null ? working.actions.Length : 0) + 1;
                    ActionSlot[] newArr = new ActionSlot[newCount];
                    if (working.actions != null)
                    {
                        for (int i = 0; i < working.actions.Length; i++)
                        {
                            newArr[i] = working.actions[i];
                        }
                    }
                    newArr[newCount - 1] = picked;
                    working.actions = newArr;
                    rebuildActions();
                });
            });
            addActionButton.text = "＋ 行動を追加";
            addActionButton.tooltip = "新しい行動スロットを追加";
            addActionButton.AddToClassList("mode-detail-add-button");
            actionsSection.Add(addActionButton);

            // === 行動ルール ===
            VisualElement rulesSection = BuildDetailSection("行動ルール (優先度順)");
            scroll.Add(rulesSection);

            VisualElement rulesList = new VisualElement();
            rulesList.AddToClassList("mode-detail-list");
            rulesSection.Add(rulesList);

            // 削除経路が struct コピーで切れる問題を避けるため getter/setter closure を渡す。
            Action rebuildRules = null;
            rebuildRules = () => RebuildActionRuleList(
                rulesList,
                () => working.actionRules,
                arr => working.actionRules = arr,
                () => working,
                rebuildRules);
            rebuildRules();

            Button addRuleButton = new Button(() =>
            {
                AIRule newRule = new AIRule
                {
                    conditions = new AICondition[0],
                    actionIndex = 0,
                    probability = 100,
                };
                int newCount = (working.actionRules != null ? working.actionRules.Length : 0) + 1;
                AIRule[] newArr = new AIRule[newCount];
                if (working.actionRules != null)
                {
                    for (int i = 0; i < working.actionRules.Length; i++)
                    {
                        newArr[i] = working.actionRules[i];
                    }
                }
                newArr[newCount - 1] = newRule;
                working.actionRules = newArr;
                rebuildRules();
            });
            addRuleButton.text = "＋ ルールを追加";
            addRuleButton.tooltip = "新しい行動ルールを追加";
            addRuleButton.AddToClassList("mode-detail-add-button");
            rulesSection.Add(addRuleButton);

            // === ボタン列 ===
            VisualElement buttons = BuildButtonRow();
            buttons.Add(BuildDialogButton("保存", "primary-button", () =>
            {
                // modeId を空にして独立コピー扱い（参照リンクを切る安全弁）
                working.modeId = "";
                _logic.UpdateModeInBuffer(slotIndex, working);
                RefreshEditor();
                RefreshDirtyIndicator();
            }));
            buttons.Add(BuildDialogButton("キャンセル", "secondary-button", null));
            dialog.Add(buttons);

            ShowDialog(dialog);
            AttachTooltipHandlers(dialog);
        }

        /// <summary>
        /// 行動スロットリストを描画する。
        /// AIMode(struct) の値渡しだと削除時の代入が呼び出し元に伝播しないため、
        /// 呼び出し側が getter/setter の closure を渡す形にする。
        /// </summary>
        private void RebuildActionSlotList(
            VisualElement container,
            Func<ActionSlot[]> getActions,
            Action<ActionSlot[]> setActions,
            Action rebuild)
        {
            container.Clear();

            ActionSlot[] actions = getActions();
            int count = actions != null ? actions.Length : 0;
            if (count == 0)
            {
                Label empty = new Label("(行動スロットが空です)");
                empty.AddToClassList("mode-detail-empty");
                container.Add(empty);
                return;
            }

            for (int i = 0; i < count; i++)
            {
                int idx = i;
                ActionSlot slot = actions[idx];
                VisualElement row = new VisualElement();
                row.AddToClassList("mode-detail-row");

                Label indexLabel = new Label("[" + idx + "]");
                indexLabel.AddToClassList("mode-detail-row__index");
                row.Add(indexLabel);

                Label label = new Label(ResolveActionSlotLabel(slot));
                label.AddToClassList("mode-detail-row__label");
                row.Add(label);

                if (slot.execType == ActionExecType.Sustained)
                {
                    Label duration = new Label(SustainedActionMetadata.GetDurationLabel(slot.paramValue));
                    duration.tooltip = SustainedActionMetadata.GetNaturalEndCondition((SustainedAction)slot.paramId);
                    duration.AddToClassList("mode-detail-row__duration");
                    row.Add(duration);
                }

                Button editButton = new Button(() =>
                {
                    ShowActionPickerDialog(ResolveInitialTab(slot), picked =>
                    {
                        ActionSlot[] arr = getActions();
                        if (arr != null && idx < arr.Length)
                        {
                            arr[idx] = picked;
                        }
                        rebuild();
                    });
                });
                editButton.text = "変更";
                editButton.tooltip = "この行動を別の行動に差し替え";
                editButton.AddToClassList("mode-detail-row__button");
                editButton.AddToClassList("secondary-button");
                row.Add(editButton);

                Button removeButton = new Button(() =>
                {
                    ActionSlot[] arr = getActions();
                    if (arr == null || arr.Length == 0)
                    {
                        return;
                    }
                    ActionSlot[] newArr = new ActionSlot[arr.Length - 1];
                    int dst = 0;
                    for (int k = 0; k < arr.Length; k++)
                    {
                        if (k == idx)
                        {
                            continue;
                        }
                        newArr[dst++] = arr[k];
                    }
                    setActions(newArr);
                    rebuild();
                });
                removeButton.text = "×";
                removeButton.tooltip = "この行動を削除";
                removeButton.AddToClassList("mode-detail-row__button");
                removeButton.AddToClassList("danger-button");
                row.Add(removeButton);

                container.Add(row);
            }

            AttachTooltipHandlers(container);
        }

        /// <summary>
        /// 行動ルールリストを描画する。
        /// AIMode(struct) の値渡し問題を避けるため getter/setter + getMode の closure を受ける。
        /// getMode は FormatRuleSummary / ShowRuleEditDialog に「今の actions[] が乗った AIMode」を渡すために必要。
        /// </summary>
        private void RebuildActionRuleList(
            VisualElement container,
            Func<AIRule[]> getRules,
            Action<AIRule[]> setRules,
            Func<AIMode> getParentMode,
            Action rebuild)
        {
            container.Clear();

            AIRule[] rules = getRules();
            int count = rules != null ? rules.Length : 0;
            if (count == 0)
            {
                Label empty = new Label("(ルールなし - デフォルト行動が実行されます)");
                empty.AddToClassList("mode-detail-empty");
                container.Add(empty);
                return;
            }

            AIMode parentMode = getParentMode();
            for (int i = 0; i < count; i++)
            {
                int idx = i;
                AIRule rule = rules[idx];
                VisualElement row = new VisualElement();
                row.AddToClassList("mode-detail-row");

                Label priority = new Label("#" + (idx + 1));
                priority.AddToClassList("mode-detail-row__index");
                row.Add(priority);

                Label summary = new Label(FormatRuleSummary(rule, parentMode));
                summary.AddToClassList("mode-detail-row__label");
                row.Add(summary);

                Button editButton = new Button(() =>
                {
                    ShowRuleEditDialog(rule, getParentMode(), edited =>
                    {
                        AIRule[] arr = getRules();
                        if (arr != null && idx < arr.Length)
                        {
                            arr[idx] = edited;
                        }
                        rebuild();
                    });
                });
                editButton.text = "編集";
                editButton.tooltip = "このルールを編集";
                editButton.AddToClassList("mode-detail-row__button");
                editButton.AddToClassList("secondary-button");
                row.Add(editButton);

                Button removeButton = new Button(() =>
                {
                    AIRule[] arr = getRules();
                    if (arr == null || arr.Length == 0)
                    {
                        return;
                    }
                    AIRule[] newArr = new AIRule[arr.Length - 1];
                    int dst = 0;
                    for (int k = 0; k < arr.Length; k++)
                    {
                        if (k == idx)
                        {
                            continue;
                        }
                        newArr[dst++] = arr[k];
                    }
                    setRules(newArr);
                    rebuild();
                });
                removeButton.text = "×";
                removeButton.tooltip = "このルールを削除";
                removeButton.AddToClassList("mode-detail-row__button");
                removeButton.AddToClassList("danger-button");
                row.Add(removeButton);

                container.Add(row);
            }

            AttachTooltipHandlers(container);
        }

        private string FormatRuleSummary(AIRule rule, AIMode mode)
        {
            int condCount = rule.conditions != null ? rule.conditions.Length : 0;
            string condText = condCount == 0 ? "常時" : condCount + "個の条件(AND)";
            string actionName = "#" + rule.actionIndex;
            if (mode.actions != null && rule.actionIndex >= 0 && rule.actionIndex < mode.actions.Length)
            {
                actionName = "#" + rule.actionIndex + " " + ResolveActionSlotLabel(mode.actions[rule.actionIndex]);
            }
            string prob = rule.probability == 100 ? "" : " (" + rule.probability + "%)";
            return condText + " → " + actionName + prob;
        }

        // =========================================================================
        // Rule Edit Dialog (Mode Detail から呼ばれるサブダイアログ)
        // =========================================================================

        private void ShowRuleEditDialog(AIRule initialRule, AIMode parentMode, Action<AIRule> onConfirm)
        {
            AIRule working = CloneRule(initialRule);

            VisualElement dialog = BuildModalDialog("行動ルール編集", "条件はすべて AND 結合されます。ルール配列の順序が優先度（先勝ち）です。");
            dialog.AddToClassList("rule-edit-dialog");

            ScrollView scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("mode-detail-scroll");
            dialog.Add(scroll);

            // actionIndex
            VisualElement targetSection = BuildDetailSection("発動する行動");
            scroll.Add(targetSection);

            List<string> actionChoices = new List<string>();
            int actionCount = parentMode.actions != null ? parentMode.actions.Length : 0;
            for (int i = 0; i < actionCount; i++)
            {
                actionChoices.Add("#" + i + " " + ResolveActionSlotLabel(parentMode.actions[i]));
            }
            if (actionChoices.Count == 0)
            {
                actionChoices.Add("(行動スロットが空です)");
            }

            DropdownField actionDropdown = new DropdownField("行動", actionChoices, Mathf.Clamp(working.actionIndex, 0, actionChoices.Count - 1));
            actionDropdown.tooltip = "このルールが満たされた時に発動する行動スロット";
            actionDropdown.RegisterValueChangedCallback(evt =>
            {
                int newIndex = actionDropdown.index;
                if (newIndex >= 0)
                {
                    working.actionIndex = newIndex;
                }
            });
            targetSection.Add(actionDropdown);

            // probability slider
            Slider probSlider = new Slider("発動確率(%)", 0, 100);
            probSlider.value = working.probability;
            probSlider.tooltip = "ルール条件成立時にこの行動を選ぶ確率(0-100)";
            probSlider.showInputField = true;
            probSlider.RegisterValueChangedCallback(evt =>
            {
                working.probability = (byte)Mathf.Clamp(Mathf.RoundToInt(evt.newValue), 0, 100);
            });
            targetSection.Add(probSlider);

            // conditions
            VisualElement conditionsSection = BuildDetailSection("条件 (AND結合)");
            scroll.Add(conditionsSection);

            VisualElement conditionsList = new VisualElement();
            conditionsList.AddToClassList("mode-detail-list");
            conditionsSection.Add(conditionsList);

            // struct を値渡しするとコピーになり変更が伝播しないため、getter/setter closure を経由する
            Action rebuildConditions = null;
            rebuildConditions = () => RebuildConditionList(
                conditionsList,
                () => working.conditions,
                arr => working.conditions = arr,
                rebuildConditions);
            rebuildConditions();

            Button addCondButton = new Button(() =>
            {
                int newCount = (working.conditions != null ? working.conditions.Length : 0) + 1;
                AICondition[] newArr = new AICondition[newCount];
                if (working.conditions != null)
                {
                    for (int i = 0; i < working.conditions.Length; i++)
                    {
                        newArr[i] = working.conditions[i];
                    }
                }
                newArr[newCount - 1] = new AICondition
                {
                    conditionType = AIConditionType.HpRatio,
                    compareOp = CompareOp.LessEqual,
                    operandA = 50,
                    operandB = 0,
                    filter = new TargetFilter { filterFlags = FilterBitFlag.IsSelf, includeSelf = true },
                };
                working.conditions = newArr;
                rebuildConditions();
            });
            addCondButton.text = "＋ 条件を追加";
            addCondButton.AddToClassList("mode-detail-add-button");
            conditionsSection.Add(addCondButton);

            VisualElement buttons = BuildButtonRow();
            buttons.Add(BuildDialogButton("保存", "primary-button", () => onConfirm?.Invoke(working)));
            buttons.Add(BuildDialogButton("キャンセル", "secondary-button", null));
            dialog.Add(buttons);

            ShowDialog(dialog);
            AttachTooltipHandlers(dialog);
        }

        /// <summary>
        /// 条件リストを描画する。
        /// AICondition[] は struct AIRule の中にあるため値渡しでのバグを避けるため、
        /// 呼び出し側が getter/setter を渡す形にしている。
        /// </summary>
        private void RebuildConditionList(
            VisualElement container,
            Func<AICondition[]> getConditions,
            Action<AICondition[]> setConditions,
            Action rebuild)
        {
            container.Clear();

            AICondition[] conditions = getConditions();
            int count = conditions != null ? conditions.Length : 0;
            if (count == 0)
            {
                Label empty = new Label("(条件なし - 常時成立)");
                empty.AddToClassList("mode-detail-empty");
                container.Add(empty);
                return;
            }

            for (int i = 0; i < count; i++)
            {
                int idx = i;
                VisualElement row = BuildConditionRow(conditions[idx], updated =>
                {
                    AICondition[] arr = getConditions();
                    if (arr != null && idx < arr.Length)
                    {
                        arr[idx] = updated;
                    }
                });

                Button removeButton = new Button(() =>
                {
                    AICondition[] arr = getConditions();
                    if (arr == null || arr.Length == 0)
                    {
                        return;
                    }
                    AICondition[] newArr = new AICondition[arr.Length - 1];
                    int dst = 0;
                    for (int k = 0; k < arr.Length; k++)
                    {
                        if (k == idx)
                        {
                            continue;
                        }
                        newArr[dst++] = arr[k];
                    }
                    setConditions(newArr);
                    rebuild();
                });
                removeButton.text = "×";
                removeButton.tooltip = "この条件を削除";
                removeButton.AddToClassList("mode-detail-row__button");
                removeButton.AddToClassList("danger-button");
                row.Add(removeButton);

                container.Add(row);
            }

            AttachTooltipHandlers(container);
        }

        private VisualElement BuildConditionRow(AICondition initial, Action<AICondition> onChanged)
        {
            AICondition current = initial;

            VisualElement row = new VisualElement();
            row.AddToClassList("mode-detail-row");
            row.AddToClassList("condition-row");

            // 条件タイプ
            List<string> typeChoices = new List<string>();
            AIConditionType[] allTypes = (AIConditionType[])Enum.GetValues(typeof(AIConditionType));
            for (int i = 0; i < allTypes.Length; i++)
            {
                typeChoices.Add(ConditionTypeMetadata.GetLabel(allTypes[i]));
            }
            int initialTypeIndex = Array.IndexOf(allTypes, current.conditionType);
            if (initialTypeIndex < 0) initialTypeIndex = 0;

            DropdownField typeDropdown = new DropdownField("種類", typeChoices, initialTypeIndex);
            typeDropdown.tooltip = ConditionTypeMetadata.GetDescription(current.conditionType);
            typeDropdown.AddToClassList("condition-row__type");
            row.Add(typeDropdown);

            // 動的入力コンテナ
            VisualElement inputContainer = new VisualElement();
            inputContainer.AddToClassList("condition-row__inputs");
            row.Add(inputContainer);

            Action rebuildInputs = null;
            rebuildInputs = () =>
            {
                inputContainer.Clear();
                BuildConditionInputWidgets(
                    inputContainer,
                    current,
                    updated =>
                    {
                        current = updated;
                        onChanged?.Invoke(current);
                    },
                    // CompareOp が InRange ⇔ 他 に切り替わった時に上限入力欄の有無が変わるので再構築
                    rebuildInputs);
            };
            rebuildInputs();

            typeDropdown.RegisterValueChangedCallback(evt =>
            {
                int newIdx = typeDropdown.index;
                if (newIdx < 0 || newIdx >= allTypes.Length)
                {
                    return;
                }
                current.conditionType = allTypes[newIdx];
                current.operandA = ConditionTypeMetadata.GetDefaultOperandA(current.conditionType);
                current.operandB = 0;
                // 種類に応じて CompareOp も初期化（例: SelfActState は Equal、Ratio/Integer は LessEqual）
                current.compareOp = ConditionTypeMetadata.GetDefaultCompareOp(current.conditionType);
                typeDropdown.tooltip = ConditionTypeMetadata.GetDescription(current.conditionType);
                onChanged?.Invoke(current);
                rebuildInputs();
            });

            // 対象キャラクターフィルター (Foldout で折りたたみ)
            VisualElement filterSection = BuildTargetFilterSection(current.filter, updatedFilter =>
            {
                current.filter = updatedFilter;
                onChanged?.Invoke(current);
            });
            row.Add(filterSection);

            return row;
        }

        /// <summary>
        /// TargetFilter の編集 UI を Foldout で構築する。
        /// 対象の選び方（自分/プレイヤー/周囲から陣営+特徴で絞る）と距離範囲を編集可能。
        /// </summary>
        private VisualElement BuildTargetFilterSection(TargetFilter initial, Action<TargetFilter> onChanged)
        {
            TargetFilter current = initial;

            Foldout foldout = new Foldout();
            foldout.AddToClassList("target-filter-foldout");
            foldout.value = false;
            foldout.text = "対象キャラクター: " + FormatFilterSummary(current);

            Action updateSummary = () =>
            {
                foldout.text = "対象キャラクター: " + FormatFilterSummary(current);
            };

            // 陣営/特徴のチェック欄で対象を絞り込むため、"自分 / プレイヤー / 周囲" のトップレベル選択は持たない。
            // IsSelf / IsPlayer ビットは現状 TargetSelector 側で参照されていない（自己除外は includeSelf で判定）。
            // 当 UI では扱わない方針のため、保存時にクリアする。将来セマンティクスが確定したら別 UI で公開する想定。
            FilterBitFlag beforeClear = current.filterFlags;
            current.filterFlags &= ~(FilterBitFlag.IsSelf | FilterBitFlag.IsPlayer);
            if (beforeClear != current.filterFlags)
            {
                // 実際にビットが立っていた場合のみ親へ通知（既にクリーンなデータへの不要な Dirty 化を避ける）
                onChanged?.Invoke(current);
            }

            Toggle includeSelfToggle = new Toggle("自分も対象に含める");
            includeSelfToggle.tooltip = "自分自身を対象に含めるかどうか";
            includeSelfToggle.value = current.includeSelf;
            includeSelfToggle.RegisterValueChangedCallback(evt =>
            {
                current.includeSelf = evt.newValue;
                onChanged?.Invoke(current);
                updateSummary();
            });
            foldout.Add(includeSelfToggle);

            // 陣営フィルター
            VisualElement belongSection = new VisualElement();
            belongSection.AddToClassList("target-filter-subsection");
            Label belongLabel = new Label("陣営");
            belongLabel.AddToClassList("target-filter-subsection__label");
            belongSection.Add(belongLabel);

            Toggle belongAndToggle = new Toggle("全ての陣営に一致（AND）");
            belongAndToggle.tooltip = "OFF: いずれか一致 / ON: 全て一致";
            belongAndToggle.value = (current.filterFlags & FilterBitFlag.BelongAnd) != 0;
            belongAndToggle.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue)
                {
                    current.filterFlags |= FilterBitFlag.BelongAnd;
                }
                else
                {
                    current.filterFlags &= ~FilterBitFlag.BelongAnd;
                }
                onChanged?.Invoke(current);
                updateSummary();
            });
            belongSection.Add(belongAndToggle);

            VisualElement belongFlags = new VisualElement();
            belongFlags.AddToClassList("condition-row__flags");
            foreach (object v in Enum.GetValues(typeof(CharacterBelong)))
            {
                CharacterBelong belong = (CharacterBelong)v;
                if ((int)belong == 0)
                {
                    continue;
                }
                CharacterBelong captured = belong;
                Toggle t = new Toggle(belong.ToString());
                t.value = (current.belong & captured) != 0;
                t.AddToClassList("condition-row__flag-toggle");
                t.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue)
                    {
                        current.belong |= captured;
                    }
                    else
                    {
                        current.belong &= ~captured;
                    }
                    onChanged?.Invoke(current);
                    updateSummary();
                });
                belongFlags.Add(t);
            }
            belongSection.Add(belongFlags);
            foldout.Add(belongSection);

            // 特徴フィルター
            VisualElement featureSection = new VisualElement();
            featureSection.AddToClassList("target-filter-subsection");
            Label featureLabel = new Label("特徴");
            featureLabel.AddToClassList("target-filter-subsection__label");
            featureSection.Add(featureLabel);

            Toggle featureAndToggle = new Toggle("全ての特徴に一致（AND）");
            featureAndToggle.tooltip = "OFF: いずれか一致 / ON: 全て一致";
            featureAndToggle.value = (current.filterFlags & FilterBitFlag.FeatureAnd) != 0;
            featureAndToggle.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue)
                {
                    current.filterFlags |= FilterBitFlag.FeatureAnd;
                }
                else
                {
                    current.filterFlags &= ~FilterBitFlag.FeatureAnd;
                }
                onChanged?.Invoke(current);
                updateSummary();
            });
            featureSection.Add(featureAndToggle);

            VisualElement featureFlags = new VisualElement();
            featureFlags.AddToClassList("condition-row__flags");
            foreach (object v in Enum.GetValues(typeof(CharacterFeature)))
            {
                CharacterFeature feat = (CharacterFeature)v;
                if ((int)feat == 0)
                {
                    continue;
                }
                CharacterFeature captured = feat;
                Toggle t = new Toggle(feat.ToString());
                t.value = (current.feature & captured) != 0;
                t.AddToClassList("condition-row__flag-toggle");
                t.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue)
                    {
                        current.feature |= captured;
                    }
                    else
                    {
                        current.feature &= ~captured;
                    }
                    onChanged?.Invoke(current);
                    updateSummary();
                });
                featureFlags.Add(t);
            }
            featureSection.Add(featureFlags);
            foldout.Add(featureSection);

            // 弱点属性フィルター
            VisualElement weakSection = new VisualElement();
            weakSection.AddToClassList("target-filter-subsection");
            Label weakLabel = new Label("弱点属性");
            weakLabel.AddToClassList("target-filter-subsection__label");
            weakSection.Add(weakLabel);

            Toggle weakAndToggle = new Toggle("全ての属性に一致（AND）");
            weakAndToggle.tooltip = "OFF: いずれか一致 / ON: 全て一致";
            weakAndToggle.value = (current.filterFlags & FilterBitFlag.WeakPointAnd) != 0;
            weakAndToggle.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue)
                {
                    current.filterFlags |= FilterBitFlag.WeakPointAnd;
                }
                else
                {
                    current.filterFlags &= ~FilterBitFlag.WeakPointAnd;
                }
                onChanged?.Invoke(current);
                updateSummary();
            });
            weakSection.Add(weakAndToggle);

            VisualElement weakFlags = new VisualElement();
            weakFlags.AddToClassList("condition-row__flags");
            foreach (object v in Enum.GetValues(typeof(Element)))
            {
                Element elem = (Element)v;
                if ((int)elem == 0)
                {
                    continue;
                }
                Element captured = elem;
                Toggle t = new Toggle(GetElementLabel(elem));
                t.value = (current.weakPoint & captured) != 0;
                t.AddToClassList("condition-row__flag-toggle");
                t.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue)
                    {
                        current.weakPoint |= captured;
                    }
                    else
                    {
                        current.weakPoint &= ~captured;
                    }
                    onChanged?.Invoke(current);
                    updateSummary();
                });
                weakFlags.Add(t);
            }
            weakSection.Add(weakFlags);
            foldout.Add(weakSection);

            // 距離範囲
            VisualElement distSection = new VisualElement();
            distSection.AddToClassList("target-filter-subsection");
            Label distLabel = new Label("距離範囲 [m] (0,0 = 無制限)");
            distLabel.AddToClassList("target-filter-subsection__label");
            distSection.Add(distLabel);

            VisualElement distRow = new VisualElement();
            distRow.AddToClassList("mode-detail-inline-row");

            FloatField distMin = new FloatField("最小");
            distMin.value = current.distanceRange.x;
            distMin.RegisterValueChangedCallback(evt =>
            {
                UnityEngine.Vector2 r = current.distanceRange;
                r.x = Mathf.Max(0f, evt.newValue);
                current.distanceRange = r;
                onChanged?.Invoke(current);
                updateSummary();
            });
            distRow.Add(distMin);

            FloatField distMax = new FloatField("最大");
            distMax.value = current.distanceRange.y;
            distMax.RegisterValueChangedCallback(evt =>
            {
                UnityEngine.Vector2 r = current.distanceRange;
                r.y = Mathf.Max(r.x, evt.newValue);
                current.distanceRange = r;
                onChanged?.Invoke(current);
                updateSummary();
            });
            distRow.Add(distMax);
            distSection.Add(distRow);
            foldout.Add(distSection);

            return foldout;
        }

        /// <summary>
        /// TargetFilter の概要を1行で返す（Foldout ヘッダー用）。
        /// </summary>
        private static string FormatFilterSummary(TargetFilter f)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            if (f.belong != 0)
            {
                sb.Append("陣営=").Append(f.belong.ToString());
            }
            if (f.feature != 0)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append("特徴=").Append(f.feature.ToString());
            }
            if (f.weakPoint != 0)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append("弱点=").Append(f.weakPoint.ToString());
            }
            if (f.distanceRange.x > 0f || f.distanceRange.y > 0f)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append("距離=")
                  .Append(f.distanceRange.x.ToString("0.#"))
                  .Append("〜")
                  .Append(f.distanceRange.y.ToString("0.#"))
                  .Append("m");
            }
            if (f.includeSelf)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append("自分も含む");
            }
            if (sb.Length == 0)
            {
                return "条件なし（全員が対象）";
            }
            return sb.ToString();
        }

        /// <summary>
        /// Element enum の日本語表示名。
        /// </summary>
        private static string GetElementLabel(Element elem)
        {
            switch (elem)
            {
                case Element.Slash:   return "斬撃";
                case Element.Strike:  return "打撃";
                case Element.Pierce:  return "刺突";
                case Element.Fire:    return "炎";
                case Element.Thunder: return "雷";
                case Element.Light:   return "聖";
                case Element.Dark:    return "闇";
                default:              return elem.ToString();
            }
        }

        /// <summary>
        /// CompareOp が現在の WidgetKind と整合していない場合、妥当なデフォルトへ正規化する。
        /// 例: 旧データが Ratio → Bitmask に切り替わったが compareOp=LessEqual のまま、という不整合を UI で吸収する。
        /// 正規化した場合は onChanged を発火して親に通知する。
        /// </summary>
        private static void NormalizeCompareOp(ref AICondition cond, ConditionTypeMetadata.WidgetKind kind, Action<AICondition> onChanged)
        {
            bool valid = false;
            switch (kind)
            {
                case ConditionTypeMetadata.WidgetKind.Ratio:
                case ConditionTypeMetadata.WidgetKind.Integer:
                    // 数値比較: 6種 + InRange
                    valid = cond.compareOp == CompareOp.Less
                         || cond.compareOp == CompareOp.LessEqual
                         || cond.compareOp == CompareOp.Equal
                         || cond.compareOp == CompareOp.GreaterEqual
                         || cond.compareOp == CompareOp.Greater
                         || cond.compareOp == CompareOp.NotEqual
                         || cond.compareOp == CompareOp.InRange;
                    break;
                case ConditionTypeMetadata.WidgetKind.FactionFlags:
                case ConditionTypeMetadata.WidgetKind.IntegerBitmask:
                    valid = cond.compareOp == CompareOp.HasFlag
                         || cond.compareOp == CompareOp.HasAny;
                    break;
                case ConditionTypeMetadata.WidgetKind.EnumSelect:
                    valid = cond.compareOp == CompareOp.Equal
                         || cond.compareOp == CompareOp.NotEqual;
                    break;
                default:
                    valid = true;
                    break;
            }

            if (!valid)
            {
                cond.compareOp = ConditionTypeMetadata.GetDefaultCompareOp(cond.conditionType);
                onChanged?.Invoke(cond);
            }
        }

        /// <summary>
        /// 条件1行分の入力ウィジェット（比較演算子ドロップダウン + 値入力）を構築する。
        /// rebuildAfterOp は CompareOp が InRange ⇔ 他 に切り替わった時に上限入力欄の有無を
        /// 反映させるため呼び出し元で再構築する必要がある場合に渡す。
        /// </summary>
        private void BuildConditionInputWidgets(
            VisualElement container,
            AICondition current,
            Action<AICondition> onChanged,
            Action rebuildAfterOp)
        {
            ConditionTypeMetadata.WidgetKind kind = ConditionTypeMetadata.GetWidgetKind(current.conditionType);

            if (kind == ConditionTypeMetadata.WidgetKind.None)
            {
                Label none = new Label("(追加入力なし)");
                none.AddToClassList("condition-row__none");
                container.Add(none);
                return;
            }

            // 種類切替や古いデータのロードで compareOp が現在の WidgetKind と整合しない可能性があるため、
            // 描画前に正規化して UI と内部状態が乖離しないようにする。
            NormalizeCompareOp(ref current, kind, onChanged);

            // 比較演算子
            DropdownField compareDropdown = null;
            if (ConditionTypeMetadata.SupportsNumericCompare(current.conditionType))
            {
                // インデックスは CompareOp enum 値と一致させる (Less=0 ... NotEqual=5, InRange=6)
                List<string> opChoices = new List<string>
                {
                    "<", "<=", "==", ">=", ">", "!=", "範囲内",
                };
                int opIdx = (int)current.compareOp;
                if (opIdx < 0 || opIdx >= opChoices.Count)
                {
                    opIdx = 0;
                }
                compareDropdown = new DropdownField("比較", opChoices, opIdx);
                compareDropdown.tooltip = "比較方法\n ・範囲内: operandA <= 値 <= operandB の範囲判定（下限+上限の2値入力）";
                compareDropdown.AddToClassList("condition-row__op");
                compareDropdown.RegisterValueChangedCallback(evt =>
                {
                    CompareOp before = current.compareOp;
                    current.compareOp = (CompareOp)compareDropdown.index;
                    onChanged?.Invoke(current);
                    // InRange へ切替/離脱した時に上限入力欄の有無が変わるので再構築
                    bool wasRange = before == CompareOp.InRange;
                    bool isRange = current.compareOp == CompareOp.InRange;
                    if (wasRange != isRange)
                    {
                        rebuildAfterOp?.Invoke();
                    }
                });
                container.Add(compareDropdown);
            }
            else if (ConditionTypeMetadata.IsBitmask(current.conditionType))
            {
                List<string> opChoices = new List<string> { "選択を全て満たす", "選択のいずれか満たす" };
                int opIdx = current.compareOp == CompareOp.HasAny ? 1 : 0;
                compareDropdown = new DropdownField("一致条件", opChoices, opIdx);
                compareDropdown.tooltip = "チェックした項目をどう評価するか\n"
                    + " ・全て満たす: 選択した全ビットが立っている必要あり\n"
                    + " ・いずれか満たす: 選択したどれか1つでも立っていればOK";
                compareDropdown.AddToClassList("condition-row__op");
                compareDropdown.RegisterValueChangedCallback(evt =>
                {
                    current.compareOp = compareDropdown.index == 1 ? CompareOp.HasAny : CompareOp.HasFlag;
                    onChanged?.Invoke(current);
                });
                container.Add(compareDropdown);
            }
            else if (kind == ConditionTypeMetadata.WidgetKind.EnumSelect)
            {
                List<string> opChoices = new List<string> { "一致", "不一致" };
                int opIdx = current.compareOp == CompareOp.NotEqual ? 1 : 0;
                compareDropdown = new DropdownField("判定", opChoices, opIdx);
                compareDropdown.tooltip = "選択した値と評価値の一致/不一致";
                compareDropdown.AddToClassList("condition-row__op");
                compareDropdown.RegisterValueChangedCallback(evt =>
                {
                    current.compareOp = compareDropdown.index == 1 ? CompareOp.NotEqual : CompareOp.Equal;
                    onChanged?.Invoke(current);
                });
                container.Add(compareDropdown);
            }

            // 値入力
            switch (kind)
            {
                case ConditionTypeMetadata.WidgetKind.Ratio:
                {
                    // operandA は 0-100 の integer として扱う（内部表現）
                    Slider slider = new Slider("%", 0, 100);
                    slider.value = current.operandA;
                    slider.showInputField = true;
                    slider.tooltip = ConditionTypeMetadata.GetDescription(current.conditionType);
                    slider.AddToClassList("condition-row__slider");
                    slider.RegisterValueChangedCallback(evt =>
                    {
                        current.operandA = Mathf.Clamp(Mathf.RoundToInt(evt.newValue), 0, 100);
                        onChanged?.Invoke(current);
                    });
                    container.Add(slider);
                    break;
                }
                case ConditionTypeMetadata.WidgetKind.Integer:
                {
                    IntegerField field = new IntegerField("値");
                    field.value = current.operandA;
                    field.tooltip = ConditionTypeMetadata.GetDescription(current.conditionType);
                    field.AddToClassList("condition-row__int");
                    field.RegisterValueChangedCallback(evt =>
                    {
                        current.operandA = evt.newValue;
                        onChanged?.Invoke(current);
                    });
                    container.Add(field);

                    if (current.compareOp == CompareOp.InRange)
                    {
                        IntegerField fieldB = new IntegerField("上限");
                        fieldB.value = current.operandB;
                        fieldB.AddToClassList("condition-row__int");
                        fieldB.RegisterValueChangedCallback(evt =>
                        {
                            current.operandB = evt.newValue;
                            onChanged?.Invoke(current);
                        });
                        container.Add(fieldB);
                    }
                    break;
                }
                case ConditionTypeMetadata.WidgetKind.FactionFlags:
                {
                    // CharacterBelong ビットフラグ選択
                    VisualElement flagsRow = new VisualElement();
                    flagsRow.AddToClassList("condition-row__flags");
                    Array belongValues = Enum.GetValues(typeof(CharacterBelong));
                    foreach (object v in belongValues)
                    {
                        CharacterBelong belong = (CharacterBelong)v;
                        if ((int)belong == 0)
                        {
                            continue;
                        }
                        Toggle toggle = new Toggle(belong.ToString());
                        toggle.value = (current.operandA & (int)belong) != 0;
                        toggle.AddToClassList("condition-row__flag-toggle");
                        toggle.RegisterValueChangedCallback(evt =>
                        {
                            if (evt.newValue)
                            {
                                current.operandA |= (int)belong;
                            }
                            else
                            {
                                current.operandA &= ~(int)belong;
                            }
                            onChanged?.Invoke(current);
                        });
                        flagsRow.Add(toggle);
                    }
                    container.Add(flagsRow);
                    break;
                }
                case ConditionTypeMetadata.WidgetKind.IntegerBitmask:
                {
                    // 各ビットに対応するチェックボックス一覧
                    string[] bitLabels = ConditionTypeMetadata.GetBitLabels(current.conditionType);
                    if (bitLabels == null || bitLabels.Length == 0)
                    {
                        Label note = new Label("(この条件のチェック項目は未定義です)");
                        note.AddToClassList("condition-row__none");
                        container.Add(note);
                        break;
                    }

                    Label header = new Label("チェック項目");
                    header.AddToClassList("condition-row__bitmask-header");
                    container.Add(header);

                    VisualElement flagsRow = new VisualElement();
                    flagsRow.AddToClassList("condition-row__flags");
                    for (int bit = 0; bit < bitLabels.Length; bit++)
                    {
                        int bitIndex = bit;
                        int mask = 1 << bitIndex;
                        Toggle toggle = new Toggle(bitLabels[bit]);
                        toggle.value = (current.operandA & mask) != 0;
                        toggle.tooltip = bitLabels[bit];
                        toggle.AddToClassList("condition-row__flag-toggle");
                        toggle.RegisterValueChangedCallback(evt =>
                        {
                            if (evt.newValue)
                            {
                                current.operandA |= mask;
                            }
                            else
                            {
                                current.operandA &= ~mask;
                            }
                            onChanged?.Invoke(current);
                        });
                        flagsRow.Add(toggle);
                    }
                    container.Add(flagsRow);
                    break;
                }
                case ConditionTypeMetadata.WidgetKind.EnumSelect:
                {
                    // 非 [Flags] な連番 enum を単一選択する（例: SelfActState）
                    // operandA に選択した enum 値を直接格納する。評価は Equal/NotEqual で行う。
                    string[] names = null;
                    if (current.conditionType == AIConditionType.SelfActState)
                    {
                        names = Enum.GetNames(typeof(ActState));
                    }

                    if (names == null || names.Length == 0)
                    {
                        Label note = new Label("(選択肢が未定義です)");
                        note.AddToClassList("condition-row__none");
                        container.Add(note);
                        break;
                    }

                    int initialIdx = Mathf.Clamp(current.operandA, 0, names.Length - 1);
                    DropdownField enumDropdown = new DropdownField(
                        "値", new List<string>(names), initialIdx);
                    enumDropdown.tooltip = ConditionTypeMetadata.GetDescription(current.conditionType);
                    enumDropdown.AddToClassList("condition-row__op");
                    enumDropdown.RegisterValueChangedCallback(evt =>
                    {
                        int idx = enumDropdown.index;
                        if (idx < 0)
                        {
                            return;
                        }
                        current.operandA = idx;
                        onChanged?.Invoke(current);
                    });
                    container.Add(enumDropdown);
                    break;
                }
            }
        }

        // =========================================================================
        // Mode Transition Dialog
        // =========================================================================

        /// <summary>
        /// モード遷移ルールを編集するダイアログを表示する。
        /// ruleIndex < 0 の場合は新規追加。
        /// </summary>
        private void ShowModeTransitionDialog(int ruleIndex)
        {
            CompanionAIConfig buffer = _logic.EditingBuffer;
            AIMode[] modes = buffer.modes;
            int modeCount = modes != null ? modes.Length : 0;

            if (modeCount == 0)
            {
                ShowInfoDialog("モードが1個もありません。先にモードを追加してください。");
                return;
            }

            bool isNew = ruleIndex < 0;
            ModeTransitionRule working;
            if (isNew)
            {
                working = new ModeTransitionRule
                {
                    sourceModeIndex = -1,
                    targetModeIndex = 0,
                    conditions = new AICondition[0],
                };
            }
            else
            {
                ModeTransitionRule[] rules = buffer.modeTransitionRules;
                if (rules == null || ruleIndex >= rules.Length)
                {
                    return;
                }
                working = CloneTransitionRule(rules[ruleIndex]);
            }

            VisualElement dialog = BuildModalDialog(
                isNew ? "遷移ルールを追加" : "遷移ルールを編集",
                "ソースモードが現在のモードと一致する時に条件を評価し、合致すればターゲットモードへ切替");
            dialog.AddToClassList("mode-detail-dialog");

            ScrollView scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("mode-detail-scroll");
            dialog.Add(scroll);

            // === モード選択 ===
            VisualElement modeSection = BuildDetailSection("モード");
            scroll.Add(modeSection);

            // sourceModeIndex (-1 = 任意)
            List<string> sourceChoices = new List<string>();
            sourceChoices.Add("(任意のモード)");
            for (int i = 0; i < modeCount; i++)
            {
                sourceChoices.Add("Mode " + i + ": " + (string.IsNullOrEmpty(modes[i].modeName) ? "(無名)" : modes[i].modeName));
            }
            int sourceInitial = working.sourceModeIndex < 0 ? 0 : Mathf.Clamp(working.sourceModeIndex + 1, 0, sourceChoices.Count - 1);
            DropdownField sourceDropdown = new DropdownField("ソースモード", sourceChoices, sourceInitial);
            sourceDropdown.tooltip = "このルールを評価するソース側のモード。(任意)にすると現在のモードに関係なく評価される";
            sourceDropdown.RegisterValueChangedCallback(evt =>
            {
                int idx = sourceDropdown.index;
                working.sourceModeIndex = idx <= 0 ? -1 : idx - 1;
            });
            modeSection.Add(sourceDropdown);

            // targetModeIndex
            List<string> targetChoices = new List<string>();
            for (int i = 0; i < modeCount; i++)
            {
                targetChoices.Add("Mode " + i + ": " + (string.IsNullOrEmpty(modes[i].modeName) ? "(無名)" : modes[i].modeName));
            }
            int targetInitial = Mathf.Clamp(working.targetModeIndex, 0, Mathf.Max(0, targetChoices.Count - 1));
            DropdownField targetDropdown = new DropdownField("ターゲットモード", targetChoices, targetInitial);
            targetDropdown.tooltip = "条件成立時に切り替え先となるモード";
            targetDropdown.RegisterValueChangedCallback(evt =>
            {
                working.targetModeIndex = targetDropdown.index;
            });
            modeSection.Add(targetDropdown);

            // === 条件 ===
            VisualElement conditionsSection = BuildDetailSection("条件 (AND結合)");
            scroll.Add(conditionsSection);

            VisualElement conditionsList = new VisualElement();
            conditionsList.AddToClassList("mode-detail-list");
            conditionsSection.Add(conditionsList);

            // struct の値渡しを避けるため getter/setter closure を渡す
            Action rebuildConditions = null;
            rebuildConditions = () => RebuildConditionList(
                conditionsList,
                () => working.conditions,
                arr => working.conditions = arr,
                rebuildConditions);
            rebuildConditions();

            Button addCondButton = new Button(() =>
            {
                int newCount = (working.conditions != null ? working.conditions.Length : 0) + 1;
                AICondition[] newArr = new AICondition[newCount];
                if (working.conditions != null)
                {
                    for (int i = 0; i < working.conditions.Length; i++)
                    {
                        newArr[i] = working.conditions[i];
                    }
                }
                newArr[newCount - 1] = new AICondition
                {
                    conditionType = AIConditionType.HpRatio,
                    compareOp = CompareOp.LessEqual,
                    operandA = 30,
                    operandB = 0,
                    filter = new TargetFilter { filterFlags = FilterBitFlag.IsSelf, includeSelf = true },
                };
                working.conditions = newArr;
                rebuildConditions();
            });
            addCondButton.text = "＋ 条件を追加";
            addCondButton.AddToClassList("mode-detail-add-button");
            conditionsSection.Add(addCondButton);

            // === ボタン列 ===
            VisualElement buttons = BuildButtonRow();
            buttons.Add(BuildDialogButton("保存", "primary-button", () =>
            {
                ModeTransitionRule[] existing = _logic.EditingBuffer.modeTransitionRules;
                if (isNew)
                {
                    int existingCount = existing != null ? existing.Length : 0;
                    ModeTransitionRule[] newArr = new ModeTransitionRule[existingCount + 1];
                    if (existing != null)
                    {
                        for (int i = 0; i < existingCount; i++)
                        {
                            newArr[i] = existing[i];
                        }
                    }
                    newArr[existingCount] = working;
                    _logic.SetTransitionRulesInBuffer(newArr);
                }
                else
                {
                    if (existing == null || ruleIndex >= existing.Length)
                    {
                        return;
                    }
                    ModeTransitionRule[] newArr = new ModeTransitionRule[existing.Length];
                    for (int i = 0; i < existing.Length; i++)
                    {
                        newArr[i] = i == ruleIndex ? working : existing[i];
                    }
                    _logic.SetTransitionRulesInBuffer(newArr);
                }
                RefreshEditor();
                RefreshDirtyIndicator();
            }));
            if (!isNew)
            {
                buttons.Add(BuildDialogButton("削除", "danger-button", () =>
                {
                    ModeTransitionRule[] existing = _logic.EditingBuffer.modeTransitionRules;
                    if (existing == null || ruleIndex >= existing.Length)
                    {
                        return;
                    }
                    ModeTransitionRule[] newArr = new ModeTransitionRule[existing.Length - 1];
                    int dst = 0;
                    for (int i = 0; i < existing.Length; i++)
                    {
                        if (i == ruleIndex)
                        {
                            continue;
                        }
                        newArr[dst++] = existing[i];
                    }
                    _logic.SetTransitionRulesInBuffer(newArr);
                    RefreshEditor();
                    RefreshDirtyIndicator();
                }));
            }
            buttons.Add(BuildDialogButton("キャンセル", "secondary-button", null));
            dialog.Add(buttons);

            ShowDialog(dialog);
            AttachTooltipHandlers(dialog);
        }

        // =========================================================================
        // Action Picker Popup (Tabs: 攻撃/詠唱/即時/継続/アイテム使用)
        // 号令(Broadcast)タブは仲間AIでは使わないため非表示。
        // UseItem は Instant タブから分離して独立したアイテム選択タブにする。
        // =========================================================================

        /// <summary>
        /// 行動ピッカーのタブ識別子。ActionExecType と 1:1 ではなく、
        /// アイテム使用は InstantAction.UseItem の別タブとして扱う。
        /// </summary>
        private enum PickerTabId : byte
        {
            Attack,
            Cast,
            Instant,
            Sustained,
            Item,
        }

        /// <summary>
        /// タブ識別子 → ラベル。
        /// </summary>
        private static string GetPickerTabLabel(PickerTabId tab)
        {
            switch (tab)
            {
                case PickerTabId.Attack:    return "攻撃";
                case PickerTabId.Cast:      return "詠唱/発射";
                case PickerTabId.Instant:   return "即時行動";
                case PickerTabId.Sustained: return "継続行動";
                case PickerTabId.Item:      return "アイテム使用";
                default:                    return "?";
            }
        }

        /// <summary>
        /// 既存 ActionSlot から、ピッカーを開くときの初期タブを決める。
        /// Instant.UseItem はアイテムタブへ、それ以外は該当 ActionExecType へマップ。
        /// </summary>
        private static PickerTabId ResolveInitialTab(ActionSlot slot)
        {
            switch (slot.execType)
            {
                case ActionExecType.Attack:    return PickerTabId.Attack;
                case ActionExecType.Cast:      return PickerTabId.Cast;
                case ActionExecType.Sustained: return PickerTabId.Sustained;
                case ActionExecType.Instant:
                    return slot.paramId == (int)InstantAction.UseItem ? PickerTabId.Item : PickerTabId.Instant;
                // Broadcast や未対応タイプは攻撃タブ扱い（仲間AIでは Broadcast 未使用）
                default: return PickerTabId.Attack;
            }
        }

        /// <summary>
        /// 行動ピッカーポップアップを表示する。
        /// 選択された ActionSlot を onPicked で返す。
        /// </summary>
        private void ShowActionPickerDialog(PickerTabId initialTab, Action<ActionSlot> onPicked)
        {
            VisualElement dialog = BuildModalDialog("行動を選択", "タブで行動タイプを切り替え、候補から選択してください。");
            dialog.AddToClassList("action-picker-dialog");

            // タブバー
            VisualElement tabBar = new VisualElement();
            tabBar.AddToClassList("action-picker-tabs");
            dialog.Add(tabBar);

            // コンテンツエリア
            VisualElement content = new VisualElement();
            content.AddToClassList("action-picker-content");
            dialog.Add(content);

            PickerTabId[] tabs = new PickerTabId[]
            {
                PickerTabId.Attack,
                PickerTabId.Cast,
                PickerTabId.Instant,
                PickerTabId.Sustained,
                PickerTabId.Item,
            };

            Dictionary<PickerTabId, Button> tabButtons = new Dictionary<PickerTabId, Button>();
            PickerTabId selectedTab = initialTab;

            Action rebuildContent = null;
            rebuildContent = () =>
            {
                foreach (KeyValuePair<PickerTabId, Button> kv in tabButtons)
                {
                    if (kv.Key == selectedTab)
                    {
                        kv.Value.AddToClassList("action-picker-tab--active");
                    }
                    else
                    {
                        kv.Value.RemoveFromClassList("action-picker-tab--active");
                    }
                }

                content.Clear();
                BuildActionPickerContent(content, selectedTab, picked =>
                {
                    CloseDialog();
                    onPicked?.Invoke(picked);
                });
                AttachTooltipHandlers(content);
            };

            for (int i = 0; i < tabs.Length; i++)
            {
                PickerTabId tab = tabs[i];
                Button tabButton = new Button(() =>
                {
                    selectedTab = tab;
                    rebuildContent();
                });
                tabButton.text = GetPickerTabLabel(tab);
                tabButton.tooltip = "行動タイプ: " + GetPickerTabLabel(tab);
                tabButton.AddToClassList("action-picker-tab");
                tabBar.Add(tabButton);
                tabButtons[tab] = tabButton;
            }

            rebuildContent();

            VisualElement buttons = BuildButtonRow();
            buttons.Add(BuildDialogButton("キャンセル", "secondary-button", null));
            dialog.Add(buttons);

            ShowDialog(dialog);
            AttachTooltipHandlers(dialog);
        }

        private void BuildActionPickerContent(VisualElement container, PickerTabId tab, Action<ActionSlot> onPicked)
        {
            switch (tab)
            {
                case PickerTabId.Attack:
                    BuildAttackPickerContent(container, onlyCasters: false, onPicked);
                    break;
                case PickerTabId.Cast:
                    BuildAttackPickerContent(container, onlyCasters: true, onPicked);
                    break;
                case PickerTabId.Instant:
                    BuildInstantPickerContent(container, onPicked);
                    break;
                case PickerTabId.Sustained:
                    BuildSustainedPickerContent(container, onPicked);
                    break;
                case PickerTabId.Item:
                    BuildItemPickerContent(container, onPicked);
                    break;
            }
        }

        /// <summary>
        /// 即時行動（Instant）のピッカー。UseItem は別タブなのでここでは除外する。
        /// </summary>
        private void BuildInstantPickerContent(VisualElement container, Action<ActionSlot> onPicked)
        {
            foreach (object v in Enum.GetValues(typeof(InstantAction)))
            {
                InstantAction action = (InstantAction)v;
                if (action == InstantAction.UseItem)
                {
                    continue;
                }
                int paramId = (int)action;
                string label = ActionSlotLabelTable.GetInstantActionLabel(action);

                Button item = new Button(() =>
                {
                    ActionSlot slot = new ActionSlot
                    {
                        execType = ActionExecType.Instant,
                        paramId = paramId,
                        paramValue = 0f,
                        displayName = label,
                    };
                    onPicked?.Invoke(slot);
                });
                item.text = label;
                item.AddToClassList("action-picker-item");
                if (!IsActionUnlocked(ActionExecType.Instant, paramId))
                {
                    item.AddToClassList("action-picker-item--locked");
                    item.SetEnabled(false);
                    item.text = label + "  [未解放]";
                }
                container.Add(item);
            }
        }

        /// <summary>
        /// アイテム使用ピッカー。_itemCatalog から候補を並べる。
        /// 選択された ActionSlot は InstantAction.UseItem + paramValue=itemId の形で返す。
        /// </summary>
        private void BuildItemPickerContent(VisualElement container, Action<ActionSlot> onPicked)
        {
            if (_itemCatalog == null || _itemCatalog.Length == 0)
            {
                Label empty = new Label("(アイテムカタログが未設定です)");
                empty.AddToClassList("mode-detail-empty");
                container.Add(empty);
                return;
            }

            // UseItem アクション自体が未解放ならアイテム全体も使用不可扱い
            bool unlocked = IsActionUnlocked(ActionExecType.Instant, (int)InstantAction.UseItem);

            for (int i = 0; i < _itemCatalog.Length; i++)
            {
                CompanionItemEntry entry = _itemCatalog[i];
                if (entry == null)
                {
                    continue;
                }
                int itemId = entry.itemId;
                string displayName = string.IsNullOrEmpty(entry.itemName)
                    ? ("アイテム#" + itemId)
                    : entry.itemName;

                Button item = new Button(() =>
                {
                    ActionSlot slot = new ActionSlot
                    {
                        execType = ActionExecType.Instant,
                        paramId = (int)InstantAction.UseItem,
                        paramValue = itemId,
                        displayName = displayName,
                    };
                    onPicked?.Invoke(slot);
                });
                item.text = displayName;
                item.tooltip = string.IsNullOrEmpty(entry.description) ? displayName : entry.description;
                item.AddToClassList("action-picker-item");
                if (!unlocked)
                {
                    item.AddToClassList("action-picker-item--locked");
                    item.SetEnabled(false);
                    item.text = displayName + "  [未解放]";
                }
                container.Add(item);
            }
        }

        private void BuildAttackPickerContent(VisualElement container, bool onlyCasters, Action<ActionSlot> onPicked)
        {
            if (_attackCatalog == null || _attackCatalog.Length == 0)
            {
                Label empty = new Label("(AttackInfo カタログが未設定です)");
                empty.AddToClassList("mode-detail-empty");
                container.Add(empty);
                return;
            }

            // カテゴリ別にグルーピング
            Dictionary<AttackCategory, List<int>> byCategory = new Dictionary<AttackCategory, List<int>>();
            for (int i = 0; i < _attackCatalog.Length; i++)
            {
                AttackInfo info = _attackCatalog[i];
                if (info == null)
                {
                    continue;
                }
                bool isCaster = IsCasterCategory(info.category);
                if (onlyCasters != isCaster)
                {
                    continue;
                }
                if (!byCategory.TryGetValue(info.category, out List<int> list))
                {
                    list = new List<int>();
                    byCategory[info.category] = list;
                }
                list.Add(i);
            }

            if (byCategory.Count == 0)
            {
                Label empty = new Label("(候補がありません)");
                empty.AddToClassList("mode-detail-empty");
                container.Add(empty);
                return;
            }

            foreach (KeyValuePair<AttackCategory, List<int>> kv in byCategory)
            {
                Label header = new Label(ActionSlotLabelTable.GetAttackCategoryLabel(kv.Key));
                header.AddToClassList("action-picker-category-header");
                container.Add(header);

                foreach (int idx in kv.Value)
                {
                    int paramId = idx;
                    AttackInfo info = _attackCatalog[idx];
                    string displayName = string.IsNullOrEmpty(info.attackName) ? info.name : info.attackName;

                    Button item = new Button(() =>
                    {
                        ActionExecType execType = onlyCasters ? ActionExecType.Cast : ActionExecType.Attack;
                        ActionSlot slot = new ActionSlot
                        {
                            execType = execType,
                            paramId = paramId,
                            paramValue = 0f,
                            displayName = displayName,
                        };
                        onPicked?.Invoke(slot);
                    });
                    item.text = displayName;
                    item.tooltip = "カテゴリ: " + ActionSlotLabelTable.GetAttackCategoryLabel(info.category)
                        + "\nMPコスト: " + info.mpCost
                        + "\nスタミナコスト: " + info.staminaCost;
                    item.AddToClassList("action-picker-item");
                    if (!IsActionUnlocked(onlyCasters ? ActionExecType.Cast : ActionExecType.Attack, paramId))
                    {
                        item.AddToClassList("action-picker-item--locked");
                        item.SetEnabled(false);
                        item.text = displayName + "  [未解放]";
                    }
                    container.Add(item);
                }
            }
        }

        private void BuildEnumPickerContent<TEnum>(VisualElement container, ActionExecType execType, Action<ActionSlot> onPicked, Func<TEnum, string> labelFunc)
            where TEnum : struct, Enum
        {
            TEnum[] values = (TEnum[])Enum.GetValues(typeof(TEnum));
            for (int i = 0; i < values.Length; i++)
            {
                TEnum value = values[i];
                int paramId = Convert.ToInt32(value);
                string label = labelFunc(value);

                Button item = new Button(() =>
                {
                    ActionSlot slot = new ActionSlot
                    {
                        execType = execType,
                        paramId = paramId,
                        paramValue = 0f,
                        displayName = label,
                    };
                    onPicked?.Invoke(slot);
                });
                item.text = label;
                item.AddToClassList("action-picker-item");
                if (!IsActionUnlocked(execType, paramId))
                {
                    item.AddToClassList("action-picker-item--locked");
                    item.SetEnabled(false);
                    item.text = label + "  [未解放]";
                }
                container.Add(item);
            }
        }

        /// <summary>
        /// Sustained 行動は選択後に「最大継続時間」の入力ダイアログを経由する。
        /// </summary>
        private void BuildSustainedPickerContent(VisualElement container, Action<ActionSlot> onPicked)
        {
            Array values = Enum.GetValues(typeof(SustainedAction));
            foreach (object v in values)
            {
                SustainedAction action = (SustainedAction)v;
                int paramId = (int)action;
                string label = ActionSlotLabelTable.GetSustainedActionLabel(action);
                string endCondition = SustainedActionMetadata.GetNaturalEndCondition(action);

                Button item = new Button(() =>
                {
                    // 継続時間入力サブダイアログへ
                    ShowSustainedDurationDialog(action, label, duration =>
                    {
                        ActionSlot slot = new ActionSlot
                        {
                            execType = ActionExecType.Sustained,
                            paramId = paramId,
                            paramValue = duration,
                            displayName = label,
                        };
                        onPicked?.Invoke(slot);
                    });
                });
                item.text = label;
                item.tooltip = "自然終了: " + endCondition;
                item.AddToClassList("action-picker-item");

                if (!IsActionUnlocked(ActionExecType.Sustained, paramId))
                {
                    item.AddToClassList("action-picker-item--locked");
                    item.SetEnabled(false);
                    item.text = label + "  [未解放]";
                }
                container.Add(item);
            }
        }

        private void ShowSustainedDurationDialog(SustainedAction action, string label, Action<float> onConfirm)
        {
            VisualElement dialog = BuildModalDialog(
                "継続時間: " + label,
                SustainedActionMetadata.GetNaturalEndCondition(action) + "\n\n0秒=無制限(自然終了まで継続)、それ以外=指定秒数で強制終了");

            FloatField durationField = new FloatField("最大継続時間(秒)");
            durationField.value = 0f;
            durationField.AddToClassList("modal-dialog__input");
            dialog.Add(durationField);

            VisualElement buttons = BuildButtonRow();
            buttons.Add(BuildDialogButton("決定", "primary-button", () =>
            {
                float v = Mathf.Max(0f, durationField.value);
                onConfirm?.Invoke(v);
            }));
            buttons.Add(BuildDialogButton("キャンセル", "secondary-button", null));
            dialog.Add(buttons);

            // 「戻る」ボタン相当でピッカーに戻るのは複雑なのでキャンセルのみサポート
            ShowDialog(dialog);
            durationField.schedule.Execute(() => durationField.Focus()).ExecuteLater(50);
        }

        // =========================================================================
        // Helpers
        // =========================================================================

        private static bool IsCasterCategory(AttackCategory category)
        {
            // Magic/Skill/Support/Summon は Cast タブに出す
            return category == AttackCategory.Magic
                || category == AttackCategory.Skill
                || category == AttackCategory.Support
                || category == AttackCategory.Summon;
        }

        private bool IsActionUnlocked(ActionExecType execType, int paramId)
        {
            if (_actionTypeRegistry == null)
            {
                // レジストリ未設定時は全て解放扱い
                return true;
            }
            return _actionTypeRegistry.IsUnlocked(new ActionUnlockKey { execType = execType, paramId = paramId });
        }

        private string ResolveActionSlotLabel(ActionSlot slot)
        {
            // displayName 優先
            if (!string.IsNullOrEmpty(slot.displayName))
            {
                return slot.displayName;
            }

            // Attack/Cast は _attackCatalog から解決
            if ((slot.execType == ActionExecType.Attack || slot.execType == ActionExecType.Cast)
                && _attackCatalog != null
                && slot.paramId >= 0
                && slot.paramId < _attackCatalog.Length
                && _attackCatalog[slot.paramId] != null)
            {
                AttackInfo info = _attackCatalog[slot.paramId];
                return string.IsNullOrEmpty(info.attackName) ? info.name : info.attackName;
            }

            // UseItem は _itemCatalog から paramValue (itemId) で解決
            if (slot.execType == ActionExecType.Instant
                && slot.paramId == (int)InstantAction.UseItem
                && _itemCatalog != null)
            {
                int itemId = Mathf.RoundToInt(slot.paramValue);
                for (int i = 0; i < _itemCatalog.Length; i++)
                {
                    CompanionItemEntry entry = _itemCatalog[i];
                    if (entry == null)
                    {
                        continue;
                    }
                    if (entry.itemId == itemId)
                    {
                        return string.IsNullOrEmpty(entry.itemName)
                            ? ("アイテム使用: #" + itemId)
                            : ("アイテム使用: " + entry.itemName);
                    }
                }
                return "アイテム使用: #" + itemId;
            }

            // fallback
            return ActionSlotLabelTable.GetFallbackLabel(slot);
        }

        private VisualElement BuildDetailSection(string title)
        {
            VisualElement section = new VisualElement();
            section.AddToClassList("mode-detail-section");

            Label label = new Label(title);
            label.AddToClassList("mode-detail-section__title");
            section.Add(label);

            return section;
        }

        private static AIMode CloneMode(AIMode source)
        {
            AIMode clone = new AIMode
            {
                modeName = source.modeName,
                modeId = source.modeId,
                defaultActionIndex = source.defaultActionIndex,
                judgeInterval = source.judgeInterval,
            };

            clone.actions = source.actions != null ? (ActionSlot[])source.actions.Clone() : new ActionSlot[0];
            clone.targetSelects = source.targetSelects != null ? (AITargetSelect[])source.targetSelects.Clone() : new AITargetSelect[0];

            // ルール配列は conditions 配列を含むので深くクローン
            int targetRuleCount = source.targetRules != null ? source.targetRules.Length : 0;
            clone.targetRules = new AIRule[targetRuleCount];
            for (int i = 0; i < targetRuleCount; i++)
            {
                clone.targetRules[i] = CloneRule(source.targetRules[i]);
            }

            int actionRuleCount = source.actionRules != null ? source.actionRules.Length : 0;
            clone.actionRules = new AIRule[actionRuleCount];
            for (int i = 0; i < actionRuleCount; i++)
            {
                clone.actionRules[i] = CloneRule(source.actionRules[i]);
            }

            return clone;
        }

        private static AIRule CloneRule(AIRule source)
        {
            AIRule clone = new AIRule
            {
                actionIndex = source.actionIndex,
                probability = source.probability,
            };
            clone.conditions = source.conditions != null ? (AICondition[])source.conditions.Clone() : new AICondition[0];
            return clone;
        }

        private static ModeTransitionRule CloneTransitionRule(ModeTransitionRule source)
        {
            ModeTransitionRule clone = new ModeTransitionRule
            {
                sourceModeIndex = source.sourceModeIndex,
                targetModeIndex = source.targetModeIndex,
            };
            clone.conditions = source.conditions != null ? (AICondition[])source.conditions.Clone() : new AICondition[0];
            return clone;
        }
    }
}
