using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// AI judgment loop that periodically evaluates target selection and action selection
    /// based on the current AIMode's rules and intervals.
    /// 第1層: targetRulesでターゲット選択
    /// 第2層: actionRulesでアクション選択（ターゲットは第1層で確定済み）
    /// 第3層: 行動実行（常に何かする = 棒立ち防止）
    /// </summary>
    public class JudgmentLoop : IDisposable
    {
        private ActionExecutor _executor;
        private SoACharaDataDic _data;
        private int _ownerHash;
        private AIMode _currentMode;
        // 旧SoA互換経路だけで使用。Runtimeの標的正本はAITargetRelations。
        private int _currentTargetHash;
        private float _targetJudgeTimer;
        private float _actionJudgeTimer;
        private AIObservationContext _observation;
        private AITargetRelations _targets;
        private ActorRef _owner;


        private AICompiledMode _compiledMode;
        private AICompiledMode[] _compiledModes;
        private int _compiledModeIndex;
        private int _revision;
        private bool _disposed;
        private AIDecisionSystem _decisions;

        internal int OwnerHash => _ownerHash;
        internal int Revision => _revision;
        internal AICompiledMode[] CompiledModes => _compiledModes;
        internal int CompiledModeIndex => _compiledModeIndex;
        internal AIObservationContext Observation => _observation;

        public int CurrentTargetHash => _targets != null
            ? (_targets.TryGetTarget(_owner, out ActorRef target) ? target.Hash : 0)
            : _currentTargetHash;
        public long ObservationTick => _observation != null ? _observation.Tick : 0;

        public void ResetActorLifetime()
        {
            InvalidateDecision();
            _decisions?.Unregister(this);
            SetTarget(0);
            _executor.CancelCurrent();
            _owner = default;
            _observation = null;
            _targets = null;
            _currentTargetHash = 0;
            _targetJudgeTimer = _actionJudgeTimer = 0f;
        }

        public bool UseObservation(AIObservationContext observation, AITargetRelations targets)
        {
            if (_disposed || observation == null || targets == null || !observation.TryGetActor(_ownerHash, out AIActorObservation owner)
                || (_owner.IsValid && !_owner.Equals(owner.actor))) { return false; }
            _owner = owner.actor;
            _observation = observation;
            _targets = targets;
            return true;
        }

        private void SetTarget(int hash)
        {
            if (_targets == null) { _currentTargetHash = hash; return; }
            ActorRef target = hash != 0 && _observation.TryGetActor(hash, out AIActorObservation value) ? value.actor : default;
            _targets.SetTarget(_owner, target);
        }

        private bool EvaluateConditions(AICondition[] conditions, float currentTime)
        {
            return _observation != null
                ? ConditionEvaluator.EvaluateAll(conditions, _ownerHash, CurrentTargetHash, _observation)
                : ConditionEvaluator.EvaluateAll(conditions, _ownerHash, CurrentTargetHash, _data, currentTime);
        }
        public ActionExecutor Executor => _executor;

        public JudgmentLoop(ActionExecutor executor, SoACharaDataDic data, int ownerHash)
        {
            _executor = executor;
            _data = data;
            _ownerHash = ownerHash;
            _compiledMode = new AICompiledMode(default);
            _compiledModes = new[] { _compiledMode };
        }

        /// <summary>
        /// Sets the active AI mode and resets judgment timers.
        /// 現在実行中のアクションをキャンセルして即座に新モードの評価を可能にする。
        /// </summary>
        public void SetMode(AIMode mode)
        {
            SetCapturedMode(AIConfigurationCopy.Clone(mode));
        }

        /// <summary>ModeControllerが所有するsnapshotだけを渡す。毎tickやモード切替で再コピーしない。</summary>
        internal void SetCapturedMode(AIMode mode)
        {
            SetCapturedMode(mode, new[] { new AICompiledMode(mode) }, 0);
        }

        internal void SetCapturedMode(AIMode mode, AICompiledMode[] compiledModes, int modeIndex)
        {
            _compiledModes = compiledModes;
            _compiledModeIndex = modeIndex;
            _compiledMode = compiledModes[modeIndex];
            InvalidateDecision();
            _currentMode = mode;
            _targetJudgeTimer = mode.judgeInterval.x;
            // アクション評価を即座に実行するためタイマーを0にする
            _actionJudgeTimer = 0f;
            // 旧モードのアクションをキャンセル（Waitが残り続ける問題を防止）
            _executor.CancelCurrent();
        }

        /// <summary>
        /// ターゲットと行動の判定タイマーを強制的に満了状態にする。
        /// 次の Tick で即座に target/action 再評価が走る。
        /// 混乱解除 / 強制ターゲットクリア / イベント完了等、
        /// AI が保持している target/action が即座に陳腐化するケースで呼ぶ。
        /// 現在実行中のアクションもキャンセルする。
        /// </summary>
        public void ForceEvaluate()
        {
            InvalidateDecision();
            _targetJudgeTimer = 0f;
            _actionJudgeTimer = 0f;
            SetTarget(0);
            _executor.CancelCurrent();
        }

        /// <summary>
        /// Ticks timers and triggers target/action evaluation when intervals expire.
        /// </summary>
        public void Tick(float deltaTime, List<int> candidates, float currentTime)
        {
            if (!AdvanceTimers(deltaTime, out bool targetDue, out bool actionDue)) { return; }
            if (_observation != null) { EvaluateObserved(candidates, targetDue, actionDue); return; }
            if (targetDue) { EvaluateTarget(candidates, currentTime); }
            if (actionDue) { EvaluateAction(currentTime); }
        }

        /// <summary>
        /// 第1層: targetRulesを優先度順に評価し、グローバルターゲットを選択する。
        /// </summary>
        public void EvaluateTarget(List<int> candidates, float currentTime)
        {
            if (_observation != null) { EvaluateObserved(candidates, true, false); return; }
            if (_currentMode.targetRules == null || _currentMode.targetSelects == null)
            {
                return;
            }

            for (int i = 0; i < _currentMode.targetRules.Length; i++)
            {
                AIRule rule = _currentMode.targetRules[i];
                if (EvaluateConditions(rule.conditions, currentTime))
                {
                    int selectIndex = rule.actionIndex;
                    if (selectIndex >= 0 && selectIndex < _currentMode.targetSelects.Length)
                    {
                        int target = _observation != null
                            ? TargetSelector.SelectTarget(_currentMode.targetSelects[selectIndex], _ownerHash, _observation, candidates)
                            : TargetSelector.SelectTarget(_currentMode.targetSelects[selectIndex], _ownerHash, candidates, _data, currentTime);
                        SetTarget(target);
                    }
                    return;
                }
            }
        }

        /// <summary>
        /// 第2層: actionRulesを優先度順に評価し、アクションを選択・実行する。
        /// ターゲットは第1層で確定済み。行動にターゲット上書きはない。
        /// </summary>
        public void EvaluateAction(float currentTime)
        {
            if (_observation != null) { EvaluateObserved(null, false, true); return; }
            if (_currentMode.actionRules == null || _currentMode.actions == null)
            {
                return;
            }

            for (int i = 0; i < _currentMode.actionRules.Length; i++)
            {
                AIRule rule = _currentMode.actionRules[i];
                if (EvaluateConditions(rule.conditions, currentTime))
                {
                    int actionIdx = rule.actionIndex;
                    if (actionIdx >= 0 && actionIdx < _currentMode.actions.Length)
                    {
                        _executor.Execute(_ownerHash, CurrentTargetHash, _currentMode.actions[actionIdx]);
                    }
                    return;
                }
            }

            if (_currentMode.defaultActionIndex >= 0 && _currentMode.defaultActionIndex < _currentMode.actions.Length)
            {
                _executor.Execute(_ownerHash, CurrentTargetHash, _currentMode.actions[_currentMode.defaultActionIndex]);
            }
        }
        public void PrepareTick(float deltaTime, List<int> candidates, float currentTime, AIDecisionSystem decisions)
        {
            if (_disposed || _observation == null || !ReferenceEquals(_observation, decisions.Observation)) { return; }
            if (!ReferenceEquals(_decisions, decisions))
            {
                _decisions?.Unregister(this);
                _decisions = decisions;
            }
            if (AdvanceTimers(deltaTime, out bool targetDue, out bool actionDue) && (targetDue || actionDue))
            {
                decisions.Queue(this, candidates, currentTime, targetDue, actionDue);
            }
        }

        private bool AdvanceTimers(float deltaTime, out bool targetDue, out bool actionDue)
        {
            targetDue = actionDue = false;
            if (_disposed || _targets != null && !_targets.IsCurrent(_owner)) { _executor.CancelCurrent(); return false; }
            if (_targets == null && _currentTargetHash != 0 && (!_data.TryGetValue(_currentTargetHash, out _)
                || _data.GetVitals(_currentTargetHash).currentHp <= 0)) { _currentTargetHash = 0; }
            _targetJudgeTimer -= deltaTime;
            _actionJudgeTimer -= deltaTime;
            _executor.Tick(deltaTime);
            if (_disposed || _targets != null && !_targets.IsCurrent(_owner)) { return false; }
            targetDue = _targetJudgeTimer <= 0f;
            actionDue = _actionJudgeTimer <= 0f;
            if (targetDue) { _targetJudgeTimer = _currentMode.judgeInterval.x; }
            if (actionDue) { _actionJudgeTimer = _currentMode.judgeInterval.y; }
            return true;
        }

        private void EvaluateObserved(List<int> candidates, bool selectTarget, bool selectAction)
        {
            AIManagedWorld world = new AIManagedWorld { observation = _observation };
            AIManagedProgram program = new AIManagedProgram { mode = _compiledMode };
            AIManagedCandidates input = new AIManagedCandidates { hashes = candidates ?? _observation.ActorHashes };
            AIDecisionResult result = AIQueryKernel.Decide(_ownerHash, CurrentTargetHash,
                selectTarget, selectAction, ref world, ref program, ref input);
            result.owner = _owner;
            result.target = _observation.TryGetActor(result.targetHash, out AIActorObservation target) ? target.actor : default;
            result.observationTick = _observation.Tick;
            result.revision = _revision;
            ApplyDecision(result, _revision);
        }

        internal void ApplyDecision(AIDecisionResult result, int revision)
        {
            if (_disposed || revision != _revision || result.revision != _revision || _observation == null
                || result.observationTick != _observation.Tick || result.owner != _owner
                || _targets == null || !_targets.IsCurrent(_owner)) { return; }
            if (result.target.IsValid && (!_targets.IsCurrent(result.target) || !_observation.Contains(result.target)))
            {
                InvalidateTarget();
                return;
            }
            SetTarget(result.targetHash);
            if (result.actionIndex >= 0)
            {
                _executor.Execute(_ownerHash, CurrentTargetHash, _compiledMode.actions[result.actionIndex]);
            }
        }

        internal void InvalidateDecision() => _revision++;

        internal void InvalidateTarget()
        {
            ForceEvaluate();
        }

        internal void Detach(AIDecisionSystem decisions)
        {
            if (ReferenceEquals(_decisions, decisions)) { _decisions = null; }
        }

        public void Dispose()
        {
            if (_disposed) { return; }
            _disposed = true;
            InvalidateDecision();
            _decisions?.Unregister(this);
            _decisions = null;
            SetTarget(0);
        }
    }
}
