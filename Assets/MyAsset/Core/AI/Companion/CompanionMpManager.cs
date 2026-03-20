using System;
using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// 仲間MPシステム。二重MPプール（currentMP/reserveMP）管理、
    /// バリアダメージ処理、消滅/復帰フロー、MP回復行動を統括する。
    /// </summary>
    public class CompanionMpManager
    {
        private float _currentMp;
        private float _maxMp;
        private int _reserveMp;
        private bool _isVanished;
        private bool _isRecovering;
        private CompanionMpSettings _settings;

        public float CurrentMp => _currentMp;
        public float MaxMp => _maxMp;
        public int ReserveMp => _reserveMp;
        public int MaxReserveMp => _settings.maxReserveMp;
        public bool IsVanished => _isVanished;
        public bool IsRecovering => _isRecovering;

        public event Action OnVanish;
        public event Action OnReturn;

        public CompanionMpManager(float maxMp, int initialReserveMp, CompanionMpSettings settings)
        {
            _maxMp = maxMp;
            _currentMp = maxMp;
            _reserveMp = Mathf.Clamp(initialReserveMp, 0, settings.maxReserveMp);
            _settings = settings;
            _isVanished = false;
            _isRecovering = false;
        }

        /// <summary>
        /// MP消費（魔法/連携/バリア共通）。
        /// 戻り値: 実際の消費量。MP不足なら残MP全消費。
        /// </summary>
        public float ConsumeMp(float amount)
        {
            if (amount <= 0f || _isVanished)
            {
                return 0f;
            }

            float consumed = Mathf.Min(amount, _currentMp);
            _currentMp -= consumed;
            return consumed;
        }

        /// <summary>
        /// バリアダメージ処理（盾判定後のMP消費量を受け取る）。
        /// MP 0になったら消滅トリガー。
        /// </summary>
        public void ApplyBarrierDamage(float mpCost)
        {
            if (_isVanished)
            {
                return;
            }

            ConsumeMp(mpCost);

            if (_currentMp <= 0f)
            {
                EnterVanishState();
            }
        }

        /// <summary>
        /// 自然回復Tick（毎フレーム呼び出し）。
        /// reserveMP → currentMP 補充 + 消滅中の倍率適用。
        /// 消滅中は復帰判定も行う。
        /// </summary>
        public void Tick(float deltaTime)
        {
            float recoveryRate = _settings.baseRecoveryRate;

            if (_isRecovering)
            {
                recoveryRate += _settings.mpRecoverActionRate;
            }

            if (_isVanished)
            {
                recoveryRate *= _settings.vanishRecoveryMultiplier;
            }

            float recoveryAmount = recoveryRate * deltaTime;

            if (recoveryAmount > 0f && _currentMp < _maxMp)
            {
                float deficit = _maxMp - _currentMp;
                float actualRecovery = Mathf.Min(recoveryAmount, deficit);

                // reserveMP から補充（reserveMP が足りなければ補充量を制限）
                if (_reserveMp > 0)
                {
                    float fromReserve = Mathf.Min(actualRecovery, _reserveMp);
                    _currentMp += fromReserve;
                    _reserveMp -= Mathf.CeilToInt(fromReserve);
                    _reserveMp = Mathf.Max(_reserveMp, 0);
                }
            }

            // 消滅中の復帰判定
            if (_isVanished && _currentMp >= _maxMp * _settings.returnThresholdRatio)
            {
                ExitVanishState();
            }
        }

        /// <summary>
        /// MP回復行動の開始。
        /// </summary>
        public void StartMpRecovery()
        {
            if (!_isVanished)
            {
                _isRecovering = true;
            }
        }

        /// <summary>
        /// MP回復行動の中断（怯み等）。
        /// </summary>
        public void StopMpRecovery()
        {
            _isRecovering = false;
        }

        /// <summary>
        /// reserveMP回復（アイテム/チェックポイント）。
        /// </summary>
        public void RestoreReserveMp(int amount)
        {
            if (amount <= 0)
            {
                return;
            }
            _reserveMp = Mathf.Min(_reserveMp + amount, _settings.maxReserveMp);
        }

        private void EnterVanishState()
        {
            _isVanished = true;
            _isRecovering = false;
            _currentMp = 0f;
            OnVanish?.Invoke();
        }

        private void ExitVanishState()
        {
            _isVanished = false;
            OnReturn?.Invoke();
        }
    }
}
