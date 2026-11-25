using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Game.Runtime
{
    /// <summary>騎士の所持品を装備済みベルトから選び、共通の行動実行へ渡す。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterActor3D))]
    public sealed class PlayerItemUse3D : MonoBehaviour
    {
        [SerializeField] private ConsumableDefinition[] items = Array.Empty<ConsumableDefinition>();
        [SerializeField, Tooltip("共通アニメーション制御が未設定の場合だけ使うアイテム演出用Animator。")]
        private Animator itemAnimator;

        private CharacterActor3D _actor;
        private SaveManager _saves;
        private PlayableGraph _motion;
        private long _presentedInstance;

        public IReadOnlyList<ConsumableDefinition> Items => items ?? Array.Empty<ConsumableDefinition>();

        private void Awake() => _actor = GetComponent<CharacterActor3D>();

        private void OnEnable() => ObserveSaveManager();

        private void OnDisable()
        {
            if (_saves != null) { _saves.Loaded -= OnSlotLoaded; }
            _saves = null;
            GameManager.Actions?.CancelConsumableUses(this);
            StopPresentation();
        }

        public bool TryUseActive() => TryUseActive(out _);

        public bool TryUseActive(out long instanceId)
        {
            instanceId = 0;
            if (!isActiveAndEnabled || GameManager.ConsumableBelt == null) { return false; }
            ObserveSaveManager();
            int selectedId = GameManager.ConsumableBelt.ActiveItemId;
            ConsumableDefinition selected = null;
            foreach (ConsumableDefinition item in Items)
            {
                if (item == null || item.ItemId != selectedId) { continue; }
                if (selected != null) { return false; }
                selected = item;
            }
            return selected != null && GameManager.Actions != null
                && GameManager.Actions.TryUseConsumable3D(this, _actor, selected, out instanceId);
        }

        private void ObserveSaveManager()
        {
            SaveManager saves = GameManager.Saves;
            if (ReferenceEquals(saves, _saves)) { return; }
            if (_saves != null) { _saves.Loaded -= OnSlotLoaded; }
            _saves = saves;
            if (_saves != null) { _saves.Loaded += OnSlotLoaded; }
        }

        private void OnSlotLoaded(int _) => GameManager.Actions?.CancelConsumableUses(this);

        internal void PresentLegacyPhase(long instance, in MotionInfo info, AnimationPhase phase, float elapsed)
        {
            if (itemAnimator == null || !itemAnimator.isActiveAndEnabled) { return; }
            AnimationClip clip = phase switch
            {
                AnimationPhase.Anticipation => info.preMotionClip,
                AnimationPhase.Active => info.activeClip,
                AnimationPhase.Recovery => info.recoveryClip,
                _ => null,
            };
            if (clip == null)
            {
                EndPresentation(instance);
                return;
            }

            if (_motion.IsValid()) { _motion.Destroy(); }
            float duration = phase switch
            {
                AnimationPhase.Anticipation => info.preMotionDuration,
                AnimationPhase.Active => info.activeMotionDuration,
                _ => info.recoveryDuration,
            };
            float speed = duration > 0f ? clip.length / duration : 1f;
            _motion = PlayableGraph.Create("Item use motion");
            _motion.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            AnimationClipPlayable animation = AnimationClipPlayable.Create(_motion, clip);
            animation.SetApplyFootIK(false);
            animation.SetTime(Math.Min(clip.length, elapsed * speed));
            animation.SetSpeed(speed);
            AnimationPlayableOutput output = AnimationPlayableOutput.Create(_motion, "Use item", itemAnimator);
            output.SetSourcePlayable(animation);
            _presentedInstance = instance;
            _motion.Play();
        }

        internal void EndPresentation(long instance)
        {
            if (_presentedInstance == instance) { StopPresentation(); }
        }

        private void StopPresentation()
        {
            _presentedInstance = 0;
            if (!_motion.IsValid()) { return; }
            _motion.Destroy();
            if (itemAnimator != null && itemAnimator.isActiveAndEnabled) { itemAnimator.Rebind(); }
        }
    }
}
