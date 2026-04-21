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
    public class JudgmentLoop
    {
        private ActionExecutor _executor;
        private SoACharaDataDic _data;
        private int _ownerHash;
        private AIMode _currentMode;
        private int _currentTargetHash;
        private float _targetJudgeTimer;
        private float _actionJudgeTimer;

        public int CurrentTargetHash => _currentTargetHash;
        public ActionExecutor Executor => _executor;

        public JudgmentLoop(ActionExecutor executor, SoACharaDataDic data, int ownerHash)
        {
            _executor = executor;
            _data = data;
            _ownerHash = ownerHash;
        }

        /// <summary>
        /// Sets the active AI mode and resets judgment timers.
        /// 現在実行中のアクションをキャンセルして即座に新モードの評価を可能にする。
        /// </summary>
        public void SetMode(AIMode mode)
        {
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
            _targetJudgeTimer = 0f;
            _actionJudgeTimer = 0f;
            _executor.CancelCurrent();
        }

        /// <summary>
        /// Ticks timers and triggers target/action evaluation when intervals expire.
        /// </summary>
        public void Tick(float deltaTime, List<int> candidates, float currentTime)
        {
            _targetJudgeTimer -= deltaTime;
            _actionJudgeTimer -= deltaTime;
            _executor.Tick(deltaTime);

            if (_targetJudgeTimer <= 0f)
            {
                _targetJudgeTimer = _currentMode.judgeInterval.x;
                EvaluateTarget(candidates, currentTime);
            }

            if (_actionJudgeTimer <= 0f)
            {
                _actionJudgeTimer = _currentMode.judgeInterval.y;
                EvaluateAction(currentTime);
            }
        }

        /// <summary>
        /// 第1層: targetRulesを優先度順に評価し、グローバルターゲットを選択する。
        /// </summary>
        public void EvaluateTarget(List<int> candidates, float currentTime)
        {
            if (_currentMode.targetRules == null || _currentMode.targetSelects == null)
            {
                return;
            }

            for (int i = 0; i < _currentMode.targetRules.Length; i++)
            {
                AIRule rule = _currentMode.targetRules[i];
                if (ConditionEvaluator.EvaluateAll(rule.conditions, _ownerHash, _currentTargetHash, _data, currentTime))
                {
                    int selectIndex = rule.actionIndex;
                    if (selectIndex >= 0 && selectIndex < _currentMode.targetSelects.Length)
                    {
                        int target = TargetSelector.SelectTarget(
                            _currentMode.targetSelects[selectIndex],
                            _ownerHash, candidates, _data, currentTime);
                        if (target != 0)
                        {
                            _currentTargetHash = target;
                        }
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
            if (_currentMode.actionRules == null || _currentMode.actions == null)
            {
                return;
            }

            for (int i = 0; i < _currentMode.actionRules.Length; i++)
            {
                AIRule rule = _currentMode.actionRules[i];
                if (ConditionEvaluator.EvaluateAll(rule.conditions, _ownerHash, _currentTargetHash, _data, currentTime))
                {
                    int actionIdx = rule.actionIndex;
                    if (actionIdx >= 0 && actionIdx < _currentMode.actions.Length)
                    {
                        _executor.Execute(_ownerHash, _currentTargetHash, _currentMode.actions[actionIdx]);
                    }
                    return;
                }
            }

            if (_currentMode.defaultActionIndex >= 0 && _currentMode.defaultActionIndex < _currentMode.actions.Length)
            {
                _executor.Execute(_ownerHash, _currentTargetHash, _currentMode.actions[_currentMode.defaultActionIndex]);
            }
        }
    }
}
