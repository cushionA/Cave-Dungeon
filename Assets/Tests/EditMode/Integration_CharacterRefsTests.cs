using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    [TestFixture]
    public class Integration_CharacterRefsTests
    {
        [Test]
        public void SoACharaDataDic_GetManaged_ReturnsNullForUnregisteredHash()
        {
            SoACharaDataDic dic = new SoACharaDataDic(4);
            Assert.IsNull(dic.GetManaged(9999));
            dic.Dispose();
        }

        [Test]
        public void SoACharaDataDic_TryGetManaged_ReturnsFalseForUnregisteredHash()
        {
            SoACharaDataDic dic = new SoACharaDataDic(4);
            bool found = dic.TryGetManaged(9999, out ManagedCharacter managed);
            Assert.IsFalse(found);
            Assert.IsNull(managed);
            dic.Dispose();
        }

        [Test]
        public void SoACharaDataDic_AddWithManaged_StoresReference()
        {
            SoACharaDataDic dic = new SoACharaDataDic(4);
            GameObject go = new GameObject("managed_test");
            DummyManagedCharacter mc = go.AddComponent<DummyManagedCharacter>();
            mc.SetHashForTest(123);

            dic.Add(123, default, default, default, default,
                default, default, default, mc);

            ManagedCharacter stored = dic.GetManaged(123);
            Assert.AreSame(mc, stored);

            Object.DestroyImmediate(go);
            dic.Dispose();
        }

        [Test]
        public void SoACharaDataDic_Remove_ClearsManagedSlot()
        {
            SoACharaDataDic dic = new SoACharaDataDic(4);
            GameObject goA = new GameObject("managed_A");
            GameObject goB = new GameObject("managed_B");
            DummyManagedCharacter mcA = goA.AddComponent<DummyManagedCharacter>();
            DummyManagedCharacter mcB = goB.AddComponent<DummyManagedCharacter>();
            mcA.SetHashForTest(100);
            mcB.SetHashForTest(200);

            dic.Add(100, default, default, default, default,
                default, default, default, mcA);
            dic.Add(200, default, default, default, default,
                default, default, default, mcB);
            dic.Remove(100);

            Assert.IsNull(dic.GetManaged(100), "removed key should no longer resolve");
            Assert.AreSame(mcB, dic.GetManaged(200), "swapped entry still accessible after BackSwap");

            Object.DestroyImmediate(goA);
            Object.DestroyImmediate(goB);
            dic.Dispose();
        }

        [Test]
        public void SoACharaDataDic_TryGetManaged_ReturnsStoredReferenceWhenRegistered()
        {
            SoACharaDataDic dic = new SoACharaDataDic(4);
            GameObject go = new GameObject("managed_trg");
            DummyManagedCharacter mc = go.AddComponent<DummyManagedCharacter>();
            mc.SetHashForTest(77);

            dic.Add(77, default, default, default, default,
                default, default, default, mc);

            bool found = dic.TryGetManaged(77, out ManagedCharacter resolved);
            Assert.IsTrue(found);
            Assert.AreSame(mc, resolved);

            Object.DestroyImmediate(go);
            dic.Dispose();
        }

        [Test]
        public void SoACharaDataDic_AddWithSameHash_OverwritesManagedReferenceWithoutIncreasingCount()
        {
            // 自動生成 AddByHash は同一hashが既に存在する場合、既存スロットを上書きする（Count は増えない）
            SoACharaDataDic dic = new SoACharaDataDic(4);
            GameObject goA = new GameObject("managed_first");
            GameObject goB = new GameObject("managed_second");
            DummyManagedCharacter mcA = goA.AddComponent<DummyManagedCharacter>();
            DummyManagedCharacter mcB = goB.AddComponent<DummyManagedCharacter>();
            mcA.SetHashForTest(42);
            mcB.SetHashForTest(42);

            dic.Add(42, default, default, default, default,
                default, default, default, mcA);
            int countAfterFirst = dic.Count;

            // 同じ hash で再 Add すると上書き
            dic.Add(42, default, default, default, default,
                default, default, default, mcB);

            Assert.AreEqual(countAfterFirst, dic.Count, "same-hash re-add should not increase Count");
            Assert.AreSame(mcB, dic.GetManaged(42), "managed reference should be overwritten to latest");

            Object.DestroyImmediate(goA);
            Object.DestroyImmediate(goB);
            dic.Dispose();
        }

        [Test]
        public void SoACharaDataDic_GetManaged_ChainAccessIsNullSafeWhenDamageableIsNull()
        {
            // Runtime側の呼び出しパターン `GetManaged(hash)?.Damageable` が
            // 「未登録」「登録済みだが Damageable が null」の両方で null を返すことを保証する
            SoACharaDataDic dic = new SoACharaDataDic(4);
            GameObject go = new GameObject("managed_nulldmg");
            DummyManagedCharacter mc = go.AddComponent<DummyManagedCharacter>();
            mc.SetHashForTest(55);

            dic.Add(55, default, default, default, default,
                default, default, default, mc);

            // 未登録 hash: null
            IDamageable unregistered = dic.GetManaged(9999)?.Damageable;
            Assert.IsNull(unregistered);

            // 登録済みだが Damageable=null: null (NullReferenceException が出ないこと)
            IDamageable registeredButNull = dic.GetManaged(55)?.Damageable;
            Assert.IsNull(registeredButNull);

            Object.DestroyImmediate(go);
            dic.Dispose();
        }

        [Test]
        public void SoACharaDataDic_GetManaged_WhenManagedDestroyed_ReturnsTrueNullNotFakeNull()
        {
            // Issue #73: 破棄済み MonoBehaviour は Unity の == オーバーライドでは null だが
            // C# native の `?.` / `is null` / `??` はバイパスする (fake-null trap)。
            // GetManaged は Unity bool 変換を内包し、破棄済み ManagedCharacter を C# native null に変換して返すべき。
            SoACharaDataDic dic = new SoACharaDataDic(4);
            GameObject go = new GameObject("managed_destroy");
            DummyManagedCharacter mc = go.AddComponent<DummyManagedCharacter>();
            mc.SetHashForTest(321);

            dic.Add(321, default, default, default, default,
                default, default, default, mc);

            // 登録は残したまま GameObject だけ破棄 → fake-null 状態を作る
            Object.DestroyImmediate(go);

            ManagedCharacter stored = dic.GetManaged(321);
            // C# native null チェック (`is null`) を通すことで fake-null trap を露呈させる。
            // 旧実装ではここで stored が fake-null (Unity == null だが C# != null) のため
            // Assert.IsNull が失敗していた。
            Assert.IsTrue(stored is null,
                "破棄済み ManagedCharacter は C# native null として返るべき (fake null は禁止)");

            // 呼び出し側パターン: `?.Damageable` が安全に短絡することを確認
            IDamageable dmg = dic.GetManaged(321)?.Damageable;
            Assert.IsTrue(dmg is null,
                "破棄済み managed への `?.Damageable` は短絡して null を返すべき");

            dic.Dispose();
        }

        [Test]
        public void SoACharaDataDic_ComputedApis_ThrowObjectDisposedExceptionAfterDispose()
        {
            SoACharaDataDic dic = new SoACharaDataDic(4);
            dic.Add(1, default, default, default, default);
            dic.Dispose();

            Assert.Throws<System.ObjectDisposedException>(() => dic.GetVitals(1));
            Assert.Throws<System.ObjectDisposedException>(() => dic.TryGetManaged(1, out _));
            Assert.Throws<System.ObjectDisposedException>(() => dic.GetAllHashes(new System.Collections.Generic.List<int>()));
        }

        private class DummyManagedCharacter : ManagedCharacter
        {
            private int _hash;
            public override int ObjectHash => _hash;
            public override IDamageable Damageable => null;
            public void SetHashForTest(int hash) { _hash = hash; }
        }
    }
}
