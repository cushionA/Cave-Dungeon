using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// SaveDataStore の .bak バックアップ + 破損検知フォールバックの結合テスト。
    /// ファイルIOを直接叩くので一時ディレクトリを使う。
    /// </summary>
    public class SaveDataStoreBackupTests
    {
        private string _testDir;
        private SaveDataStore _store;

        [SetUp]
        public void SetUp()
        {
            _testDir = Path.Combine(Application.temporaryCachePath, "SaveDataStoreBackupTest_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
            _store = new SaveDataStore(_testDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }

        // ---------- 補助 ----------

        private string GetSlotPath(int slotIndex)
        {
            return Path.Combine(_testDir, "saves", $"slot_{slotIndex}.json");
        }

        private string GetBackupPath(int slotIndex)
        {
            return GetSlotPath(slotIndex) + SaveDataStore.k_BackupExtension;
        }

        // ---------- 正常系 ----------

        [Test]
        public void SaveDataStore_WriteThenRead_WhenHealthy_ReturnsSameData()
        {
            SaveSlotData original = new SaveSlotData(0);
            original.entries["hp"] = 123;
            original.entries["name"] = "hero";

            _store.WriteToDisk(original);
            SaveSlotData restored = _store.ReadFromDisk(0);

            Assert.IsNotNull(restored);
            Assert.AreEqual(0, restored.slotIndex);
            Assert.AreEqual(123, System.Convert.ToInt32(restored.entries["hp"]));
            Assert.AreEqual("hero", restored.entries["name"].ToString());
        }

        [Test]
        public void SaveDataStore_WriteToDisk_FirstTime_DoesNotCreateBackup()
        {
            SaveSlotData data = new SaveSlotData(0);
            data.entries["val"] = 1;

            _store.WriteToDisk(data);

            // 初回書き込みでは .bak は作られない (前世代が存在しないため)
            Assert.IsTrue(File.Exists(GetSlotPath(0)));
            Assert.IsFalse(File.Exists(GetBackupPath(0)), "初回保存では .bak は作られないはず");
        }

        [Test]
        public void SaveDataStore_WriteToDisk_SecondTime_CreatesBackupFromPrevious()
        {
            SaveSlotData first = new SaveSlotData(0);
            first.entries["val"] = 100;
            _store.WriteToDisk(first);

            string originalText = File.ReadAllText(GetSlotPath(0));

            SaveSlotData second = new SaveSlotData(0);
            second.entries["val"] = 999;
            _store.WriteToDisk(second);

            // 2回目の書き込み後、.bak に1回目の内容が入っているはず
            Assert.IsTrue(File.Exists(GetBackupPath(0)), ".bak が作成されているべき");
            string backupText = File.ReadAllText(GetBackupPath(0));
            Assert.AreEqual(originalText, backupText, ".bak は前世代のメインファイルと一致するはず");

            // メインは上書き後の値
            SaveSlotData restored = _store.ReadFromDisk(0);
            Assert.AreEqual(999, System.Convert.ToInt32(restored.entries["val"]));
        }

        // ---------- 破損時フォールバック ----------

        [Test]
        public void SaveDataStore_ReadFromDisk_WhenMainCorrupt_FallsBackToBackup()
        {
            // 1) 正常保存
            SaveSlotData first = new SaveSlotData(0);
            first.entries["val"] = 42;
            _store.WriteToDisk(first);

            // 2) もう一度保存して .bak を生成 (.bak は1回目の内容)
            SaveSlotData second = new SaveSlotData(0);
            second.entries["val"] = 99;
            _store.WriteToDisk(second);

            // 3) メインファイルを破損させる
            File.WriteAllText(GetSlotPath(0), "{ this is not valid json !!!");

            // メイン破損のLogErrorとフォールバック成功のLogErrorを予期
            LogAssert.Expect(LogType.Error,
                new Regex(@"\[SaveDataStore\].+読み込みに失敗しました"));
            LogAssert.Expect(LogType.Error,
                new Regex(@"\[SaveDataStore\].+\.bak からフォールバック復元しました"));

            // 4) 読み込むと .bak (1回目の値=42) が復元される
            SaveSlotData restored = _store.ReadFromDisk(0);

            Assert.IsNotNull(restored, "メイン破損時は .bak からフォールバックするはず");
            Assert.AreEqual(42, System.Convert.ToInt32(restored.entries["val"]),
                ".bak (前世代) の値が復元されるはず");
        }

        [Test]
        public void SaveDataStore_ReadFromDisk_WhenMainAndBackupCorrupt_ReturnsNullAndLogsError()
        {
            // 両方破損させるため事前に2回保存して両ファイル存在させる
            SaveSlotData first = new SaveSlotData(0);
            first.entries["val"] = 1;
            _store.WriteToDisk(first);

            SaveSlotData second = new SaveSlotData(0);
            second.entries["val"] = 2;
            _store.WriteToDisk(second);

            // メインも .bak も壊す
            File.WriteAllText(GetSlotPath(0), "not json");
            File.WriteAllText(GetBackupPath(0), "also not json");

            LogAssert.Expect(LogType.Error,
                new Regex(@"\[SaveDataStore\].+読み込みに失敗しました"));
            LogAssert.Expect(LogType.Error,
                new Regex(@"\[SaveDataStore\].+\.bak も読み込みに失敗しました"));

            SaveSlotData restored = _store.ReadFromDisk(0);
            Assert.IsNull(restored, "両方壊れていれば null を返すはず");
        }

        [Test]
        public void SaveDataStore_ReadFromDisk_WhenMissingRequiredField_FallsBackToBackup()
        {
            // 正常な2回保存を行う
            SaveSlotData first = new SaveSlotData(0);
            first.entries["val"] = 7;
            _store.WriteToDisk(first);

            SaveSlotData second = new SaveSlotData(0);
            second.entries["val"] = 8;
            _store.WriteToDisk(second);

            // メインを「パースは通るが entries が null」な JSON に差し替える
            File.WriteAllText(GetSlotPath(0), "{\"version\":1,\"slotIndex\":0,\"timestamp\":\"x\",\"entries\":null}");

            LogAssert.Expect(LogType.Error,
                new Regex(@"\[SaveDataStore\].+読み込みに失敗しました"));
            LogAssert.Expect(LogType.Error,
                new Regex(@"\[SaveDataStore\].+\.bak からフォールバック復元しました"));

            SaveSlotData restored = _store.ReadFromDisk(0);

            Assert.IsNotNull(restored, "必須フィールド欠落でも .bak からフォールバックするはず");
            Assert.AreEqual(7, System.Convert.ToInt32(restored.entries["val"]));
        }

        [Test]
        public void SaveDataStore_ReadFromDisk_WhenMainEmptyFile_FallsBackToBackup()
        {
            SaveSlotData first = new SaveSlotData(0);
            first.entries["val"] = 11;
            _store.WriteToDisk(first);

            SaveSlotData second = new SaveSlotData(0);
            second.entries["val"] = 22;
            _store.WriteToDisk(second);

            // 空ファイル (0バイト) に差し替える
            File.WriteAllText(GetSlotPath(0), "");

            LogAssert.Expect(LogType.Error,
                new Regex(@"\[SaveDataStore\].+読み込みに失敗しました"));
            LogAssert.Expect(LogType.Error,
                new Regex(@"\[SaveDataStore\].+\.bak からフォールバック復元しました"));

            SaveSlotData restored = _store.ReadFromDisk(0);

            Assert.IsNotNull(restored);
            Assert.AreEqual(11, System.Convert.ToInt32(restored.entries["val"]));
        }

        // ---------- 境界条件 ----------

        [Test]
        public void SaveDataStore_ReadFromDisk_WhenOnlyBackupExists_LoadsFromBackup()
        {
            // メインがなく .bak だけある状態を人工的に作る
            // まず正常に1回保存し、その後メインだけ削除する形で再現する
            SaveSlotData first = new SaveSlotData(0);
            first.entries["val"] = 55;
            _store.WriteToDisk(first);

            SaveSlotData second = new SaveSlotData(0);
            second.entries["val"] = 66;
            _store.WriteToDisk(second);

            // メインを削除 → .bak (val=55) だけ残る
            File.Delete(GetSlotPath(0));
            Assert.IsFalse(File.Exists(GetSlotPath(0)));
            Assert.IsTrue(File.Exists(GetBackupPath(0)));

            LogAssert.Expect(LogType.Error,
                new Regex(@"\[SaveDataStore\].+\.bak からフォールバック復元しました"));

            SaveSlotData restored = _store.ReadFromDisk(0);
            Assert.IsNotNull(restored);
            Assert.AreEqual(55, System.Convert.ToInt32(restored.entries["val"]));
        }

        [Test]
        public void SaveDataStore_ReadFromDisk_NoMainNoBackup_ReturnsNullSilently()
        {
            // 何も保存されていない場合はエラーではなく単に null を返す (既存動作)
            SaveSlotData restored = _store.ReadFromDisk(0);
            Assert.IsNull(restored);
        }

        [Test]
        public void SaveDataStore_DeleteSlot_RemovesBothMainAndBackup()
        {
            SaveSlotData first = new SaveSlotData(0);
            first.entries["val"] = 1;
            _store.WriteToDisk(first);

            SaveSlotData second = new SaveSlotData(0);
            second.entries["val"] = 2;
            _store.WriteToDisk(second);

            Assert.IsTrue(File.Exists(GetSlotPath(0)));
            Assert.IsTrue(File.Exists(GetBackupPath(0)));

            _store.DeleteSlot(0);

            Assert.IsFalse(File.Exists(GetSlotPath(0)));
            Assert.IsFalse(File.Exists(GetBackupPath(0)),
                "DeleteSlot はメインファイルと .bak の両方を削除すべき");
        }
    }
}
