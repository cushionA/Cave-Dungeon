using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class AICore_TargetSelectorTests
    {
        private SoACharaDataDic _data;
        private int _ownerHash;
        private int _enemy1Hash;
        private int _enemy2Hash;
        private int _allyHash;

        [SetUp]
        public void SetUp()
        {
            _data = new SoACharaDataDic();
            _ownerHash = 1;
            _enemy1Hash = 10;
            _enemy2Hash = 20;
            _allyHash = 30;

            _data.Add(_ownerHash,
                new CharacterVitals { position = Vector2.zero, currentHp = 100, maxHp = 100 },
                default, CharacterFlags.Pack(CharacterBelong.Ally, 0, 0), default);
            _data.Add(_enemy1Hash,
                new CharacterVitals { position = new Vector2(3f, 0f), currentHp = 50, maxHp = 100 },
                default, CharacterFlags.Pack(CharacterBelong.Enemy, 0, 0), default);
            _data.Add(_enemy2Hash,
                new CharacterVitals { position = new Vector2(8f, 0f), currentHp = 80, maxHp = 100 },
                default, CharacterFlags.Pack(CharacterBelong.Enemy, 0, 0), default);
            _data.Add(_allyHash,
                new CharacterVitals { position = new Vector2(1f, 0f), currentHp = 60, maxHp = 100 },
                default, CharacterFlags.Pack(CharacterBelong.Ally, 0, 0), default);
        }

        [TearDown]
        public void TearDown()
        {
            _data.Dispose();
        }

        [Test]
        public void TargetSelector_SortByDistance_SelectsNearest()
        {
            AITargetSelect select = new AITargetSelect
            {
                sortKey = TargetSortKey.Distance,
                isDescending = false,
                filter = new TargetFilter { belong = CharacterBelong.Enemy, distanceRange = new Vector2(0f, 20f) }
            };
            List<int> candidates = new List<int> { _enemy1Hash, _enemy2Hash, _allyHash };

            int result = TargetSelector.SelectTarget(select, _ownerHash, candidates, _data, 0f);

            Assert.AreEqual(_enemy1Hash, result);
        }

        [Test]
        public void TargetSelector_SortByHpRatio_SelectsLowest()
        {
            AITargetSelect select = new AITargetSelect
            {
                sortKey = TargetSortKey.HpRatio,
                isDescending = false,
                filter = new TargetFilter { belong = CharacterBelong.Enemy }
            };
            List<int> candidates = new List<int> { _enemy1Hash, _enemy2Hash };

            int result = TargetSelector.SelectTarget(select, _ownerHash, candidates, _data, 0f);

            Assert.AreEqual(_enemy1Hash, result);
        }

        [Test]
        public void TargetSelector_FilterByBelong_ExcludesAllies()
        {
            TargetFilter filter = new TargetFilter { belong = CharacterBelong.Enemy };
            List<int> candidates = new List<int> { _enemy1Hash, _allyHash, _enemy2Hash };

            List<int> filtered = TargetSelector.FilterCandidates(filter, _ownerHash, candidates, _data);

            Assert.AreEqual(2, filtered.Count);
            Assert.IsFalse(filtered.Contains(_allyHash));
        }

        [Test]
        public void TargetSelector_FilterByDistance_ExcludesFar()
        {
            TargetFilter filter = new TargetFilter { belong = CharacterBelong.Enemy, distanceRange = new Vector2(0f, 5f) };
            List<int> candidates = new List<int> { _enemy1Hash, _enemy2Hash };

            List<int> filtered = TargetSelector.FilterCandidates(filter, _ownerHash, candidates, _data);

            Assert.AreEqual(1, filtered.Count);
            Assert.AreEqual(_enemy1Hash, filtered[0]);
        }

        [Test]
        public void TargetSelector_NoCandidates_ReturnsZero()
        {
            AITargetSelect select = new AITargetSelect
            {
                sortKey = TargetSortKey.Distance,
                isDescending = false,
                filter = new TargetFilter { belong = CharacterBelong.Enemy, distanceRange = new Vector2(0f, 1f) }
            };
            List<int> candidates = new List<int> { _enemy1Hash, _enemy2Hash };

            int result = TargetSelector.SelectTarget(select, _ownerHash, candidates, _data, 0f);

            Assert.AreEqual(0, result);
        }
    }
}
