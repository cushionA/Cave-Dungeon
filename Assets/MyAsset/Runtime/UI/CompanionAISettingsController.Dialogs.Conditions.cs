// 条件チップ/条件編集ダイアログ関連 (AICondition の UI) を担当する partial。
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Game.Core;

namespace Game.Runtime
{
    /// <summary>
    /// CompanionAISettingsController の partial。
    /// 条件チップリスト・条件エディタダイアログ・条件入力ウィジェット・CompareOp 正規化を担当する。
    /// </summary>
    public partial class CompanionAISettingsController
    {
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
    }
}
