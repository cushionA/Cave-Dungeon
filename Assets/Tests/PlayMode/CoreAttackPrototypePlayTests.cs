#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Game.Tests.PlayMode
{
    public class CoreAttackPrototypePlayTests
    {
        private const string k_ScenePath = "Assets/Scenes/CoreTestScene.unity";
        private Scene _scene;

        [UnityTearDown]
        public IEnumerator UnloadPrototype()
        {
            if (_scene.isLoaded) { yield return SceneManager.UnloadSceneAsync(_scene); }
            TestSceneHelper.Cleanup();
            yield return null;
        }

        [UnityTest]
        public IEnumerator AuthoredEnemy_WithSisterAsPrimaryAssignedThreeShots_OnlyKnightShotShowsAnArc()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(k_ScenePath,
                new LoadSceneParameters(LoadSceneMode.Additive));
            _scene = SceneManager.GetSceneByPath(k_ScenePath);
            GameManager.Instance.enabled = false;
            yield return null;
            GameManager.Projectiles.ClearAll();

            GameObject[] roots = _scene.GetRootGameObjects();
            PlayerCharacter knight = roots.Select(root => root.GetComponent<PlayerCharacter>()).Single(player => player != null);
            CompanionCharacter sister = roots.Single(root => root.name == "[PLACEHOLDER]Companion")
                .GetComponent<CompanionCharacter>();
            CompanionCharacter ally = roots.Single(root => root.name == "[PLACEHOLDER]MultiLockTestAlly")
                .GetComponent<CompanionCharacter>();
            EnemyCharacter enemy = roots.Single(root => root.name == "[PLACEHOLDER]Enemy_Melee_2")
                .GetComponent<EnemyCharacter>();
            Move(knight, new Vector2(3f, 5f));
            Move(sister, new Vector2(2f, 4f));
            Move(ally, new Vector2(4f, 4f));
            Move(enemy, new Vector2(7f, 4f));
            Physics2D.SyncTransforms();

            var enemyConfig = new SerializedObject(enemy);
            var ai = (AIInfo)enemyConfig.FindProperty("_aiInfo").objectReferenceValue;
            ActData attack = ai.actDataList.Single(action => action.actName == "PLACEHOLDER_MultiLockShot");
            var knightConfig = new SerializedObject(knight);
            var skill = (AttackInfo)knightConfig.FindProperty("_multiLockSkill").objectReferenceValue;
            Assert.AreEqual(attack.actionDefinitionId, skill.PersistentId);
            Assert.IsTrue(GameManager.TargetRelations.SetTarget(Actor(enemy), Actor(sister)));
            var command = ActionCommand.FromLegacySlot(new ActionSlot
            {
                execType = ActionExecType.Attack,
                actionDefinitionId = attack.actionDefinitionId,
            });
            ActionDispatchResult execution = enemy.GetComponent<ActionExecutorController>()
                .ExecuteCommand(enemy.ObjectHash, sister.ObjectHash, command);
            Assert.AreEqual(ActionDispatchStatus.Accepted, execution.Status);
            GameManager.Actions.Execution.Tick(skill.motion.info.preMotionDuration);
            yield return null;

            ProjectileController[] shots = GameManager.Projectiles.GetComponentsInChildren<ProjectileController>()
                .Where(shot => shot.IsActive && shot.Magic.actionInstanceId == execution.InstanceId).ToArray();
            CollectionAssert.AreEquivalent(new[] { Actor(knight), Actor(sister), Actor(ally) },
                shots.Select(shot => shot.CoreProjectile.ExplicitTargetRef));
            Assert.IsTrue(shots.All(shot => shot.GetComponent<SpriteRenderer>().sprite != null));
            Assert.IsTrue(GameManager.TargetRelations.TryGetTarget(Actor(enemy), out ActorRef primary));
            Assert.AreEqual(Actor(sister), primary);

            UIDocument hud = roots.Select(root => root.GetComponent<UIDocument>()).Single(document => document != null);
            ProjectileWarningOverlay overlay = hud.rootVisualElement.Q<ProjectileWarningOverlay>();
            Assert.IsNotNull(overlay);
            Assert.That(overlay.Directions.Count, Is.EqualTo(1));
        }

        private static ActorRef Actor(BaseCharacter character)
        {
            Assert.IsTrue(GameManager.Actors.TryGet(character.ObjectHash, out ActorRef actor));
            return actor;
        }

        private static void Move(BaseCharacter character, Vector2 position)
        {
            character.SetPhysicsGravityScale(0f);
            character.SetPhysicsPosition(position);
            character.PublishPositionForObservation();
        }
    }
}
#endif
