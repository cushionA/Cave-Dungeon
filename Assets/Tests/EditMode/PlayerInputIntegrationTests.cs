using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// ChargeInputHandlerとInputConverterの結合テスト。
    /// チャージ入力→InputConverter変換→MovementInfo反映の経路を検証する。
    /// </summary>
    public class PlayerInputIntegrationTests
    {
        private ChargeAttackLogic _chargeLogic;
        private ChargeInputHandler _chargeHandler;

        [SetUp]
        public void SetUp()
        {
            _chargeLogic = new ChargeAttackLogic();
            _chargeHandler = new ChargeInputHandler(_chargeLogic);
        }

        [Test]
        public void ChargeToInputConverter_ShortPressLight_ReturnsLightAttack()
        {
            // Arrange — 短押し (Light)
            _chargeHandler.BeginHold(0);
            _chargeHandler.UpdateHold(0.2f);
            _chargeHandler.EndHold(0);

            // Act — InputConverterで変換
            AttackInputType? result = InputConverter.ConvertAttackInput(
                _chargeHandler.AttackButtonId, false, _chargeHandler.IsCharging);

            // Assert
            Assert.AreEqual(AttackInputType.LightAttack, result);
            Assert.AreEqual(1.0f, _chargeHandler.ChargeMultiplier);
        }

        [Test]
        public void ChargeToInputConverter_LongPressLight_ReturnsChargeLight()
        {
            // Arrange — 長押し (Light, Level1)
            _chargeHandler.BeginHold(0);
            _chargeHandler.UpdateHold(0.6f);
            _chargeHandler.EndHold(0);

            // Act
            AttackInputType? result = InputConverter.ConvertAttackInput(
                _chargeHandler.AttackButtonId, false, _chargeHandler.IsCharging);

            // Assert
            Assert.AreEqual(AttackInputType.ChargeLight, result);
            Assert.AreEqual(ChargeAttackLogic.k_ChargeLevel1Multiplier, _chargeHandler.ChargeMultiplier);
        }

        [Test]
        public void ChargeToInputConverter_LongPressHeavy_ReturnsChargeHeavy()
        {
            // Arrange — 長押し (Heavy, Level2)
            _chargeHandler.BeginHold(1);
            _chargeHandler.UpdateHold(1.6f);
            _chargeHandler.EndHold(1);

            // Act
            AttackInputType? result = InputConverter.ConvertAttackInput(
                _chargeHandler.AttackButtonId, false, _chargeHandler.IsCharging);

            // Assert
            Assert.AreEqual(AttackInputType.ChargeHeavy, result);
            Assert.AreEqual(ChargeAttackLogic.k_ChargeLevel2Multiplier, _chargeHandler.ChargeMultiplier);
        }

        [Test]
        public void ChargeToInputConverter_AirborneOverridesCharge()
        {
            // Arrange — 空中でチャージ（空中判定が優先される）
            _chargeHandler.BeginHold(0);
            _chargeHandler.UpdateHold(0.6f);
            _chargeHandler.EndHold(0);

            // Act — isAirborne=true
            AttackInputType? result = InputConverter.ConvertAttackInput(
                _chargeHandler.AttackButtonId, true, _chargeHandler.IsCharging);

            // Assert — 空中攻撃が優先
            Assert.AreEqual(AttackInputType.AerialLight, result);
        }

        [Test]
        public void ChargeToInputConverter_SkillButton_ReturnsSkill()
        {
            // Arrange — Skill入力（buttonId=2）
            _chargeHandler.BeginHold(2);
            _chargeHandler.EndHold(2);

            // Act
            AttackInputType? result = InputConverter.ConvertAttackInput(
                _chargeHandler.AttackButtonId, false, _chargeHandler.IsCharging);

            // Assert — Skillはチャージ無関係
            Assert.AreEqual(AttackInputType.Skill, result);
        }

        [Test]
        public void ChargeMultiplier_AppliedToMotionValue_CalculatesCorrectly()
        {
            // Arrange — Level1チャージ
            _chargeHandler.BeginHold(0);
            _chargeHandler.UpdateHold(0.6f);
            _chargeHandler.EndHold(0);

            float chargeMultiplier = _chargeHandler.ChargeMultiplier;
            float baseMotionValue = 1.0f;

            // Act — PlayerCharacter.HandleAttack相当の計算
            float effectiveMultiplier = chargeMultiplier > 0f ? chargeMultiplier : 1f;
            float finalMotionValue = baseMotionValue * effectiveMultiplier;

            // Assert
            Assert.AreEqual(1.5f, finalMotionValue);
        }

        [Test]
        public void MovementInfo_ChargeMultiplier_DefaultsToZero()
        {
            // Arrange
            MovementInfo info = default;

            // Assert — デフォルト値は0（PlayerCharacterで1.0にフォールバック）
            Assert.AreEqual(0f, info.chargeMultiplier);
        }

        [Test]
        public void GuardCancelsCharge_NoAttackGenerated()
        {
            // Arrange — チャージ中にガード入力（キャンセル）
            _chargeHandler.BeginHold(0);
            _chargeHandler.UpdateHold(0.8f);

            // Act — ガードでキャンセル（PlayerInputHandler.OnGuardが呼ぶ）
            _chargeHandler.CancelCharge();

            // Assert
            Assert.IsFalse(_chargeHandler.HasAttackInput);
            Assert.IsFalse(_chargeHandler.IsHolding);
            Assert.AreEqual(1.0f, _chargeHandler.ChargeMultiplier);
        }
    }
}
