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
    public sealed class EncounterCombat3DPlayTests
    {
        private readonly List<ScriptableObject> _assets = new();
        private Scene _previous;
        private Scene _room;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            _previous = SceneManager.GetActiveScene();
            _room = SceneManager.CreateScene("Encounter combat 3D", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            SceneManager.SetActiveScene(_room);
            TestSceneHelper.CreateGameManager();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SceneManager.SetActiveScene(_previous);
            yield return SceneManager.UnloadSceneAsync(_room);
            TestSceneHelper.Cleanup();
            foreach (ScriptableObject asset in _assets) { Object.Destroy(asset); }
            _assets.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator BridgeDefense_HasOneFrontAndTransfersWhenItsOwnerLeaves()
        {
            BoxCollider floor = At("Floor", Vector3.down * 0.5f).gameObject.AddComponent<BoxCollider>();
            floor.size = new Vector3(16f, 1f, 16f);
            Transform front = At("Front", new Vector3(-2f, 0f, 1f));
            Transform reserve = At("Reserve", new Vector3(-4f, 0f, -0.5f));
            CharacterActor3D knight = Actor("Knight", new Vector3(0f, 0f, -4f), CharacterBelong.Ally, CharacterFeature.Player);
            CharacterActor3D first = Actor("Shield 1", new Vector3(-2f, 0f, -2f), CharacterBelong.Ally, CharacterFeature.Summon);
            CharacterActor3D second = Actor("Shield 2", new Vector3(-4f, 0f, -2f), CharacterBelong.Ally, CharacterFeature.Summon);
            EncounterCombat3D encounter = new GameObject("Encounter").AddComponent<EncounterCombat3D>();
            Set(encounter, "knight", knight);
            Set(encounter, "alliedFrontStation", front);
            Set(encounter, "alliedReserveStation", reserve);
            EncounterCombatant3D firstMember = Member(first, -1);
            EncounterCombatant3D secondMember = Member(second, 0);
            Assert.IsTrue(encounter.Register(secondMember));
            Assert.IsTrue(encounter.Register(firstMember));

            yield return new WaitForFixedUpdate();
            Assert.AreSame(front, encounter.GetAssignedStation(first));
            Assert.AreSame(reserve, encounter.GetAssignedStation(second));
            Assert.IsTrue(encounter.TryIssue(knight, BroadcastAction.DefendPoint, position: front.position, surface: floor));

            yield return new WaitForFixedUpdate();
            Assert.AreEqual(1, encounter.AssignedOrderCount);
            Assert.AreSame(front, encounter.GetAssignedStation(first));
            Assert.AreSame(reserve, encounter.GetAssignedStation(second));

            first.gameObject.SetActive(false);
            yield return new WaitForFixedUpdate();
            Assert.IsNull(encounter.GetAssignedStation(first));
            Assert.AreSame(front, encounter.GetAssignedStation(second));
            Assert.AreEqual(1, encounter.AssignedOrderCount);
        }

        [UnityTest]
        public IEnumerator EncounterEnemy_WhenSightOpens_ClimbsInclineAndHitsOnTheRaisedDeck()
        {
            const float deckHeight = 1f;
            const float rampThickness = 0.3f;
            Surface("Lower floor", new Vector3(0f, -0.25f, -3f), new Vector3(4f, 0.5f, 6.2f), Quaternion.identity);
            Vector3 rampSlope = new Vector3(0f, deckHeight, 4f);
            Quaternion rampRotation = Quaternion.LookRotation(rampSlope, Vector3.up);
            Vector3 rampCenter = rampSlope * 0.5f - rampRotation * Vector3.up * (rampThickness * 0.5f);
            BoxCollider ramp = Surface("Incline", rampCenter, new Vector3(4f, rampThickness, rampSlope.magnitude), rampRotation);
            BoxCollider deck = Surface("Raised deck", new Vector3(0f, deckHeight - 0.25f, 6.5f),
                new Vector3(4f, 0.5f, 5.1f), Quaternion.identity);
            BoxCollider cover = Surface("Cover", new Vector3(0f, 1.5f, 1.7f), new Vector3(4f, 3f, 0.25f), Quaternion.identity);

            Vector3 enemyStart = new Vector3(0f, 0.04f, -2.5f);
            CharacterActor3D knight = Actor("Knight", new Vector3(0f, deckHeight + 0.04f, 6.2f), CharacterBelong.Ally, CharacterFeature.Player);
            CharacterActor3D enemy = Actor("Enemy", enemyStart, CharacterBelong.Enemy, CharacterFeature.Minion);
            knight.gameObject.layer = LayerMask.NameToLayer("Player");
            enemy.gameObject.layer = LayerMask.NameToLayer("Enemy");
            var encounter = new GameObject("Height and cover encounter").AddComponent<EncounterCombat3D>();
            Set(encounter, "knight", knight);
            Set(encounter, "obstructionLayers", (LayerMask)LayerMask.GetMask("Ground"));

            MotionDefinition motion = ScriptableObject.CreateInstance<MotionDefinition>();
            _assets.Add(motion);
            motion.info = new MotionInfo { preMotionDuration = 0.1f, activeMotionDuration = 0.15f, recoveryDuration = 0.2f };
            AttackInfo attack = ScriptableObject.CreateInstance<AttackInfo>();
            _assets.Add(attack);
            attack.motion = motion;
            attack.damageMultiplier = 2f;
            attack.attackElement = Element.Slash;
            attack.attack3D = new AttackProfile3D
            {
                shape = AttackShape3D.Melee,
                localCenter = new Vector3(0f, 0.8f, 0.95f),
                halfExtents = new Vector3(0.65f, 0.55f, 0.95f),
                targetLayers = LayerMask.GetMask("Player"),
                obstructionLayers = LayerMask.GetMask("Ground"),
            };
            EncounterCombatant3D member = enemy.gameObject.AddComponent<EncounterCombatant3D>();
            Set(member, "attack", attack);
            Set(member, "attackRange", 1.6f);
            Assert.IsTrue(encounter.Register(member));
            Physics.SyncTransforms();
            _room.GetPhysicsScene().Simulate(Time.fixedDeltaTime);

            int knightHash = knight.Actor.Hash;
            int fullHp = GameManager.Data.GetVitals(knightHash).currentHp;
            for (int step = 0; step < 12; step++) { yield return new WaitForFixedUpdate(); }
            Assert.That(enemy.Motor.FeetPosition.z, Is.EqualTo(enemyStart.z).Within(0.05f));
            Assert.AreEqual(fullHp, GameManager.Data.GetVitals(knightHash).currentHp);

            cover.enabled = false;
            bool touchedRamp = false;
            for (int step = 0; step < 170 && GameManager.Data.GetVitals(knightHash).currentHp == fullHp; step++)
            {
                yield return new WaitForFixedUpdate();
                touchedRamp |= enemy.Motor.Ground.Surface == ramp;
            }

            Assert.IsTrue(touchedRamp, $"Enemy feet {enemy.Motor.FeetPosition}.");
            Assert.AreSame(deck, enemy.Motor.Ground.Surface);
            Assert.That(enemy.Motor.FeetPosition.y, Is.EqualTo(deckHeight).Within(0.11f));
            Assert.That(GameManager.Data.GetVitals(knightHash).currentHp, Is.LessThan(fullHp));
        }

        [UnityTest]
        public IEnumerator AttachedSession_WithBossAndGroupTraits_DrivesOrdersOnlyWhileGroupBattleRuns()
        {
            BoxCollider floor = At("Floor", Vector3.down * 0.5f).gameObject.AddComponent<BoxCollider>();
            floor.size = new Vector3(16f, 1f, 16f);
            Transform front = At("Front", new Vector3(-2f, 0f, 1f));
            CharacterActor3D knight = Actor("Knight", new Vector3(0f, 0f, -4f), CharacterBelong.Ally, CharacterFeature.Player);
            CharacterActor3D shield = Actor("Shield", new Vector3(-2f, 0f, -2f), CharacterBelong.Ally, CharacterFeature.Summon);
            ChallengeDefinition definition = ScriptableObject.CreateInstance<ChallengeDefinition>();
            _assets.Add(definition);
            EncounterSession3D boss = new GameObject("Boss session").AddComponent<EncounterSession3D>();
            boss.ConfigureForTest(knight, definition, EncounterTraits.Boss);
            EncounterSession3D groupBoss = new GameObject("Group boss session").AddComponent<EncounterSession3D>();
            groupBoss.ConfigureForTest(knight, definition, EncounterTraits.Boss | EncounterTraits.GroupBattle);
            EncounterCombat3D encounter = new GameObject("Encounter").AddComponent<EncounterCombat3D>();
            Set(encounter, "knight", knight);
            Set(encounter, "alliedFrontStation", front);
            Set(encounter, "objective", boss);
            Assert.IsTrue(encounter.Register(Member(shield, 0)));

            Assert.IsTrue(boss.TryStartEncounter());
            yield return new WaitForFixedUpdate();
            Assert.IsTrue(boss.IsRunning);
            Assert.IsFalse(encounter.TryIssue(knight, BroadcastAction.DefendPoint, position: front.position, surface: floor));
            Assert.IsNull(encounter.GetAssignedStation(shield));
            boss.CompleteEncounter();

            Set(encounter, "objective", groupBoss);
            Assert.IsFalse(encounter.TryIssue(knight, BroadcastAction.DefendPoint, position: front.position, surface: floor));
            Assert.IsTrue(groupBoss.TryStartEncounter());
            yield return new WaitForFixedUpdate();
            Assert.AreSame(front, encounter.GetAssignedStation(shield));
            Assert.IsTrue(encounter.TryIssue(knight, BroadcastAction.DefendPoint, position: front.position, surface: floor));
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(BroadcastAction.DefendPoint, encounter.ActiveOrder);
            Assert.AreEqual(1, encounter.AssignedOrderCount);

            groupBoss.CompleteEncounter();
            Assert.IsFalse(encounter.TryIssue(knight, BroadcastAction.DefendPoint, position: front.position, surface: floor));
            yield return new WaitForFixedUpdate();
            Assert.IsNull(encounter.ActiveOrder);
            Assert.IsNull(encounter.GetAssignedStation(shield));
            Assert.AreEqual(0, encounter.AssignedOrderCount);

            Object.Destroy(groupBoss.gameObject);
            yield return null;
            yield return new WaitForFixedUpdate();
            Assert.IsFalse(encounter.TryIssue(knight, BroadcastAction.DefendPoint, position: front.position, surface: floor));
            Assert.IsNull(encounter.GetAssignedStation(shield));
        }

        private static EncounterCombatant3D Member(CharacterActor3D actor, int priority)
        {
            EncounterCombatant3D member = actor.gameObject.AddComponent<EncounterCombatant3D>();
            Set(member, "prefersFrontStation", true);
            Set(member, "assignmentPriority", priority);
            return member;
        }

        private CharacterActor3D Actor(string name, Vector3 position, CharacterBelong belong, CharacterFeature feature)
        {
            GameObject body = At(name, position).gameObject;
            body.SetActive(false);
            CharacterController controller = body.AddComponent<CharacterController>();
            controller.center = Vector3.up * 0.8f;
            controller.height = 1.6f;
            controller.radius = 0.3f;
            CharacterActor3D actor = body.AddComponent<CharacterActor3D>();
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(belong, feature);
            _assets.Add(info);
            actor.SetCharacterInfoForTest(info);
            body.SetActive(true);
            return actor;
        }

        private static Transform At(string name, Vector3 position)
        {
            Transform transform = new GameObject(name).transform;
            transform.position = position;
            return transform;
        }

        private static BoxCollider Surface(string name, Vector3 position, Vector3 size, Quaternion rotation)
        {
            Transform surface = At(name, position);
            surface.rotation = rotation;
            surface.gameObject.layer = LayerMask.NameToLayer("Ground");
            BoxCollider collider = surface.gameObject.AddComponent<BoxCollider>();
            collider.size = size;
            return collider;
        }

        private static void Set(object target, string name, object value)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}
