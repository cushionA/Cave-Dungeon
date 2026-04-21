namespace Game.Core
{
    /// <summary>
    /// ActionSlot を人間可読なラベルに変換する静的ヘルパー。
    /// UI（CompanionAISettings のモード詳細・行動ピッカー）で使用。
    /// AttackInfo の配列経由でしか取得できない Attack/Cast の表示名は
    /// 呼び出し側で解決して渡す前提で、ここでは execType / paramId 依存の
    /// 静的な名前（Instant/Sustained/Broadcast）と、共通のフォールバックを提供する。
    /// </summary>
    public static class ActionSlotLabelTable
    {
        /// <summary>
        /// InstantAction の日本語表示名。
        /// </summary>
        public static string GetInstantActionLabel(InstantAction action)
        {
            switch (action)
            {
                case InstantAction.Dodge:           return "回避";
                case InstantAction.WarpToTarget:    return "ターゲットへワープ";
                case InstantAction.WarpBehind:      return "背後へワープ";
                case InstantAction.UseItem:         return "アイテム使用";
                case InstantAction.InteractObject:  return "オブジェクト操作";
                case InstantAction.BodySlam:        return "体当たり";
                default:                            return "不明(Instant)";
            }
        }

        /// <summary>
        /// SustainedAction の日本語表示名。
        /// </summary>
        public static string GetSustainedActionLabel(SustainedAction action)
        {
            switch (action)
            {
                case SustainedAction.MoveToTarget:  return "目標地点へ移動";
                case SustainedAction.Follow:        return "追従";
                case SustainedAction.Retreat:       return "後退";
                case SustainedAction.Flee:          return "逃走";
                case SustainedAction.Patrol:        return "巡回";
                case SustainedAction.Guard:         return "ガード";
                case SustainedAction.Flank:         return "挟撃位置取り";
                case SustainedAction.ShieldDeploy:  return "盾展開";
                case SustainedAction.Decoy:         return "囮";
                case SustainedAction.Cover:         return "遮蔽物利用";
                case SustainedAction.Stealth:       return "潜伏";
                case SustainedAction.Orbit:         return "周回";
                case SustainedAction.MpRecover:     return "MP回復専念";
                case SustainedAction.Idle:          return "何もしない";
                default:                            return "不明(Sustained)";
            }
        }

        /// <summary>
        /// BroadcastAction の日本語表示名。
        /// </summary>
        public static string GetBroadcastActionLabel(BroadcastAction action)
        {
            switch (action)
            {
                case BroadcastAction.DesignateTarget: return "ターゲット指示";
                case BroadcastAction.Rally:           return "集合指示";
                case BroadcastAction.Scatter:         return "散開指示";
                case BroadcastAction.Taunt:           return "挑発";
                case BroadcastAction.FocusFire:       return "集中攻撃指示";
                case BroadcastAction.Disengage:       return "戦闘離脱指示";
                case BroadcastAction.ModeSync:        return "モード同期";
                default:                              return "不明(Broadcast)";
            }
        }

        /// <summary>
        /// 攻撃カテゴリの日本語表示名（ActionPicker のタブ名などに使用）。
        /// </summary>
        public static string GetAttackCategoryLabel(AttackCategory category)
        {
            switch (category)
            {
                case AttackCategory.Melee:   return "近接";
                case AttackCategory.Ranged:  return "遠距離";
                case AttackCategory.Magic:   return "魔法";
                case AttackCategory.Skill:   return "スキル";
                case AttackCategory.Support: return "支援";
                case AttackCategory.Summon:  return "召喚";
                default:                     return "不明";
            }
        }

        /// <summary>
        /// ActionExecType の日本語表示名。
        /// </summary>
        public static string GetExecTypeLabel(ActionExecType execType)
        {
            switch (execType)
            {
                case ActionExecType.Attack:    return "攻撃";
                case ActionExecType.Cast:      return "詠唱/発射";
                case ActionExecType.Instant:   return "即時行動";
                case ActionExecType.Sustained: return "継続行動";
                case ActionExecType.Broadcast: return "号令";
                default:                       return "不明";
            }
        }

        /// <summary>
        /// ActionSlot の displayName が空の場合にフォールバックラベルを組み立てる。
        /// Attack/Cast は呼び出し側で AttackInfo.attackName を代入する想定。
        /// </summary>
        public static string GetFallbackLabel(ActionSlot slot)
        {
            if (!string.IsNullOrEmpty(slot.displayName))
            {
                return slot.displayName;
            }

            switch (slot.execType)
            {
                case ActionExecType.Attack:
                    return "攻撃 #" + slot.paramId;
                case ActionExecType.Cast:
                    return "詠唱 #" + slot.paramId;
                case ActionExecType.Instant:
                    return GetInstantActionLabel((InstantAction)slot.paramId);
                case ActionExecType.Sustained:
                    return GetSustainedActionLabel((SustainedAction)slot.paramId);
                case ActionExecType.Broadcast:
                    return GetBroadcastActionLabel((BroadcastAction)slot.paramId);
                default:
                    return "不明な行動";
            }
        }
    }
}
