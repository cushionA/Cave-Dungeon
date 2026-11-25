using System.Collections;
using System.Collections.Generic;
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
        private const int k_InitialHp = 100;
        private const float k_PhysicsStep = 0.02f;
        private const float k_BridgeHeight = 4f;
        private const float k_GroundProjectileRange = 6f;
        private const string k_GroundWarningName = "[Ground Impact Warning 3D]";

        private readonly List<Object> _assets = new();
        private readonly List<(DamageResult result, int attacker, int defender)> _hits = new();
        private GameEvents _events;
        private Scene _room;
        private Scene _otherRoom;
        private float _time;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            GameObject gameManager = new GameObject("Spatial combat game manager");
            gameManager.SetActive(false);
            GameObject projectiles = new GameObject("3D projectiles");
            projectiles.transform.SetParent(gameManager.transform);
            projectiles.AddComponent<ProjectileManager3D>();
            gameManager.AddComponent<GameManager>();
            gameManager.SetActive(true);
            GameManager.Instance.enabled = false;
            _events = GameManager.Events;
            _events.OnDamageDealtEvent += RecordDamage;
            _room = SceneManager.CreateScene("SpatialCombat3D", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            _otherRoom = SceneManager.CreateScene("SpatialCombat3D Other", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            _time = 0f;
            _hits.Clear();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_events != null)
            {
                _events.OnDamageDealtEvent -= RecordDamage;
            }
            if (_room.isLoaded)
            {
                yield return SceneManager.UnloadSceneAsync(_room);
            }
            if (_otherRoom.isLoaded)
            {
                yield return SceneManager.UnloadSceneAsync(_otherRoom);
            }
            foreach (Object asset in _assets)
            {
                Object.Destroy(asset);
            }
            _assets.Clear();
            TestSceneHelper.Cleanup();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Melee3D_EntersActive_HitsOnlyTheNearestOpposingActorInTheVolumeAndWorld()
        {
            CharacterActor3D attacker = CreateActor(_room, "Knight", new Vector3(0f, 0.1f, 0f), CharacterBelong.Ally);
            CharacterActor3D friendly = CreateActor(_room, "Friendly", new Vector3(0.7f, 0.1f, 1f), CharacterBelong.Ally);
            CharacterActor3D nearest = CreateActor(_room, "Nearest", new Vector3(-0.65f, 0.1f, 1.8f));
            CharacterActor3D farther = CreateActor(_room, "Farther", new Vector3(0.55f, 0.1f, 2.65f));
            CharacterActor3D higher = CreateActor(_room, "Upstairs", new Vector3(-0.65f, 4.1f, 1.8f));
            CharacterActor3D lower = CreateActor(_room, "Downstairs", new Vector3(-0.65f, -3.9f, 1.8f));
            CharacterActor3D remote = CreateActor(_otherRoom, "Separate physics scene", new Vector3(0f, 0.1f, 0.7f));
            yield return null;
            SynchronizeWorld(_room);
            SynchronizeWorld(_otherRoom);

            Assert.That(GameManager.Actions.Execute3D(attacker, CreateAttack(AttackShape3D.Melee), out long instanceId), Is.True);
            Step(0.1f);
            Assert.That(GameManager.Actions.Execution.TryGetInstance(instanceId, out ActionExecution windup), Is.True);
            Assert.That(windup.phase, Is.EqualTo(AnimationPhase.Anticipation));
            Assert.That(_hits, Is.Empty);
            Assert.That(Hp(nearest), Is.EqualTo(k_InitialHp));

            Step(0.15f);
            Assert.That(GameManager.Actions.Execution.TryGetInstance(instanceId, out ActionExecution strike), Is.True);
            Assert.That(strike.phase, Is.EqualTo(AnimationPhase.Active));
            AssertSingleHit(attacker, nearest, instanceId);

            Step(0.05f);
            Step(0.4f);
            AssertSingleHit(attacker, nearest, instanceId);
            AssertHealthy(attacker, friendly, farther, higher, lower, remote);

            nearest.transform.position += Vector3.right * 5f;
            farther.transform.position += Vector3.right * 5f;
            SynchronizeWorld(_room);
            Assert.That(GameManager.Actions.Execute3D(attacker, CreateAttack(AttackShape3D.Melee), out _), Is.True);
            Step(0.25f);
            AssertSingleHit(attacker, nearest, instanceId);
            AssertHealthy(attacker, friendly, farther, higher, lower, remote);
        }

        [UnityTest]
        public IEnumerator Melee3D_ObstacleBetweenBodies_SkipsBlockedNearestActor()
        {
            CharacterActor3D attacker = CreateActor(_room, "Knight", new Vector3(0f, 0.1f, 0f), CharacterBelong.Ally);
            CharacterActor3D blocked = CreateActor(_room, "Behind wall", new Vector3(-0.65f, 0.1f, 1.5f));
            CharacterActor3D exposed = CreateActor(_room, "Clear line", new Vector3(0.65f, 0.1f, 2.4f));
            CreateSurface("Wall", new Vector3(-0.325f, 1f, 0.75f), new Vector3(0.48f, 2f, 0.2f));
            yield return null;
            SynchronizeWorld(_room);

            Assert.That(GameManager.Actions.Execute3D(attacker, CreateAttack(AttackShape3D.Melee), out long instanceId), Is.True);
            Step(0.25f);

            AssertSingleHit(attacker, exposed, instanceId);
            AssertHealthy(blocked);
        }

        [UnityTest]
        public IEnumerator Melee3D_WithSelectedMarks_DamagesAtMostThreeMarkedBodiesOnce()
        {
            CharacterActor3D attacker = CreateActor(_room, "Knight", new Vector3(0f, 0.1f, 0f), CharacterBelong.Ally);
            CharacterActor3D nearestUnmarked = CreateActor(_room, "Nearest unmarked", new Vector3(0f, 0.1f, 0.9f));
            CharacterActor3D left = CreateActor(_room, "Left mark", new Vector3(-0.8f, 0.1f, 1.8f));
            CharacterActor3D center = CreateActor(_room, "Center mark", new Vector3(0f, 0.1f, 2.55f));
            CharacterActor3D right = CreateActor(_room, "Right mark", new Vector3(0.75f, 0.1f, 1.9f));
            yield return null;
            SynchronizeWorld(_room);

            AttackInfo attack = CreateAttack(AttackShape3D.Melee);
            attack.multiLockRange = 5f;
            ActorRef[] selections = { left.Actor, left.Actor, center.Actor, right.Actor, nearestUnmarked.Actor };
            Assert.That(GameManager.Actions.Execute3D(attacker, attack, selections, out long instanceId), Is.True);
            Step(0.1f);
            Assert.That(_hits, Is.Empty);
            Step(0.12f);

            Assert.That(_hits, Has.Count.EqualTo(3));
            AssertHit(attacker.Actor, left, instanceId);
            AssertHit(attacker.Actor, center, instanceId);
            AssertHit(attacker.Actor, right, instanceId);
            Step(0.1f);
            Step(0.4f);
            Assert.That(_hits, Has.Count.EqualTo(3));
            AssertHealthy(attacker, nearestUnmarked);
        }

        [UnityTest]
        public IEnumerator GroundArea3D_CancelsWindupAndHitsOnlyTheWarnedBridgeSurface()
        {
            BoxCollider lowerFloor = CreateSurface("Lower floor", new Vector3(0f, -0.25f, 0f), new Vector3(10f, 0.5f, 10f));
            BoxCollider upperFloor = CreateSurface("Bridge top", new Vector3(0f, 3.75f, 0f), new Vector3(10f, 0.5f, 10f));
            CharacterActor3D attacker = CreateActor(_room, "Caster", new Vector3(-0.8f, 4.12f, 0f));
            CharacterActor3D onBridge = CreateActor(_room, "On bridge", new Vector3(0.65f, 4.12f, 0f), CharacterBelong.Ally);
            CharacterActor3D below = CreateActor(_room, "Under bridge", new Vector3(0.65f, 0.12f, 0f), CharacterBelong.Ally);
            CharacterActor3D distant = CreateActor(_room, "Outside circle", new Vector3(3.1f, 4.12f, 0f), CharacterBelong.Ally);
            yield return null;
            Settle(attacker, onBridge, below, distant);
            Assert.That(attacker.Motor.Ground.Surface, Is.SameAs(upperFloor));
            Assert.That(onBridge.Motor.Ground.Surface, Is.SameAs(upperFloor));
            Assert.That(below.Motor.Ground.Surface, Is.SameAs(lowerFloor));

            AttackInfo attack = CreateAttack(AttackShape3D.GroundArea);
            Assert.That(GameManager.Actions.Execute3D(attacker, attack, out long interruptedId), Is.True);
            Step(0.05f);
            GameObject warning = VisibleWarning();
            Assert.That(warning, Is.Not.Null);
            Assert.That(warning.transform.position.y, Is.EqualTo(4f).Within(0.1f));
            Assert.That(_hits, Is.Empty);

            Assert.That(GameManager.Actions.Execution.Cancel(interruptedId, force: true), Is.True);
            Assert.That(VisibleWarning(), Is.Null);
            Step(0.35f);
            Assert.That(_hits, Is.Empty);
            AssertHealthy(attacker, onBridge, below, distant);

            Assert.That(GameManager.Actions.Execute3D(attacker, attack, out long instanceId), Is.True);
            Step(0.05f);
            Assert.That(VisibleWarning(), Is.Not.Null);
            Assert.That(_hits, Is.Empty);
            Step(0.2f);

            AssertSingleHit(attacker, onBridge, instanceId);
            AssertHealthy(attacker, below, distant);
            Assert.That(VisibleWarning(), Is.Null);
        }

        [UnityTest]
        public IEnumerator Projectile3D_WaitsForActiveAndSweepsOnlyThroughItsWorldAtItsHeight()
        {
            CharacterActor3D attacker = CreateActor(_room, "Cannon", new Vector3(0f, 0.1f, 0f), CharacterBelong.Ally);
            CharacterActor3D target = CreateActor(_room, "Downrange", new Vector3(0f, 0.1f, 6f));
            CharacterActor3D upper = CreateActor(_room, "Upper floor", new Vector3(0f, 4.1f, 2f));
            CharacterActor3D remote = CreateActor(_otherRoom, "Other physics scene", new Vector3(0f, 0.1f, 1.75f));
            BoxCollider wall = CreateSurface("Wall", new Vector3(0f, 1f, 3f), new Vector3(2f, 2f, 0.1f));
            yield return null;
            SynchronizeWorld(_room);
            SynchronizeWorld(_otherRoom);

            AttackInfo attack = CreateProjectile(BulletMoveType.Straight);
            Assert.That(GameManager.Actions.Execute3D(attacker, attack, out long blockedShot), Is.True);
            Step(0.1f);
            Assert.That(GameManager.Actions.Execution.TryGetInstance(blockedShot, out ActionExecution windup), Is.True);
            Assert.That(windup.phase, Is.EqualTo(AnimationPhase.Anticipation));
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.Zero);
            Assert.That(_hits, Is.Empty);
            Step(0.12f);
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.EqualTo(1));
            Assert.That(_hits, Is.Empty);
            Step(0.32f);
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.Zero);
            Assert.That(_hits, Is.Empty);
            AssertHealthy(target, upper, remote);
            Step(0.2f);

            wall.enabled = false;
            SynchronizeWorld(_room);
            Assert.That(GameManager.Actions.Execute3D(attacker, attack, out long clearShot), Is.True);
            Step(0.22f);
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.EqualTo(1));
            Assert.That(_hits, Is.Empty);
            ActorRef retiredCaster = attacker.Actor;
            attacker.gameObject.SetActive(false);
            Assert.That(GameManager.Actors.IsCurrent(retiredCaster), Is.False);
            Step(0.7f);
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.Zero);
            Assert.That(_hits, Has.Count.EqualTo(1));
            AssertHit(retiredCaster, target, clearShot);
            AssertHealthy(upper, remote);
        }

        [UnityTest]
        public IEnumerator MultiLockProjectile3D_AfterCoverAndActorReuse_LaunchesOnlyAtTheSurvivingMark()
        {
            CharacterActor3D caster = CreateActor(_room, "Caster", new Vector3(0f, 0.1f, 0f));
            CharacterActor3D valid = CreateActor(_room, "Valid", new Vector3(-2.4f, 0.1f, 6f), CharacterBelong.Ally);
            CharacterActor3D reappeared = CreateActor(_room, "Reappeared", new Vector3(0f, 0.1f, 7f), CharacterBelong.Ally);
            CharacterActor3D behindWall = CreateActor(_room, "Behind cover", new Vector3(2.4f, 0.1f, 6f), CharacterBelong.Ally);
            BoxCollider cover = CreateSurface("Cover", new Vector3(1.2f, 1f, 3f), new Vector3(0.7f, 2f, 0.15f));
            cover.enabled = false;
            yield return null;
            SynchronizeWorld(_room);

            ActorRef previous = reappeared.Actor;
            AttackInfo attack = CreateProjectile(BulletMoveType.Homing);
            Assert.That(GameManager.Actions.Execute3D(caster, attack, new[] { valid.Actor, previous, behindWall.Actor },
                out long shot), Is.True);
            Step(0.1f);
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.Zero);
            Assert.That(_hits, Is.Empty);

            reappeared.gameObject.SetActive(false);
            Assert.That(GameManager.Actors.IsCurrent(previous), Is.False);
            reappeared.gameObject.SetActive(true);
            Assert.That(reappeared.Actor.IsValid, Is.True);
            Assert.That(reappeared.Actor.Hash, Is.EqualTo(previous.Hash));
            Assert.That(reappeared.Actor, Is.Not.EqualTo(previous));
            cover.enabled = true;
            SynchronizeWorld(_room);

            Step(0.12f);
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.EqualTo(1));
            Assert.That(_hits, Is.Empty);
            AdvanceProjectileFlight(55);
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.Zero);
            AssertSingleHit(caster, valid, shot);
            AssertHealthy(caster, reappeared, behindWall);
        }

        [UnityTest]
        public IEnumerator HomingProjectile3D_WhenMarkedTargetChangesHeightAndDirection_FollowsTheSameLease()
        {
            CharacterActor3D caster = CreateActor(_room, "Caster", new Vector3(0f, 0.1f, 0f));
            CharacterActor3D target = CreateActor(_room, "Moving mark", new Vector3(0f, 0.1f, 8f), CharacterBelong.Ally);
            CharacterActor3D decoy = CreateActor(_room, "Body on the original path", new Vector3(0f, 0.1f, 6.4f), CharacterBelong.Ally);
            yield return null;
            SynchronizeWorld(_room);

            AttackInfo attack = CreateProjectile(BulletMoveType.Homing);
            Assert.IsTrue(GameManager.Actions.Execute3D(caster, attack, new[] { target.Actor }, out long instanceId));
            Step(0.22f);
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.EqualTo(1));
            Assert.That(_hits, Is.Empty);
            List<ProjectileLockWarning> warnings = new List<ProjectileLockWarning>();
            GameManager.Projectiles3D.CollectLockWarnings(target.Actor, warnings);
            Assert.That(warnings, Has.Count.EqualTo(1));
            ulong lease = warnings[0].LeaseId;
            Vector3 launchPath = target.Body.bounds.center + warnings[0].FromTarget;

            target.transform.position += new Vector3(2.6f, 2.1f, 0f);
            SynchronizeWorld(_room);
            AdvanceProjectileFlight(8);
            GameManager.Projectiles3D.CollectLockWarnings(target.Actor, warnings);
            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(warnings[0].LeaseId, Is.EqualTo(lease));
            Vector3 redirectedPath = target.Body.bounds.center + warnings[0].FromTarget;
            Assert.That(redirectedPath.x, Is.GreaterThan(launchPath.x + 0.2f));
            Assert.That(redirectedPath.y, Is.GreaterThan(launchPath.y + 0.2f));
            GameManager.Projectiles3D.CollectLockWarnings(decoy.Actor, warnings);
            Assert.That(warnings, Is.Empty);

            AdvanceProjectileFlight(65);
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.Zero);
            AssertSingleHit(caster, target, instanceId);
            AssertHealthy(caster, decoy);
        }

        [UnityTest]
        public IEnumerator MultiLockProjectile3D_AiPrimaryAndTwoMore_KeepThreeDistinctLocksAndDamageEvents()
        {
            CharacterActor3D caster = CreateActor(_room, "Caster", new Vector3(0f, 0.1f, 0f));
            CharacterActor3D left = CreateActor(_room, "Left mark", new Vector3(-2.4f, 0.1f, 6f), CharacterBelong.Ally);
            CharacterActor3D center = CreateActor(_room, "Higher primary mark", new Vector3(0f, 2.1f, 7.25f), CharacterBelong.Ally);
            CharacterActor3D right = CreateActor(_room, "Right mark", new Vector3(2.4f, 0.1f, 6f), CharacterBelong.Ally);
            CharacterActor3D unmarked = CreateActor(_room, "Closer than the primary", new Vector3(3.7f, 0.1f, 5.8f), CharacterBelong.Ally);
            CharacterActor3D obscured = CreateActor(_room, "Close behind cover", new Vector3(2.7f, 0.1f, 2.2f), CharacterBelong.Ally);
            CharacterActor3D behindCaster = CreateActor(_room, "Close behind caster", new Vector3(0f, 0.1f, -2f), CharacterBelong.Ally);
            CharacterActor3D anotherScene = CreateActor(_otherRoom, "Close in another world", new Vector3(0f, 0.1f, 2f), CharacterBelong.Ally);
            CreateSurface("Cover outside marked flight paths", new Vector3(1.5f, 1f, 1.42f), new Vector3(0.55f, 2f, 0.18f));
            yield return null;
            SynchronizeWorld(_room);
            SynchronizeWorld(_otherRoom);

            AttackInfo attack = CreateProjectile(BulletMoveType.Homing);
            Assert.That(GameManager.Actions.Execute3D(caster, attack, center.Actor, out long instanceId), Is.True);
            Step(0.22f);
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.EqualTo(3));
            Assert.That(_hits, Is.Empty);
            Assert.That(GameManager.Actions.Execution.TryGetInstance(instanceId, out ActionExecution execution), Is.True);
            Assert.That(execution.targetRef, Is.EqualTo(center.Actor));
            List<ProjectileLockWarning> warnings = new List<ProjectileLockWarning>();
            HashSet<ulong> leases = new HashSet<ulong>();
            foreach (CharacterActor3D mark in new[] { left, center, right })
            {
                GameManager.Projectiles3D.CollectLockWarnings(mark.Actor, warnings);
                Assert.That(warnings, Has.Count.EqualTo(1), mark.name);
                Assert.That(leases.Add(warnings[0].LeaseId), Is.True);
            }
            foreach (CharacterActor3D excluded in new[] { unmarked, obscured, behindCaster, anotherScene })
            {
                GameManager.Projectiles3D.CollectLockWarnings(excluded.Actor, warnings);
                Assert.That(warnings, Is.Empty, excluded.name);
            }

            AdvanceProjectileFlight(55);
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.Zero);
            Assert.That(_hits, Has.Count.EqualTo(3));
            AssertHit(caster.Actor, left, instanceId);
            AssertHit(caster.Actor, center, instanceId);
            AssertHit(caster.Actor, right, instanceId);
            AssertHealthy(caster, unmarked, obscured, behindCaster, anotherScene);
        }

        [UnityTest]
        public IEnumerator MultiLockProjectile3D_AfterLaunch_AnOldLockCannotHurtANewActorGeneration()
        {
            CharacterActor3D caster = CreateActor(_room, "Caster", new Vector3(0f, 0.1f, 0f));
            CharacterActor3D target = CreateActor(_room, "Target", new Vector3(0f, 0.1f, 6f), CharacterBelong.Ally);
            yield return null;
            SynchronizeWorld(_room);

            ActorRef previous = target.Actor;
            AttackInfo attack = CreateProjectile(BulletMoveType.Homing);
            Assert.That(GameManager.Actions.Execute3D(caster, attack, new[] { previous }, out _), Is.True);
            Step(0.22f);
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.EqualTo(1));
            List<ProjectileLockWarning> warnings = new List<ProjectileLockWarning>();
            GameManager.Projectiles3D.CollectLockWarnings(previous, warnings);
            Assert.That(warnings, Has.Count.EqualTo(1));

            target.gameObject.SetActive(false);
            target.gameObject.SetActive(true);
            Assert.That(target.Actor.IsValid, Is.True);
            Assert.That(target.Actor.Hash, Is.EqualTo(previous.Hash));
            Assert.That(target.Actor, Is.Not.EqualTo(previous));
            SynchronizeWorld(_room);
            GameManager.Projectiles3D.CollectLockWarnings(target.Actor, warnings);
            Assert.That(warnings, Is.Empty);

            AdvanceProjectileFlight(75);
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.Zero);
            Assert.That(_hits, Is.Empty);
            AssertHealthy(caster, target);
        }

        [UnityTest]
        public IEnumerator GroundImpactProjectile3D_UsesTheWarnedFloorAndCurrentOccupantsAtImpact()
        {
            BoxCollider upperFloor = CreateAttackFloor("Bridge", k_BridgeHeight);
            BoxCollider lowerFloor = CreateAttackFloor("Under the bridge", 0f);
            CharacterActor3D caster = CreateActor(_room, "Caster", new Vector3(0f, 4.12f, 0f));
            CharacterActor3D marked = CreateActor(_room, "Marked knight", new Vector3(0f, 4.12f, 5.5f), CharacterBelong.Ally);
            CharacterActor3D leaver = CreateActor(_room, "Leaving the area", new Vector3(-0.95f, 4.12f, 5.5f), CharacterBelong.Ally);
            CharacterActor3D entrant = CreateActor(_room, "Entering the area", new Vector3(3.4f, 4.12f, 5.5f), CharacterBelong.Ally);
            CharacterActor3D lower = CreateActor(_room, "Same position below", new Vector3(0f, 0.12f, 5.5f), CharacterBelong.Ally);
            yield return null;
            Settle(caster, marked, leaver, entrant, lower);
            Assert.That(marked.Motor.Ground.Surface, Is.SameAs(upperFloor));
            Assert.That(lower.Motor.Ground.Surface, Is.SameAs(lowerFloor));

            ActorRef previous = marked.Actor;
            Vector3 point = marked.Motor.Ground.Point;
            AttackInfo bomb = CreateGroundImpactProjectile();
            Assert.That(GameManager.Actions.Execute3D(caster, bomb, previous, out long instanceId), Is.True);
            Step(0.05f);
            GameObject warning = VisibleWarning();
            AssertWarningAt(warning, point);
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.Zero);
            Assert.That(_hits, Is.Empty);

            Step(0.2f);
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.EqualTo(1));
            Assert.That(VisibleWarning(), Is.SameAs(warning));
            List<ProjectileLockWarning> warnings = new List<ProjectileLockWarning>();
            GameManager.Projectiles3D.CollectLockWarnings(previous, warnings);
            Assert.That(warnings, Has.Count.EqualTo(1));
            GameManager.Projectiles3D.CollectLockWarnings(leaver.Actor, warnings);
            Assert.That(warnings, Is.Empty);
            GameManager.Projectiles3D.CollectLockWarnings(lower.Actor, warnings);
            Assert.That(warnings, Is.Empty);

            Step(0.4f);
            Assert.That(GameManager.Actions.Execution.TryGetInstance(instanceId, out _), Is.False);
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.EqualTo(1));
            Assert.That(VisibleWarning(), Is.SameAs(warning));
            AssertHealthy(marked, leaver, entrant, lower);

            marked.gameObject.SetActive(false);
            marked.gameObject.SetActive(true);
            Assert.That(marked.Actor.IsValid, Is.True);
            Assert.That(marked.Actor.Hash, Is.EqualTo(previous.Hash));
            Assert.That(marked.Actor, Is.Not.EqualTo(previous));
            leaver.transform.position = new Vector3(-3.4f, 4.12f, 5.5f);
            entrant.transform.position = new Vector3(0.95f, 4.12f, 5.5f);
            Settle(marked, leaver, entrant);
            Assert.That(marked.Motor.Ground.Surface, Is.SameAs(upperFloor));
            Assert.That(entrant.Motor.Ground.Surface, Is.SameAs(upperFloor));
            AssertWarningAt(warning, point);
            GameManager.Projectiles3D.CollectLockWarnings(marked.Actor, warnings);
            Assert.That(warnings, Is.Empty);
            GameManager.Projectiles3D.CollectLockWarnings(entrant.Actor, warnings);
            Assert.That(warnings, Is.Empty);

            AdvanceProjectileFlight(80);
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.Zero);
            Assert.That(VisibleWarning(), Is.Null);
            Assert.That(_hits, Has.Count.EqualTo(2));
            AssertHit(caster.Actor, marked, instanceId);
            AssertHit(caster.Actor, entrant, instanceId);
            AssertHealthy(caster, leaver, lower);
        }

        [UnityTest]
        public IEnumerator GroundImpactProjectile3D_UnaimedCastIsAreaOnlyAndInterruptedWindupLeavesNoShot()
        {
            BoxCollider upperFloor = CreateAttackFloor("Bridge", k_BridgeHeight);
            BoxCollider lowerFloor = CreateAttackFloor("Under the bridge", 0f);
            CharacterActor3D caster = CreateActor(_room, "Caster", new Vector3(0f, 4.12f, 0f));
            CharacterActor3D upper = CreateActor(_room, "On bridge", new Vector3(0.65f, 4.12f, 6f), CharacterBelong.Ally);
            CharacterActor3D lower = CreateActor(_room, "Under bridge", new Vector3(0.65f, 0.12f, 6f), CharacterBelong.Ally);
            yield return null;
            Settle(caster, upper, lower);
            Assert.That(caster.Motor.Ground.Surface, Is.SameAs(upperFloor));
            Assert.That(upper.Motor.Ground.Surface, Is.SameAs(upperFloor));
            Assert.That(lower.Motor.Ground.Surface, Is.SameAs(lowerFloor));

            AttackInfo bomb = CreateGroundImpactProjectile();
            Vector3 point = caster.Motor.Ground.Point + Vector3.forward * k_GroundProjectileRange;
            Assert.That(GameManager.Actions.Execute3D(caster, bomb, out long interruptedId), Is.True);
            Step(0.05f);
            AssertWarningAt(VisibleWarning(), point);
            Assert.That(GameManager.Actions.Execution.Cancel(interruptedId, force: true), Is.True);
            Step(0.4f);
            Assert.That(VisibleWarning(), Is.Null);
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.Zero);
            Assert.That(_hits, Is.Empty);

            Assert.That(GameManager.Actions.Execute3D(caster, bomb, out long instanceId), Is.True);
            Step(0.22f);
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.EqualTo(1));
            AssertWarningAt(VisibleWarning(), point);
            List<ProjectileLockWarning> warnings = new List<ProjectileLockWarning>();
            GameManager.Projectiles3D.CollectLockWarnings(upper.Actor, warnings);
            Assert.That(warnings, Is.Empty);

            AdvanceProjectileFlight(80);
            Assert.That(VisibleWarning(), Is.Null);
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.Zero);
            AssertSingleHit(caster, upper, instanceId);
            AssertHealthy(caster, lower);
        }

        [UnityTest]
        public IEnumerator GroundImpactProjectile3D_WhenNewCoverBlocksFlight_ClearsWarningWithoutBlasting()
        {
            CreateAttackFloor("Bridge", k_BridgeHeight);
            CharacterActor3D caster = CreateActor(_room, "Caster", new Vector3(0f, 4.12f, 0f));
            CharacterActor3D target = CreateActor(_room, "At the destination", new Vector3(0f, 4.12f, 5.5f), CharacterBelong.Ally);
            CharacterActor3D nearWall = CreateActor(_room, "Near the wall", new Vector3(0.85f, 4.12f, 3f), CharacterBelong.Ally);
            BoxCollider wall = CreateSurface("Rising wall", new Vector3(0f, 4.9f, 3f), new Vector3(1.4f, 2f, 0.15f));
            wall.enabled = false;
            yield return null;
            Settle(caster, target, nearWall);

            AttackInfo bomb = CreateGroundImpactProjectile();
            wall.enabled = true;
            SynchronizeWorld(_room);
            Assert.That(GameManager.Actions.Execute3D(caster, bomb, target.Actor, out _), Is.True);
            Step(0.22f);
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.Zero);
            Assert.That(VisibleWarning(), Is.Null);
            Assert.That(_hits, Is.Empty);
            Step(0.45f);

            wall.enabled = false;
            SynchronizeWorld(_room);
            Assert.That(GameManager.Actions.Execute3D(caster, bomb, target.Actor, out _), Is.True);
            Step(0.22f);
            AssertWarningAt(VisibleWarning(), target.Motor.Ground.Point);
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.EqualTo(1));

            wall.enabled = true;
            SynchronizeWorld(_room);
            Step(0.6f);
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.Zero);
            Assert.That(VisibleWarning(), Is.Null);
            AdvanceProjectileFlight(80);
            Assert.That(_hits, Is.Empty);
            AssertHealthy(caster, target, nearWall);
        }

        [UnityTest]
        public IEnumerator GroundImpactProjectile3D_WhenTheWarnedFloorDisappears_NeverDropsToTheLowerFloor()
        {
            BoxCollider upperFloor = CreateAttackFloor("Collapsing bridge", k_BridgeHeight);
            BoxCollider lowerFloor = CreateAttackFloor("Stable floor", 0f);
            CharacterActor3D caster = CreateActor(_room, "Caster", new Vector3(0f, 4.12f, 0f));
            CharacterActor3D upper = CreateActor(_room, "On bridge", new Vector3(0f, 4.12f, 5.5f), CharacterBelong.Ally);
            CharacterActor3D lower = CreateActor(_room, "Under bridge", new Vector3(0f, 0.12f, 5.5f), CharacterBelong.Ally);
            yield return null;
            Settle(caster, upper, lower);
            Assert.That(upper.Motor.Ground.Surface, Is.SameAs(upperFloor));
            Assert.That(lower.Motor.Ground.Surface, Is.SameAs(lowerFloor));

            AttackInfo bomb = CreateGroundImpactProjectile();
            Assert.That(GameManager.Actions.Execute3D(caster, bomb, upper.Actor, out _), Is.True);
            Step(0.22f);
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.EqualTo(1));
            AssertWarningAt(VisibleWarning(), upper.Motor.Ground.Point);

            upperFloor.enabled = false;
            SynchronizeWorld(_room);
            Step(k_PhysicsStep);
            Assert.That(GameManager.Projectiles3D.ActiveCount, Is.Zero);
            Assert.That(VisibleWarning(), Is.Null);
            AdvanceProjectileFlight(80);
            Assert.That(_hits, Is.Empty);
            AssertHealthy(caster, upper, lower);
        }

        private CharacterActor3D CreateActor(Scene scene, string name, Vector3 position,
            CharacterBelong side = CharacterBelong.Enemy, CharacterFeature feature = CharacterFeature.Minion)
        {
            GameObject actor = new GameObject(name);
            actor.SetActive(false);
            SceneManager.MoveGameObjectToScene(actor, scene);
            actor.transform.position = position;
            actor.layer = LayerMask.NameToLayer(side == CharacterBelong.Ally ? "Player" : "Enemy");

            CharacterController body = actor.AddComponent<CharacterController>();
            body.height = 1.8f;
            body.center = Vector3.up * (body.height * 0.5f);
            body.radius = 0.3f;
            body.skinWidth = 0.04f;
            body.stepOffset = 0.25f;
            body.minMoveDistance = 0f;
            actor.AddComponent<CharacterMotor3D>();
            actor.AddComponent<DamageReceiver>();
            CharacterActor3D managed = actor.AddComponent<CharacterActor3D>();
            Game.Core.CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(side, feature, k_InitialHp);
            _assets.Add(info);
            managed.SetCharacterInfoForTest(info);
            actor.SetActive(true);
            return managed;
        }

        private BoxCollider CreateSurface(string name, Vector3 center, Vector3 size)
        {
            GameObject surface = new GameObject(name);
            SceneManager.MoveGameObjectToScene(surface, _room);
            surface.layer = LayerMask.NameToLayer("Ground");
            surface.transform.position = center;
            BoxCollider collider = surface.AddComponent<BoxCollider>();
            collider.size = size;
            return collider;
        }

        private BoxCollider CreateAttackFloor(string name, float height)
        {
            return CreateSurface(name, new Vector3(0f, height - 0.25f, 3f), new Vector3(14f, 0.5f, 18f));
        }

        private AttackInfo CreateAttack(AttackShape3D shape)
        {
            MotionDefinition motion = ScriptableObject.CreateInstance<MotionDefinition>();
            motion.info = new MotionInfo { preMotionDuration = 0.2f, activeMotionDuration = 0.2f, recoveryDuration = 0.2f };
            _assets.Add(motion);
            AttackInfo attack = ScriptableObject.CreateInstance<AttackInfo>();
            attack.motion = motion;
            attack.damageMultiplier = 1f;
            attack.attackElement = Element.Slash;
            attack.attack3D = new AttackProfile3D
            {
                shape = shape,
                localCenter = new Vector3(0f, 0.9f, 1.7f),
                halfExtents = new Vector3(1.1f, 0.8f, 1.6f),
                radius = 2.2f,
                targetLayers = LayerMask.GetMask("Player", "Enemy"),
                obstructionLayers = LayerMask.GetMask("Ground")
            };
            _assets.Add(attack);
            return attack;
        }

        private AttackInfo CreateProjectile(BulletMoveType trajectory)
        {
            AttackInfo attack = CreateAttack(AttackShape3D.Projectile);
            attack.attack3D.localCenter = new Vector3(0f, 0.9f, 0.45f);
            attack.attack3D.projectileRadius = 0.08f;
            attack.projectileInfo = new ProjectileInfo
            {
                hasProjectile = true,
                trajectory = trajectory,
                speed = 10f,
                lifetime = 1.5f,
                homingStrength = 20f,
            };
            attack.multiLockRange = trajectory == BulletMoveType.Homing ? 10f : 0f;
            return attack;
        }

        private AttackInfo CreateGroundImpactProjectile()
        {
            AttackInfo attack = CreateProjectile(BulletMoveType.Straight);
            attack.projectileInfo.speed = 5f;
            attack.projectileInfo.lifetime = 3f;
            attack.projectileInfo.groundImpactRange = k_GroundProjectileRange;
            attack.projectileInfo.aoeRadius = 1.8f;
            return attack;
        }

        private void AdvanceProjectileFlight(int steps)
        {
            for (int tick = 0; tick < steps; tick++)
            {
                Step(k_PhysicsStep);
            }
        }

        private void Settle(params CharacterActor3D[] actors)
        {
            Physics.SyncTransforms();
            PhysicsScene physics = _room.GetPhysicsScene();
            for (int tick = 0; tick < 30; tick++)
            {
                foreach (CharacterActor3D actor in actors)
                {
                    actor.Motor.Simulate(Vector3.zero, false, k_PhysicsStep);
                }
                physics.Simulate(k_PhysicsStep);
            }
        }

        private static void SynchronizeWorld(Scene scene)
        {
            Physics.SyncTransforms();
            scene.GetPhysicsScene().Simulate(k_PhysicsStep);
        }

        private void Step(float deltaTime)
        {
            _time += deltaTime;
            GameManager.Instance.SimulationTick(deltaTime, _time);
        }

        private GameObject VisibleWarning()
        {
            foreach (GameObject root in _room.GetRootGameObjects())
            {
                if (root.name == k_GroundWarningName && root.activeInHierarchy)
                {
                    return root;
                }
            }
            return null;
        }

        private static void AssertWarningAt(GameObject warning, Vector3 point)
        {
            Assert.That(warning, Is.Not.Null);
            Assert.That(Vector3.Distance(warning.transform.position, point), Is.LessThan(0.08f));
            Assert.That(Vector3.Dot(warning.transform.forward, Vector3.up), Is.GreaterThan(0.98f));
        }

        private static int Hp(CharacterActor3D actor)
        {
            Assert.That(GameManager.Actors.IsCurrent(actor.Actor), Is.True);
            return GameManager.Data.GetVitals(actor.Actor.Hash).currentHp;
        }

        private static void AssertHealthy(params CharacterActor3D[] actors)
        {
            foreach (CharacterActor3D actor in actors)
            {
                Assert.That(Hp(actor), Is.EqualTo(k_InitialHp), actor.name);
            }
        }

        private void AssertSingleHit(CharacterActor3D attacker, CharacterActor3D defender, long instanceId)
        {
            Assert.That(_hits, Has.Count.EqualTo(1));
            AssertHit(attacker.Actor, defender, instanceId);
        }

        private void AssertHit(ActorRef attacker, CharacterActor3D defender, long instanceId)
        {
            int hitIndex = _hits.FindIndex(hit => hit.defender == defender.Actor.Hash);
            Assert.That(hitIndex, Is.GreaterThanOrEqualTo(0), defender.name);
            (DamageResult result, int attackerHash, int defenderHash) = _hits[hitIndex];
            Assert.That(result.totalDamage, Is.GreaterThan(0));
            Assert.That(result.actionInstanceId, Is.EqualTo(instanceId));
            Assert.That(result.attackerRef, Is.EqualTo(attacker));
            Assert.That(result.defenderRef, Is.EqualTo(defender.Actor));
            int liveAttackerHash = GameManager.Actors.IsCurrent(attacker) ? attacker.Hash : 0;
            Assert.That(attackerHash, Is.EqualTo(liveAttackerHash));
            Assert.That(defenderHash, Is.EqualTo(defender.Actor.Hash));
            Assert.That(Hp(defender), Is.EqualTo(k_InitialHp - result.totalDamage));
        }

        private void RecordDamage(DamageResult result, int attacker, int defender) => _hits.Add((result, attacker, defender));
    }
}
