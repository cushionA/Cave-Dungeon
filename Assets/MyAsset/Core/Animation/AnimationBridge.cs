using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// アニメーション制御の純ロジック層。
    /// Animatorパラメータの蓄積、アクションフェーズ（Anticipation/Active/Recovery）の管理、
    /// AnimationStateDataの生成を担う。MonoBehaviour非依存。
    /// </summary>
    public class AnimationBridge
    {
        /// <summary>フェーズ遷移時に発火。Runtime層がクリップ切り替えに使用。</summary>
        public event Action<AnimationPhase> OnPhaseChanged;
        private readonly Dictionary<string, float> _floats = new Dictionary<string, float>();
        private readonly Dictionary<string, bool> _bools = new Dictionary<string, bool>();
        private readonly Dictionary<string, int> _ints = new Dictionary<string, int>();
        private readonly HashSet<string> _triggers = new HashSet<string>();

        private AnimationStateData _currentState;
        private bool _isDirty;

        // アクションフェーズ管理
        private float _phaseElapsed;
        private float _anticipationDuration;
        private float _activeDuration;
        private float _recoveryDuration;

        private const float k_FixedDeltaTime = 0.02f; // 50fps想定のフレーム時間

        public AnimationStateData CurrentState => _currentState;
        public bool IsDirty => _isDirty;

        // ─── パラメータ設定 ───

        public void SetFloat(string name, float value)
        {
            if (_floats.TryGetValue(name, out float current) && current == value)
            {
                return;
            }
            _floats[name] = value;
            _isDirty = true;
        }

        public float GetFloat(string name)
        {
            return _floats.TryGetValue(name, out float value) ? value : 0f;
        }

        public void SetBool(string name, bool value)
        {
            if (_bools.TryGetValue(name, out bool current) && current == value)
            {
                return;
            }
            _bools[name] = value;
            _isDirty = true;
        }

        public bool GetBool(string name)
        {
            return _bools.TryGetValue(name, out bool value) && value;
        }

        public void SetInt(string name, int value)
        {
            if (_ints.TryGetValue(name, out int current) && current == value)
            {
                return;
            }
            _ints[name] = value;
            _isDirty = true;
        }

        public int GetInt(string name)
        {
            return _ints.TryGetValue(name, out int value) ? value : 0;
        }

        public void SetTrigger(string name)
        {
            _triggers.Add(name);
            _isDirty = true;
        }

        public void ResetTrigger(string name)
        {
            _triggers.Remove(name);
        }

        /// <summary>
        /// トリガーを消費する。呼び出し後は自動的にリセットされる。
        /// </summary>
        public bool ConsumeTrigger(string name)
        {
            return _triggers.Remove(name);
        }

        public void ClearDirty()
        {
            _isDirty = false;
        }

        /// <summary>
        /// 蓄積されたパラメータを列挙する。Runtime層がAnimatorに反映するために使う。
        /// </summary>
        public IReadOnlyDictionary<string, float> Floats => _floats;
        public IReadOnlyDictionary<string, bool> Bools => _bools;
        public IReadOnlyDictionary<string, int> Ints => _ints;

        /// <summary>
        /// 未消費のトリガー名を列挙する。
        /// </summary>
        public IReadOnlyCollection<string> PendingTriggers => _triggers;

        /// <summary>
        /// 全トリガーを消費済みとしてクリアする。
        /// Runtime層がAnimatorにSetTriggerした後に呼ぶ。
        /// </summary>
        public void ConsumeAllTriggers()
        {
            _triggers.Clear();
        }

        // ─── アクションフェーズ管理 ───

        /// <summary>
        /// 攻撃・スキルのアクションフェーズを開始する。
        /// MotionInfoのタイミング情報に基づいてAnticipation→Active→Recoveryを管理。
        /// </summary>
        public void StartActionPhase(MotionInfo motion, byte moveId)
        {
            _anticipationDuration = motion.preMotionDuration;
            _activeDuration = motion.activeMotionDuration;
            _recoveryDuration = motion.recoveryDuration;
            _phaseElapsed = 0f;

            _currentState.currentMoveId = moveId;
            _currentState.isCommitted = true;

            if (_anticipationDuration > 0f)
            {
                _currentState.currentPhase = AnimationPhase.Anticipation;
                _currentState.isCancelable = true;
                _currentState.normalizedTime = 0f;
            }
            else
            {
                _currentState.currentPhase = AnimationPhase.Active;
                _currentState.isCancelable = false;
                _currentState.normalizedTime = 0f;
            }

            _isDirty = true;
        }

        /// <summary>
        /// アクションフェーズを時間経過で進める。
        /// </summary>
        public void TickPhase(float deltaTime)
        {
            if (_currentState.currentPhase == AnimationPhase.Neutral)
            {
                return;
            }

            _phaseElapsed += deltaTime;

            // duration=0のフェーズを連鎖的にスキップするためループ
            bool advanced = true;
            while (advanced && _currentState.currentPhase != AnimationPhase.Neutral)
            {
                advanced = false;
                switch (_currentState.currentPhase)
                {
                    case AnimationPhase.Anticipation:
                        if (_phaseElapsed >= _anticipationDuration)
                        {
                            float overflow = _phaseElapsed - _anticipationDuration;
                            TransitionToActive();
                            _phaseElapsed = overflow;
                            advanced = true;
                        }
                        else
                        {
                            _currentState.normalizedTime = _phaseElapsed / _anticipationDuration;
                        }
                        break;

                    case AnimationPhase.Active:
                        if (_activeDuration <= 0f || _phaseElapsed >= _activeDuration)
                        {
                            float overflow = _activeDuration > 0f ? _phaseElapsed - _activeDuration : _phaseElapsed;
                            TransitionToRecovery();
                            _phaseElapsed = overflow;
                            advanced = true;
                        }
                        else
                        {
                            _currentState.normalizedTime = _phaseElapsed / _activeDuration;
                        }
                        break;

                    case AnimationPhase.Recovery:
                        if (_recoveryDuration <= 0f || _phaseElapsed >= _recoveryDuration)
                        {
                            TransitionToNeutral();
                            advanced = false;
                        }
                        else
                        {
                            _currentState.normalizedTime = _phaseElapsed / _recoveryDuration;
                        }
                        break;
                }
            }

            // framesUntilActionable: 全残りフェーズ時間からフレーム数を算出
            UpdateFramesUntilActionable();
        }

        private void UpdateFramesUntilActionable()
        {
            if (_currentState.currentPhase == AnimationPhase.Neutral)
            {
                _currentState.framesUntilActionable = 0;
                return;
            }

            float remainingTime = 0f;
            switch (_currentState.currentPhase)
            {
                case AnimationPhase.Anticipation:
                    remainingTime = (_anticipationDuration - _phaseElapsed) + _activeDuration + _recoveryDuration;
                    break;
                case AnimationPhase.Active:
                    remainingTime = (_activeDuration > 0f ? _activeDuration - _phaseElapsed : 0f) + _recoveryDuration;
                    break;
                case AnimationPhase.Recovery:
                    remainingTime = _recoveryDuration > 0f ? _recoveryDuration - _phaseElapsed : 0f;
                    break;
            }

            if (remainingTime < 0f)
            {
                remainingTime = 0f;
            }

            _currentState.framesUntilActionable = (short)(remainingTime / k_FixedDeltaTime);
        }

        /// <summary>
        /// アクションをキャンセルしてNeutralに戻す。
        /// </summary>
        public void CancelAction()
        {
            TransitionToNeutral();
        }

        private void TransitionToActive()
        {
            _currentState.currentPhase = AnimationPhase.Active;
            _currentState.isCancelable = false;
            _currentState.normalizedTime = 0f;
            _isDirty = true;
            OnPhaseChanged?.Invoke(AnimationPhase.Active);
        }

        private void TransitionToRecovery()
        {
            _currentState.currentPhase = AnimationPhase.Recovery;
            _currentState.isCancelable = false;
            _currentState.normalizedTime = 0f;
            _isDirty = true;
            OnPhaseChanged?.Invoke(AnimationPhase.Recovery);
        }

        private void TransitionToNeutral()
        {
            _currentState.currentPhase = AnimationPhase.Neutral;
            _currentState.normalizedTime = 0f;
            _currentState.currentMoveId = 0;
            _currentState.isCancelable = false;
            _currentState.isCommitted = false;
            _currentState.framesUntilActionable = 0;
            _phaseElapsed = 0f;
            _isDirty = true;
            OnPhaseChanged?.Invoke(AnimationPhase.Neutral);
        }
    }
}
