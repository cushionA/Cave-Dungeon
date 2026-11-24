using System;
using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public class FieldOrderDispatchTests
    {
        private ActorRegistry _actors;
        private GameEvents _events;
        private ActorRef _player;
        private ActorRef _enemy;
        private readonly List<FieldOrder> _accepted = new List<FieldOrder>();

        [SetUp]
        public void SetUp()
        {
            _actors = new ActorRegistry();
            _player = _actors.Register(1);
            _enemy = _actors.Register(2);
            _events = new GameEvents(_actors);
            _accepted.Clear();
            _events.SetFieldOrderReceiver(actor => actor.Hash == _player.Hash ? FieldOrderIssuer.Player : FieldOrderIssuer.EnemyCommander,
                order => { _accepted.Add(order); return true; });
        }

        [TearDown]
        public void TearDown() => _events.Dispose();

        [Test]
        public void PayloadAndIssuerAreValidatedBeforeDelivery()
        {
            FieldOrderIssuer source = FieldOrderIssuer.Player;
            Assert.IsTrue(_events.TryIssueFieldOrder(new FieldOrder(BroadcastAction.DesignateTarget, source, _player, _enemy)));
            Assert.IsTrue(_events.TryIssueFieldOrder(new FieldOrder(BroadcastAction.DefendPoint, source, _player, position: Vector2.zero)));
            Assert.IsFalse(_events.TryIssueFieldOrder(new FieldOrder(BroadcastAction.DefendPoint, source, _player)));
            Assert.IsFalse(_events.TryIssueFieldOrder(new FieldOrder(BroadcastAction.DefendPoint, source, _player, position: new Vector2(float.NaN, 1f))));
            Assert.IsFalse(_events.TryIssueFieldOrder(new FieldOrder(BroadcastAction.FocusFire, source, _player)));
            Assert.IsFalse(_events.TryIssueFieldOrder(new FieldOrder(BroadcastAction.FocusFire, source, _player, _player)));
            Assert.IsFalse(_events.TryIssueFieldOrder(new FieldOrder(BroadcastAction.Rally, source, _player, _enemy)));
            Assert.IsFalse(_events.TryIssueFieldOrder(new FieldOrder(BroadcastAction.ReleaseOrder, source, _player, position: Vector2.zero)));
            Assert.IsFalse(_events.TryIssueFieldOrder(new FieldOrder((BroadcastAction)255, source, _player)));
            Assert.IsFalse(_events.TryIssueFieldOrder(new FieldOrder(BroadcastAction.Rally, FieldOrderIssuer.None, _player)));
            Assert.IsFalse(_events.TryIssueFieldOrder(new FieldOrder(BroadcastAction.Rally, (FieldOrderIssuer)128, _player)));
            Assert.IsFalse(_events.TryIssueFieldOrder(new FieldOrder(BroadcastAction.Rally, source | FieldOrderIssuer.EnemyCommander, _player)));
            Assert.IsFalse(_events.TryIssueFieldOrder(new FieldOrder(BroadcastAction.Taunt, source, _player)));
            Assert.IsFalse(_events.TryIssueFieldOrder(new FieldOrder(BroadcastAction.Taunt, FieldOrderIssuer.EnemyCommander, _player)));
            Assert.IsTrue(_events.TryIssueFieldOrder(new FieldOrder(BroadcastAction.Taunt, FieldOrderIssuer.EnemyCommander, _enemy)));
            Assert.AreEqual(3, _accepted.Count);
        }

        [Test]
        public void RecycledActorAndMissingOrRejectingReceiverCannotAccept()
        {
            _actors.Unregister(_enemy.Hash);
            ActorRef replacement = _actors.Register(_enemy.Hash);
            Assert.IsFalse(_events.TryIssueFieldOrder(new FieldOrder(BroadcastAction.FocusFire, FieldOrderIssuer.Player, _player, _enemy)));
            Assert.IsFalse(_events.TryIssueFieldOrder(new FieldOrder(BroadcastAction.Rally, FieldOrderIssuer.EnemyCommander, _enemy)));
            Assert.IsTrue(_events.TryIssueFieldOrder(new FieldOrder(BroadcastAction.FocusFire, FieldOrderIssuer.Player, _player, replacement)));
            _events.SetFieldOrderReceiver(_ => FieldOrderIssuer.Player, _ => false);
            Assert.IsFalse(_events.TryIssueFieldOrder(new FieldOrder(BroadcastAction.Rally, FieldOrderIssuer.Player, _player)));
            _events.SetFieldOrderReceiver(null, null);
            Assert.IsFalse(_events.TryIssueFieldOrder(new FieldOrder(BroadcastAction.Rally, FieldOrderIssuer.Player, _player)));
            Assert.AreEqual(1, _accepted.Count);
        }

        [Test]
        public void ExplicitEnemyOrderUsesSharedChannelWithCombatBackendInstalled()
        {
            using EnemyController commander = new EnemyController(_enemy.Hash, null, _events);
            ActionExecutor executor = commander.JudgmentLoop.Executor;
            BusyBackend backend = new BusyBackend();
            executor.SetExecutionBackend(backend);
            backend.Busy = true;
            int completed = 0;
            executor.OnActionCompleted += () => completed++;
            ActionSlot focus = AIInfoConverter.ConvertAction(new ActData { actType = ActType.FieldOrder, fieldOrder = BroadcastAction.FocusFire });
            Assert.IsTrue(executor.Execute(_enemy.Hash, _player.Hash, focus));
            Assert.AreEqual(FieldOrderIssuer.EnemyCommander, _accepted[0].Issuer);
            Assert.AreEqual(_enemy, _accepted[0].Source);
            Assert.AreEqual(_player, _accepted[0].Target);
            Assert.IsTrue(backend.IsExecuting);
            Assert.AreEqual(0, completed);
            Assert.IsFalse(executor.Execute(_player.Hash, _enemy.Hash, focus));
            foreach (ActType type in new[] { ActType.Buff, (ActType)255 })
            {
                ActionSlot legacy = AIInfoConverter.ConvertAction(new ActData { actType = type, attackInfoIndex = 0 });
                Assert.IsFalse(executor.Execute(_enemy.Hash, _player.Hash, legacy));
            }
            Assert.IsFalse(executor.Execute(_enemy.Hash, _player.Hash, new ActionSlot { execType = ActionExecType.Broadcast, paramId = 256 }));
            Assert.AreEqual(1, _accepted.Count);
        }

        [Test]
        public void DefendPointUsesCommandersObservedPositionAndRequiresAnObservation()
        {
            using EnemyController commander = new EnemyController(_enemy.Hash, null, _events);
            ActionSlot slot = AIInfoConverter.ConvertAction(new ActData { actType = ActType.FieldOrder, fieldOrder = BroadcastAction.DefendPoint });
            JudgmentLoop loop = commander.JudgmentLoop;
            Assert.IsFalse(loop.Executor.Execute(_enemy.Hash, 0, slot));
            Vector2 point = new Vector2(-3f, 9f);
            AIObservationContext snapshot = new AIObservationContext();
            snapshot.BeginSnapshot(1, 1f);
            snapshot.AddActor(AIActorObservation.Capture(_enemy, new CharacterVitals { currentHp = 1, position = point }, default, default));
            AITargetRelations relations = new AITargetRelations();
            relations.Register(_enemy);
            Assert.IsTrue(loop.UseObservation(snapshot, relations));
            loop.SetMode(new AIMode { actions = new[] { slot }, actionRules = new[] { new AIRule { actionIndex = 0 } } });
            loop.EvaluateAction(1f);
            Assert.AreEqual(1, _accepted.Count);
            Assert.AreEqual(point, _accepted[0].Position);
            Assert.AreEqual(default(ActorRef), _accepted[0].Target);
        }

        private sealed class BusyBackend : IActionCommandBackend
        {
            public bool Busy;
            public bool IsExecuting => Busy;
            public event Action OnActionCompleted { add { } remove { } }
            public bool Supports(ActionCommandKind kind) => false;
            public void CancelCurrent() => Busy = false;
            public void Tick(float deltaTime) { }
            public bool Execute(int ownerHash, int targetHash, ActionSlot slot) => throw new NotSupportedException();
            public ActionDispatchResult ExecuteCommand(int ownerHash, int targetHash, in ActionCommand command) =>
                throw new NotSupportedException();
        }
    }
}
