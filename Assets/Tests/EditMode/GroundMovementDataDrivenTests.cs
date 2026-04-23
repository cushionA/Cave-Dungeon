using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// GroundMovementLogic のデータ駆動化検証。
    /// MoveParams.dodgeDuration / dodgeSpeedMultiplier / sprintSpeedMultiplier を
    /// キャラ別に変えたとき、回避時間・速度が期待通り変動することを保証する。
    /// </summary>
    [TestFixture]
    public class GroundMovementDataDrivenTests
    {
        /// <summary>
        /// MoveParams.dodgeDuration が 0 の場合、GroundMovementLogic は k_DodgeDuration (0.25f) を
        /// フォールバックとして使用し続ける。既存のハードコード挙動との互換性を保証する。
        /// </summary>
        [Test]
        public void UpdateDodge_WhenDodgeDurationUnset_FallsBackToConstantDefault()
        {
            GroundMovementLogic logic = new GroundMovementLogic();
            MoveParams paramsA = new MoveParams
            {
                moveSpeed = 5f,
                dashSpeed = 12f,
                dodgeStaminaCost = 15f,
                // dodgeDuration は 0 (未設定) → フォールバック
            };
            float stamina = 100f;
            logic.TryStartDodge(true, ref stamina, paramsA);

            // フォールバック値 0.25f の直前 (0.24f) ではまだ回避中
            bool stillDodging = logic.UpdateDodge(0.24f, paramsA);
            Assert.IsTrue(stillDodging, "0.24s 時点では k_DodgeDuration(0.25s) 以内なので継続");

            // フォールバック値を超過すると終了
            stillDodging = logic.UpdateDodge(0.02f, paramsA);
            Assert.IsFalse(stillDodging, "累計 0.26s で k_DodgeDuration(0.25s) を超え終了");
            Assert.IsFalse(logic.IsDodging);
        }

        /// <summary>
        /// キャラ別に dodgeDuration を変えると、回避終了までの経過時間が異なる。
        /// データ駆動: 短いキャラは早く終わり、長いキャラは遅く終わる。
        /// </summary>
        [Test]
        public void UpdateDodge_DifferentDodgeDuration_ProducesDifferentTermination()
        {
            // 短い回避キャラ (0.15s)
            GroundMovementLogic logicShort = new GroundMovementLogic();
            MoveParams paramsShort = new MoveParams
            {
                moveSpeed = 5f,
                dashSpeed = 12f,
                dodgeStaminaCost = 15f,
                dodgeDuration = 0.15f
            };
            float staminaShort = 100f;
            logicShort.TryStartDodge(true, ref staminaShort, paramsShort);

            // 長い回避キャラ (0.5s)
            GroundMovementLogic logicLong = new GroundMovementLogic();
            MoveParams paramsLong = new MoveParams
            {
                moveSpeed = 5f,
                dashSpeed = 12f,
                dodgeStaminaCost = 15f,
                dodgeDuration = 0.5f
            };
            float staminaLong = 100f;
            logicLong.TryStartDodge(true, ref staminaLong, paramsLong);

            // 0.2s 経過: 短い側は終了、長い側は継続
            bool shortStill = logicShort.UpdateDodge(0.2f, paramsShort);
            bool longStill = logicLong.UpdateDodge(0.2f, paramsLong);

            Assert.IsFalse(shortStill, "dodgeDuration=0.15s のキャラは 0.2s で終了");
            Assert.IsFalse(logicShort.IsDodging);
            Assert.IsTrue(longStill, "dodgeDuration=0.5s のキャラは 0.2s 時点ではまだ回避中");
            Assert.IsTrue(logicLong.IsDodging);
        }

        /// <summary>
        /// キャラ別に sprintSpeedMultiplier を変えると、スプリント中の水平速度が異なる。
        /// データ駆動: 倍率が大きいキャラは高速、小さいキャラは低速。
        /// </summary>
        [Test]
        public void CalculateHorizontalSpeed_DifferentSprintMultiplier_ProducesDifferentSpeed()
        {
            GroundMovementLogic logicFast = new GroundMovementLogic();
            MoveParams paramsFast = new MoveParams
            {
                moveSpeed = 5f,
                sprintStaminaPerSecond = 10f,
                sprintSpeedMultiplier = 2.0f
            };

            GroundMovementLogic logicSlow = new GroundMovementLogic();
            MoveParams paramsSlow = new MoveParams
            {
                moveSpeed = 5f,
                sprintStaminaPerSecond = 10f,
                sprintSpeedMultiplier = 1.2f
            };

            // 両方スプリント状態に遷移
            float staminaFast = 100f;
            float staminaSlow = 100f;
            logicFast.UpdateSprint(true, ref staminaFast, 0.016f, paramsFast);
            logicSlow.UpdateSprint(true, ref staminaSlow, 0.016f, paramsSlow);
            Assert.IsTrue(logicFast.IsSprinting);
            Assert.IsTrue(logicSlow.IsSprinting);

            float speedFast = logicFast.CalculateHorizontalSpeed(1f, paramsFast);
            float speedSlow = logicSlow.CalculateHorizontalSpeed(1f, paramsSlow);

            // moveSpeed(5) * sprintSpeedMultiplier
            Assert.AreEqual(5f * 2.0f, speedFast, 0.001f, "sprintSpeedMultiplier=2.0 で 10.0 m/s");
            Assert.AreEqual(5f * 1.2f, speedSlow, 0.001f, "sprintSpeedMultiplier=1.2 で 6.0 m/s");
            Assert.Greater(speedFast, speedSlow, "倍率大のキャラは倍率小のキャラより速い");
        }

        /// <summary>
        /// sprintSpeedMultiplier が未設定 (0) の場合、定数フォールバック (k_SprintSpeedMultiplier=1.6f)
        /// が使用される。既存テスト・既存アセットとの後方互換を保証する。
        /// </summary>
        [Test]
        public void CalculateHorizontalSpeed_WhenSprintMultiplierUnset_FallsBackToConstantDefault()
        {
            GroundMovementLogic logic = new GroundMovementLogic();
            MoveParams mp = new MoveParams
            {
                moveSpeed = 5f,
                sprintStaminaPerSecond = 10f
                // sprintSpeedMultiplier は 0 (未設定) → フォールバック
            };

            float stamina = 100f;
            logic.UpdateSprint(true, ref stamina, 0.016f, mp);
            Assert.IsTrue(logic.IsSprinting);

            float speed = logic.CalculateHorizontalSpeed(1f, mp);

            Assert.AreEqual(5f * GroundMovementLogic.k_SprintSpeedMultiplier, speed, 0.001f,
                "未設定時は定数 k_SprintSpeedMultiplier(1.6f) を使用");
        }

        /// <summary>
        /// 回避中は dashSpeed が優先される (既存挙動)。
        /// dashSpeed を 0 に設定すると dodgeSpeedMultiplier (キャラ別) にフォールバックする。
        /// </summary>
        [Test]
        public void CalculateHorizontalSpeed_DodgeWithoutDashSpeed_UsesDodgeSpeedMultiplier()
        {
            GroundMovementLogic logic = new GroundMovementLogic();
            MoveParams mp = new MoveParams
            {
                moveSpeed = 5f,
                dashSpeed = 0f,           // 未設定 → dodgeSpeedMultiplier にフォールバック
                dodgeStaminaCost = 15f,
                dodgeSpeedMultiplier = 3.0f
            };

            float stamina = 100f;
            logic.TryStartDodge(true, ref stamina, mp);
            Assert.IsTrue(logic.IsDodging);

            float speed = logic.CalculateHorizontalSpeed(1f, mp);

            Assert.AreEqual(5f * 3.0f, speed, 0.001f,
                "dashSpeed 未設定時は moveSpeed * dodgeSpeedMultiplier を使用");
        }

        /// <summary>
        /// dashSpeed が設定されていれば従来通り dashSpeed を優先する (既存テスト互換)。
        /// dodgeSpeedMultiplier は無視される。
        /// </summary>
        [Test]
        public void CalculateHorizontalSpeed_DodgeWithDashSpeed_UsesDashSpeedDirectly()
        {
            GroundMovementLogic logic = new GroundMovementLogic();
            MoveParams mp = new MoveParams
            {
                moveSpeed = 5f,
                dashSpeed = 12f,              // 明示設定 → こちらを優先
                dodgeStaminaCost = 15f,
                dodgeSpeedMultiplier = 999f  // 無視されるはず
            };

            float stamina = 100f;
            logic.TryStartDodge(true, ref stamina, mp);
            Assert.IsTrue(logic.IsDodging);

            float speed = logic.CalculateHorizontalSpeed(1f, mp);

            Assert.AreEqual(12f, speed, 0.001f, "dashSpeed 優先 (既存互換)");
        }
    }
}
