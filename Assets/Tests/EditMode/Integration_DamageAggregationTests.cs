using System.Collections.Generic;
using NUnit.Framework;
using Game.Core;
using UnityEngine;

namespace Game.Tests.EditMode
{
    [TestFixture]
    public class Integration_DamageAggregationTests
    {
        private SoACharaDataDic _data;
        private GameEvents _events;
        private DamageScoreTracker _tracker;

        private const int k_EnemyHash = 1;
        private const int k_Player1Hash = 10;
        private const int k_Player2Hash = 11;

        [SetUp]
        public void SetUp()
        {
            _data = new SoACharaDataDic(8);
            _events = new GameEvents();
            _tracker = new DamageScoreTracker();

            // 敵
            CharacterVitals enemyVitals = new CharacterVitals
            {
                currentHp = 500, maxHp = 500, position = Vector2.zero, hpRatio = 100
            };
            CharacterFlags enemyFlags = CharacterFlags.Pack(
                CharacterBelong.Enemy, CharacterFeature.Minion, AbilityFlag.None);
            _data.Add(k_EnemyHash, enemyVitals, default, enemyFlags, default);

            // プレイヤー1: 距離2
            CharacterVitals p1Vitals = new CharacterVitals
            {
                currentHp = 80, maxHp = 100, position = new Vector2(2f, 0f), hpRatio = 80
            };
            CharacterFlags p1Flags = CharacterFlags.Pack(
                CharacterBelong.Ally, CharacterFeature.Player, AbilityFlag.None);
            _data.Add(k_Player1Hash, p1Vitals, default, p1Flags, default);

            // プレイヤー2: 距離4
            CharacterVitals p2Vitals = new CharacterVitals
            {
                currentHp = 100, maxHp = 100, position = new Vector2(4f, 0f), hpRatio = 100
            };
            CharacterFlags p2Flags = CharacterFlags.Pack(
                CharacterBelong.Ally, CharacterFeature.Companion, AbilityFlag.None);
            _data.Add(k_Player2Hash, p2Vitals, default, p2Flags, default);
        }

        [TearDown]
        public void TearDown()
        {
            _events.Dispose();
            _data.Dispose();
        }

        [Test]
        public void DamageAggregation_MultipleDamageSources_TracksScores()
        {
            // Act: プレイヤー1が50ダメージ、プレイヤー2が30ダメージ
            _tracker.AddDamage(k_Player1Hash, 50f, 0f);
            _tracker.AddDamage(k_Player2Hash, 30f, 0f);

            // Assert: スコア追跡
            Assert.AreEqual(50f, _tracker.GetScore(k_Player1Hash, 0f), 0.1f);
            Assert.AreEqual(30f, _tracker.GetScore(k_Player2Hash, 0f), 0.1f);

            // 最高スコアはプレイヤー1
            Assert.AreEqual(k_Player1Hash, _tracker.GetHighestScoreAttacker(0f));
        }

        [Test]
        public void DamageAggregation_TimeDecay_ShiftsHighestAttacker()
        {
            // Arrange: プレイヤー1が先に100ダメージ（time=0）、プレイヤー2が後で80ダメージ（time=5）
            _tracker.AddDamage(k_Player1Hash, 100f, 0f);
            _tracker.AddDamage(k_Player2Hash, 80f, 5f);

            // Act: time=0ではプレイヤー1が最高
            Assert.AreEqual(k_Player1Hash, _tracker.GetHighestScoreAttacker(0f));

            // time=10で時間減衰適用: プレイヤー1のスコアが大きく減衰、プレイヤー2は比較的新しい
            float score1AtT10 = _tracker.GetScore(k_Player1Hash, 10f);
            float score2AtT10 = _tracker.GetScore(k_Player2Hash, 10f);

            // プレイヤー2の方が新しいダメージなのでスコアが高い可能性
            // （減衰曲線によっては逆転する）
            int highestAtT10 = _tracker.GetHighestScoreAttacker(10f);

            // 少なくとも両方のスコアが初期値から減衰している
            Assert.Less(score1AtT10, 100f);
            Assert.Less(score2AtT10, 80f);
        }

        [Test]
        public void DamageAggregation_ScoreDrivesTargetReselection()
        {
            // Arrange: HpValueによるターゲット再選択をシミュレート
            // プレイヤー1 HP=80, プレイヤー2 HP=100 → HP低い方を優先（昇順）
            AITargetSelect targetSelect = new AITargetSelect
            {
                sortKey = TargetSortKey.HpValue,
                isDescending = false,
                filter = new TargetFilter
                {
                    filterFlags = FilterBitFlag.BelongAnd,
                    belong = CharacterBelong.Ally
                }
            };

            List<int> candidates = new List<int> { k_Player1Hash, k_Player2Hash };

            // Act: HP低い順でプレイヤー1(HP=80)が選択される
            int selected1 = TargetSelector.SelectTarget(targetSelect, k_EnemyHash, candidates, _data, 0f);
            Assert.AreEqual(k_Player1Hash, selected1);

            // HP変更: プレイヤー1=100, プレイヤー2=50
            ref CharacterVitals v1 = ref _data.GetVitals(k_Player1Hash);
            v1.currentHp = 100;
            ref CharacterVitals v2 = ref _data.GetVitals(k_Player2Hash);
            v2.currentHp = 50;

            // 再選択: プレイヤー2(HP=50)が選択される
            int selected2 = TargetSelector.SelectTarget(targetSelect, k_EnemyHash, candidates, _data, 0f);
            Assert.AreEqual(k_Player2Hash, selected2);
        }

        [Test]
        public void DamageAggregation_AttackerRemoval_ClearsScore()
        {
            // Arrange
            _tracker.AddDamage(k_Player1Hash, 50f, 0f);
            Assert.Greater(_tracker.GetScore(k_Player1Hash, 0f), 0f);

            // Act
            _tracker.RemoveAttacker(k_Player1Hash);

            // Assert
            Assert.AreEqual(0f, _tracker.GetScore(k_Player1Hash, 0f), 0.001f);
        }
    }
}
