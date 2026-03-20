using System.Collections.Generic;
using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// セーブシステム結合テスト。
    /// 複数ISaveableの一括保存/復元、スロット上書き、FlagManager連携を検証。
    /// </summary>
    public class Integration_SaveSystemTests
    {
        private SaveManager _saveManager;

        [SetUp]
        public void SetUp()
        {
            _saveManager = new SaveManager();
        }

        [Test]
        public void MultipleISaveables_SaveAndLoad_AllRestored()
        {
            CurrencyManager currency = new CurrencyManager(1000);
            GateRegistry gates = new GateRegistry();
            gates.Register("gate_a", false);
            gates.Open("gate_a");
            BacktrackRewardManager backtrack = new BacktrackRewardManager();
            backtrack.MarkCollected("reward_1");

            _saveManager.Register(currency);
            _saveManager.Register(gates);
            _saveManager.Register(backtrack);

            _saveManager.Save(0);

            // 状態をリセット
            currency.TrySpend(1000);
            gates.Close("gate_a");
            // BacktrackRewardManagerにはクリアメソッドがないのでnew
            BacktrackRewardManager backtrack2 = new BacktrackRewardManager();
            _saveManager.Unregister(backtrack);
            _saveManager.Register(backtrack2);

            _saveManager.Load(0);

            Assert.AreEqual(1000, currency.Balance);
            Assert.IsTrue(gates.IsOpen("gate_a"));
            Assert.IsTrue(backtrack2.IsCollected("reward_1"));
        }

        [Test]
        public void SlotOverwrite_PreviousDataNotLeaked()
        {
            CurrencyManager currency = new CurrencyManager(500);
            _saveManager.Register(currency);

            // スロット0に保存
            _saveManager.Save(0);

            // 残高変更後にスロット0を上書き
            currency.Add(300);
            _saveManager.Save(0);

            // ロードすると上書き後の値
            currency.TrySpend(800);
            _saveManager.Load(0);

            Assert.AreEqual(800, currency.Balance);
        }

        [Test]
        public void FlagManager_WithOtherISaveables_SimultaneousSaveLoad()
        {
            CurrencyManager currency = new CurrencyManager(250);
            FlagManager flags = new FlagManager();
            flags.SetGlobalFlag("story_complete", true);
            flags.SwitchMap("map_1");
            flags.SetLocalFlag("chest_opened", true);

            _saveManager.Register(currency);
            _saveManager.Register(flags);

            _saveManager.Save(1);

            // リセット
            currency.TrySpend(250);
            FlagManager flags2 = new FlagManager();
            _saveManager.Unregister(flags);
            _saveManager.Register(flags2);

            _saveManager.Load(1);

            Assert.AreEqual(250, currency.Balance);
            Assert.IsTrue(flags2.GetGlobalFlag("story_complete"));
            flags2.SwitchMap("map_1");
            Assert.IsTrue(flags2.GetLocalFlag("chest_opened"));
        }

        [Test]
        public void MultipleSlots_IndependentData()
        {
            CurrencyManager currency = new CurrencyManager(100);
            _saveManager.Register(currency);

            _saveManager.Save(0);

            currency.Add(400);
            _saveManager.Save(1);

            // スロット0をロード → 100
            _saveManager.Load(0);
            Assert.AreEqual(100, currency.Balance);

            // スロット1をロード → 500
            _saveManager.Load(1);
            Assert.AreEqual(500, currency.Balance);
        }

        [Test]
        public void UnregisterAndReregister_DoesNotCorruptState()
        {
            CurrencyManager currency = new CurrencyManager(300);
            _saveManager.Register(currency);
            _saveManager.Save(0);

            _saveManager.Unregister(currency);

            // 同じSaveIdで再登録可能
            CurrencyManager currency2 = new CurrencyManager(0);
            _saveManager.Register(currency2);
            _saveManager.Load(0);

            Assert.AreEqual(300, currency2.Balance);
        }

        [Test]
        public void LevelUpLogic_WithCurrency_IntegrationSaveLoad()
        {
            CurrencyManager currency = new CurrencyManager(999);
            LevelUpLogic levelUp = new LevelUpLogic(1);
            levelUp.AddExp(250);
            levelUp.AllocatePoint(StatType.Str);

            _saveManager.Register(currency);
            _saveManager.Register(levelUp);

            _saveManager.Save(0);

            // リセット
            currency.TrySpend(999);
            LevelUpLogic levelUp2 = new LevelUpLogic();
            _saveManager.Unregister(levelUp);
            _saveManager.Register(levelUp2);

            _saveManager.Load(0);

            Assert.AreEqual(999, currency.Balance);
            Assert.AreEqual(2, levelUp2.Level);
            Assert.AreEqual(1, levelUp2.AllocatedStats.str);
        }

        [Test]
        public void InventoryManager_SaveLoad_Integration()
        {
            InventoryManager inventory = new InventoryManager();
            inventory.Add(1, ItemCategory.Consumable, 5, 10);
            inventory.Add(2, ItemCategory.Material, 3, 99);

            _saveManager.Register(inventory);
            _saveManager.Save(0);

            // リセット
            inventory.Remove(1, 5);
            inventory.Remove(2, 3);
            Assert.AreEqual(0, inventory.ItemCount);

            _saveManager.Load(0);

            Assert.AreEqual(2, inventory.ItemCount);
            Assert.AreEqual(5, inventory.GetCount(1));
            Assert.AreEqual(3, inventory.GetCount(2));
        }
    }
}
