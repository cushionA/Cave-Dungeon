using System;

namespace Game.Core
{
    public abstract class CoopActionBase
    {
        public abstract string ActionName { get; }
        public abstract int MpCost { get; }
        public abstract float CooldownDuration { get; }
        public abstract int MaxComboCount { get; }
        public abstract float ComboInputWindow { get; }

        public abstract void ExecuteCombo(int comboIndex, int companionHash, int targetHash);
        public virtual void OnComboEnd(int companionHash) { }
    }

    public class CoopActionManager : IDisposable
    {
        private CoopActionBase _currentAction;
        private CoopInterruptionHandler _interruptionHandler;
        private ComboWindowTimer _comboTimer;
        private int _companionHash;
        private int _currentComboIndex;
        private bool _isInCombo;

        public bool IsInCombo => _isInCombo;
        public int CurrentComboIndex => _currentComboIndex;

        public event Action<int> OnCoopActivated;

        public CoopActionManager(int companionHash, CoopInterruptionHandler interruptionHandler)
        {
            _companionHash = companionHash;
            _interruptionHandler = interruptionHandler;
            _comboTimer = new ComboWindowTimer();
        }

        public bool Activate(CoopActionBase action, int targetHash,
            bool isPlayerAlive, bool isCompanionStaggered,
            ActionSlot currentCompanionSlot, int currentCompanionTarget)
        {
            if (!isPlayerAlive)
            {
                return false;
            }

            if (isCompanionStaggered)
            {
                return false;
            }

            if (_isInCombo && _currentAction == action)
            {
                return ContinueCombo(targetHash);
            }

            _interruptionHandler.InterruptForCoop(currentCompanionSlot, currentCompanionTarget);
            _currentAction = action;
            _currentComboIndex = 0;
            _isInCombo = true;

            action.ExecuteCombo(0, _companionHash, targetHash);
            _comboTimer.Open(action.ComboInputWindow, action.MaxComboCount);

            OnCoopActivated?.Invoke(_companionHash);
            return true;
        }

        public void Tick(float deltaTime)
        {
            if (!_isInCombo)
            {
                return;
            }

            _comboTimer.Tick(deltaTime);
            if (!_comboTimer.IsOpen)
            {
                EndCombo();
            }
        }

        private bool ContinueCombo(int targetHash)
        {
            if (!_comboTimer.TryAdvance())
            {
                EndCombo();
                return false;
            }

            _currentComboIndex++;
            _currentAction.ExecuteCombo(_currentComboIndex, _companionHash, targetHash);
            return true;
        }

        private void EndCombo()
        {
            _isInCombo = false;
            _currentAction?.OnComboEnd(_companionHash);
            _currentAction = null;
            _currentComboIndex = 0;
            _comboTimer.Reset();
            _interruptionHandler.ResumeFromCoop();
        }

        public void Dispose()
        {
            OnCoopActivated = null;
        }
    }
}
