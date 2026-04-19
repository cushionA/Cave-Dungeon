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

        private class DummyManagedCharacter : ManagedCharacter
        {
            private int _hash;
            public override int ObjectHash => _hash;
            public override IDamageable Damageable => null;
            public void SetHashForTest(int hash) { _hash = hash; }
        }
    }
}
