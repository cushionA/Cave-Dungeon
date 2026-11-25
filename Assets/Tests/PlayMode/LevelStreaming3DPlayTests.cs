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
using CharacterInfo = Game.Core.CharacterInfo;

namespace Game.Tests.PlayMode
{
    public sealed class LevelStreaming3DPlayTests
    {
        private const string k_AreaA = "Assets/Scenes/StreamingAreaA3DTestScene.unity";
        private const string k_AreaB = "Assets/Scenes/StreamingAreaB3DTestScene.unity";
        private const string k_NotRegistered = "Assets/Scenes/StreamingMissingTestScene.unity";
        private readonly List<CharacterInfo> _characters = new List<CharacterInfo>();
        private Scene _origin;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (string path in new[] { k_AreaA, k_AreaB })
            {
                Scene area = SceneManager.GetSceneByPath(path);
                if (area.isLoaded) { yield return SceneManager.UnloadSceneAsync(area); }
            }
            if (_origin.isLoaded) { yield return SceneManager.UnloadSceneAsync(_origin); }
            foreach (CharacterInfo info in _characters) { Object.Destroy(info); }
            _characters.Clear();
            TestSceneHelper.Cleanup();
            yield return null;
        }

        [UnityTest]
        public IEnumerator NativeBoundary_StreamsActualAdjacentAreasAndPreservesPersistentKnight()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            _origin = SceneManager.CreateScene(nameof(LevelStreaming3DPlayTests));
            LevelStreamingController streaming = CreateStreaming();
            CreateOriginGeometry();
            CharacterActor3D knight = CreateActor("Native Knight", CharacterFeature.Player, new Vector3(0f, 0.04f, 1.2f));
            CharacterActor3D companion = CreateActor("Native Companion", CharacterFeature.Companion, new Vector3(1.3f, 0.04f, -2f));
            Assert.That(knight.Actor.IsValid, Is.True);
            ActorRef registration = knight.Actor;
            var started = new List<string>();
            var loaded = new List<string>();
            var unloaded = new List<string>();
            using var starts = GameManager.Events.OnSceneLoadStarted.Subscribe(started.Add);
            streaming.Orchestrator.OnAreaLoadCompleted += loaded.Add;
            streaming.Orchestrator.OnAreaUnloadCompleted += unloaded.Add;

            yield return DriveTo(companion, 1.15f);
            yield return DriveTo(knight, -1.4f);
            Assert.That(started, Is.Empty, "A companion and a knight crossing against the configured direction must leave the boundary idle.");

            LogAssert.Expect(LogType.Error, $"[LevelStreamingController] Scene load failed for '{k_NotRegistered}'. No fallback scene configured.");
            Assert.IsTrue(streaming.RequestAreaLoad(k_NotRegistered));
            yield return DriveTo(knight, 1.25f);
            yield return WaitForSceneChange(loaded, k_AreaA);
            Scene areaA = SceneManager.GetSceneByPath(k_AreaA);
            Assert.That(areaA.isLoaded, Is.True);
            Assert.That(areaA.GetPhysicsScene(), Is.EqualTo(_origin.GetPhysicsScene()));
            Assert.That(streaming.Orchestrator.ActiveScene, Is.EqualTo(k_AreaA));
            Assert.That(streaming.RequestAreaUnload(_origin.name), Is.False);

            yield return DriveTo(knight, 5.8f);
            yield return WaitForSceneChange(loaded, k_AreaB);
            Scene areaB = SceneManager.GetSceneByPath(k_AreaB);
            Assert.That(areaB.isLoaded, Is.True);
            Assert.That(areaB.GetPhysicsScene(), Is.EqualTo(_origin.GetPhysicsScene()));
            Assert.That(areaA.isLoaded, Is.True, "The entering area must coexist with the area behind until the exit boundary.");
            Assert.That(streaming.Orchestrator.ActiveScene, Is.EqualTo(k_AreaB));

