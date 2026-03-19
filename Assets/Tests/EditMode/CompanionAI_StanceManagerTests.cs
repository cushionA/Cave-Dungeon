using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class CompanionAI_StanceManagerTests
    {
        [Test]
        public void StanceManager_Aggressive_HighAttackWeight()
        {
            StanceManager manager = new StanceManager();
            manager.SetStance(CompanionStance.Aggressive);

            StanceWeights weights = manager.GetWeights();

            Assert.AreEqual(2.0f, weights.attackMultiplier, 0.01f);
            Assert.AreEqual(0.5f, weights.healMultiplier, 0.01f);
        }

        [Test]
        public void StanceManager_Supportive_HighHealWeight()
        {
            StanceManager manager = new StanceManager();
            manager.SetStance(CompanionStance.Supportive);

            StanceWeights weights = manager.GetWeights();

            Assert.AreEqual(3.0f, weights.healMultiplier, 0.01f);
            Assert.AreEqual(0.3f, weights.attackMultiplier, 0.01f);
        }

        [Test]
        public void StanceManager_Passive_ZeroCombat()
        {
            StanceManager manager = new StanceManager();
            manager.SetStance(CompanionStance.Passive);

            StanceWeights weights = manager.GetWeights();

            Assert.AreEqual(0f, weights.attackMultiplier, 0.01f);
            Assert.AreEqual(0f, weights.defenseMultiplier, 0.01f);
        }

        [Test]
        public void StanceManager_ChangeStance_FiresEvent()
        {
            StanceManager manager = new StanceManager();
            CompanionStance received = CompanionStance.Aggressive;
            manager.OnStanceChanged += (s) => received = s;

            manager.SetStance(CompanionStance.Defensive);

            Assert.AreEqual(CompanionStance.Defensive, received);
        }
    }
}
