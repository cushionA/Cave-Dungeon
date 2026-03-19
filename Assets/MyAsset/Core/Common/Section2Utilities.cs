using System.Collections.Generic;

namespace Game.Core
{
    public class ComboWindowTimer
    {
        private float _remainingTime;
        private int _currentStep;
        private int _maxSteps;
        private bool _isOpen;

        public bool IsOpen => _isOpen;
        public int CurrentStep => _currentStep;

        public void Open(float windowDuration, int maxSteps)
        {
            _remainingTime = windowDuration;
            _maxSteps = maxSteps;
            _currentStep = 0;
            _isOpen = true;
        }

        public bool TryAdvance()
        {
            if (!_isOpen)
            {
                return false;
            }

            _currentStep++;
            if (_currentStep >= _maxSteps)
            {
                Reset();
                return false;
            }

            return true;
        }

        public void Tick(float deltaTime)
        {
            if (!_isOpen)
            {
                return;
            }

            _remainingTime -= deltaTime;
            if (_remainingTime <= 0f)
            {
                Reset();
            }
        }

        public void Reset()
        {
            _isOpen = false;
            _remainingTime = 0f;
            _currentStep = 0;
            _maxSteps = 0;
        }
    }

    public class CooldownTracker
    {
        private Dictionary<int, float> _cooldownEndTimes;

        public CooldownTracker()
        {
            _cooldownEndTimes = new Dictionary<int, float>();
        }

        public bool IsReady(int key, float currentTime)
        {
            if (!_cooldownEndTimes.TryGetValue(key, out float endTime))
            {
                return true;
            }

            return currentTime >= endTime;
        }

        public void Start(int key, float duration, float currentTime)
        {
            _cooldownEndTimes[key] = currentTime + duration;
        }

        public float GetRemaining(int key, float currentTime)
        {
            if (!_cooldownEndTimes.TryGetValue(key, out float endTime))
            {
                return 0f;
            }

            float remaining = endTime - currentTime;
            return remaining > 0f ? remaining : 0f;
        }

        public void Clear()
        {
            _cooldownEndTimes.Clear();
        }
    }

    public class ActionInterruptHandler
    {
        private ActionSlot _savedSlot;
        private int _savedTargetHash;
        private bool _hasSaved;

        public bool HasSavedState => _hasSaved;

        public void Save(ActionSlot slot, int targetHash)
        {
            _savedSlot = slot;
            _savedTargetHash = targetHash;
            _hasSaved = true;
        }

        public (ActionSlot slot, int targetHash)? Restore()
        {
            if (!_hasSaved)
            {
                return null;
            }

            ActionSlot slot = _savedSlot;
            int hash = _savedTargetHash;
            Clear();
            return (slot, hash);
        }

        public void Clear()
        {
            _savedSlot = default;
            _savedTargetHash = 0;
            _hasSaved = false;
        }
    }
}
