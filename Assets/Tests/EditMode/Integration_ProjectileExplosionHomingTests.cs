using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// 爆発範囲ダメージ・ホーミングターゲット・GetAllHashesの結合テスト。
    /// </summary>
    public class Integration_ProjectileExplosionHomingTests
    {
        private SoACharaDataDic _data;
        private ProjectilePool _pool;

        [SetUp]
        public void SetUp()
        {
            _data = new SoACharaDataDic(8);
            _pool = new ProjectilePool(8);
        }

        [TearDown]
        public void TearDown()
        {
            _data.Dispose();
        }

        [Test]
        public void GetAllHashes_登録済み全ハッシュが取得できる()
        {
            _data.Add(100, default, default, default, default);
            _data.Add(200, default, default, default, default);
            _data.Add(300, default, default, default, default);

            List<int> hashes = new List<int>();
            _data.GetAllHashes(hashes);

            Assert.AreEqual(3, hashes.Count);
            Assert.Contains(100, hashes);
            Assert.Contains(200, hashes);
            Assert.Contains(300, hashes);
        }

        [Test]
        public void 爆発範囲_範囲内ターゲットのみヒットする()
        {
            int casterHash = 100;
            int nearHash = 200;
            int farHash = 300;

            CharacterVitals casterVitals = new CharacterVitals { currentHp = 100, maxHp = 100, position = Vector2.zero };
            _data.Add(casterHash, casterVitals, new CombatStats { attack = new ElementalStatus { fire = 30 } }, default, default);

            CharacterVitals nearVitals = new CharacterVitals { currentHp = 100, maxHp = 100, position = new Vector2(1f, 0f) };
            _data.Add(nearHash, nearVitals, default, default, default);

            CharacterVitals farVitals = new CharacterVitals { currentHp = 100, maxHp = 100, position = new Vector2(50f, 0f) };
            _data.Add(farHash, farVitals, default, default, default);

            List<int> allHashes = new List<int>();
            _data.GetAllHashes(allHashes);

            // 爆発半径5f — nearHashは範囲内、farHashは範囲外
            List<int> targets = BulletFeatureProcessor.GetExplosionTargets(
                Vector2.zero, 5f, allHashes, _data);

            Assert.IsTrue(targets.Contains(casterHash) || targets.Contains(nearHash));
            Assert.IsTrue(targets.Contains(nearHash));
            Assert.IsFalse(targets.Contains(farHash));
        }

        [Test]
        public void ホーミング弾_TargetHash指定_TargetPositionが更新される()
        {
            int casterHash = 100;
            int targetHash = 200;

            _data.Add(casterHash, default, default, default, default);
            CharacterVitals targetVitals = new CharacterVitals
            {
                currentHp = 100, maxHp = 100,
                position = new Vector2(10f, 5f)
            };
            _data.Add(targetHash, targetVitals, default, default, default);

            BulletProfile homingProfile = new BulletProfile
            {
                moveType = BulletMoveType.Homing,
                speed = 10f,
                hitLimit = 1,
                lifeTime = 5f
            };

            Projectile p = _pool.Get();
            p.Initialize(casterHash, homingProfile, Vector2.zero, Vector2.right);
            p.TargetHash = targetHash;

            // TargetHashからSoAコンテナの位置を取得してTargetPositionに設定するフロー検証
            ref CharacterVitals tv = ref _data.GetVitals(p.TargetHash);
            p.TargetPosition = tv.position;

            Assert.AreEqual(new Vector2(10f, 5f), p.TargetPosition);

            // UpdateAllでホーミング移動が適用される
            ProjectileMovement.UpdateAll(_pool, 0.1f, Vector2.zero);

            // TargetPositionが設定されているのでそちらに向かう（Y成分が正に変化）
            Assert.Greater(p.Velocity.y, 0f);
        }

        [Test]
        public void ホーミング弾_TargetHash未指定_defaultTargetにフォールバック()
        {
            int casterHash = 100;

            _data.Add(casterHash, default, default, default, default);

            BulletProfile homingProfile = new BulletProfile
            {
                moveType = BulletMoveType.Homing,
                speed = 10f,
                hitLimit = 1,
                lifeTime = 5f
            };

            Projectile p = _pool.Get();
            p.Initialize(casterHash, homingProfile, new Vector2(5f, 0f), Vector2.right);
            // TargetHash = 0 (デフォルト) → TargetPosition も Vector2.zero

            // defaultTarget=Vector2.zero なのでそちらに向かう（X成分が負方向に補正される）
            ProjectileMovement.UpdateAll(_pool, 0.5f, Vector2.zero);

            // 元は右方向だが、原点(0,0)に向かうので左方向への補正がかかる
            Assert.Less(p.Velocity.x, 10f);
        }

        [Test]
        public void Projectile_TargetHash_Resetで0に戻る()
        {
            Projectile p = new Projectile();
            BulletProfile profile = new BulletProfile { speed = 10f, hitLimit = 1 };
            p.Initialize(100, profile, Vector2.zero, Vector2.right);
            p.TargetHash = 200;

            Assert.AreEqual(200, p.TargetHash);

            p.Reset();

            Assert.AreEqual(0, p.TargetHash);
        }
    }
}
