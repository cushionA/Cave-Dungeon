using NUnit.Framework;
using R3;
using Game.Core;
using UnityEngine;

namespace Game.Tests.EditMode
{
    [TestFixture]
    public class Integration_DataContainerConsistencyTests
    {
        private SoACharaDataDic _data;
        private GameEvents _events;

        [SetUp]
        public void SetUp()
        {
            _data = new SoACharaDataDic(4);
            _events = new GameEvents();
        }

        [TearDown]
        public void TearDown()
        {
            _events.Dispose();
            _data.Dispose();
        }

        [Test]
        public void DataContainer_MultiCharUpdate_MaintainsIndependentState()
        {
            // Arrange: 3キャラ登録
            CharacterVitals v1 = new CharacterVitals { currentHp = 100, maxHp = 100, level = 1 };
            CharacterVitals v2 = new CharacterVitals { currentHp = 200, maxHp = 200, level = 2 };
            CharacterVitals v3 = new CharacterVitals { currentHp = 300, maxHp = 300, level = 3 };

            CombatStats c1 = new CombatStats { attack = new ElementalStatus { slash = 50 } };
            CombatStats c2 = new CombatStats { attack = new ElementalStatus { slash = 80 } };
            CombatStats c3 = new CombatStats { attack = new ElementalStatus { slash = 120 } };

            CharacterFlags f1 = CharacterFlags.Pack(CharacterBelong.Ally, CharacterFeature.Player, AbilityFlag.None);
            CharacterFlags f2 = CharacterFlags.Pack(CharacterBelong.Ally, CharacterFeature.Companion, AbilityFlag.None);
            CharacterFlags f3 = CharacterFlags.Pack(CharacterBelong.Enemy, CharacterFeature.Minion, AbilityFlag.None);

            _data.Add(1, v1, c1, f1, default);
            _data.Add(2, v2, c2, f2, default);
            _data.Add(3, v3, c3, f3, default);

            // Act: キャラ1のHP変更、キャラ2の攻撃力変更、キャラ3のActState変更
            ref CharacterVitals rv1 = ref _data.GetVitals(1);
            rv1.currentHp = 50;

            ref CombatStats rc2 = ref _data.GetCombatStats(2);
            rc2.attack.slash = 200;

            ref CharacterFlags rf3 = ref _data.GetFlags(3);
            rf3.ActState = ActState.Stunned;

            // Assert: 各キャラが独立した変更のみを保持
            Assert.AreEqual(50, _data.GetVitals(1).currentHp);
            Assert.AreEqual(200, _data.GetVitals(2).currentHp); // 変更なし
            Assert.AreEqual(300, _data.GetVitals(3).currentHp); // 変更なし

            Assert.AreEqual(50, _data.GetCombatStats(1).attack.slash); // 変更なし
            Assert.AreEqual(200, _data.GetCombatStats(2).attack.slash);
            Assert.AreEqual(120, _data.GetCombatStats(3).attack.slash); // 変更なし

            Assert.AreEqual(ActState.Neutral, _data.GetFlags(1).ActState); // 変更なし
            Assert.AreEqual(ActState.Neutral, _data.GetFlags(2).ActState); // 変更なし
            Assert.AreEqual(ActState.Stunned, _data.GetFlags(3).ActState);
        }

