using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public class MotionPresentationDataTests
    {
        [Test]
        public void MotionPresentation_Capture_UsesProvidedMotionAndCopiesAuthoringSettings()
        {
            MotionDefinition source = ScriptableObject.CreateInstance<MotionDefinition>();
            AnimationClip originalClip = new AnimationClip();
            AnimationClip replacementClip = new AnimationClip();
            AvatarMask originalMask = new AvatarMask();
            AvatarMask replacementMask = new AvatarMask();
            try
            {
                source.animatorLayer = 3;
                source.blendDuration = 0.25f;
                source.loop = true;
                source.mask = originalMask;
                MotionInfo motion = new MotionInfo { activeMotionDuration = 2f, activeClip = originalClip };

                MotionPresentationData captured = MotionPresentationData.Capture(source, motion);
                source.animatorLayer = 7;
                source.blendDuration = 1f;
                source.loop = false;
                source.mask = replacementMask;
                source.info = new MotionInfo { activeClip = replacementClip };
                motion.activeMotionDuration = 9f;
                motion.activeClip = replacementClip;

                Assert.AreEqual(3, captured.AnimatorLayer);
                Assert.AreEqual(0.25f, captured.BlendDuration);
                Assert.IsTrue(captured.Loop);
                Assert.AreSame(originalMask, captured.Mask);
                Assert.AreEqual(2f, captured.Motion.activeMotionDuration);
                Assert.AreSame(originalClip, captured.Motion.activeClip);
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(originalClip);
                Object.DestroyImmediate(replacementClip);
                Object.DestroyImmediate(originalMask);
                Object.DestroyImmediate(replacementMask);
            }
        }

        [Test]
        public void MotionPresentation_NoAsset_PreservesMotionWithDefaultPresentationSettings()
        {
            MotionInfo motion = new MotionInfo { preMotionDuration = 0.2f, activeMotionDuration = 0.5f };

            MotionPresentationData captured = MotionPresentationData.Capture(null, motion);
            MotionInfo modifiedCopy = captured.Motion;
            modifiedCopy.activeMotionDuration = 10f;

            Assert.AreEqual(0, captured.AnimatorLayer);
            Assert.AreEqual(0f, captured.BlendDuration);
            Assert.IsFalse(captured.Loop);
            Assert.IsNull(captured.Mask);
            Assert.AreEqual(0.2f, captured.Motion.preMotionDuration);
            Assert.AreEqual(0.5f, captured.Motion.activeMotionDuration);
        }

        [Test]
        public void MotionPresentation_DestroyAuthoringAsset_PreservesCapturedSettings()
        {
            MotionDefinition source = ScriptableObject.CreateInstance<MotionDefinition>();
            source.animatorLayer = 2;
            source.blendDuration = 0.4f;
            source.loop = true;
            MotionPresentationData captured = MotionPresentationData.Capture(source,
                new MotionInfo { activeMotionDuration = 3f });

            Object.DestroyImmediate(source);

            Assert.AreEqual(2, captured.AnimatorLayer);
            Assert.AreEqual(0.4f, captured.BlendDuration);
            Assert.IsTrue(captured.Loop);
            Assert.AreEqual(3f, captured.Motion.activeMotionDuration);
        }
    }
}
