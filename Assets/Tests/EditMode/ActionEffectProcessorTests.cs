using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class ActionEffectProcessorTests
    {
        // ===== ActionEffect.IsActive テスト =====

        [Test]
        public void ActionEffect_IsActive_WithinWindow_ReturnsTrue()
        {
            ActionEffect effect = new ActionEffect
            {
                type = ActionEffectType.Armor,
                startTime = 0.1f,
                duration = 0.3f,
                value = 20f
            };

            Assert.IsTrue(effect.IsActive(0.1f), "Should be active at start time");
            Assert.IsTrue(effect.IsActive(0.2f), "Should be active during window");
            Assert.IsTrue(effect.IsActive(0.39f), "Should be active just before end");
        }

        [Test]
        public void ActionEffect_IsActive_OutsideWindow_ReturnsFalse()
        {
            ActionEffect effect = new ActionEffect
            {
                type = ActionEffectType.Armor,
                startTime = 0.1f,
                duration = 0.3f,
                value = 20f
            };

            Assert.IsFalse(effect.IsActive(0.0f), "Should not be active before start");
            Assert.IsFalse(effect.IsActive(0.09f), "Should not be active just before start");
            Assert.IsFalse(effect.IsActive(0.4f), "Should not be active at end time (exclusive)");
            Assert.IsFalse(effect.IsActive(1.0f), "Should not be active well after end");
        }

        [Test]
        public void ActionEffect_EndTime_ReturnsCorrectValue()
        {
            ActionEffect effect = new ActionEffect
            {
                startTime = 0.2f,
                duration = 0.5f
            };

            Assert.AreEqual(0.7f, effect.EndTime, 0.001f);
        }

        // ===== Evaluate テスト =====

        [Test]
        public void Evaluate_NullEffects_ReturnsDefault()
        {
            ActionEffectProcessor.EffectState state = ActionEffectProcessor.Evaluate(null, 0.5f);

            Assert.IsFalse(state.isInvincible);
            Assert.IsFalse(state.hasSuperArmor);
            Assert.IsFalse(state.hasGuardPoint);
            Assert.AreEqual(0f, state.actionArmorValue);
            Assert.AreEqual(0f, state.damageReduction);
        }

        [Test]
        public void Evaluate_EmptyEffects_ReturnsDefault()
        {
            ActionEffect[] effects = new ActionEffect[0];

            ActionEffectProcessor.EffectState state = ActionEffectProcessor.Evaluate(effects, 0.5f);

            Assert.IsFalse(state.isInvincible);
            Assert.AreEqual(0f, state.actionArmorValue);
        }

        [Test]
        public void Evaluate_ArmorEffect_ReturnsArmorValue()
        {
            ActionEffect[] effects = new ActionEffect[]
            {
                new ActionEffect { type = ActionEffectType.Armor, startTime = 0f, duration = 1f, value = 30f }
            };

            ActionEffectProcessor.EffectState state = ActionEffectProcessor.Evaluate(effects, 0.5f);

            Assert.AreEqual(30f, state.actionArmorValue, 0.001f);
        }

        [Test]
        public void Evaluate_MultipleArmors_Stacks()
        {
            ActionEffect[] effects = new ActionEffect[]
            {
                new ActionEffect { type = ActionEffectType.Armor, startTime = 0f, duration = 1f, value = 20f },
                new ActionEffect { type = ActionEffectType.Armor, startTime = 0.3f, duration = 0.5f, value = 15f }
            };

            ActionEffectProcessor.EffectState state = ActionEffectProcessor.Evaluate(effects, 0.5f);

            Assert.AreEqual(35f, state.actionArmorValue, 0.001f, "Multiple armor effects should stack");
        }

        [Test]
        public void Evaluate_SuperArmor_SetsFlag()
        {
            ActionEffect[] effects = new ActionEffect[]
            {
                new ActionEffect { type = ActionEffectType.SuperArmor, startTime = 0.2f, duration = 0.3f }
            };

            Assert.IsFalse(ActionEffectProcessor.Evaluate(effects, 0.1f).hasSuperArmor, "Before start");
            Assert.IsTrue(ActionEffectProcessor.Evaluate(effects, 0.3f).hasSuperArmor, "During window");
            Assert.IsFalse(ActionEffectProcessor.Evaluate(effects, 0.5f).hasSuperArmor, "After end");
        }

        [Test]
        public void Evaluate_Invincible_SetsFlag()
        {
            ActionEffect[] effects = new ActionEffect[]
            {
                new ActionEffect { type = ActionEffectType.Invincible, startTime = 0.05f, duration = 0.1f }
            };

            ActionEffectProcessor.EffectState state = ActionEffectProcessor.Evaluate(effects, 0.1f);

            Assert.IsTrue(state.isInvincible);
        }

        [Test]
        public void Evaluate_DamageReduction_TakesMax()
        {
            ActionEffect[] effects = new ActionEffect[]
            {
                new ActionEffect { type = ActionEffectType.DamageReduction, startTime = 0f, duration = 1f, value = 0.3f },
                new ActionEffect { type = ActionEffectType.DamageReduction, startTime = 0f, duration = 1f, value = 0.5f }
            };

            ActionEffectProcessor.EffectState state = ActionEffectProcessor.Evaluate(effects, 0.5f);

            Assert.AreEqual(0.5f, state.damageReduction, 0.001f, "Should take maximum reduction");
        }

        [Test]
        public void Evaluate_GuardPoint_SetsFlag()
        {
            ActionEffect[] effects = new ActionEffect[]
            {
                new ActionEffect { type = ActionEffectType.GuardPoint, startTime = 0f, duration = 0.2f }
            };

            Assert.IsTrue(ActionEffectProcessor.Evaluate(effects, 0.1f).hasGuardPoint);
            Assert.IsFalse(ActionEffectProcessor.Evaluate(effects, 0.3f).hasGuardPoint);
        }

        [Test]
        public void Evaluate_MixedEffects_AllActive()
        {
            ActionEffect[] effects = new ActionEffect[]
            {
                new ActionEffect { type = ActionEffectType.Armor, startTime = 0f, duration = 0.5f, value = 25f },
                new ActionEffect { type = ActionEffectType.SuperArmor, startTime = 0.2f, duration = 0.3f },
                new ActionEffect { type = ActionEffectType.DamageReduction, startTime = 0f, duration = 0.5f, value = 0.2f }
            };

            ActionEffectProcessor.EffectState state = ActionEffectProcessor.Evaluate(effects, 0.3f);

            Assert.AreEqual(25f, state.actionArmorValue, 0.001f);
            Assert.IsTrue(state.hasSuperArmor);
            Assert.AreEqual(0.2f, state.damageReduction, 0.001f);
            Assert.IsFalse(state.isInvincible);
        }

        [Test]
        public void Evaluate_SequentialEffects_OnlyActiveOneApplies()
        {
            ActionEffect[] effects = new ActionEffect[]
            {
                new ActionEffect { type = ActionEffectType.Armor, startTime = 0f, duration = 0.3f, value = 10f },
                new ActionEffect { type = ActionEffectType.SuperArmor, startTime = 0.3f, duration = 0.5f }
            };

            // At t=0.2: only armor active
            ActionEffectProcessor.EffectState state1 = ActionEffectProcessor.Evaluate(effects, 0.2f);
            Assert.AreEqual(10f, state1.actionArmorValue, 0.001f);
            Assert.IsFalse(state1.hasSuperArmor);

            // At t=0.5: only super armor active
            ActionEffectProcessor.EffectState state2 = ActionEffectProcessor.Evaluate(effects, 0.5f);
            Assert.AreEqual(0f, state2.actionArmorValue, 0.001f);
            Assert.IsTrue(state2.hasSuperArmor);
        }

        // ===== HasActiveEffect テスト =====

        [Test]
        public void HasActiveEffect_WhenActive_ReturnsTrue()
        {
            ActionEffect[] effects = new ActionEffect[]
            {
                new ActionEffect { type = ActionEffectType.Invincible, startTime = 0.1f, duration = 0.2f }
            };

            Assert.IsTrue(ActionEffectProcessor.HasActiveEffect(effects, 0.15f, ActionEffectType.Invincible));
        }

        [Test]
        public void HasActiveEffect_WhenInactive_ReturnsFalse()
        {
            ActionEffect[] effects = new ActionEffect[]
            {
                new ActionEffect { type = ActionEffectType.Invincible, startTime = 0.1f, duration = 0.2f }
            };

            Assert.IsFalse(ActionEffectProcessor.HasActiveEffect(effects, 0.5f, ActionEffectType.Invincible));
        }

        [Test]
        public void HasActiveEffect_WrongType_ReturnsFalse()
        {
            ActionEffect[] effects = new ActionEffect[]
            {
                new ActionEffect { type = ActionEffectType.Armor, startTime = 0f, duration = 1f, value = 10f }
            };

            Assert.IsFalse(ActionEffectProcessor.HasActiveEffect(effects, 0.5f, ActionEffectType.Invincible));
        }

        [Test]
        public void HasActiveEffect_NullEffects_ReturnsFalse()
        {
            Assert.IsFalse(ActionEffectProcessor.HasActiveEffect(null, 0.5f, ActionEffectType.Armor));
        }

        // ===== GetActiveValue テスト =====

        [Test]
        public void GetActiveValue_SingleEffect_ReturnsValue()
        {
            ActionEffect[] effects = new ActionEffect[]
            {
                new ActionEffect { type = ActionEffectType.Armor, startTime = 0f, duration = 1f, value = 25f }
            };

            float result = ActionEffectProcessor.GetActiveValue(effects, 0.5f, ActionEffectType.Armor);

            Assert.AreEqual(25f, result, 0.001f);
        }

        [Test]
        public void GetActiveValue_MultipleActiveEffects_SumsValues()
        {
            ActionEffect[] effects = new ActionEffect[]
            {
                new ActionEffect { type = ActionEffectType.Armor, startTime = 0f, duration = 1f, value = 10f },
                new ActionEffect { type = ActionEffectType.Armor, startTime = 0.3f, duration = 0.5f, value = 15f },
                new ActionEffect { type = ActionEffectType.SuperArmor, startTime = 0f, duration = 1f }
            };

            float result = ActionEffectProcessor.GetActiveValue(effects, 0.5f, ActionEffectType.Armor);

            Assert.AreEqual(25f, result, 0.001f, "Should sum only matching active effects");
        }

        [Test]
        public void GetActiveValue_NullEffects_ReturnsZero()
        {
            Assert.AreEqual(0f, ActionEffectProcessor.GetActiveValue(null, 0.5f, ActionEffectType.Armor));
        }

        // ===== KnockbackImmunity テスト =====

        [Test]
        public void Evaluate_KnockbackImmunity_SetsFlag()
        {
            ActionEffect[] effects = new ActionEffect[]
            {
                new ActionEffect
                {
                    type = ActionEffectType.KnockbackImmunity,
                    startTime = 0f,
                    duration = 5f,
                    value = 0f
                }
            };

            ActionEffectProcessor.EffectState state =
                ActionEffectProcessor.Evaluate(effects, 1f);

            Assert.IsTrue(state.hasKnockbackImmunity);
        }

        [Test]
        public void Evaluate_KnockbackImmunity_Inactive_FlagFalse()
        {
            ActionEffect[] effects = new ActionEffect[]
            {
                new ActionEffect
                {
                    type = ActionEffectType.KnockbackImmunity,
                    startTime = 2f,
                    duration = 1f,
                    value = 0f
                }
            };

            ActionEffectProcessor.EffectState state =
                ActionEffectProcessor.Evaluate(effects, 0.5f);

            Assert.IsFalse(state.hasKnockbackImmunity);
        }
    }
}
