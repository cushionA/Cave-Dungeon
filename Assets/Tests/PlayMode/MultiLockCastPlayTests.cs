using System.Collections;
using System.Collections.Generic;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.PlayMode
{
    public class MultiLockCastPlayTests
    {
        private const float k_Windup = 0.25f;
        private readonly List<Object> _objects = new List<Object>();
        private ProjectileManager _projectiles;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            GameObject world = new GameObject("MultiLockCastWorld");
            world.SetActive(false);
            GameObject shots = new GameObject("Projectiles");
            shots.transform.SetParent(world.transform, false);
            _projectiles = shots.AddComponent<ProjectileManager>();
            world.AddComponent<GameManager>();
            world.SetActive(true);
            GameManager.Instance.enabled = false;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_projectiles != null) { _projectiles.ClearAll(); }
            for (int i = _objects.Count - 1; i >= 0; i--)
            {
                Object.Destroy(_objects[i]);
            }
            _objects.Clear();
            yield return null;
            TestSceneHelper.Cleanup();
            yield return null;
        }

        [UnityTest]
        public IEnumerator MultiLockCast_EnemyShootsAtThreeActors_PairsEveryProjectileWithOneExplicitTarget()
        {
            return AssertCastTargets(CharacterBelong.Enemy, CharacterBelong.Ally, false, false);
        }

        [UnityTest]
        public IEnumerator MultiLockCast_AllyShootsAfterOneActorMovesBehindCover_OmitsOnlyThatActor()
        {
            return AssertCastTargets(CharacterBelong.Ally, CharacterBelong.Enemy, true, false);
        }

        [UnityTest]
        public IEnumerator MultiLockCast_EnemyShootsAfterAnActorReusesItsObject_OmitsTheNewGeneration()
        {
            return AssertCastTargets(CharacterBelong.Enemy, CharacterBelong.Ally, false, true);
        }

        private IEnumerator AssertCastTargets(CharacterBelong casterSide, CharacterBelong targetSide,
            bool hideSecond, bool recycleSecond)
        {
            BaseCharacter caster = CreateActor(casterSide, Vector2.zero);
            BaseCharacter first = CreateActor(targetSide, new Vector2(6f, 0f));
            BaseCharacter second = CreateActor(targetSide, new Vector2(4f, 1.5f));
            BaseCharacter third = CreateActor(targetSide, new Vector2(5f, -1.5f));
            CreateActor(targetSide, new Vector2(5.5f, 1.2f)); // Nearer than the primary, but outside the three marks.
            CreateActor(targetSide, new Vector2(-2f, 0f)); // Behind the caster's view.
            CreateActor(casterSide, new Vector2(2f, 0f)); // A friendly actor never acquires a mark.
            if (hideSecond) { CreateWall(new Vector2(3f, 3f), new Vector2(0.4f, 1.3f)); }
            AttackInfo attack = CreateAttack(k_Windup, BulletMoveType.Homing);
            ActionExecutorController controller = CreateExecutor(caster, attack);
            yield return null;

            ActorRef original = Actor(first);
            ActorRef oldSecond = Actor(second);
            Assert.IsTrue(GameManager.TargetRelations.SetTarget(Actor(caster), original));
            ActionDispatchResult result = Cast(controller, caster, first);
            Assert.IsTrue(result.Accepted);
            Assert.That(_projectiles.ActiveCount, Is.Zero);

            if (hideSecond)
            {
                second.SetPhysicsPosition(new Vector2(5f, 5f));
                second.PublishPositionForObservation();
            }
            if (recycleSecond)
            {
                second.OnPoolReturn();
                second.OnPoolAcquire();
                Assert.AreNotEqual(oldSecond, Actor(second));
            }
            GameManager.Actions.Execution.Tick(k_Windup);

            AssertShots(result.InstanceId, hideSecond || recycleSecond
                ? new[] { original, Actor(third) }
                : new[] { original, oldSecond, Actor(third) });
            Assert.IsTrue(GameManager.TargetRelations.TryGetTarget(Actor(caster), out ActorRef primary));
            Assert.AreEqual(original, primary);
        }

        [UnityTest]
        public IEnumerator MultiLockCast_TargetsDisappearOrExecutionIsInterrupted_ProducesNoProjectile()
        {
            BaseCharacter caster = CreateActor(CharacterBelong.Ally, Vector2.zero);
            BaseCharacter first = CreateActor(CharacterBelong.Enemy, new Vector2(4f, 0f));
            BaseCharacter second = CreateActor(CharacterBelong.Enemy, new Vector2(5f, 1.5f));
            AttackInfo attack = CreateAttack(k_Windup, BulletMoveType.Homing);
            ActionExecutorController controller = CreateExecutor(caster, attack);
            yield return null;

            ActionDispatchResult cancelled = Cast(controller, caster, first);
            Assert.IsTrue(cancelled.Accepted);
            Assert.IsTrue(GameManager.Actions.Execution.CancelGroup(cancelled.GroupId));
            GameManager.Actions.Execution.Tick(1f);
            Assert.That(_projectiles.ActiveCount, Is.Zero);

            ActionDispatchResult lost = Cast(controller, caster, first);
            Assert.IsTrue(lost.Accepted);
            GameManager.Data.GetVitals(first.ObjectHash).currentHp = 0;
            GameManager.Data.GetFlags(second.ObjectHash).Belong = CharacterBelong.Ally;
            GameManager.Actions.Execution.Tick(k_Windup);
            Assert.That(_projectiles.ActiveCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator MultiLockCast_InstantRangedAttack_ShootsAlongEachSelectedDirection()
        {
            BaseCharacter caster = CreateActor(CharacterBelong.Ally, Vector2.zero);
            BaseCharacter first = CreateActor(CharacterBelong.Enemy, new Vector2(4f, 1f));
            BaseCharacter second = CreateActor(CharacterBelong.Enemy, new Vector2(5f, -1f));
            AttackInfo attack = CreateAttack(0f, BulletMoveType.Straight);
            attack.category = AttackCategory.Ranged;
            ActionExecutorController controller = CreateExecutor(caster, attack);
            yield return null;

            ActionDispatchResult result = Cast(controller, caster, first, ActionExecType.Attack);

            Assert.IsTrue(result.Accepted);
            ActorRef firstRef = Actor(first);
            AssertShots(result.InstanceId, new[] { firstRef, Actor(second) });
            foreach (ProjectileController shot in _projectiles.GetComponentsInChildren<ProjectileController>())
            {
                Projectile core = shot.CoreProjectile;
                BaseCharacter target = core.TargetRef == firstRef ? first : second;
                Vector2 center = target.GetComponent<BoxCollider2D>().bounds.center;
                Vector2 expected = (center - core.Position).normalized;
                Assert.That(Vector2.Dot(expected, core.InitialDirection), Is.GreaterThan(0.999f));
            }
        }

        [UnityTest]
        public IEnumerator MultiLockCast_MeleeOnlyHitsValidLockedContacts_UsesPhysicalHitboxAndOneHitPerTarget()
        {
            BaseCharacter caster = CreateActor(CharacterBelong.Ally, Vector2.zero);
            BaseCharacter first = CreateActor(CharacterBelong.Enemy, new Vector2(3f, 0f), damageable: true);
            BaseCharacter second = CreateActor(CharacterBelong.Enemy, new Vector2(4f, 1f), damageable: true);
            BaseCharacter unmarked = CreateActor(CharacterBelong.Enemy, new Vector2(5f, -1f), damageable: true);
            GameObject contact = new GameObject("MeleeContact");
            contact.transform.SetParent(caster.transform, false);
            HitBox hitBox = contact.AddComponent<HitBox>();
            contact.GetComponent<BoxCollider2D>().size = new Vector2(3f, 2f);
            AttackInfo attack = CreateAttack(0f, BulletMoveType.Straight);
            attack.category = AttackCategory.Melee;
            attack.projectileInfo = default;
            ActionExecutorController controller = CreateExecutor(caster, attack);
            yield return null;

            Assert.IsTrue(controller.ExecuteMultiLock(attack, new[] { Actor(first), Actor(second) },
                1f, ActionVariant.Default, out long instanceId, out _));
            Assert.That(instanceId, Is.Positive);
            Assert.AreEqual(100, GameManager.Data.GetVitals(first.ObjectHash).currentHp);
            Assert.That(_projectiles.ActiveCount, Is.Zero);
            Assert.IsTrue(hitBox.IsActive);

            SimulationMode2D simulation = Physics2D.simulationMode;
            try
            {
                Physics2D.simulationMode = SimulationMode2D.Script;
                unmarked.SetPhysicsPosition(Vector2.up);
                first.SetPhysicsPosition(new Vector2(1.25f, 0f));
                SimulateMeleeContact();
                int firstHitHp = GameManager.Data.GetVitals(first.ObjectHash).currentHp;
                Assert.Less(firstHitHp, 100);

                first.SetPhysicsPosition(new Vector2(3f, 0f));
                SimulateMeleeContact();
                first.SetPhysicsPosition(new Vector2(1.25f, 0f));
                SimulateMeleeContact();
                Assert.AreEqual(firstHitHp, GameManager.Data.GetVitals(first.ObjectHash).currentHp);

                second.SetPhysicsPosition(new Vector2(-1.25f, 0f));
                SimulateMeleeContact();
            }
            finally
            {
                Physics2D.simulationMode = simulation;
            }
            Assert.AreEqual(100, GameManager.Data.GetVitals(unmarked.ObjectHash).currentHp);
            Assert.Less(GameManager.Data.GetVitals(second.ObjectHash).currentHp, 100);
            Assert.AreEqual(2, hitBox.HitCount);
        }

        private static void SimulateMeleeContact()
        {
            Physics2D.SyncTransforms();
            Assert.IsTrue(Physics2D.Simulate(1f / 60f));
        }

        private BaseCharacter CreateActor(CharacterBelong side, Vector2 position, bool damageable = false)
        {
            Game.Core.CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(side);
            info.name = "CastActor" + _objects.Count;
            _objects.Add(info);
            GameObject actor = damageable ? TestSceneHelper.CreateBaseCharacterObjectWithDamageReceiver(info, position)
                : TestSceneHelper.CreateBaseCharacterObject(info, position);
            _objects.Add(actor);
            BaseCharacter character = actor.GetComponent<BaseCharacter>();
            character.Rigidbody.gravityScale = 0f;
            return character;
        }

        private void CreateWall(Vector2 position, Vector2 size)
        {
            GameObject wall = new GameObject("MultiLockOccluder");
            _objects.Add(wall);
            wall.transform.position = position;
            wall.AddComponent<BoxCollider2D>().size = size;
        }

        private AttackInfo CreateAttack(float windup, BulletMoveType trajectory)
        {
            MotionDefinition motion = ScriptableObject.CreateInstance<MotionDefinition>();
            motion.info = new MotionInfo { preMotionDuration = windup, activeMotionDuration = 0.15f };
            _objects.Add(motion);
            AttackInfo attack = ScriptableObject.CreateInstance<AttackInfo>();
            _objects.Add(attack);
            attack.category = AttackCategory.Magic;
            attack.multiLockRange = 10f;
            attack.motion = motion;
            attack.damageMultiplier = 1f;
            attack.attackElement = Element.Slash;
            attack.projectileInfo = new ProjectileInfo
            {
                hasProjectile = true, trajectory = trajectory, speed = 8f, lifetime = 5f,
                projectileCount = 9, spreadAngle = 90f,
            };
            return attack;
        }

        private static ActionExecutorController CreateExecutor(BaseCharacter caster, AttackInfo attack)
        {
            ActionExecutorController controller = caster.gameObject.AddComponent<ActionExecutorController>();
            controller.SetAttackInfosForTest(new[] { attack });
            return controller;
        }

        private static ActionDispatchResult Cast(ActionExecutorController controller, BaseCharacter caster,
            BaseCharacter target, ActionExecType kind = ActionExecType.Cast)
        {
            ActionCommand command = ActionCommand.FromLegacySlot(new ActionSlot { execType = kind });
            return controller.ExecuteCommand(caster.ObjectHash, target.ObjectHash, command);
        }

        private static ActorRef Actor(BaseCharacter character)
        {
            Assert.IsTrue(GameManager.Actors.TryGet(character.ObjectHash, out ActorRef actor));
            return actor;
        }

        private void AssertShots(long instanceId, ActorRef[] expected)
        {
            Assert.AreEqual(expected.Length, _projectiles.ActiveCount);
            ProjectileController[] shots = _projectiles.GetComponentsInChildren<ProjectileController>();
            List<ActorRef> actual = new List<ActorRef>();
            foreach (ProjectileController shot in shots)
            {
                Projectile core = shot.CoreProjectile;
                Assert.AreEqual(instanceId, shot.Magic.actionInstanceId);
                Assert.IsTrue(shot.Magic.attackContext.IsCaptured);
                Assert.AreEqual(core.TargetRef, core.ExplicitTargetRef);
                actual.Add(core.TargetRef);
            }
            CollectionAssert.AreEqual(expected, actual);
        }
    }
}
