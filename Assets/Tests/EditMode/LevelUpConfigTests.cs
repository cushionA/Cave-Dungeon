using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// LevelUpConfig ScriptableObject と LevelUpLogic の連携を検証するテスト。
    /// </summary>
    public class LevelUpConfigTests
    {
        [TearDown]
        public void TearDown()
        {
            // 他テストに副作用を残さないようデフォルトをクリア
            LevelUpLogic.SetDefaultConfig(null);
        }

        // ===== Default 挙動 (SO 未注入) =====

        [Test]
        public void LevelUpConfig_DefaultLogic_MaxLevelIs99()
        {
            LevelUpLogic logic = new LevelUpLogic(1);

            Assert.AreEqual(99, logic.MaxLevel,
                "LevelUpConfig 未注入時は k_DefaultMaxLevel (99) が適用される");
            Assert.AreEqual(LevelUpLogic.k_DefaultMaxLevel, logic.MaxLevel);
        }

        [Test]
        public void LevelUpConfig_DefaultLogic_AtLevel99_DoesNotExceed()
        {
            LevelUpLogic logic = new LevelUpLogic(99);

            int levelsGained = logic.AddExp(int.MaxValue / 2);

            Assert.AreEqual(0, levelsGained, "既定最大レベル 99 到達時は上がらない");
            Assert.AreEqual(99, logic.Level);
            Assert.AreEqual(0, logic.CurrentExp, "到達後は余剰 exp を破棄");
        }

        // ===== maxLevel=50 の SO を渡して 51 レベルでブロック =====

        [Test]
        public void LevelUpConfig_WithMaxLevel50_BlocksAt51()
        {
            LevelUpConfig config = ScriptableObject.CreateInstance<LevelUpConfig>();
            config.maxLevel = 50;

            try
            {
                LevelUpLogic logic = new LevelUpLogic(50, config);

                Assert.AreEqual(50, logic.MaxLevel);

                int levelsGained = logic.AddExp(int.MaxValue / 2);

                Assert.AreEqual(0, levelsGained,
                    "maxLevel=50 到達時点でそれ以上レベルが上がらない (51 レベルへ到達しない)");
                Assert.AreEqual(50, logic.Level,
                    "レベルは 50 で止まる");
                Assert.AreEqual(0, logic.CurrentExp,
                    "最大レベル到達後は余剰 exp を破棄");
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void LevelUpConfig_WithMaxLevel50_NearCap_StopsAtCap()
        {
            LevelUpConfig config = ScriptableObject.CreateInstance<LevelUpConfig>();
            config.maxLevel = 50;

            try
            {
                LevelUpLogic logic = new LevelUpLogic(49, config);

                int levelsGained = logic.AddExp(int.MaxValue / 2);

                Assert.AreEqual(1, levelsGained, "49→50 で 1 レベル上がる (51 には届かない)");
                Assert.AreEqual(50, logic.Level);
                Assert.AreEqual(0, logic.CurrentExp);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        // ===== maxLevel=200 の SO で 100 レベル超えても進める =====

        [Test]
        public void LevelUpConfig_WithMaxLevel200_AllowsBeyond100()
        {
            LevelUpConfig config = ScriptableObject.CreateInstance<LevelUpConfig>();
            config.maxLevel = 200;

            try
            {
                // 99 レベルからさらに上げられることを検証 (既定キャップ 99 を超える)
                LevelUpLogic logic = new LevelUpLogic(99, config);

                Assert.AreEqual(200, logic.MaxLevel);

                int levelsGained = logic.AddExp(int.MaxValue / 2);

                Assert.Greater(logic.Level, 99,
                    "maxLevel=200 のとき従来キャップ 99 を超えてレベルアップできる");
                Assert.LessOrEqual(logic.Level, 200,
                    "maxLevel=200 を超えない");
                Assert.Greater(levelsGained, 0);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void LevelUpConfig_WithMaxLevel200_ReachesExactCap()
        {
            LevelUpConfig config = ScriptableObject.CreateInstance<LevelUpConfig>();
            config.maxLevel = 200;

            try
            {
                LevelUpLogic logic = new LevelUpLogic(199, config);

                int levelsGained = logic.AddExp(int.MaxValue / 2);

                Assert.AreEqual(1, levelsGained, "199→200 で 1 レベル上がる");
                Assert.AreEqual(200, logic.Level);
                Assert.AreEqual(0, logic.CurrentExp, "最大レベル到達後は余剰 exp を破棄");
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        // ===== 静的 Default 注入経路 =====

        [Test]
        public void LevelUpConfig_SetDefaultConfig_AppliesToNewInstances()
        {
            LevelUpConfig config = ScriptableObject.CreateInstance<LevelUpConfig>();
            config.maxLevel = 30;

            try
            {
                LevelUpLogic.SetDefaultConfig(config);

                // 引数なしコンストラクタで生成しても Default が適用される
                LevelUpLogic logic = new LevelUpLogic(1);

                Assert.AreEqual(30, logic.MaxLevel,
                    "SetDefaultConfig 後は引数なしコンストラクタでも SO の maxLevel が使われる");
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void LevelUpConfig_SetDefaultConfigNull_FallsBackToDefault()
        {
            LevelUpConfig config = ScriptableObject.CreateInstance<LevelUpConfig>();
            config.maxLevel = 30;

            try
            {
                LevelUpLogic.SetDefaultConfig(config);
                LevelUpLogic.SetDefaultConfig(null);

                LevelUpLogic logic = new LevelUpLogic(1);

                Assert.AreEqual(LevelUpLogic.k_DefaultMaxLevel, logic.MaxLevel,
                    "null リセット後は k_DefaultMaxLevel (99) にフォールバック");
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        // ===== バリデーション: 既存定数の互換性 =====

        [Test]
        public void LevelUpConfig_KMaxLevelConstant_EqualsDefault()
        {
            // 後方互換のため k_MaxLevel は k_DefaultMaxLevel と同値であること
            Assert.AreEqual(LevelUpLogic.k_DefaultMaxLevel, LevelUpLogic.k_MaxLevel);
            Assert.AreEqual(99, LevelUpLogic.k_MaxLevel);
        }
    }
}
