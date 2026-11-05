using System;

namespace Game.Core
{

    /// <summary>
    /// AIテンプレートデータ。既存 CompanionAIConfig を内包。
    /// </summary>
    [Serializable]
    public struct AITemplateData
    {
        public string templateId;
        public string templateName;
        public string description;
        public string authorName;
        public AITemplateCategory category;
        public CompanionAIConfig config;
        public string[] tags;
    }
}
