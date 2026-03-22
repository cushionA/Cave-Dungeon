using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    [TestFixture]
    public class HitBoxTests
    {
        // --- maxHitCount制御 ---

        [Test]
        public void HitboxLogic_MaxHitCount1_StopsAfterOneHit()
        {
            HitboxLogic logic = new HitboxLogic(1);

            bool first = logic.TryRegisterHit(100);
            bool second = logic.TryRegisterHit(200);

            Assert.IsTrue(first, "最初のヒットは成功するべき");
            Assert.IsFalse(second, "maxHitCount=1で2ヒット目は失敗するべき");
            Assert.IsTrue(logic.IsExhausted);
        }

        [Test]
        public void HitboxLogic_MaxHitCount3_AllowsThreeHits()
        {
            HitboxLogic logic = new HitboxLogic(3);

            Assert.IsTrue(logic.TryRegisterHit(100));
            Assert.IsTrue(logic.TryRegisterHit(200));
            Assert.IsTrue(logic.TryRegisterHit(300));
            Assert.IsFalse(logic.TryRegisterHit(400));
            Assert.AreEqual(3, logic.HitCount);
        }

        // --- 同一ターゲット重複防止 ---

        [Test]
        public void HitboxLogic_SameTarget_BlocksDuplicate()
        {
            HitboxLogic logic = new HitboxLogic(5);

            bool first = logic.TryRegisterHit(100);
            bool duplicate = logic.TryRegisterHit(100);

            Assert.IsTrue(first);
            Assert.IsFalse(duplicate, "同一ターゲットは重複ヒットしない");
            Assert.AreEqual(1, logic.HitCount);
        }

        // --- Reset後の再攻撃 ---

        [Test]
        public void HitboxLogic_AfterReset_AllowsReHit()
        {
            HitboxLogic logic = new HitboxLogic(1);

            Assert.IsTrue(logic.TryRegisterHit(100));
            Assert.IsTrue(logic.IsExhausted);

            logic.Reset(2);

            Assert.IsFalse(logic.IsExhausted);
            Assert.AreEqual(0, logic.HitCount);
            Assert.IsTrue(logic.TryRegisterHit(100), "Reset後は同一ターゲットに再ヒット可能");
            Assert.IsTrue(logic.TryRegisterHit(200));
            Assert.IsFalse(logic.TryRegisterHit(300), "新maxHitCount=2で3ヒット目は失敗");
        }

        // --- 0ヒット上限 ---

        [Test]
        public void HitboxLogic_ZeroMaxHitCount_AlwaysExhausted()
        {
            HitboxLogic logic = new HitboxLogic(0);

            Assert.IsTrue(logic.IsExhausted);
            Assert.IsFalse(logic.TryRegisterHit(100));
        }

        // --- HitCount追跡 ---

        [Test]
        public void HitboxLogic_HitCount_TracksCorrectly()
        {
            HitboxLogic logic = new HitboxLogic(10);

            logic.TryRegisterHit(1);
            logic.TryRegisterHit(2);
            logic.TryRegisterHit(3);

            Assert.AreEqual(3, logic.HitCount);
        }
    }
}
