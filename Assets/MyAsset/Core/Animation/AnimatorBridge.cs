using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// 全キャラクター共通のAnimator制御コンポーネント。
    /// ゲームロジック（ActionBase, IAbility, HitReactionLogic等）からの
    /// パラメータ設定を受け、Animatorに伝達する。
    /// キャラ固有のC#コードは不要。差分はAnimatorOverrideControllerのクリップ差し替えで吸収する。
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class AnimatorBridge : MonoBehaviour
    {
        [SerializeField] private Animator _animator;

        /// <summary>現在適用中のベースRuntimeAnimatorController（Override生成元）</summary>
        private RuntimeAnimatorController _baseController;

        // ========== パラメータハッシュ（定数、全キャラ共通） ==========

        private static readonly int s_Speed = Animator.StringToHash("Speed");
        private static readonly int s_VelocityY = Animator.StringToHash("VelocityY");
        private static readonly int s_IsGrounded = Animator.StringToHash("IsGrounded");
        private static readonly int s_IsDashing = Animator.StringToHash("IsDashing");
        private static readonly int s_IsGuarding = Animator.StringToHash("IsGuarding");
        private static readonly int s_IsDead = Animator.StringToHash("IsDead");
        private static readonly int s_ActionTrigger = Animator.StringToHash("Action");
        private static readonly int s_ActionType = Animator.StringToHash("ActionType");
        private static readonly int s_FlinchTrigger = Animator.StringToHash("Flinch");
        private static readonly int s_KnockbackTrigger = Animator.StringToHash("Knockback");
        private static readonly int s_GuardBrokenTrigger = Animator.StringToHash("GuardBroken");
        private static readonly int s_StunnedTrigger = Animator.StringToHash("Stunned");
        private static readonly int s_ParryTrigger = Animator.StringToHash("Parry");
        private static readonly int s_SpecialTrigger = Animator.StringToHash("Special");
        private static readonly int s_SpecialType = Animator.StringToHash("SpecialType");

        // ========== Actionスロット数 ==========

        private const int k_MaxActionSlots = 8;

        // ========== 初期化 ==========

        private void Awake()
        {
            if (_animator == null)
            {
                _animator = GetComponent<Animator>();
            }
            _baseController = _animator.runtimeAnimatorController;
        }

        // ========== Profile適用 ==========

        /// <summary>
        /// 単一Profileを適用する（敵・NPC、両手武器プレイヤー）。
        /// </summary>
        public void ApplyProfile(ActionAnimationProfile profile)
        {
            ApplyDualProfile(profile, null);
        }

        /// <summary>
        /// 右装備 + 左装備のProfileを合成して適用する。
        /// 移動系クリップは右Profileから取得。左ProfileはActionスロットのみ追加。
        /// </summary>
        public void ApplyDualProfile(
            ActionAnimationProfile rightProfile,
            ActionAnimationProfile leftProfile)
        {
            AnimatorOverrideController overrideCtrl =
                new AnimatorOverrideController(_baseController);

            // 移動系は右Profileから（全身モーション）
            ApplyClipIfNotNull(overrideCtrl, "Idle", rightProfile.idle);
            ApplyClipIfNotNull(overrideCtrl, "Run", rightProfile.run);
            ApplyClipIfNotNull(overrideCtrl, "Dash", rightProfile.dash);
            ApplyClipIfNotNull(overrideCtrl, "Jump", rightProfile.jump);
            ApplyClipIfNotNull(overrideCtrl, "Fall", rightProfile.fall);
            ApplyClipIfNotNull(overrideCtrl, "Landing", rightProfile.landing);
            ApplyClipIfNotNull(overrideCtrl, "Guard", rightProfile.guard);

            // 右手アクション
            ApplyActionSlots(overrideCtrl, rightProfile.actionSlots);

            // 左手アクション
            if (leftProfile != null)
            {
                ApplyActionSlots(overrideCtrl, leftProfile.actionSlots);
            }

            _animator.runtimeAnimatorController = overrideCtrl;
        }

        private void ApplyActionSlots(
            AnimatorOverrideController overrideCtrl,
            ActionAnimationProfile.SlotEntry[] slots)
        {
            if (slots == null)
            {
                return;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                ActionAnimationProfile.SlotEntry entry = slots[i];
                if (entry.clip != null && entry.slotIndex >= 0 && entry.slotIndex < k_MaxActionSlots)
                {
                    overrideCtrl[$"Action_{entry.slotIndex}"] = entry.clip;
                }
            }
        }

        private void ApplyClipIfNotNull(
            AnimatorOverrideController overrideCtrl,
            string stateName,
            AnimationClip clip)
        {
            if (clip != null)
            {
                overrideCtrl[stateName] = clip;
            }
        }

        // ========== 移動系 ==========

        public void SetSpeed(float speed)
        {
            _animator.SetFloat(s_Speed, speed);
        }

        public void SetVerticalVelocity(float vy)
        {
            _animator.SetFloat(s_VelocityY, vy);
        }

        public void SetGrounded(bool grounded)
        {
            _animator.SetBool(s_IsGrounded, grounded);
        }

        public void SetDashing(bool dashing)
        {
            _animator.SetBool(s_IsDashing, dashing);
        }

        public void SetGuarding(bool guard)
        {
            _animator.SetBool(s_IsGuarding, guard);
        }

        // ========== Action（サブステート進入） ==========

        /// <summary>
        /// Actionサブステートのスロットを指定してアニメーション再生。
        /// ActionBase handlerから slot.animSlotIndex を渡す。
        /// slotIndex が負の場合は何もしない。
        /// </summary>
        public void TriggerAction(int slotIndex)
        {
            if (slotIndex < 0)
            {
                return;
            }
            _animator.SetInteger(s_ActionType, slotIndex);
            _animator.SetTrigger(s_ActionTrigger);
        }

        // ========== 共通リアクション（AnyStateから割り込み） ==========

        public void TriggerFlinch()
        {
            _animator.SetTrigger(s_FlinchTrigger);
        }

        public void TriggerKnockback()
        {
            _animator.SetTrigger(s_KnockbackTrigger);
        }

        public void TriggerGuardBroken()
        {
            _animator.SetTrigger(s_GuardBrokenTrigger);
        }

        public void TriggerStunned()
        {
            _animator.SetTrigger(s_StunnedTrigger);
        }

        public void TriggerParry()
        {
            _animator.SetTrigger(s_ParryTrigger);
        }

        public void SetDead(bool dead)
        {
            _animator.SetBool(s_IsDead, dead);
        }

        // ========== 特殊演出 ==========

        /// <summary>
        /// 特殊演出ステートを発火する（形態変化、登場演出等）。
        /// </summary>
        public void TriggerSpecial(int specialType)
        {
            _animator.SetInteger(s_SpecialType, specialType);
            _animator.SetTrigger(s_SpecialTrigger);
        }
    }
}