            yield return DriveTo(knight, 9.7f);
            yield return WaitForSceneChange(unloaded, k_AreaA);
            Assert.That(SceneManager.GetSceneByPath(k_AreaA).isLoaded, Is.False);
            Assert.That(areaB.isLoaded, Is.True);
            Assert.That(_origin.isLoaded, Is.True);
            Assert.That(knight.Motor.Ground.Surface.gameObject.scene, Is.EqualTo(areaB));
            Assert.That(knight.Actor, Is.EqualTo(registration));
            Assert.That(knight.gameObject.scene, Is.EqualTo(_origin));
            Assert.That(started, Is.EqualTo(new[] { k_NotRegistered, k_AreaA, k_AreaB }));
            Assert.That(loaded, Is.EqualTo(new[] { k_AreaA, k_AreaB }));
            Assert.That(unloaded, Is.EqualTo(new[] { k_AreaA }));

            yield return SceneManager.UnloadSceneAsync(areaB);
            Assert.That(streaming.Orchestrator.IsLoaded(k_AreaB), Is.False);
            Assert.That(streaming.Orchestrator.ActiveScene, Is.EqualTo(_origin.name));
            Assert.That(unloaded, Is.EqualTo(new[] { k_AreaA }));
            Assert.That(streaming.RequestAreaLoad(k_AreaB), Is.True);
            float reloadDeadline = Time.realtimeSinceStartup + 5f;
            while (loaded.Count < 3 && Time.realtimeSinceStartup < reloadDeadline) { yield return null; }
            Assert.That(loaded, Is.EqualTo(new[] { k_AreaA, k_AreaB, k_AreaB }));
            Assert.That(SceneManager.GetSceneByPath(k_AreaB).isLoaded, Is.True);
        }

        private LevelStreamingController CreateStreaming()
        {
            GameObject manager = NewRoot("Streaming test GameManager", Vector3.zero);
            manager.SetActive(false);
            LevelStreamingController streaming = manager.AddComponent<LevelStreamingController>();
            SetField(streaming, "persistentSceneName", _origin.name);
            manager.AddComponent<GameManager>();
            manager.SetActive(true);
            return streaming;
        }

        private void CreateOriginGeometry()
        {
            GameObject floor = NewRoot("Persistent floor", new Vector3(0f, -0.25f, -2f));
            floor.AddComponent<BoxCollider>().size = new Vector3(5f, 0.5f, 7f);

            GameObject transition = NewRoot("Native boundary into A", new Vector3(0f, 0.9f, 0f));
            BoxCollider trigger = transition.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(5f, 2f, 0.55f);
            Rigidbody anchor = transition.AddComponent<Rigidbody>();
            anchor.isKinematic = true;
            anchor.useGravity = false;
            AreaBoundaryTrigger3D boundary = transition.AddComponent<AreaBoundaryTrigger3D>();
            SetField(boundary, "sceneToLoad", k_AreaA);
            SetField(boundary, "expectedEntryDirection", Vector2.up);
        }

        private CharacterActor3D CreateActor(string name, CharacterFeature feature, Vector3 position)
        {
            GameObject owner = NewRoot(name, position);
            owner.SetActive(false);
            CharacterController body = owner.AddComponent<CharacterController>();
            body.radius = 0.32f;
            body.height = 1.65f;
            body.center = Vector3.up * 0.825f;
            CharacterActor3D actor = owner.AddComponent<CharacterActor3D>();
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(CharacterBelong.Ally, feature);
            _characters.Add(info);
            actor.SetCharacterInfoForTest(info);
            owner.SetActive(true);
            return actor;
        }

        private GameObject NewRoot(string name, Vector3 position)
        {
            GameObject result = new GameObject(name);
            SceneManager.MoveGameObjectToScene(result, _origin);
            result.transform.position = position;
            return result;
        }

        private static IEnumerator DriveTo(CharacterActor3D actor, float targetZ)
        {
            float direction = Mathf.Sign(targetZ - actor.transform.position.z);
            int steps = 0;
            while (direction * (targetZ - actor.transform.position.z) > 0f && steps++ < 100)
            {
                actor.Motor.Simulate(Vector3.forward * direction, false, Time.fixedDeltaTime);
                yield return new WaitForFixedUpdate();
            }
            Assert.That(Mathf.Abs(actor.transform.position.z - targetZ), Is.LessThan(0.25f));
            yield return null;
        }

        private static IEnumerator WaitForSceneChange(List<string> changes, string path)
        {
            float stop = Time.realtimeSinceStartup + 5f;
            while (!changes.Contains(path) && Time.realtimeSinceStartup < stop)
            {
                yield return null;
            }
            Assert.That(changes, Does.Contain(path));
        }

        private static void SetField(object target, string field, object value)
        {
            target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        }
    }
}
