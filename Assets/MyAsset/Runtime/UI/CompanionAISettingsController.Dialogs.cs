// モード詳細/モード遷移/統合行動ルール編集ダイアログ (コアダイアログ群) を担当する partial。
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Game.Core;

namespace Game.Runtime
{
    /// <summary>
    /// CompanionAISettingsController の partial。
    /// モード詳細ダイアログ / モード遷移ダイアログ / 統合行動ルール編集ダイアログ と、
    /// その内部で共通利用される小さいヘルパ (BuildDetailSection / CloneMode 系) を持つ。
    /// TargetFilter / Conditions / ActionPicker は同名の別 partial ファイルに分割している。
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
        /// internal にしてあるのは EditMode 結合テスト用途（InternalsVisibleTo で公開）。
        /// </summary>
        internal void RebuildUnifiedActionList(
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
        // Helpers (Mode/Rule clone, action slot label, detail section wrapper)
        // =========================================================================

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
