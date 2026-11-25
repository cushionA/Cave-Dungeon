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

            EncounterCombatant3D member = enemy.gameObject.AddComponent<EncounterCombatant3D>();
            Set(member, "attack", CreateMeleeAttack());
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
        public IEnumerator EncounterApproach_WithAllySighting_DetoursAroundSolidWallAndHits()
            => TraverseWall(closeShortRoute: false);

        [UnityTest]
        public IEnumerator EncounterApproach_WhenShortRouteCloses_ReplansAroundOtherSide()
            => TraverseWall(closeShortRoute: true);

        [UnityTest]
        public IEnumerator EncounterTraversal_WithDirectedJump_CrossesGapAndHitsAfterLanding()
            => TraverseGap(canJump: true, blockLanding: false);

        [UnityTest]
        public IEnumerator EncounterTraversal_WithoutJumpCapability_WalksTheBridge()
            => TraverseGap(canJump: false, blockLanding: false);

        [UnityTest]
        public IEnumerator EncounterTraversal_WithBlockedLanding_WalksTheBridge()
            => TraverseGap(canJump: true, blockLanding: true);

        [UnityTest]
        public IEnumerator EncounterTraversal_WhenSessionEndsInFlight_LandsWithoutAttack()
            => TraverseGap(canJump: true, blockLanding: false, endInFlight: true);

        private IEnumerator TraverseGap(bool canJump, bool blockLanding, bool endInFlight = false)
        {
            Surface("Near bank", new Vector3(-2.75f, -0.25f, 0f), new Vector3(4f, 0.5f, 8f), Quaternion.identity);
            BoxCollider farBank = Surface("Far bank", new Vector3(2.75f, -0.25f, 0f), new Vector3(4f, 0.5f, 8f), Quaternion.identity);
            BoxCollider bridge = Surface("Walking bridge", new Vector3(0f, -0.25f, 2.25f), new Vector3(2.5f, 0.5f, 1.25f), Quaternion.identity);
            BoxCollider landingObstruction = Surface("Landing obstruction", new Vector3(1.25f, 2f, 0f),
                new Vector3(0.55f, 0.95f, 0.9f), Quaternion.identity);
            landingObstruction.enabled = blockLanding;
            Transform takeoff = At("Takeoff", new Vector3(-1.25f, 0f, 0f));
            Transform landing = At("Landing", new Vector3(1.25f, 0f, 0f));
            Transform nearBridge = At("Bridge entry", new Vector3(-1.25f, 0f, 2.25f));
            Transform farBridge = At("Bridge exit", new Vector3(1.25f, 0f, 2.25f));
            CharacterActor3D knight = Actor("Knight", new Vector3(3.45f, 0.04f, 0f), CharacterBelong.Ally, CharacterFeature.Player);
            CharacterActor3D fighter = Actor("Fighter", new Vector3(-3.45f, 0.04f, 0f), CharacterBelong.Enemy, CharacterFeature.Minion);
            knight.gameObject.layer = LayerMask.NameToLayer("Player");
            fighter.gameObject.layer = LayerMask.NameToLayer("Enemy");
            LayerMask ground = LayerMask.GetMask("Ground");
            EncounterCombat3D encounter = new GameObject("Gap encounter").AddComponent<EncounterCombat3D>();
            Set(encounter, "knight", knight);
            Set(encounter, "obstructionLayers", ground);
            Set(encounter, "approachGroundLayers", ground);
            Set(encounter, "approachWaypoints", new[] { takeoff, landing, nearBridge, farBridge });
            Set(encounter, "approachJumpLinks", new[] { new EncounterJumpLink3D(takeoff, landing, farBank) });
            EncounterSession3D session = null;
            if (endInFlight)
            {
                ChallengeDefinition challenge = ScriptableObject.CreateInstance<ChallengeDefinition>();
                _assets.Add(challenge);
                session = encounter.gameObject.AddComponent<EncounterSession3D>();
                session.ConfigureForTest(knight, challenge, EncounterTraits.GroupBattle);
                Set(encounter, "objective", session);
                Assert.IsTrue(session.TryStartEncounter());
            }
            EncounterCombatant3D member = fighter.gameObject.AddComponent<EncounterCombatant3D>();
            Set(member, "attack", CreateMeleeAttack());
            Set(member, "attackRange", 1.6f);
            Set(member, "canUseJumpLinks", canJump);
            Assert.IsTrue(encounter.Register(member));
            Physics.SyncTransforms();
            PhysicsScene world = _room.GetPhysicsScene();
            world.Simulate(Time.fixedDeltaTime);
            Assert.IsFalse(world.Raycast(Vector3.up, Vector3.down, out _, 3f, ground, QueryTriggerInteraction.Ignore));

            int knightHash = knight.Actor.Hash;
            int fullHp = GameManager.Data.GetVitals(knightHash).currentHp;
            bool sawJump = false;
            bool sawBridge = false;
            bool sawFarBank = false;
            for (int step = 0; step < 240 && GameManager.Data.GetVitals(knightHash).currentHp == fullHp; step++)
            {
                yield return new WaitForFixedUpdate();
                Vector3 feet = fighter.Motor.FeetPosition;
                sawJump |= Mathf.Abs(feet.x) < 0.5f && Mathf.Abs(feet.z) < 0.4f && feet.y > 0.35f && !fighter.Motor.IsGrounded;
                if (sawJump && session != null && session.IsRunning) { session.CompleteEncounter(); }
                sawBridge |= Mathf.Abs(feet.x) < 0.5f && fighter.Motor.Ground.Surface == bridge;
                sawFarBank |= fighter.Motor.Ground.Surface == farBank;
                if (!sawFarBank) { Assert.AreEqual(fullHp, GameManager.Data.GetVitals(knightHash).currentHp); }
                if (endInFlight && sawFarBank) { break; }
            }

            bool expectsJump = canJump && !blockLanding;
            Assert.AreEqual(expectsJump, sawJump, $"Fighter feet {fighter.Motor.FeetPosition}.");
            Assert.AreEqual(!expectsJump, sawBridge, $"Fighter feet {fighter.Motor.FeetPosition}.");
            Assert.IsTrue(sawFarBank, $"Fighter feet {fighter.Motor.FeetPosition}.");
            if (endInFlight)
            {
                Assert.AreEqual(ChallengeState.Completed, session.Result.state);
                Vector3 landed = fighter.Motor.FeetPosition;
                for (int step = 0; step < 25; step++) { yield return new WaitForFixedUpdate(); }
                Assert.AreEqual(fullHp, GameManager.Data.GetVitals(knightHash).currentHp);
                Assert.That(Vector3.Distance(fighter.Motor.FeetPosition, landed), Is.LessThan(0.06f));
            }
            else { Assert.That(GameManager.Data.GetVitals(knightHash).currentHp, Is.LessThan(fullHp)); }
        }

        private IEnumerator TraverseWall(bool closeShortRoute)
        {
            Surface("Floor", new Vector3(0f, -0.25f, 0f), new Vector3(12f, 0.5f, 12f), Quaternion.identity);
            BoxCollider wall = Surface("Solid cover", new Vector3(0f, 1.5f, 0f), new Vector3(3f, 3f, 0.5f), Quaternion.identity);
            BoxCollider gate = Surface("Short route gate", new Vector3(-3.5f, 1.5f, 0f), new Vector3(4f, 3f, 0.5f), Quaternion.identity);
            gate.enabled = false;
            Vector3 start = new Vector3(0f, 0.04f, -3.8f);
            CharacterActor3D knight = Actor("Knight", new Vector3(0f, 0.04f, 3.8f), CharacterBelong.Ally, CharacterFeature.Player);
            CharacterActor3D fighter = Actor("Fighter", start, CharacterBelong.Enemy, CharacterFeature.Minion);
            CharacterActor3D scout = Actor("Scout", new Vector3(3.5f, 0.04f, 2f), CharacterBelong.Enemy, CharacterFeature.Minion);
            knight.gameObject.layer = LayerMask.NameToLayer("Player");
            fighter.gameObject.layer = LayerMask.NameToLayer("Enemy");
            scout.gameObject.layer = LayerMask.NameToLayer("Enemy");
            EncounterCombatant3D scoutMember = Member(scout, 0);
            scoutMember.enabled = false;
            LayerMask ground = LayerMask.GetMask("Ground");
            EncounterCombat3D encounter = new GameObject("Combat approach").AddComponent<EncounterCombat3D>();
            Set(encounter, "knight", knight);
            Set(encounter, "obstructionLayers", ground);
            Set(encounter, "approachGroundLayers", ground);
            Set(encounter, "approachWaypoints", new[]
            {
                At("Long detour near", new Vector3(2.9f, 0f, -1.2f)),
                At("Long detour far", new Vector3(2.9f, 0f, 1.2f)),
                At("Short detour near", new Vector3(-2.15f, 0f, -1.2f)),
                At("Short detour far", new Vector3(-2.15f, 0f, 1.2f)),
            });
            EncounterCombatant3D fighterMember = fighter.gameObject.AddComponent<EncounterCombatant3D>();
            Set(fighterMember, "attack", CreateMeleeAttack());
            Set(fighterMember, "attackRange", 1.6f);
            Assert.IsTrue(encounter.Register(fighterMember));
            Assert.IsTrue(encounter.Register(scoutMember));

            Physics.SyncTransforms();
            _room.GetPhysicsScene().Simulate(Time.fixedDeltaTime);
            int fullHp = GameManager.Data.GetVitals(knight.Actor.Hash).currentHp;
            for (int step = 0; step < 12; step++) { yield return new WaitForFixedUpdate(); }
            Assert.That(fighter.Motor.FeetPosition.z, Is.EqualTo(start.z).Within(0.05f));
            Assert.AreEqual(fullHp, GameManager.Data.GetVitals(knight.Actor.Hash).currentHp);

            scoutMember.enabled = true;
            bool passedShortSide = false;
            bool passedLongSide = false;
            for (int step = 0; step < 250 && GameManager.Data.GetVitals(knight.Actor.Hash).currentHp == fullHp; step++)
            {
                yield return new WaitForFixedUpdate();
                Vector3 feet = fighter.Motor.FeetPosition;
                if (closeShortRoute && !gate.enabled && feet.x < -0.8f && feet.z < -0.8f) { gate.enabled = true; }
                passedShortSide |= feet.x < -1.8f && Mathf.Abs(feet.z) <= 0.45f;
                passedLongSide |= feet.x > 1.8f && Mathf.Abs(feet.z) <= 0.45f;
                if (feet.z < 0f) { Assert.AreEqual(fullHp, GameManager.Data.GetVitals(knight.Actor.Hash).currentHp); }
            }

            Assert.IsTrue(wall.enabled);
            Assert.AreEqual(closeShortRoute, gate.enabled);
            Assert.AreEqual(!closeShortRoute, passedShortSide, $"Fighter feet {fighter.Motor.FeetPosition}.");
            Assert.AreEqual(closeShortRoute, passedLongSide, $"Fighter feet {fighter.Motor.FeetPosition}.");
            Assert.That(fighter.Motor.FeetPosition.z, Is.GreaterThan(0.5f));
            Assert.That(GameManager.Data.GetVitals(knight.Actor.Hash).currentHp, Is.LessThan(fullHp));
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

        private AttackInfo CreateMeleeAttack()
        {
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
            return attack;
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
