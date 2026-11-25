using System.Collections;
using System.Collections.Generic;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests.PlayMode
{
    public class GroundAndMeleeWarningPlayTests
    {
        private readonly List<Object> _objects = new();
        private ProjectileManager _projectiles;
        private BaseCharacter _caster;
        private BaseCharacter _knight;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            GameObject world = new GameObject("AttackWarningWorld");
            world.SetActive(false);
            GameObject projectileObject = new GameObject("Projectiles");
            projectileObject.transform.SetParent(world.transform, false);
            _projectiles = projectileObject.AddComponent<ProjectileManager>();
            world.AddComponent<GameManager>();
            world.SetActive(true);
            GameManager.Instance.enabled = false;
            _caster = CreateActor(CharacterBelong.Enemy, Vector2.zero);
            _knight = CreateActor(CharacterBelong.Ally, new Vector2(5f, -2f));
            CharacterRegistry.RegisterPlayer(_knight.ObjectHash);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (Object instance in _objects)
            {
                if (instance != null) { Object.Destroy(instance); }
            }
            _objects.Clear();
            TestSceneHelper.Cleanup();
            yield return null;
        }

        [UnityTest]
        public IEnumerator GroundWarning_WhenOriginalTargetMoves_AppliesBlastOnlyToTheMarkedCircle()
        {
            BaseCharacter sister = CreateActor(CharacterBelong.Ally, new Vector2(5f, 0f));
            BaseCharacter entering = CreateActor(CharacterBelong.Ally, new Vector2(8f, 0f));
            yield return null; // DamageReceiver.Start binds characters added after its Awake.
            AttackInfo attack = CreateAttack();
            attack.projectileInfo = new ProjectileInfo
            {
                hasProjectile = true, speed = 10f, projectileCount = 1, aoeRadius = 1.25f, groundImpactRange = 10f,
            };

            Assert.IsTrue(GameManager.Actions.Execute(_caster.ObjectHash, attack, sister.ObjectHash, Vector2.right, out _));
            SpriteRenderer warning = FindWarning();
            Assert.IsNotNull(warning);
            Assert.That((Vector2)warning.transform.position, Is.EqualTo(new Vector2(5f, 0f)));
            Assert.That(warning.bounds.size.x, Is.EqualTo(2.5f).Within(0.01f));
            Assert.AreEqual(0, _projectiles.ActiveCount);

            Move(sister, new Vector2(8f, 0f));
            Move(entering, new Vector2(5f, 0.8f));
            GameManager.Actions.Execution.Tick(0.2f);
            _projectiles.SimulationTick(0.25f);
            Assert.IsNotNull(FindWarning());
            Assert.AreEqual(1, _projectiles.ActiveCount);
            Assert.AreEqual(0, CollectKnightLockWarnings(), "The area warning is independent of the knight's lock.");
            GameManager.Actions.Execution.Tick(0.5f);
            Assert.IsNotNull(FindWarning(), "The launched effect owns its warning after the cast finishes.");
            _projectiles.SimulationTick(0.3f);
            Assert.IsNull(FindWarning());
            Assert.AreEqual(0, _projectiles.ActiveCount);
            Assert.AreEqual(1000, Hp(_knight));
            Assert.AreEqual(1000, Hp(sister));
            Assert.Less(Hp(entering), 1000);
            yield return null;
        }

        [UnityTest]
        public IEnumerator GroundWarning_WhenKnightIsLocked_AlsoWarnsAtTheScreenEdgeUntilTheProjectileIsCleared()
        {
            AttackInfo attack = CreateAttack();
            attack.projectileInfo = new ProjectileInfo
            {
                hasProjectile = true, speed = 10f, projectileCount = 1, aoeRadius = 1f, groundImpactRange = 10f,
            };

            Assert.IsTrue(GameManager.Actions.Execute(_caster.ObjectHash, attack, _knight.ObjectHash, Vector2.right, out _));
            Assert.IsNotNull(FindWarning());
            Assert.AreEqual(0, CollectKnightLockWarnings());
            GameManager.Actions.Execution.Tick(0.2f);
            Assert.IsNotNull(FindWarning());
            Assert.AreEqual(1, CollectKnightLockWarnings());

            _projectiles.ClearAll();
            Assert.IsNull(FindWarning());
            Assert.AreEqual(0, CollectKnightLockWarnings());
            Assert.AreEqual(1000, Hp(_knight));
            yield return null;
        }

        [UnityTest]
        public IEnumerator GroundWarning_WhenLockedActorReappears_UsesTheCurrentOccupantAtImpact()
        {
            BaseCharacter sister = CreateActor(CharacterBelong.Ally, new Vector2(5f, 0f));
            yield return null;
            AttackInfo attack = CreateAttack();
            attack.projectileInfo = new ProjectileInfo
            {
                hasProjectile = true, speed = 10f, projectileCount = 1, aoeRadius = 1f, groundImpactRange = 10f,
            };
            Assert.IsTrue(GameManager.Actors.TryGet(sister.ObjectHash, out ActorRef original));
            Assert.IsTrue(GameManager.Actions.Execute(_caster.ObjectHash, attack, sister.ObjectHash, Vector2.right, out _));
            GameManager.Actions.Execution.Tick(0.2f);
            Assert.AreEqual(1, _projectiles.ActiveCount);

            sister.OnPoolReturn();
            sister.OnPoolAcquire();
            Assert.IsFalse(GameManager.Actors.IsCurrent(original));
            Move(sister, new Vector2(5f, 0f));
            _projectiles.SimulationTick(0.55f);
            Assert.Less(Hp(sister), 1000);
            Assert.IsNull(FindWarning());
            yield return null;
        }

        [UnityTest]
        public IEnumerator GroundWarning_WhenUntargetedAndInterrupted_ClearsWithoutRelease()
        {
            AttackInfo attack = CreateAttack();
            attack.projectileInfo = new ProjectileInfo
            {
                hasProjectile = true, speed = 10f, projectileCount = 1, aoeRadius = 1f, groundImpactRange = 4f,
            };

            Assert.IsTrue(GameManager.Actions.Execute(_caster.ObjectHash, attack, 0, Vector2.right, out long execution));
            Assert.That((Vector2)FindWarning().transform.position, Is.EqualTo(new Vector2(4f, 0f)));
            Assert.IsTrue(GameManager.Actions.Execution.Cancel(execution, true));
            Assert.IsNull(FindWarning());
            GameManager.Actions.Execution.Tick(0.5f);
            Assert.AreEqual(0, _projectiles.ActiveCount);
            yield return null;
        }

        [UnityTest]
        public IEnumerator MeleeWarning_WhenHostileAttackIsGuardImmune_UsesAudioDuringAnticipationOnly()
        {
            AttackInfo ordinary = CreateAttack();
            AttackInfo danger = CreateAttack();
            danger.feature = AttackFeature.JustGuardImmune;
            Assert.IsTrue(GameManager.Actions.Execute(_caster.ObjectHash, ordinary, _knight.ObjectHash, Vector2.right, out long normal));
            Assert.IsNull(FindWarningAudio());
            Assert.IsTrue(GameManager.Actions.Execution.Cancel(normal, true));

            Assert.IsTrue(GameManager.Actions.Execute(_caster.ObjectHash, danger, _knight.ObjectHash, Vector2.right, out _));
            AudioSource warning = FindWarningAudio();
            Assert.IsNotNull(warning);
            Assert.IsNotNull(warning.clip);
            Assert.Greater(warning.clip.samples, 0);
            float[] samples = new float[warning.clip.samples];
            Assert.IsTrue(warning.clip.GetData(samples, 0));
            Assert.IsTrue(System.Array.Exists(samples, sample => Mathf.Abs(sample) > 0.1f));
            Assert.IsNull(FindWarning());
            Assert.AreEqual(0, CollectKnightLockWarnings());
            GameManager.Actions.Execution.Tick(0.2f);
            Assert.AreSame(warning, FindWarningAudio());
            yield return null;
        }

        private BaseCharacter CreateActor(CharacterBelong belong, Vector2 position)
        {
            Game.Core.CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(belong, maxHp: 1000);
            info.baseAttack = new ElementalStatus { slash = 100 };
            GameObject gameObject = TestSceneHelper.CreateBaseCharacterObjectWithDamageReceiver(info, position);
            _objects.Add(gameObject);
            _objects.Add(info);
            BaseCharacter character = gameObject.GetComponent<BaseCharacter>();
            character.Rigidbody.gravityScale = 0f;
            character.OnPoolAcquire();
            character.PublishPositionForObservation();
            return character;
        }

        private AttackInfo CreateAttack()
        {
            MotionDefinition motion = ScriptableObject.CreateInstance<MotionDefinition>();
            motion.info = new MotionInfo { preMotionDuration = 0.15f, activeMotionDuration = 0.2f, recoveryDuration = 0.1f };
            AttackInfo attack = ScriptableObject.CreateInstance<AttackInfo>();
            attack.motion = motion;
            attack.damageMultiplier = 2f;
            attack.attackElement = Element.Slash;
            _objects.Add(attack);
            _objects.Add(motion);
            return attack;
        }

        private static void Move(BaseCharacter character, Vector2 position)
        {
            character.SetPhysicsPosition(position);
            character.PublishPositionForObservation();
        }

        private static int Hp(BaseCharacter character) => GameManager.Data.GetVitals(character.ObjectHash).currentHp;

        private static SpriteRenderer FindWarning()
        {
            GameObject warning = GameObject.Find("[Ground Impact Warning]");
            return warning != null ? warning.GetComponent<SpriteRenderer>() : null;
        }

        private int CollectKnightLockWarnings()
        {
            Assert.IsTrue(GameManager.Actors.TryGet(_knight.ObjectHash, out ActorRef target));
            List<ProjectileLockWarning> warnings = new();
            _projectiles.CollectLockWarnings(target, warnings);
            return warnings.Count;
        }

        private static AudioSource FindWarningAudio()
        {
            Transform warning = GameManager.Instance.transform.Find("[Dangerous Melee Warning]");
            return warning != null ? warning.GetComponent<AudioSource>() : null;
        }
    }
}
