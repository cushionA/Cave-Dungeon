using UnityEngine;
using Game.Core;

namespace Game.Runtime
{
    /// <summary>
    /// キャラクターのAnimator制御MonoBehaviour。
    /// AnimationBridge（純ロジック）からのパラメータ変更をAnimatorに反映し、
    /// AnimationStateDataをSoAコンテナに同期する。
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class CharacterAnimationController : MonoBehaviour
    {
        private const float k_CrossFadeDuration = 0.1f;

        private Animator _animator;
        private AnimationBridge _bridge;
        private int _ownerHash;
        private bool _isInitialized;

        // 現在のアクションで使うクリップを保持
        private MotionInfo _currentMotion;

        public AnimationBridge Bridge => _bridge;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            _bridge = new AnimationBridge();
        }

        private void OnEnable()
        {
            if (_bridge != null)
            {
                _bridge.OnPhaseChanged += HandlePhaseChanged;
            }
        }

        private void OnDisable()
        {
            if (_bridge != null)
            {
                _bridge.OnPhaseChanged -= HandlePhaseChanged;
            }
        }

        /// <summary>
        /// BaseCharacterから呼ばれる初期化。ownerHashでSoAコンテナと紐付ける。
        /// </summary>
        public void Initialize(int ownerHash)
        {
            _ownerHash = ownerHash;
            _isInitialized = true;
        }

        /// <summary>
        /// アクションフェーズを開始する。ActionExecutor Runtimeから呼ぶ。
        /// cancelPointは行動データ側（AttackInfo.cancelPoint等）から供給する。
        /// </summary>
        public void StartActionPhase(MotionInfo motion, byte moveId, float cancelPoint = -1f)
        {
            _currentMotion = motion;
            _bridge.StartActionPhase(motion, moveId, cancelPoint);

            // 予備動作クリップが設定されていればCrossFade
            if (motion.preMotionDuration > 0f && motion.preMotionClip != null)
            {
                _animator.CrossFade(motion.preMotionClip.name, k_CrossFadeDuration);
            }
            else if (motion.activeClip != null)
            {
                // preMotionDuration=0の場合はActiveクリップを直接再生
                _animator.CrossFade(motion.activeClip.name, k_CrossFadeDuration);
            }
        }

        /// <summary>
        /// アクションをキャンセルする。
        /// </summary>
        public void CancelAction()
        {
            _bridge.CancelAction();
        }

        private void Update()
        {
            if (!_isInitialized || _bridge == null)
            {
                return;
            }

            // フェーズタイマーを進める
            _bridge.TickPhase(Time.deltaTime);

            // ダーティな場合のみAnimatorに反映
            if (_bridge.IsDirty)
            {
                ApplyToAnimator();
                _bridge.ClearDirty();
            }

            // SoAコンテナにAnimationStateDataを同期
            SyncToSoA();
        }

        private void ApplyToAnimator()
        {
            foreach (System.Collections.Generic.KeyValuePair<string, float> pair in _bridge.Floats)
            {
                _animator.SetFloat(pair.Key, pair.Value);
            }

            foreach (System.Collections.Generic.KeyValuePair<string, bool> pair in _bridge.Bools)
            {
                _animator.SetBool(pair.Key, pair.Value);
            }

            foreach (System.Collections.Generic.KeyValuePair<string, int> pair in _bridge.Ints)
            {
                _animator.SetInteger(pair.Key, pair.Value);
            }

            // トリガーは消費してAnimatorに送る
            foreach (string trigger in _bridge.PendingTriggers)
            {
                _animator.SetTrigger(trigger);
            }
            _bridge.ConsumeAllTriggers();
        }

        private void HandlePhaseChanged(Game.Core.AnimationPhase newPhase)
        {
            switch (newPhase)
            {
                case Game.Core.AnimationPhase.Active:
                    if (_currentMotion.activeClip != null)
                    {
                        _animator.CrossFade(_currentMotion.activeClip.name, k_CrossFadeDuration);
                    }
                    break;
                case Game.Core.AnimationPhase.Recovery:
                    if (_currentMotion.recoveryClip != null)
                    {
                        _animator.CrossFade(_currentMotion.recoveryClip.name, k_CrossFadeDuration);
                    }
                    break;
            }
        }

        private void SyncToSoA()
        {
            if (!GameManager.IsCharacterValid(_ownerHash))
            {
                return;
            }

            ref AnimationStateData animState = ref GameManager.Data.GetAnimationState(_ownerHash);
            animState = _bridge.CurrentState;
        }
    }
}
