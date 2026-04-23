using NUnit.Framework;
using UnityEngine;
using Game.Core;
using Game.Runtime;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// ProjectileController のキャラ別ヒット回数カウント機能のテスト。
    /// Dictionary&lt;targetHash, hitCount&gt; による多段ヒット挙動を検証する。
    ///
    /// 検証観点:
    /// - 同一ターゲットへの多段ヒット (hitLimit=3 で 3 ヒット後停止)
    /// - 複数ターゲットへの分散ヒット
    /// - Pierce 弾の多段キャラ貫通
    /// - 非 Pierce が hitLimit 到達で消滅
    /// </summary>
    public class ProjectileHitCountTests
    {
        private GameObject _go;
        private ProjectileController _controller;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("ProjectileHitCountTest");
            // RequireComponent で Rigidbody2D / CircleCollider2D が自動付与される
            _controller = _go.AddComponent<ProjectileController>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
            {
                Object.DestroyImmediate(_go);
            }
        }

        private static Projectile CreateProjectile(int hitLimit, BulletFeature features = BulletFeature.None)
        {
            Projectile p = new Projectile();
            BulletProfile profile = new BulletProfile
            {
                moveType = BulletMoveType.Straight,
                speed = 10f,
                hitLimit = hitLimit,
                lifeTime = 5f,
                features = features
            };
            p.Initialize(100, profile, Vector2.zero, Vector2.right);
            return p;
        }

        [Test]
        public void TryRegisterHit_SameTargetUnderHitLimit_AcceptsEachCall()
        {
            Projectile p = CreateProjectile(hitLimit: 3);
            _controller.Activate(p, default, null);

            const int targetHash = 200;

            Assert.IsTrue(_controller.TryRegisterHit(targetHash, out bool d1));
            Assert.IsFalse(d1, "1回目はまだ上限未到達で継続");
            Assert.AreEqual(1, _controller.GetHitCountForTarget(targetHash));

            Assert.IsTrue(_controller.TryRegisterHit(targetHash, out bool d2));
            Assert.IsFalse(d2, "2回目もまだ継続");
            Assert.AreEqual(2, _controller.GetHitCountForTarget(targetHash));

            Assert.IsTrue(_controller.TryRegisterHit(targetHash, out bool d3));
            Assert.IsTrue(d3, "3回目で hitLimit 到達 → 非Pierceなので消滅指示");
            Assert.AreEqual(3, _controller.GetHitCountForTarget(targetHash));
        }

        [Test]
        public void TryRegisterHit_SameTargetOverHitLimit_Skips()
        {
            Projectile p = CreateProjectile(hitLimit: 2);
            _controller.Activate(p, default, null);

            const int targetHash = 200;

            Assert.IsTrue(_controller.TryRegisterHit(targetHash, out _));
            Assert.IsTrue(_controller.TryRegisterHit(targetHash, out _));
            // 上限到達後はスキップされる
            Assert.IsFalse(_controller.TryRegisterHit(targetHash, out bool d3),
                "3回目は hitLimit(2) 到達済みでスキップされる");
            Assert.IsFalse(d3, "スキップされた場合は消滅指示もfalse");
            Assert.AreEqual(2, _controller.GetHitCountForTarget(targetHash),
                "スキップ時はカウンターも増えない");
        }

        [Test]
        public void TryRegisterHit_MultipleTargets_CountsIndependently()
        {
            // hitLimit=2, Pierce 付きなら複数ターゲットに分散してヒットできる
            Projectile p = CreateProjectile(hitLimit: 2, features: BulletFeature.Pierce);
            _controller.Activate(p, default, null);

            const int hashA = 201;
            const int hashB = 202;
            const int hashC = 203;

            Assert.IsTrue(_controller.TryRegisterHit(hashA, out _));
            Assert.IsTrue(_controller.TryRegisterHit(hashB, out _));
            Assert.IsTrue(_controller.TryRegisterHit(hashA, out _));
            Assert.IsTrue(_controller.TryRegisterHit(hashC, out _));
            Assert.IsTrue(_controller.TryRegisterHit(hashB, out _));

            Assert.AreEqual(2, _controller.GetHitCountForTarget(hashA));
            Assert.AreEqual(2, _controller.GetHitCountForTarget(hashB));
            Assert.AreEqual(1, _controller.GetHitCountForTarget(hashC));

            // A も B も上限到達 → さらなる登録はスキップ。C はまだ余裕あり
            Assert.IsFalse(_controller.TryRegisterHit(hashA, out _));
            Assert.IsFalse(_controller.TryRegisterHit(hashB, out _));
            Assert.IsTrue(_controller.TryRegisterHit(hashC, out _));
            Assert.AreEqual(2, _controller.GetHitCountForTarget(hashC));
        }

        [Test]
        public void TryRegisterHit_PierceBullet_DoesNotTriggerDespawnEvenAtHitLimit()
        {
            Projectile p = CreateProjectile(hitLimit: 3, features: BulletFeature.Pierce);
            _controller.Activate(p, default, null);

            const int hashA = 201;
            const int hashB = 202;

            // ターゲットA に3ヒット (上限到達) しても Pierce なので despawn 指示は出ない
            Assert.IsTrue(_controller.TryRegisterHit(hashA, out bool d1));
            Assert.IsFalse(d1);
            Assert.IsTrue(_controller.TryRegisterHit(hashA, out bool d2));
            Assert.IsFalse(d2);
            Assert.IsTrue(_controller.TryRegisterHit(hashA, out bool d3));
            Assert.IsFalse(d3, "Pierce 弾は hitLimit 到達でも despawn しない");

            // 次は別ターゲットにも当たる
            Assert.IsTrue(_controller.TryRegisterHit(hashB, out bool d4));
            Assert.IsFalse(d4);
            Assert.AreEqual(1, _controller.GetHitCountForTarget(hashB));

            // A は上限到達済みでスキップ、B はまだ当たる
            Assert.IsFalse(_controller.TryRegisterHit(hashA, out _));
            Assert.IsTrue(_controller.TryRegisterHit(hashB, out _));
        }

        [Test]
        public void TryRegisterHit_NonPierceBullet_ReachingHitLimitSignalsDespawn()
        {
            Projectile p = CreateProjectile(hitLimit: 3, features: BulletFeature.None);
            _controller.Activate(p, default, null);

            const int targetHash = 300;

            Assert.IsTrue(_controller.TryRegisterHit(targetHash, out bool d1));
            Assert.IsFalse(d1);
            Assert.IsTrue(_controller.TryRegisterHit(targetHash, out bool d2));
            Assert.IsFalse(d2);
            Assert.IsTrue(_controller.TryRegisterHit(targetHash, out bool d3));
            Assert.IsTrue(d3, "3ヒット目 (hitLimit 到達) で despawn 指示が出る");
        }

        [Test]
        public void TryRegisterHit_HitLimitZero_NormalizesToOne()
        {
            // hitLimit 未設定 (=0) は Core 層の Projectile.Initialize が
            // RemainingHits=1 にフォールバックするのと同じく、ここでも 1 として扱う
            Projectile p = CreateProjectile(hitLimit: 0);
            _controller.Activate(p, default, null);

            const int targetHash = 400;

            Assert.IsTrue(_controller.TryRegisterHit(targetHash, out bool d1));
            Assert.IsTrue(d1, "hitLimit=0 は 1 扱いとなり、1ヒットで despawn 指示");
            Assert.IsFalse(_controller.TryRegisterHit(targetHash, out _),
                "2ヒット目はスキップされる");
        }

        [Test]
        public void Activate_PreviousSessionHitCounts_AreClearedOnReactivation()
        {
            // 前回有効化時のヒット履歴が、再有効化時にリセットされることを検証
            Projectile p1 = CreateProjectile(hitLimit: 2);
            _controller.Activate(p1, default, null);

            const int targetHash = 500;
            _controller.TryRegisterHit(targetHash, out _);
            _controller.TryRegisterHit(targetHash, out _);
            Assert.AreEqual(2, _controller.GetHitCountForTarget(targetHash));

            // 再有効化 (プール再利用シナリオ)
            Projectile p2 = CreateProjectile(hitLimit: 2);
            _controller.Activate(p2, default, null);

            Assert.AreEqual(0, _controller.GetHitCountForTarget(targetHash),
                "Activate で前回の hitCount がクリアされる");
            Assert.IsTrue(_controller.TryRegisterHit(targetHash, out _),
                "リセット後は同じターゲットにも再ヒット可能");
        }
    }
}
