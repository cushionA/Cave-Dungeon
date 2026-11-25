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
    public sealed class CompanionController3DPlayTests
    {
        private readonly List<ScriptableObject> _assets = new List<ScriptableObject>();
        private Scene _previous;
        private Scene _room;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            GameObject world = new GameObject("Companion tactics world");
            world.SetActive(false);
            world.AddComponent<ProjectileManager3D>();
            world.AddComponent<GameManager>();
            world.SetActive(true);
            _previous = SceneManager.GetActiveScene();
            _room = SceneManager.CreateScene(nameof(CompanionController3DPlayTests), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            SceneManager.SetActiveScene(_room);
            GameObject floor = new GameObject("Floor");
            floor.transform.position = Vector3.down * 0.5f;
            floor.AddComponent<BoxCollider>().size = new Vector3(32f, 1f, 32f);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_previous.isLoaded) { SceneManager.SetActiveScene(_previous); }
            if (_room.isLoaded) { yield return SceneManager.UnloadSceneAsync(_room); }
            foreach (ScriptableObject asset in _assets) { Object.Destroy(asset); }
            _assets.Clear();
            TestSceneHelper.Cleanup();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Cooperation_AppliesWard_ChargesRepeats_AndReturnsToFollowing()
        {
            CharacterActor3D knight = Actor("Knight", Vector3.zero, CharacterBelong.Ally, CharacterFeature.Player);
            CharacterActor3D sister = Actor("Sister", Vector3.back * 5f, CharacterBelong.Ally, CharacterFeature.Companion);
            CompanionController3D companion = sister.gameObject.AddComponent<CompanionController3D>();
            companion.ConfigureForTest(knight);
            for (int i = 0; i < 70 && sister.transform.position.z < -3f; i++) { yield return new WaitForFixedUpdate(); }
            Assert.GreaterOrEqual(sister.transform.position.z, -3f);

            float startMp = GameManager.Data.GetVitals(sister.Actor.Hash).currentMp;
            Assert.IsTrue(companion.TryCooperate(knight));
            Assert.AreEqual(startMp, GameManager.Data.GetVitals(sister.Actor.Hash).currentMp);
            Assert.IsTrue(companion.IsCooperating);
            Assert.IsFalse(companion.TryCooperate(knight), "The same cast cannot overlap itself.");
            Assert.That(GameManager.Data.GetStatusEffects(knight.Actor.Hash).DefenseMultiplier, Is.GreaterThan(1f));
            Assert.That(companion.CooperationCooldownRemaining, Is.GreaterThan(0f));

            Move(knight, Vector3.forward * 2f);
            float beforeResume = sister.transform.position.z;
            for (int i = 0; i < 60 && companion.IsCooperating; i++) { yield return new WaitForFixedUpdate(); }
            Assert.IsFalse(companion.IsCooperating);
            for (int i = 0; i < 60 && sister.transform.position.z < beforeResume + 0.4f; i++) { yield return new WaitForFixedUpdate(); }
            Assert.That(sister.transform.position.z, Is.GreaterThanOrEqualTo(beforeResume + 0.4f));

            float beforeRepeat = GameManager.Data.GetVitals(sister.Actor.Hash).currentMp;
            Assert.IsTrue(companion.TryCooperate(knight));
            Assert.AreEqual(beforeRepeat - 12f, GameManager.Data.GetVitals(sister.Actor.Hash).currentMp, 0.001f);
        }

        [UnityTest]
        public IEnumerator AppliedTactic_LowHpChangesMode_AndNativeAttackHitsObservedEnemy()
        {
            CharacterActor3D knight = Actor("Knight", Vector3.left * 3f, CharacterBelong.Ally, CharacterFeature.Player);
            CharacterActor3D sister = Actor("Sister", Vector3.zero, CharacterBelong.Ally, CharacterFeature.Companion);
            CharacterActor3D enemy = Actor("Enemy", Vector3.forward * 1.35f, CharacterBelong.Enemy, CharacterFeature.Minion);
            CompanionController3D companion = sister.gameObject.AddComponent<CompanionController3D>();
            companion.ConfigureForTest(knight);
            AttackInfo sword = MakeAttack();
            typeof(CompanionController3D).GetField("attacks", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(companion, new[] { sword });
            AIMode idle = new AIMode
            {
                modeName = "Hold",
                actions = new[] { new ActionSlot { execType = ActionExecType.Sustained, paramId = (int)SustainedAction.Idle } },
                actionRules = System.Array.Empty<AIRule>(),
            };
            AIMode fight = new AIMode
            {
                modeName = "Fight",
                targetRules = new[] { new AIRule { actionIndex = 0, probability = 100 } },
                targetSelects = new[] { new AITargetSelect { sortKey = TargetSortKey.Distance, filter = new TargetFilter { belong = CharacterBelong.Enemy } } },
                actions = new[] { new ActionSlot { execType = ActionExecType.Attack, actionDefinitionId = sword.PersistentId } },
                actionRules = System.Array.Empty<AIRule>(),
            };
            companion.SetAIConfig(new CompanionAIConfig
            {
                modes = new[] { idle, fight },
                modeTransitionRules = new[] { new ModeTransitionRule
                {
                    sourceModeIndex = 0,
                    targetModeIndex = 1,
                    conditions = new[] { new AICondition
                    {
                        conditionType = AIConditionType.HpRatio, compareOp = CompareOp.Less, operandA = 60,
                        filter = new TargetFilter { includeSelf = true },
                    } },
                } },
            });
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(0, companion.ActiveModeIndex);
            int health = GameManager.Data.GetVitals(enemy.Actor.Hash).currentHp;
            GameManager.Data.GetVitals(sister.Actor.Hash).currentHp = GameManager.Data.GetVitals(sister.Actor.Hash).maxHp / 2;
            for (int i = 0; i < 60 && GameManager.Data.GetVitals(enemy.Actor.Hash).currentHp == health; i++)
            {
                yield return new WaitForFixedUpdate();
            }
            Assert.AreEqual(1, companion.ActiveModeIndex);
            Assert.Less(GameManager.Data.GetVitals(enemy.Actor.Hash).currentHp, health);
        }

        [UnityTest]
        public IEnumerator AppliedTactic_NearbyHostileProjectileStartsGuard_AndClearLetsGuardExpire()
        {
            CharacterActor3D knight = Actor("Knight", Vector3.left, CharacterBelong.Ally, CharacterFeature.Player);
            CharacterActor3D sister = Actor("Sister", Vector3.zero, CharacterBelong.Ally, CharacterFeature.Companion);
            CharacterActor3D enemy = Actor("Enemy", Vector3.forward * 5f, CharacterBelong.Enemy, CharacterFeature.Minion);
            CompanionController3D companion = sister.gameObject.AddComponent<CompanionController3D>();
            companion.ConfigureForTest(knight);
            companion.SetAIConfig(new CompanionAIConfig
            {
                modes = new[]
                {
                    new AIMode
                    {
                        actions = new[] { new ActionSlot { execType = ActionExecType.Sustained, paramId = (int)SustainedAction.Guard, paramValue = 0.15f } },
                        actionRules = new[]
                        {
                            new AIRule
                            {
                                actionIndex = 0,
                                conditions = new[] { new AICondition { conditionType = AIConditionType.NearbyHostileProjectileCount, compareOp = CompareOp.GreaterEqual, operandA = 1 } },
                            },
                        },
                        defaultActionIndex = -1,
                    },
                },
            });

            ProjectileManager3D projectiles = GameManager.Projectiles3D;
            Assert.IsNotNull(projectiles);
            Assert.IsTrue(Spawn(knight, knight.Body.bounds.center + Vector3.forward * 4f, AttackTargetRelation.Friendly));
            for (int i = 0; i < 5; i++) { yield return new WaitForFixedUpdate(); }
            Assert.IsFalse(GameManager.Data.GetDefenseState(sister.Actor.Hash).isGuarding);

            Assert.IsTrue(Spawn(enemy, enemy.Body.bounds.center, AttackTargetRelation.Hostile));
            for (int i = 0; i < 15 && !GameManager.Data.GetDefenseState(sister.Actor.Hash).isGuarding; i++)
            {
                yield return new WaitForFixedUpdate();
            }
            Assert.IsTrue(GameManager.Data.GetDefenseState(sister.Actor.Hash).isGuarding);

            projectiles.ClearAll();
            for (int i = 0; i < 20 && GameManager.Data.GetDefenseState(sister.Actor.Hash).isGuarding; i++)
            {
                yield return new WaitForFixedUpdate();
            }
            Assert.IsFalse(GameManager.Data.GetDefenseState(sister.Actor.Hash).isGuarding);

            bool Spawn(CharacterActor3D source, Vector3 position, AttackTargetRelation targets)
            {
                AttackProfile3D shape = new AttackProfile3D { shape = AttackShape3D.Projectile, projectileRadius = 0.1f,
                    targetLayers = LayerMask.GetMask("Player") };
                MagicDefinition magic = new MagicDefinition
                {
                    magicType = MagicType.Attack,
                    bulletProfile = new BulletProfile { moveType = BulletMoveType.Straight, speed = 1f, lifeTime = 5f },
                };
                AttackContext context = AttackContext.Capture(source.Actor, default, 1f, GameManager.Data.GetFlags(source.Actor.Hash).Belong,
                    null, targetRelations: targets);
                return projectiles.Spawn(_room, position, Vector3.back, shape, magic, default, context, default);
            }
        }

        private AttackInfo MakeAttack()
        {
            MotionDefinition motion = ScriptableObject.CreateInstance<MotionDefinition>();
            motion.info = new MotionInfo { preMotionDuration = 0.1f, activeMotionDuration = 0.1f, recoveryDuration = 0.15f };
            _assets.Add(motion);
            AttackInfo attack = ScriptableObject.CreateInstance<AttackInfo>();
            attack.SetPersistentIdForTest("companion-native-tactic");
            attack.motion = motion;
            attack.damageMultiplier = 1f;
            attack.attackElement = Element.Slash;
            attack.attack3D = new AttackProfile3D
            {
                shape = AttackShape3D.Melee,
                localCenter = new Vector3(0f, 0.8f, 1f),
                halfExtents = new Vector3(0.8f, 0.7f, 0.9f),
                targetLayers = LayerMask.GetMask("Enemy"),
            };
            _assets.Add(attack);
            return attack;
        }

        private CharacterActor3D Actor(string name, Vector3 position, CharacterBelong faction, CharacterFeature feature)
        {
            GameObject body = new GameObject(name);
            body.SetActive(false);
            body.layer = LayerMask.NameToLayer(faction == CharacterBelong.Enemy ? "Enemy" : "Player");
            body.transform.position = position + Vector3.up * 0.04f;
            CharacterController collider = body.AddComponent<CharacterController>();
            collider.height = 1.6f;
            collider.radius = 0.3f;
            collider.center = Vector3.up * 0.8f;
            CharacterActor3D actor = body.AddComponent<CharacterActor3D>();
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(faction, feature);
            _assets.Add(info);
            actor.SetCharacterInfoForTest(info);
            body.SetActive(true);
            return actor;
        }

        private static void Move(CharacterActor3D actor, Vector3 position)
        {
            actor.Body.enabled = false;
            actor.transform.position = position + Vector3.up * 0.04f;
            actor.Body.enabled = true;
            Physics.SyncTransforms();
        }
    }
}
