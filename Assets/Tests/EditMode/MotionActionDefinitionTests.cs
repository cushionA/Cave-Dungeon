using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public class MotionActionDefinitionTests
    {
        private readonly List<ScriptableObject> _created = new List<ScriptableObject>();

        private T Create<T>() where T : ScriptableObject
        {
            T value = ScriptableObject.CreateInstance<T>();
            _created.Add(value);
            return value;
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(_created[i]);
            }
            _created.Clear();
        }

        [Test]
        public void ActionDefinition_SharedMotion_KeepsIndependentCostsAndGameplayEffects()
        {
            MotionDefinition motion = Create<MotionDefinition>();
            motion.info = new MotionInfo { activeMotionDuration = 1f };
            ActionDefinition emote = Create<ActionDefinition>();
            ActionDefinition ritual = Create<ActionDefinition>();
            emote.motion = motion;
            emote.actState = ActState.Neutral;
            ritual.motion = motion;
            ritual.staminaCost = 20f;
            ritual.actionEffects = new[] { new ActionEffect { type = ActionEffectType.Invincible, duration = 0.5f } };

            Assert.AreSame(emote.motion, ritual.motion);
            Assert.AreEqual(0f, emote.staminaCost);
            Assert.AreEqual(20f, ritual.staminaCost);
            Assert.IsEmpty(emote.actionEffects);
            Assert.AreEqual(1, ritual.actionEffects.Length);
            Assert.AreEqual(ActState.Neutral, emote.actState);
        }

        [Test]
        public void ActionDefinition_GetMotion_ReturnsValueSnapshot()
        {
            MotionDefinition motion = Create<MotionDefinition>();
            motion.info = new MotionInfo { preMotionDuration = 0.2f, activeMotionDuration = 0.5f, recoveryDuration = 0.3f };
            ActionDefinition action = Create<ActionDefinition>();
            action.motion = motion;

            MotionInfo snapshot = action.GetMotion();
            snapshot.activeMotionDuration = 10f;

            Assert.AreEqual(0.5f, action.GetMotion().activeMotionDuration);
            Assert.AreEqual(1f, motion.Duration, 0.001f);
        }

        [Test]
        public void ActionDefinition_MissingMotion_AllowsGameplayWithoutVisualAsset()
        {
            ActionDefinition action = Create<ActionDefinition>();
            action.staminaCost = 5f;

            MotionInfo motion = action.GetMotion();

            Assert.AreEqual(0f, motion.preMotionDuration);
            Assert.AreEqual(0f, motion.activeMotionDuration);
            Assert.AreEqual(5f, action.staminaCost);
        }

        [Test]
        public void AttackInfo_ZeroAnticipation_PreservesMotionReferenceAndActionRules()
        {
            MotionDefinition motion = Create<MotionDefinition>();
            motion.info = new MotionInfo { activeMotionDuration = 0.3f, recoveryDuration = 0.2f };
            AttackInfo attack = Create<AttackInfo>();
            attack.motion = motion;
            attack.cancelPoint = 0.5f;
            attack.movement = new ActionMovementProfile { speed = 4f, duration = 0.25f };

            ActionDefinition action = attack;
            AttackMotionData runtime = CombatDataHelper.BuildMotionData(attack);

            Assert.AreEqual(0f, action.GetMotion().preMotionDuration);
            Assert.AreEqual(0.5f, action.cancelPoint);
            Assert.AreEqual(1f, runtime.attackMoveDistance);
            Assert.AreEqual(0.25f, runtime.attackMoveDuration);
        }
    }
}
