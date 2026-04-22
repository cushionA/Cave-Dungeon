using NUnit.Framework;
using UnityEngine;
using Game.Core;
using Game.Runtime;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// BulletProfile 拡張 4 項目（サイズ変化・スポーン遅延・スポーンオフセット・追尾力変動）の単体テスト。
    /// </summary>
    public class BulletProfileExtensionTests
    {
        // ==================== 1. 弾丸サイズ変化 ====================

        [Test]
        public void サイズ変化_scaleTimeゼロ_常に1を返す()
        {
            Projectile p = new Projectile();
            BulletProfile profile = new BulletProfile
            {
                moveType = BulletMoveType.Straight,
                speed = 10f,
                hitLimit = 1,
                lifeTime = 5f,
                startScale = 2f,
                endScale = 4f,
                scaleTime = 0f, // 無効化
            };
            p.Initialize(1, profile, Vector2.zero, Vector2.right);

            Assert.AreEqual(1f, p.GetCurrentScale(), 0.0001f,
                "scaleTime=0 ならスケール変化は無効で常に 1 を返す");
        }

        [Test]
        public void サイズ変化_startからendへ線形補間される()
        {
            Projectile p = new Projectile();
            BulletProfile profile = new BulletProfile
            {
                moveType = BulletMoveType.Straight,
                speed = 10f,
                hitLimit = 1,
                lifeTime = 10f,
                startScale = 1f,
                endScale = 3f,
                scaleTime = 1f, // 1 秒で補間完了
            };
            ProjectilePool pool = new ProjectilePool(2);
            Projectile proj = pool.Get();
            proj.Initialize(1, profile, Vector2.zero, Vector2.right);

            Assert.AreEqual(1f, proj.GetCurrentScale(), 0.0001f, "t=0 で startScale");

            // 0.5 秒進める → 中間値 = Lerp(1, 3, 0.5) = 2
            ProjectileMovement.UpdateAll(pool, 0.5f, Vector2.zero);
            Assert.AreEqual(2f, proj.GetCurrentScale(), 0.01f, "t=0.5 で中間値");

            // さらに 1 秒進める（合計 1.5 秒 > scaleTime）→ 3 に張り付く
            ProjectileMovement.UpdateAll(pool, 1.0f, Vector2.zero);
            Assert.AreEqual(3f, proj.GetCurrentScale(), 0.01f, "t>=scaleTime で endScale に張り付く");

            pool.Clear();
        }

        // ==================== 2. スポーン遅延 ====================

        [Test]
        public void スポーン遅延_遅延中は位置も寿命も進まない()
        {
            BulletProfile profile = new BulletProfile
            {
                moveType = BulletMoveType.Straight,
                speed = 10f,
                hitLimit = 1,
                lifeTime = 0.5f,
                spawnDelay = 0.3f,
            };
            ProjectilePool pool = new ProjectilePool(2);
            Projectile p = pool.Get();
            p.Initialize(1, profile, Vector2.zero, Vector2.right);

            Assert.IsTrue(p.IsSpawnDelayed, "Initialize 直後は遅延中");

            // 0.2 秒進める（<spawnDelay=0.3）→ 位置は据え置き・寿命カウント進まない
            ProjectileMovement.UpdateAll(pool, 0.2f, Vector2.zero);

            Assert.IsTrue(p.IsSpawnDelayed, "まだ遅延中");
            Assert.AreEqual(Vector2.zero, p.Position, "位置は据え置き");
            Assert.AreEqual(0f, p.ElapsedTime, 0.0001f, "ElapsedTime は遅延中は増えない");
            Assert.IsTrue(p.IsAlive, "寿命を消費していないので生存");

            pool.Clear();
        }

        [Test]
        public void スポーン遅延_遅延終了後に移動と寿命カウント開始()
        {
            BulletProfile profile = new BulletProfile
            {
                moveType = BulletMoveType.Straight,
                speed = 10f,
                hitLimit = 1,
                lifeTime = 5f,
                spawnDelay = 0.2f,
            };
            ProjectilePool pool = new ProjectilePool(2);
            Projectile p = pool.Get();
            p.Initialize(1, profile, Vector2.zero, Vector2.right);

            // 遅延を飛ばす
            ProjectileMovement.UpdateAll(pool, 0.3f, Vector2.zero);

            Assert.IsFalse(p.IsSpawnDelayed, "遅延終了");
            // 遅延終了時に ElapsedTime=0 リセットされる仕様なので、
            // この Update フレームでは移動せず次フレーム以降に移動開始する。
            ProjectileMovement.UpdateAll(pool, 0.1f, Vector2.zero);
            Assert.Greater(p.Position.x, 0f, "遅延終了後フレームで前進");

            pool.Clear();
        }

        // ==================== 3. スポーン位置オフセット ====================

        [Test]
        public void スポーンオフセット_前方成分が発射方向に沿って加算される()
        {
            // direction=(1,0) → forward=(1,0), up=(0,1)
            // localOffset=(2,0) → world offset=(2,0)
            Vector2 result = ProjectileManager.ApplyLocalSpawnOffset(
                Vector2.zero, Vector2.right, new Vector2(2f, 0f));

            Assert.AreEqual(2f, result.x, 0.0001f);
            Assert.AreEqual(0f, result.y, 0.0001f);
        }

        [Test]
        public void スポーンオフセット_direction上向きで前方オフセットはY方向になる()
        {
            // direction=(0,1) → forward=(0,1), up=(-1,0)
            // localOffset=(2, 0) → world offset = forward*2 = (0, 2)
            Vector2 result = ProjectileManager.ApplyLocalSpawnOffset(
                Vector2.zero, Vector2.up, new Vector2(2f, 0f));

            Assert.AreEqual(0f, result.x, 0.0001f);
            Assert.AreEqual(2f, result.y, 0.0001f);
        }

        [Test]
        public void スポーンオフセット_Y成分は方向に対する垂直方向に加算される()
        {
            // direction=(1,0) → forward=(1,0), up=(0,1)
            // localOffset=(0, 3) → world offset=(0, 3)
            Vector2 result = ProjectileManager.ApplyLocalSpawnOffset(
                Vector2.zero, Vector2.right, new Vector2(0f, 3f));

            Assert.AreEqual(0f, result.x, 0.0001f);
            Assert.AreEqual(3f, result.y, 0.0001f);
        }

        [Test]
        public void スポーンオフセット_ゼロ時は元の位置を返す()
        {
            Vector2 origin = new Vector2(5f, 7f);
            Vector2 result = ProjectileManager.ApplyLocalSpawnOffset(
                origin, Vector2.right, Vector2.zero);

            Assert.AreEqual(origin, result);
        }

        // ==================== 4. 追尾力の時間経過変動 ====================

        [Test]
        public void 追尾力_homingStrengthゼロ指定で既定5fにフォールバック()
        {
            Projectile p = new Projectile();
            BulletProfile profile = new BulletProfile
            {
                moveType = BulletMoveType.Homing,
                speed = 10f,
                hitLimit = 1,
                lifeTime = 5f,
                homingStrength = 0f,
                homingAcceleration = 0f,
            };
            p.Initialize(1, profile, Vector2.zero, Vector2.right);

            Assert.AreEqual(5f, p.GetCurrentHomingStrength(), 0.0001f,
                "homingStrength=0 は旧ハードコード値 5f を返す（互換）");
        }

        [Test]
        public void 追尾力_homingAcceleration正で時間経過とともに増加()
        {
            BulletProfile profile = new BulletProfile
            {
                moveType = BulletMoveType.Homing,
                speed = 10f,
                hitLimit = 1,
                lifeTime = 10f,
                homingStrength = 2f,
                homingAcceleration = 3f, // 1 秒あたり +3
            };
            ProjectilePool pool = new ProjectilePool(2);
            Projectile p = pool.Get();
            p.Initialize(1, profile, Vector2.zero, Vector2.right);

            Assert.AreEqual(2f, p.GetCurrentHomingStrength(), 0.0001f, "t=0 でベース値");

            // 1 秒進める → 2 + 3*1 = 5
            ProjectileMovement.UpdateAll(pool, 1.0f, new Vector2(5f, 0f));
            Assert.AreEqual(5f, p.GetCurrentHomingStrength(), 0.01f,
                "t=1 で homingStrength + homingAcceleration*1");

            pool.Clear();
        }

        [Test]
        public void 追尾力_負の加速で下限0にクランプされる()
        {
            BulletProfile profile = new BulletProfile
            {
                moveType = BulletMoveType.Homing,
                speed = 10f,
                hitLimit = 1,
                lifeTime = 10f,
                homingStrength = 1f,
                homingAcceleration = -10f, // 大きく減衰
            };
            ProjectilePool pool = new ProjectilePool(2);
            Projectile p = pool.Get();
            p.Initialize(1, profile, Vector2.zero, Vector2.right);

            // 1 秒進める → 1 + (-10)*1 = -9 → 0 にクランプ
            ProjectileMovement.UpdateAll(pool, 1.0f, new Vector2(5f, 0f));
            Assert.GreaterOrEqual(p.GetCurrentHomingStrength(), 0f,
                "追尾力は負値にならない（0 でクランプ）");

            pool.Clear();
        }
    }
}
