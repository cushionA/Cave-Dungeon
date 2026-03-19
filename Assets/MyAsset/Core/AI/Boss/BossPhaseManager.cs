using System;

namespace Game.Core
{
    /// <summary>
    /// ボスフェーズ遷移の結果データ。
    /// </summary>
    public struct BossPhaseTransitionResult
    {
        public bool transitioned;
        public int newPhase;
        public float invincibleTime;
        public bool spawnAdds;
        public string[] addSpawnerIds;
    }

    /// <summary>
    /// ボスのフェーズ条件評価・切替を担う純ロジッククラス。
    /// BossPhaseData配列を受け取り、条件チェックとフェーズ遷移を実行する。
    /// </summary>
    public class BossPhaseManager
    {
        private readonly BossPhaseData[] _phases;
        private int _currentPhase;
        private bool _pendingTransition;

        public int CurrentPhase => _currentPhase;
        public int MaxPhase => _phases != null ? _phases.Length : 0;

        public event Action<int, int> OnPhaseChanged;

        public BossPhaseManager(BossPhaseData[] phases)
        {
            _phases = phases ?? throw new ArgumentNullException(nameof(phases));
            _currentPhase = 0;
            _pendingTransition = false;
        }

        /// <summary>
        /// 遷移条件チェック。条件を満たしたらtrueを返し、遷移待ちフラグを立てる。
        /// </summary>
        public bool CheckTransition(float currentHpRatio, float elapsedTime, int actionCount)
        {
            if (_phases == null || _currentPhase >= _phases.Length - 1)
            {
                return false;
            }

            PhaseCondition condition = _phases[_currentPhase].exitCondition;

            bool shouldTransition = false;
            switch (condition.type)
            {
                case PhaseConditionType.HpThreshold:
                    shouldTransition = currentHpRatio <= condition.threshold;
                    break;

                case PhaseConditionType.Timer:
                    shouldTransition = elapsedTime >= condition.threshold;
                    break;

                case PhaseConditionType.ActionCount:
                    shouldTransition = actionCount >= (int)condition.threshold;
                    break;

                case PhaseConditionType.AllAddsDefeated:
                    // 外部から通知される想定。thresholdは使わない
                    break;

                case PhaseConditionType.Custom:
                    // 外部制御
                    break;
            }

            if (shouldTransition)
            {
                _pendingTransition = true;
            }

            return shouldTransition;
        }

        /// <summary>
        /// フェーズ遷移を実行する。遷移先のフェーズデータを返す。
        /// 最終フェーズでは遷移しない。
        /// </summary>
        public BossPhaseTransitionResult TransitionToNextPhase()
        {
            if (!_pendingTransition && _currentPhase < _phases.Length - 1)
            {
                // 遷移待ちでない場合は何もしない
            }

            if (_currentPhase >= _phases.Length - 1)
            {
                return new BossPhaseTransitionResult { transitioned = false, newPhase = _currentPhase };
            }

            int oldPhase = _currentPhase;
            _currentPhase++;
            _pendingTransition = false;

            BossPhaseData nextData = _phases[_currentPhase];

            OnPhaseChanged?.Invoke(oldPhase, _currentPhase);

            return new BossPhaseTransitionResult
            {
                transitioned = true,
                newPhase = _currentPhase,
                invincibleTime = nextData.transitionInvincibleTime,
                spawnAdds = nextData.spawnAdds,
                addSpawnerIds = nextData.addSpawnerIds ?? new string[0]
            };
        }

        /// <summary>
        /// 現在のフェーズデータを取得する。
        /// </summary>
        public BossPhaseData GetCurrentPhaseData()
        {
            if (_phases == null || _currentPhase >= _phases.Length)
            {
                return default;
            }
            return _phases[_currentPhase];
        }
    }
}
