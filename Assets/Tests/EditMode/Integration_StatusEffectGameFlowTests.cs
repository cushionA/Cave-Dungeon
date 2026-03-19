using NUnit.Framework;
using Game.Core;
using UnityEngine;

namespace Game.Tests.EditMode
{
    [TestFixture]
    public class Integration_StatusEffectGameFlowTests
    {
        private SoACharaDataDic _data;
        private GameEvents _events;
        private StatusEffectManager _statusManager;

        private const int k_TargetHash = 1;

        [SetUp]
        public void SetUp()
        {
            _data = new SoACharaDataDic(4);
            _events = new GameEvents();
            _statusManager = new StatusEffectManager();

            CharacterVitals vitals = new CharacterVitals
            {
                currentHp = 100,
                maxHp = 100,
                position = Vector2.zero,
                level = 5
            };
            CombatStats combat = new CombatStats
            {
                guardStats = new GuardStats { statusCut = 0f }
            };
            CharacterFlags flags = CharacterFlags.Pack(
                CharacterBelong.Ally, CharacterFeature.Player, AbilityFlag.None);

            _data.Add(k_TargetHash, vitals, combat, flags, default);
        }

        [TearDown]
        public void TearDown()
        {
            _events.Clear();
            _data.Dispose();
        }

        [Test]
        public void StatusEffectGameFlow_AccumulateThenApply_ReducesHpViaTick()
        {
            // Arrange
            StatusEffectInfo poisonInfo = new StatusEffectInfo
            {
                effect = StatusEffectId.Poison,
                accumulateValue = 60f,
                duration = 10f,
                tickDamage = 5f,
                tickInterval = 2f,
                modifier = 0f,
                maxStack = 0
            };

            bool eventFired = false;
            _events.OnStatusEffectApplied += (hash, id) => eventFired = true;

            // Act: 1ヒット目 → 60蓄積（閾値100未満）
            bool triggered1 = _statusManager.Accumulate(poisonInfo, 0f);
            Assert.IsFalse(triggered1);
            Assert.IsFalse(_statusManager.IsActive(StatusEffectId.Poison));

            // 2ヒット目 → 120蓄積 > 100 → 発症
            bool triggered2 = _statusManager.Accumulate(poisonInfo, 0f);
            Assert.IsTrue(triggered2);
            Assert.IsTrue(_statusManager.IsActive(StatusEffectId.Poison));

            // イベント発火
            _events.FireStatusEffectApplied(k_TargetHash, StatusEffectId.Poison);
            Assert.IsTrue(eventFired);

            // Tick 2秒 → tickDamage=5 → HP=95
            int tickDmg = _statusManager.Tick(2f);
            ref CharacterVitals vitals = ref _data.GetVitals(k_TargetHash);
            vitals.currentHp -= tickDmg;

            Assert.AreEqual(5, tickDmg);
            Assert.AreEqual(95, vitals.currentHp);
        }

        [Test]
        public void StatusEffectGameFlow_DecayRemovesEffect_RestoresNormalState()
        {
            // Arrange: 毒発症（duration=10秒）
            StatusEffectInfo poisonInfo = new StatusEffectInfo
            {
                effect = StatusEffectId.Poison,
                accumulateValue = 100f,
                duration = 10f,
                tickDamage = 5f,
                tickInterval = 2f,
                modifier = 0f,
                maxStack = 0
            };
            _statusManager.Accumulate(poisonInfo, 0f);
            Assert.IsTrue(_statusManager.IsActive(StatusEffectId.Poison));
            Assert.AreEqual(1, _statusManager.ActiveCount);

            // Act: 10秒経過 → 効果終了
            _statusManager.Tick(10f);

            // Assert: 効果消滅
            Assert.AreEqual(0, _statusManager.ActiveCount);

            // さらにTick → ダメージなし
            int tickDmg = _statusManager.Tick(2f);
            Assert.AreEqual(0, tickDmg);
        }

        [Test]
        public void StatusEffectGameFlow_ResistanceReducesAccumulation_DelaysTrigger()
        {
            // Arrange: 蓄積60/hit, 耐性0.5 → 実効30/hit, 閾値100到達に4ヒット必要
            StatusEffectInfo poisonInfo = new StatusEffectInfo
            {
                effect = StatusEffectId.Poison,
                accumulateValue = 60f,
                duration = 10f,
                tickDamage = 5f,
                tickInterval = 2f,
                modifier = 0f,
                maxStack = 0
            };
            float statusCut = 0.5f; // 60 * (1-0.5) = 30/hit

            // Act: 3ヒット → 90蓄積（未発症）
            _statusManager.Accumulate(poisonInfo, statusCut);
            _statusManager.Accumulate(poisonInfo, statusCut);
            bool triggered3 = _statusManager.Accumulate(poisonInfo, statusCut);
            Assert.IsFalse(triggered3);
            Assert.IsFalse(_statusManager.IsActive(StatusEffectId.Poison));

            // 4ヒット目 → 120蓄積 > 100 → 発症
            bool triggered4 = _statusManager.Accumulate(poisonInfo, statusCut);
            Assert.IsTrue(triggered4);
            Assert.IsTrue(_statusManager.IsActive(StatusEffectId.Poison));
        }

        [Test]
        public void StatusEffectGameFlow_ThreeSimultaneousEffects_AllTick()
        {
            // Arrange: 3種同時発症
            StatusEffectInfo poisonInfo = new StatusEffectInfo
            {
                effect = StatusEffectId.Poison,
                accumulateValue = 100f,
                duration = 10f,
                tickDamage = 5f,
                tickInterval = 2f,
                modifier = 0f,
                maxStack = 0
            };
            StatusEffectInfo burnInfo = new StatusEffectInfo
            {
                effect = StatusEffectId.Burn,
                accumulateValue = 100f,
                duration = 10f,
                tickDamage = 5f,
                tickInterval = 2f,
                modifier = 0f,
                maxStack = 0
            };
            StatusEffectInfo bleedInfo = new StatusEffectInfo
            {
                effect = StatusEffectId.Bleed,
                accumulateValue = 100f,
                duration = 10f,
                tickDamage = 5f,
                tickInterval = 2f,
                modifier = 0f,
                maxStack = 0
            };

            Assert.IsTrue(_statusManager.Accumulate(poisonInfo, 0f));
            Assert.IsTrue(_statusManager.Accumulate(burnInfo, 0f));
            Assert.IsTrue(_statusManager.Accumulate(bleedInfo, 0f));
            Assert.AreEqual(3, _statusManager.ActiveCount);

            // Act: 2秒Tick → 3種 × 5 = 15
            int totalTickDmg = _statusManager.Tick(2f);
            ref CharacterVitals vitals = ref _data.GetVitals(k_TargetHash);
            vitals.currentHp -= totalTickDmg;

            // Assert
            Assert.AreEqual(15, totalTickDmg);
            Assert.AreEqual(85, vitals.currentHp);
        }
    }
}
