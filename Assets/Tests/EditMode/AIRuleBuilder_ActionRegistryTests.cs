using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class AIRuleBuilder_ActionRegistryTests
    {
        [Test]
        public void ActionRegistry_DefaultActions_Unlocked()
        {
            ActionTypeRegistry registry = new ActionTypeRegistry();
            Assert.IsTrue(registry.IsUnlocked(new ActionUnlockKey
            {
                execType = ActionExecType.Attack,
                paramId = 0
            }));
            Assert.IsTrue(registry.IsUnlocked(new ActionUnlockKey
            {
                execType = ActionExecType.Instant,
                paramId = (int)InstantAction.Dodge
            }));
        }

        [Test]
        public void ActionRegistry_Unlock_AddsNew()
        {
            ActionTypeRegistry registry = new ActionTypeRegistry();
            ActionUnlockKey key = new ActionUnlockKey
            {
                execType = ActionExecType.Sustained,
                paramId = (int)SustainedAction.Flank
            };

            Assert.IsFalse(registry.IsUnlocked(key));
            registry.Unlock(key);
            Assert.IsTrue(registry.IsUnlocked(key));
        }

        [Test]
        public void ActionRegistry_Unlock_FiresEvent()
        {
            ActionTypeRegistry registry = new ActionTypeRegistry();
            ActionUnlockKey received = default;
            registry.OnActionUnlocked += (k) => received = k;

            ActionUnlockKey key = new ActionUnlockKey
            {
                execType = ActionExecType.Sustained,
                paramId = (int)SustainedAction.ShieldDeploy
            };
            registry.Unlock(key);

            Assert.AreEqual(key.execType, received.execType);
            Assert.AreEqual(key.paramId, received.paramId);
        }

        [Test]
        public void ActionRegistry_NotUnlocked_ReturnsFalse()
        {
            ActionTypeRegistry registry = new ActionTypeRegistry();
            ActionUnlockKey key = new ActionUnlockKey
            {
                execType = ActionExecType.Broadcast,
                paramId = 99
            };
            Assert.IsFalse(registry.IsUnlocked(key));
        }
    }
}
