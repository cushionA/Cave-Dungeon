using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class AICore_DamageScoreTrackerTests
    {
        [Test]
        public void DamageScoreTracker_AddDamage_AccumulatesScore()
        {
            DamageScoreTracker tracker = new DamageScoreTracker();

            tracker.AddDamage(10, 50f, 0f);
            tracker.AddDamage(10, 30f, 0f);

            float score = tracker.GetScore(10, 0f);
            Assert.AreEqual(80f, score, 0.01f);
        }

        [Test]
        public void DamageScoreTracker_Decay_ReducesOverTime()
        {
            DamageScoreTracker tracker = new DamageScoreTracker();
            tracker.AddDamage(10, 100f, 0f);

            float scoreAt0 = tracker.GetScore(10, 0f);
            float scoreAt5 = tracker.GetScore(10, 5f);

            Assert.AreEqual(100f, scoreAt0, 0.01f);
            Assert.Less(scoreAt5, scoreAt0);
            Assert.Greater(scoreAt5, 0f);
        }

        [Test]
        public void DamageScoreTracker_GetHighest_ReturnsTopAttacker()
        {
            DamageScoreTracker tracker = new DamageScoreTracker();
            tracker.AddDamage(10, 50f, 0f);
            tracker.AddDamage(20, 100f, 0f);

            int highest = tracker.GetHighestScoreAttacker(0f);

            Assert.AreEqual(20, highest);
        }

        [Test]
        public void DamageScoreTracker_RemoveAttacker_ClearsEntry()
        {
            DamageScoreTracker tracker = new DamageScoreTracker();
            tracker.AddDamage(10, 50f, 0f);

            tracker.RemoveAttacker(10);

            Assert.AreEqual(0f, tracker.GetScore(10, 0f));
        }
    }
}
