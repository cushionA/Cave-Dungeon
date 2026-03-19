using UnityEngine;
using AnyPortrait;

namespace Game.Runtime
{
    /// <summary>
    /// AnyPortraitとゲームのアニメーションシステムを接続するブリッジ。
    /// キャラクターの状態変化に応じてAnyPortraitのアニメーションを制御する。
    /// Animator/SpriteRendererの代替としてAnyPortraitのapPortraitを使用。
    /// </summary>
    public class AnyPortraitBridge : MonoBehaviour
    {
        [Header("AnyPortrait")]
        [SerializeField] private apPortrait _portrait;

        [Header("Animation Names")]
        [SerializeField] private string _idleAnim = "Idle";
        [SerializeField] private string _runAnim = "Run";
        [SerializeField] private string _jumpAnim = "Jump";
        [SerializeField] private string _fallAnim = "Fall";
        [SerializeField] private string _attackAnim = "Attack";
        [SerializeField] private string _hitAnim = "Hit";
        [SerializeField] private string _deathAnim = "Death";
        [SerializeField] private string _guardAnim = "Guard";

        private string _currentAnim;
        private bool _isFacingRight = true;

        private void Awake()
        {
            if (_portrait == null)
            {
                _portrait = GetComponent<apPortrait>();
            }
        }

        /// <summary>
        /// アニメーション再生。Crossfade付き。
        /// </summary>
        public void Play(string animName, float crossfadeDuration = 0.1f)
        {
            if (_portrait == null || _currentAnim == animName)
            {
                return;
            }

            _currentAnim = animName;
            _portrait.CrossFade(animName, crossfadeDuration);
        }

        /// <summary>
        /// アイドルアニメーション。
        /// </summary>
        public void PlayIdle()
        {
            Play(_idleAnim);
        }

        /// <summary>
        /// 移動アニメーション。
        /// </summary>
        public void PlayRun()
        {
            Play(_runAnim);
        }

        /// <summary>
        /// ジャンプアニメーション。
        /// </summary>
        public void PlayJump()
        {
            Play(_jumpAnim, 0.05f);
        }

        /// <summary>
        /// 落下アニメーション。
        /// </summary>
        public void PlayFall()
        {
            Play(_fallAnim);
        }

        /// <summary>
        /// 攻撃アニメーション。
        /// </summary>
        public void PlayAttack()
        {
            Play(_attackAnim, 0.05f);
        }

        /// <summary>
        /// 被弾アニメーション。
        /// </summary>
        public void PlayHit()
        {
            Play(_hitAnim, 0.05f);
        }

        /// <summary>
        /// 死亡アニメーション。
        /// </summary>
        public void PlayDeath()
        {
            Play(_deathAnim, 0.1f);
        }

        /// <summary>
        /// ガードアニメーション。
        /// </summary>
        public void PlayGuard()
        {
            Play(_guardAnim, 0.05f);
        }

        /// <summary>
        /// 向き変更。AnyPortraitのFlip制御。
        /// </summary>
        public void SetFacing(bool facingRight)
        {
            if (_portrait == null || _isFacingRight == facingRight)
            {
                return;
            }

            _isFacingRight = facingRight;

            // AnyPortraitのFlipはtransformのscale.xで行う
            Vector3 scale = transform.localScale;
            scale.x = facingRight ? Mathf.Abs(scale.x) : -Mathf.Abs(scale.x);
            transform.localScale = scale;
        }

        /// <summary>
        /// アニメーション速度制御。
        /// </summary>
        public void SetAnimationSpeed(float speed)
        {
            if (_portrait != null)
            {
                _portrait.SetAnimationSpeed(speed);
            }
        }

        /// <summary>
        /// 現在のアニメーションが再生中かどうか。
        /// </summary>
        public bool IsPlaying(string animName)
        {
            return _currentAnim == animName && _portrait != null && _portrait.IsPlaying(animName);
        }
    }
}
