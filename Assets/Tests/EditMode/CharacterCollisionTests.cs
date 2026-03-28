using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class CharacterCollisionTests
    {
        // --- ShouldBlockMovement: contactTypeに基づく衝突判定 ---

        [Test]
        public void CharacterCollision_PassThrough_ShouldNotBlock()
        {
            bool result = CharacterCollisionLogic.ShouldBlockMovement(AttackContactType.PassThrough);

            Assert.IsFalse(result);
        }

        [Test]
        public void CharacterCollision_StopOnHit_ShouldBlock()
        {
            bool result = CharacterCollisionLogic.ShouldBlockMovement(AttackContactType.StopOnHit);

            Assert.IsTrue(result);
        }

        [Test]
        public void CharacterCollision_Carry_ShouldBlock()
        {
            // Carryモードでは接触検出のために衝突を有効にする
            bool result = CharacterCollisionLogic.ShouldBlockMovement(AttackContactType.Carry);

            Assert.IsTrue(result);
        }

        // --- CanCarry: 運搬可能な状態判定 ---

        [Test]
        public void CanCarry_WhenTargetFlinching_ShouldReturnTrue()
        {
            bool result = CharacterCollisionLogic.CanCarry(
                ActState.Flinch, CharacterBelong.Enemy);

            Assert.IsTrue(result);
        }

        [Test]
        public void CanCarry_WhenTargetStunned_ShouldReturnTrue()
        {
            bool result = CharacterCollisionLogic.CanCarry(
                ActState.Stunned, CharacterBelong.Enemy);

            Assert.IsTrue(result);
        }

        [Test]
        public void CanCarry_WhenTargetGuardBroken_ShouldReturnTrue()
        {
            bool result = CharacterCollisionLogic.CanCarry(
                ActState.GuardBroken, CharacterBelong.Enemy);

            Assert.IsTrue(result);
        }

        [Test]
        public void CanCarry_WhenTargetKnockbacked_ShouldReturnTrue()
        {
            bool result = CharacterCollisionLogic.CanCarry(
                ActState.Knockbacked, CharacterBelong.Enemy);

            Assert.IsTrue(result);
        }

        [Test]
        public void CanCarry_WhenTargetNeutral_ShouldReturnFalse()
        {
            bool result = CharacterCollisionLogic.CanCarry(
                ActState.Neutral, CharacterBelong.Enemy);

            Assert.IsFalse(result);
        }

        [Test]
        public void CanCarry_WhenTargetAttacking_ShouldReturnFalse()
        {
            bool result = CharacterCollisionLogic.CanCarry(
                ActState.Attacking, CharacterBelong.Enemy);

            Assert.IsFalse(result);
        }

        [Test]
        public void CanCarry_WhenTargetRunning_ShouldReturnFalse()
        {
            bool result = CharacterCollisionLogic.CanCarry(
                ActState.Running, CharacterBelong.Enemy);

            Assert.IsFalse(result);
        }

        [Test]
        public void CanCarry_WhenTargetDead_ShouldReturnFalse()
        {
            bool result = CharacterCollisionLogic.CanCarry(
                ActState.Dead, CharacterBelong.Enemy);

            Assert.IsFalse(result);
        }

        [Test]
        public void CanCarry_WhenTargetIsAlly_ShouldReturnFalse()
        {
            // 味方は運搬不可
            bool result = CharacterCollisionLogic.CanCarry(
                ActState.Flinch, CharacterBelong.Ally);

            Assert.IsFalse(result);
        }

        [Test]
        public void CanCarry_WhenTargetIsNeutral_ShouldReturnFalse()
        {
            bool result = CharacterCollisionLogic.CanCarry(
                ActState.Stunned, CharacterBelong.Neutral);

            Assert.IsFalse(result);
        }

        // --- CarryState: 運搬状態の追跡 ---

        [Test]
        public void CarryState_Default_ShouldBeInactive()
        {
            CarryState state = default;

            Assert.IsFalse(state.IsActive);
            Assert.AreEqual(0, state.CarrierHash);
            Assert.AreEqual(0, state.CarriedHash);
        }

        [Test]
        public void CarryState_WhenStarted_ShouldTrackCarrierAndTarget()
        {
            CarryState state = CarryState.Start(100, 200);

            Assert.IsTrue(state.IsActive);
            Assert.AreEqual(100, state.CarrierHash);
            Assert.AreEqual(200, state.CarriedHash);
        }

        [Test]
        public void CarryState_WhenReleased_ShouldClear()
        {
            CarryState state = CarryState.Start(100, 200);
            state = CarryState.Release();

            Assert.IsFalse(state.IsActive);
            Assert.AreEqual(0, state.CarrierHash);
            Assert.AreEqual(0, state.CarriedHash);
        }

        // --- GetCollisionMode: 現在の衝突モードを取得 ---

        [Test]
        public void GetCollisionMode_WhenNoAction_ShouldReturnPassThrough()
        {
            AttackContactType result = CharacterCollisionLogic.GetCollisionMode(
                isActionActive: false, AttackContactType.StopOnHit);

            Assert.AreEqual(AttackContactType.PassThrough, result);
        }

        [Test]
        public void GetCollisionMode_WhenActionActive_ShouldReturnContactType()
        {
            AttackContactType result = CharacterCollisionLogic.GetCollisionMode(
                isActionActive: true, AttackContactType.StopOnHit);

            Assert.AreEqual(AttackContactType.StopOnHit, result);
        }

        [Test]
        public void GetCollisionMode_WhenActionActiveWithCarry_ShouldReturnCarry()
        {
            AttackContactType result = CharacterCollisionLogic.GetCollisionMode(
                isActionActive: true, AttackContactType.Carry);

            Assert.AreEqual(AttackContactType.Carry, result);
        }
    }
}
