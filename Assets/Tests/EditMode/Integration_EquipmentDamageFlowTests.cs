using NUnit.Framework;
using R3;
using Game.Core;
using UnityEngine;

namespace Game.Tests.EditMode
{
    [TestFixture]
    public class Integration_EquipmentDamageFlowTests
    {
        private SoACharaDataDic _data;
        private GameEvents _events;
        private StatusEffectManager _defenderStatusManager;

        private const int k_AttackerHash = 1;
        private const int k_DefenderHash = 2;

        [SetUp]
        public void SetUp()
        {
            _data = new SoACharaDataDic(4);
            _events = new GameEvents();
            _defenderStatusManager = new StatusEffectManager();

            // 攻撃者: slash=100, fire=30
            CharacterVitals atkVitals = new CharacterVitals { currentHp = 200, maxHp = 200, level = 10 };
            CombatStats atkCombat = new CombatStats
            {
                attack = new ElementalStatus { slash = 100, fire = 30 },
                criticalRate = 0.3f,
                criticalMultiplier = 1.5f
            };
            EquipmentStatus atkEquip = new EquipmentStatus
            {
                weaponId = 1,
                finalAttack = new ElementalStatus { slash = 100, fire = 30 }
            };
            CharacterFlags atkFlags = CharacterFlags.Pack(CharacterBelong.Ally, CharacterFeature.Player, AbilityFlag.None);
            _data.Add(k_AttackerHash, atkVitals, atkCombat, atkFlags, default, atkEquip);

            // 防御者: def slash=20, fire=10, HP=150, armor=50
            CharacterVitals defVitals = new CharacterVitals
            {
                currentHp = 150,
                maxHp = 150,
                currentArmor = 50f,
                maxArmor = 50f,
                level = 8
            };
            CombatStats defCombat = new CombatStats
            {
                defense = new ElementalStatus { slash = 20, fire = 10 },
                guardStats = new GuardStats
                {
                    slashCut = 0.5f,
                    guardStrength = 80f,
                    statusCut = 0.3f
                }
            };
            CharacterFlags defFlags = CharacterFlags.Pack(CharacterBelong.Enemy, CharacterFeature.Minion, AbilityFlag.None);
            _data.Add(k_DefenderHash, defVitals, defCombat, defFlags, default);
        }

        [TearDown]
        public void TearDown()
        {
            _events.Dispose();
            _data.Dispose();
        }

        [Test]
        public void EquipmentDamageFlow_WeaponEquipToHit_CalculatesDamageFromEquipmentStats()
        {
            // Arrange: 装備のfinalAttackを使ってダメージ計算
            EquipmentStatus equip = _data.GetEquipmentStatus(k_AttackerHash);
            ElementalStatus defenseStats = _data.GetCombatStats(k_DefenderHash).defense;

            // Act: motionValue=1.2で7属性ダメージ計算
            int totalDamage = DamageCalculator.CalculateTotalDamage(
                equip.finalAttack,
                1.2f,
                defenseStats,
                Element.None
            );

            // Assert: 各チャネルの計算値を手動検証
            // slash: (100²×1.2)/(100+20) = 12000/120 = 100
            // fire:  (30²×1.2)/(30+10)   = 1080/40   = 27
            int expectedSlash = DamageCalculator.CalculateChannelDamage(100, 1.2f, 20, Element.Slash, Element.None);
            int expectedFire = DamageCalculator.CalculateChannelDamage(30, 1.2f, 10, Element.Fire, Element.None);
            int expected = expectedSlash + expectedFire;

            Assert.AreEqual(expected, totalDamage);
            Assert.Greater(totalDamage, 0);
        }

        [Test]
        public void EquipmentDamageFlow_GuardedHit_ReducesDamage()
        {
            // Arrange: 生ダメージ計算
            EquipmentStatus equip = _data.GetEquipmentStatus(k_AttackerHash);
            int rawDamage = DamageCalculator.CalculateTotalDamage(
                equip.finalAttack,
                1.0f,
                _data.GetCombatStats(k_DefenderHash).defense,
                Element.None
            );

            // Act: ガード判定（通常ガード: guardTime > JustGuardWindow）
            GuardResult guardResult = GuardJudgmentLogic.Judge(
                isGuarding: true,
                guardTimeSinceStart: 0.5f,
                guardStrength: 80f,
                attackPower: 50f,
                attackFeature: AttackFeature.None
            );

            float reduction = GuardJudgmentLogic.GetDamageReduction(guardResult);
            int guardedDamage = (int)(rawDamage * (1f - reduction));

            // Assert
            Assert.AreEqual(GuardResult.Guarded, guardResult);
            Assert.Less(guardedDamage, rawDamage);
            Assert.Greater(guardedDamage, 0);
        }

