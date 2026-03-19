using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class EnemySystem_DropTableTests
    {
        [Test]
        public void DropTable_Evaluate_ReturnsExp()
        {
            DropTableData table = new DropTableData { expReward = 50, currencyMin = 10, currencyMax = 20 };
            DropTableEvaluator.DropResult result = DropTableEvaluator.Evaluate(table, new float[] { 0.5f });
            Assert.AreEqual(50, result.exp);
            Assert.GreaterOrEqual(result.currency, 10);
            Assert.LessOrEqual(result.currency, 20);
        }

        [Test]
        public void DropTable_GuaranteedDrop_AlwaysDrops()
        {
            DropTableData table = new DropTableData
            {
                entries = new DropEntry[] { new DropEntry { itemId = 1, dropRate = 1.0f, minCount = 1, maxCount = 1 } }
            };
            DropTableEvaluator.DropResult result = DropTableEvaluator.Evaluate(table, new float[] { 0.5f, 0.5f });
            Assert.AreEqual(1, result.droppedItemCount);
            Assert.AreEqual(1, result.droppedItemIds[0]);
        }

        [Test]
        public void DropTable_EmptyTable_NoDrops()
        {
            DropTableData table = new DropTableData { expReward = 10 };
            DropTableEvaluator.DropResult result = DropTableEvaluator.Evaluate(table, new float[] { 0.5f });
            Assert.AreEqual(0, result.droppedItemCount);
        }

        [Test]
        public void DropTable_LowRate_MayNotDrop()
        {
            DropTableData table = new DropTableData
            {
                entries = new DropEntry[] { new DropEntry { itemId = 1, dropRate = 0.1f, minCount = 1, maxCount = 1 } }
            };
            DropTableEvaluator.DropResult result = DropTableEvaluator.Evaluate(table, new float[] { 0.5f, 0.9f });
            Assert.AreEqual(0, result.droppedItemCount);
        }
    }
}
