using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// CompanionAISettings UI 支援クラス群の単体テスト。
    /// - SustainedActionMetadata: 継続時間ラベル・自然終了条件テーブル
    /// - ActionSlotLabelTable: enum → 表示ラベル変換
    /// - ConditionTypeMetadata: 条件タイプ → UI widget種別/既定値
    /// - CompanionAISettingsLogic: Mode/Transition の新規 API
    /// </summary>
    [TestFixture]
    public class CompanionAISettings_MetadataTests
    {
        // =========================================================================
        // SustainedActionMetadata
        // =========================================================================

        [Test]
        public void SustainedActionMetadata_GetDurationLabel_ZeroValue_Unlimited()
        {
            string label = SustainedActionMetadata.GetDurationLabel(0f);
            StringAssert.Contains("無制限", label);
        }

        [Test]
        public void SustainedActionMetadata_GetDurationLabel_BelowThreshold_Unlimited()
        {
            // k_UnlimitedDurationThreshold (0.01f) ぎりぎり以下は「無制限」
            string label = SustainedActionMetadata.GetDurationLabel(0.005f);
            StringAssert.Contains("無制限", label);
        }

        [Test]
        public void SustainedActionMetadata_GetDurationLabel_AboveThreshold_ShowsSeconds()
        {
            string label = SustainedActionMetadata.GetDurationLabel(3.5f);
            StringAssert.Contains("3.5", label);
            StringAssert.Contains("秒", label);
        }

        [Test]
        public void SustainedActionMetadata_IsUnlimited_ZeroAndBelow_ReturnsTrue()
        {
            Assert.IsTrue(SustainedActionMetadata.IsUnlimited(0f));
            Assert.IsTrue(SustainedActionMetadata.IsUnlimited(0.005f));
        }

        [Test]
        public void SustainedActionMetadata_IsUnlimited_AboveThreshold_ReturnsFalse()
        {
            Assert.IsFalse(SustainedActionMetadata.IsUnlimited(0.02f));
            Assert.IsFalse(SustainedActionMetadata.IsUnlimited(10f));
        }

        [Test]
        public void SustainedActionMetadata_GetNaturalEndCondition_AllValuesReturnNonEmpty()
        {
            // 全 SustainedAction について空文字を返さないこと
            foreach (SustainedAction action in System.Enum.GetValues(typeof(SustainedAction)))
            {
                string result = SustainedActionMetadata.GetNaturalEndCondition(action);
                Assert.IsFalse(string.IsNullOrEmpty(result),
                    $"SustainedAction.{action} は自然終了条件の説明を返すべき");
            }
        }

        // =========================================================================
        // ActionSlotLabelTable
        // =========================================================================

        [Test]
        public void ActionSlotLabelTable_GetInstantActionLabel_AllValuesReturnNonEmpty()
        {
            foreach (InstantAction action in System.Enum.GetValues(typeof(InstantAction)))
            {
                string label = ActionSlotLabelTable.GetInstantActionLabel(action);
                Assert.IsFalse(string.IsNullOrEmpty(label),
                    $"InstantAction.{action} はラベルを返すべき");
            }
        }

        [Test]
        public void ActionSlotLabelTable_GetSustainedActionLabel_AllValuesReturnNonEmpty()
        {
            foreach (SustainedAction action in System.Enum.GetValues(typeof(SustainedAction)))
            {
                string label = ActionSlotLabelTable.GetSustainedActionLabel(action);
                Assert.IsFalse(string.IsNullOrEmpty(label),
                    $"SustainedAction.{action} はラベルを返すべき");
            }
        }

        [Test]
        public void ActionSlotLabelTable_GetBroadcastActionLabel_AllValuesReturnNonEmpty()
        {
            foreach (BroadcastAction action in System.Enum.GetValues(typeof(BroadcastAction)))
            {
                string label = ActionSlotLabelTable.GetBroadcastActionLabel(action);
                Assert.IsFalse(string.IsNullOrEmpty(label),
                    $"BroadcastAction.{action} はラベルを返すべき");
            }
        }

        [Test]
        public void ActionSlotLabelTable_GetAttackCategoryLabel_AllValuesReturnNonEmpty()
        {
            foreach (AttackCategory cat in System.Enum.GetValues(typeof(AttackCategory)))
            {
                string label = ActionSlotLabelTable.GetAttackCategoryLabel(cat);
                Assert.IsFalse(string.IsNullOrEmpty(label),
                    $"AttackCategory.{cat} はラベルを返すべき");
            }
        }

        [Test]
        public void ActionSlotLabelTable_GetExecTypeLabel_AllValuesReturnNonEmpty()
        {
            foreach (ActionExecType t in System.Enum.GetValues(typeof(ActionExecType)))
            {
                string label = ActionSlotLabelTable.GetExecTypeLabel(t);
                Assert.IsFalse(string.IsNullOrEmpty(label),
                    $"ActionExecType.{t} はラベルを返すべき");
            }
        }

        [Test]
        public void ActionSlotLabelTable_GetFallbackLabel_WithDisplayName_ReturnsDisplayName()
        {
            ActionSlot slot = new ActionSlot
            {
                execType = ActionExecType.Attack,
                paramId = 0,
                displayName = "特殊攻撃A",
            };
            string label = ActionSlotLabelTable.GetFallbackLabel(slot);
            Assert.AreEqual("特殊攻撃A", label);
        }

        [Test]
        public void ActionSlotLabelTable_GetFallbackLabel_EmptyDisplayName_AttackFormat()
        {
            ActionSlot slot = new ActionSlot
            {
                execType = ActionExecType.Attack,
                paramId = 5,
                displayName = "",
            };
            string label = ActionSlotLabelTable.GetFallbackLabel(slot);
            StringAssert.Contains("攻撃", label);
            StringAssert.Contains("5", label);
        }

        [Test]
        public void ActionSlotLabelTable_GetFallbackLabel_EmptyDisplayName_InstantUsesEnumLabel()
        {
            ActionSlot slot = new ActionSlot
            {
                execType = ActionExecType.Instant,
                paramId = (int)InstantAction.Dodge,
                displayName = "",
            };
            string label = ActionSlotLabelTable.GetFallbackLabel(slot);
            Assert.AreEqual(ActionSlotLabelTable.GetInstantActionLabel(InstantAction.Dodge), label);
        }

        // =========================================================================
        // ConditionTypeMetadata
        // =========================================================================

        [Test]
        public void ConditionTypeMetadata_GetWidgetKind_RatioTypes_ReturnsRatio()
        {
            Assert.AreEqual(ConditionTypeMetadata.WidgetKind.Ratio,
                ConditionTypeMetadata.GetWidgetKind(AIConditionType.HpRatio));
            Assert.AreEqual(ConditionTypeMetadata.WidgetKind.Ratio,
                ConditionTypeMetadata.GetWidgetKind(AIConditionType.MpRatio));
            Assert.AreEqual(ConditionTypeMetadata.WidgetKind.Ratio,
                ConditionTypeMetadata.GetWidgetKind(AIConditionType.StaminaRatio));
            Assert.AreEqual(ConditionTypeMetadata.WidgetKind.Ratio,
                ConditionTypeMetadata.GetWidgetKind(AIConditionType.ArmorRatio));
        }

        [Test]
        public void ConditionTypeMetadata_GetWidgetKind_IntegerTypes_ReturnsInteger()
        {
            Assert.AreEqual(ConditionTypeMetadata.WidgetKind.Integer,
                ConditionTypeMetadata.GetWidgetKind(AIConditionType.Count));
            Assert.AreEqual(ConditionTypeMetadata.WidgetKind.Integer,
                ConditionTypeMetadata.GetWidgetKind(AIConditionType.Distance));
            Assert.AreEqual(ConditionTypeMetadata.WidgetKind.Integer,
                ConditionTypeMetadata.GetWidgetKind(AIConditionType.DamageScore));
        }

        [Test]
        public void ConditionTypeMetadata_GetWidgetKind_NearbyFaction_ReturnsFactionFlags()
        {
            Assert.AreEqual(ConditionTypeMetadata.WidgetKind.FactionFlags,
                ConditionTypeMetadata.GetWidgetKind(AIConditionType.NearbyFaction));
        }

        [Test]
        public void ConditionTypeMetadata_GetWidgetKind_BitmaskTypes_ReturnsIntegerBitmask()
        {
            Assert.AreEqual(ConditionTypeMetadata.WidgetKind.IntegerBitmask,
                ConditionTypeMetadata.GetWidgetKind(AIConditionType.ObjectNearby));
            Assert.AreEqual(ConditionTypeMetadata.WidgetKind.IntegerBitmask,
                ConditionTypeMetadata.GetWidgetKind(AIConditionType.EventFired));
        }

        [Test]
        public void ConditionTypeMetadata_GetWidgetKind_ProjectileNear_ReturnsInteger()
        {
            // ConditionEvaluator のコメント通り、operandA は閾値距離として使う（Integer）。
            // ビットマスク扱いは実装と食い違うので Integer に矯正されている。
            Assert.AreEqual(ConditionTypeMetadata.WidgetKind.Integer,
                ConditionTypeMetadata.GetWidgetKind(AIConditionType.ProjectileNear));
        }

        [Test]
        public void ConditionTypeMetadata_GetDefaultOperandA_ProjectileNear_Returns3m()
        {
            Assert.AreEqual(3, ConditionTypeMetadata.GetDefaultOperandA(AIConditionType.ProjectileNear));
        }

        [Test]
        public void ConditionTypeMetadata_GetWidgetKind_SelfActState_ReturnsEnumSelect()
        {
            // ActState は非 [Flags] 連番 enum のため単一選択+Equal比較で評価させる必要がある
            Assert.AreEqual(ConditionTypeMetadata.WidgetKind.EnumSelect,
                ConditionTypeMetadata.GetWidgetKind(AIConditionType.SelfActState));
        }

        [Test]
        public void ConditionTypeMetadata_IsBitmask_SelfActState_ReturnsFalse()
        {
            // EnumSelect はビットマスクではない
            Assert.IsFalse(ConditionTypeMetadata.IsBitmask(AIConditionType.SelfActState));
        }

        [Test]
        public void ConditionTypeMetadata_GetDefaultCompareOp_SelfActState_ReturnsEqual()
        {
            // 評価式 (Compare で Equal→Mathf.Approximately) と噛み合わせるため Equal が既定
            Assert.AreEqual(CompareOp.Equal,
                ConditionTypeMetadata.GetDefaultCompareOp(AIConditionType.SelfActState));
        }

        [Test]
        public void ConditionTypeMetadata_GetDefaultCompareOp_Ratio_ReturnsLessEqual()
        {
            Assert.AreEqual(CompareOp.LessEqual,
                ConditionTypeMetadata.GetDefaultCompareOp(AIConditionType.HpRatio));
            Assert.AreEqual(CompareOp.LessEqual,
                ConditionTypeMetadata.GetDefaultCompareOp(AIConditionType.Distance));
        }

        [Test]
        public void ConditionTypeMetadata_GetDefaultCompareOp_Bitmask_ReturnsHasAny()
        {
            Assert.AreEqual(CompareOp.HasAny,
                ConditionTypeMetadata.GetDefaultCompareOp(AIConditionType.ObjectNearby));
            Assert.AreEqual(CompareOp.HasAny,
                ConditionTypeMetadata.GetDefaultCompareOp(AIConditionType.NearbyFaction));
        }

        [Test]
        public void ConditionTypeMetadata_GetBitLabels_SelfActState_ReturnsEmpty()
        {
            // SelfActState は EnumSelect に移行したのでビットラベルは返さない
            string[] labels = ConditionTypeMetadata.GetBitLabels(AIConditionType.SelfActState);
            Assert.IsNotNull(labels);
            Assert.AreEqual(0, labels.Length);
        }

        [Test]
        public void ConditionTypeMetadata_GetWidgetKind_None_ReturnsNone()
        {
            Assert.AreEqual(ConditionTypeMetadata.WidgetKind.None,
                ConditionTypeMetadata.GetWidgetKind(AIConditionType.None));
        }

        [Test]
        public void ConditionTypeMetadata_GetLabel_AllValuesReturnNonEmpty()
        {
            foreach (AIConditionType t in System.Enum.GetValues(typeof(AIConditionType)))
            {
                string label = ConditionTypeMetadata.GetLabel(t);
                Assert.IsFalse(string.IsNullOrEmpty(label),
                    $"AIConditionType.{t} はラベルを返すべき");
            }
        }

        [Test]
        public void ConditionTypeMetadata_GetDescription_AllValuesReturnString()
        {
            foreach (AIConditionType t in System.Enum.GetValues(typeof(AIConditionType)))
            {
                string desc = ConditionTypeMetadata.GetDescription(t);
                Assert.IsNotNull(desc, $"AIConditionType.{t} の説明文は null を返してはいけない");
            }
        }

        [Test]
        public void ConditionTypeMetadata_GetDefaultOperandA_RatioTypes_Returns50()
        {
            Assert.AreEqual(50, ConditionTypeMetadata.GetDefaultOperandA(AIConditionType.HpRatio));
            Assert.AreEqual(50, ConditionTypeMetadata.GetDefaultOperandA(AIConditionType.MpRatio));
            Assert.AreEqual(50, ConditionTypeMetadata.GetDefaultOperandA(AIConditionType.StaminaRatio));
            Assert.AreEqual(50, ConditionTypeMetadata.GetDefaultOperandA(AIConditionType.ArmorRatio));
        }

        [Test]
        public void ConditionTypeMetadata_GetDefaultOperandA_Count_Returns1()
        {
            Assert.AreEqual(1, ConditionTypeMetadata.GetDefaultOperandA(AIConditionType.Count));
        }

        [Test]
        public void ConditionTypeMetadata_GetDefaultOperandA_Distance_Returns5()
        {
            Assert.AreEqual(5, ConditionTypeMetadata.GetDefaultOperandA(AIConditionType.Distance));
        }

        [Test]
        public void ConditionTypeMetadata_GetDefaultOperandA_DamageScore_Returns100()
        {
            Assert.AreEqual(100, ConditionTypeMetadata.GetDefaultOperandA(AIConditionType.DamageScore));
        }

        [Test]
        public void ConditionTypeMetadata_SupportsNumericCompare_RatioAndInteger_True()
        {
            Assert.IsTrue(ConditionTypeMetadata.SupportsNumericCompare(AIConditionType.HpRatio));
            Assert.IsTrue(ConditionTypeMetadata.SupportsNumericCompare(AIConditionType.Count));
            Assert.IsTrue(ConditionTypeMetadata.SupportsNumericCompare(AIConditionType.Distance));
        }

        [Test]
        public void ConditionTypeMetadata_SupportsNumericCompare_BitmaskAndNone_False()
        {
            Assert.IsFalse(ConditionTypeMetadata.SupportsNumericCompare(AIConditionType.NearbyFaction));
            Assert.IsFalse(ConditionTypeMetadata.SupportsNumericCompare(AIConditionType.ObjectNearby));
            Assert.IsFalse(ConditionTypeMetadata.SupportsNumericCompare(AIConditionType.None));
        }

        [Test]
        public void ConditionTypeMetadata_IsBitmask_FlagsAndBitmask_True()
        {
            Assert.IsTrue(ConditionTypeMetadata.IsBitmask(AIConditionType.NearbyFaction));
            Assert.IsTrue(ConditionTypeMetadata.IsBitmask(AIConditionType.ObjectNearby));
            Assert.IsTrue(ConditionTypeMetadata.IsBitmask(AIConditionType.EventFired));
            // SelfActState は EnumSelect に移行したため bitmask ではない（別テストで検証）
        }

        [Test]
        public void ConditionTypeMetadata_IsBitmask_NumericAndNone_False()
        {
            Assert.IsFalse(ConditionTypeMetadata.IsBitmask(AIConditionType.HpRatio));
            Assert.IsFalse(ConditionTypeMetadata.IsBitmask(AIConditionType.Count));
            Assert.IsFalse(ConditionTypeMetadata.IsBitmask(AIConditionType.None));
        }

        // =========================================================================
        // CompanionAISettingsLogic - UpdateModeInBuffer / SetTransitionRulesInBuffer
        // =========================================================================

        private ModePresetRegistry _modeRegistry;
        private TacticalPresetRegistry _tacticalRegistry;
        private CompanionAISettingsLogic _logic;

        [SetUp]
        public void SetUp()
        {
            _modeRegistry = new ModePresetRegistry();
            _tacticalRegistry = new TacticalPresetRegistry(_modeRegistry);
            _logic = new CompanionAISettingsLogic(_modeRegistry, _tacticalRegistry);
        }

        [TearDown]
        public void TearDown()
        {
            _tacticalRegistry?.Dispose();
        }

        [Test]
        public void CompanionAISettingsLogic_UpdateModeInBuffer_ValidIndex_UpdatesAndMarksDirty()
        {
            _logic.AddModeToBuffer(new AIMode { modeName = "元" });

            AIMode updated = new AIMode { modeName = "更新後", defaultActionIndex = 2 };
            bool ok = _logic.UpdateModeInBuffer(0, updated);

            Assert.IsTrue(ok);
            Assert.AreEqual("更新後", _logic.EditingBuffer.modes[0].modeName);
            Assert.AreEqual(2, _logic.EditingBuffer.modes[0].defaultActionIndex);
            Assert.IsTrue(_logic.IsDirty);
        }

        [Test]
        public void CompanionAISettingsLogic_UpdateModeInBuffer_InvalidIndex_Rejected()
        {
            _logic.AddModeToBuffer(new AIMode { modeName = "A" });

            Assert.IsFalse(_logic.UpdateModeInBuffer(-1, new AIMode()));
            Assert.IsFalse(_logic.UpdateModeInBuffer(1, new AIMode()));
            Assert.IsFalse(_logic.UpdateModeInBuffer(100, new AIMode()));
        }

        [Test]
        public void CompanionAISettingsLogic_UpdateModeInBuffer_EmptyBuffer_Rejected()
        {
            bool ok = _logic.UpdateModeInBuffer(0, new AIMode { modeName = "X" });
            Assert.IsFalse(ok);
        }

        [Test]
        public void CompanionAISettingsLogic_SetTransitionRulesInBuffer_AssignsAndMarksDirty()
        {
            ModeTransitionRule[] rules = new ModeTransitionRule[]
            {
                new ModeTransitionRule
                {
                    sourceModeIndex = -1,
                    targetModeIndex = 0,
                    conditions = new AICondition[0],
                },
                new ModeTransitionRule
                {
                    sourceModeIndex = 0,
                    targetModeIndex = 1,
                    conditions = new AICondition[0],
                },
            };

            _logic.SetTransitionRulesInBuffer(rules);

            Assert.IsNotNull(_logic.EditingBuffer.modeTransitionRules);
            Assert.AreEqual(2, _logic.EditingBuffer.modeTransitionRules.Length);
            Assert.AreEqual(-1, _logic.EditingBuffer.modeTransitionRules[0].sourceModeIndex);
            Assert.AreEqual(1, _logic.EditingBuffer.modeTransitionRules[1].targetModeIndex);
            Assert.IsTrue(_logic.IsDirty);
        }

        [Test]
        public void CompanionAISettingsLogic_SetTransitionRulesInBuffer_Null_AssignsEmptyArray()
        {
            _logic.SetTransitionRulesInBuffer(null);

            Assert.IsNotNull(_logic.EditingBuffer.modeTransitionRules);
            Assert.AreEqual(0, _logic.EditingBuffer.modeTransitionRules.Length);
            Assert.IsTrue(_logic.IsDirty);
        }

        // =========================================================================
        // CompareOp 正規化テスト
        // （UI 側の BuildConditionInputWidgets.NormalizeCompareOp の挙動を、
        //   GetDefaultCompareOp の返値で間接的に検証する）
        // =========================================================================

        [Test]
        public void ConditionTypeMetadata_GetDefaultCompareOp_Integer_ReturnsLessEqual()
        {
            Assert.AreEqual(CompareOp.LessEqual,
                ConditionTypeMetadata.GetDefaultCompareOp(AIConditionType.Count));
            Assert.AreEqual(CompareOp.LessEqual,
                ConditionTypeMetadata.GetDefaultCompareOp(AIConditionType.DamageScore));
        }

        [Test]
        public void ConditionTypeMetadata_GetDefaultCompareOp_FactionFlags_ReturnsHasAny()
        {
            Assert.AreEqual(CompareOp.HasAny,
                ConditionTypeMetadata.GetDefaultCompareOp(AIConditionType.NearbyFaction));
        }

        [Test]
        public void ConditionTypeMetadata_GetDefaultCompareOp_None_ReturnsEqual()
        {
            // None は追加入力なしだが、デフォルトは安全側の Equal
            Assert.AreEqual(CompareOp.Equal,
                ConditionTypeMetadata.GetDefaultCompareOp(AIConditionType.None));
        }
    }
}
