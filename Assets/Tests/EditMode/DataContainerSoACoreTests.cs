using NUnit.Framework;
using Game.Core;
using UnityEngine;

namespace Game.Tests.EditMode
{
    [TestFixture]
    public class DataContainerSoACoreTests
    {
        [Test]
        public void SoACharaDataDic_Add_IncreasesCountAndStoresData()
        {
            // Arrange
            SoACharaDataDic dic = new SoACharaDataDic(4);
            int hash = 100;

            CharacterVitals vitals = new CharacterVitals
            {
                currentHp = 80,
                maxHp = 100,
                currentMp = 30,
                maxMp = 50,
                currentStamina = 60f,
                maxStamina = 100f,
                currentArmor = 10f,
                maxArmor = 20f,
                position = new Vector2(1f, 2f),
                level = 5
            };

            CombatStats combat = new CombatStats
            {
                attack = new ElementalStatus { slash = 25, fire = 10 },
                defense = new ElementalStatus { slash = 15, fire = 8 },
                criticalRate = 0.1f,
                criticalMultiplier = 1.5f,
                knockbackResistance = 0.4f
            };

            CharacterFlags flags = CharacterFlags.Pack(
                CharacterBelong.Ally,
                CharacterFeature.Player,
                AbilityFlag.DoubleJump
            );

            MoveParams move = new MoveParams
            {
                moveSpeed = 5f,
                jumpForce = 10f,
                dashSpeed = 8f,
                dashDuration = 0.2f,
                gravityScale = 1f,
                weightRatio = 1f
            };

            // Act
            int index = dic.Add(hash, vitals, combat, flags, move);

            // Assert
            Assert.AreEqual(1, dic.Count);
            Assert.AreEqual(0, index);

            CharacterVitals storedVitals = dic.GetVitals(hash);
            Assert.AreEqual(80, storedVitals.currentHp);
            Assert.AreEqual(100, storedVitals.maxHp);
            Assert.AreEqual(5, storedVitals.level);
            Assert.AreEqual(new Vector2(1f, 2f), storedVitals.position);

            CombatStats storedCombat = dic.GetCombatStats(hash);
            Assert.AreEqual(25, storedCombat.attack.slash);
            Assert.AreEqual(10, storedCombat.attack.fire);
            Assert.AreEqual(0.1f, storedCombat.criticalRate, 0.001f);
            Assert.AreEqual(0.4f, storedCombat.knockbackResistance, 0.001f);

            CharacterFlags storedFlags = dic.GetFlags(hash);
            Assert.AreEqual(CharacterBelong.Ally, storedFlags.Belong);
            Assert.AreEqual(CharacterFeature.Player, storedFlags.Feature);
            Assert.AreEqual(AbilityFlag.DoubleJump, storedFlags.AbilityFlags);

            MoveParams storedMove = dic.GetMoveParams(hash);
            Assert.AreEqual(5f, storedMove.moveSpeed, 0.001f);
            Assert.AreEqual(10f, storedMove.jumpForce, 0.001f);

            dic.Dispose();
        }

        [Test]
        public void SoACharaDataDic_Remove_SwapBackAndDecreasesCount()
        {
            // Arrange
            SoACharaDataDic dic = new SoACharaDataDic(4);

            int hashA = 10;
            int hashB = 20;
            int hashC = 30;

            CharacterVitals vitalsA = new CharacterVitals { currentHp = 100, level = 1 };
            CharacterVitals vitalsB = new CharacterVitals { currentHp = 200, level = 2 };
            CharacterVitals vitalsC = new CharacterVitals { currentHp = 300, level = 3 };

            CombatStats combatDefault = default;
            CharacterFlags flagsDefault = default;
            MoveParams moveDefault = default;

            dic.Add(hashA, vitalsA, combatDefault, flagsDefault, moveDefault);
            dic.Add(hashB, vitalsB, combatDefault, flagsDefault, moveDefault);
            dic.Add(hashC, vitalsC, combatDefault, flagsDefault, moveDefault);

            Assert.AreEqual(3, dic.Count);

            // Act: Remove the first element (hashA at index 0)
            // hashC (last) should be swapped into index 0
            dic.Remove(hashA);

            // Assert
            Assert.AreEqual(2, dic.Count);

            // hashA should no longer be accessible
            bool foundA = dic.TryGetValue(hashA, out int _);
            Assert.IsFalse(foundA);

            // hashB should still be at its original data
            CharacterVitals storedB = dic.GetVitals(hashB);
            Assert.AreEqual(200, storedB.currentHp);
            Assert.AreEqual(2, storedB.level);

            // hashC should have been swapped to index 0 and still accessible
            CharacterVitals storedC = dic.GetVitals(hashC);
            Assert.AreEqual(300, storedC.currentHp);
            Assert.AreEqual(3, storedC.level);

            // Verify hashC is now at index 0 (swapped from index 2)
            bool foundC = dic.TryGetValue(hashC, out int indexC);
            Assert.IsTrue(foundC);
            Assert.AreEqual(0, indexC);

            dic.Dispose();
        }

