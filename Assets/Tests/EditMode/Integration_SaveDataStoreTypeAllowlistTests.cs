using System;
using System.IO;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// SaveDataStore の許可リスト制 (SaveTypeRegistry 委譲) 統合テスト。
    ///
    /// 結合テスト 3 観点:
    /// 1. 既存ロジック呼び出し検証: SaveDataStore が SaveTypeRegistry.TryResolve 経由で型解決し、
    ///    許可型は型復元される/未許可型は JToken にフォールバックすることを書込→読込ラウンドトリップで確認
    /// 2. 状態シーケンス検証: ホワイトリスト追加→書込→読込で型復元、未登録のままだと JToken のまま
    /// 3. 境界値・不変条件検証: 偽造 typeName ("System.IO.File" 等) を JSON に直接埋め込んで読み込み、
    ///    型インスタンス化されないこと (security invariant)
    /// </summary>
    public class Integration_SaveDataStoreTypeAllowlistTests
    {
        private string _testDir;
        private SaveDataStore _store;

        [SetUp]
        public void SetUp()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "SaveTypeAllowlist_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
            _store = new SaveDataStore(_testDir);

            // クリーンスレートで開始 (primitive 型のみ登録)
            SaveTypeRegistry.Reset();
            SaveTypeRegistry.PrePopulatePrimitives();
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }

            // 後続テストへの汚染を防ぐ
            SaveTypeRegistry.Reset();
            SaveTypeRegistry.PrePopulatePrimitives();
        }

        [Test]
        public void SaveDataStore_WriteThenRead_RegisteredCustomType_PreservesType()
        {
            // 許可リストにテスト用 ISaveable 実装型を登録
            SaveTypeRegistry.RegisterType<AllowlistTestSaveable>();

            AllowlistTestSaveable original = new AllowlistTestSaveable
            {
                Name = "test_value",
                Count = 42
            };

            SaveSlotData slot = new SaveSlotData(0);
            slot.entries["custom"] = original;

            _store.WriteToDisk(slot);
            SaveSlotData restored = _store.ReadFromDisk(0);

            Assert.IsNotNull(restored);
            Assert.IsTrue(restored.entries.ContainsKey("custom"));
            // 登録済みの型は実型で復元される (JToken ではない)
            Assert.IsInstanceOf<AllowlistTestSaveable>(restored.entries["custom"]);

            AllowlistTestSaveable round = (AllowlistTestSaveable)restored.entries["custom"];
            Assert.AreEqual("test_value", round.Name);
            Assert.AreEqual(42, round.Count);
        }

        [Test]
        public void SaveDataStore_WriteThenRead_UnregisteredType_FallsBackToJToken()
        {
            // 意図的に登録しないままの POCO
            UnregisteredAllowlistPoco original = new UnregisteredAllowlistPoco
            {
                Label = "unregistered",
                Value = 99
            };

            SaveSlotData slot = new SaveSlotData(0);
            slot.entries["poco"] = original;

            _store.WriteToDisk(slot);
            SaveSlotData restored = _store.ReadFromDisk(0);

            Assert.IsNotNull(restored);
            Assert.IsTrue(restored.entries.ContainsKey("poco"));
            // 未許可型は JToken (JObject) にフォールバックすることが境界値の不変条件
            Assert.IsInstanceOf<JToken>(restored.entries["poco"]);
            Assert.IsNotInstanceOf<UnregisteredAllowlistPoco>(restored.entries["poco"]);

            // 中身は JSON として保持されているので、必要なら呼び出し側が ToObject で復元可
            JObject jobj = (JObject)restored.entries["poco"];
            Assert.AreEqual("unregistered", jobj["Label"]?.ToString());
            Assert.AreEqual(99, (int)jobj["Value"]);
        }

        [Test]
        public void SaveDataStore_ReadFromDisk_ForgedTypeName_FallsBackToJToken()
        {
            // 偽造された JSON を直接ディスクに書き、SaveDataStore が読み込んでも
            // 危険型 (System.IO.File 等) のインスタンス化が起きないことを確認する。
            // これがセキュリティ上の最重要不変条件。
            string savesDir = Path.Combine(_testDir, "saves");
            Directory.CreateDirectory(savesDir);
            string filePath = Path.Combine(savesDir, "slot_0.json");

            string forgedJson = @"{
  ""version"": 1,
  ""slotIndex"": 0,
  ""timestamp"": ""2026-04-25T00:00:00"",
  ""entries"": {
    ""malicious"": {
      ""typeName"": ""System.IO.File"",
      ""json"": ""\""dummy\""""
    },
    ""attempt2"": {
      ""typeName"": ""System.Diagnostics.Process"",
      ""json"": ""\""dummy\""""
    }
  }
}";
            File.WriteAllText(filePath, forgedJson);

            // 読み込みは成功するが、危険型のインスタンス化は起きない (JToken フォールバック)
            SaveSlotData restored = _store.ReadFromDisk(0);

            Assert.IsNotNull(restored, "偽造ファイルでも構造が valid なら読み込み自体は成功する");
            Assert.IsTrue(restored.entries.ContainsKey("malicious"));
            Assert.IsTrue(restored.entries.ContainsKey("attempt2"));

            // 危険型として復元されていないことが核心 (security invariant)
            Assert.IsNotInstanceOf<System.IO.FileStream>(restored.entries["malicious"]);
            Assert.IsNotInstanceOf<System.Diagnostics.Process>(restored.entries["attempt2"]);

            // JToken (もしくは null/string) 経路のいずれかにフォールバックしていればよい。
            // 現実装では JToken (JValue) として保持される
            object malicious = restored.entries["malicious"];
            object attempt2 = restored.entries["attempt2"];
            Assert.IsTrue(malicious is JToken || malicious is string,
                $"偽造 typeName 'System.IO.File' は JToken/string にフォールバックすべき。実型: {malicious?.GetType().FullName}");
            Assert.IsTrue(attempt2 is JToken || attempt2 is string,
                $"偽造 typeName 'System.Diagnostics.Process' は JToken/string にフォールバックすべき。実型: {attempt2?.GetType().FullName}");
        }

        [Test]
        public void SaveDataStore_WriteThenRead_PrimitiveTypes_StillWorks()
        {
            // primitive 型はプリ登録されているので従来通り型復元できることを確認
            // (既存セーブファイルの後方互換性)
            SaveSlotData slot = new SaveSlotData(0);
            slot.entries["intVal"] = 1234;
            slot.entries["strVal"] = "hello";
            slot.entries["boolVal"] = true;
            slot.entries["intArr"] = new int[] { 1, 2, 3 };

            _store.WriteToDisk(slot);
            SaveSlotData restored = _store.ReadFromDisk(0);

            Assert.IsNotNull(restored);
            Assert.AreEqual(1234, Convert.ToInt32(restored.entries["intVal"]));
            Assert.AreEqual("hello", restored.entries["strVal"]);
            Assert.AreEqual(true, restored.entries["boolVal"]);

            int[] restoredArr = restored.entries["intArr"] as int[];
            Assert.IsNotNull(restoredArr, "int[] は型復元されるべき (primitive 初期登録)");
            CollectionAssert.AreEqual(new int[] { 1, 2, 3 }, restoredArr);
        }

        // テスト用 ISaveable 実装。
        // SaveTypeRegistry.RegisterType<T> で明示登録された場合のみ復元経路に乗る。
        [Serializable]
        private class AllowlistTestSaveable : ISaveable
        {
            public string Name;
            public int Count;

            public string SaveId => "AllowlistTestSaveable";

            public object Serialize() { return this; }
            public void Deserialize(object data) { }
        }

        // 登録しない POCO。JToken フォールバック経路の確認に使う
        private class UnregisteredAllowlistPoco
        {
            public string Label;
            public int Value;
        }
    }
}
