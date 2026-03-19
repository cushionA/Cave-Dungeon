using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class Common_Section3TypesTests
    {
        // ===== Enum Tests =====

        [Test]
        public void PhaseConditionType_AllValues_AreDefined()
        {
            Assert.AreEqual(0, (int)PhaseConditionType.HpThreshold);
            Assert.AreEqual(1, (int)PhaseConditionType.Timer);
            Assert.AreEqual(2, (int)PhaseConditionType.ActionCount);
            Assert.AreEqual(3, (int)PhaseConditionType.AllAddsDefeated);
            Assert.AreEqual(4, (int)PhaseConditionType.Custom);
        }

        [Test]
        public void SummonType_AllValues_AreDefined()
        {
            Assert.AreEqual(0, (int)SummonType.Combat);
            Assert.AreEqual(1, (int)SummonType.Utility);
            Assert.AreEqual(2, (int)SummonType.Decoy);
        }

        [Test]
        public void ElementalRequirement_AllValues_AreDefined()
        {
            Assert.AreEqual(0, (int)ElementalRequirement.Fire);
            Assert.AreEqual(1, (int)ElementalRequirement.Thunder);
            Assert.AreEqual(2, (int)ElementalRequirement.Light);
            Assert.AreEqual(3, (int)ElementalRequirement.Dark);
            Assert.AreEqual(4, (int)ElementalRequirement.Slash);
            Assert.AreEqual(5, (int)ElementalRequirement.Strike);
            Assert.AreEqual(6, (int)ElementalRequirement.Pierce);
        }

        [Test]
        public void BacktrackRewardType_AllValues_AreDefined()
        {
            Assert.AreEqual(0, (int)BacktrackRewardType.Item);
            Assert.AreEqual(1, (int)BacktrackRewardType.Currency);
            Assert.AreEqual(2, (int)BacktrackRewardType.AbilityOrb);
            Assert.AreEqual(3, (int)BacktrackRewardType.Shortcut);
            Assert.AreEqual(4, (int)BacktrackRewardType.Lore);
        }

        [Test]
        public void ArenaState_AllValues_AreDefined()
        {
            Assert.AreEqual(0, (int)ArenaState.Open);
            Assert.AreEqual(1, (int)ArenaState.Locked);
            Assert.AreEqual(2, (int)ArenaState.Cleared);
        }

        [Test]
        public void GateType_Elemental_IsAdded()
        {
            Assert.AreEqual(3, (int)GateType.Elemental);
        }

        [Test]
        public void MagicType_Summon_IsAdded()
        {
            Assert.AreEqual(3, (int)MagicType.Summon);
        }

        [Test]
        public void StatusEffectId_Confusion_IsAdded()
        {
            Assert.AreEqual(12, (int)StatusEffectId.Confusion);
        }

        // ===== Struct Tests =====

        [Test]
        public void PhaseCondition_Fields_StoreCorrectly()
        {
            PhaseCondition condition = new PhaseCondition
            {
                type = PhaseConditionType.HpThreshold,
                threshold = 0.5f
            };

            Assert.AreEqual(PhaseConditionType.HpThreshold, condition.type);
            Assert.AreEqual(0.5f, condition.threshold, 0.001f);
        }

        [Test]
        public void SummonSlot_Fields_StoreCorrectly()
        {
            SummonSlot slot = new SummonSlot
            {
                summonHash = 12345,
                remainingTime = 30.0f,
                summonType = SummonType.Combat
            };

            Assert.AreEqual(12345, slot.summonHash);
            Assert.AreEqual(30.0f, slot.remainingTime, 0.001f);
            Assert.AreEqual(SummonType.Combat, slot.summonType);
        }

        [Test]
        public void BacktrackEntry_Fields_StoreCorrectly()
        {
            BacktrackEntry entry = new BacktrackEntry
            {
                rewardId = "reward_01",
                rewardType = BacktrackRewardType.AbilityOrb,
                requiredAbility = AbilityFlag.WallKick,
                locationHint = "壁蹴りで到達できる高台",
                collected = false
            };

            Assert.AreEqual("reward_01", entry.rewardId);
            Assert.AreEqual(BacktrackRewardType.AbilityOrb, entry.rewardType);
            Assert.AreEqual(AbilityFlag.WallKick, entry.requiredAbility);
            Assert.AreEqual("壁蹴りで到達できる高台", entry.locationHint);
            Assert.IsFalse(entry.collected);
        }

        [Test]
        public void ElementalGateRequirement_Fields_StoreCorrectly()
        {
            ElementalGateRequirement req = new ElementalGateRequirement
            {
                element = ElementalRequirement.Fire,
                minDamage = 10.0f
            };

            Assert.AreEqual(ElementalRequirement.Fire, req.element);
            Assert.AreEqual(10.0f, req.minDamage, 0.001f);
        }

        // ===== PartyManager Tests =====

        [Test]
        public void PartyManager_Constants_AreCorrect()
        {
            Assert.AreEqual(4, PartyManager.k_MaxPartySize);
            Assert.AreEqual(2, PartyManager.k_MaxSummonSlots);
            Assert.AreEqual(3, PartyManager.k_MaxConfusedEnemies);
        }

        // ===== ElementalRequirement → Element Mapping Tests =====

        [Test]
        public void ElementalRequirementMapper_Fire_ReturnsFireElement()
        {
            Element result = ElementalRequirementMapper.ToElement(ElementalRequirement.Fire);
            Assert.AreEqual(Element.Fire, result);
        }

        [Test]
        public void ElementalRequirementMapper_AllMappings_AreCorrect()
        {
            Assert.AreEqual(Element.Fire, ElementalRequirementMapper.ToElement(ElementalRequirement.Fire));
            Assert.AreEqual(Element.Thunder, ElementalRequirementMapper.ToElement(ElementalRequirement.Thunder));
            Assert.AreEqual(Element.Light, ElementalRequirementMapper.ToElement(ElementalRequirement.Light));
            Assert.AreEqual(Element.Dark, ElementalRequirementMapper.ToElement(ElementalRequirement.Dark));
            Assert.AreEqual(Element.Slash, ElementalRequirementMapper.ToElement(ElementalRequirement.Slash));
            Assert.AreEqual(Element.Strike, ElementalRequirementMapper.ToElement(ElementalRequirement.Strike));
            Assert.AreEqual(Element.Pierce, ElementalRequirementMapper.ToElement(ElementalRequirement.Pierce));
        }

        [Test]
        public void ElementalRequirementMapper_MatchesElement_ReturnsTrueForMatchingFlags()
        {
            Element attack = Element.Fire | Element.Slash;

            Assert.IsTrue(ElementalRequirementMapper.MatchesElement(ElementalRequirement.Fire, attack));
            Assert.IsTrue(ElementalRequirementMapper.MatchesElement(ElementalRequirement.Slash, attack));
            Assert.IsFalse(ElementalRequirementMapper.MatchesElement(ElementalRequirement.Thunder, attack));
        }

        // ===== ConfusionState Tests =====

        [Test]
        public void ConfusionState_Fields_StoreCorrectly()
        {
            ConfusionState state = new ConfusionState
            {
                targetHash = 100,
                controllerHash = 200,
                remainingDuration = 15.0f,
                originalBelong = CharacterBelong.Enemy,
                accumulatedDamage = 0.0f
            };

            Assert.AreEqual(100, state.targetHash);
            Assert.AreEqual(200, state.controllerHash);
            Assert.AreEqual(15.0f, state.remainingDuration, 0.001f);
            Assert.AreEqual(CharacterBelong.Enemy, state.originalBelong);
            Assert.AreEqual(0.0f, state.accumulatedDamage, 0.001f);
        }

        // ===== BossPhaseData Tests =====

        [Test]
        public void BossPhaseData_Fields_StoreCorrectly()
        {
            BossPhaseData data = new BossPhaseData
            {
                phaseName = "第1形態",
                exitCondition = new PhaseCondition
                {
                    type = PhaseConditionType.HpThreshold,
                    threshold = 0.5f
                },
                transitionInvincibleTime = 2.0f,
                spawnAdds = true,
                addSpawnerIds = new string[] { "spawner_01", "spawner_02" }
            };

            Assert.AreEqual("第1形態", data.phaseName);
            Assert.AreEqual(PhaseConditionType.HpThreshold, data.exitCondition.type);
            Assert.AreEqual(0.5f, data.exitCondition.threshold, 0.001f);
            Assert.AreEqual(2.0f, data.transitionInvincibleTime, 0.001f);
            Assert.IsTrue(data.spawnAdds);
            Assert.AreEqual(2, data.addSpawnerIds.Length);
        }
    }
}