        [Test]
        public void SoACharaDataDic_GetVitals_ReturnsRefToStoredData()
        {
            // Arrange
            SoACharaDataDic dic = new SoACharaDataDic(4);
            int hash = 42;

            CharacterVitals vitals = new CharacterVitals
            {
                currentHp = 100,
                maxHp = 100
            };

            dic.Add(hash, vitals, default, default, default);

            // Act: modify via ref return
            ref CharacterVitals vitalsRef = ref dic.GetVitals(hash);
            vitalsRef.currentHp = 50;
            vitalsRef.position = new Vector2(99f, 88f);

            // Assert: read again and confirm the mutation persisted
            CharacterVitals updated = dic.GetVitals(hash);
            Assert.AreEqual(50, updated.currentHp);
            Assert.AreEqual(new Vector2(99f, 88f), updated.position);

            dic.Dispose();
        }

        [Test]
        public void SoACharaDataDic_TryGetValue_ReturnsFalseForUnknownHash()
        {
            // Arrange
            SoACharaDataDic dic = new SoACharaDataDic(4);
            dic.Add(1, default, default, default, default);

            // Act
            bool found = dic.TryGetValue(9999, out int index);

            // Assert
            Assert.IsFalse(found);
            // 自動生成 TryGetIndexByHash は未登録時 index=-1 を返す
            Assert.AreEqual(-1, index);

            dic.Dispose();
        }

        [Test]
        public void SoACharaDataDic_Dispose_BlocksSubsequentAccess()
        {
            // Arrange
            SoACharaDataDic dic = new SoACharaDataDic(4);
            dic.Add(1, default, default, default, default);
            dic.Add(2, default, default, default, default);
            Assert.AreEqual(2, dic.Count);

            // Act
            Assert.DoesNotThrow(() => dic.Dispose());

            // Assert: 二重 Dispose は許容、Compat 経由のアクセスは ObjectDisposedException
            Assert.DoesNotThrow(() => dic.Dispose());
            Assert.Throws<System.ObjectDisposedException>(() =>
            {
                dic.TryGetValue(1, out int _);
            });
        }

        [Test]
        public void SoACharaDataDic_EquipmentStatus_StoresAndRetrieves()
        {
            SoACharaDataDic dic = new SoACharaDataDic(4);
            int hash = 1;

            EquipmentStatus equip = new EquipmentStatus
            {
                weaponId = 5,
                gripMode = GripMode.TwoHanded,
                weightRatio = 0.8f
            };

            dic.Add(hash, default, default, default, default, equip);

            ref EquipmentStatus stored = ref dic.GetEquipmentStatus(hash);
            Assert.AreEqual(5, stored.weaponId);
            Assert.AreEqual(GripMode.TwoHanded, stored.gripMode);
            Assert.AreEqual(0.8f, stored.weightRatio, 0.001f);

            dic.Dispose();
        }

        [Test]
        public void CharacterFlags_ActState_PacksAndRetrievesCorrectly()
        {
            CharacterFlags flags = CharacterFlags.Pack(
                CharacterBelong.Enemy,
                CharacterFeature.Boss,
                ActState.Attacking,
                AbilityFlag.AirDash
            );

            Assert.AreEqual(CharacterBelong.Enemy, flags.Belong);
            Assert.AreEqual(CharacterFeature.Boss, flags.Feature);
            Assert.AreEqual(ActState.Attacking, flags.ActState);
            Assert.AreEqual(AbilityFlag.AirDash, flags.AbilityFlags);
        }
    }
}
