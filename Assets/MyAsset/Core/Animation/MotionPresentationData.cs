using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// 登録時に確定する表示設定。アセット参照先の入替えやSOの値変更を実行中に読み直さない。
    /// クリップとマスクは表示資源として参照を保持し、UnityObject自体の内容は複製しない。
    /// </summary>
    public readonly struct MotionPresentationData
    {
        public MotionInfo Motion { get; }
        public int AnimatorLayer { get; }
        public float BlendDuration { get; }
        public bool Loop { get; }
        public AvatarMask Mask { get; }

        private MotionPresentationData(MotionInfo motion, int animatorLayer, float blendDuration,
            bool loop, AvatarMask mask)
        {
            Motion = motion;
            AnimatorLayer = animatorLayer;
            BlendDuration = blendDuration;
            Loop = loop;
            Mask = mask;
        }

        public static MotionPresentationData Capture(MotionDefinition source, MotionInfo motion)
        {
            return source != null
                ? new MotionPresentationData(motion, source.animatorLayer, source.blendDuration, source.loop, source.mask)
                : new MotionPresentationData(motion, 0, 0f, false, null);
        }
    }
}
