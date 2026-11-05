using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Game.Core
{
    [CreateAssetMenu(fileName = "Motion", menuName = "Game/Motion Definition")]
    public sealed class MotionDefinition : ScriptableObject
    {
        public MotionInfo info;
        [Min(0)] public int animatorLayer;
        [Min(0f)] public float blendDuration = 0.1f;
        public ActionResources resources = ActionResources.UpperPose | ActionResources.LowerPose;
        public bool loop;
        public AvatarMask mask;

        public float Duration => info.preMotionDuration + info.activeMotionDuration + info.recoveryDuration;
    }

    [Serializable]
    public struct MotionInfo
    {
        [FoldoutGroup("タイミング")]
        [MinValue(0)] public float preMotionDuration;
        [FoldoutGroup("タイミング")]
        [MinValue(0)] public float activeMotionDuration;
        [FoldoutGroup("タイミング")]
        [MinValue(0)] public float recoveryDuration;

        [FoldoutGroup("アニメーション")]
        public AnimationClip preMotionClip;
        [FoldoutGroup("アニメーション")]
        public AnimationClip activeClip;
        [FoldoutGroup("アニメーション")]
        public AnimationClip chargeLoopClip;
        [FoldoutGroup("アニメーション")]
        public AnimationClip recoveryClip;
    }
}
