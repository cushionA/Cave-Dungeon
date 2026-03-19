namespace Game.Core
{
    public class CoopCooldownTracker
    {
        private float _cooldownDuration;
        private float _cooldownEndTime;
        private bool _hasCooldown;

        public bool IsCooldownReady(float currentTime)
        {
            if (!_hasCooldown)
            {
                return true;
            }
            return currentTime >= _cooldownEndTime;
        }

        public float GetRemaining(float currentTime)
        {
            if (!_hasCooldown)
            {
                return 0f;
            }
            float remaining = _cooldownEndTime - currentTime;
            return remaining > 0f ? remaining : 0f;
        }

        public struct ActivationResult
        {
            public bool success;
            public bool isFree;
            public int mpConsumed;
        }

        public ActivationResult TryActivate(float currentTime, int mpCost, int currentMp,
            float cooldownDuration)
        {
            ActivationResult result = new ActivationResult();

            if (IsCooldownReady(currentTime))
            {
                result.success = true;
                result.isFree = true;
                result.mpConsumed = 0;
                _cooldownDuration = cooldownDuration;
                _cooldownEndTime = currentTime + cooldownDuration;
                _hasCooldown = true;
            }
            else
            {
                if (currentMp < mpCost)
                {
                    result.success = false;
                    return result;
                }

                result.success = true;
                result.isFree = false;
                result.mpConsumed = mpCost;
            }

            return result;
        }

        public void Reset()
        {
            _hasCooldown = false;
            _cooldownEndTime = 0f;
        }
    }
}
