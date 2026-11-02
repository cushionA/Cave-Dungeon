using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using CharacterInfo = Game.Core.CharacterInfo;

namespace Game.Tests.PlayMode
{
    public class ProjectileTargetPolicyPlayTests
    {
        private readonly List<GameObject> _actors = new List<GameObject>();
        private readonly List<CharacterInfo> _infos = new List<CharacterInfo>();
        private ProjectileManager _projectiles;
        private BaseCharacter _owner;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            GameObject world = new GameObject("ProjectileTargetPolicyWorld");
            world.SetActive(false);
            GameObject projectileObject = new GameObject("Projectiles");
            projectileObject.transform.SetParent(world.transform, false);
            _projectiles = projectileObject.AddComponent<ProjectileManager>();
            typeof(ProjectileManager).GetField("_preWarmCount", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(_projectiles, 0);
            world.AddComponent<GameManager>();
            world.SetActive(true);
            GameManager.Instance.enabled = false;
            _owner = CreateActor(CharacterBelong.Ally, new Vector2(-10f, 0f));
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_projectiles != null) { _projectiles.ClearAll(); }
            for (int i = _actors.Count - 1; i >= 0; i--)
            {
                if (_actors[i] != null) { Object.Destroy(_actors[i]); }
            }
            _actors.Clear();
            yield return null;
            for (int i = _infos.Count - 1; i >= 0; i--)
            {
                if (_infos[i] != null) { Object.Destroy(_infos[i]); }
            }
            _infos.Clear();
            TestSceneHelper.Cleanup();
            yield return null;
        }

        private BaseCharacter CreateActor(CharacterBelong belong, Vector2 position)
        {
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(belong, maxHp: 1000);
            info.name = "ProjectilePolicyActor" + _infos.Count;
            _infos.Add(info);
            GameObject actor = TestSceneHelper.CreateBaseCharacterObjectWithDamageReceiver(info, position);
            _actors.Add(actor);
            BaseCharacter character = actor.GetComponent<BaseCharacter>();
            character.Rigidbody.gravityScale = 0f;
            return character;
        }

        private static MagicDefinition Magic(AttackTargetRelation relations,
            MagicType type = MagicType.Attack, float speed = 0f, int hitLimit = 2)
        {
            return new MagicDefinition
            {
                magicType = type,
                targetPolicy = new AttackTargetPolicy { overrideTargets = true, targets = relations },
                motionValue = 1f,
                attackElement = Element.Slash,
                healAmount = 25,
                bulletProfile = new BulletProfile
                {
                    moveType = BulletMoveType.Straight,
                    speed = speed,
                    lifeTime = 5f,
                    hitLimit = hitLimit,
                    perTargetHitLimit = 1,
                },
            };
        }

        private ProjectileController Spawn(MagicDefinition magic, Vector2 position = default, ActorRef target = default)
        {
            Assert.IsTrue(GameManager.Actors.TryGet(_owner.ObjectHash, out ActorRef owner));
            ProjectileController projectile = _projectiles.SpawnProjectile(owner, magic, position, Vector2.right, target);
            Assert.IsNotNull(projectile);
            return projectile;
        }

        private static void Contact(ProjectileController projectile, BaseCharacter target)
        {
            projectile.gameObject.SendMessage("OnTriggerEnter2D", target.GetComponent<BoxCollider2D>(),
                SendMessageOptions.RequireReceiver);
        }

        private static int Hp(BaseCharacter actor) => GameManager.Data.GetVitals(actor.ObjectHash).currentHp;

        [UnityTest]
        public IEnumerator ProjectilePolicy_RejectedDirectAndSweptFriend_PreservesBudgetAndEffectsUntilEnemy()
        {
            BaseCharacter friend = CreateActor(CharacterBelong.Ally, new Vector2(2f, 0f));
            BaseCharacter explosionWitness = CreateActor(CharacterBelong.Enemy, new Vector2(2f, 1.5f));
            BaseCharacter enemy = CreateActor(CharacterBelong.Enemy, new Vector2(6f, 0f));
            yield return null;
            Physics2D.SyncTransforms();
            MagicDefinition magic = Magic(AttackTargetRelation.Hostile, speed: 1000f, hitLimit: 1);
            magic.bulletProfile.features = BulletFeature.Explode;
            magic.bulletProfile.explodeRadius = 2f;
            magic.childBullet = new ChildBulletConfig
            {
                trigger = ChildBulletTrigger.OnHit,
                count = 2,
                profile = new BulletProfile { moveType = BulletMoveType.Set, lifeTime = 5f, hitLimit = 1 },
            };
            ProjectileController projectile = Spawn(magic);

            Contact(projectile, friend);

            Assert.AreEqual(1, projectile.CoreProjectile.RemainingHits);
            Assert.AreEqual(0, projectile.GetHitCountForTarget(friend.ObjectHash));
            Assert.AreEqual(1000, Hp(friend));
            Assert.AreEqual(1000, Hp(explosionWitness), "A rejected contact must not trigger an explosion.");
            Assert.AreEqual(1, _projectiles.ActiveCount, "A rejected contact must not emit OnHit children.");

            _projectiles.SimulationTick(0.003f);

            Assert.AreEqual(1, projectile.CoreProjectile.RemainingHits);
            Assert.AreEqual(0, projectile.GetHitCountForTarget(friend.ObjectHash));
            Assert.AreEqual(1000, Hp(friend));
            Assert.AreEqual(1000, Hp(explosionWitness));
            Assert.AreEqual(1, _projectiles.ActiveCount);

            _projectiles.SimulationTick(0.005f);

            Assert.Less(Hp(enemy), 1000);
            Assert.AreEqual(1000, Hp(friend));
            Assert.AreEqual(1000, Hp(explosionWitness));
            Assert.AreEqual(2, _projectiles.ActiveCount, "The eligible enemy contact emits exactly two children.");
        }

