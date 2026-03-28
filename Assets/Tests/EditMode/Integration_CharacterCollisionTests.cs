using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// CharacterCollisionLogicの結合テスト。
    /// CharacterFlagsとの連携、状態シーケンス、境界値を検証する。
    /// </summary>
    public class Integration_CharacterCollisionTests
    {
        // --- CharacterFlags連携: CharacterFlagsから抽出した値でCanCarryが正しく動作するか ---

        [Test]
        public void CanCarry_WithPackedFlags_FlinchEnemy_ShouldReturnTrue()
        {
            CharacterFlags flags = CharacterFlags.Pack(
                CharacterBelong.Enemy, CharacterFeature.Minion,
                ActState.Flinch, AbilityFlag.None);

            bool result = CharacterCollisionLogic.CanCarry(flags.ActState, flags.Belong);

            Assert.IsTrue(result);
        }

        [Test]
        public void CanCarry_WithPackedFlags_StunnedEnemy_ShouldReturnTrue()
        {
            CharacterFlags flags = CharacterFlags.Pack(
                CharacterBelong.Enemy, CharacterFeature.Boss,
                ActState.Stunned, AbilityFlag.None);

            bool result = CharacterCollisionLogic.CanCarry(flags.ActState, flags.Belong);

            Assert.IsTrue(result);
        }

        [Test]
        public void CanCarry_WithPackedFlags_AttackingEnemy_ShouldReturnFalse()
        {
            CharacterFlags flags = CharacterFlags.Pack(
                CharacterBelong.Enemy, CharacterFeature.Minion,
                ActState.Attacking, AbilityFlag.None);

            bool result = CharacterCollisionLogic.CanCarry(flags.ActState, flags.Belong);

            Assert.IsFalse(result);
        }

        [Test]
        public void CanCarry_WithPackedFlags_FlinchAllyCompanion_ShouldReturnFalse()
        {
            // 味方はどの状態でも運搬不可
            CharacterFlags flags = CharacterFlags.Pack(
                CharacterBelong.Ally, CharacterFeature.Companion,
                ActState.Flinch, AbilityFlag.None);

            bool result = CharacterCollisionLogic.CanCarry(flags.ActState, flags.Belong);

            Assert.IsFalse(result);
        }

        // --- 状態シーケンス: CarryStateの連続操作 ---

        [Test]
        public void CarryState_StartThenStartAgain_ShouldOverwrite()
        {
            CarryState state = CarryState.Start(100, 200);
            state = CarryState.Start(100, 300);

            Assert.IsTrue(state.IsActive);
            Assert.AreEqual(300, state.CarriedHash);
        }

        [Test]
        public void CarryState_ReleaseThenRelease_ShouldRemainInactive()
        {
            CarryState state = CarryState.Start(100, 200);
            state = CarryState.Release();
            state = CarryState.Release();

            Assert.IsFalse(state.IsActive);
            Assert.AreEqual(0, state.CarrierHash);
        }

        [Test]
        public void CarryState_StartReleaseStart_ShouldBeActive()
        {
            CarryState state = CarryState.Start(100, 200);
            state = CarryState.Release();
            state = CarryState.Start(300, 400);

            Assert.IsTrue(state.IsActive);
            Assert.AreEqual(300, state.CarrierHash);
            Assert.AreEqual(400, state.CarriedHash);
        }

        // --- 衝突モード: 全ActState × AttackContactType境界 ---

        [Test]
        public void CanCarry_AllCarriableStates_ShouldOnlyAllowFourStates()
        {
            int carriableCount = 0;

            // ActState全値を網羅して、運搬可能な状態が正確に4つであることを検証
            foreach (ActState state in System.Enum.GetValues(typeof(ActState)))
            {
                if (CharacterCollisionLogic.CanCarry(state, CharacterBelong.Enemy))
                {
                    carriableCount++;
                }
            }

            Assert.AreEqual(4, carriableCount,
                "運搬可能な状態はFlinch, Stunned, GuardBroken, Knockbackedの4つのみ");
        }

        [Test]
        public void ShouldBlockMovement_AllContactTypes_CorrectBlockCount()
        {
            int blockCount = 0;

            foreach (AttackContactType contactType in System.Enum.GetValues(typeof(AttackContactType)))
            {
                if (CharacterCollisionLogic.ShouldBlockMovement(contactType))
                {
                    blockCount++;
                }
            }

            // StopOnHit + Carry = 2
            Assert.AreEqual(2, blockCount,
                "衝突をブロックするのはStopOnHitとCarryの2種のみ");
        }

        // --- AttackMotionData連携: contactTypeフィールドからの正しい取得 ---

        [Test]
        public void AttackMotionData_ContactType_IntegratesWithCollisionLogic()
        {
            AttackMotionData carryMotion = new AttackMotionData
            {
                contactType = AttackContactType.Carry,
                motionValue = 1.0f,
                maxHitCount = 1,
            };

            AttackMotionData passThroughMotion = new AttackMotionData
            {
                contactType = AttackContactType.PassThrough,
                motionValue = 1.0f,
                maxHitCount = 1,
            };

            Assert.IsTrue(CharacterCollisionLogic.ShouldBlockMovement(carryMotion.contactType));
            Assert.IsFalse(CharacterCollisionLogic.ShouldBlockMovement(passThroughMotion.contactType));
        }
    }
}
