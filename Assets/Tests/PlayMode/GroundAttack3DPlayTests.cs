using System;
using System.Collections;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests.PlayMode
{
    public sealed class GroundAttack3DPlayTests
    {
        private const float k_Step = 0.02f;
        private Scene _room;
        private PhysicsScene _physics;
        private Game.Core.CharacterInfo _characterInfo;

        [UnitySetUp]
        public IEnumerator CreateRoom()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            TestSceneHelper.CreateGameManager();
            GameManager.Instance.enabled = false;
            _room = SceneManager.CreateScene(nameof(GroundAttack3DPlayTests),
                new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            _physics = _room.GetPhysicsScene();
            _characterInfo = TestSceneHelper.CreateTestCharacterInfo();
        }

        [UnityTearDown]
        public IEnumerator UnloadRoom()
        {
            if (_room.isLoaded) { yield return SceneManager.UnloadSceneAsync(_room); }
            if (_characterInfo != null) { Object.Destroy(_characterInfo); }
            TestSceneHelper.Cleanup();
            yield return null;
        }

        [UnityTest]
        public IEnumerator GroundAttack_WhenBridgeIsAboveAnotherFloor_ClipsWarningAndDistinguishesOccupants()
        {
            BoxCollider upperFloor = CreateFloor("Narrow bridge", 4f, 2f);
            BoxCollider lowerFloor = CreateFloor("Floor under bridge", 0f, 10f);
            CharacterActor3D caster = CreateActor("Caster on bridge", new Vector3(0f, 4.15f, 0f));
            CharacterActor3D nearby = CreateActor("On bridge within radius", new Vector3(0f, 4.15f, 1.15f));
            CharacterActor3D distant = CreateActor("On bridge beyond radius", new Vector3(0f, 4.15f, 3.15f));
            CharacterActor3D below = CreateActor("Below the bridge", new Vector3(0f, 0.15f, 1.15f));
            Settle(caster, nearby, distant, below);

            Assert.AreSame(upperFloor, caster.Motor.Ground.Surface);
            Assert.AreSame(upperFloor, nearby.Motor.Ground.Surface);
            Assert.AreSame(upperFloor, distant.Motor.Ground.Surface);
            Assert.AreSame(lowerFloor, below.Motor.Ground.Surface);
            Assert.IsTrue(GroundAttack3D.TryCreate(caster, 2f, out GroundAttack3D blast));
            Assert.IsTrue(blast.Contains(nearby));
            Assert.IsFalse(blast.Contains(distant));
            Assert.IsFalse(blast.Contains(below));

            GameObject warning = blast.ShowWarning();
            Assert.IsNotNull(warning);
            Assert.AreEqual(_room, warning.scene);
            Assert.That(warning.transform.position.y, Is.EqualTo(4f).Within(0.04f));
            Assert.That(Vector3.Dot(warning.transform.forward, Vector3.up), Is.GreaterThan(0.99f));
            Texture2D texture = warning.GetComponent<SpriteRenderer>().sprite.texture;
            int center = texture.width / 2;
            Assert.That(texture.GetPixel(center, center).a, Is.InRange(0.1f, 0.4f), "The top of the bridge has a red fill.");
            Assert.That(texture.GetPixel(center * 7 / 4, center).a, Is.Zero, "The disk does not extend beyond the bridge.");
            Assert.IsTrue(Array.Exists(texture.GetPixels32(), pixel => pixel.r == 255 && pixel.a > 200),
                "The clipped area retains an outline.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator GroundAttack_WhenItsCapturedFloorMovesOrDisappears_UpdatesAndClearsWarning()
        {
            BoxCollider floor = CreateFloor("Platform", 0f, 4f);
            CharacterActor3D caster = CreateActor("Platform caster", new Vector3(0f, 0.15f, 0f));
            Settle(caster);
            Assert.IsTrue(GroundAttack3D.TryCreate(caster, 1f, out GroundAttack3D blast));
            GameObject warning = blast.ShowWarning();
            Assert.IsNotNull(warning);
            Sprite sprite = warning.GetComponent<SpriteRenderer>().sprite;
            Texture2D texture = sprite.texture;
            Vector3 originalCenter = blast.Center;
            float originalWarningY = warning.transform.position.y;

            floor.transform.position += Vector3.up;
            Physics.SyncTransforms();
            yield return null;
            yield return null;
            Assert.That(blast.Center.y, Is.EqualTo(originalCenter.y + 1f).Within(0.01f));
            Assert.That(warning.transform.position.y, Is.EqualTo(originalWarningY + 1f).Within(0.01f));

            floor.gameObject.SetActive(false);
            yield return null;
            yield return null;
            Assert.IsFalse(blast.Contains(caster));
            Assert.IsTrue(warning == null);
            Assert.IsTrue(sprite == null);
            Assert.IsTrue(texture == null);
        }

        [UnityTest]
        public IEnumerator GroundAttack_WhenAimingAheadFromABridge_UsesOnlyTheIntendedTopPatch()
        {
            BoxCollider bridge = CreateFloor("Bridge", 4f, 2f);
            BoxCollider lower = CreateFloor("Floor under bridge", 0f, 10f);
            lower.size = new Vector3(10f, 0.5f, 18f);
            CharacterActor3D caster = CreateActor("Bridge caster", new Vector3(0f, 4.15f, 0f));
            Settle(caster);

            Assert.IsTrue(GroundAttack3D.TryCreateAhead(caster, 2f, 0.75f, out GroundAttack3D impact));
            Assert.AreSame(bridge, impact.Surface);
            Assert.That(impact.Center.z, Is.EqualTo(2f).Within(0.01f));
            Assert.That(impact.Center.y, Is.EqualTo(4f).Within(0.01f));
            Assert.That(Vector3.Dot(impact.Normal, Vector3.up), Is.GreaterThan(0.99f));
            Assert.IsTrue(impact.BelongsTo(_physics));

            Assert.IsTrue(bridge.Raycast(new Ray(impact.Center + Vector3.up, Vector3.down), out RaycastHit topHit, 1.1f));
            Assert.IsTrue(impact.IsLandingContact(topHit));
            Assert.IsTrue(bridge.Raycast(new Ray(new Vector3(2f, 3.75f, 2f), Vector3.left), out RaycastHit sideHit, 1.1f));
            Assert.IsFalse(impact.IsLandingContact(sideHit));
            Assert.IsTrue(bridge.Raycast(new Ray(new Vector3(0f, 5f, 3f), Vector3.down), out RaycastHit awayHit, 1.1f));
            Assert.IsFalse(impact.IsLandingContact(awayHit));

            Assert.IsFalse(GroundAttack3D.TryCreateAhead(caster, 6f, 0.75f, out _),
                "The lower floor beneath the open edge must remain unmarked.");
            caster.Motor.Simulate(Vector3.zero, true, k_Step);
            Assert.IsFalse(GroundAttack3D.TryCreateAhead(caster, 2f, 0.75f, out _),
                "An airborne caster has no surface to extend.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator GroundAttack_WhenAimingAcrossAFloorSeam_PrefersOriginalSupportWithinItsExtent()
        {
            BoxCollider origin = CreateFloor("Origin floor", 4f, 2f);
            BoxCollider continuation = CreateFloor("Continuation floor", 4.04f, 2f);
            continuation.transform.position += Vector3.forward * 6f;
            continuation.size = new Vector3(2f, 0.5f, 3f);
            CharacterActor3D caster = CreateActor("Floor caster", new Vector3(0f, 4.15f, 0f));
            Settle(caster);

            Assert.IsTrue(GroundAttack3D.TryCreateAhead(caster, 4.75f, 0.75f, out GroundAttack3D overlap));
            Assert.AreSame(origin, overlap.Surface);
            Assert.IsTrue(GroundAttack3D.TryCreateAhead(caster, 6f, 0.75f, out GroundAttack3D across));
            Assert.AreSame(continuation, across.Surface);
            Assert.That(across.Center.y, Is.EqualTo(4.04f).Within(0.01f));
            yield return null;
        }

        private BoxCollider CreateFloor(string name, float top, float width)
        {
            GameObject floor = NewRoomObject(name, new Vector3(0f, top - 0.25f, 0f));
            floor.layer = LayerMask.NameToLayer("Ground");
            BoxCollider collider = floor.AddComponent<BoxCollider>();
            collider.size = new Vector3(width, 0.5f, 10f);
            return collider;
        }

        private CharacterActor3D CreateActor(string name, Vector3 feet)
        {
            GameObject body = NewRoomObject(name, feet);
            body.SetActive(false);
            body.layer = LayerMask.NameToLayer("Player");
            CharacterController controller = body.AddComponent<CharacterController>();
            controller.height = 1.6f;
            controller.center = Vector3.up * 0.8f;
            controller.radius = 0.3f;
            controller.skinWidth = 0.03f;
            CharacterActor3D actor = body.AddComponent<CharacterActor3D>();
            actor.SetCharacterInfoForTest(_characterInfo);
            body.SetActive(true);
            Assert.IsTrue(actor.Actor.IsValid);
            return actor;
        }

        private GameObject NewRoomObject(string name, Vector3 position)
        {
            GameObject gameObject = new GameObject(name);
            SceneManager.MoveGameObjectToScene(gameObject, _room);
            gameObject.transform.position = position;
            return gameObject;
        }

        private void Settle(params CharacterActor3D[] actors)
        {
            Physics.SyncTransforms();
            for (int i = 0; i < 30; i++)
            {
                foreach (CharacterActor3D actor in actors)
                {
                    actor.Motor.Simulate(Vector3.zero, false, k_Step);
                }
                _physics.Simulate(k_Step);
            }
        }
    }
}
