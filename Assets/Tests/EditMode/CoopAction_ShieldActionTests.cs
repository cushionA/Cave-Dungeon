using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class CoopAction_ShieldActionTests
    {
        [Test]
        public void ShieldAction_Execute_ActivatesShield()
        {
            ShieldCoopAction action = new ShieldCoopAction(5f);
            action.ExecuteCombo(0, 1, 2);
            Assert.IsTrue(action.IsShieldActive);
        }

        [Test]
        public void ShieldAction_Tick_DeactivatesAfterDuration()
        {
            ShieldCoopAction action = new ShieldCoopAction(2f);
            action.ExecuteCombo(0, 1, 2);

            action.TickShield(1f);
            Assert.IsTrue(action.IsShieldActive);

            action.TickShield(1.5f);
            Assert.IsFalse(action.IsShieldActive);
        }

        [Test]
        public void ShieldAction_CheckReflect_InRange()
        {
            ShieldCoopAction action = new ShieldCoopAction(5f);
            action.ExecuteCombo(0, 1, 2);

            Assert.IsTrue(action.CheckReflect(new Vector2(0.5f, 0f), 2f));
            Assert.IsFalse(action.CheckReflect(new Vector2(5f, 0f), 2f));
        }

        [Test]
        public void ShieldAction_Inactive_NoReflect()
        {
            ShieldCoopAction action = new ShieldCoopAction(5f);
            Assert.IsFalse(action.CheckReflect(Vector2.zero, 2f));
        }
    }
}
