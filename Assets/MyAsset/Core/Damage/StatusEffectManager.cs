namespace Game.Core
{
    public struct ActiveEffect
    {
        public StatusEffectId id;
        public float remainTime;
        public float tickTimer;
        public float tickInterval;
        public int tickDamage;
    }

    public class StatusEffectManager
    {
        public const float k_DefaultThreshold = 100f;
        public const float k_DefaultDuration = 10f;
        public const float k_DefaultTickInterval = 2f;
        public const int k_DefaultTickDamage = 5;

        private const int k_MaxStatusEffectTypes = 12; // StatusEffectId enum count
        private const int k_MaxActiveSlots = 3;

        private float[] _accumulations;
        private ActiveEffect[] _activeEffects;
        private int _activeCount;

        public int ActiveCount => _activeCount;

        public StatusEffectManager()
        {
            _accumulations = new float[k_MaxStatusEffectTypes];
            _activeEffects = new ActiveEffect[k_MaxActiveSlots];
            _activeCount = 0;
        }

        /// <summary>蓄積追加。耐性(statusCut)で軽減。閾値超過で発症。戻り値:発症したか。</summary>
        public bool Accumulate(StatusEffectId id, float value, float statusCut)
        {
            if (id == StatusEffectId.None)
            {
                return false;
            }

            float reducedValue = value * (1f - statusCut);
            int index = (int)id;
            _accumulations[index] += reducedValue;

            if (_accumulations[index] >= k_DefaultThreshold)
            {
                if (!IsActive(id) && _activeCount < k_MaxActiveSlots)
                {
                    _activeEffects[_activeCount] = new ActiveEffect
                    {
                        id = id,
                        remainTime = k_DefaultDuration,
                        tickTimer = 0f,
                        tickInterval = k_DefaultTickInterval,
                        tickDamage = k_DefaultTickDamage,
                    };
                    _activeCount++;
                    _accumulations[index] = 0f;
                    return true;
                }
            }

            return false;
        }

        /// <summary>効果Tick。deltaTime経過。戻り値:このフレームのtickダメージ合計。</summary>
        public int Tick(float deltaTime)
        {
            int totalDamage = 0;

            for (int i = _activeCount - 1; i >= 0; i--)
            {
                ActiveEffect effect = _activeEffects[i];
                effect.remainTime -= deltaTime;
                effect.tickTimer += deltaTime;

                while (effect.tickTimer >= effect.tickInterval)
                {
                    totalDamage += effect.tickDamage;
                    effect.tickTimer -= effect.tickInterval;
                }

                if (effect.remainTime <= 0f)
                {
                    // Remove by swapping with last element
                    _activeCount--;
                    if (i < _activeCount)
                    {
                        _activeEffects[i] = _activeEffects[_activeCount];
                    }
                }
                else
                {
                    _activeEffects[i] = effect;
                }
            }

            return totalDamage;
        }

        /// <summary>指定IDが発症中か。</summary>
        public bool IsActive(StatusEffectId id)
        {
            for (int i = 0; i < _activeCount; i++)
            {
                if (_activeEffects[i].id == id)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>全蓄積・発症をクリア。</summary>
        public void ClearAll()
        {
            for (int i = 0; i < _accumulations.Length; i++)
            {
                _accumulations[i] = 0f;
            }
            _activeCount = 0;
        }
    }
}
