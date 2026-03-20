using System;

namespace Game.Core
{
    /// <summary>
    /// Definition of a magic spell including cost, timing, bullet profile, and effects.
    /// </summary>
    [Serializable]
    public struct MagicDefinition
    {
        public string magicName;
        public int magicId;
        public MagicType magicType;
        public int mpCost;
        public float cooldownDuration;
        public CastType castType;
        public FireType fireType;
        public float castTime;
        public int bulletCount;
        public BulletProfile bulletProfile;
        public float motionValue;
        public Element attackElement;
        public StatusEffectInfo statusEffect;
        public int healAmount;
    }

    /// <summary>
    /// State of the casting process.
    /// </summary>
    public enum CastState : byte
    {
        Idle,
        Casting,
        Fired,
    }

    /// <summary>
    /// Manages the magic casting flow: MP check, cooldown, cast time, fire, and interrupt.
    /// </summary>
    public class MagicCaster : IDisposable
    {
        private CooldownTracker _cooldowns;
        private CastState _state;
        private MagicDefinition _currentMagic;
        private float _castTimer;
        private int _casterHash;

        public CastState State => _state;
        public MagicDefinition CurrentMagic => _currentMagic;

        public event Action<int, MagicDefinition> OnCastStarted;
        public event Action<int, MagicDefinition> OnFired;
        public event Action OnCastInterrupted;

        public MagicCaster()
        {
            _cooldowns = new CooldownTracker();
        }

        /// <summary>
        /// Checks if the caster has enough MP and the spell is off cooldown.
        /// </summary>
        public bool CanCast(MagicDefinition magic, int casterHash, SoACharaDataDic data, float currentTime)
        {
            if (!data.TryGetValue(casterHash, out int _))
            {
                return false;
            }

            ref CharacterVitals vitals = ref data.GetVitals(casterHash);
            if (vitals.currentMp < magic.mpCost)
            {
                return false;
            }

            if (!_cooldowns.IsReady(magic.magicId, currentTime))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Begins the casting process. Consumes MP, starts cooldown, and enters Casting or Fired state.
        /// Returns false if casting prerequisites are not met.
        /// </summary>
        public bool StartCast(MagicDefinition magic, int casterHash, SoACharaDataDic data, float currentTime)
        {
            if (!CanCast(magic, casterHash, data, currentTime))
            {
                return false;
            }

            ref CharacterVitals vitals = ref data.GetVitals(casterHash);
            vitals.currentMp -= magic.mpCost;

            _currentMagic = magic;
            _casterHash = casterHash;
            _castTimer = magic.castTime;
            _state = magic.castTime > 0f ? CastState.Casting : CastState.Fired;

            if (magic.cooldownDuration > 0f)
            {
                _cooldowns.Start(magic.magicId, magic.cooldownDuration, currentTime);
            }

            OnCastStarted?.Invoke(casterHash, magic);

            if (_state == CastState.Fired)
            {
                Fire();
            }

            return true;
        }

        /// <summary>
        /// Advances the cast timer. Fires the spell when cast time expires.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (_state != CastState.Casting)
            {
                return;
            }

            _castTimer -= deltaTime;
            if (_castTimer <= 0f)
            {
                _state = CastState.Fired;
                Fire();
            }
        }

        /// <summary>
        /// Interrupts an in-progress cast and returns to Idle state.
        /// </summary>
        public void Interrupt()
        {
            if (_state == CastState.Casting)
            {
                _state = CastState.Idle;
                OnCastInterrupted?.Invoke();
            }
        }

        private void Fire()
        {
            OnFired?.Invoke(_casterHash, _currentMagic);
            _state = CastState.Idle;
        }

        public void Dispose()
        {
            OnCastStarted = null;
            OnFired = null;
            OnCastInterrupted = null;
        }
    }
}
