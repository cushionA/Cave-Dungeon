using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class LevelUpSystemCoreTests
    {
        [Test]
        public void LevelUpLogic_AddExp_AccumulatesExp()
        {
            LevelUpLogic logic = new LevelUpLogic(1);

            logic.AddExp(50);

            Assert.AreEqual(50, logic.CurrentExp);
        }

        [Test]
        public void LevelUpLogic_AddExp_WhenEnough_LevelsUp()
        {
            LevelUpLogic logic = new LevelUpLogic(1);
            // Level1: need 100 exp to level up

            int levelsGained = logic.AddExp(100);

            Assert.AreEqual(1, levelsGained);
            Assert.AreEqual(2, logic.Level);
            Assert.AreEqual(LevelUpLogic.k_PointsPerLevel, logic.AvailablePoints);
            Assert.AreEqual(0, logic.CurrentExp, "Remaining exp after level up should be 0");
        }

        [Test]
        public void LevelUpLogic_AllocatePoint_DecreasesAvailable()
        {
            LevelUpLogic logic = new LevelUpLogic(1);
            logic.AddExp(100); // Level up to 2, gain 3 points

            bool success = logic.AllocatePoint(StatType.Vit);

            Assert.IsTrue(success);
            Assert.AreEqual(LevelUpLogic.k_PointsPerLevel - 1, logic.AvailablePoints);
            Assert.AreEqual(1, logic.AllocatedStats.vit);
        }

        [Test]
        public void LevelUpLogic_RecalculateVitals_AppliesStatBonuses()
        {
            LevelUpLogic logic = new LevelUpLogic(1);
            logic.AddExp(100); // Level up to 2, gain 3 points
            logic.AllocatePoint(StatType.Vit); // vit +1
            logic.AllocatePoint(StatType.Vit); // vit +2
            logic.AllocatePoint(StatType.Mnd); // mnd +1

            CharacterVitals baseVitals = new CharacterVitals
            {
                currentHp = 100,
                maxHp = 100,
                currentMp = 50,
                maxMp = 50,
                level = 1
            };

            CharacterVitals result = logic.RecalculateVitals(baseVitals);

            // vit 2 * k_HpPerVit(10) = 20 bonus hp
            Assert.AreEqual(120, result.maxHp);
            // mnd 1 * k_MpPerMnd(5) = 5 bonus mp
            Assert.AreEqual(55, result.maxMp);
            // level should be updated
            Assert.AreEqual(2, result.level);
        }

        [Test]
        public void LevelUpLogic_AddExp_AtMaxLevel_DoesNotExceedCap()
        {
            LevelUpLogic logic = new LevelUpLogic(LevelUpLogic.k_MaxLevel);

            // 最大レベルで膨大な exp を与える
            int levelsGained = logic.AddExp(int.MaxValue / 2);

            Assert.AreEqual(0, levelsGained,
                "最大レベル到達時はレベルアップしない");
            Assert.AreEqual(LevelUpLogic.k_MaxLevel, logic.Level);
            Assert.AreEqual(0, logic.CurrentExp,
                "最大レベル到達後は余剰 exp を破棄する");
        }

        [Test]
        public void LevelUpLogic_AddExp_AtNearMaxLevel_StopsAtCap()
        {
            // MaxLevel-1 から大量 exp で MaxLevel に到達する
            LevelUpLogic logic = new LevelUpLogic(LevelUpLogic.k_MaxLevel - 1);

            int levelsGained = logic.AddExp(int.MaxValue / 2);

            Assert.AreEqual(1, levelsGained,
                "レベルは最大1つだけ上がる（MaxLevel に到達）");
            Assert.AreEqual(LevelUpLogic.k_MaxLevel, logic.Level);
            Assert.AreEqual(0, logic.CurrentExp,
                "MaxLevel 到達時の余剰 exp は破棄");
        }
    }
}
