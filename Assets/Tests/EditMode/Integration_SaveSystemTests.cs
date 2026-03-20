using System.Collections.Generic;
using System.IO;
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

        [Test]
        public void DiskRoundTrip_AllISaveables_RestoredAfterJsonSerialization()
        {
            // 一時ディレクトリを使用
            string tempDir = Path.Combine(Path.GetTempPath(), "SaveSystemTest_" + System.Guid.NewGuid().ToString("N"));

            try
            {
                // --- 準備: 各ISaveableにデータを設定 ---
                CurrencyManager currency = new CurrencyManager(1234);
                InventoryManager inventory = new InventoryManager();
                inventory.Add(10, ItemCategory.Consumable, 3, 10);
                inventory.Add(20, ItemCategory.Weapon, 1, 1);

                LevelUpLogic levelUp = new LevelUpLogic(1);
                levelUp.AddExp(250); // レベル2へ (exp 100必要, 残り150)
                levelUp.AllocatePoint(StatType.Vit);
                levelUp.AllocatePoint(StatType.Str);

                GateRegistry gates = new GateRegistry();
                gates.Register("gate_north", false);
                gates.Register("gate_south", true);
                gates.Open("gate_north");

                BacktrackRewardManager backtrack = new BacktrackRewardManager();
                backtrack.MarkCollected("chest_01");
                backtrack.MarkCollected("chest_02");

                FlagManager flags = new FlagManager();
                flags.SetGlobalFlag("boss_defeated", true);
                flags.SetGlobalFlag("intro_seen", true);
                flags.SwitchMap("dungeon_1");
                flags.SetLocalFlag("trap_triggered", true);

                // --- Save: メモリに保存 → ディスクへ書き出し ---
                _saveManager.Register(currency);
                _saveManager.Register(inventory);
                _saveManager.Register(levelUp);
                _saveManager.Register(gates);
                _saveManager.Register(backtrack);
                _saveManager.Register(flags);

                SaveSlotData savedSlot = _saveManager.Save(0);
                Assert.IsNotNull(savedSlot);

                SaveDataStore store = new SaveDataStore(tempDir);
                store.WriteToDisk(savedSlot);

                // --- Load: ディスクから読み込み → 新しいSaveManagerで復元 ---
                SaveSlotData loadedSlot = store.ReadFromDisk(0);
                Assert.IsNotNull(loadedSlot);

                // 新しいSaveManagerと新しいISaveableインスタンスを用意
                SaveManager saveManager2 = new SaveManager();
                CurrencyManager currency2 = new CurrencyManager(0);
                InventoryManager inventory2 = new InventoryManager();
                LevelUpLogic levelUp2 = new LevelUpLogic(1);
                GateRegistry gates2 = new GateRegistry();
                BacktrackRewardManager backtrack2 = new BacktrackRewardManager();
                FlagManager flags2 = new FlagManager();

                saveManager2.Register(currency2);
                saveManager2.Register(inventory2);
                saveManager2.Register(levelUp2);
                saveManager2.Register(gates2);
                saveManager2.Register(backtrack2);
                saveManager2.Register(flags2);

                saveManager2.SetSlotData(0, loadedSlot);
                bool loadResult = saveManager2.Load(0);
                Assert.IsTrue(loadResult);

                // --- 検証: 全ISaveableの値が正しく復元されていること ---

                // CurrencyManager
                Assert.AreEqual(1234, currency2.Balance, "CurrencyManager: 残高が復元されていない");

                // InventoryManager
                Assert.AreEqual(2, inventory2.ItemCount, "InventoryManager: アイテム数が復元されていない");
                Assert.AreEqual(3, inventory2.GetCount(10), "InventoryManager: アイテム10の数量が正しくない");
                Assert.AreEqual(1, inventory2.GetCount(20), "InventoryManager: アイテム20の数量が正しくない");

                // LevelUpLogic
                Assert.AreEqual(2, levelUp2.Level, "LevelUpLogic: レベルが復元されていない");
                Assert.AreEqual(150, levelUp2.CurrentExp, "LevelUpLogic: 経験値が復元されていない");
                Assert.AreEqual(1, levelUp2.AllocatedStats.vit, "LevelUpLogic: VIT割り振りが復元されていない");
                Assert.AreEqual(1, levelUp2.AllocatedStats.str, "LevelUpLogic: STR割り振りが復元されていない");

                // GateRegistry
                Assert.IsTrue(gates2.IsOpen("gate_north"), "GateRegistry: gate_northが開いていない");
                Assert.IsTrue(gates2.IsOpen("gate_south"), "GateRegistry: gate_southが開いていない");

                // BacktrackRewardManager
                Assert.IsTrue(backtrack2.IsCollected("chest_01"), "BacktrackRewardManager: chest_01が回収済みでない");
                Assert.IsTrue(backtrack2.IsCollected("chest_02"), "BacktrackRewardManager: chest_02が回収済みでない");

                // FlagManager
                Assert.IsTrue(flags2.GetGlobalFlag("boss_defeated"), "FlagManager: boss_defeatedフラグが復元されていない");
                Assert.IsTrue(flags2.GetGlobalFlag("intro_seen"), "FlagManager: intro_seenフラグが復元されていない");
                flags2.SwitchMap("dungeon_1");
                Assert.IsTrue(flags2.GetLocalFlag("trap_triggered"), "FlagManager: trap_triggeredローカルフラグが復元されていない");
            }
            finally
            {
                // テスト後に一時ディレクトリを削除
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }
    }
}
