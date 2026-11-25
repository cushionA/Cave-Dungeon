using Game.Core;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    public class ActionTargetSelectionTests
    {
        [Test]
        public void ActionTargetSelection_DuplicateInvalidAndExtraMarks_KeepsThreeOriginalGenerations()
        {
            ActionTargetSelection selection = default;
            ActorRef original = new ActorRef(7, 1);
            Assert.IsTrue(selection.TryAdd(original));
            ActionTargetSelection snapshot = selection;
            Assert.IsFalse(selection.TryAdd(default));
            Assert.IsFalse(selection.TryAdd(original));
            Assert.IsFalse(selection.TryAdd(new ActorRef(7, 2)));
            Assert.IsTrue(selection.TryAdd(new ActorRef(8, 2)));
            Assert.IsTrue(selection.TryAdd(new ActorRef(9, 3)));
            Assert.IsFalse(selection.TryAdd(new ActorRef(10, 4)));
            Assert.AreEqual(3, selection.Count);
            Assert.AreEqual(1, snapshot.Count);
            Assert.AreEqual(original, snapshot[0]);
        }
    }
}
