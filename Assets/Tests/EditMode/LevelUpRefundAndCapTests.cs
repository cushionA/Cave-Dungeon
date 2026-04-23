using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Refund API (特殊アイテム消費前提) と各ステータス上限値設定 (動的最大レベル算出) の
    /// テスト群。
    /// </summary>
    public class LevelUpRefundAndCapTests
    {
        // ─────────────────────────────────────────────
        //  Refund API テスト
        // ─────────────────────────────────────────────

        [Test]
        public void LevelUpLogic_RefundAllStatusPoints_WhenStrAllocated_ResetsStatsAndRestoresPoints()
        {
            LevelUpLogic logic = new LevelUpLogic(1);
            // Lv1 → Lv6 で 5 * 3 = 15 pts
            for (int i = 0; i < 5; i++)
            {
                logic.AddExp(logic.GetExpForNextLevel());
            }
            // 5 pts を Str に振る
            for (int i = 0; i < 5; i++)
            {
                Assert.IsTrue(logic.AllocatePoint(StatType.Str));
            }

            int levelBefore = logic.Level;
            int expectedPointsAfterRefund = levelBefore * LevelUpLogic.k_PointsPerLevel;

            logic.RefundAllStatusPoints();

            Assert.AreEqual(0, logic.AllocatedStats.str, "Str は 0 に戻る");
            Assert.AreEqual(0, logic.AllocatedStats.dex);
            Assert.AreEqual(0, logic.AllocatedStats.intel);
            Assert.AreEqual(0, logic.AllocatedStats.vit);
            Assert.AreEqual(0, logic.AllocatedStats.mnd);
            Assert.AreEqual(0, logic.AllocatedStats.end);
            Assert.AreEqual(expectedPointsAfterRefund, logic.AvailablePoints,
                "振れるポイントは level * k_PointsPerLevel に復元される");
        }

        [Test]
        public void LevelUpLogic_RefundStatus_WhenValidPoints_ReducesStatAndRestoresPoints()
        {
            LevelUpLogic logic = new LevelUpLogic(1);
            // Lv1 → Lv6 で 15 pts
            for (int i = 0; i < 5; i++)
            {
                logic.AddExp(logic.GetExpForNextLevel());
            }
            // 5 pts を Str に振る
            for (int i = 0; i < 5; i++)
            {
                logic.AllocatePoint(StatType.Str);
            }

            int availableBefore = logic.AvailablePoints;

            bool success = logic.RefundStatus(StatType.Str, 3);

            Assert.IsTrue(success);
            Assert.AreEqual(2, logic.AllocatedStats.str, "Str が 3 減って 2");
            Assert.AreEqual(availableBefore + 3, logic.AvailablePoints,
                "振れるポイントが 3 増える");
        }

        [Test]
        public void LevelUpLogic_RefundStatus_WhenNegativePoints_ReturnsFalse()
        {
            LevelUpLogic logic = new LevelUpLogic(1);
            for (int i = 0; i < 5; i++)
            {
                logic.AddExp(logic.GetExpForNextLevel());
            }
            logic.AllocatePoint(StatType.Str);
            int strBefore = logic.AllocatedStats.str;
            int availableBefore = logic.AvailablePoints;

            bool success = logic.RefundStatus(StatType.Str, -1);

            Assert.IsFalse(success, "負ポイント refund は失敗する");
            Assert.AreEqual(strBefore, logic.AllocatedStats.str, "状態は変更されない");
            Assert.AreEqual(availableBefore, logic.AvailablePoints);
        }

        [Test]
        public void LevelUpLogic_RefundStatus_WhenPointsExceedAllocated_ReturnsFalse()
        {
            LevelUpLogic logic = new LevelUpLogic(1);
            for (int i = 0; i < 5; i++)
            {
                logic.AddExp(logic.GetExpForNextLevel());
            }
            logic.AllocatePoint(StatType.Str);
            logic.AllocatePoint(StatType.Str);
            // Str = 2
            int strBefore = logic.AllocatedStats.str;
            int availableBefore = logic.AvailablePoints;

            bool success = logic.RefundStatus(StatType.Str, 3);

            Assert.IsFalse(success, "振り分け済みを超える refund は失敗");
            Assert.AreEqual(strBefore, logic.AllocatedStats.str,
                "失敗時は状態を変更しない");
            Assert.AreEqual(availableBefore, logic.AvailablePoints);
        }

        [Test]
        public void LevelUpLogic_RefundStatus_WhenZeroPoints_ReturnsTrueAndDoesNotChangeState()
        {
            LevelUpLogic logic = new LevelUpLogic(1);
            for (int i = 0; i < 5; i++)
            {
                logic.AddExp(logic.GetExpForNextLevel());
            }
            logic.AllocatePoint(StatType.Vit);
            int vitBefore = logic.AllocatedStats.vit;
            int availableBefore = logic.AvailablePoints;

            bool success = logic.RefundStatus(StatType.Vit, 0);

            Assert.IsTrue(success, "0 ポイント refund は no-op で成功");
            Assert.AreEqual(vitBefore, logic.AllocatedStats.vit);
            Assert.AreEqual(availableBefore, logic.AvailablePoints);
        }

        [Test]
        public void LevelUpLogic_RefundAllStatusPoints_AfterNoAllocation_RestoresToZeroBaseline()
        {
            // 1Lv でポイント未取得の状態で呼んでも安全
            LevelUpLogic logic = new LevelUpLogic(1);
            logic.RefundAllStatusPoints();

            Assert.AreEqual(0, logic.AllocatedStats.str);
            Assert.AreEqual(1 * LevelUpLogic.k_PointsPerLevel, logic.AvailablePoints,
                "level=1 なら 1 * k_PointsPerLevel = 3 に復元");
        }

        // ─────────────────────────────────────────────
        //  ステータス上限値 (cap) テスト
        // ─────────────────────────────────────────────

        [Test]
        public void LevelUpLogic_AllocatePoint_WhenCapReached_ReturnsFalse()
        {
            // statCaps.Str = 10 の想定で 10 回まで振れるが 11 回目は失敗する
            int[] caps = new int[] { 10, 99, 99, 99, 99, 99 };

            LevelUpLogic logic = new LevelUpLogic(1);
            // 十分なレベルで振れるポイントを確保 (Lv15 で 14*3 = 42 pts)
            for (int i = 0; i < 14; i++)
            {
                logic.AddExp(logic.GetExpForNextLevel());
            }

            for (int i = 0; i < 10; i++)
            {
                bool ok = logic.AllocatePoint(StatType.Str, caps);
                Assert.IsTrue(ok, $"{i + 1} 回目の Str 振り分けは成功するはず");
            }

            bool overflow = logic.AllocatePoint(StatType.Str, caps);
            Assert.IsFalse(overflow, "Str cap (=10) を超える 11 回目は失敗する");
            Assert.AreEqual(10, logic.AllocatedStats.str, "Str は cap の 10 に留まる");
        }

        [Test]
        public void LevelUpLogic_AllocatePoint_WhenCapReached_AvailablePointsPreserved()
        {
            int[] caps = new int[] { 1, 99, 99, 99, 99, 99 };
            LevelUpLogic logic = new LevelUpLogic(1);
            for (int i = 0; i < 2; i++)
            {
                logic.AddExp(logic.GetExpForNextLevel());
            }
            // 1 回振って cap 到達
            logic.AllocatePoint(StatType.Str, caps);
            int availableBefore = logic.AvailablePoints;

            bool success = logic.AllocatePoint(StatType.Str, caps);

            Assert.IsFalse(success);
            Assert.AreEqual(availableBefore, logic.AvailablePoints,
                "cap 到達で失敗した場合、振れるポイントは消費されない");
        }

        [Test]
        public void LevelUpLogic_AllocatePoint_WithNullCaps_DoesNotEnforceCap()
        {
            // 後方互換: statCaps = null なら上限チェックしない
            LevelUpLogic logic = new LevelUpLogic(1);
            for (int i = 0; i < 10; i++)
            {
                logic.AddExp(logic.GetExpForNextLevel());
            }

            for (int i = 0; i < 20; i++)
            {
                bool ok = logic.AllocatePoint(StatType.Str, null);
                Assert.IsTrue(ok, "null caps なら cap チェックしない");
            }

            Assert.AreEqual(20, logic.AllocatedStats.str);
        }

        [Test]
        public void LevelUpLogic_GetEffectiveMaxLevel_WithTotalSixty_DividesByPointsPerLevel()
        {
            // 全 stat cap 合計 60 / pointsPerLevel=3 → 動的最大レベル 20
            int[] caps = new int[] { 10, 10, 10, 10, 10, 10 };

            int effective = LevelUpLogic.GetEffectiveMaxLevel(caps);

            int expectedDynamic = 60 / LevelUpLogic.k_PointsPerLevel; // 20
            int expected = System.Math.Min(expectedDynamic, LevelUpLogic.k_MaxLevel);
            Assert.AreEqual(expected, effective);
            Assert.AreEqual(20, effective, "sanity: pointsPerLevel=3 なら 60/3=20");
        }

        [Test]
        public void LevelUpLogic_GetEffectiveMaxLevel_ReturnsMinOfDynamicAndHardCap()
        {
            // デフォルト 99×6 = 594 / 3 = 198 だが、ハードキャップ k_MaxLevel=99 でクランプ
            int[] caps = new int[] { 99, 99, 99, 99, 99, 99 };

            int effective = LevelUpLogic.GetEffectiveMaxLevel(caps);

            Assert.AreEqual(LevelUpLogic.k_MaxLevel, effective,
                "dynamicMaxLevel (198) > k_MaxLevel (99) なら k_MaxLevel が返る");
        }

        [Test]
        public void LevelUpLogic_GetEffectiveMaxLevel_WhenDynamicBelowHardCap_ReturnsDynamic()
        {
            // 合計 30 / 3 = 10 (< 99)
            int[] caps = new int[] { 5, 5, 5, 5, 5, 5 };

            int effective = LevelUpLogic.GetEffectiveMaxLevel(caps);

            Assert.AreEqual(10, effective);
        }

        [Test]
        public void LevelUpLogic_GetEffectiveMaxLevel_WithNullCaps_ReturnsHardCap()
        {
            int effective = LevelUpLogic.GetEffectiveMaxLevel(null);
            Assert.AreEqual(LevelUpLogic.k_MaxLevel, effective,
                "caps=null は上限未設定扱いでハードキャップを返す");
        }

        [Test]
        public void LevelUpLogic_GetEffectiveMaxLevel_WithShortArray_ReturnsHardCap()
        {
            // 6 要素未満は不正扱い
            int[] caps = new int[] { 10, 10, 10 };

            int effective = LevelUpLogic.GetEffectiveMaxLevel(caps);

            Assert.AreEqual(LevelUpLogic.k_MaxLevel, effective);
        }

        [Test]
        public void LevelUpLogic_GetEffectiveMaxLevel_TreatsNegativeCapsAsZero()
        {
            // 不正値に対する防御: 負値は 0 として扱う
            int[] caps = new int[] { -5, 10, 10, 10, 10, 10 };

            int effective = LevelUpLogic.GetEffectiveMaxLevel(caps);

            // 0 + 50 = 50 / 3 = 16
            Assert.AreEqual(50 / LevelUpLogic.k_PointsPerLevel, effective);
        }

        // ─────────────────────────────────────────────
        //  B3 LevelUpConfig SO との統合 (R1 対応)
        //  GetEffectiveMaxLevel が SO 経路の maxLevel を読み取ることを検証。
        // ─────────────────────────────────────────────

        [TearDown]
        public void ResetLevelUpConfig()
        {
            // 他テストへ影響しないよう SO デフォルトをクリア
            LevelUpLogic.SetDefaultConfig(null);
        }

        [Test]
        public void LevelUpLogic_GetEffectiveMaxLevel_RespectsLevelUpConfigHardCap()
        {
            // LevelUpConfig SO で maxLevel=50 を設定 → ハードキャップ 50 として動作
            LevelUpConfig config = ScriptableObject.CreateInstance<LevelUpConfig>();
            config.maxLevel = 50;
            LevelUpLogic.SetDefaultConfig(config);

            // 動的最大 = 594 / 3 = 198 だが SO のハードキャップ 50 でクランプ
            int[] caps = new int[] { 99, 99, 99, 99, 99, 99 };
            int effective = LevelUpLogic.GetEffectiveMaxLevel(caps);

            Assert.AreEqual(50, effective,
                "GetEffectiveMaxLevel は LevelUpConfig.maxLevel を読んでハードキャップ適用する");

            UnityEngine.Object.DestroyImmediate(config);
        }

        [Test]
        public void LevelUpLogic_GetEffectiveMaxLevel_FallsBackToDefaultWhenConfigNull()
        {
            LevelUpLogic.SetDefaultConfig(null);

            int effective = LevelUpLogic.GetEffectiveMaxLevel(null);
            Assert.AreEqual(LevelUpLogic.k_MaxLevel, effective,
                "SO 未設定時は k_DefaultMaxLevel (= k_MaxLevel) フォールバック");
        }

        // ─────────────────────────────────────────────
        //  Refund × Cap 交互シナリオ
        // ─────────────────────────────────────────────

        [Test]
        public void LevelUpLogic_AfterRefund_CanReallocateWithinCap()
        {
            int[] caps = new int[] { 3, 99, 99, 99, 99, 99 };
            LevelUpLogic logic = new LevelUpLogic(1);
            for (int i = 0; i < 5; i++)
            {
                logic.AddExp(logic.GetExpForNextLevel());
            }

            // Str cap (=3) まで振る
            for (int i = 0; i < 3; i++)
            {
                logic.AllocatePoint(StatType.Str, caps);
            }
            Assert.IsFalse(logic.AllocatePoint(StatType.Str, caps),
                "cap 到達で失敗");

            // Refund で 0 に戻す
            logic.RefundAllStatusPoints();

            // 再度振れる
            bool ok = logic.AllocatePoint(StatType.Str, caps);
            Assert.IsTrue(ok, "Refund 後は再び Str に振れる");
            Assert.AreEqual(1, logic.AllocatedStats.str);
        }
    }
}
