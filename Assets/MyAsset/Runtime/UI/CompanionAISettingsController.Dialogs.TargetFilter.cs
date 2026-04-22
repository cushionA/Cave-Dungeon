// ターゲット選定/切替ルール/TargetFilter 編集 UI (JudgmentLoop 第1層) を担当する partial。
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Game.Core;

namespace Game.Runtime
{
    /// <summary>
    /// CompanionAISettingsController の partial。
    /// TargetFilter Foldout、AITargetSelect/AIRule リスト、編集ダイアログを担当する。
    /// </summary>
    public partial class CompanionAISettingsController
    {
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
