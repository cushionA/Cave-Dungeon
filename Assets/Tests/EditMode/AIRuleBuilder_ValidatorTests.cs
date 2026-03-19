using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class AIRuleBuilder_ValidatorTests
    {
        [Test]
        public void Validator_NoModes_ReturnsWarning()
        {
            RuleValidator validator = new RuleValidator();
            CompanionAIConfig config = new CompanionAIConfig();

            List<ValidationWarning> warnings = validator.Validate(config);

            Assert.Greater(warnings.Count, 0);
        }

        [Test]
        public void Validator_InvalidDefaultAction_ReturnsWarning()
        {
            RuleValidator validator = new RuleValidator();
            CompanionAIConfig config = new CompanionAIConfig
            {
                modes = new AIMode[]
                {
                    new AIMode
                    {
                        modeName = "Test",
                        actions = new ActionSlot[]
                        {
                            new ActionSlot { execType = ActionExecType.Attack }
                        },
                        defaultActionIndex = 5
                    }
                }
            };

            List<ValidationWarning> warnings = validator.Validate(config);

            Assert.Greater(warnings.Count, 0);
        }

        [Test]
        public void Validator_ValidConfig_NoWarnings()
        {
            RuleValidator validator = new RuleValidator();
            CompanionAIConfig config = new CompanionAIConfig
            {
                modes = new AIMode[]
                {
                    new AIMode
                    {
                        modeName = "Combat",
                        actions = new ActionSlot[]
                        {
                            new ActionSlot { execType = ActionExecType.Attack }
                        },
                        defaultActionIndex = 0,
                        judgeInterval = Vector2.one
                    }
                }
            };

            List<ValidationWarning> warnings = validator.Validate(config);

            Assert.AreEqual(0, warnings.Count);
        }
    }
}
