#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.PlayMode
{
    public class MotionPresentationTests
    {
        private readonly List<GameObject> _actors = new List<GameObject>();
        private readonly List<Object> _assets = new List<Object>();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (int i = 0; i < _actors.Count; i++)
            {
                if (_actors[i] != null) { Object.Destroy(_actors[i]); }
            }
            _actors.Clear();
            yield return null;
            for (int i = 0; i < _assets.Count; i++)
            {
                if (_assets[i] != null) { Object.Destroy(_assets[i]); }
            }
            _assets.Clear();
            yield return null;
        }

        private CharacterAnimationController CreateController()
        {
            GameObject actor = new GameObject("MotionPresentationActor");
            GameObject visual = new GameObject("Visual");
            visual.transform.SetParent(actor.transform, false);
            _actors.Add(actor);
            return actor.AddComponent<CharacterAnimationController>();
        }

        private MotionDefinition CreateMotion()
        {
            AnimationClip clip = new AnimationClip { name = "UnregisteredAnimatorState" };
            AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve("Visual", typeof(Transform), "m_LocalPosition.x"),
                AnimationCurve.Linear(0f, 0f, 1f, 1f));
            _assets.Add(clip);
            MotionDefinition motion = ScriptableObject.CreateInstance<MotionDefinition>();
            motion.info = new MotionInfo { activeMotionDuration = 1f, activeClip = clip };
            motion.blendDuration = 0f;
            _assets.Add(motion);
            return motion;
        }

        private static ActionExecution Execution(long id, float elapsed = 0.25f)
        {
            return new ActionExecution
            {
                instanceId = id, phase = AnimationPhase.Active, elapsed = elapsed, phaseElapsed = elapsed,
                definition = new ActionDefinitionData { motionOnly = true, activeDuration = 1f },
            };
        }

        [UnityTest]
        public IEnumerator MotionPresentation_MotionOnly_UsesArbitraryClipWithoutChangingExecutionState()
        {
            CharacterAnimationController controller = CreateController();
            MotionDefinition motion = CreateMotion();
            controller.SetExecutionState(default);

            controller.Present(Execution(1), motion, motion.info);
            yield return null;
            yield return null;

            Assert.IsTrue(controller.HasActiveGraph);
            Assert.AreEqual(1, controller.ActiveMotionCount);
            Assert.AreEqual(0.25f, controller.transform.GetChild(0).localPosition.x, 0.01f);
            Assert.AreEqual(AnimationPhase.Neutral, controller.Bridge.CurrentState.currentPhase);
            Assert.IsFalse(controller.Bridge.CurrentState.isCommitted);
        }

        [UnityTest]
        public IEnumerator MotionPresentation_EndOneExecution_PreservesOtherLayerAndReusesCapacity()
        {
            CharacterAnimationController controller = CreateController();
            MotionDefinition first = CreateMotion();
            MotionDefinition second = CreateMotion();
            controller.Present(Execution(1), first, first.info);
            controller.Present(Execution(2), second, second.info);

            controller.EndExecution(1);
            Assert.AreEqual(1, controller.ActiveMotionCount);
            Assert.IsTrue(controller.HasMotion(2));
            Assert.IsFalse(controller.HasMotion(1));
            controller.Present(Execution(3), first, first.info);
            Assert.AreEqual(2, controller.ActiveMotionCount);
            controller.EndExecution(2);
            Assert.IsTrue(controller.HasMotion(3));
            controller.EndExecution(3);
            Assert.IsFalse(controller.HasActiveGraph);
            Assert.AreEqual(0, controller.ActiveMotionCount);
            yield return null;
        }

        [UnityTest]
        public IEnumerator MotionPresentation_AuthoringChangedAfterCapture_UsesCapturedClipAndLoop()
        {
            CharacterAnimationController controller = CreateController();
            MotionDefinition motion = CreateMotion();
            motion.loop = true;
            MotionPresentationData captured = MotionPresentationData.Capture(motion, motion.info);
            motion.loop = false;
            motion.info = default;

            controller.Present(Execution(1, 1.25f), captured);
            yield return null;
            yield return null;

            Assert.IsTrue(controller.HasMotion(1));
            Assert.AreEqual(0.25f, controller.transform.GetChild(0).localPosition.x, 0.01f);
            controller.EndExecution(1);
            Assert.IsFalse(controller.HasActiveGraph);
        }

        [UnityTest]
        public IEnumerator MotionPresentation_AuthoringDestroyedAfterCapture_ContinuesAndRecreatesFromSnapshot()
        {
            CharacterAnimationController controller = CreateController();
            MotionDefinition motion = CreateMotion();
            MotionPresentationData captured = MotionPresentationData.Capture(motion, motion.info);
            Object.Destroy(motion);
            yield return null;

            controller.Present(Execution(1, 0.5f), captured);
            yield return null;
            yield return null;
            Assert.IsTrue(controller.HasMotion(1));
            Assert.AreEqual(0.5f, controller.transform.GetChild(0).localPosition.x, 0.01f);

            controller.enabled = false;
            Assert.IsFalse(controller.HasActiveGraph);
            controller.enabled = true;
            controller.Present(Execution(2, 0.75f), captured);
            yield return null;
            yield return null;

            Assert.AreEqual(1, controller.ActiveMotionCount);
            Assert.IsTrue(controller.HasMotion(2));
            Assert.AreEqual(0.75f, controller.transform.GetChild(0).localPosition.x, 0.01f);
        }

        [UnityTest]
        public IEnumerator MotionPresentation_Disable_ReleasesGraphAndAllowsRecreation()
        {
            CharacterAnimationController controller = CreateController();
            MotionDefinition motion = CreateMotion();
            controller.Present(Execution(1), motion, motion.info);
            controller.Present(Execution(1, 0.5f), motion, motion.info);
            Assert.AreEqual(1, controller.ActiveMotionCount);

            controller.enabled = false;
            Assert.IsFalse(controller.HasActiveGraph);
            Assert.AreEqual(0, controller.ActiveMotionCount);
            controller.enabled = true;
            controller.Present(Execution(1, 0.75f), motion, motion.info);
            Assert.IsTrue(controller.HasActiveGraph);
            Assert.AreEqual(1, controller.ActiveMotionCount);
            yield return null;
        }

        [UnityTest]
        public IEnumerator MotionPresentation_Update_DoesNotAdvanceSharedPhaseClock()
        {
            CharacterAnimationController controller = CreateController();
            controller.SetExecutionState(new AnimationStateData
            {
                currentPhase = AnimationPhase.Active, normalizedTime = 0.4f, isCommitted = true,
            });

            yield return null;
            yield return null;

            Assert.AreEqual(AnimationPhase.Active, controller.Bridge.CurrentState.currentPhase);
            Assert.AreEqual(0.4f, controller.Bridge.CurrentState.normalizedTime);
            controller.SetExecutionState(default);
            Assert.AreEqual(AnimationPhase.Neutral, controller.Bridge.CurrentState.currentPhase);
        }
    }
}
#endif
