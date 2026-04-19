using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class MagicSystem_HitProcessingTests
    {
        [Test]
        public void HitProcessor_Attack_AppliesDamage()
        {
            SoACharaDataDic data = new SoACharaDataDic();
            data.Add(1, new CharacterVitals { currentHp = 100, maxHp = 100 },
                new CombatStats { attack = new ElementalStatus { fire = 50 } }, default, default);
            data.Add(2, new CharacterVitals { currentHp = 100, maxHp = 100 },
                new CombatStats { defense = new ElementalStatus { fire = 10 } }, default, default);

            Projectile p = new Projectile();
            p.Initialize(1, new BulletProfile { hitLimit = 1, speed = 1f }, default, default);
            MagicDefinition magic = new MagicDefinition
            {
                magicType = MagicType.Attack,
                motionValue = 1.0f,
                attackElement = Element.Fire
            };

            SoABackedMockDamageable receiver = new SoABackedMockDamageable(data, 2);
            ProjectileHitProcessor.HitResult result =
                ProjectileHitProcessor.ProcessHit(p, receiver, data, magic);

            Assert.Greater(result.damage, 0);
            ref CharacterVitals v = ref data.GetVitals(2);
            Assert.Less(v.currentHp, 100);
            data.Dispose();
        }

        [Test]
        public void HitProcessor_Recover_HealsTarget()
        {
            SoACharaDataDic data = new SoACharaDataDic();
            data.Add(1, default, default, default, default);
            data.Add(2, new CharacterVitals { currentHp = 50, maxHp = 100 },
                default, default, default);

            Projectile p = new Projectile();
            p.Initialize(1, new BulletProfile { hitLimit = 1, speed = 1f }, default, default);
            MagicDefinition magic = new MagicDefinition
            {
                magicType = MagicType.Recover,
                healAmount = 30
            };

            StubDamageable stub = new StubDamageable { ObjectHash = 2 };
            ProjectileHitProcessor.HitResult result =
                ProjectileHitProcessor.ProcessHit(p, stub, data, magic);

            Assert.AreEqual(30, result.healAmount);
            ref CharacterVitals v = ref data.GetVitals(2);
            Assert.AreEqual(80, v.currentHp);
            data.Dispose();
        }

        [Test]
        public void HitProcessor_Recover_CapsAtMaxHp()
        {
            SoACharaDataDic data = new SoACharaDataDic();
            data.Add(1, default, default, default, default);
            data.Add(2, new CharacterVitals { currentHp = 90, maxHp = 100 },
                default, default, default);

            Projectile p = new Projectile();
            p.Initialize(1, new BulletProfile { hitLimit = 1, speed = 1f }, default, default);
            MagicDefinition magic = new MagicDefinition
            {
                magicType = MagicType.Recover,
                healAmount = 50
            };

            StubDamageable stub = new StubDamageable { ObjectHash = 2 };
            ProjectileHitProcessor.ProcessHit(p, stub, data, magic);

            ref CharacterVitals v = ref data.GetVitals(2);
            Assert.AreEqual(100, v.currentHp);
            data.Dispose();
        }

        [Test]
        public void HitProcessor_Attack_ReducesProjectileHits()
        {
            SoACharaDataDic data = new SoACharaDataDic();
            data.Add(1, default,
                new CombatStats { attack = new ElementalStatus { fire = 20 } }, default, default);
            data.Add(2, new CharacterVitals { currentHp = 100, maxHp = 100 },
                new CombatStats { defense = new ElementalStatus { fire = 5 } }, default, default);

            Projectile p = new Projectile();
            p.Initialize(1, new BulletProfile { hitLimit = 2, speed = 1f }, default, default);
            MagicDefinition magic = new MagicDefinition
            {
                magicType = MagicType.Attack,
                motionValue = 1.0f,
                attackElement = Element.Fire
            };

            SoABackedMockDamageable receiver = new SoABackedMockDamageable(data, 2);
            ProjectileHitProcessor.ProcessHit(p, receiver, data, magic);

            Assert.AreEqual(1, p.RemainingHits);
            Assert.IsTrue(p.IsAlive);
            data.Dispose();
        }
    }
}
