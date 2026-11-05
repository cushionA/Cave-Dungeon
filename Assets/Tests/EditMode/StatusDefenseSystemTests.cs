using System;
using Game.Core;
using NUnit.Framework;
using R3;

namespace Game.Tests.EditMode
{
    /// <summary>実GameManagerCoreとNative SoAを通す接続回帰。外部純ロジックハーネスの対象外。</summary>
    public class StatusDefenseSystemTests
    {
        private const int k_SourceHash = 1001;
        private const int k_TargetHash = 2002;
        private GameManagerCore _world;
        private ActorRef _source;
        private ActorRef _target;

        [SetUp]
        public void SetUp()
        {
            _world = new GameManagerCore();
            _world.Initialize(8);
            Register(k_SourceHash, CharacterBelong.Ally);
            Register(k_TargetHash, CharacterBelong.Enemy);
            Assert.IsTrue(_world.Actors.TryGet(k_SourceHash, out _source));
            Assert.IsTrue(_world.Actors.TryGet(k_TargetHash, out _target));
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        private void Register(int hash, CharacterBelong belong)
        {
            _world.RegisterCharacter(hash, new CharacterVitals { currentHp = 100, maxHp = 100 },
                default, CharacterFlags.Pack(belong, CharacterFeature.NPC, AbilityFlag.None), default);
        }

        private static StatusEffectInfo Info(StatusEffectId effect, float accumulation = 100f)
        {
            return new StatusEffectInfo
            {
                effect = effect, accumulateValue = accumulation, duration = 5f, modifier = 0.25f,
            };
        }

        private static StatusEffectApplicationData Application(StatusEffectId effect,
            StatusEffectApplicationMode mode = StatusEffectApplicationMode.Accumulate,
            StatusEffectTarget target = StatusEffectTarget.HitTarget)
        {
            return new StatusEffectApplicationData
            {
                info = Info(effect), mode = mode, target = target,
                guardRule = StatusEffectGuardRule.Ignore,
            };
        }

        [Test]
        public void StatusDefense_DirectAccumulation_CombinesCurrentProfileAndLegacyCutBeforeThreshold()
        {
            _world.Data.GetCombatStats(k_TargetHash).statusDefense = new StatusEffectDefenseProfile
            {
                commonReduction = 0.2f,
                poison = new StatusEffectDefense { accumulationReduction = 0.25f, threshold = 50f },
            };
            StatusEffectInfo poison = Info(StatusEffectId.Poison);

            Assert.IsFalse(_world.StatusEffects.Accumulate(k_TargetHash, poison, 0.5f, k_SourceHash));

            Assert.AreEqual(27.5f, _world.StatusEffects.Store.GetAccumulation(k_TargetHash, poison.effect), 0.0001f);
            Assert.IsFalse(_world.StatusEffects.Store.IsActive(k_TargetHash, poison.effect));
            Assert.IsTrue(_world.StatusEffects.Accumulate(k_TargetHash, poison, 0.5f, k_SourceHash));
            Assert.IsTrue(_world.StatusEffects.Store.IsActive(k_TargetHash, poison.effect));
            Assert.AreEqual(0f, _world.StatusEffects.Store.GetAccumulation(k_TargetHash, poison.effect));
        }

        [Test]
        public void StatusDefense_Immediate_UsesSpecificImmunityAndDoesNotUseCommonAccumulationCut()
        {
            _world.Data.GetCombatStats(k_TargetHash).statusDefense = new StatusEffectDefenseProfile
            {
                commonReduction = 1f,
                poison = new StatusEffectDefense { immune = true },
            };
            int applied = 0;
            using IDisposable subscription = _world.Events.OnStatusEffectApplied.Subscribe(_ => applied++);

            Assert.IsFalse(_world.StatusEffects.Apply(k_TargetHash, Info(StatusEffectId.Poison), k_SourceHash));
            Assert.AreEqual(0, applied);
            Assert.AreEqual(0, _world.StatusEffects.Store.Count);
            Assert.IsTrue(_world.StatusEffects.Apply(k_TargetHash, Info(StatusEffectId.Burn), k_SourceHash));

            Assert.AreEqual(1, applied);
            Assert.IsFalse(_world.StatusEffects.Store.IsActive(k_TargetHash, StatusEffectId.Poison));
            Assert.IsTrue(_world.StatusEffects.Store.IsActive(k_TargetHash, StatusEffectId.Burn));
        }

        [Test]
        public void StatusDefense_CapturedTechniqueAndEquipment_UseTheSameCurrentTargetProfile()
        {
            _world.Data.GetCombatStats(k_TargetHash).statusDefense = new StatusEffectDefenseProfile
            { commonReduction = 0.5f };
            StatusEffectApplicationData[] technique = { Application(StatusEffectId.Poison) };
            StatusEffectApplicationData[] equipment = { Application(StatusEffectId.Poison) };
            AttackContext attack = AttackContext.Capture(_source, default, 1f, CharacterBelong.Ally,
                equipment, technique);

            StatusEffectId result = _world.StatusEffects.ApplyApplications(attack, k_TargetHash,
                GuardResult.NoGuard, 0f);

            Assert.AreEqual(StatusEffectId.Poison, result);
            Assert.IsTrue(_world.StatusEffects.Store.TryGetEffect(k_TargetHash, StatusEffectId.Poison,
                out ActiveEffect effect));
            Assert.AreEqual(_source, effect.sourceRef);
            Assert.AreEqual(_target, effect.targetRef);
            Assert.AreEqual(0f, _world.StatusEffects.Store.GetAccumulation(k_TargetHash, StatusEffectId.Poison));
        }

        [Test]
        public void StatusDefense_SourceAccumulation_UsesSourceProfileWhileIgnoringHitTargetGuardCut()
        {
            _world.Data.GetCombatStats(k_SourceHash).statusDefense = new StatusEffectDefenseProfile
            {
                commonReduction = 0.75f,
                attackUp = new StatusEffectDefense { threshold = 50f },
            };
            StatusEffectApplicationData[] applications =
            { Application(StatusEffectId.AttackUp, target: StatusEffectTarget.Source) };
            AttackContext attack = AttackContext.Capture(_source, default, 1f, CharacterBelong.Ally,
                null, applications);

            Assert.AreEqual(StatusEffectId.None,
                _world.StatusEffects.ApplyApplications(attack, k_TargetHash, GuardResult.NoGuard, 1f));
            Assert.AreEqual(25f, _world.StatusEffects.Store.GetAccumulation(k_SourceHash, StatusEffectId.AttackUp));
            Assert.AreEqual(StatusEffectId.AttackUp,
                _world.StatusEffects.ApplyApplications(attack, k_TargetHash, GuardResult.NoGuard, 1f));
            Assert.AreEqual(1.25f, _world.Data.GetStatusEffects(k_SourceHash).AttackMultiplier, 0.0001f);
            Assert.AreEqual(1f, _world.Data.GetStatusEffects(k_TargetHash).AttackMultiplier);
        }

        [Test]
        public void StatusDefense_SourceImmediateBuff_CutDoesNotBlockButExplicitImmunityDoes()
        {
            _world.Data.GetCombatStats(k_SourceHash).statusDefense = new StatusEffectDefenseProfile
            { commonReduction = 1f };
            StatusEffectApplicationData[] applications =
            { Application(StatusEffectId.AttackUp, StatusEffectApplicationMode.Immediate, StatusEffectTarget.Source) };
            AttackContext attack = AttackContext.Capture(_source, default, 1f, CharacterBelong.Ally,
                null, applications);

            Assert.AreEqual(StatusEffectId.AttackUp,
                _world.StatusEffects.ApplyApplications(attack, k_TargetHash, GuardResult.NoGuard, 1f));
            Assert.AreEqual(1.25f, _world.Data.GetStatusEffects(k_SourceHash).AttackMultiplier, 0.0001f);
            _world.StatusEffects.Remove(k_SourceHash, StatusEffectId.AttackUp);
            _world.Data.GetCombatStats(k_SourceHash).statusDefense = new StatusEffectDefenseProfile
            { attackUp = new StatusEffectDefense { immune = true } };

            Assert.AreEqual(StatusEffectId.None,
                _world.StatusEffects.ApplyApplications(attack, k_TargetHash, GuardResult.NoGuard, 1f));
            Assert.AreEqual(1f, _world.Data.GetStatusEffects(k_SourceHash).AttackMultiplier);
        }

        [Test]
        public void StatusDefense_DefenseChangesBetweenApplications_UsesCurrentDefenseWithoutRetroactiveRemoval()
        {
            StatusEffectInfo poison = Info(StatusEffectId.Poison);
            _world.Data.GetCombatStats(k_TargetHash).statusDefense = new StatusEffectDefenseProfile
            { poison = new StatusEffectDefense { accumulationReduction = 0.5f } };
            Assert.IsFalse(_world.StatusEffects.Accumulate(k_TargetHash, poison, 0f, k_SourceHash));
            _world.Data.GetCombatStats(k_TargetHash).statusDefense = new StatusEffectDefenseProfile
            { poison = new StatusEffectDefense { immune = true } };

            Assert.IsFalse(_world.StatusEffects.Accumulate(k_TargetHash, poison, 0f, k_SourceHash));
            Assert.IsFalse(_world.StatusEffects.Apply(k_TargetHash, poison, k_SourceHash));
            Assert.AreEqual(50f, _world.StatusEffects.Store.GetAccumulation(k_TargetHash, poison.effect));
            _world.Data.GetCombatStats(k_TargetHash).statusDefense = default;

            Assert.IsTrue(_world.StatusEffects.Accumulate(k_TargetHash, poison, 0f, k_SourceHash));
            Assert.IsTrue(_world.StatusEffects.Store.IsActive(k_TargetHash, poison.effect));
        }

        [Test]
        public void StatusDefense_CapturedApplicationsTargetReusedDuringCallback_DoesNotAffectReplacement()
        {
            StatusEffectApplicationData[] applications =
            {
                Application(StatusEffectId.Freeze, StatusEffectApplicationMode.Immediate),
                Application(StatusEffectId.Poison, StatusEffectApplicationMode.Immediate),
            };
            AttackContext attack = AttackContext.Capture(_source, default, 1f, CharacterBelong.Ally,
                null, applications);
            bool replaced = false;
            using IDisposable subscription = _world.Events.OnStatusEffectApplied.Subscribe(notification =>
            {
                if (notification.targetHash != k_TargetHash || replaced) { return; }
                replaced = true;
                _world.UnregisterCharacter(k_TargetHash);
                Register(k_TargetHash, CharacterBelong.Enemy);
            });

            _world.StatusEffects.ApplyApplications(attack, k_TargetHash, GuardResult.NoGuard, 0f);

            Assert.IsTrue(replaced);
            Assert.IsTrue(_world.Actors.TryGet(k_TargetHash, out ActorRef replacement));
            Assert.AreNotEqual(_target, replacement);
            Assert.AreEqual(0, _world.StatusEffects.Store.Count);
            Assert.IsFalse(_world.StatusEffects.Store.IsActive(k_TargetHash, StatusEffectId.Poison));
            Assert.IsFalse(_world.Data.GetStatusEffects(k_TargetHash).BlocksMovement);
        }

        [Test]
        public void StatusNotification_TargetReplacedDuringChanged_DoesNotPublishAppliedForReplacement()
        {
            int applied = 0;
            bool replaced = false;
            using IDisposable appliedSubscription = _world.Events.OnStatusEffectApplied.Subscribe(_ => applied++);
            using IDisposable changedSubscription = _world.Events.OnStatusEffectChanged.Subscribe(notification =>
            {
                if (notification.targetHash != k_TargetHash || notification.kind != StatusEffectChangeKind.Applied
                    || replaced) { return; }
                replaced = true;
                _world.UnregisterCharacter(k_TargetHash);
                Register(k_TargetHash, CharacterBelong.Enemy);
            });

            Assert.IsTrue(_world.StatusEffects.Apply(k_TargetHash, Info(StatusEffectId.Poison), k_SourceHash));

            Assert.IsTrue(replaced);
            Assert.IsFalse(_world.Actors.IsCurrent(_target));
            Assert.AreEqual(0, applied);
            Assert.AreEqual(0, _world.Data.GetStatusEffects(k_TargetHash).ActiveCount);
        }
    }
}
