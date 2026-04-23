using System;
using System.Collections.Generic;
using R3;
using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// Orchestrates AI subsystems for a single enemy character.
    /// Owns a JudgmentLoop, ModeController, and ActionExecutor,
    /// and drives them each frame via Tick().
    /// </summary>
    public class EnemyController : IDisposable
    {
        private JudgmentLoop _judgmentLoop;
        private ModeController _modeController;
        private ActionExecutor _executor;
        private SoACharaDataDic _data;
        private int _enemyHash;
        private bool _isActive;
        private IDisposable _confusionClearedSubscription;

        public int EnemyHash => _enemyHash;
        public bool IsActive => _isActive;
        public JudgmentLoop JudgmentLoop => _judgmentLoop;
        public ModeController ModeController => _modeController;

        /// <summary>
        /// GameEvents を注入して混乱解除時の即時 AI 再評価を有効化する。
        /// events が null の場合は従来どおり外部からの <see cref="JudgmentLoop.ForceEvaluate"/> に任せる。
        /// </summary>
        public EnemyController(int enemyHash, SoACharaDataDic data, GameEvents events)
        {
            _enemyHash = enemyHash;
            _data = data;
            _isActive = true;

            _executor = new ActionExecutor();
            _executor.Register(new AttackActionHandler());
            _executor.Register(new CastActionHandler());
            _executor.Register(new InstantActionHandler());
            _executor.Register(new SustainedActionHandler());
            _executor.Register(new BroadcastActionHandler());

            _judgmentLoop = new JudgmentLoop(_executor, data, enemyHash);
            _modeController = new ModeController(_judgmentLoop);

            if (events != null)
            {
                _confusionClearedSubscription = events.OnConfusionCleared.Subscribe(OnConfusionClearedBridge);
            }
        }

        /// <summary>
        /// GameEvents.OnConfusionCleared の受け口。自ハッシュと一致した時だけ
        /// JudgmentLoop.ForceEvaluate() を呼び、ターゲット/行動を即時再評価する。
        /// </summary>
        private void OnConfusionClearedBridge(int targetHash)
        {
            if (targetHash == _enemyHash)
            {
                _judgmentLoop.ForceEvaluate();
            }
        }

        /// <summary>
        /// 購読解除 + 実行中アクションキャンセル。キャラ破棄時に必ず呼ぶ。
        /// </summary>
        public void Dispose()
        {
            _confusionClearedSubscription?.Dispose();
            _confusionClearedSubscription = null;
            _executor?.CancelCurrent();
        }

        /// <summary>
        /// Configures the AI behaviour by setting modes and transition rules.
        /// </summary>
        public void SetAIModes(AIMode[] modes, ModeTransitionRule[] transitions)
        {
            _modeController.SetModes(modes, transitions);
        }

        /// <summary>
        /// Drives one frame of enemy AI: checks liveness, evaluates mode transitions,
        /// and ticks the judgment loop.
        /// </summary>
        public void Tick(float deltaTime, List<int> candidates, float currentTime)
        {
            if (!_isActive)
            {
                return;
            }

            if (!_data.TryGetValue(_enemyHash, out int _))
            {
                _isActive = false;
                return;
            }

            ref CharacterVitals vitals = ref _data.GetVitals(_enemyHash);
            if (vitals.currentHp <= 0)
            {
                _isActive = false;
                return;
            }

            _modeController.EvaluateTransitions(
                _enemyHash, _judgmentLoop.CurrentTargetHash, _data, currentTime);
            _judgmentLoop.Tick(deltaTime, candidates, currentTime);
        }

        /// <summary>
        /// Deactivates the enemy, cancelling any in-progress action.
        /// </summary>
        public void Deactivate()
        {
            _isActive = false;
            _executor.CancelCurrent();
        }

        /// <summary>
        /// Re-activates a previously deactivated enemy.
        /// </summary>
        public void Activate()
        {
            _isActive = true;
        }
    }
}
