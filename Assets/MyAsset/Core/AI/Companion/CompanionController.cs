using System.Collections.Generic;
using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// Top-level controller for companion AI. Integrates JudgmentLoop, ModeController,
    /// FollowBehavior, StanceManager, and CompanionMpManager into a single update loop.
    /// </summary>
    public class CompanionController
    {
        private JudgmentLoop _judgmentLoop;
        private ModeController _modeController;
        private FollowBehavior _followBehavior;
        private StanceManager _stanceManager;
        private CompanionMpManager _mpManager;
        private ActionExecutor _executor;
        private SoACharaDataDic _data;
        private int _companionHash;
        private int _playerHash;

        public JudgmentLoop JudgmentLoop => _judgmentLoop;
        public ModeController ModeController => _modeController;
        public FollowBehavior FollowBehavior => _followBehavior;
        public StanceManager StanceManager => _stanceManager;
        public CompanionMpManager MpManager => _mpManager;
        public int CompanionHash => _companionHash;

        public CompanionController(
            int companionHash,
            int playerHash,
            SoACharaDataDic data,
            float maxMp,
            int initialReserveMp,
            CompanionMpSettings mpSettings)
        {
            _companionHash = companionHash;
            _playerHash = playerHash;
            _data = data;

            _executor = new ActionExecutor();
            _executor.Register(new AttackActionHandler());
            _executor.Register(new CastActionHandler());
            _executor.Register(new InstantActionHandler());
            _executor.Register(new SustainedActionHandler());
            _executor.Register(new BroadcastActionHandler());

            _judgmentLoop = new JudgmentLoop(_executor, data, companionHash);
            _modeController = new ModeController(_judgmentLoop);
            _followBehavior = new FollowBehavior();
            _stanceManager = new StanceManager();
            _mpManager = new CompanionMpManager(maxMp, initialReserveMp, mpSettings);
        }

        /// <summary>
        /// Sets the AI modes and transition rules for the companion.
        /// </summary>
        public void SetAIModes(AIMode[] modes, ModeTransitionRule[] transitions)
        {
            _modeController.SetModes(modes, transitions);
        }

        /// <summary>
        /// Main update loop. Evaluates follow behavior, mode transitions, and AI judgment.
        /// Teleports the companion if too far from the player.
        /// Vanished state skips AI judgment and only ticks MP recovery.
        /// </summary>
        public void Tick(float deltaTime, List<int> candidates, float currentTime)
        {
            if (!_data.TryGetValue(_companionHash, out int _) ||
                !_data.TryGetValue(_playerHash, out int _))
            {
                return;
            }

            // MP回復は常にTick（消滅中も回復する）
            _mpManager.Tick(deltaTime);

            // 消滅中はAI判定をスキップ
            if (_mpManager.IsVanished)
            {
                return;
            }

            ref CharacterVitals companionVitals = ref _data.GetVitals(_companionHash);
            ref CharacterVitals playerVitals = ref _data.GetVitals(_playerHash);

            FollowState followState = _followBehavior.Evaluate(
                companionVitals.position, playerVitals.position);

            if (followState == FollowState.Teleporting)
            {
                companionVitals.position = playerVitals.position;
            }

            _modeController.EvaluateTransitions(
                _companionHash, _judgmentLoop.CurrentTargetHash, _data, currentTime);
            _judgmentLoop.Tick(deltaTime, candidates, currentTime);
        }

        /// <summary>
        /// 連携発動可否。消滅中は連携拒否。
        /// </summary>
        public bool CanAcceptCoop()
        {
            return !_mpManager.IsVanished;
        }
    }
}
