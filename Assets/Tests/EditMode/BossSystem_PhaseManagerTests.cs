using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class BossSystem_PhaseManagerTests
    {
        private BossPhaseData[] CreateTestPhases()
        {
            return new BossPhaseData[]
            {
                new BossPhaseData
                {
                    phaseName = "第1形態",
                    exitCondition = new PhaseCondition { type = PhaseConditionType.HpThreshold, threshold = 0.5f },
                    transitionInvincibleTime = 2.0f,
                    spawnAdds = false,
                    addSpawnerIds = new string[0]
                },
                new BossPhaseData
                {
                    phaseName = "第2形態",
                    exitCondition = new PhaseCondition { type = PhaseConditionType.HpThreshold, threshold = 0.2f },
                    transitionInvincibleTime = 3.0f,
                    spawnAdds = true,
                    addSpawnerIds = new string[] { "spawner_01" }
                },
                new BossPhaseData
                {
                    phaseName = "最終形態",
                    exitCondition = new PhaseCondition { type = PhaseConditionType.Custom, threshold = 0f },
                    transitionInvincibleTime = 0f,
                    spawnAdds = false,
                    addSpawnerIds = new string[0]
                }
            };
        }

        [Test]
        public void BossPhaseManager_Constructor_StartsAtPhase0()
        {
            BossPhaseData[] phases = CreateTestPhases();
            BossPhaseManager manager = new BossPhaseManager(phases);

            Assert.AreEqual(0, manager.CurrentPhase);
            Assert.AreEqual(3, manager.MaxPhase);
        }

        [Test]
        public void BossPhaseManager_CheckTransition_HpThreshold_ReturnsTrueWhenBelow()
        {
            BossPhaseData[] phases = CreateTestPhases();
            BossPhaseManager manager = new BossPhaseManager(phases);

            // HP 60% — まだ遷移しない
            Assert.IsFalse(manager.CheckTransition(0.6f, 0f, 0));

            // HP 50% — 閾値ぴったり → 遷移
            Assert.IsTrue(manager.CheckTransition(0.5f, 0f, 0));
        }

        [Test]
        public void BossPhaseManager_TransitionToNextPhase_AdvancesPhase()
        {
            BossPhaseData[] phases = CreateTestPhases();
            BossPhaseManager manager = new BossPhaseManager(phases);

            // フェーズ0→1
            manager.CheckTransition(0.4f, 0f, 0);
            BossPhaseTransitionResult result = manager.TransitionToNextPhase();

            Assert.AreEqual(1, manager.CurrentPhase);
            Assert.AreEqual(3.0f, result.invincibleTime, 0.001f);
            Assert.IsTrue(result.spawnAdds);
            Assert.AreEqual(1, result.addSpawnerIds.Length);
        }

        [Test]
        public void BossPhaseManager_CheckTransition_Timer_ReturnsTrueWhenElapsed()
        {
            BossPhaseData[] phases = new BossPhaseData[]
            {
                new BossPhaseData
                {
                    phaseName = "Phase1",
                    exitCondition = new PhaseCondition { type = PhaseConditionType.Timer, threshold = 60f },
                    transitionInvincibleTime = 1.0f,
                    spawnAdds = false,
                    addSpawnerIds = new string[0]
                },
                new BossPhaseData
                {
                    phaseName = "Phase2",
                    exitCondition = new PhaseCondition { type = PhaseConditionType.Custom, threshold = 0f },
                    transitionInvincibleTime = 0f,
                    spawnAdds = false,
                    addSpawnerIds = new string[0]
                }
            };

            BossPhaseManager manager = new BossPhaseManager(phases);

            Assert.IsFalse(manager.CheckTransition(1.0f, 59f, 0));
            Assert.IsTrue(manager.CheckTransition(1.0f, 60f, 0));
        }

        [Test]
        public void BossPhaseManager_TransitionAtMaxPhase_DoesNotAdvance()
        {
            BossPhaseData[] phases = CreateTestPhases();
            BossPhaseManager manager = new BossPhaseManager(phases);

            // 最終フェーズまで進める
            manager.CheckTransition(0.4f, 0f, 0);
            manager.TransitionToNextPhase(); // → 1
            manager.CheckTransition(0.1f, 0f, 0);
            manager.TransitionToNextPhase(); // → 2

            // 最終フェーズで遷移試行 → 変化なし
            BossPhaseTransitionResult result = manager.TransitionToNextPhase();
            Assert.AreEqual(2, manager.CurrentPhase);
            Assert.IsFalse(result.transitioned);
        }
    }
}
