using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// Serializable configuration for a companion AI setup.
    /// Contains modes, transition rules, and shortcut bindings.
    /// </summary>
    [Serializable]
    public struct CompanionAIConfig
    {
        /// <summary>
        /// 戦術プリセットの一意ID（GUID）。
        /// TacticalPresetRegistry で管理され、上書き保存やカスケード更新の対象を特定する。
        /// 空文字列/null の場合は「現在の戦術」など未保存の状態を表す。
        /// </summary>
        public string configId;

        /// <summary>戦術プリセットの表示名。空文字列/null の場合は未命名。</summary>
        public string configName;

        public AIMode[] modes;
        public ModeTransitionRule[] modeTransitionRules;
        public int[] shortcutModeBindings;
    }

    /// <summary>
    /// Logic for the AI rule editor UI. Manages mode list editing,
    /// transition rule configuration, and config building.
    /// </summary>
    public class RuleEditorLogic
    {
        private const int k_MaxModes = 4;

        private List<AIMode> _modes;
        private List<ModeTransitionRule> _transitionRules;
        private ActionTypeRegistry _registry;

        public IReadOnlyList<AIMode> Modes => _modes;
        public int ModeCount => _modes.Count;

        public RuleEditorLogic(ActionTypeRegistry registry)
        {
            _registry = registry;
            _modes = new List<AIMode>();
            _transitionRules = new List<ModeTransitionRule>();
        }

        /// <summary>
        /// Adds a new mode. Returns false if the maximum mode count has been reached.
        /// </summary>
        public bool AddMode(AIMode mode)
        {
            if (_modes.Count >= k_MaxModes)
            {
                return false;
            }

            _modes.Add(mode);
            return true;
        }

        /// <summary>
        /// Removes a mode at the given index. Cannot remove the last remaining mode.
        /// </summary>
        public bool RemoveMode(int index)
        {
            if (index < 0 || index >= _modes.Count || _modes.Count <= 1)
            {
                return false;
            }

            _modes.RemoveAt(index);
            return true;
        }

        /// <summary>
        /// Replaces the mode at the given index.
        /// </summary>
        public void SetMode(int index, AIMode mode)
        {
            if (index >= 0 && index < _modes.Count)
            {
                _modes[index] = mode;
            }
        }

        /// <summary>
        /// Adds a transition rule to the configuration.
        /// </summary>
        public void AddTransitionRule(ModeTransitionRule rule)
        {
            _transitionRules.Add(rule);
        }

        /// <summary>
        /// Builds a CompanionAIConfig from the current editor state.
        /// </summary>
        public CompanionAIConfig BuildConfig()
        {
            return new CompanionAIConfig
            {
                modes = _modes.ToArray(),
                modeTransitionRules = _transitionRules.ToArray(),
                shortcutModeBindings = new int[4]
            };
        }

        /// <summary>
        /// Checks whether the action in the given slot is unlocked in the registry.
        /// </summary>
        public bool IsActionAvailable(ActionSlot slot)
        {
            ActionUnlockKey key = new ActionUnlockKey
            {
                execType = slot.execType,
                paramId = slot.paramId
            };
            return _registry.IsUnlocked(key);
        }
    }
}
