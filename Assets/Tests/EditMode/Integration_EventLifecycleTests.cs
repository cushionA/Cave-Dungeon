using System;
using NUnit.Framework;
using R3;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// GameEvents の Subject ベースイベントに関するライフサイクル検証。
    /// 多重購読 / Unsubscribe 対称性 / リーク検知（購読解除で呼び出し 0 に戻る）
    /// を 1 テスト 1 観点で検証する。R3 の Observable から提供される既定動作の回帰。
    /// </summary>
    [TestFixture]
    public class Integration_EventLifecycleTests
    {
        private GameEvents _events;

        [SetUp]
        public void SetUp()
        {
            _events = new GameEvents();
        }

        [TearDown]
        public void TearDown()
        {
            _events?.Dispose();
            _events = null;
        }

        // =========================================================================
        // 多重購読: 同じロジックを 2 回 Subscribe → 1 回発火で 2 回呼ばれる
        // =========================================================================

        [Test]
        public void GameEvents_WhenHandlerSubscribedTwice_FireInvokesHandlerTwice()
        {
            int callCount = 0;
            Action<int> handler = _ => callCount++;

            IDisposable sub1 = _events.OnCharacterRegistered.Subscribe(h => handler(h));
            IDisposable sub2 = _events.OnCharacterRegistered.Subscribe(h => handler(h));

            _events.FireCharacterRegistered(42);

            Assert.AreEqual(2, callCount,
                "同じ効果のハンドラを 2 回 Subscribe すると 1 回の発火で 2 回呼ばれる");

            sub1.Dispose();
            sub2.Dispose();
        }

        // =========================================================================
        // Unsubscribe 対称性: Dispose 後に発火しても呼ばれない
        // =========================================================================

        [Test]
        public void GameEvents_AfterSubscriptionDisposed_FireDoesNotInvokeHandler()
        {
            int callCount = 0;
            IDisposable sub = _events.OnGateOpened.Subscribe(_ => callCount++);

            _events.FireGateOpened("gate-1");
            Assert.AreEqual(1, callCount, "購読中は呼ばれる");

            sub.Dispose();
            _events.FireGateOpened("gate-2");
            Assert.AreEqual(1, callCount,
                "Dispose 後の Fire では呼ばれない（対称性が保たれる）");
        }

        [Test]
        public void GameEvents_MixedSubscribeUnsubscribe_OnlyActiveHandlersAreInvoked()
        {
            int countA = 0;
            int countB = 0;
            int countC = 0;

            IDisposable subA = _events.OnCooldownReady.Subscribe(_ => countA++);
            IDisposable subB = _events.OnCooldownReady.Subscribe(_ => countB++);
            IDisposable subC = _events.OnCooldownReady.Subscribe(_ => countC++);

            _events.FireCooldownReady(1);
            Assert.AreEqual(1, countA);
            Assert.AreEqual(1, countB);
            Assert.AreEqual(1, countC);

            // B のみ解除
            subB.Dispose();

            _events.FireCooldownReady(2);
            Assert.AreEqual(2, countA, "A は継続");
            Assert.AreEqual(1, countB, "B は解除後に呼ばれない");
            Assert.AreEqual(2, countC, "C は継続");

            subA.Dispose();
            subC.Dispose();
        }

        // =========================================================================
        // リーク検知: Subscribe 数が Unsubscribe で 0 に戻る
        // （Observable への発火カウンタで代用し、購読数の論理的 0 を検証）
        // =========================================================================

        [Test]
        public void GameEvents_AfterAllSubscriptionsDisposed_NoHandlersRemainInvokable()
        {
            int totalCalls = 0;
            IDisposable sub1 = _events.OnRest.Subscribe(_ => totalCalls++);
            IDisposable sub2 = _events.OnRest.Subscribe(_ => totalCalls++);
            IDisposable sub3 = _events.OnRest.Subscribe(_ => totalCalls++);

            _events.FireRest();
            Assert.AreEqual(3, totalCalls, "3 購読で 3 回呼ばれる");

            // 全 Dispose → Subscribe 数は論理的に 0
            sub1.Dispose();
            sub2.Dispose();
            sub3.Dispose();

            _events.FireRest();
            Assert.AreEqual(3, totalCalls,
                "全 Dispose 後は Fire しても呼び出しが増えない（リークなし）");
        }

        [Test]
        public void GameEvents_SubscribeDisposeSubscribe_RestartsInvocationCleanly()
        {
            int callCount = 0;
            IDisposable sub = _events.OnFreeCoopActivated.Subscribe(_ => callCount++);
            _events.FireFreeCoopActivated();
            Assert.AreEqual(1, callCount);

            sub.Dispose();
            _events.FireFreeCoopActivated();
            Assert.AreEqual(1, callCount, "解除後は増えない");

            // 再購読
            sub = _events.OnFreeCoopActivated.Subscribe(_ => callCount++);
            _events.FireFreeCoopActivated();
            Assert.AreEqual(2, callCount, "再購読後は再びカウントアップ");

            sub.Dispose();
        }

        [Test]
        public void GameEvents_OnCharacterDeathEvent_CSharpEventSymmetry()
        {
            // C# event (Integration 層用) も同様に購読解除で呼ばれなくなることを確認
            int eventCount = 0;
            Action<int, int> handler = (dead, killer) => eventCount++;

            _events.OnCharacterDeathEvent += handler;
            _events.FireCharacterDeath(1, 2);
            Assert.AreEqual(1, eventCount, "購読中は呼ばれる");

            _events.OnCharacterDeathEvent -= handler;
            _events.FireCharacterDeath(3, 4);
            Assert.AreEqual(1, eventCount,
                "C# event 版も -= 後に呼ばれない（対称性が保たれる）");
        }
    }
}
