using System;

namespace Game.Core
{
    /// <summary>描画更新と固定更新が入力の時間を二重に進めないための共通時計。</summary>
    public sealed class InputSamplingClock
    {
        private float _lastTime;

        public void Reset(float currentTime)
        {
            Validate(currentTime);
            _lastTime = currentTime;
        }

        public float Advance(float currentTime)
        {
            Validate(currentTime);
            if (currentTime <= _lastTime)
            {
                return 0f;
            }
            float deltaTime = currentTime - _lastTime;
            _lastTime = currentTime;
            return deltaTime;
        }

        private static void Validate(float currentTime)
        {
            if (float.IsNaN(currentTime) || float.IsInfinity(currentTime))
            {
                throw new ArgumentOutOfRangeException(nameof(currentTime));
            }
        }
    }
}
