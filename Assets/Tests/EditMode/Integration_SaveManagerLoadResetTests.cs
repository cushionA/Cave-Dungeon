using System.Collections.Generic;
using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Issue #80 L2: SaveManager.Load 前 state 引きずり対策の結合テスト。
    ///
    /// 観点:
    /// 1. 既存ロジック呼び出し検証 — SaveManager.Load が、ロード対象スロットに entry が無い ISaveable に対して
    ///    Deserialize(null) を呼び出し、各実装が初期状態にリセットすることを保証する。
    /// 2. 状態シーケンス検証 — スロット A → スロット B 切替で、A の state が B にリークしないこと。
    /// 3. 境界値・不変条件 — 各 ISaveable 実装は Deserialize(null) を呼ばれてもエラーを投げず、
    ///    内部コレクションが空 / 数値がデフォルト値にリセットされること。
    /// </summary>
    public class Integration_SaveManagerLoadResetTests
    {
        // ===== SaveManager: Deserialize(null) ディスパッチ =====

        [Test]
        public void SaveManager_Load_WhenSaveableHasNoEntry_CallsDeserializeWithNull()
        {
            SaveManager manager = new SaveManager();
            ResetTrackingSaveable saveable = new ResetTrackingSaveable { SaveId = "tracker" };
            manager.Register(saveable);

            // 空のスロットを作成 (entries 内に "tracker" は存在しない)
            SaveSlotData emptySlot = new SaveSlotData(0);
            manager.SetSlotData(0, emptySlot);

            manager.Load(0);

            Assert.IsTrue(saveable.DeserializeCalled, "entry が無い ISaveable にも Deserialize が呼ばれること");
            Assert.IsNull(saveable.LastReceivedData, "entry が無いとき Deserialize に null が渡されること");
        }

        [Test]
        public void SaveManager_Load_AfterPriorLoad_ResetsMissingEntries()
        {
            // Slot 0 で値を持ち、Slot 1 では entry なし → Slot 0 → Slot 1 切替で reset されること
            SaveManager manager = new SaveManager();
            ResetTrackingSaveable saveable = new ResetTrackingSaveable { SaveId = "tracker" };
            manager.Register(saveable);

            SaveSlotData slot0 = new SaveSlotData(0);
            slot0.entries["tracker"] = "PERSISTED_VALUE";
            manager.SetSlotData(0, slot0);

            SaveSlotData slot1 = new SaveSlotData(1);
            manager.SetSlotData(1, slot1);

            manager.Load(0);
            Assert.AreEqual("PERSISTED_VALUE", saveable.LastReceivedData);

            manager.Load(1);
            Assert.IsNull(saveable.LastReceivedData,
                "別スロット読み込み時、entry が無い saveable は前 state を引きずらず Deserialize(null) で reset");
        }

        [Test]
        public void SaveManager_Load_WhenEntryExists_StillPassesActualData()
        {
            SaveManager manager = new SaveManager();
            ResetTrackingSaveable saveable = new ResetTrackingSaveable { SaveId = "tracker" };
            manager.Register(saveable);

            SaveSlotData slot = new SaveSlotData(0);
            slot.entries["tracker"] = "ACTUAL_DATA";
            manager.SetSlotData(0, slot);

            manager.Load(0);

            Assert.AreEqual("ACTUAL_DATA", saveable.LastReceivedData);
        }

        // ===== 各 ISaveable 実装の Deserialize(null) リセット契約 =====

        [Test]
        public void CurrencyManager_DeserializeNull_ResetsBalanceToZero()
        {
            CurrencyManager currency = new CurrencyManager(500);
            currency.Add(200);
            ISaveable saveable = currency;

            saveable.Deserialize(null);

            Assert.AreEqual(0, currency.Balance);
        }

        [Test]
        public void FlagManager_DeserializeNull_ClearsAllFlags()
        {
            FlagManager flags = new FlagManager();
            flags.SetGlobalFlag("story_chapter1_done", true);
            flags.SwitchMap("dungeon_a");
            flags.SetLocalFlag("chest_opened", true);
            ISaveable saveable = flags;

            saveable.Deserialize(null);

            Assert.IsFalse(flags.GetGlobalFlag("story_chapter1_done"));
            Assert.IsNull(flags.CurrentMapId, "CurrentMapId も reset されること");
            Assert.IsFalse(flags.GetLocalFlag("chest_opened"));
        }

        [Test]
        public void GateRegistry_DeserializeNull_ClearsAllGates()
        {
            GateRegistry gates = new GateRegistry();
            gates.Register("gate_a", true);
            gates.Register("gate_b", true);
            ISaveable saveable = gates;

            saveable.Deserialize(null);

            Assert.AreEqual(0, gates.Count, "Deserialize(null) で gate 状態が空になること");
        }

        [Test]
        public void BacktrackRewardManager_DeserializeNull_ClearsCollectedRewards()
        {
            BacktrackRewardManager rewards = new BacktrackRewardManager();
            rewards.MarkCollected("reward_a");
            rewards.MarkCollected("reward_b");
            ISaveable saveable = rewards;

            saveable.Deserialize(null);

            Assert.IsFalse(rewards.IsCollected("reward_a"));
            Assert.IsFalse(rewards.IsCollected("reward_b"));
        }

        [Test]
        public void LevelUpLogic_DeserializeNull_ResetsToInitialState()
        {
            LevelUpLogic logic = new LevelUpLogic(initialLevel: 5);
            logic.AddExp(1000);
            ISaveable saveable = logic;

            saveable.Deserialize(null);

            Assert.AreEqual(1, logic.Level, "level は 1 に reset");
            Assert.AreEqual(0, logic.CurrentExp, "currentExp は 0 に reset");
            Assert.AreEqual(0, logic.AvailablePoints, "availablePoints は 0 に reset");
            Assert.AreEqual(default(StatModifier), logic.AllocatedStats, "allocatedStats は default に reset");
        }

        // ===== 既に正しく動作する実装の回帰ガード =====

        [Test]
        public void InventoryManager_DeserializeNull_ClearsItems()
        {
            InventoryManager inventory = new InventoryManager();
            inventory.Add(1, ItemCategory.Consumable, 5, 10);
            inventory.Add(2, ItemCategory.Material, 3, 99);
            ISaveable saveable = inventory;

            saveable.Deserialize(null);

            Assert.AreEqual(0, inventory.ItemCount);
        }

        [Test]
        public void ChallengeManager_DeserializeNull_ClearsUnlocks()
        {
            ChallengeManager challenges = new ChallengeManager();
            challenges.UnlockChallenge("ch1");
            challenges.UnlockChallenge("ch2");
            ISaveable saveable = challenges;

            saveable.Deserialize(null);

            Assert.IsFalse(challenges.IsUnlocked("ch1"));
            Assert.IsFalse(challenges.IsUnlocked("ch2"));
        }

        [Test]
        public void LeaderboardManager_DeserializeNull_ClearsRecords()
        {
            LeaderboardManager board = new LeaderboardManager();
            ChallengeResult result = new ChallengeResult
            {
                challengeId = "ch1",
                state = ChallengeState.Completed,
                score = 100,
                clearTime = 60f,
                rank = ChallengeRank.S,
            };
            board.UpdateRecord(result);
            ISaveable saveable = board;

            saveable.Deserialize(null);

            LeaderboardEntry[] all = board.GetAllRecords();
            Assert.AreEqual(0, all.Length);
        }

        [Test]
        public void AITemplateManager_DeserializeNull_ClearsTemplates()
        {
            AITemplateManager templates = new AITemplateManager();
            AITemplateData template = new AITemplateData
            {
                templateId = "t1",
                templateName = "Aggressive",
            };
            templates.SaveTemplate(template);
            ISaveable saveable = templates;

            saveable.Deserialize(null);

            Assert.IsNull(templates.GetTemplate("t1"));
        }

        // ===== 不変条件: Deserialize(null) は例外を投げない =====

        [Test]
        public void AllSaveables_DeserializeNull_DoesNotThrow()
        {
            ISaveable[] all = new ISaveable[]
            {
                new CurrencyManager(),
                new FlagManager(),
                new GateRegistry(),
                new BacktrackRewardManager(),
                new LevelUpLogic(),
                new InventoryManager(),
                new ChallengeManager(),
                new LeaderboardManager(),
                new AITemplateManager(),
            };

            for (int i = 0; i < all.Length; i++)
            {
                ISaveable target = all[i];
                Assert.DoesNotThrow(() => target.Deserialize(null),
                    $"{target.GetType().Name}.Deserialize(null) が例外を投げないこと");
            }
        }

        // ===== test helpers =====

        private class ResetTrackingSaveable : ISaveable
        {
            public string SaveId { get; set; }
            public bool DeserializeCalled { get; private set; }
            public object LastReceivedData { get; private set; }

            public object Serialize() => LastReceivedData;

            public void Deserialize(object data)
            {
                DeserializeCalled = true;
                LastReceivedData = data;
            }
        }
    }
}
