// 行動ピッカーダイアログ関連 (タブ: 攻撃/詠唱/即時/継続/アイテム) を担当する partial。
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Game.Core;

namespace Game.Runtime
{
    /// <summary>
    /// CompanionAISettingsController の partial。
    /// 行動ピッカーポップアップと各タブ内容の構築、Sustained 継続時間ダイアログを担う。
    /// ダイアログ本体は Dialogs.cs 側から <see cref="ShowActionPickerDialog"/> 経由で開かれる。
    /// </summary>
    public partial class CompanionAISettingsController
    {
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

        private static bool IsCasterCategory(AttackCategory category)
        {
            // Magic/Skill/Support/Summon は Cast タブに出す
            return category == AttackCategory.Magic
                || category == AttackCategory.Skill
                || category == AttackCategory.Support
                || category == AttackCategory.Summon;
        }
    }
}
