using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using CharacterInfo = Game.Core.CharacterInfo;

namespace Game.Tests.PlayMode
{
    public sealed class EncounterExplosiveAim3DPlayTests
    {
        private readonly List<ScriptableObject> _assets = new();
        private Scene _room;
        private CharacterActor3D _caster;
        private CharacterActor3D _knight;
        private ExplosiveProp3D _prop;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            GameObject world = new GameObject("Explosive aim world");
            world.SetActive(false);
            world.AddComponent<ProjectileManager3D>();
            world.AddComponent<GameManager>();
            world.SetActive(true);
            _room = SceneManager.CreateScene(nameof(EncounterExplosiveAim3DPlayTests),
                new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            CreateBox("Floor", new Vector3(0f, -0.25f, 3f), new Vector3(12f, 0.5f, 14f), "Ground");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_room.isLoaded) { yield return SceneManager.UnloadSceneAsync(_room); }
            foreach (ScriptableObject asset in _assets) { Object.Destroy(asset); }
            _assets.Clear();
            TestSceneHelper.Cleanup();
            yield return null;
        }

        [UnityTest]
        public IEnumerator ExplosiveAim_WhenObservedEnemyIsBesideSafeProp_FiresAtPropAndOneBlastHits()
        {
            CreateEncounter();
            int fullHp = Hp(_knight);
            int detonations = 0;
            _prop.OnDetonated.AddListener(() => detonations++);

            for (int step = 0; step < 85 && !_prop.HasDetonated; step++) { yield return new WaitForFixedUpdate(); }

            Assert.That(_prop.HasDetonated, Is.True);
            Assert.That(detonations, Is.EqualTo(1));
            Assert.That(Hp(_knight), Is.LessThan(fullHp));
        }

        [UnityTest]
        public IEnumerator ExplosiveAim_WhenProjectileRadiusClipsCover_FallsBackToActorAttack()
        {
            CreateEncounter();
            CreateBox("Cover clips the swept radius", new Vector3(0.22f, 1f, 2.4f), new Vector3(0.10f, 2f, 0.2f), "Ground");
            int fullHp = Hp(_knight);

            for (int step = 0; step < 85 && Hp(_knight) == fullHp; step++) { yield return new WaitForFixedUpdate(); }

            Assert.That(Hp(_knight), Is.LessThan(fullHp));
            Assert.That(_prop.HasDetonated, Is.False);
        }

        [UnityTest]
        public IEnumerator ExplosiveAim_WhenUnregisteredAllyIsInBlast_FallsBackToActorAttack()
        {
            CreateEncounter();
            CharacterActor3D ally = CreateActor("Outside encounter roster", new Vector3(-1.2f, 0.04f, 5f), CharacterBelong.Enemy);
            int fullHp = Hp(_knight);

            for (int step = 0; step < 85 && Hp(_knight) == fullHp; step++) { yield return new WaitForFixedUpdate(); }

            Assert.That(Hp(_knight), Is.LessThan(fullHp));
            Assert.That(_prop.HasDetonated, Is.False);
            Assert.That(ally.Actor.IsValid, Is.True);
        }

