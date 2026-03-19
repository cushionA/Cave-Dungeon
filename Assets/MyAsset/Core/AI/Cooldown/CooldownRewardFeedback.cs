using System;

namespace Game.Core
{
    /// <summary>
    /// Monitors cooldown state transitions and fires feedback events
    /// when the cooldown becomes ready or a free activation occurs.
    /// </summary>
    public class CooldownRewardFeedback
    {
        public event Action OnCooldownReady;
        public event Action OnFreeActivation;

        private bool _wasCooldownActive;

        /// <summary>
        /// Call each frame/tick. Detects transition from cooldown-active to cooldown-ready
        /// and fires OnCooldownReady when that transition occurs.
        /// </summary>
        public void Update(CoopCooldownTracker tracker, float currentTime)
        {
            bool isReady = tracker.IsCooldownReady(currentTime);

            if (_wasCooldownActive && isReady)
            {
                OnCooldownReady?.Invoke();
            }

            _wasCooldownActive = !isReady;
        }

        /// <summary>
        /// Explicitly notifies that a free activation occurred (cooldown was ready).
        /// </summary>
        public void NotifyFreeActivation()
        {
            OnFreeActivation?.Invoke();
        }
    }
}
