namespace Game.Core
{
    /// <summary>
    /// AI difficulty levels that control deliberation delay ranges.
    /// </summary>
    public enum AIDifficulty : byte
    {
        Easy,
        Normal,
        Hard,
    }

    /// <summary>
    /// Adds a human-like reaction delay before AI actions execute.
    /// Delay range depends on AIDifficulty setting.
    /// </summary>
    public class DeliberationBuffer
    {
        private const float k_EasyMin = 0.3f;
        private const float k_EasyMax = 0.6f;
        private const float k_NormalMin = 0.1f;
        private const float k_NormalMax = 0.33f;
        private const float k_HardMin = 0.03f;
        private const float k_HardMax = 0.13f;

        private AIDifficulty _difficulty;
        private float _bufferTimer;
        private bool _isBuffering;
        private System.Action _pendingAction;

        public bool IsBuffering => _isBuffering;

        public DeliberationBuffer(AIDifficulty difficulty)
        {
            _difficulty = difficulty;
        }

        /// <summary>
        /// Queues an action with a randomized delay based on difficulty.
        /// randomValue should be in [0, 1] range.
        /// </summary>
        public void Buffer(System.Action action, float randomValue)
        {
            _pendingAction = action;
            _bufferTimer = GetDelay(randomValue);
            _isBuffering = true;
        }

        /// <summary>
        /// Ticks the buffer timer. Executes the pending action when delay expires.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (!_isBuffering)
            {
                return;
            }

            _bufferTimer -= deltaTime;
            if (_bufferTimer <= 0f)
            {
                _isBuffering = false;
                _pendingAction?.Invoke();
                _pendingAction = null;
            }
        }

        /// <summary>
        /// Calculates the delay for a given random value [0, 1] based on current difficulty.
        /// </summary>
        public float GetDelay(float randomValue)
        {
            float min;
            float max;

            switch (_difficulty)
            {
                case AIDifficulty.Easy:
                    min = k_EasyMin;
                    max = k_EasyMax;
                    break;
                case AIDifficulty.Normal:
                    min = k_NormalMin;
                    max = k_NormalMax;
                    break;
                case AIDifficulty.Hard:
                    min = k_HardMin;
                    max = k_HardMax;
                    break;
                default:
                    min = k_NormalMin;
                    max = k_NormalMax;
                    break;
            }

            return min + (max - min) * randomValue;
        }
    }
}
