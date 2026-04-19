using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// ProjectileController結合テスト。
    /// MagicCaster発火→弾丸生成→移動→衝突→プール返却のE2Eフローを検証する。
    /// </summary>
    public class Integration_ProjectileFlowTests
    {
        private SoACharaDataDic _data;
        private ProjectilePool _pool;
        private int _casterHash;
        private int _targetHash;
        private MagicDefinition _attackMagic;

        [SetUp]
        public void SetUp()
        {
            _data = new SoACharaDataDic(4);
            _pool = new ProjectilePool(8);
            _casterHash = 100;
            _targetHash = 200;

            CharacterVitals casterVitals = new CharacterVitals
            {
                currentHp = 100, maxHp = 100,
                currentMp = 50, maxMp = 50
            };
            CombatStats casterCombat = new CombatStats
            {
                attack = new ElementalStatus { fire = 40 }
            };
            _data.Add(_casterHash, casterVitals, casterCombat, default, default);

            CharacterVitals targetVitals = new CharacterVitals
            {
                currentHp = 200, maxHp = 200,
                currentArmor = 10, maxArmor = 10
            };
            CombatStats targetCombat = new CombatStats
            {
                defense = new ElementalStatus { fire = 5 }
            };
            _data.Add(_targetHash, targetVitals, targetCombat, default, default);

            _attackMagic = new MagicDefinition
            {
                magicName = "Fireball",
                magicId = 1,
                magicType = MagicType.Attack,
                mpCost = 10,
                castTime = 0f,
                bulletCount = 1,
                bulletProfile = new BulletProfile
                {
                    moveType = BulletMoveType.Straight,
                    speed = 15f,
                    hitLimit = 1,
                    lifeTime = 3f
                },
                motionValue = 1.2f,
                attackElement = Element.Fire
            };
        }

        [TearDown]
        public void TearDown()
        {
            _data.Dispose();
        }

        [Test]
        public void MagicCaster発火_OnFiredイベント_弾丸データが正しく初期化される()
        {
            MagicCaster caster = new MagicCaster();
            int firedCasterHash = 0;
            MagicDefinition firedMagic = default;

            caster.OnFired += (hash, magic) =>
            {
                firedCasterHash = hash;
                firedMagic = magic;
            };

            bool started = caster.StartCast(_attackMagic, _casterHash, _data, 0f);

            Assert.IsTrue(started);
            Assert.AreEqual(_casterHash, firedCasterHash);
            Assert.AreEqual("Fireball", firedMagic.magicName);
            Assert.AreEqual(BulletMoveType.Straight, firedMagic.bulletProfile.moveType);

            caster.Dispose();
        }

        [Test]
        public void 複数弾生成_SpreadAngle指定_角度が分散する()
        {
            BulletProfile spreadProfile = new BulletProfile
            {
                moveType = BulletMoveType.Straight,
                speed = 10f,
                hitLimit = 1,
                lifeTime = 3f,
                spreadAngle = 30f
            };

            int bulletCount = 3;
            float totalSpread = spreadProfile.spreadAngle;
            float angleStep = totalSpread / (bulletCount - 1);
            float startAngle = -totalSpread / 2f;

            Projectile[] projectiles = new Projectile[bulletCount];
            for (int i = 0; i < bulletCount; i++)
            {
                float angle = startAngle + angleStep * i;
                float rad = angle * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));

                projectiles[i] = _pool.Get();
                projectiles[i].Initialize(_casterHash, spreadProfile, Vector2.zero, dir);
            }

            Assert.AreEqual(bulletCount, _pool.ActiveCount);

            // 中央弾は概ね水平、端弾は角度がついている
            Assert.Greater(Mathf.Abs(projectiles[0].Velocity.y), 0.01f);
            Assert.Less(Mathf.Abs(projectiles[1].Velocity.y), 0.01f);
            Assert.Greater(Mathf.Abs(projectiles[2].Velocity.y), 0.01f);
        }

        [Test]
        public void ProjectileHitProcessor経由_HpArmorLogicが適用_HPがクランプされる()
        {
            // 超高ダメージでもHP < 0にならないことを検証
            CombatStats overpower = new CombatStats
            {
                attack = new ElementalStatus { fire = 9999 }
            };
            int opHash = 999;
            CharacterVitals opVitals = new CharacterVitals
            {
                currentHp = 100, maxHp = 100
            };
            _data.Add(opHash, opVitals, overpower, default, default);

            Projectile p = _pool.Get();
            p.Initialize(opHash, _attackMagic.bulletProfile, Vector2.zero, Vector2.right);

            MagicDefinition strongMagic = _attackMagic;
            strongMagic.motionValue = 10f;

            SoABackedMockDamageable receiver = new SoABackedMockDamageable(_data, _targetHash);
            ProjectileHitProcessor.ProcessHit(p, receiver, _data, strongMagic);

            ref CharacterVitals postTarget = ref _data.GetVitals(_targetHash);
            Assert.GreaterOrEqual(postTarget.currentHp, 0);
        }

        [Test]
        public void 死亡弾丸_ReturnAllDead_Poolに正しく返却される()
        {
            Projectile p1 = _pool.Get();
            Projectile p2 = _pool.Get();
            p1.Initialize(_casterHash, _attackMagic.bulletProfile, Vector2.zero, Vector2.right);
            p2.Initialize(_casterHash, _attackMagic.bulletProfile, Vector2.zero, Vector2.right);

            Assert.AreEqual(2, _pool.ActiveCount);

            p1.Kill();
            _pool.ReturnAllDead();

            Assert.AreEqual(1, _pool.ActiveCount);
            Assert.IsTrue(p2.IsAlive);
        }

        [Test]
        public void キャスター不在_ProcessHit_ダメージ0で安全に終了する()
        {
            int invalidCaster = 9999;
            Projectile p = _pool.Get();
            p.Initialize(invalidCaster, _attackMagic.bulletProfile, Vector2.zero, Vector2.right);

            SoABackedMockDamageable receiver = new SoABackedMockDamageable(_data, _targetHash);
            ProjectileHitProcessor.HitResult result =
                ProjectileHitProcessor.ProcessHit(p, receiver, _data, _attackMagic);

            Assert.AreEqual(0, result.damage);
            // ターゲットのHPが変わっていないことを検証
            ref CharacterVitals targetVitals = ref _data.GetVitals(_targetHash);
            Assert.AreEqual(200, targetVitals.currentHp);
        }
    }
}
