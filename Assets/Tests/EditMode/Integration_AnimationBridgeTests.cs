using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// AnimationBridgeとSoAコンテナ・ActionExecutorの結合テスト。
    /// </summary>
    [TestFixture]
    public class Integration_AnimationBridgeTests
    {
        private SoACharaDataDic _data;
        private AnimationBridge _bridge;

        [SetUp]
        public void SetUp()
        {
            _data = new SoACharaDataDic(4);
            _bridge = new AnimationBridge();
        }

        [TearDown]
        public void TearDown()
        {
            _data.Dispose();
        }

        // ─── SoAコンテナ連携 ───

        [Test]
        public void AnimationStateData_SoAコンテナに登録後_参照で取得できる()
        {
            int hash = 100;
            _data.Add(hash, default, default, default, default);

            ref AnimationStateData animState = ref _data.GetAnimationState(hash);
            animState.currentPhase = AnimationPhase.Active;
            animState.currentMoveId = 5;

            ref AnimationStateData retrieved = ref _data.GetAnimationState(hash);
            Assert.AreEqual(AnimationPhase.Active, retrieved.currentPhase);
            Assert.AreEqual(5, retrieved.currentMoveId);
        }

        [Test]
        public void AnimationStateData_SwapBack削除後_残ったキャラのデータが正しい()
        {
            int hash1 = 100;
            int hash2 = 200;
            _data.Add(hash1, default, default, default, default);
            _data.Add(hash2, default, default, default, default);

            ref AnimationStateData state2 = ref _data.GetAnimationState(hash2);
            state2.currentPhase = AnimationPhase.Recovery;
            state2.currentMoveId = 7;

            _data.Remove(hash1);

            ref AnimationStateData after = ref _data.GetAnimationState(hash2);
            Assert.AreEqual(AnimationPhase.Recovery, after.currentPhase);
            Assert.AreEqual(7, after.currentMoveId);
        }

        // ─── AnimationBridge → SoA同期 ───

        [Test]
        public void AnimationBridge_フェーズ遷移結果がCurrentStateに正しく反映される()
        {
            int hash = 300;
            _data.Add(hash, default, default, default, default);

            MotionInfo motion = new MotionInfo
            {
                preMotionDuration = 0.1f,
                activeMotionDuration = 0.2f,
                recoveryDuration = 0.3f
            };

            _bridge.StartActionPhase(motion, 3);
            _bridge.TickPhase(0.15f); // → Active

            // SoAにコピー
            ref AnimationStateData animState = ref _data.GetAnimationState(hash);
            animState = _bridge.CurrentState;

            Assert.AreEqual(AnimationPhase.Active, animState.currentPhase);
            Assert.AreEqual(3, animState.currentMoveId);
            Assert.IsFalse(animState.isCancelable);
            Assert.IsTrue(animState.isCommitted);
        }

        // ─── 状態シーケンス検証 ───

        [Test]
        public void AnimationBridge_連続アクション開始_前回の状態がリセットされる()
        {
            MotionInfo motion1 = new MotionInfo
            {
                preMotionDuration = 0.5f,
                activeMotionDuration = 0.5f,
                recoveryDuration = 0.5f
            };
            MotionInfo motion2 = new MotionInfo
            {
                preMotionDuration = 0.3f,
                activeMotionDuration = 0.3f,
                recoveryDuration = 0.3f
            };

            _bridge.StartActionPhase(motion1, 1);
            _bridge.TickPhase(0.3f);

            // 途中で新しいアクション開始
            _bridge.StartActionPhase(motion2, 2);

            AnimationStateData state = _bridge.CurrentState;
            Assert.AreEqual(AnimationPhase.Anticipation, state.currentPhase);
            Assert.AreEqual(2, state.currentMoveId);
            Assert.AreEqual(0f, state.normalizedTime, 0.01f);
        }

        [Test]
        public void AnimationBridge_キャンセル後に新アクション開始_正常に遷移する()
        {
            MotionInfo motion = new MotionInfo
            {
                preMotionDuration = 0.5f,
                activeMotionDuration = 0.3f,
                recoveryDuration = 0.2f
            };

            _bridge.StartActionPhase(motion, 1);
            _bridge.CancelAction();

            Assert.AreEqual(AnimationPhase.Neutral, _bridge.CurrentState.currentPhase);

            _bridge.StartActionPhase(motion, 2);
            Assert.AreEqual(AnimationPhase.Anticipation, _bridge.CurrentState.currentPhase);
            Assert.AreEqual(2, _bridge.CurrentState.currentMoveId);
        }

        // ─── 境界値検証 ───

        [Test]
        public void AnimationBridge_全フェーズ0秒_即座にNeutralへ()
        {
            MotionInfo motion = new MotionInfo
            {
                preMotionDuration = 0f,
                activeMotionDuration = 0f,
                recoveryDuration = 0f
            };

            _bridge.StartActionPhase(motion, 1);
            // preMotionDuration=0 → Activeから開始
            // activeDuration=0 → 即Recovery、recoveryDuration=0 → 即Neutral
            _bridge.TickPhase(0.01f);

            Assert.AreEqual(AnimationPhase.Neutral, _bridge.CurrentState.currentPhase);
            Assert.AreEqual(0, _bridge.CurrentState.currentMoveId);
        }

        [Test]
        public void AnimationBridge_大きなdeltaTime_全フェーズを連鎖通過してNeutralに到達()
        {
            MotionInfo motion = new MotionInfo
            {
                preMotionDuration = 0.1f,
                activeMotionDuration = 0.1f,
                recoveryDuration = 0.1f
            };

            _bridge.StartActionPhase(motion, 1);
            // 1回のTickで全フェーズを超える時間を渡す
            _bridge.TickPhase(10.0f);

            // overflowを伝搬するループにより、1回のTickで全フェーズを通過しNeutralに到達
            Assert.AreEqual(AnimationPhase.Neutral, _bridge.CurrentState.currentPhase);
            Assert.AreEqual(0, _bridge.CurrentState.currentMoveId);
        }
    }
}
