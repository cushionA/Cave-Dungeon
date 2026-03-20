using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// 状況に応じたAIテンプレートの推薦を行う静的クラス。
    /// ボス戦、多数敵、低HP等の状況を判定し、最適なテンプレートを最大3件返す。
    /// </summary>
    public static class AITemplateSuggester
    {
        private const int k_MaxSuggestions = 3;
        private const float k_LowHpThreshold = 0.3f;
        private const int k_MobCountThreshold = 5;

        /// <summary>
        /// 状況に合ったテンプレートIDを推薦順で返す（最大3件）。
        /// </summary>
        /// <param name="isBossFight">ボス戦かどうか</param>
        /// <param name="playerHpRatio">プレイヤーHP割合（0.0~1.0）</param>
        /// <param name="enemyCount">周辺の敵数</param>
        /// <param name="availableTemplates">利用可能なテンプレート一覧</param>
        /// <returns>推薦テンプレートIDの配列（最大3件）</returns>
        public static string[] SuggestTemplates(
            bool isBossFight,
            float playerHpRatio,
            int enemyCount,
            AITemplateData[] availableTemplates)
        {
            if (availableTemplates == null || availableTemplates.Length == 0)
            {
                return System.Array.Empty<string>();
            }

            // 優先カテゴリを決定
            List<AITemplateCategory> priorityCategories = DeterminePriorityCategories(
                isBossFight, playerHpRatio, enemyCount);

            // 優先カテゴリに一致するテンプレートを収集
            List<string> result = new List<string>();
            CollectByCategories(result, availableTemplates, priorityCategories);

            // 残りを General / Aggressive で埋める
            if (result.Count < k_MaxSuggestions)
            {
                FillWithFallback(result, availableTemplates);
            }

            // 最大3件に切り詰め
            if (result.Count > k_MaxSuggestions)
            {
                result.RemoveRange(k_MaxSuggestions, result.Count - k_MaxSuggestions);
            }

            return result.ToArray();
        }

        private static List<AITemplateCategory> DeterminePriorityCategories(
            bool isBossFight, float playerHpRatio, int enemyCount)
        {
            List<AITemplateCategory> categories = new List<AITemplateCategory>();

            if (isBossFight)
            {
                categories.Add(AITemplateCategory.BossFight);
            }
            else if (enemyCount >= k_MobCountThreshold)
            {
                categories.Add(AITemplateCategory.MobClear);
            }
            else if (playerHpRatio <= k_LowHpThreshold)
            {
                categories.Add(AITemplateCategory.SupportFocus);
                categories.Add(AITemplateCategory.Defensive);
            }

            return categories;
        }

        private static void CollectByCategories(
            List<string> result,
            AITemplateData[] templates,
            List<AITemplateCategory> categories)
        {
            foreach (AITemplateCategory category in categories)
            {
                foreach (AITemplateData template in templates)
                {
                    if (template.category == category && !result.Contains(template.templateId))
                    {
                        result.Add(template.templateId);
                    }
                }
            }
        }

        private static void FillWithFallback(List<string> result, AITemplateData[] templates)
        {
            // General を先に、次に Aggressive で埋める
            AITemplateCategory[] fallbackCategories = new AITemplateCategory[]
            {
                AITemplateCategory.General,
                AITemplateCategory.Aggressive,
            };

            foreach (AITemplateCategory category in fallbackCategories)
            {
                foreach (AITemplateData template in templates)
                {
                    if (result.Count >= k_MaxSuggestions)
                    {
                        return;
                    }

                    if (template.category == category && !result.Contains(template.templateId))
                    {
                        result.Add(template.templateId);
                    }
                }
            }
        }
    }
}
