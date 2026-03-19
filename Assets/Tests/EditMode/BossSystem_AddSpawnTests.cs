using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class BossSystem_AddSpawnTests
    {
        [Test]
        public void BossAddSpawnLogic_PhaseWithAdds_ReturnsSpawnerIds()
        {
            BossPhaseData phase = new BossPhaseData
            {
                phaseName = "Phase2",
                spawnAdds = true,
                addSpawnerIds = new string[] { "spawner_a", "spawner_b" }
            };

            BossAddSpawnLogic logic = new BossAddSpawnLogic();
            string[] ids = logic.GetSpawnersForPhase(phase);

            Assert.AreEqual(2, ids.Length);
            Assert.AreEqual("spawner_a", ids[0]);
        }

        [Test]
        public void BossAddSpawnLogic_PhaseWithoutAdds_ReturnsEmpty()
        {
            BossPhaseData phase = new BossPhaseData
            {
                phaseName = "Phase1",
                spawnAdds = false,
                addSpawnerIds = new string[0]
            };

            BossAddSpawnLogic logic = new BossAddSpawnLogic();
            string[] ids = logic.GetSpawnersForPhase(phase);

            Assert.AreEqual(0, ids.Length);
        }

        [Test]
        public void BossAddSpawnLogic_NullSpawnerIds_ReturnsEmpty()
        {
            BossPhaseData phase = new BossPhaseData
            {
                phaseName = "Phase1",
                spawnAdds = true,
                addSpawnerIds = null
            };

            BossAddSpawnLogic logic = new BossAddSpawnLogic();
            string[] ids = logic.GetSpawnersForPhase(phase);

            Assert.AreEqual(0, ids.Length);
        }
    }
}
