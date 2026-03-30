using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// ProjectileController Runtime橋渡しに必要なCore層フローの単体テスト。
    /// 生成・移動・衝突・寿命・貫通の基本フローを検証する。
    /// </summary>
    public class ProjectileControllerTests
    {
        private ProjectilePool _pool;
        private BulletProfile _defaultProfile;

        [SetUp]
        public void SetUp()
        {
            _pool = new ProjectilePool(4);
            _defaultProfile = new BulletProfile
            {
                moveType = BulletMoveType.Straight,
                speed = 10f,
                hitLimit = 1,
                lifeTime = 5f
            };
        }

        [Test]
        public void 飛翔体生成_CorePoolからGet_ActiveCountが1増加する()
        {
            int beforeCount = _pool.ActiveCount;

            Projectile p = _pool.Get();
            p.Initialize(100, _defaultProfile, Vector2.zero, Vector2.right);

            Assert.AreEqual(beforeCount + 1, _pool.ActiveCount);
            Assert.IsTrue(p.IsAlive);
            Assert.AreEqual(100, p.CasterHash);
        }

        [Test]
        public void 飛翔体移動_UpdateAll実行_Positionが前進する()
        {
            Projectile p = _pool.Get();
            p.Initialize(100, _defaultProfile, Vector2.zero, Vector2.right);
            Vector2 initialPos = p.Position;

            ProjectileMovement.UpdateAll(_pool, 0.1f, Vector2.zero);

            Assert.Greater(p.Position.x, initialPos.x);
        }

        [Test]
        public void 飛翔体衝突_ProcessHit実行_ダメージが適用される()
        {
            SoACharaDataDic data = new SoACharaDataDic(4);
            int casterHash = 100;
            int targetHash = 200;

            CharacterVitals casterVitals = new CharacterVitals { currentHp = 100, maxHp = 100 };
            CombatStats casterCombat = new CombatStats
            {
                attack = new ElementalStatus { slash = 50 }
            };
            data.Add(casterHash, casterVitals, casterCombat, default, default);

            CharacterVitals targetVitals = new CharacterVitals { currentHp = 100, maxHp = 100 };
            CombatStats targetCombat = new CombatStats
            {
                defense = new ElementalStatus { slash = 10 }
            };
            data.Add(targetHash, targetVitals, targetCombat, default, default);

            Projectile p = _pool.Get();
            p.Initialize(casterHash, _defaultProfile, Vector2.zero, Vector2.right);

            MagicDefinition magic = new MagicDefinition
            {
                magicType = MagicType.Attack,
                motionValue = 1.0f,
                attackElement = Element.Slash
            };

            ProjectileHitProcessor.HitResult result =
                ProjectileHitProcessor.ProcessHit(p, targetHash, data, magic);

            Assert.Greater(result.damage, 0);
            ref CharacterVitals postVitals = ref data.GetVitals(targetHash);
            Assert.Less(postVitals.currentHp, 100);

            data.Dispose();
        }

        [Test]
        public void 飛翔体寿命_LifeTime経過_IsAliveがfalseになる()
        {
            BulletProfile shortLife = new BulletProfile
            {
                speed = 10f,
                hitLimit = 1,
                lifeTime = 0.5f
            };
            Projectile p = _pool.Get();
            p.Initialize(100, shortLife, Vector2.zero, Vector2.right);

            ProjectileMovement.UpdateAll(_pool, 0.6f, Vector2.zero);

            Assert.IsFalse(p.IsAlive);
        }

        [Test]
        public void 飛翔体貫通_Pierceフラグあり_ヒット後も生存する()
        {
            BulletProfile pierceProfile = new BulletProfile
            {
                speed = 10f,
                hitLimit = 1,
                lifeTime = 5f,
                features = BulletFeature.Pierce
            };
            Projectile p = _pool.Get();
            p.Initialize(100, pierceProfile, Vector2.zero, Vector2.right);

            p.RegisterHit();

            Assert.IsTrue(p.IsAlive);
        }
    }
}
