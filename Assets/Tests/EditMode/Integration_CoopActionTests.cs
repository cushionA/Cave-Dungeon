using NUnit.Framework;
using Game.Core;
using UnityEngine;

namespace Game.Tests.EditMode
{
    [TestFixture]
    public class Integration_CoopActionTests
    {
        private SoACharaDataDic _data;
        private GameEvents _events;
        private CoopCooldownTracker _cooldownTracker;
        private CoopInterruptionHandler _interruptionHandler;

        private const int k_CompanionHash = 1;
        private const int k_TargetHash = 10;

        [SetUp]
        public void SetUp()
        {
            _data = new SoACharaDataDic(4);
            _events = new GameEvents();
            _cooldownTracker = new CoopCooldownTracker();
            _interruptionHandler = new CoopInterruptionHandler();

            // 仲間: position=0,0, MP=50
            CharacterVitals companionVitals = new CharacterVitals
            {
                currentHp = 100,
                maxHp = 100,
                currentMp = 50,
                maxMp = 50,
                position = Vector2.zero,
                level = 8
            };
            CharacterFlags companionFlags = CharacterFlags.Pack(
                CharacterBelong.Ally, CharacterFeature.Companion, AbilityFlag.None);
            _data.Add(k_CompanionHash, companionVitals, default, companionFlags, default);

            // ターゲット: position=5,0
            CharacterVitals targetVitals = new CharacterVitals
            {
                currentHp = 200,
                maxHp = 200,
                position = new Vector2(5f, 0f),
                level = 10
            };
            CharacterFlags targetFlags = CharacterFlags.Pack(
                CharacterBelong.Enemy, CharacterFeature.Minion, AbilityFlag.None);
            _data.Add(k_TargetHash, targetVitals, default, targetFlags, default);
        }

        [TearDown]
        public void TearDown()
        {
            _events.Dispose();
            _data.Dispose();
        }

        [Test]
        public void CoopAction_TriggerShield_ActivatesAndThenCooldown()
        {
            // Arrange
            ShieldCoopAction shield = new ShieldCoopAction(5f);

            // Act: クールダウンチェック→アクティベート
            CoopCooldownTracker.ActivationResult result = _cooldownTracker.TryActivate(
                currentTime: 0f,
                mpCost: shield.MpCost,
                currentMp: 50,
                cooldownDuration: shield.CooldownDuration
            );

            // Assert: 初回は無料発動
            Assert.IsTrue(result.success);
            Assert.IsTrue(result.isFree);

            // シールド実行
            shield.ExecuteCombo(0, k_CompanionHash, k_TargetHash);
            Assert.IsTrue(shield.IsShieldActive);

            // クールダウン中
            Assert.IsFalse(_cooldownTracker.IsCooldownReady(1f));
        }

        [Test]
        public void CoopAction_WarpAction_MovesCompanionPosition()
        {
            // Arrange
            WarpCoopAction warp = new WarpCoopAction(_data);

            // Act: ワープ実行（comboIndex=0 → Behind）
            warp.ExecuteCombo(0, k_CompanionHash, k_TargetHash);

            // Assert: 仲間の位置がターゲット付近に移動
            Vector2 targetPos = _data.GetVitals(k_TargetHash).position; // (5, 0)
            Vector2 expectedPos = WarpCoopAction.CalculateWarpPosition(targetPos, WarpTarget.Behind);
            Vector2 companionPos = _data.GetVitals(k_CompanionHash).position;

            Assert.AreEqual(expectedPos.x, companionPos.x, 0.1f);
            Assert.AreEqual(expectedPos.y, companionPos.y, 0.1f);
        }

        [Test]
        public void CoopAction_CooldownReady_FiresFeedbackEvent()
        {
            // Arrange
            CooldownRewardFeedback feedback = new CooldownRewardFeedback();
            bool readyFired = false;
            feedback.OnCooldownReady += () => readyFired = true;

            // クールダウン開始（duration=5秒）
            _cooldownTracker.TryActivate(0f, 20, 50, 5f);

            // Act: time=3 → まだクールダウン中
            feedback.Update(_cooldownTracker, 3f);
            Assert.IsFalse(readyFired);

            // time=6 → クールダウン完了
            feedback.Update(_cooldownTracker, 6f);
            Assert.IsTrue(readyFired);
        }

        [Test]
        public void CoopAction_DuringCooldown_MpActivation()
        {
            // Arrange: 初回無料発動
            CoopCooldownTracker.ActivationResult result1 = _cooldownTracker.TryActivate(0f, 20, 50, 18f);
            Assert.IsTrue(result1.success);
            Assert.IsTrue(result1.isFree);

            // Act: クールダウン中に再発動（MP消費）
            CoopCooldownTracker.ActivationResult result2 = _cooldownTracker.TryActivate(1f, 20, 50, 18f);

            // Assert: 成功するがMP消費あり
            Assert.IsTrue(result2.success);
            Assert.IsFalse(result2.isFree);
            Assert.AreEqual(20, result2.mpConsumed);
        }

        [Test]
        public void CoopAction_InterruptAndResume_SavesCompanionState()
        {
            // Arrange: 仲間の現在アクション
            ActionSlot currentSlot = new ActionSlot
            {
                execType = ActionExecType.Sustained,
                paramId = 1,
                paramValue = 5f,
                displayName = "Follow"
            };

            // Act: 協力アクションで中断
            bool interrupted = _interruptionHandler.InterruptForCoop(currentSlot, k_TargetHash);
            Assert.IsTrue(interrupted);
            Assert.IsTrue(_interruptionHandler.IsInterrupted);

            // 協力アクション終了後、復帰
            (ActionSlot slot, int targetHash)? restored = _interruptionHandler.ResumeFromCoop();

            // Assert: 復帰データが保存されたものと一致
            Assert.IsNotNull(restored);
            Assert.IsFalse(_interruptionHandler.IsInterrupted);
            Assert.AreEqual(ActionExecType.Sustained, restored.Value.slot.execType);
            Assert.AreEqual("Follow", restored.Value.slot.displayName);
            Assert.AreEqual(k_TargetHash, restored.Value.targetHash);
        }
    }
}
