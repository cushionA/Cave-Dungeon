namespace Game.Core
{
    /// <summary>
    /// 属性ゲートのヒントテキスト生成ユーティリティ。
    /// ElementalRequirementに応じたデフォルトヒントと解決テキストを提供する。
    /// </summary>
    public static class ElementalGateHintProvider
    {
        /// <summary>
        /// 属性ゲートのヒントテキストを取得する。
        /// </summary>
        public static string GetHint(ElementalRequirement requirement)
        {
            switch (requirement)
            {
                case ElementalRequirement.Fire:    return "炎で溶かせそうだ";
                case ElementalRequirement.Thunder: return "雷で起動できそうだ";
                case ElementalRequirement.Light:   return "聖なる力で浄化できそうだ";
                case ElementalRequirement.Dark:    return "闇の力で通れそうだ";
                case ElementalRequirement.Slash:   return "斬撃で切断できそうだ";
                case ElementalRequirement.Strike:  return "打撃で破壊できそうだ";
                case ElementalRequirement.Pierce:  return "刺突で穿てそうだ";
                default:                           return "";
            }
        }

        /// <summary>
        /// 属性ゲートの解決テキストを取得する。
        /// </summary>
        public static string GetSolvedText(ElementalRequirement requirement)
        {
            switch (requirement)
            {
                case ElementalRequirement.Fire:    return "氷壁が溶けた！";
                case ElementalRequirement.Thunder: return "機械が起動した！";
                case ElementalRequirement.Light:   return "闇が浄化された！";
                case ElementalRequirement.Dark:    return "暗幕が張られた！";
                case ElementalRequirement.Slash:   return "切断に成功した！";
                case ElementalRequirement.Strike:  return "壁が破壊された！";
                case ElementalRequirement.Pierce:  return "穴が開いた！";
                default:                           return "解除された！";
            }
        }
    }
}
