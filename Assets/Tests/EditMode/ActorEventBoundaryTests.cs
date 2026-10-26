using System;
using Game.Core;
using NUnit.Framework;
using R3;

namespace Game.Tests.EditMode
{
    public class ActorEventBoundaryTests
    {
        [Test]
        public void ActorEvents_UnresolvedDamageSource_IsNotReboundBySynchronousSubscriber()
        {
            ActorRegistry actors = new ActorRegistry();
            actors.Register(18);
            using GameEvents events = new GameEvents(actors);
            int observableSource = -1;
            int callbackSource = -1;
            using IDisposable subscription = events.OnDamageDealt.Subscribe(change =>
            {
                observableSource = change.attackerHash;
                actors.Register(17);
            });
            events.OnDamageDealtEvent += (result, attacker, target) => callbackSource = attacker;

            events.FireDamageDealt(default, 17, 18);

            Assert.AreEqual(0, observableSource);
            Assert.AreEqual(0, callbackSource);
        }

        [Test]
        public void ActorEvents_UnresolvedDeathSource_IsNotReboundByTypedSubscriber()
        {
            ActorRegistry actors = new ActorRegistry();
            actors.Register(18);
            using GameEvents events = new GameEvents(actors);
            int observableKiller = -1;
            int callbackKiller = -1;
            using IDisposable typed = events.OnCharacterDeathResolved.Subscribe(_ => actors.Register(17));
            using IDisposable legacy = events.OnCharacterDeath.Subscribe(death => observableKiller = death.killerHash);
            events.OnCharacterDeathEvent += (target, killer) => callbackKiller = killer;

            events.FireCharacterDeath(18, 17);

            Assert.AreEqual(0, observableKiller);
            Assert.AreEqual(0, callbackKiller);
        }
    }
}