        [Test]
        public void EquipmentDamageFlow_HitWithStatusEffect_AccumulatesAndTriggers()
        {
            // Arrange: 状態異常付き攻撃（蓄積60、耐性0.3 → 実効42/hit、閾値100）
            StatusEffectInfo poisonInfo = new StatusEffectInfo
            {
                effect = StatusEffectId.Poison,
                accumulateValue = 60f,
                duration = 10f,
                tickDamage = 5f,
                tickInterval = 2f,
                modifier = 0.2f,
                maxStack = 0
            };
            float statusCut = _data.GetCombatStats(k_DefenderHash).guardStats.statusCut; // 0.3

            // Act: 1ヒット目 → 60*(1-0.3)=42 蓄積
            bool triggered1 = _defenderStatusManager.Accumulate(poisonInfo, statusCut);
            Assert.IsFalse(triggered1);
            Assert.IsFalse(_defenderStatusManager.IsActive(StatusEffectId.Poison));

            // 2ヒット目 → 42+42=84 蓄積（まだ未発症）
            bool triggered2 = _defenderStatusManager.Accumulate(poisonInfo, statusCut);
            Assert.IsFalse(triggered2);
            Assert.IsFalse(_defenderStatusManager.IsActive(StatusEffectId.Poison));

            // 3ヒット目 → 84+42=126 > 100 → 発症
            bool triggered3 = _defenderStatusManager.Accumulate(poisonInfo, statusCut);
            Assert.IsTrue(triggered3);
            Assert.IsTrue(_defenderStatusManager.IsActive(StatusEffectId.Poison));
        }

        [Test]
        public void EquipmentDamageFlow_FullChain_DamageReducesHpAndArmor()
        {
            // Arrange: イベントリスナー
            bool damageEventFired = false;
            _events.OnDamageDealt.Subscribe(e => damageEventFired = true);

            EquipmentStatus equip = _data.GetEquipmentStatus(k_AttackerHash);
            int totalDamage = DamageCalculator.CalculateTotalDamage(
                equip.finalAttack,
                1.2f,
                _data.GetCombatStats(k_DefenderHash).defense,
                Element.None
            );

            // Act: HP/アーマーにダメージ適用
            ref CharacterVitals defVitals = ref _data.GetVitals(k_DefenderHash);
            int hpBefore = defVitals.currentHp;
            float armorBefore = defVitals.currentArmor;

            (int actualDmg, bool isKill, bool armorBroken) = HpArmorLogic.ApplyDamage(
                ref defVitals.currentHp, ref defVitals.currentArmor, totalDamage, 20f);

            // イベント発火
            DamageResult damageResult = new DamageResult
            {
                totalDamage = actualDmg,
                guardResult = GuardResult.NoGuard,
                isCritical = false,
                isKill = isKill,
                armorDamage = 20f,
                appliedEffect = StatusEffectId.None
            };
            _events.FireDamageDealt(damageResult, k_AttackerHash, k_DefenderHash);

            // Assert
            Assert.Less(_data.GetVitals(k_DefenderHash).currentHp, hpBefore);
            Assert.Less(_data.GetVitals(k_DefenderHash).currentArmor, armorBefore);
            Assert.IsTrue(damageEventFired);
        }

        [Test]
        public void EquipmentDamageFlow_LowElementalDefense_TakesMoreDamage()
        {
            // 弱点システムは存在しない。属性防御力の高低でダメージ量が変わる。
            // 防御者A: slash防御=50, fire防御=5（fire防御が極端に低い）
            // 防御者B: slash防御=50, fire防御=50（均等防御）
            // 攻撃者: slash=100, fire=30 → fire防御が低い相手にはfireチャネルが大きく通る

            ElementalStatus atkStats = _data.GetEquipmentStatus(k_AttackerHash).finalAttack;

            ElementalStatus defLowFire = new ElementalStatus { slash = 50, fire = 5 };
            ElementalStatus defBalanced = new ElementalStatus { slash = 50, fire = 50 };

            // Act
            int dmgVsLowFire = DamageCalculator.CalculateTotalDamage(atkStats, 1.0f, defLowFire, Element.None);
            int dmgVsBalanced = DamageCalculator.CalculateTotalDamage(atkStats, 1.0f, defBalanced, Element.None);

            // Assert: fire防御が低い相手の方がトータルダメージが高い
            Assert.Greater(dmgVsLowFire, dmgVsBalanced);

            // 個別チャネル検証: fire防御5 vs fire防御50
            int fireDmgLow = DamageCalculator.CalculateChannelDamage(30, 1.0f, 5, Element.Fire, Element.None);
            int fireDmgHigh = DamageCalculator.CalculateChannelDamage(30, 1.0f, 50, Element.Fire, Element.None);
            Assert.Greater(fireDmgLow, fireDmgHigh);
        }
    }
}