        [UnityTest]
        public IEnumerator ProjectilePolicy_ExplosionAllowsSelfAndHostile_ExcludesOtherFriendlyActors()
        {
            BaseCharacter friend = CreateActor(CharacterBelong.Ally, new Vector2(-9f, 0f));
            BaseCharacter enemy = CreateActor(CharacterBelong.Enemy, new Vector2(-8f, 0f));
            yield return null;
            MagicDefinition magic = Magic(AttackTargetRelation.Self | AttackTargetRelation.Hostile);
            magic.bulletProfile.explodeRadius = 3f;
            ProjectileController projectile = Spawn(magic, new Vector2(-10f, 0f));

            _projectiles.ProcessExplosion(projectile.CoreProjectile, projectile.Magic);

            Assert.Less(Hp(_owner), 1000);
            Assert.AreEqual(1000, Hp(friend));
            Assert.Less(Hp(enemy), 1000);
            Assert.AreEqual(2, projectile.CoreProjectile.RemainingHits);
        }

        [UnityTest]
        public IEnumerator ProjectilePolicy_ExplicitHomingTargetRejected_FallsBackToEligibleTarget()
        {
            BaseCharacter friend = CreateActor(CharacterBelong.Ally, new Vector2(2f, 0f));
            BaseCharacter enemy = CreateActor(CharacterBelong.Enemy, new Vector2(4f, 3f));
            yield return null;
            MagicDefinition magic = Magic(AttackTargetRelation.Hostile, speed: 5f);
            magic.bulletProfile.moveType = BulletMoveType.Homing;
            Assert.IsTrue(GameManager.Actors.TryGet(friend.ObjectHash, out ActorRef rejected));
            ProjectileController projectile = Spawn(magic, target: rejected);

            _projectiles.SimulationTick(0.01f);

            Assert.AreNotEqual(rejected, projectile.CoreProjectile.TargetRef);
            Assert.AreEqual(GameManager.Data.GetVitals(enemy.ObjectHash).position,
                projectile.CoreProjectile.TargetPosition);
            Assert.AreEqual(2, projectile.CoreProjectile.RemainingHits);
        }

        [UnityTest]
        public IEnumerator ProjectilePolicy_AutomaticHoming_UsesExplicitFriendlyPolicyInsteadOfEnemySearch()
        {
            BaseCharacter friend = CreateActor(CharacterBelong.Ally, new Vector2(3f, 2f));
            CreateActor(CharacterBelong.Enemy, new Vector2(3f, 0f));
            yield return null;
            MagicDefinition magic = Magic(AttackTargetRelation.Friendly, speed: 5f);
            magic.bulletProfile.moveType = BulletMoveType.Homing;
            ProjectileController projectile = Spawn(magic);

            _projectiles.SimulationTick(0.01f);

            Assert.AreEqual(GameManager.Data.GetVitals(friend.ObjectHash).position,
                projectile.CoreProjectile.TargetPosition);
            Assert.AreEqual(2, projectile.CoreProjectile.RemainingHits);
        }

        [UnityTest]
        public IEnumerator ProjectilePolicy_RecoverSelf_HealsCasterAndRejectsOtherFriendlyActor()
        {
            BaseCharacter friend = CreateActor(CharacterBelong.Ally, new Vector2(-9f, 0f));
            yield return null;
            GameManager.Data.GetVitals(_owner.ObjectHash).currentHp = 60;
            GameManager.Data.GetVitals(friend.ObjectHash).currentHp = 40;
            ProjectileController projectile = Spawn(Magic(AttackTargetRelation.Self, MagicType.Recover, hitLimit: 1),
                new Vector2(-10f, 0f));

            Contact(projectile, friend);

            Assert.AreEqual(40, Hp(friend));
            Assert.AreEqual(1, projectile.CoreProjectile.RemainingHits);
            Contact(projectile, _owner);
            Assert.AreEqual(85, Hp(_owner));
            Assert.AreEqual(0, projectile.CoreProjectile.RemainingHits);
            _projectiles.SimulationTick(0f);
            Assert.AreEqual(0, _projectiles.ActiveCount);
        }

