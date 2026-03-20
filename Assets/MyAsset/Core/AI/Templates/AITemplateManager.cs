using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// AIテンプレートの保存・読込・一覧・削除を管理する純ロジッククラス。
    /// ISaveable を実装し、セーブ/ロードに対応する。
    /// テンプレートの適用・元に戻す機能も提供する。
    /// </summary>
    public class AITemplateManager : ISaveable
    {
        public string SaveId => "AITemplateManager";

        private const int k_MaxTemplates = 30;
        private const string k_SystemAuthorName = "System";

        private readonly Dictionary<string, AITemplateData> _templates
            = new Dictionary<string, AITemplateData>();

        private readonly Dictionary<int, CompanionAIConfig> _appliedConfigs
            = new Dictionary<int, CompanionAIConfig>();

        /// <summary>
        /// テンプレートを保存する。
        /// templateId が重複していたら上書き。上限超過時は false を返す。
        /// </summary>
        public bool SaveTemplate(AITemplateData template)
        {
            bool isOverwrite = _templates.ContainsKey(template.templateId);

            if (!isOverwrite && _templates.Count >= k_MaxTemplates)
            {
                return false;
            }

            _templates[template.templateId] = template;
            return true;
        }

        /// <summary>
        /// テンプレート一覧を取得する。
        /// category が null なら全件、指定ありならフィルタして返す。
        /// </summary>
        public AITemplateData[] GetTemplates(AITemplateCategory? category = null)
        {
            if (!category.HasValue)
            {
                AITemplateData[] all = new AITemplateData[_templates.Count];
                _templates.Values.CopyTo(all, 0);
                return all;
            }

            List<AITemplateData> filtered = new List<AITemplateData>();
            foreach (KeyValuePair<string, AITemplateData> kvp in _templates)
            {
                if (kvp.Value.category == category.Value)
                {
                    filtered.Add(kvp.Value);
                }
            }
            return filtered.ToArray();
        }

        /// <summary>
        /// 指定IDのテンプレートを取得する。見つからなければ null を返す。
        /// </summary>
        public AITemplateData? GetTemplate(string templateId)
        {
            if (_templates.TryGetValue(templateId, out AITemplateData template))
            {
                return template;
            }
            return null;
        }

        /// <summary>
        /// テンプレートを削除する。
        /// authorName が "System" のテンプレートは削除不可（false 返却）。
        /// 存在しないIDの場合も false を返す。
        /// </summary>
        public bool DeleteTemplate(string templateId)
        {
            if (!_templates.TryGetValue(templateId, out AITemplateData template))
            {
                return false;
            }

            if (template.authorName == k_SystemAuthorName)
            {
                return false;
            }

            _templates.Remove(templateId);
            return true;
        }

        /// <summary>
        /// テンプレートを仲間に適用する。
        /// 適用前の currentConfig を保存し、Revert で復元できるようにする。
        /// templateId が見つからなければ false を返す。
        /// </summary>
        public bool ApplyTemplate(string templateId, int companionHash, CompanionAIConfig currentConfig)
        {
            if (!_templates.ContainsKey(templateId))
            {
                return false;
            }

            _appliedConfigs[companionHash] = currentConfig;
            return true;
        }

        /// <summary>
        /// 適用前の設定に戻す。
        /// 適用済みでなければ false を返し、previousConfig は default になる。
        /// 成功時はエントリを削除する。
        /// </summary>
        public bool RevertTemplate(int companionHash, out CompanionAIConfig previousConfig)
        {
            if (!_appliedConfigs.TryGetValue(companionHash, out previousConfig))
            {
                previousConfig = default;
                return false;
            }

            _appliedConfigs.Remove(companionHash);
            return true;
        }

        /// <summary>
        /// 指定の仲間にテンプレートが適用中かどうかを返す。
        /// </summary>
        public bool HasAppliedTemplate(int companionHash)
        {
            return _appliedConfigs.ContainsKey(companionHash);
        }

        /// <summary>
        /// 全テンプレートを List&lt;AITemplateData&gt; として返す。
        /// </summary>
        public object Serialize()
        {
            List<AITemplateData> list = new List<AITemplateData>(_templates.Values);
            return list;
        }

        /// <summary>
        /// List&lt;AITemplateData&gt; からテンプレートを復元する。
        /// 既存データはクリアされる。
        /// </summary>
        public void Deserialize(object data)
        {
            _templates.Clear();

            List<AITemplateData> list = data as List<AITemplateData>;
            if (list == null)
            {
                return;
            }

            foreach (AITemplateData template in list)
            {
                _templates[template.templateId] = template;
            }
        }
    }
}
