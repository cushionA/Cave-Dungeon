using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// A warning produced by the rule validator with a message and the affected mode index.
    /// </summary>
    public struct ValidationWarning
    {
        public string message;
        public int modeIndex;
    }

    /// <summary>
    /// Validates a CompanionAIConfig for common configuration errors.
    /// Returns a list of warnings for missing modes, empty action lists,
    /// invalid default action indices, and out-of-range rule references.
    /// </summary>
    public class RuleValidator
    {
        /// <summary>
        /// Validates the given config and returns all warnings found.
        /// </summary>
        public List<ValidationWarning> Validate(CompanionAIConfig config)
        {
            List<ValidationWarning> warnings = new List<ValidationWarning>();

            if (config.modes == null || config.modes.Length == 0)
            {
                warnings.Add(new ValidationWarning
                {
                    message = "モードが1つも定義されていません",
                    modeIndex = -1
                });
                return warnings;
            }

            for (int i = 0; i < config.modes.Length; i++)
            {
                AIMode mode = config.modes[i];

                if (mode.actions == null || mode.actions.Length == 0)
                {
                    warnings.Add(new ValidationWarning
                    {
                        message = $"モード '{mode.modeName}' にアクションが未定義",
                        modeIndex = i
                    });
                    continue;
                }

                if (mode.defaultActionIndex < 0 || mode.defaultActionIndex >= mode.actions.Length)
                {
                    warnings.Add(new ValidationWarning
                    {
                        message = $"モード '{mode.modeName}' のデフォルトアクションが無効",
                        modeIndex = i
                    });
                }

                if (mode.actionRules != null)
                {
                    for (int j = 0; j < mode.actionRules.Length; j++)
                    {
                        AIRule rule = mode.actionRules[j];
                        if (rule.actionIndex < 0 || rule.actionIndex >= mode.actions.Length)
                        {
                            warnings.Add(new ValidationWarning
                            {
                                message = $"モード '{mode.modeName}' のルール{j}が無効なアクションインデックスを参照",
                                modeIndex = i
                            });
                        }
                    }
                }
            }

            return warnings;
        }
    }
}
