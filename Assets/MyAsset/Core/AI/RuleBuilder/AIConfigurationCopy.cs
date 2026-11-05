using System;

namespace Game.Core
{
    /// <summary>
    /// 編集用managed設定を保存・実行の所有境界で分離する。Nativeの共有状態や毎tickでは使わない。
    /// 文字列は不変なので共有し、配列と入れ子の条件配列だけをコピーする。
    /// </summary>
    public static class AIConfigurationCopy
    {
        public static AIRule Clone(AIRule source)
        {
            source.conditions = source.conditions == null ? null : (AICondition[])source.conditions.Clone();
            return source;
        }

        public static ModeTransitionRule Clone(ModeTransitionRule source)
        {
            source.conditions = source.conditions == null ? null : (AICondition[])source.conditions.Clone();
            return source;
        }

        public static AIMode Clone(AIMode source)
        {
            source.actions = source.actions == null ? null : (ActionSlot[])source.actions.Clone();
            source.targetSelects = source.targetSelects == null ? null : (AITargetSelect[])source.targetSelects.Clone();
            source.targetRules = CloneRules(source.targetRules);
            source.actionRules = CloneRules(source.actionRules);
            return source;
        }

        public static AIMode[] CloneModes(AIMode[] source)
        {
            if (source == null) { return null; }
            if (source.Length == 0) { return Array.Empty<AIMode>(); }
            AIMode[] result = new AIMode[source.Length];
            for (int i = 0; i < source.Length; i++) { result[i] = Clone(source[i]); }
            return result;
        }

        public static AIRule[] CloneRules(AIRule[] source)
        {
            if (source == null) { return null; }
            if (source.Length == 0) { return Array.Empty<AIRule>(); }
            AIRule[] result = new AIRule[source.Length];
            for (int i = 0; i < source.Length; i++) { result[i] = Clone(source[i]); }
            return result;
        }

        public static ModeTransitionRule[] CloneTransitions(ModeTransitionRule[] source)
        {
            if (source == null) { return null; }
            if (source.Length == 0) { return Array.Empty<ModeTransitionRule>(); }
            ModeTransitionRule[] result = new ModeTransitionRule[source.Length];
            for (int i = 0; i < source.Length; i++) { result[i] = Clone(source[i]); }
            return result;
        }

        public static CompanionAIConfig Clone(CompanionAIConfig source)
        {
            source.modes = CloneModes(source.modes);
            source.modeTransitionRules = CloneTransitions(source.modeTransitionRules);
            source.shortcutModeBindings = source.shortcutModeBindings == null
                ? null : (int[])source.shortcutModeBindings.Clone();
            return source;
        }
    }
}