        [Test]
        public void DataContainer_RemoveWithSwapBack_OtherCharactersUnaffected()
        {
            // Arrange: 3キャラ登録
            CharacterVitals v1 = new CharacterVitals { currentHp = 100, level = 1 };
            CharacterVitals v2 = new CharacterVitals { currentHp = 200, level = 2 };
            CharacterVitals v3 = new CharacterVitals { currentHp = 300, level = 3 };

            CombatStats c1 = new CombatStats { criticalRate = 0.1f };
            CombatStats c2 = new CombatStats { criticalRate = 0.2f };
            CombatStats c3 = new CombatStats { criticalRate = 0.3f };

            _data.Add(1, v1, c1, default, default);
            _data.Add(2, v2, c2, default, default);
            _data.Add(3, v3, c3, default, default);

            // Act: 中間キャラ（hash=2）を削除 → hash=3がswap-backでslot 1に移動
            _data.Remove(2);

            // Assert
            Assert.AreEqual(2, _data.Count);

            // hash=1は変更なし
            Assert.AreEqual(100, _data.GetVitals(1).currentHp);
            Assert.AreEqual(0.1f, _data.GetCombatStats(1).criticalRate, 0.001f);

            // hash=3はデータ保持（slot移動しても値は維持）
            Assert.AreEqual(300, _data.GetVitals(3).currentHp);
            Assert.AreEqual(0.3f, _data.GetCombatStats(3).criticalRate, 0.001f);

            // hash=2はアクセス不可
            Assert.IsFalse(_data.TryGetValue(2, out int _));
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => _data.GetVitals(2));
        }

        [Test]
        public void DataContainer_GrowCapacity_PreservesAllData()
        {
            // Arrange: 初期容量2で作成し、5キャラ追加でGrowを強制
            SoACharaDataDic smallData = new SoACharaDataDic(2);

            for (int i = 1; i <= 5; i++)
            {
                CharacterVitals v = new CharacterVitals { currentHp = i * 100, maxHp = i * 100, level = i };
                CombatStats c = new CombatStats { attack = new ElementalStatus { slash = i * 10 } };
                CharacterFlags f = CharacterFlags.Pack(CharacterBelong.Ally, CharacterFeature.Player, AbilityFlag.None);
                smallData.Add(i, v, c, f, default);
            }

            // Assert: 全5キャラのデータが正しく保持
            Assert.AreEqual(5, smallData.Count);

            for (int i = 1; i <= 5; i++)
            {
                CharacterVitals stored = smallData.GetVitals(i);
                Assert.AreEqual(i * 100, stored.currentHp);
                Assert.AreEqual(i, stored.level);

                CombatStats storedCombat = smallData.GetCombatStats(i);
                Assert.AreEqual(i * 10, storedCombat.attack.slash);
            }

            smallData.Dispose();
        }

        [Test]
        public void DataContainer_EventsAndSoASync_ConsistentAfterRegistration()
        {
            // Arrange: イベントリスナーでキャラ登録を追跡
            int registeredHash = 0;
            _events.OnCharacterRegistered.Subscribe(hash => registeredHash = hash);

            // Act: SoAにキャラ追加 → イベント発火
            CharacterVitals vitals = new CharacterVitals
            {
                currentHp = 150,
                maxHp = 150,
                position = new Vector2(10f, 5f),
                level = 7
            };
            CombatStats combat = new CombatStats
            {
                attack = new ElementalStatus { slash = 80, fire = 40 },
                criticalRate = 0.25f
            };
            CharacterFlags flags = CharacterFlags.Pack(
                CharacterBelong.Enemy,
                CharacterFeature.Boss,
                AbilityFlag.AirDash
            );

            _data.Add(42, vitals, combat, flags, default);
            _events.FireCharacterRegistered(42);

            // Assert: イベントで受け取ったhashとSoAデータの整合性
            Assert.AreEqual(42, registeredHash);
            Assert.IsTrue(_data.TryGetValue(42, out int _));

            CharacterVitals stored = _data.GetVitals(42);
            Assert.AreEqual(150, stored.currentHp);
            Assert.AreEqual(new Vector2(10f, 5f), stored.position);

            CombatStats storedCombat = _data.GetCombatStats(42);
            Assert.AreEqual(80, storedCombat.attack.slash);
            Assert.AreEqual(40, storedCombat.attack.fire);

            CharacterFlags storedFlags = _data.GetFlags(42);
            Assert.AreEqual(CharacterBelong.Enemy, storedFlags.Belong);
            Assert.AreEqual(CharacterFeature.Boss, storedFlags.Feature);
            Assert.AreEqual(AbilityFlag.AirDash, storedFlags.AbilityFlags);
        }
    }
}
