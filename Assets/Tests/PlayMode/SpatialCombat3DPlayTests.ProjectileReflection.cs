using System.Collections;
using System.Collections.Generic;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.PlayMode
{
    public sealed partial class SpatialCombat3DPlayTests
    {
        [UnityTest]
        public IEnumerator ProjectileReflection3D_FastHomingShotKeepsCapturedDamageAndHitsItsOriginalShooter()
        {
            CharacterActor3D knight = CreateActor(_room, "Knight", new Vector3(0f, 0.1f, 0f),
                CharacterBelong.Ally, CharacterFeature.Player);
            CharacterActor3D sister = CreateActor(_room, "Sister", new Vector3(-2f, 0.1f, 0f),
                CharacterBelong.Ally, CharacterFeature.Companion);
            CharacterActor3D caster = CreateActor(_room, "Caster", new Vector3(0f, 0.1f, 4.5f));
            yield return null;
            GameManager.Data.GetCombatStats(caster.Actor.Hash).attack = new ElementalStatus { slash = 35 };
            Assert.That(ProjectileReflectionShield3D.TryActivate(sister, knight, 2f, 0.75f), Is.True);
            LaunchAtShieldBearer(caster, knight);

            var warnings = new List<ProjectileLockWarning>();
            GameManager.Projectiles3D.CollectLockWarnings(knight.Actor, warnings);
            Assert.That(warnings, Has.Count.EqualTo(1));
            GameManager.Data.GetCombatStats(caster.Actor.Hash).attack = default;
            GameManager.Data.GetCombatStats(sister.Actor.Hash).attack = default;

            Step(0.22f);

            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.Zero);
            GameManager.Projectiles3D.CollectLockWarnings(knight.Actor, warnings);
            Assert.That(warnings, Is.Empty);
            AssertSingleHit(sister, caster, instanceId: 0);
            Assert.That(_hits[0].result.totalDamage, Is.GreaterThan(20));
            AssertHealthy(knight, sister);
        }

        [UnityTest]
        public IEnumerator ProjectileReflection3D_NearerWallTakesPriority()
        {
            CharacterActor3D knight = CreateActor(_room, "Knight", new Vector3(0f, 0.1f, 0f),
                CharacterBelong.Ally, CharacterFeature.Player);
            CharacterActor3D sister = CreateActor(_room, "Sister", new Vector3(-2f, 0.1f, 0f),
                CharacterBelong.Ally, CharacterFeature.Companion);
            CharacterActor3D caster = CreateActor(_room, "Caster", new Vector3(0f, 0.1f, 4.5f));
            yield return null;
            Assert.That(ProjectileReflectionShield3D.TryActivate(sister, knight, 2f, 0.75f), Is.True);
            LaunchAtShieldBearer(caster, knight);

            CreateSurface("Wall in front of the shield", new Vector3(0f, 1f, 1.9f), new Vector3(2f, 2f, 0.1f));
            Step(0.22f);

            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.Zero);
            Assert.That(_hits, Is.Empty);
            AssertHealthy(knight, sister, caster);
        }

        [UnityTest]
        public IEnumerator ProjectileReflection3D_UnprotectedRearStillTakesTheOriginalHit()
        {
            CharacterActor3D knight = CreateActor(_room, "Knight", new Vector3(0f, 0.1f, 0f),
                CharacterBelong.Ally, CharacterFeature.Player);
            CharacterActor3D sister = CreateActor(_room, "Sister", new Vector3(-2f, 0.1f, 0f),
                CharacterBelong.Ally, CharacterFeature.Companion);
            CharacterActor3D caster = CreateActor(_room, "Caster", new Vector3(0f, 0.1f, -4.5f));
            yield return null;
            Assert.That(ProjectileReflectionShield3D.TryActivate(sister, knight, 2f, 0.75f), Is.True);
            long original = LaunchAtShieldBearer(caster, knight);

            Step(0.22f);

            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.Zero);
            AssertSingleHit(caster, knight, original);
            AssertHealthy(caster, sister);
        }

        [UnityTest]
        public IEnumerator ProjectileReflection3D_DoesNotHitReplacementInTheOriginalShooterSlot()
        {
            CharacterActor3D knight = CreateActor(_room, "Knight", new Vector3(0f, 0.1f, 0f),
                CharacterBelong.Ally, CharacterFeature.Player);
            CharacterActor3D sister = CreateActor(_room, "Sister", new Vector3(-2f, 0.1f, 0f),
                CharacterBelong.Ally, CharacterFeature.Companion);
            CharacterActor3D caster = CreateActor(_room, "Caster", new Vector3(0f, 0.1f, 4.5f));
            yield return null;
            Assert.That(ProjectileReflectionShield3D.TryActivate(sister, knight, 2f, 0.75f), Is.True);
            LaunchAtShieldBearer(caster, knight);

            Step(0.06f);
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.EqualTo(1));
            Assert.That(_hits, Is.Empty);
            var warnings = new List<ProjectileLockWarning>();
            GameManager.Projectiles3D.CollectLockWarnings(knight.Actor, warnings);
            Assert.That(warnings, Is.Empty);

            ActorRef original = caster.Actor;
            caster.gameObject.SetActive(false);
            caster.gameObject.SetActive(true);
            Assert.That(caster.Actor.Hash, Is.EqualTo(original.Hash));
            Assert.That(caster.Actor, Is.Not.EqualTo(original));
            AdvanceProjectileFlight(90);

            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.Zero);
            Assert.That(_hits, Is.Empty);
            AssertHealthy(knight, sister, caster);
        }

        [UnityTest]
        public IEnumerator ProjectileReflection3D_ExpiredShieldLetsTheOriginalShotThrough()
        {
            var (knight, sister) = CreateShieldPair();
            CharacterActor3D caster = CreateActor(_room, "Caster", new Vector3(0f, 0.1f, 4.5f));
            yield return null;
            Assert.That(ProjectileReflectionShield3D.TryActivate(sister, knight, 0.04f, 0.75f), Is.True);
            yield return new WaitForSeconds(0.08f);

            long original = LaunchAtShieldBearer(caster, knight);
            Step(0.22f);

            AssertSingleHit(caster, knight, original);
            AssertHealthy(caster, sister);
        }

        [UnityTest]
        public IEnumerator ProjectileReflection3D_RecastStaysActiveAfterPreviousShieldIsDestroyed()
        {
            var (knight, sister) = CreateShieldPair();
            CharacterActor3D caster = CreateActor(_room, "Caster", new Vector3(0f, 0.1f, 4.5f));
            yield return null;
            Assert.That(ProjectileReflectionShield3D.TryActivate(sister, knight, 0.04f, 0.75f), Is.True);
            Assert.That(ProjectileReflectionShield3D.TryActivate(sister, knight, 2f, 0.75f), Is.True);
            yield return new WaitForSeconds(0.08f);

            LaunchAtShieldBearer(caster, knight);
            Step(0.22f);

            AssertSingleHit(sister, caster, instanceId: 0);
            AssertHealthy(knight, sister);
        }

        [UnityTest]
        public IEnumerator ProjectileReflection3D_BulletGrazingTheRimReflects()
            => AssertShotAtShieldEdge(0.8f, reflected: true);

        [UnityTest]
        public IEnumerator ProjectileReflection3D_BulletPassingOutsideTheRimReachesItsTarget()
            => AssertShotAtShieldEdge(0.86f, reflected: false);

        private IEnumerator AssertShotAtShieldEdge(float x, bool reflected)
        {
            var (knight, sister) = CreateShieldPair();
            CharacterActor3D target = CreateActor(_room, "Ally behind the rim", new Vector3(x, 0.1f, 0f),
                CharacterBelong.Ally);
            CharacterActor3D caster = CreateActor(_room, "Caster", new Vector3(x, 0.1f, 4.5f));
            yield return null;
            Assert.That(ProjectileReflectionShield3D.TryActivate(sister, knight, 2f, 0.75f), Is.True);

            // x は円盤の半径より大きい。弾の半径 0.08 だけが縁に届き得る。
            long original = LaunchAtShieldBearer(caster, target);
            Step(0.22f);

            if (reflected)
            {
                AssertSingleHit(sister, caster, instanceId: 0);
                AssertHealthy(target);
            }
            else
            {
                AssertSingleHit(caster, target, original);
                AssertHealthy(caster);
            }
            AssertHealthy(knight, sister);
        }

        private (CharacterActor3D knight, CharacterActor3D sister) CreateShieldPair()
        {
            CharacterActor3D knight = CreateActor(_room, "Knight", new Vector3(0f, 0.1f, 0f),
                CharacterBelong.Ally, CharacterFeature.Player);
            CharacterActor3D sister = CreateActor(_room, "Sister", new Vector3(-2f, 0.1f, 0f),
                CharacterBelong.Ally, CharacterFeature.Companion);
            return (knight, sister);
        }

        private long LaunchAtShieldBearer(CharacterActor3D caster, CharacterActor3D bearer)
        {
            Vector3 offset = bearer.transform.position - caster.transform.position;
            caster.transform.rotation = Quaternion.LookRotation(offset);
            SynchronizeWorld(_room);
            AttackInfo attack = CreateProjectile(BulletMoveType.Homing);
            attack.multiLockRange = 0f;
            attack.attack3D.targetLayers = LayerMask.GetMask("Player");
            attack.projectileInfo.speed = 50f;
            Assert.That(GameManager.Actions.Execute3D(caster, attack, bearer.Actor, out long instanceId), Is.True);
            Step(0.18f);
            Step(0.025f);
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.EqualTo(1));
            Assert.That(_hits, Is.Empty);
            return instanceId;
        }
    }
}
