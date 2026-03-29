using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// AnimationBridge の単体テスト。
    /// 純ロジック層でAnimatorパラメータ変更の蓄積・消費、
    /// AnimationStateDataの更新を検証する。
    /// </summary>
    [TestFixture]
    public class AnimationBridgeTests
    {
        private AnimationBridge _bridge;

        [SetUp]
        public void SetUp()
        {
            _bridge = new AnimationBridge();
        }

        // ─── パラメータ設定・取得 ───

        [Test]
        public void SetFloat_パラメータ名で設定_値が取得できる()
        {
            _bridge.SetFloat("Speed", 5.0f);

            Assert.AreEqual(5.0f, _bridge.GetFloat("Speed"), 0.001f);
        }

        [Test]
        public void SetBool_パラメータ名で設定_値が取得できる()
        {
            _bridge.SetBool("IsGrounded", true);

            Assert.IsTrue(_bridge.GetBool("IsGrounded"));
        }

        [Test]
        public void SetInt_パラメータ名で設定_値が取得できる()
        {
            _bridge.SetInt("WeaponType", 3);

            Assert.AreEqual(3, _bridge.GetInt("WeaponType"));
        }

        [Test]
        public void SetTrigger_トリガーフラグが立つ()
        {
            _bridge.SetTrigger("Attack");

            Assert.IsTrue(_bridge.ConsumeTrigger("Attack"));
        }

        [Test]
        public void ConsumeTrigger_消費後はfalse()
        {
            _bridge.SetTrigger("Attack");
            _bridge.ConsumeTrigger("Attack");

            Assert.IsFalse(_bridge.ConsumeTrigger("Attack"));
        }

        [Test]
        public void ResetTrigger_設定済みトリガーをリセットできる()
        {
            _bridge.SetTrigger("Attack");
            _bridge.ResetTrigger("Attack");

            Assert.IsFalse(_bridge.ConsumeTrigger("Attack"));
        }

        // ─── ダーティフラグ ───

        [Test]
        public void SetFloat_ダーティフラグが立つ()
        {
            _bridge.ClearDirty();
            _bridge.SetFloat("Speed", 1.0f);

            Assert.IsTrue(_bridge.IsDirty);
        }

        [Test]
        public void ClearDirty_フラグがリセットされる()
        {
            _bridge.SetFloat("Speed", 1.0f);
            _bridge.ClearDirty();

            Assert.IsFalse(_bridge.IsDirty);
        }

        [Test]
        public void SetFloat_同じ値再設定_ダーティにならない()
        {
            _bridge.SetFloat("Speed", 1.0f);
            _bridge.ClearDirty();
            _bridge.SetFloat("Speed", 1.0f);

            Assert.IsFalse(_bridge.IsDirty);
        }

        // ─── アクションフェーズ管理 ───

        [Test]
        public void StartActionPhase_フェーズがAnticipationに遷移()
        {
            MotionInfo motion = new MotionInfo
            {
                preMotionDuration = 0.2f,
                activeMotionDuration = 0.3f,
                recoveryDuration = 0.5f
            };

            _bridge.StartActionPhase(motion, 1);

            AnimationStateData state = _bridge.CurrentState;
            Assert.AreEqual(AnimationPhase.Anticipation, state.currentPhase);
            Assert.AreEqual(1, state.currentMoveId);
            Assert.IsTrue(state.isCommitted);
        }

        [Test]
        public void TickPhase_予備動作完了後_Activeに遷移()
        {
            MotionInfo motion = new MotionInfo
            {
                preMotionDuration = 0.2f,
                activeMotionDuration = 0.3f,
                recoveryDuration = 0.5f
            };

            _bridge.StartActionPhase(motion, 1);
            _bridge.TickPhase(0.25f); // 0.2秒を超過

            Assert.AreEqual(AnimationPhase.Active, _bridge.CurrentState.currentPhase);
        }

        [Test]
        public void TickPhase_Active完了後_Recoveryに遷移()
        {
            MotionInfo motion = new MotionInfo
            {
                preMotionDuration = 0.1f,
                activeMotionDuration = 0.2f,
                recoveryDuration = 0.3f
            };

            _bridge.StartActionPhase(motion, 1);
            _bridge.TickPhase(0.15f); // Anticipation → Active
            _bridge.TickPhase(0.25f); // Active → Recovery

            Assert.AreEqual(AnimationPhase.Recovery, _bridge.CurrentState.currentPhase);
        }

        [Test]
        public void TickPhase_Recovery完了後_Neutralに遷移()
        {
            MotionInfo motion = new MotionInfo
            {
                preMotionDuration = 0.1f,
                activeMotionDuration = 0.1f,
                recoveryDuration = 0.1f
            };

            _bridge.StartActionPhase(motion, 1);
            _bridge.TickPhase(0.15f); // → Active
            _bridge.TickPhase(0.15f); // → Recovery
            _bridge.TickPhase(0.15f); // → Neutral

            AnimationStateData state = _bridge.CurrentState;
            Assert.AreEqual(AnimationPhase.Neutral, state.currentPhase);
            Assert.AreEqual(0, state.currentMoveId);
            Assert.IsFalse(state.isCommitted);
        }

        [Test]
        public void TickPhase_Neutral時_何も変わらない()
        {
            _bridge.TickPhase(1.0f);

            Assert.AreEqual(AnimationPhase.Neutral, _bridge.CurrentState.currentPhase);
        }

        [Test]
        public void CancelAction_Anticipation中_Neutralに即座に戻る()
        {
            MotionInfo motion = new MotionInfo
            {
                preMotionDuration = 0.5f,
                activeMotionDuration = 0.3f,
                recoveryDuration = 0.2f
            };

            _bridge.StartActionPhase(motion, 1);
            _bridge.CancelAction();

            AnimationStateData state = _bridge.CurrentState;
            Assert.AreEqual(AnimationPhase.Neutral, state.currentPhase);
            Assert.AreEqual(0, state.currentMoveId);
        }

        [Test]
        public void StartActionPhase_preMotionDurationが0_Active開始()
        {
            MotionInfo motion = new MotionInfo
            {
                preMotionDuration = 0f,
                activeMotionDuration = 0.3f,
                recoveryDuration = 0.2f
            };

            _bridge.StartActionPhase(motion, 2);

            Assert.AreEqual(AnimationPhase.Active, _bridge.CurrentState.currentPhase);
        }

        // ─── normalizedTime ───

        [Test]
        public void TickPhase_normalizedTimeがフェーズ内で0から1に進む()
        {
            MotionInfo motion = new MotionInfo
            {
                preMotionDuration = 1.0f,
                activeMotionDuration = 1.0f,
                recoveryDuration = 1.0f
            };

            _bridge.StartActionPhase(motion, 1);
            _bridge.TickPhase(0.5f);

            Assert.AreEqual(0.5f, _bridge.CurrentState.normalizedTime, 0.01f);
        }

        // ─── isCancelable ───

        [Test]
        public void StartActionPhase_Anticipation中はisCancelableがtrue()
        {
            MotionInfo motion = new MotionInfo
            {
                preMotionDuration = 0.5f,
                activeMotionDuration = 0.3f,
                recoveryDuration = 0.2f
            };

            _bridge.StartActionPhase(motion, 1);

            Assert.IsTrue(_bridge.CurrentState.isCancelable);
        }

        [Test]
        public void TickPhase_Active中はisCancelableがfalse()
        {
            MotionInfo motion = new MotionInfo
            {
                preMotionDuration = 0.1f,
                activeMotionDuration = 0.3f,
                recoveryDuration = 0.2f
            };

            _bridge.StartActionPhase(motion, 1);
            _bridge.TickPhase(0.15f);

            Assert.IsFalse(_bridge.CurrentState.isCancelable);
        }
    }
}
