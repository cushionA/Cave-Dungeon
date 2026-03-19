using System;

namespace Game.Core
{
    /// <summary>
    /// ボスAI統括の純ロジッククラス。
    /// エンカウント開始/終了、フェーズ遷移チェックを毎判定で実行する。
    /// MonoBehaviour版BossControllerはこのクラスをコンポジションで保持する。
    /// </summary>
    public class BossControllerLogic
    {
        private readonly BossPhaseManager _phaseManager;
        private readonly string _bossId;
        private bool _isEncounterActive;
        private float _encounterElapsedTime;

        public bool IsEncounterActive => _isEncounterActive;
        public string BossId => _bossId;
        public int CurrentPhase => _phaseManager.CurrentPhase;

        public event Action<int, int> OnPhaseChanged;
        public event Action<string> OnBossDefeated;
        public event Action<string> OnEncounterStart;

        public BossControllerLogic(BossPhaseData[] phases, string bossId)
        {
            _phaseManager = new BossPhaseManager(phases);
            _bossId = bossId;
            _isEncounterActive = false;
            _encounterElapsedTime = 0f;

            _phaseManager.OnPhaseChanged += (oldP, newP) =>
            {
                OnPhaseChanged?.Invoke(oldP, newP);
            };
        }

        /// <summary>
        /// ボス戦開始。
        /// </summary>
        public void StartEncounter()
        {
            _isEncounterActive = true;
            _encounterElapsedTime = 0f;
            OnEncounterStart?.Invoke(_bossId);
        }

        /// <summary>
        /// 毎判定で呼ばれるフェーズ遷移チェック。
        /// </summary>
        public BossPhaseTransitionResult UpdateEncounter(float currentHpRatio, float deltaTime, int actionCount)
        {
            if (!_isEncounterActive)
            {
                return new BossPhaseTransitionResult { transitioned = false };
            }

            _encounterElapsedTime += deltaTime;

            if (_phaseManager.CheckTransition(currentHpRatio, _encounterElapsedTime, actionCount))
            {
                return _phaseManager.TransitionToNextPhase();
            }

            return new BossPhaseTransitionResult { transitioned = false };
        }

        /// <summary>
        /// ボス撃破処理。
        /// </summary>
        public void OnDefeated()
        {
            _isEncounterActive = false;
            OnBossDefeated?.Invoke(_bossId);
        }

        /// <summary>
        /// 現在のフェーズのAIMode配列を取得する。
        /// </summary>
        public AIMode[] GetCurrentPhaseModes()
        {
            return _phaseManager.GetCurrentPhaseData().modes;
        }
    }
}
