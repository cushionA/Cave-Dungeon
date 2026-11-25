using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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
    public sealed class ExpeditionSummonDeployment3DPlayTests
    {
        private readonly List<ScriptableObject> _assets = new();
        private readonly List<ExpeditionSummonDeployment3D.DeployedSummon> _summons = new();
        private Scene _room;
        private ExpeditionSquad _squad;
        private ExpeditionSummonDeployment3D _deployment;
        private CharacterActor3D _scoutPrefab;
        private CharacterActor3D _guardPrefab;
        private Transform[] _positions;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            _room = SceneManager.CreateScene(nameof(ExpeditionSummonDeployment3DPlayTests),
                new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            ExpeditionSquadRules rules = ScriptableObject.CreateInstance<ExpeditionSquadRules>();
            _assets.Add(rules);
            SetSerializedField(rules, "maxCount", 3);
            SetSerializedField(rules, "maxCost", 4);
            SetSerializedField(rules, "species", new[]
            {
                new ExpeditionSquad.Species("scout", 1),
                new ExpeditionSquad.Species("guard", 2)
            });

            GameObject world = new GameObject("Expedition game manager");
            world.SetActive(false);
            ExpeditionSquadManager roster = world.AddComponent<ExpeditionSquadManager>();
            SetSerializedField(roster, "rules", rules);
            world.AddComponent<GameManager>();
            world.SetActive(true);
            GameManager.Instance.enabled = false;
            _squad = roster.Squad;
            Assert.That(_squad, Is.Not.Null);
            Unlock("scout");
            Unlock("guard");
            Assert.That(_squad.TryConfirm(new[]
            {
                new ExpeditionSquad.Selection("scout", 2),
                new ExpeditionSquad.Selection("guard", 1)
            }), Is.True);

            CreateBox("Room floor", new Vector3(0f, -0.25f, 0f), new Vector3(14f, 0.5f, 14f));
            CharacterActor3D sister = CreateCharacter("Sister", new Vector3(0f, 0.04f, -4f), CharacterFeature.Companion);
            _deployment = sister.gameObject.AddComponent<ExpeditionSummonDeployment3D>();
            sister.gameObject.SetActive(true);
            _scoutPrefab = CreateCharacter("Scout template", Vector3.zero, CharacterFeature.Summon);
            _guardPrefab = CreateCharacter("Guard template", Vector3.zero, CharacterFeature.Summon);
            _positions = new[]
            {
                NewObject("Spawn 1", new Vector3(-2.5f, 0.02f, 1f)).transform,
                NewObject("Spawn 2", new Vector3(0f, 0.02f, 1f)).transform,
                NewObject("Spawn 3", new Vector3(2.5f, 0.02f, 1f)).transform
            };
            Configure();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_room.isLoaded) { yield return SceneManager.UnloadSceneAsync(_room); }
            foreach (ScriptableObject asset in _assets) { Object.Destroy(asset); }
            _assets.Clear();
            _summons.Clear();
            TestSceneHelper.Cleanup();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Deployment_ConfirmedSpeciesAndAuthoredSlots_RegisterEachCopyAndJoinTheSceneEncounter()
        {
            EncounterCombat3D encounter = NewObject("Encounter", Vector3.zero).AddComponent<EncounterCombat3D>();
            encounter.enabled = false;
            _scoutPrefab.gameObject.AddComponent<EncounterCombatant3D>();
            _guardPrefab.gameObject.AddComponent<EncounterCombatant3D>();
            Configure(encounter);
            int before = GameManager.Data.Count;

            _deployment.DeployConfirmed();
            Assert.That(_deployment.ActiveCount, Is.EqualTo(3));
            Assert.That(GameManager.Data.Count, Is.EqualTo(before + 3));
            _deployment.CopyActiveSummons(_summons);
            Assert.That(_summons, Has.Count.EqualTo(3));
            HashSet<ActorRef> identities = new HashSet<ActorRef>();
            for (int i = 0; i < _summons.Count; i++)
            {
                ExpeditionSummonDeployment3D.DeployedSummon summon = _summons[i];
                Assert.That(summon.SpeciesId, Is.EqualTo(i == 2 ? "guard" : "scout"));
                Assert.That(summon.IsReinforcement, Is.False);
                Assert.That(identities.Add(summon.Actor), Is.True);
                CharacterActor3D unit = GetUnit(summon.Actor);
                Assert.That(unit.transform.position, Is.EqualTo(_positions[i].position));
                Assert.That(unit.gameObject.scene, Is.EqualTo(_room));
                Assert.That(GameManager.Data.GetFlags(summon.Actor.Hash).Belong, Is.EqualTo(CharacterBelong.Ally));
                Assert.That(encounter.Register(unit.GetComponent<EncounterCombatant3D>()), Is.False,
                    "An individually deployed combatant already belongs to the encounter.");
            }
            Assert.That(_deployment.TryDeployConfirmed(), Is.False);
            Assert.That(GameManager.Data.Count, Is.EqualTo(before + 3));
            yield return null;
        }

        [UnityTest]
        public IEnumerator Deployment_WhenAnyPrefabOrNativeSlotIsUnavailable_DoesNotPartiallyRegister()
        {
            int before = GameManager.Data.Count;
            int additions = 0;
            using IDisposable registrations = GameManager.Events.OnCharacterRegistered.Subscribe(_ => additions++);
            _deployment.ConfigureForTest(new[] { new ExpeditionSummonDeployment3D.SpeciesPrefab("scout", _scoutPrefab) },
                _positions, Physics.DefaultRaycastLayers);
            Assert.That(_deployment.TryDeployConfirmed(), Is.False);
            Assert.That(_deployment.ActiveCount, Is.Zero);
            Assert.That(GameManager.Data.Count, Is.EqualTo(before));
            Assert.That(additions, Is.Zero);

            Configure();
            BoxCollider wall = CreateBox("Blocked third slot", _positions[2].position + Vector3.up * 0.9f,
                new Vector3(1f, 2f, 1f));
            Assert.That(_deployment.TryDeployConfirmed(), Is.False);
            Assert.That(_deployment.ActiveCount, Is.Zero);
            Assert.That(GameManager.Data.Count, Is.EqualTo(before));
            Assert.That(additions, Is.Zero);
            Assert.That(_squad.ConfirmedCount, Is.EqualTo(3));

            wall.enabled = false;
            Assert.That(_deployment.TryDeployConfirmed(), Is.True);
            Assert.That(additions, Is.EqualTo(3));
            Assert.That(_deployment.ActiveCount, Is.EqualTo(3));
            yield return null;
        }

        [UnityTest]
        public IEnumerator Deployment_WhenAnIndividualReturnsOrDies_OlderHandlesCannotRemoveTheNewGeneration()
        {
            Assert.That(_deployment.TryDeployConfirmed(), Is.True);
            _deployment.CopyActiveSummons(_summons);
            ActorRef old = _summons[0].Actor;
            ActorRef survivor = _summons[1].Actor;
            ActorRef fatality = _summons[2].Actor;
            CharacterActor3D reused = GetUnit(old);
            reused.gameObject.SetActive(false);
            reused.gameObject.SetActive(true);
            ActorRef current = reused.Actor;
            Assert.That(current.Hash, Is.EqualTo(old.Hash));
            Assert.That(current, Is.Not.EqualTo(old));
            Assert.That(_deployment.TryDismiss(old), Is.False);
            Assert.That(_deployment.ActiveCount, Is.EqualTo(3));

            Assert.That(_deployment.TryDismiss(current), Is.True);
            Assert.That(GameManager.Actors.IsCurrent(current), Is.False);
            GameManager.Data.GetVitals(fatality.Hash).currentHp = 0;
            yield return null;
            yield return null;
            Assert.That(GameManager.Actors.IsCurrent(fatality), Is.False);
            Assert.That(GameManager.Actors.IsCurrent(survivor), Is.True);
            Assert.That(_deployment.ActiveCount, Is.EqualTo(1));
            Assert.That(_deployment.TryDeployConfirmed(), Is.False);
            Assert.That(_squad.ConfirmedCount, Is.EqualTo(3));
            Assert.That(GameManager.ExpeditionAid.IsUnlocked("scout"), Is.True);

            _deployment.DismissAll();
            Assert.That(_deployment.ActiveCount, Is.Zero);
            Assert.That(GameManager.Actors.IsCurrent(survivor), Is.False);
        }

        [UnityTest]
        public IEnumerator Deployment_WhenCancelledDuringRegistration_RollsBackTheWholeGroupAndCanRetry()
        {
            int before = GameManager.Data.Count;
            using (GameManager.Events.OnCharacterRegistered.Subscribe(_ => _deployment.DismissAll()))
            {
                Assert.That(_deployment.TryDeployConfirmed(), Is.False);
            }
            Assert.That(GameManager.Data.Count, Is.EqualTo(before));
            Assert.That(_deployment.ActiveCount, Is.Zero);
            Assert.That(_squad.ConfirmedCount, Is.EqualTo(3));
            Assert.That(_deployment.TryDeployConfirmed(), Is.True);
            Assert.That(_deployment.ActiveCount, Is.EqualTo(3));
            yield return null;
        }

        [UnityTest]
        public IEnumerator Deployment_WhenReinforcementsShareSlots_RespectsOccupiedBodiesAndAttemptOwnership()
        {
            Transform extra = NewObject("Spawn 4", new Vector3(0f, 0.02f, 3.5f)).transform;
            _scoutPrefab.gameObject.layer = LayerMask.NameToLayer("Companion");
            _guardPrefab.gameObject.layer = LayerMask.NameToLayer("Companion");
            _deployment.ConfigureForTest(new[]
            {
                new ExpeditionSummonDeployment3D.SpeciesPrefab("scout", _scoutPrefab),
                new ExpeditionSummonDeployment3D.SpeciesPrefab("guard", _guardPrefab)
            }, new[] { _positions[0], _positions[1], _positions[2], extra }, 0);

            Assert.That(_deployment.TryDeployConfirmed(), Is.True);
            object attempt = new object();
            Assert.That(_deployment.TryDeployReinforcements(attempt, new[] { new ExpeditionSquad.Selection("guard", 1) }), Is.True);
            _deployment.CopyActiveSummons(_summons);
            Assert.That(_summons, Has.Count.EqualTo(4));
            ActorRef reinforcement = _summons[3].Actor;
            Assert.That(_summons[3].IsReinforcement, Is.True);
            Assert.That(GetUnit(reinforcement).transform.position, Is.EqualTo(extra.position));

            _deployment.DismissReinforcements(new object());
            Assert.That(_deployment.ActiveCount, Is.EqualTo(4));
            _deployment.DismissReinforcements(attempt);
            Assert.That(_deployment.ActiveCount, Is.EqualTo(3));
            Assert.That(GameManager.Actors.IsCurrent(reinforcement), Is.False);
            for (int i = 0; i < 3; i++)
            {
                Assert.That(GameManager.Actors.IsCurrent(_summons[i].Actor), Is.True);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator NormalDeployment_WhenKnightWalks_FollowsAndHonorsMovementStatus()
        {
            Assert.That(_squad.TryConfirm(new[] { new ExpeditionSquad.Selection("scout", 1) }), Is.True);
            CharacterActor3D knight = CreateCharacter("Knight", new Vector3(-2.5f, 0.04f, 2.4f), CharacterFeature.Player);
            knight.gameObject.SetActive(true);
            Configure(leader: knight);
            Assert.That(_deployment.TryDeployConfirmed(), Is.True);
            _deployment.CopyActiveSummons(_summons);
            ActorRef identity = _summons[0].Actor;
            CharacterActor3D scout = GetUnit(identity);

            for (int i = 0; i < 5; i++) { yield return new WaitForFixedUpdate(); }
            Assert.That(scout.transform.position.z, Is.EqualTo(_positions[0].position.z).Within(0.05f));

            Assert.That(GameManager.StatusEffects.Apply(identity.Hash, new StatusEffectInfo
            {
                effect = StatusEffectId.Freeze, duration = 10f, maxStack = 1,
            }), Is.True);
            for (int i = 0; i < 36; i++)
            {
                knight.Motor.Simulate(Vector3.forward, false, Time.fixedDeltaTime);
                yield return new WaitForFixedUpdate();
            }
            Assert.That(scout.transform.position.z, Is.EqualTo(_positions[0].position.z).Within(0.05f));
            Assert.That(GameManager.StatusEffects.Remove(identity.Hash, StatusEffectId.Freeze), Is.True);

            for (int i = 0; i < 50; i++) { yield return new WaitForFixedUpdate(); }
            Assert.That(scout.transform.position.z, Is.GreaterThan(_positions[0].position.z + 2f));
            Assert.That(knight.transform.position.z - scout.transform.position.z, Is.InRange(2.05f, 2.22f));
            Assert.That(scout.Motor.IsGrounded, Is.True);
            Assert.That(scout.Actor, Is.EqualTo(identity));
        }

        [UnityTest]
        public IEnumerator NormalDeployment_WhenFollowDistanceIsInvalid_WarnsOnceAndResumesAfterCorrection()
        {
            const string warning = "[ExpeditionSummonDeployment3D] 追従距離には0.1以上の有限値を設定してください。";
            Assert.That(_squad.TryConfirm(new[] { new ExpeditionSquad.Selection("scout", 1) }), Is.True);
            CharacterActor3D knight = CreateCharacter("Knight", new Vector3(-2.5f, 0.04f, 5f), CharacterFeature.Player);
            knight.gameObject.SetActive(true);
            Configure(leader: knight);
            SetSerializedField(_deployment, "followDistance", 0f);
            Assert.That(_deployment.TryDeployConfirmed(), Is.True);
            _deployment.CopyActiveSummons(_summons);
            ActorRef identity = _summons[0].Actor;
            CharacterActor3D scout = GetUnit(identity);
            float spawnZ = scout.transform.position.z;

            int warnings = 0;
            void CountWarning(string message, string stackTrace, LogType type)
            {
                if (type == LogType.Warning && message == warning) { warnings++; }
            }

            Application.logMessageReceived += CountWarning;
            try
            {
                LogAssert.Expect(LogType.Warning, warning);
                for (int i = 0; i < 5; i++) { yield return new WaitForFixedUpdate(); }
                SetSerializedField(_deployment, "followDistance", float.NaN);
                for (int i = 0; i < 5; i++) { yield return new WaitForFixedUpdate(); }

                Assert.That(warnings, Is.EqualTo(1));
                Assert.That(scout.transform.position.z, Is.EqualTo(spawnZ).Within(0.05f));

                SetSerializedField(_deployment, "followDistance", 2.2f);
                for (int i = 0; i < 18; i++) { yield return new WaitForFixedUpdate(); }
                Assert.That(scout.transform.position.z, Is.GreaterThan(spawnZ + 0.5f));
                Assert.That(scout.Actor, Is.EqualTo(identity));
            }
            finally { Application.logMessageReceived -= CountWarning; }
        }

        [UnityTest]
        public IEnumerator NormalDeployment_WhenEncounterStopsDriving_ResumesOnlyNormalFollow()
        {
            Assert.That(_squad.TryConfirm(new[] { new ExpeditionSquad.Selection("scout", 1) }), Is.True);
            CharacterActor3D knight = CreateCharacter("Knight", new Vector3(-2.5f, 0.04f, 5f), CharacterFeature.Player);
            knight.gameObject.SetActive(true);
            EncounterCombat3D encounter = NewObject("Encounter", Vector3.zero).AddComponent<EncounterCombat3D>();
            _scoutPrefab.gameObject.AddComponent<EncounterCombatant3D>();
            _guardPrefab.gameObject.AddComponent<EncounterCombatant3D>();
            Configure(encounter, knight);
            Assert.That(_deployment.TryDeployConfirmed(), Is.True);
            object attempt = new object();
            Assert.That(_deployment.TryDeployReinforcements(attempt, new[] { new ExpeditionSquad.Selection("guard", 1) }), Is.True);
            _deployment.CopyActiveSummons(_summons);
            CharacterActor3D scout = GetUnit(_summons[0].Actor);
            CharacterActor3D reinforcement = GetUnit(_summons[1].Actor);
            float reinforcementStart = reinforcement.transform.position.z;

            for (int i = 0; i < 20; i++) { yield return new WaitForFixedUpdate(); }
            Assert.That(scout.transform.position.z, Is.EqualTo(_positions[0].position.z).Within(0.05f));

            encounter.enabled = false;
            for (int i = 0; i < 30; i++) { yield return new WaitForFixedUpdate(); }
            Assert.That(scout.transform.position.z, Is.GreaterThan(_positions[0].position.z + 1f));
            Assert.That(reinforcement.transform.position.z, Is.EqualTo(reinforcementStart).Within(0.05f));
            _deployment.DismissReinforcements(attempt);
            Assert.That(reinforcement.Actor.IsValid, Is.False);
            Assert.That(scout.Actor, Is.EqualTo(_summons[0].Actor));
        }

        [UnityTest]
        public IEnumerator NormalDeployment_WhenEncounterStartsBetweenDrivers_ConsumesExactlyOneFixedStep()
            => CheckEncounterTransitionSteps(start: true);

        [UnityTest]
        public IEnumerator NormalDeployment_WhenEncounterStopsBetweenDrivers_ConsumesExactlyOneFixedStep()
            => CheckEncounterTransitionSteps(start: false);

        private IEnumerator CheckEncounterTransitionSteps(bool start)
        {
            const float moveSpeed = 3f;
            Vector3 spawn = _positions[0].position;
            Assert.That(_squad.TryConfirm(new[] { new ExpeditionSquad.Selection("scout", 1) }), Is.True);
            CharacterActor3D knight = CreateCharacter("Knight", spawn + Vector3.forward * 4f, CharacterFeature.Player);
            knight.gameObject.SetActive(true);
            EncounterSession3D session = NewObject("Group objective", Vector3.zero).AddComponent<EncounterSession3D>();
            ChallengeDefinition challenge = ScriptableObject.CreateInstance<ChallengeDefinition>();
            _assets.Add(challenge);
            challenge.SetForTest("handoff", "Group objective", ChallengeType.TimeAttack);
            session.ConfigureForTest(knight, challenge, EncounterTraits.GroupBattle);

            EncounterCombat3D encounter = NewObject("Group combat", Vector3.zero).AddComponent<EncounterCombat3D>();
            SetSerializedField(encounter, "knight", knight);
            SetSerializedField(encounter, "objective", session);
            EncounterCombatant3D combatant = _scoutPrefab.gameObject.AddComponent<EncounterCombatant3D>();
            SetSerializedField(combatant, "home", NewObject("Combat destination", spawn + Vector3.right * 4f).transform);
            SetSerializedField(_scoutPrefab.GetComponent<CharacterMotor3D>(), "moveSpeed", moveSpeed);
            Configure(encounter, knight);

            for (int ordering = 0; ordering < 2; ordering++)
            {
                // この固定更新の通常コールバックを終えてから、まだ未駆動の個体を同じ時刻に登録する。
                yield return new WaitForFixedUpdate();
                if (!start) { Assert.That(session.TryStartEncounter(), Is.True); }
                Assert.That(_deployment.TryDeployConfirmed(), Is.True);
                _deployment.CopyActiveSummons(_summons);
                CharacterActor3D scout = GetUnit(_summons[0].Actor);
                Vector3 before = scout.transform.position;
                bool encounterFirst = ordering == 0;
                MonoBehaviour first = encounterFirst ? encounter : _deployment;
                MonoBehaviour second = encounterFirst ? _deployment : encounter;

                // 探索は+Z、集団戦は+Xへ進む。実際の移動量で0回・1回・2回を区別する。
                InvokeFixedStep(first);
                if (start) { Assert.That(session.TryStartEncounter(), Is.True); }
                else { session.AbortEncounter(); }
                Assert.That(session.IsRunning, Is.EqualTo(start));
                InvokeFixedStep(second);

                Vector3 actual = scout.transform.position - before;
                Vector3 direction = encounterFirst && !start ? Vector3.right : Vector3.forward;
                float expectedDistance = moveSpeed * Time.fixedDeltaTime;
                Assert.That(actual.x, Is.EqualTo(direction.x * expectedDistance).Within(0.005f),
                    $"start={start}, encounterFirst={encounterFirst} (X)");
                Assert.That(actual.z, Is.EqualTo(direction.z * expectedDistance).Within(0.005f),
                    $"start={start}, encounterFirst={encounterFirst} (Z)");

                session.AbortEncounter();
                _deployment.DismissAll();
            }
        }

        private static void InvokeFixedStep(MonoBehaviour controller)
        {
            MethodInfo fixedUpdate = controller.GetType().GetMethod("FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(fixedUpdate, Is.Not.Null);
            fixedUpdate.Invoke(controller, null);
        }

        private CharacterActor3D CreateCharacter(string name, Vector3 position, CharacterFeature feature)
        {
            GameObject gameObject = NewObject(name, position);
            gameObject.SetActive(false);
            CharacterController body = gameObject.AddComponent<CharacterController>();
            body.height = 1.8f;
            body.radius = 0.32f;
            body.center = Vector3.up * 0.9f;
            body.skinWidth = 0.04f;
            CharacterActor3D actor = gameObject.AddComponent<CharacterActor3D>();
            Game.Core.CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(CharacterBelong.Ally, feature);
            _assets.Add(info);
            actor.SetCharacterInfoForTest(info);
            return actor;
        }

        private GameObject NewObject(string name, Vector3 position)
        {
            GameObject gameObject = new GameObject(name);
            SceneManager.MoveGameObjectToScene(gameObject, _room);
            gameObject.transform.position = position;
            return gameObject;
        }

        private BoxCollider CreateBox(string name, Vector3 position, Vector3 size)
        {
            GameObject gameObject = NewObject(name, position);
            BoxCollider box = gameObject.AddComponent<BoxCollider>();
            box.size = size;
            return box;
        }

        private static CharacterActor3D GetUnit(ActorRef actor)
        {
            Assert.That(GameManager.Actors.IsCurrent(actor), Is.True);
            CharacterActor3D unit = GameManager.Data.GetManaged(actor.Hash) as CharacterActor3D;
            Assert.That(unit, Is.Not.Null);
            return unit;
        }

        private void Configure(EncounterCombat3D encounter = null, CharacterActor3D leader = null)
            => _deployment.ConfigureForTest(new[]
            {
                new ExpeditionSummonDeployment3D.SpeciesPrefab("scout", _scoutPrefab),
                new ExpeditionSummonDeployment3D.SpeciesPrefab("guard", _guardPrefab)
            }, _positions, Physics.DefaultRaycastLayers, encounter, leader);

        private static void Unlock(string species)
        {
            ExpeditionAidUnlocks.Completion[] events = { new ExpeditionAidUnlocks.Completion(species, species) };
            Assert.That(GameManager.ExpeditionAid.TryComplete(species, events), Is.True);
        }

        private static void SetSerializedField(Object instance, string fieldName, object value)
        {
            FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(instance, value);
        }
    }
}
