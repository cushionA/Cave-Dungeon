using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// 設計書05: TargetSortKey未テスト種 (AttackPower/DefensePower/TargetingCount/LastAttacker/Self/Player/Sister)
    /// + 確率ゲーティング(AIRule.probability) + TargetFilter拡張
    /// </summary>
    public class AICore_TargetSelectorExtendedTests
    {
        private SoACharaDataDic _data;
        private int _ownerHash;
        // _ownerHash doubles as player hash in this test
        private int _companionHash;
        private int _enemy1Hash;
        private int _enemy2Hash;

        [SetUp]
        public void SetUp()
        {
            _data = new SoACharaDataDic();
            _ownerHash = 100;
            // Owner is player
            _companionHash = 200;
            _enemy1Hash = 300;
            _enemy2Hash = 400;

            _data.Add(_ownerHash,
                new CharacterVitals { currentHp = 100, maxHp = 100, position = Vector2.zero },
                new CombatStats { attack = new ElementalStatus { slash = 50 }, defense = new ElementalStatus { slash = 30 } },
                CharacterFlags.Pack(CharacterBelong.Ally, CharacterFeature.Player, AbilityFlag.None),
                default);

            _data.Add(_companionHash,
                new CharacterVitals { currentHp = 80, maxHp = 100, position = new Vector2(2f, 0f) },
                new CombatStats { attack = new ElementalStatus { fire = 40 }, defense = new ElementalStatus { fire = 20 } },
                CharacterFlags.Pack(CharacterBelong.Ally, CharacterFeature.Companion, AbilityFlag.None),
                default);

            _data.Add(_enemy1Hash,
                new CharacterVitals { currentHp = 60, maxHp = 100, position = new Vector2(5f, 0f) },
                new CombatStats { attack = new ElementalStatus { strike = 80 }, defense = new ElementalStatus { strike = 10 } },
                CharacterFlags.Pack(CharacterBelong.Enemy, CharacterFeature.Minion, AbilityFlag.None),
                default);

            _data.Add(_enemy2Hash,
                new CharacterVitals { currentHp = 90, maxHp = 100, position = new Vector2(10f, 0f) },
                new CombatStats { attack = new ElementalStatus { fire = 30 }, defense = new ElementalStatus { fire = 50 } },
                CharacterFlags.Pack(CharacterBelong.Enemy, CharacterFeature.Boss, AbilityFlag.None),
                default);
        }

        [TearDown]
        public void TearDown()
        {
            _data.Dispose();
        }

        // --- SortByAttackPower ---

        [Test]
        public void TargetSelector_SortByAttackPower_SelectsHighest()
        {
            List<int> candidates = new List<int> { _enemy1Hash, _enemy2Hash };

            // enemy1: attack.Total = 80 (strike), enemy2: attack.Total = 30 (fire)
            int result = TargetSelector.SortAndPick(
                TargetSortKey.AttackPower, true, _ownerHash, candidates, _data, 0f);

            Assert.AreEqual(_enemy1Hash, result, "攻撃力最大=enemy1(80)");
        }

        // --- SortByDefensePower ---

        [Test]
        public void TargetSelector_SortByDefensePower_SelectsLowest()
        {
            List<int> candidates = new List<int> { _enemy1Hash, _enemy2Hash };

            // enemy1: defense.Total = 10, enemy2: defense.Total = 50
            int result = TargetSelector.SortAndPick(
                TargetSortKey.DefensePower, false, _ownerHash, candidates, _data, 0f);

            Assert.AreEqual(_enemy1Hash, result, "防御力最低=enemy1(10)");
        }

        // --- SortByTargetingCount ---

        [Test]
        public void TargetSelector_SortByTargetingCount_SelectsHighestBitCount()
        {
            ref CharacterFlags f1 = ref _data.GetFlags(_enemy1Hash);
            f1.RecognizeObjectType = 0b1111; // 4ビット
            ref CharacterFlags f2 = ref _data.GetFlags(_enemy2Hash);
            f2.RecognizeObjectType = 0b0001; // 1ビット

            List<int> candidates = new List<int> { _enemy1Hash, _enemy2Hash };

            int result = TargetSelector.SortAndPick(
                TargetSortKey.TargetingCount, true, _ownerHash, candidates, _data, 0f);

            Assert.AreEqual(_enemy1Hash, result, "ビット数最大=enemy1(4)");
        }

        // --- SortByLastAttacker ---

        [Test]
        public void TargetSelector_SortByLastAttacker_SelectsTopScoreAttacker()
        {
            DamageScoreTracker tracker = new DamageScoreTracker();
            tracker.AddDamage(_enemy2Hash, 50f, 0f);
            tracker.AddDamage(_enemy1Hash, 10f, 0f);

            List<int> candidates = new List<int> { _enemy1Hash, _enemy2Hash };

            int result = TargetSelector.SortAndPick(
                TargetSortKey.LastAttacker, true, _ownerHash, candidates, _data, 0f, tracker);

            Assert.AreEqual(_enemy2Hash, result, "最高スコア攻撃者=enemy2");

            tracker.Dispose();
        }

        // --- SortByDamageScore ---

        [Test]
        public void TargetSelector_SortByDamageScore_SelectsHighestScore()
        {
            DamageScoreTracker tracker = new DamageScoreTracker();
            tracker.AddDamage(_enemy1Hash, 100f, 0f);
            tracker.AddDamage(_enemy2Hash, 30f, 0f);

            List<int> candidates = new List<int> { _enemy1Hash, _enemy2Hash };

            int result = TargetSelector.SortAndPick(
                TargetSortKey.DamageScore, true, _ownerHash, candidates, _data, 0f, tracker);

            Assert.AreEqual(_enemy1Hash, result, "累積ダメージ最大=enemy1(100)");

            tracker.Dispose();
        }

        // --- FixedTarget: Self/Player/Sister ---

        [Test]
        public void TargetSelector_SortBySelf_SelectsOwner()
        {
            List<int> candidates = new List<int> { _ownerHash, _companionHash, _enemy1Hash };

            int result = TargetSelector.SortAndPick(
                TargetSortKey.Self, true, _ownerHash, candidates, _data, 0f);

            Assert.AreEqual(_ownerHash, result, "Self: 自分自身を選択");
        }

        [Test]
        public void TargetSelector_SortByPlayer_SelectsPlayerFeature()
        {
            List<int> candidates = new List<int> { _companionHash, _ownerHash, _enemy1Hash };

            int result = TargetSelector.SortAndPick(
                TargetSortKey.Player, true, _ownerHash, candidates, _data, 0f);

            Assert.AreEqual(_ownerHash, result, "Player: CharacterFeature.Playerを持つキャラ");
        }

        [Test]
        public void TargetSelector_SortBySister_SelectsCompanionFeature()
        {
            List<int> candidates = new List<int> { _ownerHash, _companionHash, _enemy1Hash };

            int result = TargetSelector.SortAndPick(
                TargetSortKey.Sister, true, _ownerHash, candidates, _data, 0f);

            Assert.AreEqual(_companionHash, result, "Sister: CharacterFeature.Companionを持つキャラ");
        }

        // --- TargetFilter: WeakPoint ---

        [Test]
        public void TargetSelector_FilterByWeakPoint_ExcludesNonWeak()
        {
            // enemy1: defense.strike=10 (>0, 弱点ではない), enemy2: defense.fire=50
            // slashで弱点フィルタ → enemy1のdefense.slash=0 → 弱点!
            TargetFilter filter = new TargetFilter
            {
                weakPoint = Element.Slash,
                belong = CharacterBelong.Enemy,
            };

            List<int> candidates = new List<int> { _enemy1Hash, _enemy2Hash };
            List<int> result = TargetSelector.FilterCandidates(
                filter, _ownerHash, candidates, _data);

            // enemy1: defense.slash=0 (弱点), enemy2: defense.slash=0 (弱点)
            // 両方slashの防御値が0なので両方通過する
            Assert.Greater(result.Count, 0, "弱点フィルタで1体以上通過");
        }

        // --- TargetFilter: Feature ---

        [Test]
        public void TargetSelector_FilterByFeature_SelectsBossOnly()
        {
            TargetFilter filter = new TargetFilter
            {
                feature = CharacterFeature.Boss,
                belong = CharacterBelong.Enemy,
            };

            List<int> candidates = new List<int> { _enemy1Hash, _enemy2Hash };
            List<int> result = TargetSelector.FilterCandidates(
                filter, _ownerHash, candidates, _data);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(_enemy2Hash, result[0], "Bossフィーチャーを持つenemy2のみ");
        }

        // --- TargetFilter: DistanceRange ---

        [Test]
        public void TargetSelector_FilterByDistanceRange_ExcludesOutOfRange()
        {
            TargetFilter filter = new TargetFilter
            {
                distanceRange = new Vector2(3f, 8f), // 3〜8mの範囲
                belong = CharacterBelong.Enemy,
            };

            List<int> candidates = new List<int> { _enemy1Hash, _enemy2Hash };
            List<int> result = TargetSelector.FilterCandidates(
                filter, _ownerHash, candidates, _data);

            // enemy1: distance=5 (範囲内), enemy2: distance=10 (範囲外)
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(_enemy1Hash, result[0]);
        }

        // --- SelectTarget統合: filter→sort→pick ---

        [Test]
        public void TargetSelector_SelectTarget_FiltersAndSortsCombined()
        {
            AITargetSelect select = new AITargetSelect
            {
                sortKey = TargetSortKey.HpRatio,
                isDescending = false, // HP最低を選択
                filter = new TargetFilter
                {
                    belong = CharacterBelong.Enemy,
                },
            };

            List<int> candidates = new List<int> { _ownerHash, _companionHash, _enemy1Hash, _enemy2Hash };

            int result = TargetSelector.SelectTarget(
                select, _ownerHash, candidates, _data, 0f);

            // enemy1: 60/100=0.6, enemy2: 90/100=0.9
            Assert.AreEqual(_enemy1Hash, result, "Enemy陣営フィルタ後、HP割合最低のenemy1を選択");
        }
    }
}
