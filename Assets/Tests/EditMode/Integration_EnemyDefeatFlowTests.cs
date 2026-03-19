using NUnit.Framework;
using Game.Core;
using UnityEngine;

namespace Game.Tests.EditMode
{
    [TestFixture]
    public class Integration_EnemyDefeatFlowTests
    {
        private SoACharaDataDic _data;
        private GameEvents _events;

        private const int k_EnemyHash = 5000;
        private const int k_PlayerHash = 1;

        [SetUp]
        public void SetUp()
        {
            _data = new SoACharaDataDic(4);
            _events = new GameEvents();

            // 敵: HP=1（撃破直前）
            CharacterVitals enemyVitals = new CharacterVitals
            {
                currentHp = 1,
                maxHp = 100,
                position = new Vector2(10f, 0f),
                level = 3,
                hpRatio = 1
            };
            CharacterFlags enemyFlags = CharacterFlags.Pack(
                CharacterBelong.Enemy, CharacterFeature.Minion, AbilityFlag.None);
            _data.Add(k_EnemyHash, enemyVitals, default, enemyFlags, default);

            // プレイヤー
            CharacterVitals playerVitals = new CharacterVitals
            {
                currentHp = 200,
                maxHp = 200,
                position = Vector2.zero,
                level = 10
            };
            CharacterFlags playerFlags = CharacterFlags.Pack(
                CharacterBelong.Ally, CharacterFeature.Player, AbilityFlag.None);
            _data.Add(k_PlayerHash, playerVitals, default, playerFlags, default);
        }

        [TearDown]
        public void TearDown()
        {
            _events.Dispose();
            _data.Dispose();
        }

        [Test]
        public void EnemyDefeatFlow_HpZero_DeactivatesAndDropsLoot()
        {
            // Arrange
            EnemyController controller = new EnemyController(k_EnemyHash, _data);
            AIMode defaultMode = new AIMode
            {
                modeName = "Default",
                actionRules = new AIRule[0],
                targetRules = new AIRule[0],
                targetSelects = new AITargetSelect[0],
                actions = new ActionSlot[0],
                defaultActionIndex = -1,
                judgeInterval = new Vector2(1f, 2f)
            };
            controller.SetAIModes(new AIMode[] { defaultMode }, new ModeTransitionRule[0]);

            DropTableData dropTable = new DropTableData
            {
                expReward = 50,
                currencyMin = 10,
                currencyMax = 20,
                entries = new DropEntry[]
                {
                    new DropEntry { itemId = 101, dropRate = 1.0f, minCount = 1, maxCount = 1 }
                }
            };

            // Act: HPを0にする
            ref CharacterVitals enemyVitals = ref _data.GetVitals(k_EnemyHash);
            HpArmorLogic.ApplyDamage(ref enemyVitals.currentHp, ref enemyVitals.currentArmor, 10, 0f);
            Assert.AreEqual(0, _data.GetVitals(k_EnemyHash).currentHp);

            // コントローラー非活性化
            controller.Deactivate();
            Assert.IsFalse(controller.IsActive);

            // ドロップ評価（確定ドロップ: randomValue=0.5）
            float[] randomValues = { 0.5f, 0.5f };
            DropTableEvaluator.DropResult dropResult = DropTableEvaluator.Evaluate(dropTable, randomValues);

            // Assert
            Assert.AreEqual(50, dropResult.exp);
            Assert.Greater(dropResult.currency, 0);
            Assert.AreEqual(1, dropResult.droppedItemCount);
            Assert.AreEqual(101, dropResult.droppedItemIds[0]);
        }

        [Test]
        public void EnemyDefeatFlow_DropTableEvaluation_DeterministicResults()
        {
            // Arrange: 2エントリ（dropRate=0.5 と dropRate=0.3）
            DropTableData dropTable = new DropTableData
            {
                expReward = 30,
                currencyMin = 5,
                currencyMax = 15,
                entries = new DropEntry[]
                {
                    new DropEntry { itemId = 201, dropRate = 0.5f, minCount = 1, maxCount = 1 },
                    new DropEntry { itemId = 202, dropRate = 0.3f, minCount = 1, maxCount = 1 }
                }
            };

            // randomValues[0]=通貨用, randomValues[1]=entry[0]判定, randomValues[2]=entry[1]判定
            // entry[0]: rv=0.1 < 0.5 → ドロップ, entry[1]: rv=0.9 >= 0.3 → 非ドロップ
            float[] randomValues = { 0.5f, 0.1f, 0.9f };
            DropTableEvaluator.DropResult result = DropTableEvaluator.Evaluate(dropTable, randomValues);

            // Assert: entry[0]のみドロップ
            Assert.AreEqual(1, result.droppedItemCount);
            Assert.AreEqual(201, result.droppedItemIds[0]);
            Assert.AreEqual(30, result.exp);
        }

        [Test]
        public void EnemyDefeatFlow_LootDistribution_FiresAllEvents()
        {
            // Arrange
            LootRewardDistributor distributor = new LootRewardDistributor();

            bool expFired = false;
            bool currencyFired = false;
            bool itemFired = false;
            int receivedExp = 0;
            int receivedCurrency = 0;
            int receivedItemId = 0;

            distributor.OnExpRewarded += (exp) => { expFired = true; receivedExp = exp; };
            distributor.OnCurrencyRewarded += (currency) => { currencyFired = true; receivedCurrency = currency; };
            distributor.OnItemDropped += (itemId, count) => { itemFired = true; receivedItemId = itemId; };

            // Act
            DropTableEvaluator.DropResult dropResult = new DropTableEvaluator.DropResult
            {
                exp = 50,
                currency = 15,
                droppedItemIds = new int[] { 101 },
                droppedItemCounts = new int[] { 1 },
                droppedItemCount = 1
            };
            distributor.Distribute(dropResult);

            // Assert
            Assert.IsTrue(expFired);
            Assert.AreEqual(50, receivedExp);
            Assert.IsTrue(currencyFired);
            Assert.AreEqual(15, receivedCurrency);
            Assert.IsTrue(itemFired);
            Assert.AreEqual(101, receivedItemId);
        }

        [Test]
        public void EnemyDefeatFlow_PoolReturnOnDefeat_ReturnsId()
        {
            // Arrange
            EnemyPool pool = new EnemyPool(4);
            int id1 = pool.Get();
            int id2 = pool.Get();
            int initialActive = pool.ActiveCount;

            // Act: 1体を撃破してプールに返却
            pool.Return(id1);

            // Assert
            Assert.AreEqual(initialActive - 1, pool.ActiveCount);
        }
    }
}
