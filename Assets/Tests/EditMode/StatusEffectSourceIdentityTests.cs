using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;
using R3;

namespace Game.Tests.EditMode
{
    public class StatusEffectSourceIdentityTests
    {
        [Test]
        public void StatusEffect_SourceReusedBeforeTick_PreservesOriginalGenerationAndDoesNotMergeNewSource()
        {
            ActorRegistry actors = new ActorRegistry();
            ActorRef source = actors.Register(11);
            ActorRef target = actors.Register(22);
            StatusEffectStore effects = new StatusEffectStore();
            StatusEffectInfo poison = new StatusEffectInfo
            { effect = StatusEffectId.Poison, duration = 5f, tickInterval = 1f, tickDamage = 3f };
            effects.Apply(target, poison, source);
            actors.Unregister(source.Hash);
            ActorRef replacement = actors.Register(source.Hash);
            effects.Apply(target, poison, replacement);
            List<StatusEffectTick> ticks = new List<StatusEffectTick>();
            effects.TickAll(1f, ticks);
            Assert.AreEqual(2, ticks.Count);
            Assert.AreEqual(2, effects.ActiveCount);
            Assert.IsTrue(ticks.Exists(tick => tick.sourceRef == source && tick.targetRef == target));
            Assert.IsTrue(ticks.Exists(tick => tick.sourceRef == replacement && tick.targetRef == target));
            Assert.IsFalse(actors.IsCurrent(source));
        }

        [Test]
        public void GameEvents_StaleCapturedSource_PublishesHistoricalIdentityAndNoLegacyKillerAlias()
        {
            ActorRegistry actors = new ActorRegistry();
            ActorRef source = actors.Register(11);
            ActorRef target = actors.Register(22);
            using GameEvents events = new GameEvents(actors);
            actors.Unregister(source.Hash);
            ActorRef replacement = actors.Register(source.Hash);
            int damages = 0;
            int deaths = 0;
            using System.IDisposable damageSubscription = events.OnDamageDealt.Subscribe(hit =>
            {
                damages++;
                Assert.AreEqual(0, hit.attackerHash);
                Assert.AreEqual(source, hit.result.attackerRef);
                Assert.AreNotEqual(replacement, hit.result.attackerRef);
            });
            using System.IDisposable deathSubscription = events.OnCharacterDeath.Subscribe(death =>
            { Assert.AreEqual(0, death.killerHash); deaths++; });
            using System.IDisposable resolvedSubscription = events.OnCharacterDeathResolved.Subscribe(death =>
            { Assert.AreEqual(source, death.Killer); Assert.AreEqual(target, death.Dead); });
            events.FireDamageDealt(new DamageResult { attackerRef = source, defenderRef = target }, source.Hash, target.Hash);
            events.FireCharacterDeath(target, source);
            Assert.AreEqual(1, damages);
            Assert.AreEqual(1, deaths);
        }

        [Test]
        public void GameEvents_InlineSourceReuse_DoesNotAttributeLaterBridgeNotificationToReplacement()
        {
            ActorRegistry actors = new ActorRegistry();
            ActorRef source = actors.Register(11);
            ActorRef target = actors.Register(22);
            using GameEvents events = new GameEvents(actors);
            using System.IDisposable subscription = events.OnDamageDealt.Subscribe(hit =>
            {
                Assert.AreEqual(11, hit.attackerHash);
                actors.Unregister(source.Hash);
                actors.Register(source.Hash);
            });
            int bridgeNotifications = 0;
            events.OnDamageDealtEvent += (damage, sourceHash, targetHash) =>
            {
                bridgeNotifications++;
                Assert.AreEqual(0, sourceHash);
                Assert.AreEqual(source, damage.attackerRef);
                Assert.IsFalse(actors.IsCurrent(damage.attackerRef));
            };
            events.FireDamageDealt(new DamageResult { attackerRef = source, defenderRef = target }, source.Hash, target.Hash);
            Assert.AreEqual(1, bridgeNotifications);
        }
    }
}
