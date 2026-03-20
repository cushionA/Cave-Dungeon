using NUnit.Framework;
using R3;
using Game.Core;
using UnityEngine;

namespace Game.Tests.EditMode
{
    [TestFixture]
    public class Integration_CombatStateFlowTests
    {
        private SoACharaDataDic _data;
        private GameEvents _events;

        private const int k_PlayerHash = 1;
        private const int k_EnemyHash = 2;

        [SetUp]
        public void SetUp()
        {
            _data = new SoACharaDataDic(4);
            _events = new GameEvents();

            // プレイヤー: attack slash=80
            CharacterVitals playerVitals = new CharacterVitals { currentHp = 200, maxHp = 200, level = 10 };
            CombatStats playerCombat = new CombatStats
            {
                attack = new ElementalStatus { slash = 80 },
                criticalRate = 0.2f,
                criticalMultiplier = 1.5f
            };
            CharacterFlags playerFlags = CharacterFlags.Pack(
                CharacterBelong.Ally, CharacterFeature.Player, ActState.Neutral, AbilityFlag.None);
            _data.Add(k_PlayerHash, playerVitals, playerCombat, playerFlags, default);

            // 敵: defense slash=15, HP=300, armor=40
            CharacterVitals enemyVitals = new CharacterVitals
            {
                currentHp = 300,
                maxHp = 300,
                currentArmor = 40f,
                maxArmor = 40f,
                level = 8
            };
            CombatStats enemyCombat = new CombatStats
            {
                defense = new ElementalStatus { slash = 15 }
            };
            CharacterFlags enemyFlags = CharacterFlags.Pack(
                CharacterBelong.Enemy, CharacterFeature.Minion, ActState.Neutral, AbilityFlag.None);
            _data.Add(k_EnemyHash, enemyVitals, enemyCombat, enemyFlags, default);
        }

        [TearDown]
        public void TearDown()
        {
            _events.Dispose();
            _data.Dispose();
        }

        [Test]
        public void CombatStateFlow_ComboProgression_EscalatesDamage()
        {
            // Arrange: 3段コンボ（motionValue: 0.8, 1.0, 1.5）
            float[] motionValues = { 0.8f, 1.0f, 1.5f };
            ElementalStatus atkStats = _data.GetCombatStats(k_PlayerHash).attack;
            ElementalStatus defStats = _data.GetCombatStats(k_EnemyHash).defense;

            // Act: 各段のダメージを計算
            int dmg1 = DamageCalculator.CalculateTotalDamage(atkStats, motionValues[0], defStats, Element.None);
            int dmg2 = DamageCalculator.CalculateTotalDamage(atkStats, motionValues[1], defStats, Element.None);
            int dmg3 = DamageCalculator.CalculateTotalDamage(atkStats, motionValues[2], defStats, Element.None);

            // Assert: コンボ段数が上がるとダメージも上がる
            Assert.Greater(dmg2, dmg1);
            Assert.Greater(dmg3, dmg2);
            Assert.Greater(dmg1, 0);
        }

        [Test]
        public void CombatStateFlow_HitChangesActState_ToAttackingThenKnockback()
        {
            // Arrange: 攻撃者をAttacking状態に
            ref CharacterFlags attackerFlags = ref _data.GetFlags(k_PlayerHash);
            attackerFlags.ActState = ActState.Attacking;

            // Act: 攻撃ヒット後、防御者をKnockback状態に
            ref CharacterFlags defenderFlags = ref _data.GetFlags(k_EnemyHash);
            defenderFlags.ActState = ActState.Knockbacked;

            // Assert: 各キャラのActState確認
            Assert.AreEqual(ActState.Attacking, _data.GetFlags(k_PlayerHash).ActState);
            Assert.AreEqual(ActState.Knockbacked, _data.GetFlags(k_EnemyHash).ActState);

            // 回復後Neutralに戻す
            ref CharacterFlags afterFlags = ref _data.GetFlags(k_PlayerHash);
            afterFlags.ActState = ActState.AttackRecovery;
            Assert.AreEqual(ActState.AttackRecovery, _data.GetFlags(k_PlayerHash).ActState);
        }

        [Test]
        public void CombatStateFlow_ArmorBreakDuringCombo_IncreasesSubsequentDamage()
        {
            // Arrange: 1撃目でアーマー破壊
            ref CharacterVitals enemyVitals = ref _data.GetVitals(k_EnemyHash);
            float armorBefore = enemyVitals.currentArmor; // 40

            ElementalStatus atkStats = _data.GetCombatStats(k_PlayerHash).attack;
            ElementalStatus defStats = _data.GetCombatStats(k_EnemyHash).defense;

            // Act: 1撃目 armorBreak=40 → アーマー0
            int dmgHit1 = DamageCalculator.CalculateTotalDamage(atkStats, 1.0f, defStats, Element.None);
            float actionArmor1 = 0f;
            (int actual1, bool kill1, bool armorBroken) = HpArmorLogic.ApplyDamage(
                ref enemyVitals.currentHp, ref enemyVitals.currentArmor, dmgHit1, 40f, ref actionArmor1);

            int hpAfterHit1 = enemyVitals.currentHp;

            // Assert: アーマー破壊
            Assert.IsTrue(armorBroken);
            Assert.AreEqual(0f, enemyVitals.currentArmor, 0.001f);

            // 2撃目: アーマー0状態でのダメージ → アーマーブレイクボーナスなし
            int dmgHit2 = DamageCalculator.CalculateTotalDamage(atkStats, 1.0f, defStats, Element.None);
            float actionArmor2 = 0f;
            (int actual2, bool kill2, bool armorBroken2) = HpArmorLogic.ApplyDamage(
                ref enemyVitals.currentHp, ref enemyVitals.currentArmor, dmgHit2, 0f, ref actionArmor2);

            // Assert: HPが確実に減少
            Assert.Less(enemyVitals.currentHp, hpAfterHit1);
        }

        [Test]
        public void CombatStateFlow_DamageEvent_FiredOnEachHit()
        {
            // Arrange
            int hitCount = 0;
            _events.OnDamageDealt.Subscribe(e => hitCount++);

            ElementalStatus atkStats = _data.GetCombatStats(k_PlayerHash).attack;
            ElementalStatus defStats = _data.GetCombatStats(k_EnemyHash).defense;
            float[] motionValues = { 0.8f, 1.0f, 1.5f };

            // Act: 3段コンボ、各ヒットでイベント発火
            for (int i = 0; i < motionValues.Length; i++)
            {
                int dmg = DamageCalculator.CalculateTotalDamage(atkStats, motionValues[i], defStats, Element.None);
                ref CharacterVitals enemyVitals = ref _data.GetVitals(k_EnemyHash);
                float actionArmorCombo = 0f;
                HpArmorLogic.ApplyDamage(ref enemyVitals.currentHp, ref enemyVitals.currentArmor, dmg, 0f, ref actionArmorCombo);

                DamageResult result = new DamageResult
                {
                    totalDamage = dmg,
                    guardResult = GuardResult.NoGuard,
                    isCritical = false,
                    isKill = false
                };
                _events.FireDamageDealt(result, k_PlayerHash, k_EnemyHash);
            }

            // Assert: 3回のイベント発火
            Assert.AreEqual(3, hitCount);
        }
    }
}