        [UnityTest]
        public IEnumerator ExplosiveAim_WhenAllyEntersBlastDuringWindup_DoesNotReleaseCommittedShot()
        {
            CreateEncounter();
            CharacterActor3D ally = CreateActor("Entering ally", new Vector3(-3f, 0.04f, 5f), CharacterBelong.Enemy);
            ActionExecutionSystem execution = GameManager.Actions.Execution;
            ActionExecution shot = default;
            for (int step = 0; step < 30; step++)
            {
                yield return new WaitForFixedUpdate();
                if (execution.GetPrimary(_caster.Actor.Hash, out shot) && shot.phase == AnimationPhase.Anticipation) { break; }
            }
            Assert.That(shot.instanceId, Is.Not.Zero);
            Assert.That(shot.phase, Is.EqualTo(AnimationPhase.Anticipation));
            Assert.That(shot.targetRef, Is.EqualTo(_knight.Actor));
            ally.transform.position = new Vector3(-1.2f, 0.04f, 5f);

            ActionExecution current = default;
            for (int step = 0; step < 30; step++)
            {
                yield return new WaitForFixedUpdate();
                if (!execution.TryGetInstance(shot.instanceId, out current) || current.phase == AnimationPhase.Active) { break; }
            }

            Assert.That(current.phase, Is.EqualTo(AnimationPhase.Active));
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.Zero);
            Assert.That(_prop.HasDetonated, Is.False);
        }

        private void CreateEncounter()
        {
            _caster = CreateActor("Caster", Vector3.up * 0.04f, CharacterBelong.Enemy);
            _knight = CreateActor("Knight", new Vector3(1.4f, 0.04f, 5f), CharacterBelong.Ally);
            BoxCollider hit = CreateBox("Barrel", new Vector3(0f, 0.95f, 5f), new Vector3(0.45f, 1.1f, 0.5f), "Default");
            _prop = hit.gameObject.AddComponent<ExplosiveProp3D>();
            Set(_prop, "hitCollider", hit);
            Set(_prop, "blastRadius", 1.8f);
            Set(_prop, "blastDamage", 60);

            EncounterCombat3D encounter = CreateObject("Encounter", Vector3.zero).AddComponent<EncounterCombat3D>();
            Set(encounter, "knight", _knight);
            Set(encounter, "obstructionLayers", (LayerMask)LayerMask.GetMask("Ground"));
            Set(encounter, "explosiveProps", new[] { _prop });
            EncounterCombatant3D combatant = _caster.gameObject.AddComponent<EncounterCombatant3D>();
            Set(combatant, "attack", CreateProjectileAttack());
            Set(combatant, "attackRange", 8f);
            Assert.That(encounter.Register(combatant), Is.True);
            Physics.SyncTransforms();
            _room.GetPhysicsScene().Simulate(Time.fixedDeltaTime);
        }

        private AttackInfo CreateProjectileAttack()
        {
            MotionDefinition motion = ScriptableObject.CreateInstance<MotionDefinition>();
            motion.info = new MotionInfo { preMotionDuration = 0.2f, activeMotionDuration = 0.15f, recoveryDuration = 0.2f };
            _assets.Add(motion);
            AttackInfo attack = ScriptableObject.CreateInstance<AttackInfo>();
            attack.motion = motion;
            attack.damageMultiplier = 1f;
            attack.attackElement = Element.Slash;
            attack.projectileInfo = new ProjectileInfo
            {
                hasProjectile = true, trajectory = BulletMoveType.Straight, speed = 18f, lifetime = 1f, projectileCount = 1,
            };
            attack.attack3D = new AttackProfile3D
            {
                shape = AttackShape3D.Projectile, localCenter = new Vector3(0f, 0.95f, 0.1f), projectileRadius = 0.25f,
                targetLayers = LayerMask.GetMask("Player", "Default"), obstructionLayers = LayerMask.GetMask("Ground"),
            };
            _assets.Add(attack);
            return attack;
        }

        private CharacterActor3D CreateActor(string name, Vector3 position, CharacterBelong belong)
        {
            bool player = belong == CharacterBelong.Ally;
            GameObject body = CreateObject(name, position);
            body.SetActive(false);
            body.layer = LayerMask.NameToLayer(player ? "Player" : "Enemy");
            CharacterController controller = body.AddComponent<CharacterController>();
            controller.center = Vector3.up * 0.8f;
            controller.height = 1.6f;
            controller.radius = 0.3f;
            CharacterActor3D actor = body.AddComponent<CharacterActor3D>();
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(belong, player ? CharacterFeature.Player : CharacterFeature.Minion);
            _assets.Add(info);
            actor.SetCharacterInfoForTest(info);
            body.SetActive(true);
            return actor;
        }

        private GameObject CreateObject(string name, Vector3 position)
        {
            GameObject obj = new GameObject(name);
            SceneManager.MoveGameObjectToScene(obj, _room);
            obj.transform.position = position;
            return obj;
        }

        private BoxCollider CreateBox(string name, Vector3 position, Vector3 size, string layer)
        {
            GameObject obj = CreateObject(name, position);
            obj.layer = LayerMask.NameToLayer(layer);
            BoxCollider hit = obj.AddComponent<BoxCollider>();
            hit.size = size;
            return hit;
        }

        private static int Hp(CharacterActor3D actor) => GameManager.Data.GetVitals(actor.Actor.Hash).currentHp;

        private static void Set(Object instance, string field, object value)
        {
            FieldInfo info = instance.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(info, Is.Not.Null);
            info.SetValue(instance, value);
        }
    }
}
