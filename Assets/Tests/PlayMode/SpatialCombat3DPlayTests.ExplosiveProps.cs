using System.Collections;
using System.Reflection;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests.PlayMode
{
    public sealed partial class SpatialCombat3DPlayTests
    {
        [UnityTest]
        public IEnumerator ExplosiveMelee3D_OnlyTheDesignatedColliderDetonatesAndBlastStaysInItsWorldAndSphere()
        {
            CharacterActor3D attacker = CreateActor(_room, "Knight", new Vector3(0f, 0.1f, 0f), CharacterBelong.Ally);
            CharacterActor3D enemy = CreateActor(_room, "Hostile beside the prop", new Vector3(1.65f, 0.1f, 1.8f));
            CharacterActor3D friendly = CreateActor(_room, "Friendly beside the prop",
                new Vector3(-1.65f, 0.1f, 1.8f), CharacterBelong.Ally);
            CharacterActor3D upstairs = CreateActor(_room, "Hostile upstairs", new Vector3(0f, 4.1f, 1.8f));
            CharacterActor3D foreign = CreateActor(_otherRoom, "Hostile in another physics world", enemy.transform.position);
            ExplosiveProp3D prop = CreateExplosiveProp(new Vector3(0f, 1f, 1.8f), out BoxCollider hit);
            hit.center = Vector3.right * 3f;
            BoxCollider decoy = prop.gameObject.AddComponent<BoxCollider>();
            decoy.size = hit.size;
            int detonations = 0;
            prop.OnDetonated.AddListener(() => detonations++);
            yield return null;
            SynchronizeWorld(_room);
            SynchronizeWorld(_otherRoom);

            AttackInfo melee = CreateAttack(AttackShape3D.Melee);
            melee.attack3D.targetLayers |= LayerMask.GetMask("Default");
            Assert.That(GameManager.Actions.Execute3D(attacker, melee, out _), Is.True);
            Step(0.25f);
            Assert.That(prop.HasDetonated, Is.False, "Another solid collider on the prop is not the designated hit collider.");
            Assert.That(hit.enabled, Is.True);
            Assert.That(_hits, Is.Empty);
            Step(0.45f);

            hit.center = Vector3.zero;
            SynchronizeWorld(_room);
            Assert.That(GameManager.Actions.Execute3D(attacker, melee, out _), Is.True);
            Step(0.1f);
            Assert.That(prop.HasDetonated, Is.False, "The melee windup cannot trigger a prop.");
            Assert.That(_hits, Is.Empty);

            Step(0.15f);
            Assert.That(prop.HasDetonated, Is.True);
            Assert.That(hit.enabled, Is.False);
            Assert.That(decoy.enabled, Is.True);
            AssertSingleBlastHit(attacker, enemy);
            AssertHealthy(attacker, friendly, upstairs, foreign);
            Step(0.45f);

            Assert.That(GameManager.Actions.Execute3D(attacker, melee, out _), Is.True);
            Step(0.25f);
            Assert.That(detonations, Is.EqualTo(1));
            AssertSingleBlastHit(attacker, enemy);
        }

        [UnityTest]
        public IEnumerator ExplosiveProjectile3D_AWallInterceptsWhileAClearSweepDetonatesTheProp()
        {
            CharacterActor3D caster = CreateActor(_room, "Caster", new Vector3(0f, 0.1f, 0f), CharacterBelong.Ally);
            CharacterActor3D victim = CreateActor(_room, "Hostile off the projectile line", new Vector3(1.65f, 0.1f, 5f));
            CharacterActor3D carrier = CreateActor(_room, "Friendly prop carrier", new Vector3(-1.65f, 0.1f, 5f), CharacterBelong.Ally);
            ExplosiveProp3D prop = CreateExplosiveProp(new Vector3(0f, 1f, 5f), out _);
            prop.transform.SetParent(carrier.transform, worldPositionStays: true);
            BoxCollider wall = CreateSurface("Solid wall", new Vector3(0f, 1f, 3.2f), new Vector3(2f, 2f, 0.2f));
            int detonations = 0;
            prop.OnDetonated.AddListener(() => detonations++);
            yield return null;
            SynchronizeWorld(_room);

            AttackInfo shot = CreateProjectile(BulletMoveType.Straight);
            shot.attack3D.targetLayers |= LayerMask.GetMask("Default");
            Assert.That(GameManager.Actions.Execute3D(caster, shot, out _), Is.True);
            Step(0.22f);
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.EqualTo(1));
            Assert.That(detonations, Is.Zero);
            Step(0.5f);
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.Zero);
            Assert.That(prop.HasDetonated, Is.False);
            Assert.That(_hits, Is.Empty);
            AssertHealthy(victim);

            wall.enabled = false;
            ExplosiveProp3D inert = CreateExplosiveProp(new Vector3(0f, 1f, 3.2f), out _, damage: 0);
            SynchronizeWorld(_room);
            Assert.That(GameManager.Actions.Execute3D(caster, shot, out _), Is.True);
            Step(0.22f);
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.EqualTo(1));
            Assert.That(detonations, Is.Zero, "Accepting and launching the shot cannot detonate the prop.");
            Step(0.5f);
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.Zero);
            Assert.That(prop.HasDetonated, Is.True);
            Assert.That(inert.HasDetonated, Is.False);
            Assert.That(detonations, Is.EqualTo(1));
            AssertSingleBlastHit(caster, victim);
            AssertHealthy(carrier);
        }

        private ExplosiveProp3D CreateExplosiveProp(Vector3 position, out BoxCollider hit, int damage = 30)
        {
            var root = new GameObject("Explosive prop");
            root.SetActive(false);
            SceneManager.MoveGameObjectToScene(root, _room);
            root.transform.position = position;
            hit = root.AddComponent<BoxCollider>();
            hit.size = new Vector3(0.4f, 0.7f, 0.4f);
            ExplosiveProp3D prop = root.AddComponent<ExplosiveProp3D>();
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(ExplosiveProp3D).GetField("hitCollider", fields).SetValue(prop, hit);
            typeof(ExplosiveProp3D).GetField("blastRadius", fields).SetValue(prop, 2f);
            typeof(ExplosiveProp3D).GetField("blastDamage", fields).SetValue(prop, damage);
            root.SetActive(true);
            return prop;
        }

        private void AssertSingleBlastHit(CharacterActor3D attacker, CharacterActor3D victim)
        {
            Assert.That(_hits, Has.Count.EqualTo(1));
            DamageResult damage = _hits[0].result;
            Assert.That(damage.attackerRef, Is.EqualTo(attacker.Actor));
            Assert.That(damage.defenderRef, Is.EqualTo(victim.Actor));
            Assert.That(damage.totalDamage, Is.Positive);
            Assert.That(Hp(victim), Is.EqualTo(k_InitialHp - damage.totalDamage));
        }
    }
}
