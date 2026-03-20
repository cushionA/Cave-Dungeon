using NUnit.Framework;
using R3;
using Game.Core;
using UnityEngine;

namespace Game.Tests.EditMode
{
    [TestFixture]
    public class Integration_CharacterVitalsUpdateTests
    {
        private SoACharaDataDic _data;
        private GameEvents _events;
        private StatusEffectManager _statusManager;

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
                currentArmor = 30f,
                maxArmor = 30f,
                position = Vector2.zero,
                level = 5
            };
            CombatStats combat = new CombatStats
            {
                defense = new ElementalStatus { slash = 20 },
                guardStats = new GuardStats { statusCut = 0f }
            };
            CharacterFlags flags = CharacterFlags.Pack(
                CharacterBelong.Ally,
                CharacterFeature.Player,
                AbilityFlag.None
            );

            _data.Add(1, vitals, combat, flags, default);
        }

        [TearDown]
        public void TearDown()
        {
            _events.Dispose();
            _data.Dispose();
        }

        [Test]
        public void CharacterVitals_DamageReducesHp_ToZero_FiresDeathEvent()
        {
            // Arrange
            bool deathFired = false;
            int deadHash = 0;
            int killerHash = 0;
            _events.OnCharacterDeath.Subscribe(e =>
            {
                deathFired = true;
                deadHash = e.deadHash;
                killerHash = e.killerHash;
            });

            // Act: 致死ダメージを計算→HP適用→死亡イベント
            int damage = DamageCalculator.CalculateTotalDamage(
                new ElementalStatus { slash = 200 },
                1.5f,
                _data.GetCombatStats(1).defense,
                Element.None
            );

            ref CharacterVitals vitals = ref _data.GetVitals(1);
            int hpBefore = vitals.currentHp;
            float actionArmor = 0f;
            HpArmorLogic.ApplyDamage(ref vitals.currentHp, ref vitals.currentArmor, damage, 0f, ref actionArmor);

            if (vitals.currentHp <= 0)
            {
                _events.FireCharacterDeath(1, 2);
            }

            // Assert
            Assert.AreEqual(0, _data.GetVitals(1).currentHp);
            Assert.IsTrue(deathFired);
            Assert.AreEqual(1, deadHash);
            Assert.AreEqual(2, killerHash);
        }

        [Test]
        public void CharacterVitals_ArmorDepletionThenHpDamage_CorrectOrder()
        {
            // Arrange: アーマー30のキャラに armorBreak=30 でヒット → アーマー0に
            ref CharacterVitals vitals = ref _data.GetVitals(1);
            float armorBefore = vitals.currentArmor;
            Assert.AreEqual(30f, armorBefore, 0.001f);

            // Act: 1撃目 armorBreak=30 → アーマー枯渇
            int rawDamage1 = 40;
            float actionArmor1 = 0f;
            (int actual1, bool kill1, bool armorBroken1) = HpArmorLogic.ApplyDamage(
                ref vitals.currentHp, ref vitals.currentArmor, rawDamage1, 30f, ref actionArmor1);

            int hpAfterHit1 = vitals.currentHp;
            float armorAfterHit1 = vitals.currentArmor;

            // 2撃目: アーマー0で全ダメージがHPへ
            int rawDamage2 = 30;
            float actionArmor2 = 0f;
            (int actual2, bool kill2, bool armorBroken2) = HpArmorLogic.ApplyDamage(
                ref vitals.currentHp, ref vitals.currentArmor, rawDamage2, 0f, ref actionArmor2);

            // Assert
            Assert.AreEqual(0f, armorAfterHit1, 0.001f);
            Assert.IsTrue(armorBroken1);
            Assert.Less(vitals.currentHp, hpAfterHit1);
            Assert.IsFalse(kill2);
        }

        [Test]
        public void CharacterVitals_StatusEffectTickDamage_ReducesHpOverTime()
        {
            // Arrange: 毒を蓄積して発症させる（閾値100）
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

            bool triggered = _statusManager.Accumulate(poisonInfo, 0f);
            Assert.IsTrue(triggered);
            Assert.IsTrue(_statusManager.IsActive(StatusEffectId.Poison));

            // Act: Tick 2秒 → tickDamage=5
            int tickDmg1 = _statusManager.Tick(2f);
            ref CharacterVitals vitals = ref _data.GetVitals(1);
            vitals.currentHp -= tickDmg1;

            Assert.AreEqual(5, tickDmg1);
            Assert.AreEqual(95, vitals.currentHp);

            // Act: さらに2秒Tick → 追加tickDamage=5
            int tickDmg2 = _statusManager.Tick(2f);
            vitals.currentHp -= tickDmg2;

            Assert.AreEqual(5, tickDmg2);
            Assert.AreEqual(90, vitals.currentHp);
        }

        [Test]
        public void CharacterVitals_MultipleStatusEffects_StackTickDamage()
        {
            // Arrange: 毒と火傷の両方を発症
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

            bool poisonTriggered = _statusManager.Accumulate(poisonInfo, 0f);
            bool burnTriggered = _statusManager.Accumulate(burnInfo, 0f);

            Assert.IsTrue(poisonTriggered);
            Assert.IsTrue(burnTriggered);
            Assert.AreEqual(2, _statusManager.ActiveCount);

            // Act: 2秒Tick → 毒5 + 火傷5 = 10
            int totalTickDmg = _statusManager.Tick(2f);
            ref CharacterVitals vitals = ref _data.GetVitals(1);
            vitals.currentHp -= totalTickDmg;

            // Assert
            Assert.AreEqual(10, totalTickDmg);
            Assert.AreEqual(90, vitals.currentHp);
        }
    }
}
