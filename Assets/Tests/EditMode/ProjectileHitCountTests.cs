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

        /// <summary>
        /// 旧セマンティクス互換の弾丸生成ヘルパ: hitLimit と perTargetHitLimit を揃えて設定する。
        /// 旧実装では hitLimit 単独でターゲット別上限を表現していたため、二段管理導入後もこのヘルパ経由で
        /// 既存テストの期待値が保たれる。
        /// </summary>
        private static Projectile CreateProjectile(int hitLimit, BulletFeature features = BulletFeature.None)
        {
            Projectile p = new Projectile();
            BulletProfile profile = new BulletProfile
            {
                moveType = BulletMoveType.Straight,
                speed = 10f,
                hitLimit = hitLimit,
                perTargetHitLimit = hitLimit,
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

        // ==================== perTargetHitLimit 二段管理仕様 (2026-04-23 確定) ====================
        //
        // 新仕様: `BulletProfile.perTargetHitLimit` 追加。
        //   - ターゲットごとの上限。0/未設定は 1 として扱う (互換: 旧 HashSet セマンティクス)
        //   - 到達したターゲットには以降ヒット不成立 (カウント加算スキップ)
        //   - 総数 `hitLimit` は `Projectile.RegisterHit()` 経由で消費、
        //     非Pierce かつ総数到達で `shouldDespawn=true`

        private static Projectile CreateProjectileWithPerTargetLimit(
            int hitLimit, int perTargetHitLimit, BulletFeature features = BulletFeature.None)
        {
            Projectile p = new Projectile();
            BulletProfile profile = new BulletProfile
            {
                moveType = BulletMoveType.Straight,
                speed = 10f,
                hitLimit = hitLimit,
                perTargetHitLimit = perTargetHitLimit,
                lifeTime = 5f,
                features = features,
            };
            p.Initialize(100, profile, Vector2.zero, Vector2.right);
            return p;
        }

        [Test]
        public void PerTargetHitLimit_DefaultOne_BlocksSecondHitToSameTarget()
        {
            // perTargetHitLimit 未設定 (=0) は 1 として解釈される。
            // hitLimit は十分大きいので総数到達は起きない。
            Projectile p = CreateProjectileWithPerTargetLimit(
                hitLimit: 5, perTargetHitLimit: 0, features: BulletFeature.Pierce);
            _controller.Activate(p, default, null);

            const int targetHash = 600;

            Assert.IsTrue(_controller.TryRegisterHit(targetHash, out _),
                "1回目は成立");
            Assert.AreEqual(1, _controller.GetHitCountForTarget(targetHash));

            Assert.IsFalse(_controller.TryRegisterHit(targetHash, out _),
                "perTargetHitLimit デフォルト 1 で同一ターゲットの 2 回目は不成立");
            Assert.AreEqual(1, _controller.GetHitCountForTarget(targetHash),
                "スキップ時はカウントも増えない");
        }

        [Test]
        public void PerTargetHitLimit_Three_AllowsThreeHitsToSameTarget()
        {
            Projectile p = CreateProjectileWithPerTargetLimit(
                hitLimit: 10, perTargetHitLimit: 3, features: BulletFeature.Pierce);
            _controller.Activate(p, default, null);

            const int targetHash = 601;

            Assert.IsTrue(_controller.TryRegisterHit(targetHash, out _), "1回目成立");
            Assert.IsTrue(_controller.TryRegisterHit(targetHash, out _), "2回目成立");
            Assert.IsTrue(_controller.TryRegisterHit(targetHash, out _), "3回目成立");
            Assert.AreEqual(3, _controller.GetHitCountForTarget(targetHash));

            Assert.IsFalse(_controller.TryRegisterHit(targetHash, out _),
                "perTargetHitLimit=3 の上限到達後、4回目は不成立");
            Assert.AreEqual(3, _controller.GetHitCountForTarget(targetHash),
                "スキップ時はカウントも増えない");
        }

        [Test]
        public void PerTargetHitLimit_DifferentTargets_IndependentCounts()
        {
            // A が上限到達しても B は独立したカウントで受け付けられる
            Projectile p = CreateProjectileWithPerTargetLimit(
                hitLimit: 10, perTargetHitLimit: 1, features: BulletFeature.Pierce);
            _controller.Activate(p, default, null);

            const int hashA = 701;
            const int hashB = 702;

            Assert.IsTrue(_controller.TryRegisterHit(hashA, out _));
            Assert.IsFalse(_controller.TryRegisterHit(hashA, out _),
                "A は perTargetHitLimit=1 で上限到達");

            Assert.IsTrue(_controller.TryRegisterHit(hashB, out _),
                "B は A とは独立しているので受理される");
            Assert.AreEqual(1, _controller.GetHitCountForTarget(hashA));
            Assert.AreEqual(1, _controller.GetHitCountForTarget(hashB));
        }

        [Test]
        public void ProjectileController_Acquire_ClearsHitCountsForPoolReuse()
        {
            // Pool 再利用シナリオ: 使用済み弾丸を再 Activate した際に
            // _hitCounts が完全クリアされ、以前のターゲットへ再度ヒットできる
            Projectile first = CreateProjectileWithPerTargetLimit(
                hitLimit: 5, perTargetHitLimit: 1, features: BulletFeature.Pierce);
            _controller.Activate(first, default, null);

            const int hashA = 801;
            const int hashB = 802;
            _controller.TryRegisterHit(hashA, out _);
            _controller.TryRegisterHit(hashB, out _);
            Assert.AreEqual(1, _controller.GetHitCountForTarget(hashA));
            Assert.AreEqual(1, _controller.GetHitCountForTarget(hashB));

            // Pool が同じ Controller を新しい Projectile で再利用する
            Projectile reused = CreateProjectileWithPerTargetLimit(
                hitLimit: 5, perTargetHitLimit: 1, features: BulletFeature.Pierce);
            _controller.Activate(reused, default, null);

            Assert.AreEqual(0, _controller.GetHitCountForTarget(hashA),
                "再 Activate 時に前回の hitCount (A) がクリアされる");
            Assert.AreEqual(0, _controller.GetHitCountForTarget(hashB),
                "再 Activate 時に前回の hitCount (B) がクリアされる");

            Assert.IsTrue(_controller.TryRegisterHit(hashA, out _),
                "クリア後は同じターゲット A に再ヒット可能");
            Assert.IsTrue(_controller.TryRegisterHit(hashB, out _),
                "クリア後は同じターゲット B に再ヒット可能");
        }

        [Test]
        public void PerTargetHitLimit_PierceReachesHitLimit_DoesNotDespawn()
        {
            // Pierce 弾は総数 hitLimit 到達でも shouldDespawn は立たない
            Projectile p = CreateProjectileWithPerTargetLimit(
                hitLimit: 3, perTargetHitLimit: 3, features: BulletFeature.Pierce);
            _controller.Activate(p, default, null);

            const int targetHash = 900;

            Assert.IsTrue(_controller.TryRegisterHit(targetHash, out bool d1));
            Assert.IsFalse(d1);
            Assert.IsTrue(_controller.TryRegisterHit(targetHash, out bool d2));
            Assert.IsFalse(d2);
            Assert.IsTrue(_controller.TryRegisterHit(targetHash, out bool d3));
            Assert.IsFalse(d3, "Pierce 弾は hitLimit 到達でも shouldDespawn しない");
        }

        [Test]
        public void PerTargetHitLimit_NonPierceReachesHitLimit_DespawnsTrue()
        {
            // 非 Pierce 弾は総数 hitLimit 到達の瞬間に shouldDespawn=true
            Projectile p = CreateProjectileWithPerTargetLimit(
                hitLimit: 3, perTargetHitLimit: 3, features: BulletFeature.None);
            _controller.Activate(p, default, null);

            const int targetHash = 901;

            Assert.IsTrue(_controller.TryRegisterHit(targetHash, out bool d1));
            Assert.IsFalse(d1, "1回目は総数未到達");
            Assert.IsTrue(_controller.TryRegisterHit(targetHash, out bool d2));
            Assert.IsFalse(d2, "2回目も総数未到達");
            Assert.IsTrue(_controller.TryRegisterHit(targetHash, out bool d3));
            Assert.IsTrue(d3, "3回目 (hitLimit=3 到達) で despawn 指示");
        }
    }
}
