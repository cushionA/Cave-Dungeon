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

        // ─── 上書きロックアウトパターン（ActionExecutorController.ExecuteActionの契約） ───

        [Test]
        public void 上書きロックアウト_アニメ再生中_isCancelableは常にfalse_cancelPoint到達でtrue()
        {
            // ActionExecutorController.ExecuteAction は isCancelable を見て新行動の上書きを許可するため、
            // アニメ再生開始～Recovery中のcancelPoint到達まで isCancelable=false を維持しなければならない。
            MotionInfo motion = new MotionInfo
            {
                preMotionDuration = 0.1f,
                activeMotionDuration = 0.1f,
                recoveryDuration = 1.0f
            };
            const float cancelPoint = 0.5f;

            // 開始直後（Anticipation 0%）
            _bridge.StartActionPhase(motion, 1, cancelPoint);
            Assert.AreEqual(AnimationPhase.Anticipation, _bridge.CurrentState.currentPhase);
            Assert.IsFalse(_bridge.CurrentState.isCancelable, "Anticipation開始直後は上書き不可");

            // Anticipation 終了直前
            _bridge.TickPhase(0.05f);
            Assert.AreEqual(AnimationPhase.Anticipation, _bridge.CurrentState.currentPhase);
            Assert.IsFalse(_bridge.CurrentState.isCancelable, "Anticipation途中も上書き不可");

            // Active 突入
            _bridge.TickPhase(0.05f);
            Assert.AreEqual(AnimationPhase.Active, _bridge.CurrentState.currentPhase);
            Assert.IsFalse(_bridge.CurrentState.isCancelable, "Active中は上書き不可");

            // Recovery 突入（cancelPoint未到達 = 0%）
            _bridge.TickPhase(0.1f);
            Assert.AreEqual(AnimationPhase.Recovery, _bridge.CurrentState.currentPhase);
            Assert.IsFalse(_bridge.CurrentState.isCancelable, "Recovery開始直後はcancelPoint未到達で上書き不可");

            // Recovery cancelPoint直前（normalizedTime = 0.4）
            _bridge.TickPhase(0.4f);
            Assert.AreEqual(AnimationPhase.Recovery, _bridge.CurrentState.currentPhase);
            Assert.IsFalse(_bridge.CurrentState.isCancelable, "Recovery cancelPoint未到達は上書き不可");

            // Recovery cancelPoint到達（normalizedTime = 0.6 > 0.5）
            _bridge.TickPhase(0.2f);
            Assert.AreEqual(AnimationPhase.Recovery, _bridge.CurrentState.currentPhase);
            Assert.IsTrue(_bridge.CurrentState.isCancelable, "cancelPoint超過で上書き許可");
        }

        [Test]
        public void 上書きロックアウト_cancelPoint負値_Recovery完全終了まで上書き不可()
        {
            // cancelPoint = -1 → Recovery完了まで上書き不可、Neutral到達でIsExecuting=falseになり受け入れ可能。
            MotionInfo motion = new MotionInfo
            {
                preMotionDuration = 0.1f,
                activeMotionDuration = 0.1f,
                recoveryDuration = 0.5f
            };

            _bridge.StartActionPhase(motion, 1); // cancelPoint = -1f (default)

            _bridge.TickPhase(0.1f); // → Active
            Assert.IsFalse(_bridge.CurrentState.isCancelable);

            _bridge.TickPhase(0.1f); // → Recovery
            Assert.IsFalse(_bridge.CurrentState.isCancelable);

            _bridge.TickPhase(0.4f); // Recovery 80% (まだ未完了)
            Assert.AreEqual(AnimationPhase.Recovery, _bridge.CurrentState.currentPhase);
            Assert.IsFalse(_bridge.CurrentState.isCancelable, "cancelPoint=-1ならRecovery中は常に上書き不可");

            _bridge.TickPhase(0.15f); // → Neutral
            Assert.AreEqual(AnimationPhase.Neutral, _bridge.CurrentState.currentPhase);
            // Neutral では isCancelable=false だが、ExecuteAction側の IsExecuting=false で受け入れられる
        }
    }
}
