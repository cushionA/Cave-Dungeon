namespace Game.Core
{
    /// <summary>
    /// Manages shortcut-based mode overrides for the companion AI.
    /// Shortcuts temporarily switch the active mode with an auto-revert timeout.
    /// </summary>
    public class ModeTransitionEditor
    {
        private ModeController _modeController;
        private int[] _shortcutBindings;
        private float _overrideTimeout;
        private float _overrideTimer;
        private int _preOverrideModeIndex;
        private bool _isOverriding;

        public bool IsOverriding => _isOverriding;

        public ModeTransitionEditor(ModeController modeController, float overrideTimeout = 15f)
        {
            _modeController = modeController;
            _overrideTimeout = overrideTimeout;
            _shortcutBindings = new int[] { 0, 1, 2, 3 };
        }

        /// <summary>
        /// Binds a shortcut index (0-3) to a mode index.
        /// </summary>
        public void SetShortcutBinding(int shortcutIndex, int modeIndex)
        {
            if (shortcutIndex >= 0 && shortcutIndex < _shortcutBindings.Length)
            {
                _shortcutBindings[shortcutIndex] = modeIndex;
            }
        }

        /// <summary>
        /// Activates a shortcut, switching to the bound mode.
        /// Saves the current mode for auto-revert on timeout.
        /// </summary>
        public void ActivateShortcut(int shortcutIndex)
        {
            if (shortcutIndex < 0 || shortcutIndex >= _shortcutBindings.Length)
            {
                return;
            }

            if (!_isOverriding)
            {
                _preOverrideModeIndex = _modeController.CurrentModeIndex;
            }

            _modeController.SwitchMode(_shortcutBindings[shortcutIndex]);
            _isOverriding = true;
            _overrideTimer = _overrideTimeout;
        }

        /// <summary>
        /// Ticks the override timer. Reverts to the pre-override mode when timeout expires.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (!_isOverriding)
            {
                return;
            }

            _overrideTimer -= deltaTime;
            if (_overrideTimer <= 0f)
            {
                _isOverriding = false;
                _modeController.SwitchMode(_preOverrideModeIndex);
            }
        }
    }
}
