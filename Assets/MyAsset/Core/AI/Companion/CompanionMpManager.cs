using System;
using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// 仲間MPシステム。二重MPプール（currentMP/reserveMP）管理、
    /// バリアダメージ処理、消滅/復帰フロー、MP回復行動を統括する。
    /// </summary>
    public class CompanionMpManager : IDisposable
    {
        private float _currentMp;
        private float _maxMp;
        private int _reserveMp;
        private bool _isVanished;
        private bool _isRecovering;
        private CompanionMpSettings _settings;

        /// <summary>
        /// reserveMP → currentMP 転送時の端数累積。毎Tickの actualRecovery は
        /// 小数点を持つため、int の _reserveMp と整合させるため端数をここに貯め、
        /// 1.0 以上になったら整数分だけ転送する。
        /// </summary>
        private float _reserveTransferCarry;

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

                // reserveMP → currentMP 転送。
                // 旧実装は currentMp を float で加算しつつ _reserveMp を CeilToInt で減算していたため、
                // 毎Tickで差分が「上方向に丸め」で消失する総和ズレバグがあった (FUTURE_TASKS参照)。
                // 端数を _reserveTransferCarry に累積し、整数分だけ双方同時に動かす対称構造に修正。
                if (_reserveMp > 0)
                {
                    float rawFromReserve = Mathf.Min(actualRecovery, (float)_reserveMp);
                    _reserveTransferCarry += rawFromReserve;
                    int transferAmount = Mathf.FloorToInt(_reserveTransferCarry);
                    if (transferAmount > 0)
                    {
                        transferAmount = Mathf.Min(transferAmount, _reserveMp);
                        _currentMp += transferAmount;
                        _reserveMp -= transferAmount;
                        _reserveTransferCarry -= transferAmount;
                    }
                }
            }
            else
            {
                // 回復しないTickでは端数をリセット（長時間保持して突発的に大量転送されるのを防ぐ）
                _reserveTransferCarry = 0f;
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

        public void Dispose()
        {
            OnVanish = null;
            OnReturn = null;
        }
    }
}
