using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class CompanionAI_FollowBehaviorTests
    {
        [Test]
        public void FollowBehavior_CloseDistance_Waiting()
        {
            FollowBehavior follow = new FollowBehavior(2.0f, 15.0f);

            FollowState state = follow.Evaluate(Vector2.zero, new Vector2(1f, 0f));

            Assert.AreEqual(FollowState.Waiting, state);
        }

        [Test]
        public void FollowBehavior_MidDistance_Following()
        {
            FollowBehavior follow = new FollowBehavior(2.0f, 15.0f);

            FollowState state = follow.Evaluate(Vector2.zero, new Vector2(8f, 0f));

            Assert.AreEqual(FollowState.Following, state);
        }

        [Test]
        public void FollowBehavior_FarDistance_Teleporting()
        {
            FollowBehavior follow = new FollowBehavior(2.0f, 15.0f);

            FollowState state = follow.Evaluate(Vector2.zero, new Vector2(20f, 0f));

            Assert.AreEqual(FollowState.Teleporting, state);
        }

        [Test]
        public void FollowBehavior_SetDistances_Changes()
        {
            FollowBehavior follow = new FollowBehavior(2.0f, 15.0f);
            follow.SetDistances(5.0f, 10.0f);

            FollowState state = follow.Evaluate(Vector2.zero, new Vector2(3f, 0f));
            Assert.AreEqual(FollowState.Waiting, state);

            state = follow.Evaluate(Vector2.zero, new Vector2(12f, 0f));
            Assert.AreEqual(FollowState.Teleporting, state);
        }
    }
}
