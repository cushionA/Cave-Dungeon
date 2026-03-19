using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class SummonSystem_PartyLimitTests
    {
        [Test]
        public void SummonManager_GetOldestSummonHash_ReturnsCorrect()
        {
            SummonManager manager = new SummonManager();
            manager.AddSummon(100, 30f, SummonType.Combat);
            manager.AddSummon(200, 10f, SummonType.Combat);

            int oldest = manager.GetOldestSummonHash();
            Assert.AreEqual(200, oldest); // 残り10秒が最も短い
        }

        [Test]
        public void SummonMagicBridge_TrySummonWithReplace_ReplacesOldest()
        {
            SummonManager manager = new SummonManager();
            SummonMagicBridge bridge = new SummonMagicBridge(manager);

            bridge.TrySummon(100, 30f, SummonType.Combat, 200);
            bridge.TrySummon(200, 10f, SummonType.Combat, 200);

            // 枠満杯 → 最古を置換
            bool result = bridge.TrySummonWithReplace(300, 25f, SummonType.Combat, 200);
            Assert.IsTrue(result);
            Assert.AreEqual(2, manager.ActiveCount);

            // hash=200（最古）が削除されたことを確認
            SummonSlot[] slots = manager.GetActiveSlots();
            bool has200 = false;
            bool has300 = false;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].summonHash == 200) { has200 = true; }
                if (slots[i].summonHash == 300) { has300 = true; }
            }
            Assert.IsFalse(has200);
            Assert.IsTrue(has300);
        }

        [Test]
        public void SummonMagicBridge_TrySummonWithReplace_EmptySlot_NoReplace()
        {
            SummonManager manager = new SummonManager();
            SummonMagicBridge bridge = new SummonMagicBridge(manager);

            // 枠あり → 通常追加
            bool result = bridge.TrySummonWithReplace(100, 30f, SummonType.Combat, 200);
            Assert.IsTrue(result);
            Assert.AreEqual(1, manager.ActiveCount);
        }
    }
}
