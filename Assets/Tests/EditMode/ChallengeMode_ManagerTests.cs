using System.Collections.Generic;
using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class ChallengeMode_ManagerTests
    {
        private ChallengeManager _manager;

        [SetUp]
        public void SetUp()
        {
            _manager = new ChallengeManager();
        }

        [Test]
        public void UnlockChallenge_WhenCalled_ShouldMakeItUnlocked()
        {
            _manager.UnlockChallenge("challenge_01");

            Assert.IsTrue(_manager.IsUnlocked("challenge_01"));
            string[] unlocked = _manager.GetUnlockedChallengeIds();
            Assert.AreEqual(1, unlocked.Length);
            Assert.AreEqual("challenge_01", unlocked[0]);
        }

        [Test]
        public void UnlockChallenge_WhenDuplicate_ShouldNotDuplicate()
        {
            _manager.UnlockChallenge("challenge_01");
            _manager.UnlockChallenge("challenge_01");
            _manager.UnlockChallenge("challenge_01");

            string[] unlocked = _manager.GetUnlockedChallengeIds();
            Assert.AreEqual(1, unlocked.Length);
        }

        [Test]
        public void IsUnlocked_WhenNotUnlocked_ShouldReturnFalse()
        {
            Assert.IsFalse(_manager.IsUnlocked("challenge_99"));
            Assert.IsFalse(_manager.IsUnlocked(""));
            Assert.IsFalse(_manager.IsUnlocked(null));
        }

        [Test]
        public void SerializeDeserialize_ShouldPreserveState()
        {
            _manager.UnlockChallenge("challenge_01");
            _manager.UnlockChallenge("challenge_02");
            _manager.UnlockChallenge("challenge_03");

            object serialized = _manager.Serialize();

            ChallengeManager restored = new ChallengeManager();
            restored.Deserialize(serialized);

            Assert.IsTrue(restored.IsUnlocked("challenge_01"));
            Assert.IsTrue(restored.IsUnlocked("challenge_02"));
            Assert.IsTrue(restored.IsUnlocked("challenge_03"));
            Assert.IsFalse(restored.IsUnlocked("challenge_04"));
            Assert.AreEqual(3, restored.GetUnlockedChallengeIds().Length);

            // SaveId の確認
            Assert.AreEqual("ChallengeManager", _manager.SaveId);
        }
    }
}
