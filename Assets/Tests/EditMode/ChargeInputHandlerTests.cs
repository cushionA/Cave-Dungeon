using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class ChargeInputHandlerTests
    {
        private ChargeAttackLogic _chargeLogic;
        private ChargeInputHandler _handler;

        [SetUp]
        public void SetUp()
        {
            _chargeLogic = new ChargeAttackLogic();
            _handler = new ChargeInputHandler(_chargeLogic);
        }

        [Test]
        public void ChargeInput_WhenShortPress_ReturnsNormalAttack()
        {
            // Arrange — 0.3秒ホールド（Level1閾値0.5秒未満）
            _handler.BeginHold(0);
            _handler.UpdateHold(0.3f);

            // Act
            _handler.EndHold(0);

            // Assert — 通常攻撃、倍率1.0
            Assert.IsTrue(_handler.HasAttackInput);
            Assert.AreEqual(0, _handler.AttackButtonId);
            Assert.IsFalse(_handler.IsCharging);
            Assert.AreEqual(1.0f, _handler.ChargeMultiplier);
        }

        [Test]
        public void ChargeInput_WhenHoldAboveLevel1_ReturnsChargeAttack()
        {
            // Arrange — 0.6秒ホールド（Level1閾値0.5秒以上）
            _handler.BeginHold(0);
            _handler.UpdateHold(0.6f);

            // Act
            _handler.EndHold(0);

            // Assert — チャージ攻撃、倍率1.5
            Assert.IsTrue(_handler.HasAttackInput);
            Assert.AreEqual(0, _handler.AttackButtonId);
            Assert.IsTrue(_handler.IsCharging);
            Assert.AreEqual(ChargeAttackLogic.k_ChargeLevel1Multiplier, _handler.ChargeMultiplier);
        }

        [Test]
        public void ChargeInput_WhenHoldAboveLevel2_ReturnsLevel2Multiplier()
        {
            // Arrange — 1.6秒ホールド（Level2閾値1.5秒以上）
            _handler.BeginHold(1);
            _handler.UpdateHold(1.6f);

            // Act
            _handler.EndHold(1);

            // Assert — チャージ攻撃（Heavy）、倍率2.5
            Assert.IsTrue(_handler.HasAttackInput);
            Assert.AreEqual(1, _handler.AttackButtonId);
            Assert.IsTrue(_handler.IsCharging);
            Assert.AreEqual(ChargeAttackLogic.k_ChargeLevel2Multiplier, _handler.ChargeMultiplier);
        }

        [Test]
        public void ChargeInput_WhenCancelled_ReturnsNoAttack()
        {
            // Arrange — ホールド中にキャンセル
            _handler.BeginHold(0);
            _handler.UpdateHold(0.8f);

            // Act
            _handler.CancelCharge();

            // Assert — 攻撃なし
            Assert.IsFalse(_handler.HasAttackInput);
            Assert.IsFalse(_handler.IsHolding);
            Assert.IsFalse(_handler.IsCharging);
            Assert.AreEqual(1.0f, _handler.ChargeMultiplier);
        }

        [Test]
        public void ChargeInput_WhenDifferentButtonReleased_IgnoresRelease()
        {
            // Arrange — ボタン0をホールド中
            _handler.BeginHold(0);
            _handler.UpdateHold(0.6f);

            // Act — ボタン1のリリース
            _handler.EndHold(1);

            // Assert — ホールドは継続、攻撃なし
            Assert.IsTrue(_handler.IsHolding);
            Assert.IsFalse(_handler.HasAttackInput);
        }

        [Test]
        public void ChargeInput_ConsumeAttack_ClearsAllFlags()
        {
            // Arrange — チャージ攻撃を発生させる
            _handler.BeginHold(0);
            _handler.UpdateHold(0.6f);
            _handler.EndHold(0);

            // Act
            _handler.ConsumeAttack();

            // Assert — 全フラグクリア
            Assert.IsFalse(_handler.HasAttackInput);
            Assert.IsFalse(_handler.IsCharging);
            Assert.AreEqual(1.0f, _handler.ChargeMultiplier);
            Assert.AreEqual(-1, _handler.AttackButtonId);
        }

        [Test]
        public void ChargeInput_WhenHolding_ChargeLevelIncreases()
        {
            // Arrange & Act
            _handler.BeginHold(0);

            // Assert — 初期はレベル0
            Assert.AreEqual(0, _handler.ChargeLevel);

            // Act — 0.5秒経過
            _handler.UpdateHold(0.5f);

            // Assert — レベル1
            Assert.AreEqual(1, _handler.ChargeLevel);

            // Act — さらに1.0秒経過（合計1.5秒）
            _handler.UpdateHold(1.0f);

            // Assert — レベル2
            Assert.AreEqual(2, _handler.ChargeLevel);
        }

        [Test]
        public void ChargeInput_BeginHold_CancelsPreviousHold()
        {
            // Arrange — ボタン0でホールド中
            _handler.BeginHold(0);
            _handler.UpdateHold(0.6f);

            // Act — ボタン1で新たにホールド開始
            _handler.BeginHold(1);

            // Assert — 新しいホールドに切り替わり、チャージはリセット
            Assert.IsTrue(_handler.IsHolding);
            Assert.AreEqual(0, _handler.ChargeLevel);
        }
    }
}
