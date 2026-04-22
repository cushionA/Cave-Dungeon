using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// アニメーション制御の純ロジック層。
    /// Animatorパラメータの蓄積、アクションフェーズ（Anticipation/Active/Recovery）の管理、
    /// AnimationStateDataの生成を担う。MonoBehaviour非依存。
    /// パラメータキーは int hash で保持し、string 版 API は内部で一度だけ StringToHash してキャッシュする。
    /// ダーティ判定はパラメータ単位で行い、Flush 時に変更があった分だけ送信する。
    /// </summary>
    public class AnimationBridge
    {
        /// <summary>フェーズ遷移時に発火。Runtime層がクリップ切り替えに使用。</summary>
        public event Action<AnimationPhase> OnPhaseChanged;

        // パラメータ本体（キーは Animator.StringToHash による int hash）
        private readonly Dictionary<int, float> _floats = new Dictionary<int, float>();
        private readonly Dictionary<int, bool> _bools = new Dictionary<int, bool>();
        private readonly Dictionary<int, int> _ints = new Dictionary<int, int>();
        private readonly HashSet<int> _triggers = new HashSet<int>();

        // パラメータ単位のダーティ集合（Flush で空にする）
        private readonly HashSet<int> _dirtyFloats = new HashSet<int>();
        private readonly HashSet<int> _dirtyBools = new HashSet<int>();
        private readonly HashSet<int> _dirtyInts = new HashSet<int>();

        // string → int hash ルックアップキャッシュ（同じ文字列を毎回ハッシュ化しない）
        private readonly Dictionary<string, int> _nameHashCache = new Dictionary<string, int>();

        // フェーズ状態の変化を示す独立ダーティフラグ（パラメータ送信には使わない）
        private bool _phaseDirty;

        private AnimationStateData _currentState;

        // アクションフェーズ管理
        private float _phaseElapsed;
        private float _anticipationDuration;
        private float _activeDuration;
        private float _recoveryDuration;
        private float _cancelPoint; // -1=キャンセル不可, 0~1=Recovery中のキャンセル可能正規化時間

        private const float k_FixedDeltaTime = 0.02f; // 50fps想定のフレーム時間

        public AnimationStateData CurrentState => _currentState;

        /// <summary>
        /// いずれかのパラメータまたはフェーズ状態に変更があるか。
        /// パラメータ単位 dirty 集合が空でない、未消費トリガーがある、または
        /// フェーズ状態が更新されている場合に true。
        /// </summary>
        public bool IsDirty =>
            _dirtyFloats.Count > 0
            || _dirtyBools.Count > 0
            || _dirtyInts.Count > 0
            || _triggers.Count > 0
            || _phaseDirty;

        // ─── パラメータ設定（string 版：内部で一度 StringToHash してキャッシュ） ───

        public void SetFloat(string name, float value)
        {
            SetFloat(GetOrAddHash(name), value);
        }

        public void SetFloat(int nameHash, float value)
        {
            if (_floats.TryGetValue(nameHash, out float current) && current == value)
            {
                return;
            }
            _floats[nameHash] = value;
            _dirtyFloats.Add(nameHash);
        }

        public float GetFloat(string name)
        {
            return GetFloat(GetOrAddHash(name));
        }

        public float GetFloat(int nameHash)
        {
            return _floats.TryGetValue(nameHash, out float value) ? value : 0f;
        }

        public void SetBool(string name, bool value)
        {
            SetBool(GetOrAddHash(name), value);
        }

        public void SetBool(int nameHash, bool value)
        {
            if (_bools.TryGetValue(nameHash, out bool current) && current == value)
            {
                return;
            }
            _bools[nameHash] = value;
            _dirtyBools.Add(nameHash);
        }

        public bool GetBool(string name)
        {
            return GetBool(GetOrAddHash(name));
        }

        public bool GetBool(int nameHash)
        {
            return _bools.TryGetValue(nameHash, out bool value) && value;
        }

        public void SetInt(string name, int value)
        {
            SetInt(GetOrAddHash(name), value);
        }

        public void SetInt(int nameHash, int value)
        {
            if (_ints.TryGetValue(nameHash, out int current) && current == value)
            {
                return;
            }
            _ints[nameHash] = value;
            _dirtyInts.Add(nameHash);
        }

        public int GetInt(string name)
        {
            return GetInt(GetOrAddHash(name));
        }

        public int GetInt(int nameHash)
        {
            return _ints.TryGetValue(nameHash, out int value) ? value : 0;
        }

        public void SetTrigger(string name)
        {
            SetTrigger(GetOrAddHash(name));
        }

        public void SetTrigger(int nameHash)
        {
            _triggers.Add(nameHash);
        }

        public void ResetTrigger(string name)
        {
            ResetTrigger(GetOrAddHash(name));
        }

        public void ResetTrigger(int nameHash)
        {
            _triggers.Remove(nameHash);
        }

        /// <summary>
        /// トリガーを消費する。呼び出し後は自動的にリセットされる。
        /// </summary>
        public bool ConsumeTrigger(string name)
        {
            return ConsumeTrigger(GetOrAddHash(name));
        }

        public bool ConsumeTrigger(int nameHash)
        {
            return _triggers.Remove(nameHash);
        }

        /// <summary>
        /// ダーティ集合と phaseDirty を全てクリアする。Flush 完了後に呼ぶ。
        /// トリガーは ConsumeAllTriggers で別途管理する。
        /// </summary>
        public void ClearDirty()
        {
            _dirtyFloats.Clear();
            _dirtyBools.Clear();
            _dirtyInts.Clear();
            _phaseDirty = false;
        }

        /// <summary>
        /// 変更があったパラメータキーのみを列挙する。Runtime 層はこれを使って差分送信する。
        /// </summary>
        public IReadOnlyCollection<int> DirtyFloats => _dirtyFloats;
        public IReadOnlyCollection<int> DirtyBools => _dirtyBools;
        public IReadOnlyCollection<int> DirtyInts => _dirtyInts;

        /// <summary>
        /// 蓄積されたパラメータを列挙する。キーは Animator.StringToHash で得た int hash。
        /// </summary>
        public IReadOnlyDictionary<int, float> Floats => _floats;
        public IReadOnlyDictionary<int, bool> Bools => _bools;
        public IReadOnlyDictionary<int, int> Ints => _ints;

        /// <summary>
        /// 未消費のトリガー hash を列挙する。
        /// </summary>
        public IReadOnlyCollection<int> PendingTriggers => _triggers;

        /// <summary>
        /// 全トリガーを消費済みとしてクリアする。
        /// Runtime層がAnimatorにSetTriggerした後に呼ぶ。
        /// </summary>
        public void ConsumeAllTriggers()
        {
            _triggers.Clear();
        }

        private int GetOrAddHash(string name)
        {
            if (!_nameHashCache.TryGetValue(name, out int hash))
            {
                hash = Animator.StringToHash(name);
                _nameHashCache[name] = hash;
            }
            return hash;
        }

        // ─── アクションフェーズ管理 ───

        /// <summary>
        /// 攻撃・スキルのアクションフェーズを開始する。
        /// MotionInfoのタイミング情報に基づいてAnticipation→Active→Recoveryを管理。
        /// cancelPointは行動データ側（AttackInfo.cancelPoint等）から供給する。
        /// </summary>
        /// <param name="motion">モーションタイミング情報</param>
        /// <param name="moveId">実行中のモーション識別子</param>
        /// <param name="cancelPoint">Recovery中のキャンセル可能正規化時間（-1でキャンセル不可）</param>
        public void StartActionPhase(MotionInfo motion, byte moveId, float cancelPoint = -1f)
        {
            _anticipationDuration = motion.preMotionDuration;
            _activeDuration = motion.activeMotionDuration;
            _recoveryDuration = motion.recoveryDuration;
            _cancelPoint = cancelPoint;
            _phaseElapsed = 0f;

            _currentState.currentMoveId = moveId;
            _currentState.isCommitted = true;

            // isCancelable は「ExecuteActionで新行動に上書き可能」を意味する。
            // アニメ再生開始から Recovery 中の cancelPoint 到達まで false を維持し、
            // 行動の中断を防ぐ（cancelPoint 到達時のみ true に昇格）。
            if (_anticipationDuration > 0f)
            {
                _currentState.currentPhase = AnimationPhase.Anticipation;
                _currentState.isCancelable = false;
                _currentState.normalizedTime = 0f;
            }
            else
            {
                _currentState.currentPhase = AnimationPhase.Active;
                _currentState.isCancelable = false;
                _currentState.normalizedTime = 0f;
            }

            _phaseDirty = true;
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
                            // キャンセルポイント到達でキャンセル可能にする
                            if (!_currentState.isCancelable
                                && _cancelPoint >= 0f
                                && _currentState.normalizedTime >= _cancelPoint)
                            {
                                _currentState.isCancelable = true;
                                _phaseDirty = true;
                            }
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
            _phaseDirty = true;
            OnPhaseChanged?.Invoke(AnimationPhase.Active);
        }

        private void TransitionToRecovery()
        {
            _currentState.currentPhase = AnimationPhase.Recovery;
            _currentState.isCancelable = false;
            _currentState.normalizedTime = 0f;
            _phaseDirty = true;
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
            _phaseDirty = true;
            OnPhaseChanged?.Invoke(AnimationPhase.Neutral);
        }
    }
}
