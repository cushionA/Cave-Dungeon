using System;

namespace Game.Core
{
    /// <summary>
    /// 召喚されたキャラクターの純ロジッククラス。
    /// MonoBehaviour版SummonedCharacterControllerがこのクラスをコンポジションで保持する。
    /// </summary>
    public class SummonedCharacterLogic
    {
        private int _summonerId;
        private float _remainingDuration;
        private SummonType _type;
        private bool _isActive;

        public int SummonerId => _summonerId;
        public float RemainingDuration => _remainingDuration;
        public SummonType Type => _type;
        public bool IsActive => _isActive;

        public event Action OnDismissed;

        /// <summary>
        /// 召喚獣を初期化する。
        /// </summary>
        public void Initialize(int summonerHash, float duration, SummonType type)
        {
            _summonerId = summonerHash;
            _remainingDuration = duration;
            _type = type;
            _isActive = true;
        }

        /// <summary>
        /// 毎フレーム更新。寿命を減算し、期限切れで自動解除。
        /// duration=0 の場合は無制限（手動解除のみ）。
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (!_isActive || _remainingDuration <= 0f)
            {
                return;
            }

            _remainingDuration -= deltaTime;

            if (_remainingDuration <= 0f)
            {
                Dismiss();
            }
        }

        /// <summary>
        /// 召喚獣を解除する。
        /// </summary>
        public void Dismiss()
        {
            if (!_isActive)
            {
                return;
            }

            _isActive = false;
            OnDismissed?.Invoke();
        }
    }
}
