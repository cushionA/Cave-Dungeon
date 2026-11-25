#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using CharacterInfo = Game.Core.CharacterInfo;

namespace Game.Tests.PlayMode
{
    public sealed class ExpeditionConsumablesMockScenePlayTests
    {
        private const string k_Mock = "Assets/Scenes/ExpeditionMock3DScene.unity";
        private const int k_HealingDraught = 4101;
        private const int k_EmberFlask = 4102;
        private Scene _mock;
        private CharacterInfo _enemyInfo;

        [UnityTearDown]
        public IEnumerator UnloadMock()
        {
            if (_mock.isLoaded) { yield return SceneManager.UnloadSceneAsync(_mock); }
            Object.Destroy(_enemyInfo);
            TestSceneHelper.Cleanup();
            yield return null;
        }

        [UnityTest]
        public IEnumerator AuthoredSupplies_ThroughInteractionAndBelt_AnimateHealAndBlastTheGround()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(k_Mock, new LoadSceneParameters(LoadSceneMode.Additive));
            _mock = SceneManager.GetSceneByPath(k_Mock);
            yield return new WaitForFixedUpdate();

            CharacterActor3D knight = FindRoot("[PLACEHOLDER] Knight").GetComponent<CharacterActor3D>();
            knight.GetComponent<PlayerCharacterInput3D>().enabled = false;
            GameManager.Instance.enabled = false;
            PlayerItemUse3D use = knight.GetComponent<PlayerItemUse3D>();
            Assert.IsNotNull(use);
            ConsumableDefinition draught = use.Items.Single(item => item.ItemId == k_HealingDraught);
            ConsumableDefinition ember = use.Items.Single(item => item.ItemId == k_EmberFlask);
            Assert.AreEqual(0, GameManager.Inventory.GetConsumableCount(k_HealingDraught));
            Assert.AreEqual(0, GameManager.Inventory.GetConsumableCount(k_EmberFlask));

            yield return Collect(knight, "Healing draught supplies", k_HealingDraught, 3);
            yield return Collect(knight, "Ember flask supplies", k_EmberFlask, 2);
            Assert.IsTrue(GameManager.ConsumableBelt.TryAssign(0, k_HealingDraught));
            Assert.IsTrue(GameManager.ConsumableBelt.TryAssign(1, k_EmberFlask));
            Assert.IsTrue(GameManager.ConsumableBelt.TrySelect(0));

            Transform prop = knight.transform.Find("Item-use prop");
            Assert.IsNotNull(prop);
            Assert.IsNotNull(knight.GetComponent<Animator>());
            MotionInfo motion = draught.UseAction.GetMotion();
            Assert.IsNotNull(motion.preMotionClip);
            Assert.IsNotNull(motion.activeClip);
            Assert.IsNotNull(motion.recoveryClip);
            Assert.IsNotNull(draught.EffectPrefab);
            Vector3 rest = prop.localPosition;
            int knightHash = knight.Actor.Hash;
            int hpBefore = GameManager.Data.GetVitals(knightHash).maxHp - 35;
            Assert.That(hpBefore, Is.Positive);
            GameManager.Data.GetVitals(knightHash).currentHp = hpBefore;
            Assert.IsTrue(use.TryUseActive(out long action));
            ActionExecutionSystem execution = GameManager.Actions.Execution;
            Assert.IsTrue(execution.TryGetInstance(action, out ActionExecution anticipation));
            Assert.AreEqual(AnimationPhase.Anticipation, anticipation.phase);

            execution.Tick(motion.preMotionDuration * 0.5f);
            GameManager.Actions.Present();
            yield return null;
            Assert.That(prop.localPosition.y, Is.GreaterThan(rest.y + 0.04f), "The configured anticipation clip moves the hand prop.");
            Assert.AreEqual(hpBefore, GameManager.Data.GetVitals(knightHash).currentHp);
            Assert.AreEqual(3, GameManager.Inventory.GetConsumableCount(k_HealingDraught));
            Assert.IsNull(FindEffect(draught));

            execution.Tick(motion.preMotionDuration * 0.5f);
            Assert.AreEqual(hpBefore + 25, GameManager.Data.GetVitals(knightHash).currentHp);
            Assert.AreEqual(2, GameManager.Inventory.GetConsumableCount(k_HealingDraught));
            Assert.AreEqual(2, GameManager.Inventory.GetConsumableCount(k_EmberFlask));
            yield return AssertVisibleBurst(draught);
            execution.Tick(1f);

