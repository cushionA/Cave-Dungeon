using NUnit.Framework;
using Game.Core;
using R3;

namespace Game.Tests.EditMode
{
    [TestFixture]
    public class DamageScoreWiringTests
    {
        private GameEvents _events;
        private DamageScoreTracker _tracker;
        private DamageScoreConnector _connector;

        private const int k_AttackerHash = 10;
        private const int k_DefenderHash = 20;

        [SetUp]
        public void SetUp()
        {
            _events = new GameEvents();
            _tracker = new DamageScoreTracker();
            _connector = new DamageScoreConnector(_tracker, _events);
        }

        [TearDown]
        public void TearDown()
        {
            _connector.Dispose();
            _events.Dispose();
        }

        [Test]
        public void DamageScoreConnector_OnDamageDealt_AccumulatesScore()
        {
            DamageResult result = new DamageResult
            {
                totalDamage = 50,
                isCritical = false,
                isKill = false
            };

            _events.FireDamageDealt(result, k_AttackerHash, k_DefenderHash);

            float score = _tracker.GetScore(k_AttackerHash, 0f);
            Assert.AreEqual(50f, score, 0.1f, "ダメージイベントでスコアが蓄積されるべき");
        }

        [Test]
        public void DamageScoreConnector_MultipleDamageEvents_AccumulatesCorrectly()
        {
            DamageResult result1 = new DamageResult { totalDamage = 30 };
            DamageResult result2 = new DamageResult { totalDamage = 20 };

            _events.FireDamageDealt(result1, k_AttackerHash, k_DefenderHash);
            _events.FireDamageDealt(result2, k_AttackerHash, k_DefenderHash);

            float score = _tracker.GetScore(k_AttackerHash, 0f);
            Assert.AreEqual(50f, score, 0.1f, "複数ダメージイベントのスコアが蓄積されるべき");
        }

        [Test]
        public void DamageScoreConnector_DifferentAttackers_TrackedSeparately()
        {
            int attacker2Hash = 11;

            DamageResult result1 = new DamageResult { totalDamage = 100 };
            DamageResult result2 = new DamageResult { totalDamage = 60 };

            _events.FireDamageDealt(result1, k_AttackerHash, k_DefenderHash);
            _events.FireDamageDealt(result2, attacker2Hash, k_DefenderHash);

            float score1 = _tracker.GetScore(k_AttackerHash, 0f);
            float score2 = _tracker.GetScore(attacker2Hash, 0f);

            Assert.AreEqual(100f, score1, 0.1f);
            Assert.AreEqual(60f, score2, 0.1f);
        }

        [Test]
        public void DamageScoreConnector_ZeroDamage_DoesNotAccumulate()
        {
            DamageResult result = new DamageResult { totalDamage = 0 };

            _events.FireDamageDealt(result, k_AttackerHash, k_DefenderHash);

            float score = _tracker.GetScore(k_AttackerHash, 0f);
            Assert.AreEqual(0f, score, 0.001f, "0ダメージではスコア蓄積しない");
        }

        [Test]
        public void DamageScoreConnector_AfterDispose_NoLongerAccumulates()
        {
            _connector.Dispose();

            DamageResult result = new DamageResult { totalDamage = 100 };
            _events.FireDamageDealt(result, k_AttackerHash, k_DefenderHash);

            float score = _tracker.GetScore(k_AttackerHash, 0f);
            Assert.AreEqual(0f, score, 0.001f, "Dispose後はスコア蓄積しない");
        }
    }
}
