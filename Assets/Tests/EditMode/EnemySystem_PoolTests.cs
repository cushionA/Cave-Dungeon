using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class EnemySystem_PoolTests
    {
        [Test]
        public void EnemyPool_Get_ReturnsId()
        {
            EnemyPool pool = new EnemyPool(4);
            int id = pool.Get();
            Assert.AreNotEqual(0, id);
            Assert.AreEqual(1, pool.ActiveCount);
        }

        [Test]
        public void EnemyPool_Return_Recycles()
        {
            EnemyPool pool = new EnemyPool(4);
            int id = pool.Get();
            pool.Return(id);
            Assert.AreEqual(0, pool.ActiveCount);
        }

        [Test]
        public void EnemyPool_ReturnAll_ClearsActive()
        {
            EnemyPool pool = new EnemyPool(4);
            pool.Get();
            pool.Get();
            pool.Get();
            pool.ReturnAll();
            Assert.AreEqual(0, pool.ActiveCount);
        }
    }
}
