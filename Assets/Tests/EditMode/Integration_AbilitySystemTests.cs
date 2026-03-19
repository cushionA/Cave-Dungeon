using NUnit.Framework;
using R3;
using Game.Core;

namespace Game.Tests.EditMode
{
    [TestFixture]
    public class Integration_AbilitySystemTests
    {
        private SoACharaDataDic _data;
        private GameEvents _events;

        private const int k_PlayerHash = 1;

        [SetUp]
        public void SetUp()
        {
            _data = new SoACharaDataDic(4);
            _events = new GameEvents();

            CharacterVitals vitals = new CharacterVitals { currentHp = 100, maxHp = 100, level = 5 };
            CharacterFlags flags = CharacterFlags.Pack(
                CharacterBelong.Ally, CharacterFeature.Player, AbilityFlag.None);
            EquipmentStatus equip = new EquipmentStatus { activeFlags = AbilityFlag.None };

            _data.Add(k_PlayerHash, vitals, default, flags, default, equip);
        }

        [TearDown]
        public void TearDown()
        {
            _events.Dispose();
            _data.Dispose();
        }

        [Test]
        public void AbilitySystem_EquipmentGivesAbility_FlagsUpdate()
        {
            // Arrange: 装備にWallKick|DoubleJumpを設定
            ref EquipmentStatus equip = ref _data.GetEquipmentStatus(k_PlayerHash);
            equip.activeFlags = AbilityFlag.WallKick | AbilityFlag.DoubleJump;

            // Act: 装備のアビリティフラグをキャラクターフラグにコピー
            ref CharacterFlags flags = ref _data.GetFlags(k_PlayerHash);
            flags.AbilityFlags = equip.activeFlags;

            _events.FireAbilityFlagsChanged(k_PlayerHash, flags.AbilityFlags);

            // Assert
            CharacterFlags stored = _data.GetFlags(k_PlayerHash);
            Assert.AreEqual(AbilityFlag.WallKick | AbilityFlag.DoubleJump, stored.AbilityFlags);
        }

        [Test]
        public void AbilitySystem_AbilityChange_UnlocksGate()
        {
            // Arrange: WallKick必須のAbilityゲート
            GateDefinition gate = new GateDefinition
            {
                gateId = "gate_wallkick_01",
                gateType = GateType.Ability,
                requiredAbility = AbilityFlag.WallKick
            };

            // Act: 装備前（AbilityFlag.None）→ゲート不通過
            CharacterFlags beforeFlags = _data.GetFlags(k_PlayerHash);
            bool beforeResult = GateConditionChecker.Evaluate(gate, beforeFlags, id => false, flag => false);

            // 装備後（WallKick取得）→ゲート通過
            ref CharacterFlags flagsRef = ref _data.GetFlags(k_PlayerHash);
            flagsRef.AbilityFlags = AbilityFlag.WallKick;

            CharacterFlags afterFlags = _data.GetFlags(k_PlayerHash);
            bool afterResult = GateConditionChecker.Evaluate(gate, afterFlags, id => false, flag => false);

            // Assert
            Assert.IsFalse(beforeResult);
            Assert.IsTrue(afterResult);
        }

        [Test]
        public void AbilitySystem_RemoveEquipment_LosesAbility()
        {
            // Arrange: WallKickを持つ状態にする
            ref CharacterFlags flags = ref _data.GetFlags(k_PlayerHash);
            flags.AbilityFlags = AbilityFlag.WallKick;
            ref EquipmentStatus equip = ref _data.GetEquipmentStatus(k_PlayerHash);
            equip.activeFlags = AbilityFlag.WallKick;

            GateDefinition gate = new GateDefinition
            {
                gateId = "gate_wallkick_01",
                gateType = GateType.Ability,
                requiredAbility = AbilityFlag.WallKick
            };

            // 装備あり→通過可能
            Assert.IsTrue(GateConditionChecker.Evaluate(gate, _data.GetFlags(k_PlayerHash), id => false, flag => false));

            // Act: 装備解除→アビリティ喪失
            ref EquipmentStatus equipRef = ref _data.GetEquipmentStatus(k_PlayerHash);
            equipRef.activeFlags = AbilityFlag.None;
            ref CharacterFlags flagsRef = ref _data.GetFlags(k_PlayerHash);
            flagsRef.AbilityFlags = equipRef.activeFlags;

            // Assert: ゲート不通過
            Assert.AreEqual(AbilityFlag.None, _data.GetFlags(k_PlayerHash).AbilityFlags);
            Assert.IsFalse(GateConditionChecker.Evaluate(gate, _data.GetFlags(k_PlayerHash), id => false, flag => false));
        }

        [Test]
        public void AbilitySystem_AbilityFlagsChangedEvent_FiresWithCorrectData()
        {
            // Arrange
            int receivedHash = 0;
            AbilityFlag receivedFlags = AbilityFlag.None;
            _events.OnAbilityFlagsChanged.Subscribe(e =>
            {
                receivedHash = e.ownerHash;
                receivedFlags = e.newFlags;
            });

            // Act
            _events.FireAbilityFlagsChanged(k_PlayerHash, AbilityFlag.AirDash | AbilityFlag.Swim);

            // Assert
            Assert.AreEqual(k_PlayerHash, receivedHash);
            Assert.AreEqual(AbilityFlag.AirDash | AbilityFlag.Swim, receivedFlags);
        }
    }
}
