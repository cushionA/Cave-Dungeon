#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests.PlayMode
{
    public sealed class BossArena3DScenePlayTests
    {
        private const string k_ScenePath = "Assets/Scenes/BossArena3DTestScene.unity";
        private const float k_StrikeDistance = 1.75f;

        private Scene _scene;

        [UnityTearDown]
        public IEnumerator UnloadArena()
        {
            if (_scene.isLoaded) { yield return SceneManager.UnloadSceneAsync(_scene); }
            TestSceneHelper.Cleanup();
            yield return null;
        }

        [UnityTest]
        public IEnumerator AuthoredArena_WhenKnightFightsSentinel_AdvancesPhaseThenClearsOnDefeat()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(k_ScenePath,
                new LoadSceneParameters(LoadSceneMode.Additive, LocalPhysicsMode.Physics3D));
            _scene = SceneManager.GetSceneByPath(k_ScenePath);
            yield return new WaitForFixedUpdate();

            BossEncounter3D encounter = Find<BossEncounter3D>("Cave Sentinel encounter");
            CharacterActor3D knight = Find<CharacterActor3D>("[PLACEHOLDER] Knight");
            CharacterActor3D sentinel = Find<CharacterActor3D>("[PLACEHOLDER] Cave Sentinel");
            Assert.That(encounter.Traits, Is.EqualTo(EncounterTraits.Boss));
            Assert.IsTrue(encounter.IsBossBattle);
            Assert.IsFalse(encounter.IsGroupBattle);
            Assert.IsTrue(encounter.IsRunning);
            Assert.That(encounter.Result.challengeId, Is.EqualTo("test-cave-sentinel"));
            Assert.That(encounter.CurrentPhase, Is.Zero);
            Assert.IsTrue(knight.IsAlive);
            Assert.IsTrue(sentinel.IsAlive);

            PlayerCharacterInput3D input = knight.GetComponent<PlayerCharacterInput3D>();
            AttackInfo sword = new SerializedObject(input).FindProperty("attack3D").objectReferenceValue as AttackInfo;
            Assert.IsNotNull(sword);
            Assert.That(sword.attack3D.shape, Is.EqualTo(AttackShape3D.Melee));
            input.enabled = false;

            Vector3 approach = ToSentinel(knight, sentinel);
            for (int step = 0; step < 60 && approach.magnitude > k_StrikeDistance; step++)
            {
                knight.Motor.Simulate(approach.normalized, false, Time.fixedDeltaTime);
                yield return new WaitForFixedUpdate();
                approach = ToSentinel(knight, sentinel);
            }
            Assert.That(approach.magnitude, Is.LessThanOrEqualTo(k_StrikeDistance));
            Assert.IsTrue(knight.Motor.IsGrounded);
            Physics.SyncTransforms();
            _scene.GetPhysicsScene().Simulate(Time.fixedDeltaTime);

            var outcomes = new List<ChallengeState>();
            encounter.Ended += outcomes.Add;
            try
            {
                bool reachedSecondPhase = false;
                ActionExecutionSystem execution = GameManager.Actions.Execution;
                for (int strike = 0; strike < 16 && sentinel.IsAlive; strike++)
                {
                    int healthBefore = GameManager.Data.GetVitals(sentinel.ObjectHash).currentHp;
                    Assert.IsTrue(GameManager.Actions.Execute3D(knight, sword, out long instanceId));
                    for (int step = 0; step < 60 && execution.TryGetInstance(instanceId, out _); step++)
                    {
                        yield return new WaitForFixedUpdate();
                    }
                    Assert.IsFalse(execution.TryGetInstance(instanceId, out _));
                    Assert.That(GameManager.Data.GetVitals(sentinel.ObjectHash).currentHp, Is.LessThan(healthBefore));
                    if (sentinel.IsAlive)
                    {
                        Assert.IsTrue(encounter.IsRunning);
                        Assert.That(outcomes, Is.Empty);
                        reachedSecondPhase |= encounter.CurrentPhase == 1;
                    }
                }

                Assert.IsTrue(reachedSecondPhase);
                Assert.IsFalse(sentinel.IsAlive);
                Assert.IsFalse(encounter.IsRunning);
                Assert.That(encounter.Result.state, Is.EqualTo(ChallengeState.Completed));
                yield return new WaitForFixedUpdate();
                Assert.That(outcomes, Is.EqualTo(new[] { ChallengeState.Completed }));
            }
            finally
            {
                encounter.Ended -= outcomes.Add;
            }
        }

        private static Vector3 ToSentinel(CharacterActor3D knight, CharacterActor3D sentinel)
            => Vector3.ProjectOnPlane(sentinel.transform.position - knight.transform.position, Vector3.up);

        private T Find<T>(string name) where T : Component => _scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true))
            .Single(component => component.name == name);
    }
}
#endif
