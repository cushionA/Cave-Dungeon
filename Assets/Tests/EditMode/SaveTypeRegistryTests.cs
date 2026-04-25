using System;
using System.Collections.Generic;
using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// SaveTypeRegistry の単体テスト。
    /// 許可リスト制で型を解決する基盤の挙動を検証する。
    ///
    /// なぜ Reset を毎テストで呼ぶか:
    /// SaveTypeRegistry は process-wide static state を持ち、起動時 RuntimeInitializeOnLoadMethod で
    /// ISaveable 型 + primitive 型をプリ登録する。テストで明示的に登録/未登録を制御するため、
    /// 毎テストで Reset → primitive のみ再登録 (PrePopulatePrimitives) してクリーンスレートから始める。
    /// </summary>
    public class SaveTypeRegistryTests
    {
        [SetUp]
        public void SetUp()
        {
            SaveTypeRegistry.Reset();
            SaveTypeRegistry.PrePopulatePrimitives();
        }

        [TearDown]
        public void TearDown()
        {
            // 後続テストへの汚染を防ぐため、起動時状態 (primitive + ISaveable 自動登録) を完全復元する。
            // PrePopulatePrimitives のみだと、後続テストが SaveDataStore 経由で ISaveable 型のディスク
            // ラウンドトリップを行った場合に該当型が JToken にフォールバックし、Deserialize 側で
            // 期待型キャスト失敗 (LeaderboardManager / AITemplateManager 等は JArray/JObject フォールバック
            // を持たない) → silent data loss を起こす。
            SaveTypeRegistry.Reset();
            SaveTypeRegistry.PrePopulatePrimitives();
            SaveTypeRegistry.AutoRegisterAllSaveables();
        }

        [Test]
        public void SaveTypeRegistry_RegisterType_AllowsResolution()
        {
            SaveTypeRegistry.RegisterType<RegistryTestSaveable>();

            string typeName = typeof(RegistryTestSaveable).FullName;
            bool resolved = SaveTypeRegistry.TryResolve(typeName, out Type type);

            Assert.IsTrue(resolved, "登録した型は TryResolve で復元できるべき");
            Assert.AreEqual(typeof(RegistryTestSaveable), type);
        }

        [Test]
        public void SaveTypeRegistry_TryResolve_UnregisteredType_ReturnsFalse()
        {
            // 未登録の任意型 (process-wide で誰も登録していないことが前提)
            string typeName = typeof(UnregisteredPoco).FullName;
            bool resolved = SaveTypeRegistry.TryResolve(typeName, out Type type);

            Assert.IsFalse(resolved, "未登録型は TryResolve で false を返すべき");
            Assert.IsNull(type);
        }

        [Test]
        public void SaveTypeRegistry_TryResolve_NullOrEmpty_ReturnsFalse()
        {
            bool resolvedNull = SaveTypeRegistry.TryResolve(null, out Type typeForNull);
            Assert.IsFalse(resolvedNull);
            Assert.IsNull(typeForNull);

            bool resolvedEmpty = SaveTypeRegistry.TryResolve(string.Empty, out Type typeForEmpty);
            Assert.IsFalse(resolvedEmpty);
            Assert.IsNull(typeForEmpty);

            bool resolvedWhitespace = SaveTypeRegistry.TryResolve("   ", out Type typeForWs);
            Assert.IsFalse(resolvedWhitespace);
            Assert.IsNull(typeForWs);
        }

        [Test]
        public void SaveTypeRegistry_PrimitiveTypes_PreRegistered()
        {
            // PrePopulatePrimitives で登録される BCL 型が解決できることを確認
            Assert.IsTrue(SaveTypeRegistry.TryResolve(typeof(int).FullName, out _), "int は初期登録されるべき");
            Assert.IsTrue(SaveTypeRegistry.TryResolve(typeof(long).FullName, out _), "long は初期登録されるべき");
            Assert.IsTrue(SaveTypeRegistry.TryResolve(typeof(float).FullName, out _), "float は初期登録されるべき");
            Assert.IsTrue(SaveTypeRegistry.TryResolve(typeof(double).FullName, out _), "double は初期登録されるべき");
            Assert.IsTrue(SaveTypeRegistry.TryResolve(typeof(bool).FullName, out _), "bool は初期登録されるべき");
            Assert.IsTrue(SaveTypeRegistry.TryResolve(typeof(string).FullName, out _), "string は初期登録されるべき");

            Assert.IsTrue(SaveTypeRegistry.TryResolve(typeof(int[]).FullName, out _), "int[] は初期登録されるべき");
            Assert.IsTrue(SaveTypeRegistry.TryResolve(typeof(string[]).FullName, out _), "string[] は初期登録されるべき");
            Assert.IsTrue(SaveTypeRegistry.TryResolve(typeof(bool[]).FullName, out _), "bool[] は初期登録されるべき");

            Assert.IsTrue(SaveTypeRegistry.TryResolve(typeof(Dictionary<string, bool>).FullName, out _), "Dictionary<string,bool> は初期登録されるべき");
            Assert.IsTrue(SaveTypeRegistry.TryResolve(typeof(List<string>).FullName, out _), "List<string> は初期登録されるべき");
            Assert.IsTrue(SaveTypeRegistry.TryResolve(typeof(List<int>).FullName, out _), "List<int> は初期登録されるべき");
        }

        [Test]
        public void SaveTypeRegistry_RegisterType_DangerousType_NotAllowed()
        {
            // System.IO.File など、登録していない危険型は解決できないことを確認
            // (プリ登録 primitive にも含まれない)
            bool resolved = SaveTypeRegistry.TryResolve("System.IO.File", out Type type);
            Assert.IsFalse(resolved, "明示的に登録されていない危険型 (System.IO.File) は解決できないべき");
            Assert.IsNull(type);
        }

        [Test]
        public void SaveTypeRegistry_IsAllowed_ReturnsTrueForRegistered()
        {
            SaveTypeRegistry.RegisterType<RegistryTestSaveable>();
            Assert.IsTrue(SaveTypeRegistry.IsAllowed(typeof(RegistryTestSaveable)));
            Assert.IsFalse(SaveTypeRegistry.IsAllowed(typeof(UnregisteredPoco)));
        }

        // テスト用の最小 ISaveable 実装。private で外部影響を遮断し、
        // SaveTypeRegistry.RegisterType<T> で明示的に登録した時のみ resolution 経路に乗る
        [Serializable]
        private class RegistryTestSaveable : ISaveable
        {
            public string SaveId => "RegistryTestSaveable";

            public object Serialize()
            {
                return 0;
            }

            public void Deserialize(object data)
            {
            }
        }

        // 登録しない POCO。SaveTypeRegistry.IsAllowed の denial 経路を確認する用
        private class UnregisteredPoco
        {
            public int Value;
        }
    }
}
