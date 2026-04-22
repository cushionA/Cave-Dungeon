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
        /// <summary>
        /// 手動モード切替中に自動遷移を抑制する時間（秒）。
        /// タイムアウト経過後は自動モード遷移評価を再開する。
        /// </summary>
        private const float k_ManualOverrideTimeoutSeconds = 5f;

        private JudgmentLoop _judgmentLoop;
        private ModeController _modeController;
        private ModeTransitionEditor _modeTransitionEditor;
        private FollowBehavior _followBehavior;
        private StanceManager _stanceManager;
        private CompanionMpManager _mpManager;
        private ActionExecutor _executor;
        private SoACharaDataDic _data;
        private int _companionHash;
        private int _playerHash;
        private float _manualOverrideTimer;

        public JudgmentLoop JudgmentLoop => _judgmentLoop;
        public ModeController ModeController => _modeController;
        public ModeTransitionEditor ModeTransitionEditor => _modeTransitionEditor;
        public FollowBehavior FollowBehavior => _followBehavior;
        public StanceManager StanceManager => _stanceManager;
        public CompanionMpManager MpManager => _mpManager;
        public int CompanionHash => _companionHash;

        /// <summary>手動切替中か。タイマー > 0 の間は true。</summary>
        public bool IsManualOverrideActive => _manualOverrideTimer > 0f;

        /// <summary>手動切替の残り時間（秒）。</summary>
        public float ManualOverrideRemaining => _manualOverrideTimer;

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
            _modeTransitionEditor = new ModeTransitionEditor(
                _modeController, k_ManualOverrideTimeoutSeconds);
            _followBehavior = new FollowBehavior();
            _stanceManager = new StanceManager();
            _mpManager = new CompanionMpManager(maxMp, initialReserveMp, mpSettings);
        }

        /// <summary>
        /// 手動でモードを切り替える。タイムアウト（k_ManualOverrideTimeoutSeconds）経過までは
        /// 自動モード遷移評価を抑制する。UI/ショートカット/連携アクションから呼び出し想定。
        /// </summary>
        public void RequestModeSwitch(int modeIndex)
        {
            if (_modeController == null)
            {
                return;
            }

            _modeController.SwitchMode(modeIndex);
            _manualOverrideTimer = k_ManualOverrideTimeoutSeconds;
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

            // 手動オーバーライドタイマーを消化。タイマー中は自動遷移を抑制する。
            if (_manualOverrideTimer > 0f)
            {
                _manualOverrideTimer -= deltaTime;
                if (_manualOverrideTimer < 0f)
                {
                    _manualOverrideTimer = 0f;
                }
            }
            else
            {
                _modeController.EvaluateTransitions(
                    _companionHash, _judgmentLoop.CurrentTargetHash, _data, currentTime);
            }

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
