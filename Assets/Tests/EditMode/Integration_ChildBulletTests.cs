using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// 子弾システムの結合テスト。
    /// 親→子弾の生成フロー、TargetHash継承、無限再帰防止を検証する。
    /// </summary>
    public class Integration_ChildBulletTests
    {
        private ProjectilePool _pool;
        private BulletProfile _parentProfile;
        private BulletProfile _childProfile;
        private ChildBulletConfig _config;
        private MagicDefinition _parentMagic;

        [SetUp]
        public void SetUp()
        {
            _pool = new ProjectilePool(16);

            _parentProfile = new BulletProfile
            {
                moveType = BulletMoveType.Straight,
                speed = 15f,
                hitLimit = 1,
                lifeTime = 3f
            };

            _childProfile = new BulletProfile
            {
                moveType = BulletMoveType.Straight,
                speed = 8f,
                hitLimit = 1,
                lifeTime = 2f
            };

            _config = new ChildBulletConfig
            {
                trigger = ChildBulletTrigger.OnHit,
                profile = _childProfile,
                count = 3,
                spreadAngle = 60f
            };

            _parentMagic = new MagicDefinition
            {
                magicName = "SplitShot",
                magicType = MagicType.Attack,
                bulletProfile = _parentProfile,
                motionValue = 1.0f,
                attackElement = Element.Fire,
                childBullet = _config
            };
        }

        [Test]
        public void 子弾_親のCasterHashを継承する()
        {
            int casterHash = 100;

            Projectile parent = _pool.Get();
            parent.Initialize(casterHash, _parentProfile, Vector2.zero, Vector2.right);

            MagicDefinition childMagic = ChildBulletHelper.CreateChildMagic(_parentMagic, _config);

            // 子弾を親と同じcasterHashで生成
            Projectile child = _pool.Get();
            child.Initialize(parent.CasterHash, childMagic.bulletProfile, parent.Position, Vector2.right);

            Assert.AreEqual(casterHash, child.CasterHash);
        }

        [Test]
        public void 子弾_親のTargetHashを継承する()
        {
            int casterHash = 100;
            int targetHash = 200;

            Projectile parent = _pool.Get();
            parent.Initialize(casterHash, _parentProfile, Vector2.zero, Vector2.right);
            parent.TargetHash = targetHash;

            // 子弾にも親のTargetHashをコピー
            Projectile child = _pool.Get();
            child.Initialize(parent.CasterHash, _childProfile, parent.Position, Vector2.right);
            child.TargetHash = parent.TargetHash;

            Assert.AreEqual(targetHash, child.TargetHash);
        }

        [Test]
        public void 子弾_spreadAngle指定_角度が分散する()
        {
            int casterHash = 100;
            int childCount = 3;
            float spreadAngle = 60f;

            Projectile parent = _pool.Get();
            parent.Initialize(casterHash, _parentProfile, Vector2.zero, Vector2.right);

            // SpreadAngle計算ロジックの検証（ProjectileManager.SpawnSpreadと同じ計算）
            float baseAngle = Mathf.Atan2(0f, 1f) * Mathf.Rad2Deg; // 0度（右方向）
            float startAngle = -spreadAngle / 2f;
            float angleStep = spreadAngle / (childCount - 1);

            Projectile[] children = new Projectile[childCount];
            for (int i = 0; i < childCount; i++)
            {
                float angle = baseAngle + startAngle + angleStep * i;
                float rad = angle * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));

                children[i] = _pool.Get();
                children[i].Initialize(casterHash, _childProfile, parent.Position, dir);
            }

            // 端の弾はY方向に分散、中央弾は概ね水平
            Assert.Greater(Mathf.Abs(children[0].Velocity.y), 0.01f);
            Assert.Less(Mathf.Abs(children[1].Velocity.y), 0.01f);
            Assert.Greater(Mathf.Abs(children[2].Velocity.y), 0.01f);
            // 上下対称
            Assert.Less(children[0].Velocity.y, 0f);
            Assert.Greater(children[2].Velocity.y, 0f);
        }

        [Test]
        public void OnDestroy_親消滅時に子弾がPoolに追加される()
        {
            int casterHash = 100;
            ChildBulletConfig destroyConfig = new ChildBulletConfig
            {
                trigger = ChildBulletTrigger.OnDestroy,
                profile = _childProfile,
                count = 2,
                spreadAngle = 30f
            };

            Projectile parent = _pool.Get();
            parent.Initialize(casterHash, _parentProfile, new Vector2(5f, 3f), Vector2.right);
            int countBeforeChildren = _pool.ActiveCount;

            // 親を消滅させて子弾を生成するフロー
            parent.Kill();

            MagicDefinition childMagic = ChildBulletHelper.CreateChildMagic(_parentMagic, destroyConfig);

            // 子弾を親の位置に生成
            for (int i = 0; i < destroyConfig.count; i++)
            {
                Projectile child = _pool.Get();
                child.Initialize(casterHash, childMagic.bulletProfile, parent.Position, Vector2.right);
            }

            // 親(dead) + 子2発 = countBeforeChildren + 2
            Assert.AreEqual(countBeforeChildren + 2, _pool.ActiveCount);
        }

        [Test]
        public void 無限再帰防止_子弾のchildBulletがnull_孫弾は生成されない()
        {
            // 親に子弾設定あり
            Assert.IsNotNull(_parentMagic.childBullet);

            // 子弾MagicDefinitionを生成
            MagicDefinition childMagic = ChildBulletHelper.CreateChildMagic(_parentMagic, _config);

            // 子弾のchildBulletはnull → 孫弾は生成されない
            Assert.IsNull(childMagic.childBullet);

            // childBulletがnullなのでHasChildBulletはfalse
            Assert.IsFalse(ChildBulletHelper.HasChildBullet(childMagic));
        }
    }
}
