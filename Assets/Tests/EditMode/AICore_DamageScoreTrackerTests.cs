using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class AICore_DamageScoreTrackerTests
    {
        private DamageScoreTracker _tracker;

        [SetUp]
        public void SetUp()
        {
            _tracker = new DamageScoreTracker();
        }

        [TearDown]
        public void TearDown()
        {
            _tracker.Dispose();
        }

        [Test]
        public void DamageScoreTracker_AddDamage_AccumulatesScore()
        {
            _tracker.AddDamage(10, 50f, 0f);
            _tracker.AddDamage(10, 30f, 0f);

            float score = _tracker.GetScore(10, 0f);
            Assert.AreEqual(80f, score, 0.01f);
        }

        [Test]
        public void DamageScoreTracker_Decay_ReducesOverTime()
        {
            _tracker.AddDamage(10, 100f, 0f);

            float scoreAt0 = _tracker.GetScore(10, 0f);
            float scoreAt5 = _tracker.GetScore(10, 5f);

            Assert.AreEqual(100f, scoreAt0, 0.01f);
            Assert.Less(scoreAt5, scoreAt0);
            Assert.Greater(scoreAt5, 0f);
        }

        [Test]
        public void DamageScoreTracker_GetHighest_ReturnsTopAttacker()
        {
            _tracker.AddDamage(10, 50f, 0f);
            _tracker.AddDamage(20, 100f, 0f);

            int highest = _tracker.GetHighestScoreAttacker(0f);

            Assert.AreEqual(20, highest);
        }

        [Test]
        public void DamageScoreTracker_RemoveAttacker_ClearsEntry()
        {
            _tracker.AddDamage(10, 50f, 0f);

            _tracker.RemoveAttacker(10);

            Assert.AreEqual(0f, _tracker.GetScore(10, 0f));
        }
    }
}
