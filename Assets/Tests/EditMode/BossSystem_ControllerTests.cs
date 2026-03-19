using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class BossSystem_ControllerTests
    {
        private BossPhaseData[] CreateTestPhases()
        {
            return new BossPhaseData[]
            {
                new BossPhaseData
                {
                    phaseName = "Phase1",
                    modes = new AIMode[] { new AIMode { modeName = "Normal" } },
                    exitCondition = new PhaseCondition { type = PhaseConditionType.HpThreshold, threshold = 0.5f },
                    transitionInvincibleTime = 2.0f,
                    spawnAdds = false,
                    addSpawnerIds = new string[0]
                },
                new BossPhaseData
                {
                    phaseName = "Phase2",
                    modes = new AIMode[] { new AIMode { modeName = "Enraged" } },
                    exitCondition = new PhaseCondition { type = PhaseConditionType.Custom, threshold = 0f },
                    transitionInvincibleTime = 0f,
                    spawnAdds = false,
                    addSpawnerIds = new string[0]
                }
            };
        }

        [Test]
        public void BossControllerLogic_Constructor_InitializesCorrectly()
        {
            BossPhaseData[] phases = CreateTestPhases();
            BossControllerLogic controller = new BossControllerLogic(phases, "boss_01");

            Assert.IsFalse(controller.IsEncounterActive);
            Assert.AreEqual("boss_01", controller.BossId);
        }

        [Test]
        public void BossControllerLogic_StartEncounter_ActivatesBoss()
        {
            BossPhaseData[] phases = CreateTestPhases();
            BossControllerLogic controller = new BossControllerLogic(phases, "boss_01");

            controller.StartEncounter();

            Assert.IsTrue(controller.IsEncounterActive);
        }

        [Test]
        public void BossControllerLogic_UpdateEncounter_TransitionsPhaseOnHpDrop()
        {
            BossPhaseData[] phases = CreateTestPhases();
            BossControllerLogic controller = new BossControllerLogic(phases, "boss_01");
            controller.StartEncounter();

            int phaseChanged = -1;
            controller.OnPhaseChanged += (oldP, newP) => phaseChanged = newP;

            // HP 40% → 遷移
            BossPhaseTransitionResult result = controller.UpdateEncounter(0.4f, 10f, 0);

            Assert.IsTrue(result.transitioned);
            Assert.AreEqual(1, phaseChanged);
        }

        [Test]
        public void BossControllerLogic_OnDefeated_EndsEncounter()
        {
            BossPhaseData[] phases = CreateTestPhases();
            BossControllerLogic controller = new BossControllerLogic(phases, "boss_01");
            controller.StartEncounter();

            bool defeatedFired = false;
            controller.OnBossDefeated += (id) => defeatedFired = true;

            controller.OnDefeated();

            Assert.IsFalse(controller.IsEncounterActive);
            Assert.IsTrue(defeatedFired);
        }

        [Test]
        public void BossControllerLogic_UpdateEncounter_WhenNotActive_DoesNothing()
        {
            BossPhaseData[] phases = CreateTestPhases();
            BossControllerLogic controller = new BossControllerLogic(phases, "boss_01");

            BossPhaseTransitionResult result = controller.UpdateEncounter(0.1f, 0f, 0);

            Assert.IsFalse(result.transitioned);
        }
    }
}
