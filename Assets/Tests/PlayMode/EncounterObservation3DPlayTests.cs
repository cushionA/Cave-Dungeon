using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using CharacterInfo = Game.Core.CharacterInfo;

namespace Game.Tests.PlayMode
{
    public sealed class EncounterObservation3DPlayTests
    {
        private readonly List<CharacterInfo> _characters = new List<CharacterInfo>();
        private readonly EncounterObservation3D _observation = new EncounterObservation3D();
        private Scene _room;
        private Scene _otherRoom;

        [UnitySetUp]
        public IEnumerator CreateRooms()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            TestSceneHelper.CreateGameManager();
            GameManager.Instance.enabled = false;
            CreateSceneParameters parameters = new CreateSceneParameters(LocalPhysicsMode.Physics3D);
            _room = SceneManager.CreateScene("EncounterObservationRoom", parameters);
            _otherRoom = SceneManager.CreateScene("EncounterObservationOtherRoom", parameters);
        }

        [UnityTearDown]
        public IEnumerator UnloadRooms()
        {
            if (_room.isLoaded) { yield return SceneManager.UnloadSceneAsync(_room); }
            if (_otherRoom.isLoaded) { yield return SceneManager.UnloadSceneAsync(_otherRoom); }
            foreach (CharacterInfo character in _characters) { Object.Destroy(character); }
            _characters.Clear();
            TestSceneHelper.Cleanup();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Capture_WhenRoomIsOccluded_ReportsOnlyReachableOpponentsToEachFaction()
        {
            CharacterActor3D ally = CreateActor(_room, "Ally observer", Vector3.zero, CharacterBelong.Ally);
            CharacterActor3D open = CreateActor(_room, "Visible enemy", new Vector3(0f, 0f, 3f), CharacterBelong.Enemy);
            CharacterActor3D hidden = CreateActor(_room, "Covered enemy", new Vector3(0f, 0f, -3f), CharacterBelong.Enemy);
            CharacterActor3D above = CreateActor(_room, "Enemy above range", new Vector3(0f, 6f, 0f), CharacterBelong.Enemy);
            CharacterActor3D neutral = CreateActor(_room, "Neutral actor", new Vector3(1f, 0f, 0f), CharacterBelong.Neutral);
            CharacterActor3D unlisted = CreateActor(_room, "Unlisted enemy", new Vector3(1f, 0f, 2f), CharacterBelong.Enemy);
            CharacterActor3D remote = CreateActor(_otherRoom, "Enemy in another world", Vector3.forward, CharacterBelong.Enemy);
            CreateWall(new Vector3(0f, 0.8f, -1.5f));
            CreateWall(new Vector3(0f, 0.8f, 1.5f)).isTrigger = true;

            ActorRef[] participants = { ally.Actor, open.Actor, hidden.Actor, above.Actor, neutral.Actor, remote.Actor };
            _observation.Capture(participants, 4f, LayerMask.GetMask("Ground"));

            EncounterSighting3D allyReport = _observation.SightingsFor(CharacterBelong.Ally).Single();
            Assert.That(allyReport.Observer, Is.EqualTo(ally.Actor));
            Assert.That(allyReport.Target, Is.EqualTo(open.Actor));
            Assert.That(allyReport.Position, Is.EqualTo(open.Motor.FeetPosition));
            EncounterSighting3D enemyReport = _observation.SightingsFor(CharacterBelong.Enemy).Single();
            Assert.That(enemyReport.Observer, Is.EqualTo(open.Actor));
            Assert.That(enemyReport.Target, Is.EqualTo(ally.Actor));
            Assert.That(_observation.SightingsFor(CharacterBelong.Neutral), Is.Empty);
            Assert.That(_observation.CanSee(ally.Actor, open.Actor), Is.True);
            Assert.That(_observation.CanSee(ally.Actor, hidden.Actor), Is.False);
            Assert.That(_observation.CanSee(ally.Actor, above.Actor), Is.False);
            Assert.That(_observation.CanSee(ally.Actor, unlisted.Actor), Is.False);
            Assert.That(_observation.CanSee(ally.Actor, remote.Actor), Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Capture_WhenTerrainMovesOrTargetReusesItsHash_RebuildsCurrentSightings()
        {
            CharacterActor3D ally = CreateActor(_room, "Observer", Vector3.zero, CharacterBelong.Ally);
            CharacterActor3D enemy = CreateActor(_room, "Recycled target", new Vector3(0f, 0f, 3f), CharacterBelong.Enemy);
            ActorRef original = enemy.Actor;
            BoxCollider wall = CreateWall(new Vector3(5f, 0.8f, 1.5f));
            LayerMask obstacles = LayerMask.GetMask("Ground");
            ActorRef[] participants = { ally.Actor, original };
            _observation.Capture(participants, 5f, obstacles);
            Assert.That(_observation.CanSee(ally.Actor, original), Is.True);

            wall.transform.position = new Vector3(0f, 0.8f, 1.5f);
            _observation.Capture(participants, 5f, obstacles);
            Assert.That(_observation.SightingsFor(CharacterBelong.Ally), Is.Empty);

            wall.transform.position = new Vector3(5f, 0.8f, 1.5f);
            enemy.gameObject.SetActive(false);
            enemy.gameObject.SetActive(true);
            ActorRef replacement = enemy.Actor;
            Assert.That(replacement.Hash, Is.EqualTo(original.Hash));
            Assert.That(replacement, Is.Not.EqualTo(original));

            _observation.Capture(participants, 5f, obstacles);
            Assert.That(_observation.SightingsFor(CharacterBelong.Ally), Is.Empty);
            participants[1] = replacement;
            _observation.Capture(participants, 5f, obstacles);
            Assert.That(_observation.CanSee(ally.Actor, original), Is.False);
            Assert.That(_observation.CanSee(ally.Actor, replacement), Is.True);

            GameManager.Data.GetFlags(replacement.Hash).IsVanished = true;
            _observation.Capture(participants, 5f, obstacles);
            Assert.That(_observation.SightingsFor(CharacterBelong.Ally), Is.Empty);
            GameManager.Data.GetFlags(replacement.Hash).IsVanished = false;
            GameManager.Data.GetVitals(replacement.Hash).currentHp = 0;
            _observation.Capture(participants, 5f, obstacles);
            Assert.That(_observation.SightingsFor(CharacterBelong.Enemy), Is.Empty);
            Assert.That(_observation.CanSee(ally.Actor, replacement), Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Capture_WhenParticipantsAreReorderedOrDuplicated_ProducesStableReports()
        {
            ActorRef a = CreateActor(_room, "Ally A", Vector3.zero, CharacterBelong.Ally).Actor;
            ActorRef b = CreateActor(_room, "Ally B", Vector3.right, CharacterBelong.Ally).Actor;
            ActorRef c = CreateActor(_room, "Enemy A", Vector3.forward, CharacterBelong.Enemy).Actor;
            ActorRef d = CreateActor(_room, "Enemy B", new Vector3(1f, 0f, 1f), CharacterBelong.Enemy).Actor;
            _observation.Capture(new[] { b, d, a, b, c, c }, 4f, Physics.DefaultRaycastLayers);
            (ActorRef observer, ActorRef target)[] allyPairs = GetPairs(CharacterBelong.Ally);
            (ActorRef observer, ActorRef target)[] enemyPairs = GetPairs(CharacterBelong.Enemy);
            Assert.That(allyPairs, Has.Length.EqualTo(4));
            Assert.That(enemyPairs, Has.Length.EqualTo(4));

            _observation.Capture(new[] { c, a, d, b }, 4f, Physics.DefaultRaycastLayers);
            Assert.That(GetPairs(CharacterBelong.Ally), Is.EqualTo(allyPairs));
            Assert.That(GetPairs(CharacterBelong.Enemy), Is.EqualTo(enemyPairs));
            Assert.That(_observation.SightingsFor(CharacterBelong.Ally | CharacterBelong.Enemy), Is.Empty);
            yield return null;
        }

        private (ActorRef observer, ActorRef target)[] GetPairs(CharacterBelong faction) => _observation.SightingsFor(faction)
            .Select(sighting => (sighting.Observer, sighting.Target)).ToArray();

        private CharacterActor3D CreateActor(Scene scene, string name, Vector3 position, CharacterBelong faction)
        {
            CharacterInfo character = TestSceneHelper.CreateTestCharacterInfo(faction, CharacterFeature.Minion);
            _characters.Add(character);
            GameObject gameObject = NewObject(scene, name, position);
            gameObject.SetActive(false);
            gameObject.layer = LayerMask.NameToLayer(faction == CharacterBelong.Ally ? "Player" : "Enemy");
            CharacterController body = gameObject.AddComponent<CharacterController>();
            body.height = 1.6f;
            body.radius = 0.3f;
            body.center = Vector3.up * 0.8f;
            CharacterActor3D actor = gameObject.AddComponent<CharacterActor3D>();
            actor.SetCharacterInfoForTest(character);
            gameObject.SetActive(true);
            Assert.That(actor.Actor.IsValid, Is.True);
            return actor;
        }

        private BoxCollider CreateWall(Vector3 position)
        {
            GameObject wall = NewObject(_room, "Wall", position);
            wall.layer = LayerMask.NameToLayer("Ground");
            BoxCollider collider = wall.AddComponent<BoxCollider>();
            collider.size = new Vector3(3f, 2f, 0.3f);
            return collider;
        }

        private static GameObject NewObject(Scene scene, string name, Vector3 position)
        {
            GameObject gameObject = new GameObject(name);
            SceneManager.MoveGameObjectToScene(gameObject, scene);
            gameObject.transform.position = position;
            return gameObject;
        }
    }
}
