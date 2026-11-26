using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using R3;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests.PlayMode
{
    public sealed class ExplosiveProp2DPlayTests
    {
        private static readonly Vector2 s_Origin = new Vector2(130f, 0.5f);
        private readonly List<ScriptableObject> _assets = new();
        private Scene _previous;
        private Scene _room;
        private Scene _foreign;
        private SimulationMode2D _previousSimulation;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            _previous = SceneManager.GetActiveScene();
            _room = SceneManager.CreateScene(nameof(ExplosiveProp2DPlayTests));
            SceneManager.SetActiveScene(_room);
            _previousSimulation = Physics2D.simulationMode;
            Physics2D.simulationMode = SimulationMode2D.Script;
            var manager = new GameObject("TestGameManager");
            manager.SetActive(false);
            var projectiles = new GameObject("Projectiles");
            projectiles.transform.SetParent(manager.transform, false);
            projectiles.AddComponent<ProjectileManager>();
            manager.AddComponent<GameManager>();
            manager.SetActive(true);
            GameManager.Instance.enabled = false;
            GameObject ground = new GameObject("Ground");
            ground.layer = LayerMask.NameToLayer("Ground");
            ground.transform.position = s_Origin + Vector2.down;
            ground.AddComponent<BoxCollider2D>().size = new Vector2(12f, 1f);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Physics2D.simulationMode = _previousSimulation;
            if (_previous.isLoaded) { SceneManager.SetActiveScene(_previous); }
            if (_room.isLoaded) { yield return SceneManager.UnloadSceneAsync(_room); }
            if (_foreign.isLoaded) { yield return SceneManager.UnloadSceneAsync(_foreign); }
            foreach (ScriptableObject asset in _assets) { Object.Destroy(asset); }
            _assets.Clear();
            TestSceneHelper.Cleanup();
            yield return null;
        }

        [UnityTest]
        public IEnumerator ProjectileSweep_ConsumesTheClosestImpactAndDetonatesOnlyAttackMagic()
        {
            ProjectileManager projectiles = MakeProjectileManager(_room);
            BaseCharacter caster = MakeVictim("Caster", Vector2.left * 4f, CharacterBelong.Ally);
            BaseCharacter blocker = MakeVictim("Blocking enemy", Vector2.left * 0.5f, CharacterBelong.Enemy);
            BaseCharacter next = MakeVictim("Enemy beyond prop", new Vector2(2.05f, 0f), CharacterBelong.Enemy);
            ExplosiveProp2D prop = MakeProp(out BoxCollider2D hit);
            int detonations = 0;
            prop.OnDetonated.AddListener(() => detonations++);
            yield return null;
            int blockerHp = Hp(blocker);
            int nextHp = Hp(next);

            Assert.IsNotNull(Shoot(projectiles, caster));
            projectiles.SimulationTick(0.05f);
            Assert.Less(Hp(blocker), blockerHp);
            Assert.AreEqual(nextHp, Hp(next));
            Assert.IsFalse(prop.HasDetonated);
            Assert.AreEqual(0, projectiles.ActiveCount);

            blocker.gameObject.SetActive(false);
            Assert.IsNotNull(Shoot(projectiles, caster, MagicType.Recover));
            projectiles.SimulationTick(0.05f);
            Assert.IsFalse(prop.HasDetonated);
            projectiles.ClearAll();

            Assert.IsNotNull(Shoot(projectiles, caster));
            projectiles.SimulationTick(0.05f);
            Assert.IsTrue(prop.HasDetonated);
            Assert.IsFalse(hit.enabled);
            Assert.AreEqual(1, detonations);
            Assert.Less(Hp(next), nextHp);
            Assert.AreEqual(0, projectiles.ActiveCount);
            StepPhysics();
            Assert.AreEqual(1, detonations);
        }

        [UnityTest]
        public IEnumerator ProjectilePropImpact_RemainsInItsPhysicsWorldAndCannotAffectAReturnedLease()
        {
            _foreign = SceneManager.CreateScene("Isolated projectile world", new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            ProjectileManager projectiles = MakeProjectileManager(_foreign);
            BaseCharacter caster = MakeVictim("Caster", Vector2.left * 4f, CharacterBelong.Ally, _foreign);
            BaseCharacter local = MakeVictim("Same-world enemy", new Vector2(2.05f, 0f), CharacterBelong.Enemy, _foreign);
            BaseCharacter other = MakeVictim("Other-world enemy", new Vector2(2.05f, 0f), CharacterBelong.Enemy);
            ExplosiveProp2D otherProp = MakeProp(out _);
            ExplosiveProp2D localProp = MakeProp(out _);
            SceneManager.MoveGameObjectToScene(localProp.gameObject, _foreign);
            yield return null;
            int localHp = Hp(local);
            int otherHp = Hp(other);
            GameManager.Data.GetVitals(local.ObjectHash).position = local.PhysicsPosition;
            GameManager.Data.GetVitals(other.ObjectHash).position = other.PhysicsPosition;
            int sameWorldDamage = 0;
            using var receiver = GameManager.Events.OnDamageDealt.Subscribe(change =>
            {
                if (change.result.defenderRef.Hash == local.ObjectHash) { sameWorldDamage++; }
            });

            Assert.IsNotNull(Shoot(projectiles, caster, features: BulletFeature.Explode));
            projectiles.SimulationTick(0.05f);
            Assert.IsTrue(localProp.HasDetonated);
            Assert.IsFalse(otherProp.HasDetonated);
            Assert.AreEqual(2, sameWorldDamage, "The prop and the projectile impact each damage the local actor.");
            Assert.Less(Hp(local), localHp);
            Assert.AreEqual(otherHp, Hp(other));

            localProp = MakeProp(out _);
            SceneManager.MoveGameObjectToScene(localProp.gameObject, _foreign);
            ProjectileController replacement = null;
            localProp.OnDetonated.AddListener(() =>
            {
                projectiles.ClearAll();
                replacement = Shoot(projectiles, caster);
            });
            ProjectileController former = Shoot(projectiles, caster, features: BulletFeature.Explode);
            ulong lease = former.CoreProjectile.LeaseId;
            projectiles.SimulationTick(0.05f);
            Assert.IsTrue(localProp.HasDetonated);
            Assert.IsNotNull(replacement);
            Assert.AreNotEqual(lease, replacement.CoreProjectile.LeaseId);
            Assert.AreEqual(s_Origin + Vector2.left * 2.5f, replacement.CoreProjectile.Position);
            Assert.AreEqual(3, sameWorldDamage, "Effects from the old projectile stop after pool reuse.");
            Assert.AreEqual(1, projectiles.ActiveCount);
        }

        [UnityTest]
        public IEnumerator ProjectilePropImpact_RequiresTheOriginalCasterWorldForActionAndChildShots()
        {
            _foreign = SceneManager.CreateScene("Isolated shooter world", new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            BaseCharacter caster = MakeVictim("Isolated caster", Vector2.left * 2.5f, CharacterBelong.Ally, _foreign);
            ExplosiveProp2D prop = MakeProp(out _);
            ProjectileManager projectiles = GameManager.Projectiles;
            yield return null;

            GameManager.Data.GetVitals(caster.ObjectHash).position = caster.PhysicsPosition;
            Assert.AreNotEqual(caster.gameObject.scene.GetPhysicsScene2D(), projectiles.gameObject.scene.GetPhysicsScene2D());
            Assert.IsTrue(GameManager.Actions.Execute(caster.ObjectHash, MakeShot(), 0, Vector2.right, out _));
            Assert.AreEqual(1, projectiles.ActiveCount);
            projectiles.SimulationTick(0.08f);
            Assert.IsFalse(prop.HasDetonated, "The global projectile manager must preserve the action's original physics world.");
            projectiles.ClearAll();

            ProjectileController parent = projectiles.SpawnProjectile(caster.ObjectHash, new MagicDefinition
            {
                magicType = MagicType.Attack,
                bulletProfile = new BulletProfile { moveType = BulletMoveType.Straight, lifeTime = 0.05f, hitLimit = 1 },
                childBullet = new ChildBulletConfig
                {
                    trigger = ChildBulletTrigger.OnDestroy,
                    count = 1,
                    profile = new BulletProfile { moveType = BulletMoveType.Straight, speed = 160f, lifeTime = 1f, hitLimit = 1 },
                },
            }, caster.PhysicsPosition, Vector2.right);
            Assert.IsNotNull(parent);
            SceneManager.MoveGameObjectToScene(caster.gameObject, _room);
            projectiles.SimulationTick(0.1f);
            Assert.AreEqual(1, projectiles.ActiveCount, "The expired shot emitted its child.");
            projectiles.SimulationTick(0.05f);
            Assert.IsFalse(prop.HasDetonated, "A child must inherit the launch world before the caster moved.");

            projectiles.ClearAll();
            Assert.IsNotNull(Shoot(projectiles, caster));
            projectiles.SimulationTick(0.05f);
            Assert.IsTrue(prop.HasDetonated, "A new shot from the same-world caster can detonate the prop.");
        }

        private static ProjectileManager MakeProjectileManager(Scene scene)
        {
            GameObject root = new GameObject("Projectile manager");
            SceneManager.MoveGameObjectToScene(root, scene);
            ProjectileManager projectiles = root.AddComponent<ProjectileManager>();
            projectiles.Initialize();
            return projectiles;
        }

        private static ProjectileController Shoot(ProjectileManager projectiles, BaseCharacter caster,
            MagicType type = MagicType.Attack, BulletFeature features = BulletFeature.None)
        {
            return projectiles.SpawnProjectile(caster.ObjectHash, new MagicDefinition
            {
                magicType = type,
                motionValue = 1f,
                attackElement = Element.Slash,
                bulletProfile = new BulletProfile
                {
                    moveType = BulletMoveType.Straight,
                    speed = 160f,
                    lifeTime = 3f,
                    hitLimit = 1,
                    features = features,
                    explodeRadius = 2.25f,
                },
            }, s_Origin + Vector2.left * 2.5f, Vector2.right);
        }

        [UnityTest]
        public IEnumerator PlayerMelee_OnlyAnActiveStrikeOnTheConfiguredCollider_DetonatesOnceInItsWorld()
        {
            AttackInfo strike = MakeAttack();
            GameObject knightObject = MakeBody("Knight", Vector2.zero);
            AddWeaponCollider<HitBox>(knightObject);
            var input = knightObject.AddComponent<PlayerInputHandler>();
            ActionExecutorController executor = knightObject.AddComponent<ActionExecutorController>();
            PlayerCharacter knight = AddActor<PlayerCharacter>(knightObject, CharacterBelong.Ally, CharacterFeature.Player);
            executor.SetAttackInfosForTest(new[] { strike });
            knight.SetLightAttacksForTest(new[] { strike });
            knightObject.SetActive(true);

            BaseCharacter enemy = MakeVictim("Nearby enemy", new Vector2(2.05f, 0f), CharacterBelong.Enemy);
            BaseCharacter friend = MakeVictim("Nearby ally", new Vector2(2.2f, 1.05f), CharacterBelong.Ally);
            BaseCharacter distant = MakeVictim("Distant enemy", new Vector2(4.5f, 0f), CharacterBelong.Enemy);
            _foreign = SceneManager.CreateScene("Separate physics world", new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            BaseCharacter foreign = MakeVictim("Other-world enemy", new Vector2(2.05f, 0f), CharacterBelong.Enemy, _foreign);
            ExplosiveProp2D prop = MakeProp(out BoxCollider2D hit);
            hit.offset = Vector2.right;
            BoxCollider2D decoy = prop.gameObject.AddComponent<BoxCollider2D>();
            decoy.isTrigger = true;
            decoy.size = hit.size;
            int detonations = 0;
            prop.OnDetonated.AddListener(() => detonations++);
            yield return null;

            Assert.IsTrue(GameManager.Actors.TryGet(knight.ObjectHash, out ActorRef striker));
            Assert.IsTrue(GameManager.Actors.TryGet(enemy.ObjectHash, out ActorRef victim));
            int damageEvents = 0;
            long damageAction = -1;
            using var listener = GameManager.Events.OnDamageDealt.Subscribe(change =>
            {
                if (change.result.attackerRef != striker || change.result.defenderRef != victim) { return; }
                damageAction = change.result.actionInstanceId;
                damageEvents++;
            });

            int startingHp = Hp(enemy);
            input.SetOverrideInput(new MovementInfo { attackInput = AttackInputType.LightAttack });
            GameManager.Instance.SimulationTick(0.02f, 1f);
            Assert.IsTrue(GameManager.Actions.Execution.GetPrimary(knight.ObjectHash, out ActionExecution windup));
            Assert.AreEqual(AnimationPhase.Anticipation, windup.phase);
            StepPhysics();
            Assert.IsFalse(prop.HasDetonated);
            Assert.AreEqual(startingHp, Hp(enemy));

            GameManager.Instance.SimulationTick(0.12f, 1.12f);
            Assert.IsTrue(GameManager.Actions.Execution.TryGetInstance(windup.instanceId, out ActionExecution active));
            Assert.AreEqual(AnimationPhase.Active, active.phase);
            StepPhysics();
            Assert.IsFalse(prop.HasDetonated, "Touching the decoy collider cannot spend the prop.");
            hit.offset = Vector2.zero;
            StepPhysics();
            Assert.IsTrue(prop.HasDetonated);
            Assert.IsFalse(hit.enabled);
            Assert.Less(Hp(enemy), startingHp);
            Assert.AreEqual(startingHp, Hp(knight));
            Assert.AreEqual(startingHp, Hp(friend));
            Assert.AreEqual(startingHp, Hp(distant));
            Assert.AreEqual(startingHp, Hp(foreign));
            Assert.AreEqual(1, damageEvents);
            Assert.AreEqual(0, damageAction);

            prop.gameObject.SetActive(false);
            hit.enabled = true;
            prop.gameObject.SetActive(true);
            StepPhysics();
            Assert.AreEqual(1, detonations);
            Assert.AreEqual(1, damageEvents);
        }

        [UnityTest]
        public IEnumerator AiMelee_DetonationUsesTheSharedAttackAndCannotHitARecycledVictim()
        {
            AttackInfo strike = MakeAttack();
            GameObject body = MakeBody("Enemy striker", Vector2.zero);
            AddWeaponCollider<DamageDealer>(body);
            ActionExecutorController executor = body.AddComponent<ActionExecutorController>();
            EnemyCharacter enemy = AddActor<EnemyCharacter>(body, CharacterBelong.Enemy, CharacterFeature.Minion);
            var attackAi = ScriptableObject.CreateInstance<AIInfo>();
            _assets.Add(attackAi);
            attackAi.actDataList = new[] { new ActData
            {
                actType = ActType.Attack, weight = 100, actionDefinitionId = strike.PersistentId,
            } };
            attackAi.modes = new[] { new CharacterModeData
            {
                mode = CharacterMode.Combat, detectionRange = 10f, availableActIndices = new[] { 0 },
            } };
            enemy.SetAIInfo(attackAi);
            executor.SetAttackInfosForTest(new[] { strike });
            body.SetActive(true);

            BaseCharacter knight = MakeVictim("Knight target", new Vector2(2.05f, 0f), CharacterBelong.Ally);
            BaseCharacter companion = MakeVictim("Companion", new Vector2(2.2f, 1.05f), CharacterBelong.Ally);
            ExplosiveProp2D prop = MakeProp(out _);
            yield return null;
            CharacterRegistry.RegisterPlayer(knight.ObjectHash);
            int fullHp = Hp(knight);
            Assert.IsTrue(GameManager.Actors.TryGet(enemy.ObjectHash, out ActorRef striker));
            BaseCharacter recycled = null;
            ActorRef previous = default;
            int damageEvents = 0;
            using var listener = GameManager.Events.OnDamageDealt.Subscribe(change =>
            {
                if (change.result.attackerRef != striker || change.result.actionInstanceId != 0) { return; }
                damageEvents++;
                if (recycled != null) { return; }
                recycled = change.result.defenderRef.Hash == knight.ObjectHash ? companion : knight;
                GameManager.Actors.TryGet(recycled.ObjectHash, out previous);
                recycled.OnPoolReturn();
                recycled.OnPoolAcquire();
            });

            GameManager.Instance.SimulationTick(0.6f, 1f);
            Assert.IsTrue(GameManager.Actions.Execution.GetPrimary(enemy.ObjectHash, out ActionExecution windup));
            Assert.AreEqual(AnimationPhase.Anticipation, windup.phase);
            Assert.AreEqual(knight.ObjectHash, windup.targetRef.Hash);
            GameManager.Instance.SimulationTick(0.12f, 1.12f);
            Assert.IsTrue(GameManager.Actions.Execution.TryGetInstance(windup.instanceId, out ActionExecution active));
            Assert.AreEqual(AnimationPhase.Active, active.phase);
            StepPhysics();

            Assert.IsTrue(prop.HasDetonated);
            Assert.AreEqual(1, damageEvents);
            Assert.IsNotNull(recycled);
            Assert.IsTrue(previous.IsValid);
            Assert.IsFalse(GameManager.Actors.IsCurrent(previous));
            Assert.AreEqual(fullHp, Hp(recycled));
            Assert.Less(Hp(recycled == knight ? companion : knight), fullHp);
        }

        [UnityTest]
        public IEnumerator AiGimmick_RequiresSafeClearAim_AndOnlyTheProjectileContactDetonates()
        {
            AttackInfo shot = MakeShot();
            GameObject body = MakeBody("Enemy archer", Vector2.left * 2.5f);
            body.AddComponent<ActionExecutorController>();
            EnemyCharacter archer = AddActor<EnemyCharacter>(body, CharacterBelong.Enemy, CharacterFeature.Minion);
            var ai = ScriptableObject.CreateInstance<AIInfo>();
            _assets.Add(ai);
            ai.actDataList = new[] { new ActData { actType = ActType.InteractObject, weight = 100f } };
            ai.modes = new[] { new CharacterModeData
            {
                mode = CharacterMode.Combat, detectionRange = 10f, availableActIndices = new[] { 0 },
            } };
            archer.SetAIInfo(ai);
            body.SetActive(true);
            BaseCharacter knight = MakeVictim("Knight beyond the prop", new Vector2(2.05f, 0f), CharacterBelong.Ally);
            BaseCharacter teammate = MakeVictim("Archer teammate in the blast", new Vector2(1.85f, 0.65f), CharacterBelong.Enemy);
            ExplosiveProp2D prop = MakeProp(out _);
            AISceneGimmickController gimmick = prop.gameObject.AddComponent<AISceneGimmickController>();
            gimmick.Configure(1 << 4, null, 5f, null, 0f);
            gimmick.SetProjectileOperationForTest(prop, shot);
            int activations = 0;
            int detonations = 0;
            gimmick.OnActivated.AddListener(() => activations++);
            prop.OnDetonated.AddListener(() => detonations++);
            yield return null;
            CharacterRegistry.RegisterPlayer(knight.ObjectHash);
            int startingHp = Hp(knight);
            ProjectileManager projectiles = GameManager.Projectiles;

            GameManager.Instance.SimulationTick(0.6f, 1f);
            Assert.AreEqual(0, projectiles.ActiveCount, "A teammate is inside the blast.");
            Assert.IsFalse(prop.HasDetonated);

            teammate.SetPhysicsPosition(s_Origin + new Vector2(4.5f, 0.65f));
            var wall = new GameObject("Wall blocking the shot") { layer = LayerMask.NameToLayer("Ground") };
            wall.transform.position = s_Origin + Vector2.left;
            wall.AddComponent<BoxCollider2D>().size = new Vector2(0.2f, 1.5f);
            GameManager.Instance.SimulationTick(0.6f, 1.6f);
            Assert.AreEqual(0, projectiles.ActiveCount, "The prop's configured collider is occluded.");

            wall.SetActive(false);
            ExplosiveProp2D nearer = MakeProp(out _);
            nearer.transform.position = s_Origin + new Vector2(-1f, 0.42f);
            GameManager.Instance.SimulationTick(0.6f, 2.2f);
            Assert.AreEqual(0, projectiles.ActiveCount, "The projectile radius clips a nearer prop outside the centerline.");
            Assert.IsFalse(nearer.HasDetonated);

            nearer.gameObject.SetActive(false);
            GameManager.Instance.SimulationTick(0.6f, 2.8f);
            Assert.AreEqual(knight.ObjectHash, archer.FieldOrderJudgment.CurrentTargetHash);
            Assert.IsTrue(GameManager.Actions.Execution.GetPrimary(archer.ObjectHash, out ActionExecution execution));
            Assert.AreEqual(Vector2.right, execution.direction);
            Assert.IsFalse(execution.targetRef.IsValid, "Passing the actor to the attack would re-aim the projectile.");
            Assert.AreEqual(1, projectiles.ActiveCount);
            Assert.IsFalse(prop.HasDetonated, "Accepting a gimmick command only fires the projectile.");
            Assert.IsFalse(gimmick.IsAvailable, "The in-flight shot reserves this prop.");
            Assert.AreEqual(0, activations);
            Assert.AreEqual(startingHp, Hp(knight));

            projectiles.SimulationTick(0.08f);
            Assert.IsTrue(prop.HasDetonated);
            Assert.AreEqual(0, activations);
            Assert.AreEqual(1, detonations);
            Assert.AreEqual(0, projectiles.ActiveCount);
            Assert.Less(Hp(knight), startingHp);
        }

        [UnityTest]
        public IEnumerator PropRegistration_RejectsALiveOwnerAndReclaimsADestroyedOwner()
        {
            ExplosiveProp2D prop = MakeProp(out BoxCollider2D hit);
            ExplosiveProp2D former = MakeProp(out _);
            var registrations = (Dictionary<Collider2D, ExplosiveProp2D>)typeof(ExplosiveProp2D)
                .GetField("s_Props", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            try
            {
                // Model a stale registration that normal Unity disable ordering would clean up.
                registrations[hit] = former;
                prop.enabled = false;
                Assert.AreSame(former, registrations[hit]);
                LogAssert.Expect(LogType.Warning, new Regex(@"\[ExplosiveProp2D\].*別の爆発物に登録済み"));
                prop.enabled = true;
                Assert.AreSame(former, registrations[hit]);

                Object.Destroy(former);
                yield return null;
                Assert.IsTrue(registrations[hit] == null);
                prop.enabled = false;
                prop.enabled = true;
                Assert.AreSame(prop, registrations[hit]);
                prop.enabled = false;
                Assert.IsFalse(registrations.ContainsKey(hit));
            }
            finally { registrations.Remove(hit); }
        }

        [UnityTest]
        public IEnumerator Detonation_WhenDamageAndCompletionCallbacksThrow_AppliesRemainingDamageAndCompletesOnce()
        {
            AttackInfo strike = MakeAttack();
            GameObject body = MakeBody("Striker", Vector2.zero);
            AddWeaponCollider<HitBox>(body);
            body.AddComponent<ActionExecutorController>();
            BaseCharacter striker = AddActor<BaseCharacter>(body, CharacterBelong.Ally, CharacterFeature.Minion);
            body.SetActive(true);
            BaseCharacter first = MakeVictim("First nearby hostile", new Vector2(2.05f, 0f), CharacterBelong.Enemy);
            BaseCharacter second = MakeVictim("Second nearby hostile", new Vector2(2.2f, 1.05f), CharacterBelong.Enemy);
            ExplosiveProp2D prop = MakeProp(out BoxCollider2D hit);
            yield return null;
            int firstHp = Hp(first);
            int secondHp = Hp(second);
            int notifiedDamage = 0;
            int completions = 0;
            prop.OnDetonated.AddListener(() =>
            {
                completions++;
                throw new InvalidOperationException("Prop completion failure");
            });
            GameEvents events = GameManager.Events;
            events.OnDamageDealtEvent += OnDamageDealt;
            try
            {
                Assert.IsTrue(GameManager.Actions.Execute(striker.ObjectHash, strike, 0, Vector2.right, out _));
                GameManager.Actions.Execution.Tick(0.12f);
                LogAssert.Expect(LogType.Exception, new Regex("First prop damage listener failure"));
                LogAssert.Expect(LogType.Exception, new Regex("Prop completion failure"));
                StepPhysics();

                Assert.IsTrue(prop.HasDetonated);
                Assert.IsFalse(hit.enabled);
                Assert.AreEqual(2, notifiedDamage);
                Assert.Less(Hp(first), firstHp);
                Assert.Less(Hp(second), secondHp);
                Assert.AreEqual(1, completions);
                StepPhysics();
                Assert.AreEqual(1, completions);
            }
            finally { events.OnDamageDealtEvent -= OnDamageDealt; }

            void OnDamageDealt(DamageResult damage, int attacker, int defender)
            {
                if (attacker == striker.ObjectHash && ++notifiedDamage == 1)
                {
                    throw new InvalidOperationException("First prop damage listener failure");
                }
            }
        }

        private static void StepPhysics()
        {
            Physics2D.SyncTransforms();
            Assert.IsTrue(Physics2D.Simulate(0.02f));
        }

        private static GameObject MakeBody(string name, Vector2 position)
        {
            var body = new GameObject(name);
            body.SetActive(false);
            body.transform.position = s_Origin + position;
            Rigidbody2D rigidbody = body.AddComponent<Rigidbody2D>();
            rigidbody.gravityScale = 0f;
            rigidbody.constraints = RigidbodyConstraints2D.FreezePositionY | RigidbodyConstraints2D.FreezeRotation;
            body.AddComponent<BoxCollider2D>().size = new Vector2(0.6f, 0.9f);
            return body;
        }

        private static void AddWeaponCollider<T>(GameObject body) where T : Component
        {
            var hitBox = new GameObject("Weapon contact");
            hitBox.transform.SetParent(body.transform, false);
            hitBox.transform.localPosition = Vector2.right * 0.85f;
            hitBox.AddComponent<BoxCollider2D>().size = new Vector2(0.58f, 0.5f);
            hitBox.AddComponent<T>();
        }

        private T AddActor<T>(GameObject body, CharacterBelong side, CharacterFeature feature) where T : BaseCharacter
        {
            body.AddComponent<DamageReceiver>();
            T actor = body.AddComponent<T>();
            Game.Core.CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(side, feature);
            _assets.Add(info);
            actor.SetCharacterInfoForTest(info);
            return actor;
        }

        private BaseCharacter MakeVictim(string name, Vector2 position, CharacterBelong side, Scene scene = default)
        {
            GameObject body = MakeBody(name, position);
            if (scene.IsValid()) { SceneManager.MoveGameObjectToScene(body, scene); }
            BaseCharacter actor = AddActor<BaseCharacter>(body, side, CharacterFeature.Minion);
            body.SetActive(true);
            return actor;
        }

        private static ExplosiveProp2D MakeProp(out BoxCollider2D hit)
        {
            var body = new GameObject("Explosive prop");
            body.SetActive(false);
            body.transform.position = s_Origin + Vector2.right * 0.85f;
            hit = body.AddComponent<BoxCollider2D>();
            hit.isTrigger = true;
            hit.size = new Vector2(0.2f, 0.3f);
            ExplosiveProp2D prop = body.AddComponent<ExplosiveProp2D>();
            prop.SetHitColliderForTest(hit);
            body.SetActive(true);
            return prop;
        }

        private AttackInfo MakeAttack()
        {
            MotionDefinition motion = ScriptableObject.CreateInstance<MotionDefinition>();
            motion.info = new MotionInfo { preMotionDuration = 0.1f, activeMotionDuration = 0.2f, recoveryDuration = 0.1f };
            _assets.Add(motion);
            AttackInfo attack = ScriptableObject.CreateInstance<AttackInfo>();
            attack.SetPersistentIdForTest("explosive-prop-test-melee");
            attack.motion = motion;
            attack.damageMultiplier = 1f;
            attack.attackElement = Element.Slash;
            _assets.Add(attack);
            return attack;
        }

        private AttackInfo MakeShot()
        {
            AttackInfo shot = MakeAttack();
            shot.SetPersistentIdForTest("explosive-prop-test-shot");
            shot.motion.info = new MotionInfo { activeMotionDuration = 0.2f, recoveryDuration = 0.1f };
            shot.magicType = MagicType.Attack;
            shot.projectileInfo = new ProjectileInfo
            {
                hasProjectile = true, trajectory = BulletMoveType.Straight, speed = 60f, lifetime = 1f, projectileCount = 1,
            };
            return shot;
        }

        private static int Hp(BaseCharacter actor) => GameManager.Data.GetVitals(actor.ObjectHash).currentHp;
    }
}
