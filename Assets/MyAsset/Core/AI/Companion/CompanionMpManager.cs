using System;

namespace Game.Core
{
    public class CompanionMpManager : IDisposable
    {
        private readonly IExecutionWorld _world;
        private readonly int _ownerHash;
        private readonly CompanionMpSettings _settings;
        private float _reserveMp;
        private bool _isVanished;
        private bool _isRecovering;
        private bool _disposed;

        public float CurrentMp => TryGetActor(out ActionActorState actor) ? actor.vitals.currentMp : 0f;
        public float MaxMp => TryGetActor(out ActionActorState actor) ? actor.vitals.maxMp : 0f;
        public float ReserveMp => _reserveMp;
        public int MaxReserveMp => _settings.maxReserveMp;
        public bool IsVanished => _isVanished;
        public bool IsRecovering => _isRecovering;

        public event Action OnVanish;
        public event Action OnReturn;

        public CompanionMpManager(IExecutionWorld world, int ownerHash, int initialReserveMp, CompanionMpSettings settings)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _ownerHash = ownerHash;
            _settings = settings;
            _reserveMp = Math.Max(0, Math.Min(initialReserveMp, settings.maxReserveMp));
        }

        public float ConsumeMp(float amount)
        {
            if (amount <= 0f || _isVanished || !TryGetActor(out ActionActorState actor)) { return 0f; }
            float consumed = Math.Min(amount, actor.vitals.currentMp);
            actor.vitals.currentMp -= consumed;
            _world.CommitActor(_ownerHash, actor);
            return consumed;
        }

        public void ApplyBarrierDamage(float mpCost)
        {
            if (_isVanished || !TryGetActor(out _)) { return; }
            ConsumeMp(mpCost);
            if (CurrentMp <= 0f) { EnterVanishState(); }
        }

        public void Tick(float deltaTime)
        {
            if (!TryGetActor(out ActionActorState actor)) { return; }
            if (!_isVanished && actor.vitals.currentMp <= 0f) { EnterVanishState(); }
            if (!TryGetActor(out actor)) { return; }

            float rate = _settings.baseRecoveryRate + (_isRecovering ? _settings.mpRecoverActionRate : 0f);
            if (_isVanished) { rate *= _settings.vanishRecoveryMultiplier; }
            float recovery = Math.Min(Math.Max(0f, rate * deltaTime),
                Math.Min(_reserveMp, Math.Max(0f, actor.vitals.maxMp - actor.vitals.currentMp)));
            actor.vitals.currentMp += recovery;
            _reserveMp -= recovery;
            _world.CommitActor(_ownerHash, actor);

            if (_isVanished && actor.vitals.maxMp > 0 && actor.vitals.currentMp >= actor.vitals.maxMp * _settings.returnThresholdRatio)
            {
                _isVanished = false;
                OnReturn?.Invoke();
            }
        }

        public void StartMpRecovery()
        {
            if (!_isVanished && TryGetActor(out _)) { _isRecovering = true; }
        }

        public void StopMpRecovery() => _isRecovering = false;

        public void RestoreReserveMp(int amount)
        {
            if (amount > 0 && !_disposed) { _reserveMp = Math.Min(_reserveMp + amount, _settings.maxReserveMp); }
        }

        private void EnterVanishState()
        {
            if (!TryGetActor(out ActionActorState actor)) { return; }
            _isVanished = true;
            _isRecovering = false;
            actor.vitals.currentMp = 0f;
            _world.CommitActor(_ownerHash, actor);
            OnVanish?.Invoke();
        }

        private bool TryGetActor(out ActionActorState actor)
        {
            if (_disposed) { actor = default; return false; }
            return _world.TryGetActor(_ownerHash, out actor);
        }

        public void Dispose()
        {
            _disposed = true;
            _isRecovering = false;
            OnVanish = null;
            OnReturn = null;
        }
    }
}
