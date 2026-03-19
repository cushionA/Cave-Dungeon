using System;

namespace Game.Core
{
    /// <summary>
    /// Rule for transitioning between AI modes based on conditions.
    /// </summary>
    [Serializable]
    public struct ModeTransitionRule
    {
        public AICondition[] conditions;
        public int targetModeIndex;
    }

    /// <summary>
    /// Manages AI mode transitions. Evaluates transition rules and switches
    /// the JudgmentLoop's active mode when conditions are met.
    /// </summary>
    public class ModeController
    {
        private AIMode[] _modes;
        private ModeTransitionRule[] _transitionRules;
        private int _currentModeIndex;
        private JudgmentLoop _judgmentLoop;

        public int CurrentModeIndex => _currentModeIndex;
        public AIMode CurrentMode => _modes != null && _currentModeIndex < _modes.Length ? _modes[_currentModeIndex] : default;

        public event Action<int> OnModeChanged;

        public ModeController(JudgmentLoop judgmentLoop)
        {
            _judgmentLoop = judgmentLoop;
        }

        /// <summary>
        /// Sets available modes and transition rules. Initializes to mode 0.
        /// </summary>
        public void SetModes(AIMode[] modes, ModeTransitionRule[] transitionRules)
        {
            _modes = modes;
            _transitionRules = transitionRules;
            if (_modes != null && _modes.Length > 0)
            {
                SwitchMode(0);
            }
        }

        /// <summary>
        /// Evaluates transition rules in order. First matching rule triggers a mode switch.
        /// Does not switch if already in the target mode.
        /// </summary>
        public void EvaluateTransitions(int ownerHash, int targetHash,
            SoACharaDataDic data, float currentTime)
        {
            if (_transitionRules == null)
            {
                return;
            }

            for (int i = 0; i < _transitionRules.Length; i++)
            {
                ModeTransitionRule rule = _transitionRules[i];
                if (ConditionEvaluator.EvaluateAll(rule.conditions, ownerHash, targetHash, data, currentTime))
                {
                    if (rule.targetModeIndex != _currentModeIndex)
                    {
                        SwitchMode(rule.targetModeIndex);
                    }
                    return;
                }
            }
        }

        /// <summary>
        /// Switches to the specified mode index and updates the JudgmentLoop.
        /// </summary>
        public void SwitchMode(int modeIndex)
        {
            if (_modes == null || modeIndex < 0 || modeIndex >= _modes.Length)
            {
                return;
            }

            _currentModeIndex = modeIndex;
            _judgmentLoop.SetMode(_modes[_currentModeIndex]);
            OnModeChanged?.Invoke(_currentModeIndex);
        }
    }
}
