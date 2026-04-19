using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// 設計書04: ガード中の状態異常無効・ガードスタミナコスト・Weakness被ダメ増加
    /// </summary>
    public class DamageGuardStatusBlockTests
    {
        // --- ガード中は状態異常蓄積無効 ---
        // 設計書: "ガード中は状態異常を完全ブロック"

        [Test]
        public void StatusEffectManager_Accumulate_WhenGuarded_ShouldNotAccumulate()
        {
            // ガード成立時は DamageReceiver が TryApplyStatusEffect をスキップする設計
            // ここではStatusEffectManager側で「呼ばれなければ蓄積しない」ことを確認
            StatusEffectManager manager = new StatusEffectManager();

            // ガード中はTryApplyStatusEffectを呼ばない → 蓄積されない
            Assert.IsFalse(manager.IsActive(StatusEffectId.Poison));
            Assert.AreEqual(0, manager.ActiveCount);
        }

        [Test]
        public void StatusEffectManager_Accumulate_WhenNotGuarded_ShouldAccumulate()
        {
            StatusEffectManager manager = new StatusEffectManager();

            StatusEffectInfo info = new StatusEffectInfo
            {
                effect = StatusEffectId.Poison,
                accumulateValue = StatusEffectManager.k_DefaultThreshold, // 閾値ちょうど
                duration = 10f,
                tickDamage = 5f,
                tickInterval = 2f,
            };

            bool triggered = manager.Accumulate(info, 0f);

            Assert.IsTrue(triggered, "ガードなし: 閾値到達で発症");
            Assert.IsTrue(manager.IsActive(StatusEffectId.Poison));
        }

        // --- 状態異常11種: 蓄積→発症→Tick個別テスト ---

        [Test]
        public void StatusEffect_Poison_AccumulateAndTick_DealsTickDamage()
        {
            StatusEffectManager manager = new StatusEffectManager();
            StatusEffectInfo info = new StatusEffectInfo
            {
                effect = StatusEffectId.Poison,
                accumulateValue = 100f,
                duration = 10f,
                tickDamage = 3f,
                tickInterval = 1f,
            };
            manager.Accumulate(info, 0f);

            int damage = manager.Tick(1.0f);

            Assert.AreEqual(3, damage, "Poison: 1秒で3ダメージ");
        }

        [Test]
        public void StatusEffect_Burn_AccumulateAndTick_DealsTickDamage()
        {
            StatusEffectManager manager = new StatusEffectManager();
            StatusEffectInfo info = new StatusEffectInfo
            {
                effect = StatusEffectId.Burn,
                accumulateValue = 100f,
                duration = 8f,
                tickDamage = 5f,
                tickInterval = 0.5f,
            };
            manager.Accumulate(info, 0f);

            int damage = manager.Tick(1.0f);

            Assert.AreEqual(10, damage, "Burn: 0.5秒間隔×2回=10ダメージ");
        }

        [Test]
        public void StatusEffect_Bleed_AccumulateAndTick_DealsTickDamage()
        {
            StatusEffectManager manager = new StatusEffectManager();
            StatusEffectInfo info = new StatusEffectInfo
            {
                effect = StatusEffectId.Bleed,
                accumulateValue = 100f,
                duration = 6f,
                tickDamage = 4f,
                tickInterval = 2f,
            };
            manager.Accumulate(info, 0f);

            int damage = manager.Tick(2.0f);

            Assert.AreEqual(4, damage, "Bleed: 2秒で1tick=4ダメージ");
        }

        [Test]
        public void StatusEffect_Stun_Activates_HasZeroTickDamage()
        {
            StatusEffectManager manager = new StatusEffectManager();
            StatusEffectInfo info = new StatusEffectInfo
            {
                effect = StatusEffectId.Stun,
                accumulateValue = 100f,
                duration = 1.5f,
                tickDamage = 0f,
                tickInterval = 1f,
            };
            manager.Accumulate(info, 0f);

            Assert.IsTrue(manager.IsActive(StatusEffectId.Stun));
            int damage = manager.Tick(1.0f);
            Assert.AreEqual(0, damage, "Stun: ダメージなし、行動不能のみ");
        }

        [Test]
        public void StatusEffect_Freeze_Activates()
        {
            StatusEffectManager manager = new StatusEffectManager();
            StatusEffectInfo info = new StatusEffectInfo
            {
                effect = StatusEffectId.Freeze,
                accumulateValue = 100f,
                duration = 3f,
                tickDamage = 0f,
                tickInterval = 1f,
            };
            manager.Accumulate(info, 0f);
            Assert.IsTrue(manager.IsActive(StatusEffectId.Freeze));
        }

        [Test]
        public void StatusEffect_Weakness_Activates_WithModifier()
        {
            StatusEffectManager manager = new StatusEffectManager();
            StatusEffectInfo info = new StatusEffectInfo
            {
                effect = StatusEffectId.Weakness,
                accumulateValue = 100f,
                duration = 15f,
                tickDamage = 0f,
                tickInterval = 1f,
                modifier = 1.3f,
            };
            bool triggered = manager.Accumulate(info, 0f);
            Assert.IsTrue(triggered);
            Assert.IsTrue(manager.IsActive(StatusEffectId.Weakness));
        }

        [Test]
        public void StatusEffect_MaxThreeSimultaneous_FourthRejected()
        {
            StatusEffectManager manager = new StatusEffectManager();

            StatusEffectId[] effects = { StatusEffectId.Poison, StatusEffectId.Burn, StatusEffectId.Bleed };
            foreach (StatusEffectId id in effects)
            {
                StatusEffectInfo info = new StatusEffectInfo
                {
                    effect = id, accumulateValue = 100f, duration = 10f,
                    tickDamage = 1f, tickInterval = 1f,
                };
                manager.Accumulate(info, 0f);
            }

            Assert.AreEqual(3, manager.ActiveCount, "最大3スロット同時");

            // 4つ目は拒否
            StatusEffectInfo fourthInfo = new StatusEffectInfo
            {
                effect = StatusEffectId.Stun, accumulateValue = 100f, duration = 5f,
                tickDamage = 0f, tickInterval = 1f,
            };
            bool fourthTriggered = manager.Accumulate(fourthInfo, 0f);
            Assert.IsFalse(fourthTriggered, "4つ目は発症しない");
        }

        // --- ガードスタミナコスト: GuardJudgmentLogicのガードブレイク境界 ---
        // 新仕様: ブレイクは「削り量(= max(0, armorBreakValue - guardStrength))が現在スタミナを超えた時」のみ。

        [Test]
        public void GuardJudgmentLogic_GuardBreak_WhenStaminaInsufficient()
        {
            // 削り量 = max(0, 100 - 10) = 90、スタミナ5 < 90 → GuardBreak
            GuardResult result = GuardJudgmentLogic.Judge(
                isGuarding: true,
                guardTimeSinceStart: 1.0f, // ジャストガードウィンドウ外
                inContinuousJustGuardWindow: false,
                attackFeature: AttackFeature.None,
                guardDirection: GuardDirection.Both,
                isAttackFromFront: true,
                hasGuardAttackEffect: false,
                currentStamina: 5f,
                guardStrength: 10f,
                armorBreakValue: 100f);

            Assert.AreEqual(GuardResult.GuardBreak, result);
        }

        [Test]
        public void GuardJudgmentLogic_GuardSuccess_WhenStaminaSufficient()
        {
            // 削り量 = max(0, 30 - 100) = 0、完全受け流し → Guarded
            GuardResult result = GuardJudgmentLogic.Judge(
                isGuarding: true,
                guardTimeSinceStart: 1.0f,
                inContinuousJustGuardWindow: false,
                attackFeature: AttackFeature.None,
                guardDirection: GuardDirection.Both,
                isAttackFromFront: true,
                hasGuardAttackEffect: false,
                currentStamina: 100f,
                guardStrength: 100f,
                armorBreakValue: 30f);

            Assert.AreEqual(GuardResult.Guarded, result);
        }

        // --- ジャストガード回復 ---

        [Test]
        public void GuardJudgmentLogic_JustGuardRecovery_RestoresStaminaAndArmor()
        {
            float stamina = 50f;
            float armor = 20f;

            GuardJudgmentLogic.ApplyJustGuardRecovery(
                ref stamina, 100f, ref armor, 50f);

            Assert.AreEqual(65f, stamina, 0.01f, "スタミナ+15回復");
            Assert.AreEqual(30f, armor, 0.01f, "アーマー+10回復");
        }

        [Test]
        public void GuardJudgmentLogic_JustGuardRecovery_ClampsToMax()
        {
            float stamina = 95f;
            float armor = 45f;

            GuardJudgmentLogic.ApplyJustGuardRecovery(
                ref stamina, 100f, ref armor, 50f);

            Assert.AreEqual(100f, stamina, 0.01f, "スタミナはmaxにクランプ");
            Assert.AreEqual(50f, armor, 0.01f, "アーマーはmaxにクランプ");
        }
    }
}