        [UnityTest]
        public IEnumerator ProjectilePolicy_SourceChangesFaction_UsesCapturedSourceAndCurrentTargetFactions()
        {
            BaseCharacter converted = CreateActor(CharacterBelong.Enemy, new Vector2(2f, 0f));
            BaseCharacter enemy = CreateActor(CharacterBelong.Enemy, new Vector2(5f, 0f));
            yield return null;
            ProjectileController projectile = Spawn(Magic(AttackTargetRelation.Hostile));
            GameManager.Data.GetFlags(_owner.ObjectHash).Belong = CharacterBelong.Enemy;
            GameManager.Data.GetFlags(converted.ObjectHash).Belong = CharacterBelong.Ally;

            Contact(projectile, converted);

            Assert.AreEqual(1000, Hp(converted));
            Assert.AreEqual(0, projectile.GetHitCountForTarget(converted.ObjectHash));
            Assert.AreEqual(2, projectile.CoreProjectile.RemainingHits);
            Contact(projectile, enemy);
            Assert.Less(Hp(enemy), 1000);
            Assert.AreEqual(1, projectile.CoreProjectile.RemainingHits);
            Assert.AreEqual(CharacterBelong.Ally, projectile.Magic.attackContext.Belong);
        }

        [UnityTest]
        public IEnumerator ProjectilePolicy_CasterGameObjectReused_DoesNotTreatNewGenerationAsSelf()
        {
            ProjectileController projectile = Spawn(Magic(AttackTargetRelation.Self, MagicType.Recover));
            ActorRef original = projectile.Magic.attackContext.Owner;
            _owner.OnPoolReturn();
            _owner.OnPoolAcquire();
            Assert.IsTrue(GameManager.Actors.TryGet(_owner.ObjectHash, out ActorRef replacement));
            Assert.AreEqual(original.Hash, replacement.Hash);
            Assert.AreNotEqual(original, replacement);
            GameManager.Data.GetVitals(_owner.ObjectHash).currentHp = 50;

            Contact(projectile, _owner);

            Assert.AreEqual(50, Hp(_owner));
            Assert.AreEqual(0, projectile.GetHitCountForTarget(_owner.ObjectHash));
            Assert.AreEqual(2, projectile.CoreProjectile.RemainingHits);
            Assert.AreEqual(original, projectile.Magic.attackContext.Owner);
            yield break;
        }

        [UnityTest]
        public IEnumerator ProjectilePolicy_SupportMagic_UsesExplicitFriendlyOrHostileReceiverPolicy()
        {
            BaseCharacter friend = CreateActor(CharacterBelong.Ally, new Vector2(2f, 0f));
            BaseCharacter enemy = CreateActor(CharacterBelong.Enemy, new Vector2(5f, 0f));
            yield return null;
            StatusEffectApplicationData[] applications =
            {
                new StatusEffectApplicationData
                {
                    info = new StatusEffectInfo { effect = StatusEffectId.DefenseUp, duration = 5f, modifier = 0.25f },
                    mode = StatusEffectApplicationMode.Immediate,
                },
            };
            MagicDefinition friendlyMagic = Magic(AttackTargetRelation.Friendly, MagicType.Support);
            friendlyMagic.statusEffects = applications;
            MagicDefinition hostileMagic = Magic(AttackTargetRelation.Hostile, MagicType.Support);
            hostileMagic.statusEffects = applications;
            ProjectileController friendly = Spawn(friendlyMagic);
            ProjectileController hostile = Spawn(hostileMagic);

            Contact(friendly, enemy);
            Contact(hostile, friend);

            Assert.AreEqual(2, friendly.CoreProjectile.RemainingHits);
            Assert.AreEqual(2, hostile.CoreProjectile.RemainingHits);
            Assert.AreEqual(0, friendly.GetHitCountForTarget(enemy.ObjectHash));
            Assert.AreEqual(0, hostile.GetHitCountForTarget(friend.ObjectHash));
            Assert.AreEqual(1f, GameManager.Data.GetStatusEffects(friend.ObjectHash).DefenseMultiplier);
            Assert.AreEqual(1f, GameManager.Data.GetStatusEffects(enemy.ObjectHash).DefenseMultiplier);
            Contact(friendly, friend);
            Contact(hostile, enemy);
            Assert.AreEqual(1, friendly.CoreProjectile.RemainingHits);
            Assert.AreEqual(1, hostile.CoreProjectile.RemainingHits);
            Assert.AreEqual(1, friendly.GetHitCountForTarget(friend.ObjectHash));
            Assert.AreEqual(1, hostile.GetHitCountForTarget(enemy.ObjectHash));
            Assert.AreEqual(1.25f, GameManager.Data.GetStatusEffects(friend.ObjectHash).DefenseMultiplier);
            Assert.AreEqual(1.25f, GameManager.Data.GetStatusEffects(enemy.ObjectHash).DefenseMultiplier);
        }
    }
}
