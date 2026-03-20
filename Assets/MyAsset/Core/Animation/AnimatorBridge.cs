using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Game.Core
{
    /// <summary>
    /// 全キャラクター共通のAnimator制御コンポーネント。
    /// ゲームロジック（ActionBase, IAbility, HitReactionLogic等）からの
    /// パラメータ設定を受け、Animatorに伝達する。
    /// キャラ固有のC#コードは不要。差分はAnimatorOverrideControllerのクリップ差し替えで吸収する。
    /// エフェクト・音声はAnimationEventのコールバック経由でProfileのデータに基づき再生する。
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class AnimatorBridge : MonoBehaviour
    {
        [SerializeField] private Animator _animator;

        // ========== 共通リアクションエフェクト ==========

        [Header("共通リアクションエフェクト")]
        [SerializeField] private AssetReferenceGameObject _flinchVfx;
        [SerializeField] private AssetReferenceGameObject _knockbackVfx;

        [Header("共通リアクション音声")]
        [SerializeField] private AssetReferenceT<AudioClip> _flinchSfx;
        [SerializeField] private AssetReferenceT<AudioClip> _knockbackSfx;

        /// <summary>現在適用中のベースRuntimeAnimatorController（Override生成元）</summary>
        private RuntimeAnimatorController _baseController;

        /// <summary>現在再生中のActionスロットインデックス（-1 = 非Action中）</summary>
        private int _currentActionSlot = -1;

        /// <summary>現在適用中の右Profile（ActionEvent解決用）</summary>
        private ActionAnimationProfile _currentRightProfile;

        /// <summary>現在適用中の左Profile（ActionEvent解決用）</summary>
        private ActionAnimationProfile _currentLeftProfile;

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
            _currentRightProfile = rightProfile;
            _currentLeftProfile = leftProfile;

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
            _currentActionSlot = slotIndex;
            _animator.SetInteger(s_ActionType, slotIndex);
            _animator.SetTrigger(s_ActionTrigger);
        }

        // ========== AnimationEvent コールバック ==========

        /// <summary>
        /// AnimationClipに埋め込んだAnimationEventから呼ばれる。
        /// 現在のActionスロットのProfileからeventIdに対応するVFX/SFXを再生する。
        /// クリップ側は OnActionEvent(int eventId) で呼び出す。
        /// </summary>
        public void OnActionEvent(int eventId)
        {
            if (_currentActionSlot < 0)
            {
                return;
            }

            ActionAnimationProfile.ActionEventData eventData;
            if (TryGetEventData(_currentActionSlot, eventId, out eventData))
            {
                PlayVfx(eventData.vfxRef, eventData.vfxOffset);
                PlaySfx(eventData.sfxRef, eventData.sfxVolume);
            }
        }

        /// <summary>
        /// 現在のProfileからスロットとeventIdに対応するActionEventDataを検索する。
        /// 右Profile → 左Profile の順で検索。
        /// </summary>
        private bool TryGetEventData(
            int slotIndex,
            int eventId,
            out ActionAnimationProfile.ActionEventData result)
        {
            // 右Profileから検索
            if (TryFindEventInProfile(_currentRightProfile, slotIndex, eventId, out result))
            {
                return true;
            }

            // 左Profileから検索
            if (_currentLeftProfile != null &&
                TryFindEventInProfile(_currentLeftProfile, slotIndex, eventId, out result))
            {
                return true;
            }

            result = default;
            return false;
        }

        private bool TryFindEventInProfile(
            ActionAnimationProfile profile,
            int slotIndex,
            int eventId,
            out ActionAnimationProfile.ActionEventData result)
        {
            if (profile == null || profile.actionSlots == null)
            {
                result = default;
                return false;
            }

            for (int i = 0; i < profile.actionSlots.Length; i++)
            {
                ActionAnimationProfile.SlotEntry slot = profile.actionSlots[i];
                if (slot.slotIndex != slotIndex || slot.events == null)
                {
                    continue;
                }

                for (int j = 0; j < slot.events.Length; j++)
                {
                    if (slot.events[j].eventId == eventId)
                    {
                        result = slot.events[j];
                        return true;
                    }
                }
            }

            result = default;
            return false;
        }

        // ========== VFX/SFX 再生 ==========

        private void PlayVfx(AssetReferenceGameObject vfxRef, Vector2 offset)
        {
            if (vfxRef == null || !vfxRef.RuntimeKeyIsValid())
            {
                return;
            }

            Vector3 spawnPos = transform.position + (Vector3)offset;
            AsyncOperationHandle<GameObject> handle =
                Addressables.InstantiateAsync(vfxRef, spawnPos, Quaternion.identity);
        }

        private void PlaySfx(AssetReferenceT<AudioClip> sfxRef, float volume)
        {
            if (sfxRef == null || !sfxRef.RuntimeKeyIsValid())
            {
                return;
            }

            AsyncOperationHandle<AudioClip> handle = Addressables.LoadAssetAsync<AudioClip>(sfxRef);
            handle.Completed += op =>
            {
                if (op.Status == AsyncOperationStatus.Succeeded)
                {
                    AudioSource.PlayClipAtPoint(op.Result, transform.position, volume);
                }
            };
        }

        // ========== 共通リアクション（AnyStateから割り込み） ==========

        public void TriggerFlinch()
        {
            _currentActionSlot = -1;
            _animator.SetTrigger(s_FlinchTrigger);
            PlayVfx(_flinchVfx, Vector2.zero);
            PlaySfx(_flinchSfx, 1f);
        }

        public void TriggerKnockback()
        {
            _currentActionSlot = -1;
            _animator.SetTrigger(s_KnockbackTrigger);
            PlayVfx(_knockbackVfx, Vector2.zero);
            PlaySfx(_knockbackSfx, 1f);
        }

        public void TriggerGuardBroken()
        {
            _currentActionSlot = -1;
            _animator.SetTrigger(s_GuardBrokenTrigger);
        }

        public void TriggerStunned()
        {
            _currentActionSlot = -1;
            _animator.SetTrigger(s_StunnedTrigger);
        }

        public void TriggerParry()
        {
            _animator.SetTrigger(s_ParryTrigger);
        }

        public void SetDead(bool dead)
        {
            if (dead)
            {
                _currentActionSlot = -1;
            }
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