            Assert.IsTrue(knight.Motor.IsGrounded);
            Vector3 ground = knight.Motor.Ground.Point;
            _enemyInfo = TestSceneHelper.CreateTestCharacterInfo(CharacterBelong.Enemy, CharacterFeature.Minion);
            _enemyInfo.baseDefense = default;
            CharacterActor3D inside = CreateEnemy(knight, ground + Vector3.right * 0.9f);
            CharacterActor3D outside = CreateEnemy(knight, ground + Vector3.right * 3f);
            Assert.AreSame(knight.Motor.Ground.Surface, inside.Motor.Ground.Surface);
            Assert.AreSame(knight.Motor.Ground.Surface, outside.Motor.Ground.Surface);
            AttackInfo fire = ember.UseAction as AttackInfo;
            Assert.IsNotNull(fire);
            Assert.AreEqual(AttackShape3D.GroundArea, fire.attack3D.shape);
            Assert.IsTrue(GameManager.ConsumableBelt.TrySelect(1));
            Assert.IsTrue(use.TryUseActive(out long blast));
            Assert.IsTrue(execution.TryGetInstance(blast, out ActionExecution casting));
            Assert.AreEqual(AnimationPhase.Anticipation, casting.phase);
            Assert.AreEqual(100, GameManager.Data.GetVitals(inside.Actor.Hash).currentHp);
            Assert.IsNull(FindEffect(ember));

            execution.Tick(fire.GetMotion().preMotionDuration);
            int power = ConsumableDefinition.CalculatePower(ember.BasePower, ember.ScalingStat, ember.StatScale,
                GameManager.Data.GetAttributes(knightHash));
            Assert.AreEqual(100 - power, GameManager.Data.GetVitals(inside.Actor.Hash).currentHp);
            Assert.AreEqual(100, GameManager.Data.GetVitals(outside.Actor.Hash).currentHp);
            Assert.AreEqual(1, GameManager.Inventory.GetConsumableCount(k_EmberFlask));
            Assert.AreEqual(2, GameManager.Inventory.GetConsumableCount(k_HealingDraught));
            ParticleSystem blastEffect = FindEffect(ember);
            Assert.IsNotNull(blastEffect);
            Vector3 expectedOrigin = ground + knight.transform.TransformVector(ember.EffectOffset);
            Assert.That(Vector3.Distance(expectedOrigin, blastEffect.transform.position), Is.LessThan(0.01f));
            yield return AssertVisibleBurst(ember);
        }

        private IEnumerator AssertVisibleBurst(ConsumableDefinition item)
        {
            ParticleSystem effect = FindEffect(item);
            Assert.IsNotNull(effect);
            Assert.IsNotNull(effect.GetComponent<ParticleSystemRenderer>().sharedMaterial);
            float deadline = Time.realtimeSinceStartup + 1f;
            while (effect != null && effect.particleCount == 0 && Time.realtimeSinceStartup < deadline) { yield return null; }
            Assert.IsNotNull(effect);
            Assert.That(effect.particleCount, Is.Positive);
        }

        private CharacterActor3D CreateEnemy(CharacterActor3D knight, Vector3 ground)
        {
            GameObject root = new GameObject("Ember flask target");
            root.SetActive(false);
            SceneManager.MoveGameObjectToScene(root, _mock);
            root.layer = LayerMask.NameToLayer("Enemy");
            Vector3 facing = knight.Motor.Ground.Point - ground;
            root.transform.SetPositionAndRotation(ground + Vector3.up * 0.04f, Quaternion.LookRotation(facing));
            CharacterController body = root.AddComponent<CharacterController>();
            body.center = Vector3.up * 0.8f;
            body.height = 1.6f;
            body.radius = 0.3f;
            CharacterActor3D enemy = root.AddComponent<CharacterActor3D>();
            enemy.SetCharacterInfoForTest(_enemyInfo);
            root.SetActive(true);
            Physics.SyncTransforms();
            for (int i = 0; i < 12; i++) { enemy.Motor.Simulate(Vector3.zero, false, Time.fixedDeltaTime); }
            Assert.IsTrue(enemy.Motor.IsGrounded);
            return enemy;
        }

        private IEnumerator Collect(CharacterActor3D knight, string supplyId, int itemId, int expectedCount)
        {
            NpcInteraction3D supply = FindRoot(supplyId).GetComponent<NpcInteraction3D>();
            Assert.IsNotNull(supply);
            for (int step = 0; step < 150 && !supply.CanInteract(knight.Actor.Hash); step++)
            {
                Vector3 direction = supply.transform.position - knight.Motor.FeetPosition;
                direction.y = 0f;
                knight.Motor.Simulate(direction.normalized, false, Time.fixedDeltaTime);
                yield return new WaitForFixedUpdate();
            }
            Assert.IsTrue(supply.CanInteract(knight.Actor.Hash));
            supply.Interact(knight.Actor.Hash);
            Assert.AreEqual(expectedCount, GameManager.Inventory.GetConsumableCount(itemId));
            Assert.IsFalse(supply.gameObject.activeSelf);
        }

        private ParticleSystem FindEffect(ConsumableDefinition item) => _mock.GetRootGameObjects()
            .SingleOrDefault(root => root.name == item.EffectPrefab.name + "(Clone)")?.GetComponent<ParticleSystem>();

        private GameObject FindRoot(string name) => _mock.GetRootGameObjects().Single(root => root.name == name);
    }
}
#endif
