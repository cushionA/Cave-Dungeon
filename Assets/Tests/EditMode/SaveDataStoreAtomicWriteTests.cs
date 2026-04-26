using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// SaveDataStore の 3 段アトミック書き込みに関するテスト群。
    ///
    /// アトミック書き込み仕様 (PR #36 R6 対応):
    ///   1. 一時ファイル (filePath + ".tmp") へ書き込み完了
    ///   2. 既存 filePath を backupPath (.bak) へ File.Move で退避 (初回は skip)
    ///   3. 一時ファイルを filePath へ File.Move で昇格
    /// </summary>
    public class SaveDataStoreAtomicWriteTests
    {
        private string _testDir;
        private SaveDataStore _store;

        [SetUp]
        public void SetUp()
        {
            _testDir = Path.Combine(Application.temporaryCachePath, "SaveDataStoreAtomicWriteTest_" + System.Guid.NewGuid().ToString("N"));
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

        private string GetTempPath(int slotIndex)
        {
            return GetSlotPath(slotIndex) + SaveDataStore.k_TempExtension;
        }

        // ---------- 一時ファイルのクリーンアップ ----------

        [Test]
        public void SaveDataStore_WriteToDisk_AfterSuccess_LeavesNoTempFile()
        {
            SaveSlotData data = new SaveSlotData(0);
            data.entries["val"] = 42;

            _store.WriteToDisk(data);

            Assert.IsTrue(File.Exists(GetSlotPath(0)));
            Assert.IsFalse(File.Exists(GetTempPath(0)),
                "書き込み成功後は .tmp ファイルが残らないはず");
        }

        [Test]
        public void SaveDataStore_WriteToDisk_SecondTime_LeavesNoTempFile()
        {
            SaveSlotData first = new SaveSlotData(0);
            first.entries["val"] = 1;
            _store.WriteToDisk(first);

            SaveSlotData second = new SaveSlotData(0);
            second.entries["val"] = 2;
            _store.WriteToDisk(second);

            Assert.IsFalse(File.Exists(GetTempPath(0)),
                "2 回目書き込み成功後も .tmp ファイルが残らないはず");
        }

        // ---------- 起動時の .tmp 残骸クリーンアップ ----------

        [Test]
        public void SaveDataStore_WriteToDisk_WhenStaleTempExists_OverwritesIt()
        {
            // .tmp が前回クラッシュ時などで残っているケース
            Directory.CreateDirectory(Path.Combine(_testDir, "saves"));
            File.WriteAllText(GetTempPath(0), "stale temp content (should be overwritten)");

            SaveSlotData data = new SaveSlotData(0);
            data.entries["val"] = 7;

            _store.WriteToDisk(data);

            // 書き込み成功 → .tmp は消えて filePath に本体がある
            Assert.IsTrue(File.Exists(GetSlotPath(0)));
            Assert.IsFalse(File.Exists(GetTempPath(0)),
                "前回残骸の .tmp は置換・削除されるはず");

            // 内容が stale content ではなく今回のデータで上書きされている
            SaveSlotData restored = _store.ReadFromDisk(0);
            Assert.IsNotNull(restored);
            Assert.AreEqual(7, System.Convert.ToInt32(restored.entries["val"]));
        }

        // ---------- 3 段 rename の中間状態検証 ----------

        [Test]
        public void SaveDataStore_WriteToDisk_SecondTime_MovesExistingFileToBackup()
        {
            // 1 回目保存
            SaveSlotData first = new SaveSlotData(0);
            first.entries["val"] = 100;
            _store.WriteToDisk(first);

            string firstFileContent = File.ReadAllText(GetSlotPath(0));

            // 2 回目保存 → 1 回目の filePath が .bak に移動、2 回目内容が filePath に確定
            SaveSlotData second = new SaveSlotData(0);
            second.entries["val"] = 200;
            _store.WriteToDisk(second);

            // .bak には 1 回目の内容（File.Move で移動した結果）
            Assert.IsTrue(File.Exists(GetBackupPath(0)),
                ".bak が作成されているべき");
            string backupContent = File.ReadAllText(GetBackupPath(0));
            Assert.AreEqual(firstFileContent, backupContent,
                ".bak は前世代のメインファイルと一致するはず (Move で退避された内容)");

            // filePath には 2 回目の内容（.tmp から Move された結果）
            SaveSlotData restored = _store.ReadFromDisk(0);
            Assert.AreEqual(200, System.Convert.ToInt32(restored.entries["val"]));
        }

        [Test]
        public void SaveDataStore_WriteToDisk_ThirdTime_OverwritesBackupWithLatestFormer()
        {
            // 3 回連続で保存し、.bak が常に「直前世代」になることを検証
            SaveSlotData v1 = new SaveSlotData(0);
            v1.entries["val"] = 1;
            _store.WriteToDisk(v1);

            SaveSlotData v2 = new SaveSlotData(0);
            v2.entries["val"] = 2;
            _store.WriteToDisk(v2);

            // 2 回目書き込み後、.bak は v1、filePath は v2
            SaveSlotData v3 = new SaveSlotData(0);
            v3.entries["val"] = 3;
            _store.WriteToDisk(v3);

            // 3 回目書き込み後、.bak は v2 (直前)、filePath は v3 になっているはず
            SaveSlotData restored = _store.ReadFromDisk(0);
            Assert.AreEqual(3, System.Convert.ToInt32(restored.entries["val"]),
                "filePath には最新 (v3) が入っているはず");

            // filePath を破損させ、.bak フォールバックで v2 が取れることを確認
            File.WriteAllText(GetSlotPath(0), "{ corrupt }");
            LogAssert.Expect(LogType.Error,
                new Regex(@"\[SaveDataStore\].+読み込みに失敗しました"));
            LogAssert.Expect(LogType.Error,
                new Regex(@"\[SaveDataStore\].+\.bak からフォールバック復元しました"));

            SaveSlotData fallback = _store.ReadFromDisk(0);
            Assert.IsNotNull(fallback);
            Assert.AreEqual(2, System.Convert.ToInt32(fallback.entries["val"]),
                ".bak は直前世代 (v2) になっているはず");
        }

        // ---------- 初回書き込み時の挙動 ----------

        [Test]
        public void SaveDataStore_WriteToDisk_FirstTime_SkipsBackupMoveWithoutError()
        {
            // 初回書き込みは filePath が存在しないので backup への Move をスキップし、
            // .tmp → filePath の昇格だけが行われるはず
            SaveSlotData data = new SaveSlotData(0);
            data.entries["val"] = 42;

            _store.WriteToDisk(data);

            Assert.IsTrue(File.Exists(GetSlotPath(0)), "filePath が作成されているはず");
            Assert.IsFalse(File.Exists(GetBackupPath(0)), "初回では .bak は作られないはず");
            Assert.IsFalse(File.Exists(GetTempPath(0)), ".tmp も残っていないはず");
        }

        // ---------- Issue #74: filePath 不在 + .bak 残存シナリオでバックアップが破壊されないこと ----------

        [Test]
        public void SaveDataStore_WriteToDisk_WhenOnlyBackupExists_PreservesBackupContent()
        {
            // Issue #74 再現シナリオ:
            //   前回 WriteToDisk で「Step 3 失敗 → filePath 不在、.bak に直前世代」
            //   状態のまま次の WriteToDisk が走った時、旧実装は Step 2 で .bak を delete
            //   してから File.Move(filePath, .bak) → FileNotFoundException で catch し、
            //   .bak が完全に喪失していた。File.Replace ベースの実装では .bak は
            //   破壊されず、新しい filePath が作成され、.bak は前世代のまま残るはず。

            // 1) 通常 2 回保存 → filePath=v2, .bak=v1
            SaveSlotData v1 = new SaveSlotData(0);
            v1.entries["val"] = 1;
            _store.WriteToDisk(v1);

            SaveSlotData v2 = new SaveSlotData(0);
            v2.entries["val"] = 2;
            _store.WriteToDisk(v2);

            // 2) Step 3 失敗を模擬: filePath を削除 → .bak (v1) だけが残る
            File.Delete(GetSlotPath(0));
            string backupBefore = File.ReadAllText(GetBackupPath(0));

            // 3) この状態で新規保存 (v3) を実行
            SaveSlotData v3 = new SaveSlotData(0);
            v3.entries["val"] = 3;
            _store.WriteToDisk(v3);

            // 4) filePath は v3 で作成されているはず
            Assert.IsTrue(File.Exists(GetSlotPath(0)), "filePath が再作成されているはず");
            Assert.IsFalse(File.Exists(GetTempPath(0)), ".tmp は残らないはず");

            // 5) .bak は v1 のまま (= 復旧経路として有効) であるべき。
            //    旧実装ではここで .bak が delete + Move 失敗で消失していた。
            Assert.IsTrue(File.Exists(GetBackupPath(0)), ".bak は破壊されず残るべき");
            string backupAfter = File.ReadAllText(GetBackupPath(0));
            Assert.AreEqual(backupBefore, backupAfter,
                "filePath 不在状態での再保存は .bak を破壊してはいけない (Issue #74)");

            // 6) filePath を破損させても .bak (v1) からフォールバックできることを確認
            File.WriteAllText(GetSlotPath(0), "{ corrupt }");
            LogAssert.Expect(LogType.Error,
                new Regex(@"\[SaveDataStore\].+読み込みに失敗しました"));
            LogAssert.Expect(LogType.Error,
                new Regex(@"\[SaveDataStore\].+\.bak からフォールバック復元しました"));

            SaveSlotData fallback = _store.ReadFromDisk(0);
            Assert.IsNotNull(fallback, ".bak から復旧できるはず");
            Assert.AreEqual(1, System.Convert.ToInt32(fallback.entries["val"]),
                ".bak は v1 のまま保たれているはず");
        }
    }
}
