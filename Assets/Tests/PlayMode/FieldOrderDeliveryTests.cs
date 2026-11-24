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
    public sealed class FieldOrderDeliveryTests
    {
        private readonly List<Object> _assets = new();
        private Scene _room;
        private PlayerCharacter _player;
        private CompanionCharacter _sister;
        private float _time;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            TestSceneHelper.Cleanup();
            yield return null;
            TestSceneHelper.CreateGameManager();
            GameManager.Instance.enabled = false;
            _room = SceneManager.CreateScene("FieldOrders", new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            _player = Actor<PlayerCharacter>(0f, CharacterBelong.Ally, CharacterFeature.Player);
            yield return null;
            _sister = Actor<CompanionCharacter>(1f, CharacterBelong.Ally, CharacterFeature.Companion);
            _sister.SetAIInfo(Brain(ActType.Wait));
            yield return null;
        }

        private T Actor<T>(float x, CharacterBelong side, CharacterFeature feature) where T : BaseCharacter
        {
            Game.Core.CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo(side, feature);
            _assets.Add(info);
            GameObject go = new GameObject(typeof(T).Name);
            SceneManager.MoveGameObjectToScene(go, _room);
            go.transform.position = new Vector3(x, 3f);
            go.AddComponent<Rigidbody2D>().gravityScale = 0f;
            go.AddComponent<BoxCollider2D>();
            go.AddComponent<ActionExecutorController>();
            T actor = go.AddComponent<T>();
            actor.SetCharacterInfoForTest(info);
            return actor;
        }

        private AIInfo Brain(ActType action, BroadcastAction broadcast = BroadcastAction.FocusFire)
        {
            AIInfo ai = ScriptableObject.CreateInstance<AIInfo>();
            _assets.Add(ai);
            ai.actDataList = new[] { new ActData { actType = action, fieldOrder = broadcast, weight = 10f } };
            ai.modes = new[] { new CharacterModeData { mode = CharacterMode.Combat,
                detectionRange = 30f, availableActIndices = new[] { 0 } } };
            return ai;
        }

        private EnemyCharacter Enemy(float x, ActType action = ActType.Wait, BroadcastAction broadcast = BroadcastAction.FocusFire)
        {
            EnemyCharacter actor = Actor<EnemyCharacter>(x, CharacterBelong.Enemy, CharacterFeature.Minion);
            actor.SetAIInfo(Brain(action, broadcast));
            return actor;
        }

        private static ActorRef Id(BaseCharacter actor)
        {
            Assert.IsTrue(GameManager.Actors.TryGet(actor.ObjectHash, out ActorRef id));
            return id;
        }

        private static ActorRef TargetOf(BaseCharacter actor)
        {
            GameManager.TargetRelations.TryGetTarget(Id(actor), out ActorRef target);
            return target;
        }

        private bool PlayerOrder(BroadcastAction action, ActorRef target = default, Vector2? point = null) =>
            GameManager.Events.TryIssueFieldOrder(new FieldOrder(action, FieldOrderIssuer.Player, Id(_player), target, point));

        private void Tick()
        {
            _time += 0.6f;
            GameManager.Instance.SimulationTick(0.6f, _time);
        }

        [UnityTest]
        public IEnumerator Focus_PlayerAndAuthoredEnemyCommanderRedirectOnlyTheirOwnAI()
        {
            EnemyCharacter soldier = Enemy(2f);
            EnemyCharacter commander = Enemy(-5f, ActType.FieldOrder);
            yield return null;
            Tick();
            Assert.AreEqual(Id(soldier), TargetOf(_sister));
            Assert.AreEqual(Id(_sister), TargetOf(soldier));
            Assert.AreEqual(Id(_player), TargetOf(commander));
            Assert.IsFalse(PlayerOrder(BroadcastAction.DesignateTarget, Id(commander)), "already engaged");
            Assert.IsTrue(PlayerOrder(BroadcastAction.FocusFire, Id(commander)));
            Tick();
            Assert.AreEqual(Id(commander), TargetOf(_sister));
            Assert.AreEqual(Id(_player), TargetOf(soldier), "the enemy commander's authored Focus changes its soldier's normal judgment");
            Assert.IsFalse(TargetOf(_player).IsValid);
            Assert.IsFalse(GameManager.Events.TryIssueFieldOrder(new FieldOrder(BroadcastAction.FocusFire,
                FieldOrderIssuer.EnemyCommander, Id(soldier), Id(_sister))), "ordinary enemy cannot impersonate a commander");
        }

        [UnityTest]
        public IEnumerator Focus_BlockedFriendlyAndPooledTargetCannotReplacePreviousOrder()
        {
            EnemyCharacter near = Enemy(2f);
            EnemyCharacter far = Enemy(5f);
            EnemyCharacter hidden = Enemy(-5f);
            yield return null;
            Tick();
            ActorRef old = Id(far);
            Assert.IsTrue(PlayerOrder(BroadcastAction.FocusFire, old));
            GameObject wall = new GameObject("opaque ground");
            SceneManager.MoveGameObjectToScene(wall, _room);
            wall.layer = 6;
            wall.transform.position = new Vector3(-2f, 3f);
            wall.AddComponent<BoxCollider2D>();
            Assert.IsFalse(PlayerOrder(BroadcastAction.FocusFire, Id(hidden)));
            Assert.IsFalse(PlayerOrder(BroadcastAction.FocusFire, Id(_sister)));
            Tick();
            Assert.AreEqual(old, TargetOf(_sister));
            far.OnPoolReturn();
            far.OnPoolAcquire();
            Assert.AreNotEqual(old, Id(far));
            Assert.IsFalse(PlayerOrder(BroadcastAction.FocusFire, old));
            Tick();
            Assert.AreEqual(Id(near), TargetOf(_sister));
        }

        [UnityTest]
        public IEnumerator Rally_ArrivalBesideIssuerRestoresAutonomyAndDefendHoldsUntilRelease()
        {
            EnemyCharacter foe = Enemy(2f);
            yield return null;
            _sister.SetPhysicsPosition(new Vector2(6f, 3f));
            Tick();
            Assert.IsTrue(PlayerOrder(BroadcastAction.Rally));
            Tick();
            Assert.Less(_sister.PhysicsVelocity.x, 0f);
            _sister.SetPhysicsPosition(new Vector2(1.05f, 3f));
            Tick();
            Assert.AreEqual(Id(foe), TargetOf(_sister));
            Assert.IsFalse(PlayerOrder(BroadcastAction.ReleaseOrder), "already arrived beside the knight");

            Assert.IsFalse(PlayerOrder(BroadcastAction.DefendPoint, point: new Vector2(6f, 9f)), "another floor");
            Assert.IsTrue(PlayerOrder(BroadcastAction.DefendPoint, point: new Vector2(6f, 3f)));
            Tick();
            Assert.Greater(_sister.PhysicsVelocity.x, 0f);
            _sister.SetPhysicsPosition(new Vector2(6f, 3f));
            Tick();
            Assert.AreEqual(Id(foe), TargetOf(_sister), "at its station the defender evaluates normal combat targets");
            Assert.AreEqual(0f, _sister.PhysicsVelocity.x, 0.01f);
            ActionDispatchResult chase = _sister.GetComponent<ActionExecutorController>().ExecuteCommand(_sister.ObjectHash, foe.ObjectHash,
                new ActionCommand { kind = ActionCommandKind.MoveToTarget });
            Assert.AreEqual(ActionDispatchStatus.Rejected, chase.Status);
            Assert.IsTrue(PlayerOrder(BroadcastAction.ReleaseOrder));
            Tick();
            Assert.AreEqual(Id(foe), TargetOf(_sister));
        }

        [UnityTest]
        public IEnumerator Defend_AtPostAttacksAndWaitsForCommittedSwingBeforeMovingToNewPost()
        {
            EnemyCharacter foe = Enemy(2f);
            yield return null;
            Tick();
            AttackInfo attack = ScriptableObject.CreateInstance<AttackInfo>();
            _assets.Add(attack);
            attack.durationMode = ActionDurationMode.Fixed;
            attack.duration = 1.5f;
            attack.cancelAfter = -1f;
            ActionExecutorController executor = _sister.GetComponent<ActionExecutorController>();
            executor.SetAttackInfosForTest(new[] { attack });
            AIInfo authored = Brain(ActType.Attack);
            AIMode[] modes = AIInfoConverter.ConvertModes(authored);
            modes[0].targetSelects[0].filter.belong = CharacterBelong.Enemy;
            _sister.AIController.SetAIModes(modes, AIInfoConverter.ConvertTransitions(authored));

            Assert.IsTrue(PlayerOrder(BroadcastAction.DefendPoint, point: _sister.PhysicsPosition));
            Tick();
            long swing = executor.CurrentGroupId;
            Assert.AreEqual(Id(foe), TargetOf(_sister));
            Assert.IsTrue(GameManager.Actions.IsGroupActive(swing), "normal authored attack started at the post");
            Assert.IsTrue(PlayerOrder(BroadcastAction.DefendPoint, point: new Vector2(8f, 3f)));
            Tick();
            Assert.IsTrue(GameManager.Actions.IsGroupActive(swing), "new order must preserve the committed attack");
            Assert.AreEqual(0f, _sister.PhysicsVelocity.x, 0.01f);
            Tick();
            Tick();
            Assert.IsFalse(GameManager.Actions.IsGroupActive(swing));
            Assert.Greater(_sister.PhysicsVelocity.x, 0f);
        }

        [UnityTest]
        public IEnumerator EnemyTaunt_DrawsOnlyHostileAIAndPlayerDisableEndsItsAssignment()
        {
            EnemyCharacter near = Enemy(2f);
            EnemyCharacter taunter = Enemy(-5f, ActType.FieldOrder, BroadcastAction.Taunt);
            yield return null;
            Tick();
            Assert.AreEqual(Id(near), TargetOf(_sister));
            Assert.IsFalse(PlayerOrder(BroadcastAction.Taunt));
            Tick();
            Assert.AreEqual(Id(taunter), TargetOf(_sister));
            Assert.AreEqual(Id(_sister), TargetOf(near));
            Assert.IsFalse(TargetOf(_player).IsValid);

            taunter.enabled = false;
            Assert.IsTrue(PlayerOrder(BroadcastAction.DefendPoint, point: new Vector2(6f, 3f)));
            _player.enabled = false;
            _player.enabled = true;
            Tick();
            Assert.AreEqual(Id(near), TargetOf(_sister));
            Assert.IsFalse(PlayerOrder(BroadcastAction.ReleaseOrder));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_room.IsValid() && _room.isLoaded) { yield return SceneManager.UnloadSceneAsync(_room); }
            for (int i = 0; i < _assets.Count; i++) { Object.Destroy(_assets[i]); }
            _assets.Clear();
            TestSceneHelper.Cleanup();
            yield return null;
        }
    }
}
