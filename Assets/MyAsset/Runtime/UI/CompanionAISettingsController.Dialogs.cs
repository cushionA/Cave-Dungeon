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

            // defaultActionIndex は「行動」セクションのデフォルト行（DEFAULT バッジ付き）から
            // 直接編集する。ここでは IntegerField を出さないことでユーザーが index を意識しなくて
            // 済むようにする（Phase 5 UX 刷新）。

            // === ターゲット選定 (targetSelects) — JudgmentLoop 第1層 ===
            VisualElement targetSelectsSection = BuildDetailSection("ターゲット選定 (誰を狙うか)");
            scroll.Add(targetSelectsSection);

            VisualElement targetSelectsList = new VisualElement();
            targetSelectsList.AddToClassList("mode-detail-list");
            targetSelectsSection.Add(targetSelectsList);

            Action rebuildTargetSelects = null;
            rebuildTargetSelects = () => RebuildTargetSelectList(
                targetSelectsList,
                () => working.targetSelects,
                arr => working.targetSelects = arr,
                rebuildTargetSelects,
                () => working.targetRules,
                arr => working.targetRules = arr);
            rebuildTargetSelects();

            Button addTargetSelectButton = new Button(() =>
            {
                AITargetSelect newTs = new AITargetSelect
                {
                    sortKey = TargetSortKey.Distance,
                    isDescending = false,
                    filter = new TargetFilter
                    {
                        belong = CharacterBelong.Enemy,
                        includeSelf = false,
                    },
                };
                int newCount = (working.targetSelects != null ? working.targetSelects.Length : 0) + 1;
                AITargetSelect[] newArr = new AITargetSelect[newCount];
                if (working.targetSelects != null)
                {
                    for (int i = 0; i < working.targetSelects.Length; i++)
                    {
                        newArr[i] = working.targetSelects[i];
                    }
                }
                newArr[newCount - 1] = newTs;
                working.targetSelects = newArr;
                rebuildTargetSelects();
            });
            addTargetSelectButton.text = "＋ ターゲット選定を追加";
            addTargetSelectButton.tooltip = "新しいターゲット選定パターンを追加";
            addTargetSelectButton.AddToClassList("mode-detail-add-button");
            targetSelectsSection.Add(addTargetSelectButton);

            // === ターゲット切替ルール (targetRules) — JudgmentLoop 第1層ルール ===
            VisualElement targetRulesSection = BuildDetailSection("ターゲット切替ルール (優先度順)");
            scroll.Add(targetRulesSection);

            VisualElement targetRulesList = new VisualElement();
            targetRulesList.AddToClassList("mode-detail-list");
            targetRulesSection.Add(targetRulesList);

            Action rebuildTargetRules = null;
            rebuildTargetRules = () => RebuildTargetRuleList(
                targetRulesList,
                () => working.targetRules,
                arr => working.targetRules = arr,
                () => working,
                rebuildTargetRules);
            rebuildTargetRules();

            Button addTargetRuleButton = new Button(() =>
            {
                AIRule newRule = new AIRule
                {
                    conditions = new AICondition[0],
                    actionIndex = 0,
                    probability = 100,
                };
                int newCount = (working.targetRules != null ? working.targetRules.Length : 0) + 1;
                AIRule[] newArr = new AIRule[newCount];
                if (working.targetRules != null)
                {
                    for (int i = 0; i < working.targetRules.Length; i++)
                    {
                        newArr[i] = working.targetRules[i];
                    }
                }
                newArr[newCount - 1] = newRule;
                working.targetRules = newArr;
                rebuildTargetRules();
            });
            addTargetRuleButton.text = "＋ ターゲット切替ルールを追加";
            addTargetRuleButton.tooltip = "新しいターゲット切替ルールを追加";
            addTargetRuleButton.AddToClassList("mode-detail-add-button");
            targetRulesSection.Add(addTargetRuleButton);

            // === 行動（優先度順） — Phase 5 統合UI ===
            // 「1ルール = 条件 + 行動」として 1行で見せる。上から順に評価され、最初に成立した
            // ルールの行動が実行される。デフォルト行動はリスト末尾に DEFAULT バッジ付きで表示。
            // 行クリックで [編集 / 別条件で複製 / 削除] メニューを開く。
            VisualElement actionSection = BuildDetailSection("行動（優先度順）");
            scroll.Add(actionSection);

            Label actionHint = new Label("上から順に評価され、最初に成立したルールの行動が実行されます。行をクリックして編集・複製・削除できます。");
            actionHint.AddToClassList("section-hint");
            actionSection.Add(actionHint);

            VisualElement unifiedList = new VisualElement();
            unifiedList.AddToClassList("mode-detail-list");
            unifiedList.AddToClassList("unified-action-list");
            actionSection.Add(unifiedList);

            // struct working を reassign する必要があるので getter/setter closure で書き戻す
            Action rebuildUnified = null;
            rebuildUnified = () => RebuildUnifiedActionList(
                unifiedList,
                () => working,
                m => working = m,
                rebuildUnified);
            rebuildUnified();

            Button addActionRuleButton = new Button(() =>
            {
                ShowActionPickerDialog(PickerTabId.Attack, picked =>
                {
                    working = CompanionAISettingsLogic.AddActionRuleWithNewSlot(
                        working,
                        new AICondition[0],
                        picked,
                        100);
                    rebuildUnified();
                    // 追加直後に編集ダイアログを開いて条件入力を促す。
                    // 行クリック経路と完全に同じ共通エントリを使い、振る舞いを一元化する。
                    int newIdx = working.actionRules.Length - 1;
                    OpenActionRuleEditorForIndex(
                        newIdx,
                        isNewlyAdded: true,
                        () => working,
                        m => working = m,
                        rebuildUnified);
                });
            });
            addActionRuleButton.text = "＋ 行動を追加";
            addActionRuleButton.tooltip = "新しい行動ルールを追加（行動を選択→条件を設定）";
            addActionRuleButton.AddToClassList("mode-detail-add-button");
            actionSection.Add(addActionRuleButton);

            // === ボタン列 ===
            VisualElement buttons = BuildButtonRow();
            buttons.Add(BuildDialogButton("保存", "primary-button", () =>
            {
                // modeId を空にして独立コピー扱い（参照リンクを切る安全弁）
                working.modeId = "";
                // 編集中に発生した orphan ActionSlot を保存時に一括 GC し、
                // actionRules/defaultActionIndex を詰め後のインデックスにリマップする
                working = CompanionAISettingsLogic.GcOrphanActionSlots(working);
                _logic.UpdateModeInBuffer(slotIndex, working);
                RefreshEditor();
                RefreshDirtyIndicator();
            }));
            buttons.Add(BuildDialogButton("キャンセル", "secondary-button", null));
            dialog.Add(buttons);

            ShowDialog(dialog);
            AttachTooltipHandlers(dialog);
        }

        // =====================================================================
        // Phase 5: 統合「行動」リスト
        //
        // 1行=1ルール(条件+行動) として表示する。
        // - 通常ルール行: [#N] 行動名 / 条件サマリ  → クリックで行動詳細ダイアログへ直行
        // - デフォルト行: DEFAULTバッジ + 「何もしない」 / (編集不可)  → クリック不可
        //
        // 中間メニュー（編集/複製/削除の選択）は廃止し、詳細ダイアログのフッターに
        // 「別条件で複製」「削除」を配置する（モックアップ仕様）。
        // =====================================================================

        /// <summary>
        /// 「行動」統合リストを描画する。AIMode(struct) の丸ごと再構築が必要なので
        /// getter/setter の closure を受ける。
        /// </summary>
        private void RebuildUnifiedActionList(
            VisualElement container,
            Func<AIMode> getMode,
            Action<AIMode> setMode,
            Action rebuild)
        {
            container.Clear();

            AIMode mode = getMode();
            AIRule[] rules = mode.actionRules ?? new AIRule[0];

            if (rules.Length == 0 && (mode.actions == null || mode.actions.Length == 0))
            {
                Label empty = new Label("(まだ行動がありません。下の『＋ 行動を追加』から登録してください)");
                empty.AddToClassList("mode-detail-empty");
                container.Add(empty);
                // actions が空ならデフォルト行も情報を持たないので出さない
                return;
            }

            // 通常ルール行（クリックで詳細ダイアログを開く）
            for (int i = 0; i < rules.Length; i++)
            {
                int idx = i;
                AIRule rule = rules[idx];
                VisualElement row = BuildUnifiedActionRow(rule, mode, idx, isDefault: false);
                row.RegisterCallback<ClickEvent>(evt =>
                {
                    OpenActionRuleEditorForIndex(idx, isNewlyAdded: false, getMode, setMode, rebuild);
                });
                container.Add(row);
            }

            // デフォルト行動（DEFAULTバッジ付き、ルール不一致時の既定行動）
            // Phase 5 方針: デフォルトは「何もしない」固定表示。クリック不可・編集不可。
            // 内部 defaultActionIndex は従来通り保持するが UI からは編集しない
            // （別 Phase で runtime 側に "Idle" アクションを追加したら連動させる）。
            VisualElement defaultRow = BuildUnifiedActionRow(default, mode, -1, isDefault: true);
            container.Add(defaultRow);

            AttachTooltipHandlers(container);
        }

        /// <summary>
        /// 指定 ruleIdx のルールに対して統合編集ダイアログを開き、各コールバックを配線する。
        /// 行クリック・「＋ 行動を追加」両方から呼ばれる共通エントリ。
        /// </summary>
        private void OpenActionRuleEditorForIndex(
            int idx,
            bool isNewlyAdded,
            Func<AIMode> getMode,
            Action<AIMode> setMode,
            Action rebuild)
        {
            AIMode modeNow = getMode();
            if (modeNow.actionRules == null || idx < 0 || idx >= modeNow.actionRules.Length)
            {
                return;
            }

            AIRule ruleNow = modeNow.actionRules[idx];
            ShowUnifiedActionRuleEditorDialog(
                ruleNow,
                modeNow,
                isNewlyAdded,
                onConfirmMeta: editedMeta =>
                {
                    AIMode m = getMode();
                    if (m.actionRules != null && idx < m.actionRules.Length)
                    {
                        AIRule r = m.actionRules[idx];
                        r.conditions = editedMeta.conditions;
                        r.probability = editedMeta.probability;
                        m.actionRules[idx] = r;
                        setMode(m);
                    }
                    rebuild();
                },
                onActionChanged: pickedNewSlot =>
                {
                    AIMode m = getMode();
                    m = CompanionAISettingsLogic.ReplaceActionRuleSlot(m, idx, pickedNewSlot);
                    setMode(m);
                    rebuild();
                },
                onDuplicate: () =>
                {
                    AIMode m = getMode();
                    m = CompanionAISettingsLogic.DuplicateActionRule(m, idx);
                    setMode(m);
                    rebuild();
                },
                onDelete: () =>
                {
                    AIMode m = getMode();
                    m = CompanionAISettingsLogic.RemoveActionRule(m, idx);
                    setMode(m);
                    rebuild();
                });
        }

        /// <summary>
        /// 統合行動リストの1行ビルダー。
        /// 通常行: [優先度] 行動名 ─ 条件サマリ
        /// デフォルト行: [DEFAULT] 何もしない ─ ルール不一致時の既定行動（編集不可）
        /// </summary>
        private VisualElement BuildUnifiedActionRow(AIRule rule, AIMode mode, int priorityIdx, bool isDefault)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("unified-action-row");
            if (isDefault)
            {
                row.AddToClassList("unified-action-row--default");
                // デフォルト行はクリックを吸わない（背後要素もクリック不可で問題ないが、
                // hover 効果を消すことで「編集できる要素ではない」という非言語的シグナルを送る）
                row.pickingMode = PickingMode.Ignore;
            }

            // 左端: 優先度番号 or DEFAULTバッジ
            if (isDefault)
            {
                Label badge = new Label("DEFAULT");
                badge.AddToClassList("unified-action-row__default-badge");
                row.Add(badge);
            }
            else
            {
                Label priority = new Label("#" + (priorityIdx + 1));
                priority.AddToClassList("unified-action-row__priority");
                row.Add(priority);
            }

            // 中央: 行動名（プライマリに視覚化）。デフォルト行は「何もしない」固定。
            string actionText = isDefault ? "何もしない" : FormatRuleActionName(rule, mode);
            Label actionName = new Label(actionText);
            actionName.AddToClassList("unified-action-row__action");
            row.Add(actionName);

            // Sustained なら継続時間を小さく表示（通常行のみ）
            if (!isDefault
                && mode.actions != null
                && rule.actionIndex >= 0
                && rule.actionIndex < mode.actions.Length
                && mode.actions[rule.actionIndex].execType == ActionExecType.Sustained)
            {
                ActionSlot slot = mode.actions[rule.actionIndex];
                Label duration = new Label(SustainedActionMetadata.GetDurationLabel(slot.paramValue));
                duration.tooltip = SustainedActionMetadata.GetNaturalEndCondition((SustainedAction)slot.paramId);
                duration.AddToClassList("unified-action-row__duration");
                row.Add(duration);
            }

            // 区切り
            Label sep = new Label("─");
            sep.AddToClassList("unified-action-row__sep");
            row.Add(sep);

            // 右側: 条件サマリ（補助）
            Label conditions = new Label(FormatRuleConditionSummary(rule, isDefault));
            conditions.AddToClassList("unified-action-row__conditions");
            row.Add(conditions);

            // 右端: 発動確率（通常行かつ100%以外のみ）
            if (!isDefault && rule.probability != 100)
            {
                Label prob = new Label(rule.probability + "%");
                prob.tooltip = "発動確率（条件成立しても確率的にスキップ）";
                prob.AddToClassList("unified-action-row__prob");
                row.Add(prob);
            }

            // 行全体にツールチップでも詳細を再掲
            row.tooltip = isDefault
                ? "デフォルト行動（ルール不一致時に実行）— 何もしない"
                : FormatRuleActionName(rule, mode) + " / " + FormatRuleConditionSummary(rule, isDefault);

            return row;
        }

        /// <summary>
        /// ルールが指す ActionSlot の表示名（"#index" プレフィックスなし）。
        /// index を露出しないのが Phase 5 の方針。
        /// </summary>
        private string FormatRuleActionName(AIRule rule, AIMode mode)
        {
            if (mode.actions != null && rule.actionIndex >= 0 && rule.actionIndex < mode.actions.Length)
            {
                return ResolveActionSlotLabel(mode.actions[rule.actionIndex]);
            }
            return "(未設定)";
        }

        /// <summary>
        /// 条件サマリの1行テキスト。条件数だけ示す簡易版。
        /// デフォルト行は「ルール不一致時の既定行動」固定。
        /// </summary>
        private string FormatRuleConditionSummary(AIRule rule, bool isDefault)
        {
            if (isDefault)
            {
                return "ルール不一致時の既定行動";
            }
            int condCount = rule.conditions != null ? rule.conditions.Length : 0;
            if (condCount == 0)
            {
                return "無条件（常時成立）";
            }
            return condCount + "個の条件（AND結合）";
        }

        /// <summary>
        /// ResolveInitialTab の安全版。actionIndex が範囲外でも Attack にフォールバックする。
        /// </summary>
        private PickerTabId ResolveInitialTabSafe(AIMode mode, int actionIndex)
        {
            if (mode.actions == null || actionIndex < 0 || actionIndex >= mode.actions.Length)
            {
                return PickerTabId.Attack;
            }
            return ResolveInitialTab(mode.actions[actionIndex]);
        }

        /// <summary>
        /// 編集ダイアログから返ってくる「ルールのメタ情報（条件・確率）」。
        /// ActionSlot 差し替えは別コールバックで通知するため、ここには含めない。
        /// </summary>
        private struct EditedRuleMeta
        {
            public AICondition[] conditions;
            public byte probability;
        }

        // =========================================================================
        // 統合アクションルール編集ダイアログ（Phase 5）
        //
        // 1クリックで行動詳細ウインドウが開き、その中で「行動」「各条件」「発動確率」を
        // 個別にクリックして編集できる。「別条件で複製」「削除」はフッターに配置。
        //
        // 行動変更・各条件編集はスタック式サブダイアログで開く（このダイアログは残る）。
        // ActionSlot の差し替えは ReplaceActionRuleSlot を経由するため、親UI側で
        // 共有判定と append/in-place の使い分けが自動で行われる。
        // =========================================================================

        private void ShowUnifiedActionRuleEditorDialog(
            AIRule initialRule,
            AIMode parentMode,
            bool isNewlyAdded,
            Action<EditedRuleMeta> onConfirmMeta,
            Action<ActionSlot> onActionChanged,
            Action onDuplicate,
            Action onDelete)
        {
            AIRule working = CloneRule(initialRule);

            string titleText = isNewlyAdded ? "追加した行動の条件を設定" : FormatRuleActionName(working, parentMode);
            VisualElement dialog = BuildModalDialog(
                titleText,
                "「行動」「各条件」「発動確率」はそれぞれクリックして個別に編集できます。条件は全て AND 結合されます。");
            dialog.AddToClassList("unified-action-editor-dialog");

            ScrollView scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("mode-detail-scroll");
            dialog.Add(scroll);

            // === この条件で実行する行動（クリッカブルチップ） ===
            VisualElement actionSection = BuildDetailSection("この条件で実行する行動");
            Label actionHint = new Label("クリックして別の行動に差し替え");
            actionHint.AddToClassList("section-hint");
            actionSection.Add(actionHint);
            scroll.Add(actionSection);

            // 行動チップはクリックで ActionPicker を stacked ダイアログで開く。
            // BuildDialogButton は CloseDialog を挟むので使わず、直接 Button を使う。
            // クロージャ内で chip 自身を参照するため、先に null で宣言してから割当する。
            Button actionChip = null;
            actionChip = new Button(() =>
            {
                ShowActionPickerDialog(
                    ResolveInitialTabSafe(parentMode, working.actionIndex),
                    picked =>
                    {
                        // 親UI側で ReplaceActionRuleSlot を呼ぶ（共有判定込み）。
                        // 本ダイアログはスタック下層に残るので、ここでは chip のラベルだけ更新する。
                        onActionChanged?.Invoke(picked);
                        UpdateActionChipLabel(actionChip, picked);
                    });
            });
            UpdateActionChipLabel(actionChip, ResolveActionSlotFromRule(working, parentMode));
            actionChip.tooltip = "この行動を別のものに差し替え";
            actionChip.AddToClassList("action-chip");
            actionSection.Add(actionChip);

            // === 条件（クリッカブルチップリスト） ===
            VisualElement conditionsSection = BuildDetailSection("条件（AND結合）");
            Label condHint = new Label("各条件をクリックして詳細を編集。追加・削除もここから。");
            condHint.AddToClassList("section-hint");
            conditionsSection.Add(condHint);
            scroll.Add(conditionsSection);

            VisualElement conditionsList = new VisualElement();
            conditionsList.AddToClassList("condition-chip-list");
            conditionsSection.Add(conditionsList);

            Action rebuildConditions = null;
            rebuildConditions = () => RebuildConditionChipList(
                conditionsList,
                () => working.conditions,
                arr => working.conditions = arr,
                rebuildConditions);
            rebuildConditions();

            Button addCondButton = new Button(() =>
            {
                // 新規条件を追加し、その場で編集ダイアログを開いて入力を促す
                int newCount = (working.conditions != null ? working.conditions.Length : 0) + 1;
                AICondition[] newArr = new AICondition[newCount];
                if (working.conditions != null)
                {
                    for (int i = 0; i < working.conditions.Length; i++)
                    {
                        newArr[i] = working.conditions[i];
                    }
                }
                AICondition blank = new AICondition
                {
                    conditionType = AIConditionType.HpRatio,
                    compareOp = CompareOp.LessEqual,
                    operandA = 50,
                    operandB = 0,
                    filter = new TargetFilter(),
                };
                newArr[newCount - 1] = blank;
                working.conditions = newArr;
                rebuildConditions();

                int addedIdx = newCount - 1;
                ShowConditionEditorDialog(blank, updated =>
                {
                    if (working.conditions != null && addedIdx < working.conditions.Length)
                    {
                        working.conditions[addedIdx] = updated;
                    }
                    rebuildConditions();
                });
            });
            addCondButton.text = "＋ 条件を追加";
            addCondButton.tooltip = "新しい条件を追加（すべて AND で結合されます）";
            addCondButton.AddToClassList("mode-detail-add-button");
            conditionsSection.Add(addCondButton);

            // === 発動確率 ===
            VisualElement probSection = BuildDetailSection("発動確率");
            scroll.Add(probSection);

            Slider probSlider = new Slider("発動確率(%)", 0, 100);
            probSlider.value = working.probability;
            probSlider.tooltip = "ルール条件成立時にこの行動を選ぶ確率(0-100)。100未満なら確率的にスキップされ、次のルールに評価が進む。";
            probSlider.showInputField = true;
            probSlider.RegisterValueChangedCallback(evt =>
            {
                working.probability = (byte)Mathf.Clamp(Mathf.RoundToInt(evt.newValue), 0, 100);
            });
            probSection.Add(probSlider);

            // === フッター: 左に 複製/削除、右に 保存/キャンセル ===
            VisualElement footer = new VisualElement();
            footer.AddToClassList("modal-dialog__buttons");
            footer.AddToClassList("modal-dialog__buttons--split");

            VisualElement footerLeft = new VisualElement();
            footerLeft.AddToClassList("modal-dialog__buttons-group");
            footerLeft.Add(BuildDialogButton("別条件で複製", "secondary-button", onDuplicate));
            footerLeft.Add(BuildDialogButton("削除", "danger-button", onDelete));
            footer.Add(footerLeft);

            VisualElement footerRight = new VisualElement();
            footerRight.AddToClassList("modal-dialog__buttons-group");
            footerRight.Add(BuildDialogButton("キャンセル", "secondary-button", null));
            footerRight.Add(BuildDialogButton("保存", "primary-button", () =>
            {
                EditedRuleMeta meta = new EditedRuleMeta
                {
                    conditions = working.conditions,
                    probability = working.probability,
                };
                onConfirmMeta?.Invoke(meta);
            }));
            footer.Add(footerRight);

            dialog.Add(footer);

            ShowDialog(dialog);
            AttachTooltipHandlers(dialog);
        }

        /// <summary>
        /// アクションチップのラベル/tooltipを現在の ActionSlot 内容で更新する。
        /// 行動差し替え時にもこれで即時反映する。
        /// </summary>
        private void UpdateActionChipLabel(Button chip, ActionSlot slot)
        {
            string name = ResolveActionSlotLabel(slot);
            chip.text = string.IsNullOrEmpty(name) ? "(未設定) — クリックして選択" : name;
        }

        /// <summary>
        /// AIRule の actionIndex から ActionSlot を解決する。範囲外なら default(ActionSlot)。
        /// </summary>
        private ActionSlot ResolveActionSlotFromRule(AIRule rule, AIMode mode)
        {
            if (mode.actions != null && rule.actionIndex >= 0 && rule.actionIndex < mode.actions.Length)
            {
                return mode.actions[rule.actionIndex];
            }
            return default;
        }

        /// <summary>
        /// 条件リストを「クリッカブルチップ」形式で描画する（Phase 5）。
        /// 各チップはサマリ表示のみ、クリックで <see cref="ShowConditionEditorDialog"/> を開いて
        /// 詳細編集する。× ボタンで単体削除も可能。
        /// </summary>
        private void RebuildConditionChipList(
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
                Label empty = new Label("(条件なし — このルールは常時成立します)");
                empty.AddToClassList("mode-detail-empty");
                container.Add(empty);
                return;
            }

            for (int i = 0; i < count; i++)
            {
                int idx = i;
                AICondition cond = conditions[idx];

                VisualElement chip = new VisualElement();
                chip.AddToClassList("condition-chip");

                // 本体をクリックすると編集ダイアログを開く
                Button body = new Button(() =>
                {
                    AICondition[] arr = getConditions();
                    if (arr == null || idx >= arr.Length)
                    {
                        return;
                    }
                    ShowConditionEditorDialog(arr[idx], updated =>
                    {
                        // Remove 経路と同じ「新配列生成→setter」パターンに揃える。
                        AICondition[] arr2 = getConditions();
                        if (arr2 == null || idx >= arr2.Length)
                        {
                            return;
                        }
                        AICondition[] newArr = new AICondition[arr2.Length];
                        for (int k = 0; k < arr2.Length; k++)
                        {
                            newArr[k] = k == idx ? updated : arr2[k];
                        }
                        setConditions(newArr);
                        rebuild();
                    });
                });
                body.text = FormatConditionSummary(cond);
                body.tooltip = "クリックして条件の詳細を編集";
                body.AddToClassList("condition-chip__body");
                chip.Add(body);

                // × 削除ボタン（チップ本体のクリックと分離するため独立）
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
                removeButton.AddToClassList("condition-chip__remove");
                removeButton.AddToClassList("danger-button");
                chip.Add(removeButton);

                container.Add(chip);

                // チップ間に AND 区切り（最終チップの後には出さない）
                if (idx < count - 1)
                {
                    Label andLabel = new Label("AND");
                    andLabel.AddToClassList("condition-chip__and-sep");
                    container.Add(andLabel);
                }
            }

            AttachTooltipHandlers(container);
        }

        /// <summary>
        /// 条件を1行テキストに整形する。行動詳細ダイアログ内の条件チップ表示に使う。
        /// 例: "HP ratio ≤ 30%"、"距離 3.0 ≤ X ≤ 10.0"、"敵の種類 含む(OR) 弱点=Fire"
        /// TargetFilter が非空のときは末尾に [陣営=Enemy, 弱点=Fire] の形で追記する。
        /// </summary>
        private string FormatConditionSummary(AICondition cond)
        {
            string typeLabel = ConditionTypeMetadata.GetLabel(cond.conditionType);
            string opSymbol = FormatCompareOpSymbol(cond.compareOp);

            string main;
            if (cond.compareOp == CompareOp.InRange)
            {
                main = typeLabel + " " + cond.operandA + " ≤ X ≤ " + cond.operandB;
            }
            else if (cond.compareOp == CompareOp.HasFlag || cond.compareOp == CompareOp.HasAny)
            {
                main = typeLabel + " " + opSymbol + " (" + cond.operandA + ")";
            }
            else
            {
                main = typeLabel + " " + opSymbol + " " + cond.operandA;
            }

            // TargetFilter が制約を持っているときだけ末尾に追記（空のときに冗長な
            //「条件なし（全員が対象）」は出さない）
            if (HasFilterConstraints(cond.filter))
            {
                main += " [" + FormatFilterSummary(cond.filter) + "]";
            }
            return main;
        }

        /// <summary>
        /// TargetFilter が何らかの絞り込み条件を持っているかどうか。
        /// chip サマリに filter 情報を出すか判定するために使う。
        /// </summary>
        private static bool HasFilterConstraints(TargetFilter f)
        {
            return f.belong != 0
                || f.feature != 0
                || f.weakPoint != 0
                || f.distanceRange.x > 0f
                || f.distanceRange.y > 0f;
        }

        /// <summary>
        /// CompareOp を記号表記へ変換する。
        /// </summary>
        private string FormatCompareOpSymbol(CompareOp op)
        {
            switch (op)
            {
                case CompareOp.Less:         return "<";
                case CompareOp.LessEqual:    return "≤";
                case CompareOp.Equal:        return "=";
                case CompareOp.GreaterEqual: return "≥";
                case CompareOp.Greater:      return ">";
                case CompareOp.NotEqual:     return "≠";
                case CompareOp.InRange:      return "範囲内";
                case CompareOp.HasFlag:      return "含む(AND)";
                case CompareOp.HasAny:       return "含む(OR)";
                default:                     return op.ToString();
            }
        }

        /// <summary>
        /// 単一の条件を編集する個別ダイアログ（Phase 5）。
        /// 既存 <see cref="BuildConditionRow"/> が inline で生成していた UI を
        /// そのままダイアログコンテンツとして利用し、保存/キャンセルで確定する。
        /// タイトルには初期状態の条件種別を含めて、どの条件を編集しているか文脈が分かるようにする。
        /// </summary>
        private void ShowConditionEditorDialog(AICondition initial, Action<AICondition> onConfirm)
        {
            AICondition working = initial;

            string title = "条件: " + ConditionTypeMetadata.GetLabel(initial.conditionType);
            VisualElement dialog = BuildModalDialog(
                title,
                "条件の種類・比較演算子・閾値・対象フィルタを設定してください。");
            dialog.AddToClassList("condition-editor-dialog");

            ScrollView scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("mode-detail-scroll");
            dialog.Add(scroll);

            VisualElement editorRow = BuildConditionRow(working, updated =>
            {
                working = updated;
            });
            editorRow.AddToClassList("condition-editor-dialog__row");
            scroll.Add(editorRow);

            VisualElement buttons = BuildButtonRow();
            buttons.Add(BuildDialogButton("保存", "primary-button", () => onConfirm?.Invoke(working)));
            buttons.Add(BuildDialogButton("キャンセル", "secondary-button", null));
            dialog.Add(buttons);

            ShowDialog(dialog);
            AttachTooltipHandlers(dialog);
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
        /// 陣営/特徴/弱点属性/距離範囲を編集可能。
        /// 「自分を参照」は仲間AI設定では特徴=Companion（コンパニオンは1体のみ）で代替できるため、includeSelf UI は提供しない。
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

            // includeSelf は UI で提供しない。
            // 自分を参照したい場合は下の「特徴」で Companion にチェックを入れる（コンパニオンは1体のみ）。
            // 既存データの includeSelf 値はそのまま保持される。

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
            // includeSelf は UI 編集廃止（Companion 特徴で代替）したため、サマリでも表示しない。
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
                    // Ratio は 0-100% のスライダーUI。InRange は operandB の上限入力欄を
                    // 必要とするが、2値スライダー化すると UI が煩雑になるため非対応。
                    // (旧データで InRange が入っていた場合は既定値の LessEqual に矯正)
                    valid = cond.compareOp == CompareOp.Less
                         || cond.compareOp == CompareOp.LessEqual
                         || cond.compareOp == CompareOp.Equal
                         || cond.compareOp == CompareOp.GreaterEqual
                         || cond.compareOp == CompareOp.Greater
                         || cond.compareOp == CompareOp.NotEqual;
                    break;
                case ConditionTypeMetadata.WidgetKind.Integer:
                    // Integer は下限+上限の 2 つの IntegerField が自然に並べられるため InRange 対応
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
                // Ratio は下限+上限の2値入力 UI が無いため InRange を除外する (NormalizeCompareOp と連動)
                bool supportsInRange = kind == ConditionTypeMetadata.WidgetKind.Integer;
                List<string> opChoices = new List<string>
                {
                    "<", "<=", "==", ">=", ">", "!=",
                };
                if (supportsInRange)
                {
                    opChoices.Add("範囲内");
                }
                int opIdx = (int)current.compareOp;
                if (opIdx < 0 || opIdx >= opChoices.Count)
                {
                    opIdx = 0;
                }
                compareDropdown = new DropdownField("比較", opChoices, opIdx);
                compareDropdown.tooltip = supportsInRange
                    ? "比較方法\n ・範囲内: operandA <= 値 <= operandB の範囲判定（下限+上限の2値入力）"
                    : "比較方法";
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
            conditionsList.AddToClassList("condition-chip-list");
            conditionsSection.Add(conditionsList);

            // Phase 5: チップ式（クリックで個別編集ダイアログ）に統一
            Action rebuildConditions = null;
            rebuildConditions = () => RebuildConditionChipList(
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
                AICondition blank = new AICondition
                {
                    conditionType = AIConditionType.HpRatio,
                    compareOp = CompareOp.LessEqual,
                    operandA = 30,
                    operandB = 0,
                    filter = new TargetFilter(),
                };
                newArr[newCount - 1] = blank;
                working.conditions = newArr;
                rebuildConditions();

                int addedIdx = newCount - 1;
                ShowConditionEditorDialog(blank, updated =>
                {
                    if (working.conditions != null && addedIdx < working.conditions.Length)
                    {
                        working.conditions[addedIdx] = updated;
                    }
                    rebuildConditions();
                });
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
            durationField.schedule.Execute(() => durationField.Focus()).ExecuteLater(k_DialogFocusDelayMs);
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

        // =========================================================================
        // Target Select / Target Rule 編集UI (JudgmentLoop 第1層: ターゲット選択)
        // =========================================================================

        private static AITargetSelect CloneTargetSelect(AITargetSelect source)
        {
            return new AITargetSelect
            {
                sortKey = source.sortKey,
                elementFilter = source.elementFilter,
                isDescending = source.isDescending,
                filter = source.filter,
            };
        }

        private static string GetSortKeyLabel(TargetSortKey k)
        {
            switch (k)
            {
                case TargetSortKey.Distance:        return "距離";
                case TargetSortKey.HpRatio:         return "HP割合";
                case TargetSortKey.HpValue:         return "HP値";
                case TargetSortKey.AttackPower:     return "攻撃力";
                case TargetSortKey.DefensePower:    return "防御力";
                case TargetSortKey.TargetingCount:  return "狙われ数";
                case TargetSortKey.LastAttacker:    return "最終攻撃者";
                case TargetSortKey.DamageScore:     return "ダメージスコア";
                case TargetSortKey.Self:            return "自分";
                case TargetSortKey.Player:          return "プレイヤー";
                case TargetSortKey.Sister:          return "妹";
                default:                            return k.ToString();
            }
        }

        /// <summary>
        /// AITargetSelect の概要を1行で返す。リスト表示・ドロップダウンラベル用。
        /// </summary>
        private static string FormatTargetSelectSummary(AITargetSelect ts)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append(GetSortKeyLabel(ts.sortKey));
            sb.Append(ts.isDescending ? "↓" : "↑");
            if (ts.elementFilter != 0)
            {
                sb.Append(" 弱点=").Append(ts.elementFilter.ToString());
            }
            string filterSummary = FormatFilterSummary(ts.filter);
            if (filterSummary != "条件なし（全員が対象）")
            {
                sb.Append(" / ").Append(filterSummary);
            }
            return sb.ToString();
        }

        /// <summary>
        /// ターゲット切替ルールの概要を1行で返す。参照先 targetSelects の内容もプレビューする。
        /// </summary>
        private static string FormatTargetRuleSummary(AIRule rule, AIMode mode)
        {
            int condCount = rule.conditions != null ? rule.conditions.Length : 0;
            string condText = condCount == 0 ? "常時" : condCount + "個の条件(AND)";
            string tsName = "#" + rule.actionIndex;
            if (mode.targetSelects != null && rule.actionIndex >= 0 && rule.actionIndex < mode.targetSelects.Length)
            {
                tsName = "#" + rule.actionIndex + " " + FormatTargetSelectSummary(mode.targetSelects[rule.actionIndex]);
            }
            string prob = rule.probability == 100 ? "" : " (" + rule.probability + "%)";
            return condText + " → " + tsName + prob;
        }

        /// <summary>
        /// ターゲット選定リスト (AITargetSelect[]) を描画する。
        /// 削除時に targetRules の actionIndex を自動補正する（参照ルール破棄 + より大きい index の decrement）。
        /// </summary>
        private void RebuildTargetSelectList(
            VisualElement container,
            Func<AITargetSelect[]> getSelects,
            Action<AITargetSelect[]> setSelects,
            Action rebuild,
            Func<AIRule[]> getTargetRules,
            Action<AIRule[]> setTargetRules)
        {
            container.Clear();

            AITargetSelect[] selects = getSelects();
            int count = selects != null ? selects.Length : 0;
            if (count == 0)
            {
                Label empty = new Label("(ターゲット選定が空です - 第1層判定が機能しません)");
                empty.AddToClassList("mode-detail-empty");
                container.Add(empty);
                return;
            }

            for (int i = 0; i < count; i++)
            {
                int idx = i;
                AITargetSelect ts = selects[idx];
                VisualElement row = new VisualElement();
                row.AddToClassList("mode-detail-row");

                Label indexLabel = new Label("[" + idx + "]");
                indexLabel.AddToClassList("mode-detail-row__index");
                row.Add(indexLabel);

                Label label = new Label(FormatTargetSelectSummary(ts));
                label.AddToClassList("mode-detail-row__label");
                row.Add(label);

                Button editButton = new Button(() =>
                {
                    AITargetSelect[] arr = getSelects();
                    if (arr == null || idx >= arr.Length)
                    {
                        return;
                    }
                    ShowTargetSelectEditDialog(arr[idx], edited =>
                    {
                        // Remove 経路と同じ「新配列生成→setter」パターンに揃える。
                        // in-place 書き換え（current[idx] = edited）だと setter が呼ばれないため、
                        // 親スコープが配列参照を差し替えて監視している場合に検出漏れする。
                        AITargetSelect[] current = getSelects();
                        if (current == null || idx >= current.Length)
                        {
                            return;
                        }
                        AITargetSelect[] newArr = new AITargetSelect[current.Length];
                        for (int k = 0; k < current.Length; k++)
                        {
                            newArr[k] = k == idx ? edited : current[k];
                        }
                        setSelects(newArr);
                        rebuild();
                    });
                });
                editButton.text = "編集";
                editButton.tooltip = "このターゲット選定を編集";
                editButton.AddToClassList("mode-detail-row__button");
                editButton.AddToClassList("secondary-button");
                row.Add(editButton);

                Button removeButton = new Button(() =>
                {
                    AITargetSelect[] arr = getSelects();
                    if (arr == null || arr.Length == 0)
                    {
                        return;
                    }
                    AITargetSelect[] newArr = new AITargetSelect[arr.Length - 1];
                    int dst = 0;
                    for (int k = 0; k < arr.Length; k++)
                    {
                        if (k == idx)
                        {
                            continue;
                        }
                        newArr[dst++] = arr[k];
                    }
                    setSelects(newArr);

                    // targetRules のインデックス整合: Logic 層の純粋関数を経由（テストで直接検証される）
                    AIRule[] rules = getTargetRules();
                    if (rules != null && rules.Length > 0)
                    {
                        setTargetRules(CompanionAISettingsLogic.AdjustTargetRulesForRemovedSelect(rules, idx));
                    }

                    rebuild();
                });
                removeButton.text = "×";
                removeButton.tooltip = "このターゲット選定を削除（参照するルールも自動調整）";
                removeButton.AddToClassList("mode-detail-row__button");
                removeButton.AddToClassList("danger-button");
                row.Add(removeButton);

                container.Add(row);
            }

            AttachTooltipHandlers(container);
        }

        /// <summary>
        /// ターゲット切替ルールリスト (AIRule[]) を描画する。actionRules と同型の getter/setter closure パターン。
        /// </summary>
        private void RebuildTargetRuleList(
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
                Label empty = new Label("(ルールなし - 第1層判定は何もしません)");
                empty.AddToClassList("mode-detail-empty");
                container.Add(empty);
                return;
            }

            AIMode parentMode = getParentMode();

            // ルールはあるが targetSelects が空の場合、このモードの第1層判定は機能しない状態。警告を出す。
            if (parentMode.targetSelects == null || parentMode.targetSelects.Length == 0)
            {
                Label warn = new Label("⚠ ターゲット選定が空のため、これらのルールは参照先が無く機能しません");
                warn.AddToClassList("mode-detail-empty");
                container.Add(warn);
            }

            for (int i = 0; i < count; i++)
            {
                int idx = i;
                AIRule rule = rules[idx];
                VisualElement row = new VisualElement();
                row.AddToClassList("mode-detail-row");

                Label priority = new Label("#" + (idx + 1));
                priority.AddToClassList("mode-detail-row__index");
                row.Add(priority);

                Label summary = new Label(FormatTargetRuleSummary(rule, parentMode));
                summary.AddToClassList("mode-detail-row__label");
                row.Add(summary);

                Button editButton = new Button(() =>
                {
                    AIRule[] arr = getRules();
                    if (arr == null || idx >= arr.Length)
                    {
                        return;
                    }
                    ShowTargetRuleEditDialog(arr[idx], getParentMode(), edited =>
                    {
                        // Remove 経路と同じ「新配列生成→setter」パターンに揃える。
                        // setter 経由にすることで親 closure の再代入・Dirty 通知経路が一貫する。
                        AIRule[] current = getRules();
                        if (current == null || idx >= current.Length)
                        {
                            return;
                        }
                        AIRule[] newArr = new AIRule[current.Length];
                        for (int k = 0; k < current.Length; k++)
                        {
                            newArr[k] = k == idx ? edited : current[k];
                        }
                        setRules(newArr);
                        rebuild();
                    });
                });
                editButton.text = "編集";
                editButton.tooltip = "このターゲットルールを編集";
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

        /// <summary>
        /// AITargetSelect 1件の編集ダイアログ。sortKey / isDescending / elementFilter / TargetFilter を編集。
        /// </summary>
        private void ShowTargetSelectEditDialog(AITargetSelect initial, Action<AITargetSelect> onConfirm)
        {
            AITargetSelect working = CloneTargetSelect(initial);

            VisualElement dialog = BuildModalDialog("ターゲット選定編集", "並び順 + 属性/陣営フィルタで候補を絞り込みます。");
            dialog.AddToClassList("target-select-edit-dialog");

            ScrollView scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("mode-detail-scroll");
            dialog.Add(scroll);

            // === 並び順 ===
            VisualElement sortSection = BuildDetailSection("並び順");
            scroll.Add(sortSection);

            TargetSortKey[] allSortKeys = (TargetSortKey[])Enum.GetValues(typeof(TargetSortKey));
            List<string> sortChoices = new List<string>();
            for (int i = 0; i < allSortKeys.Length; i++)
            {
                sortChoices.Add(GetSortKeyLabel(allSortKeys[i]));
            }
            int initialSortIndex = Array.IndexOf(allSortKeys, working.sortKey);
            if (initialSortIndex < 0) initialSortIndex = 0;

            DropdownField sortDropdown = new DropdownField("並び順キー", sortChoices, initialSortIndex);
            sortDropdown.tooltip = "候補の中から最優先を1体選ぶための判断基準";
            sortDropdown.RegisterValueChangedCallback(evt =>
            {
                int newIdx = sortDropdown.index;
                if (newIdx >= 0 && newIdx < allSortKeys.Length)
                {
                    working.sortKey = allSortKeys[newIdx];
                }
            });
            sortSection.Add(sortDropdown);

            Toggle descToggle = new Toggle("降順 (大きい順)");
            descToggle.tooltip = "OFF: 小さい順（例: 距離の昇順 = 近い順） / ON: 大きい順";
            descToggle.value = working.isDescending;
            descToggle.RegisterValueChangedCallback(evt => working.isDescending = evt.newValue);
            sortSection.Add(descToggle);

            // === 弱点属性フィルタ ===
            VisualElement elemSection = BuildDetailSection("弱点属性フィルタ (候補絞り込み)");
            scroll.Add(elemSection);

            VisualElement elemFlags = new VisualElement();
            elemFlags.AddToClassList("condition-row__flags");
            foreach (object v in Enum.GetValues(typeof(Element)))
            {
                Element elem = (Element)v;
                if ((int)elem == 0)
                {
                    continue;
                }
                Element captured = elem;
                Toggle t = new Toggle(GetElementLabel(elem));
                t.tooltip = "指定属性に弱い候補のみ対象。未指定=属性フィルタなし。";
                t.value = (working.elementFilter & captured) != 0;
                t.AddToClassList("condition-row__flag-toggle");
                t.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue)
                    {
                        working.elementFilter |= captured;
                    }
                    else
                    {
                        working.elementFilter &= ~captured;
                    }
                });
                elemFlags.Add(t);
            }
            elemSection.Add(elemFlags);

            // === 対象キャラクターフィルタ (既存 BuildTargetFilterSection 再利用) ===
            VisualElement filterSection = BuildDetailSection("対象キャラクターフィルタ");
            scroll.Add(filterSection);

            VisualElement filterUi = BuildTargetFilterSection(working.filter, updated =>
            {
                working.filter = updated;
            });
            filterSection.Add(filterUi);

            // === ボタン列 ===
            VisualElement buttons = BuildButtonRow();
            buttons.Add(BuildDialogButton("保存", "primary-button", () => onConfirm?.Invoke(working)));
            buttons.Add(BuildDialogButton("キャンセル", "secondary-button", null));
            dialog.Add(buttons);

            ShowDialog(dialog);
            AttachTooltipHandlers(dialog);
        }

        /// <summary>
        /// AIRule (ターゲット切替ルール) 1件の編集ダイアログ。
        /// actionIndex の選択肢は parentMode.targetSelects を参照する。
        /// </summary>
        private void ShowTargetRuleEditDialog(AIRule initialRule, AIMode parentMode, Action<AIRule> onConfirm)
        {
            AIRule working = CloneRule(initialRule);

            VisualElement dialog = BuildModalDialog("ターゲット切替ルール編集", "条件はすべて AND 結合されます。ルール配列の順序が優先度（先勝ち）です。");
            dialog.AddToClassList("target-rule-edit-dialog");

            ScrollView scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("mode-detail-scroll");
            dialog.Add(scroll);

            // === 使用するターゲット選定 (actionIndex) ===
            VisualElement targetSection = BuildDetailSection("使用するターゲット選定");
            scroll.Add(targetSection);

            List<string> tsChoices = new List<string>();
            int tsCount = parentMode.targetSelects != null ? parentMode.targetSelects.Length : 0;
            for (int i = 0; i < tsCount; i++)
            {
                tsChoices.Add("#" + i + " " + FormatTargetSelectSummary(parentMode.targetSelects[i]));
            }
            if (tsChoices.Count == 0)
            {
                tsChoices.Add("(ターゲット選定が空です)");
            }

            DropdownField tsDropdown = new DropdownField("ターゲット選定", tsChoices, Mathf.Clamp(working.actionIndex, 0, tsChoices.Count - 1));
            tsDropdown.tooltip = "このルールが満たされた時に使うターゲット選定パターン";
            tsDropdown.RegisterValueChangedCallback(evt =>
            {
                int newIndex = tsDropdown.index;
                if (newIndex >= 0)
                {
                    working.actionIndex = newIndex;
                }
            });
            targetSection.Add(tsDropdown);

            Slider probSlider = new Slider("発動確率(%)", 0, 100);
            probSlider.value = working.probability;
            probSlider.tooltip = "ルール条件成立時にこのターゲット選定を使う確率(0-100)";
            probSlider.showInputField = true;
            probSlider.RegisterValueChangedCallback(evt =>
            {
                working.probability = (byte)Mathf.Clamp(Mathf.RoundToInt(evt.newValue), 0, 100);
            });
            targetSection.Add(probSlider);

            // === 条件（Phase 5: チップ式） ===
            VisualElement conditionsSection = BuildDetailSection("条件 (AND結合)");
            scroll.Add(conditionsSection);

            VisualElement conditionsList = new VisualElement();
            conditionsList.AddToClassList("condition-chip-list");
            conditionsSection.Add(conditionsList);

            Action rebuildConditions = null;
            rebuildConditions = () => RebuildConditionChipList(
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
                AICondition blank = new AICondition
                {
                    conditionType = AIConditionType.HpRatio,
                    compareOp = CompareOp.LessEqual,
                    operandA = 50,
                    operandB = 0,
                    filter = new TargetFilter(),
                };
                newArr[newCount - 1] = blank;
                working.conditions = newArr;
                rebuildConditions();

                int addedIdx = newCount - 1;
                ShowConditionEditorDialog(blank, updated =>
                {
                    if (working.conditions != null && addedIdx < working.conditions.Length)
                    {
                        working.conditions[addedIdx] = updated;
                    }
                    rebuildConditions();
                });
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
    }
}
