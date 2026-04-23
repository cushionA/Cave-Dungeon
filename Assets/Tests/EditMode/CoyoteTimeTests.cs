using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// GroundMovementLogic の Coyote Time 機構を検証する。
    /// UpdateGroundedState で追跡する _timeSinceLeftGround と
    /// TryStartJump の許容判定 (isGrounded || IsInCoyoteWindow) を単体で検証。
    /// </summary>
    [TestFixture]
    public class CoyoteTimeTests
    {
        private GroundMovementLogic _logic;
        private MoveParams _moveParams;

        [SetUp]
        public void SetUp()
        {
            _logic = new GroundMovementLogic();
            _moveParams = new MoveParams
            {
                moveSpeed = 6f,
                jumpForce = 10f,
                jumpStaminaCost = 0f,
            };
        }

        [Test]
        public void CoyoteTime_WhenGrounded_JumpSucceeds()
        {
            _logic.UpdateGroundedState(isGrounded: true, deltaTime: 0.02f);
            float jumpForce = _logic.TryStartJump(true, isGrounded: true, _moveParams);
            Assert.Greater(jumpForce, 0f, "接地中はジャンプが成立する");
        }

        [Test]
        public void CoyoteTime_WhenJustLeftGround_JumpAllowedWithinWindow()
        {
            // 接地中 → 離地直後 (5ms 経過) で Coyote 窓内
            _logic.UpdateGroundedState(true, 0.02f);
            _logic.UpdateGroundedState(false, 0.005f);
            Assert.IsTrue(_logic.IsInCoyoteWindow, "離地直後は Coyote 窓内");

            float jumpForce = _logic.TryStartJump(true, isGrounded: false, _moveParams);
            Assert.Greater(jumpForce, 0f, "Coyote 窓内なら空中でもジャンプ成立");
        }

        [Test]
        public void CoyoteTime_WhenExceededWindow_JumpRejected()
        {
            _logic.UpdateGroundedState(true, 0.02f);
            // 100ms 経過 (k_CoyoteTimeSeconds = 0.1f) を確実に超える
            _logic.UpdateGroundedState(false, 0.11f);
            Assert.IsFalse(_logic.IsInCoyoteWindow, "窓を越えたら Coyote 不成立");

            float jumpForce = _logic.TryStartJump(true, isGrounded: false, _moveParams);
            Assert.AreEqual(0f, jumpForce, "窓を越えた空中ジャンプは不可");
        }

        [Test]
        public void CoyoteTime_WhenJumpTriggeredInWindow_CoyoteConsumedPreventsSecondJump()
        {
            _logic.UpdateGroundedState(true, 0.02f);
            _logic.UpdateGroundedState(false, 0.01f);

            float firstJump = _logic.TryStartJump(true, isGrounded: false, _moveParams);
            Assert.Greater(firstJump, 0f, "1 度目の Coyote ジャンプは成立");

            // 直後にもう 1 度ジャンプ入力しても窓は消費済み
            _logic.UpdateGroundedState(false, 0.01f);
            float secondJump = _logic.TryStartJump(true, isGrounded: false, _moveParams);
            Assert.AreEqual(0f, secondJump, "Coyote 窓は 1 ジャンプで消費、2 度目は不可");
        }

        [Test]
        public void CoyoteTime_NeverGroundedSinceCreation_AirJumpRejected()
        {
            // 空中スポーン等で一度も接地していない場合、
            // _timeSinceLeftGround = MaxValue で Coyote が誤発動しない
            Assert.IsFalse(_logic.IsInCoyoteWindow, "未接地状態は Coyote 窓外");
            float jumpForce = _logic.TryStartJump(true, isGrounded: false, _moveParams);
            Assert.AreEqual(0f, jumpForce, "接地未経験の空中ジャンプは不可");
        }

        [Test]
        public void CoyoteTime_AfterGrounded_WindowResetsForNextLeave()
        {
            // 1 度空中に出て窓を消化 → 再接地 → 再離地で窓が復活する
            _logic.UpdateGroundedState(true, 0.02f);
            _logic.UpdateGroundedState(false, 0.01f);
            _logic.TryStartJump(true, false, _moveParams); // 窓消費

            _logic.UpdateGroundedState(true, 0.02f); // 再接地 → 窓リセット
            _logic.UpdateGroundedState(false, 0.01f);

            float jumpForce = _logic.TryStartJump(true, isGrounded: false, _moveParams);
            Assert.Greater(jumpForce, 0f, "再接地後は Coyote 窓が復活する");
        }

        // =====================================================
        //  データ駆動化検証 (MoveParams.coyoteTime)
        //  キャラ別に coyoteTime を変えたとき、許容窓が期待通り変動することを保証する。
        // =====================================================

        /// <summary>
        /// MoveParams.coyoteTime が 0 (未設定) の場合は k_CoyoteTimeSeconds (0.1f) が
        /// フォールバックとして使用される。既存アセットとの後方互換を保証する。
        /// </summary>
        [Test]
        public void CoyoteTime_WhenCoyoteTimeUnset_FallsBackToConstantDefault()
        {
            MoveParams mp = new MoveParams
            {
                moveSpeed = 6f,
                jumpForce = 10f,
                jumpStaminaCost = 0f,
                // coyoteTime は 0 (未設定) → 定数 k_CoyoteTimeSeconds (0.1f) フォールバック
            };

            _logic.UpdateGroundedState(true, 0.02f);
            // 90ms 経過: 定数フォールバック窓 (100ms) 内 → 成立
            _logic.UpdateGroundedState(false, 0.09f);
            Assert.IsTrue(_logic.IsInCoyoteWindowFor(mp), "未設定時は定数フォールバック (100ms) の窓内");

            float jumpForce = _logic.TryStartJump(true, isGrounded: false, mp);
            Assert.Greater(jumpForce, 0f, "未設定時は定数フォールバックでジャンプ成立");
        }

        /// <summary>
        /// MoveParams.coyoteTime を小さく設定したキャラは、通常より早く Coyote 窓が閉じる。
        /// </summary>
        [Test]
        public void CoyoteTime_WithShortCoyoteTime_WindowClosesEarlier()
        {
            MoveParams mp = new MoveParams
            {
                moveSpeed = 6f,
                jumpForce = 10f,
                jumpStaminaCost = 0f,
                coyoteTime = 0.05f  // 50ms (短縮)
            };

            _logic.UpdateGroundedState(true, 0.02f);
            // 60ms 経過: キャラ別 coyoteTime (50ms) を超過、定数フォールバック (100ms) 内
            _logic.UpdateGroundedState(false, 0.06f);

            Assert.IsFalse(_logic.IsInCoyoteWindowFor(mp), "coyoteTime=0.05s のキャラは 60ms で窓外");

            float jumpForce = _logic.TryStartJump(true, isGrounded: false, mp);
            Assert.AreEqual(0f, jumpForce, "短縮設定時は定数より早く窓が閉じてジャンプ不可");
        }

        /// <summary>
        /// MoveParams.coyoteTime を大きく設定したキャラは、通常より長く Coyote 窓が維持される。
        /// </summary>
        [Test]
        public void CoyoteTime_WithLongCoyoteTime_WindowStaysOpenLonger()
        {
            MoveParams mp = new MoveParams
            {
                moveSpeed = 6f,
                jumpForce = 10f,
                jumpStaminaCost = 0f,
                coyoteTime = 0.25f  // 250ms (延長)
            };

            _logic.UpdateGroundedState(true, 0.02f);
            // 150ms 経過: 定数フォールバック (100ms) は超過、キャラ別 (250ms) 内
            _logic.UpdateGroundedState(false, 0.15f);

            Assert.IsTrue(_logic.IsInCoyoteWindowFor(mp), "coyoteTime=0.25s のキャラは 150ms でもまだ窓内");

            float jumpForce = _logic.TryStartJump(true, isGrounded: false, mp);
            Assert.Greater(jumpForce, 0f, "延長設定時は定数フォールバックより長く窓が開く");
        }

        /// <summary>
        /// 2 体のキャラが異なる coyoteTime を持つ場合、同じ離地時間でも一方は成立、
        /// もう一方は失敗するという意味でデータ駆動が機能している。
        /// </summary>
        [Test]
        public void CoyoteTime_DifferentCoyoteTime_ProducesDifferentJumpResult()
        {
            GroundMovementLogic logicA = new GroundMovementLogic();
            GroundMovementLogic logicB = new GroundMovementLogic();

            MoveParams mpShort = new MoveParams
            {
                moveSpeed = 6f,
                jumpForce = 10f,
                jumpStaminaCost = 0f,
                coyoteTime = 0.05f
            };
            MoveParams mpLong = new MoveParams
            {
                moveSpeed = 6f,
                jumpForce = 10f,
                jumpStaminaCost = 0f,
                coyoteTime = 0.2f
            };

            logicA.UpdateGroundedState(true, 0.02f);
            logicB.UpdateGroundedState(true, 0.02f);
            // 100ms 経過: 短いキャラは窓外、長いキャラは窓内
            logicA.UpdateGroundedState(false, 0.1f);
            logicB.UpdateGroundedState(false, 0.1f);

            float jumpA = logicA.TryStartJump(true, false, mpShort);
            float jumpB = logicB.TryStartJump(true, false, mpLong);

            Assert.AreEqual(0f, jumpA, "coyoteTime=0.05s は 100ms で窓外、ジャンプ不可");
            Assert.Greater(jumpB, 0f, "coyoteTime=0.2s は 100ms で窓内、ジャンプ成立");
        }
    }
}
