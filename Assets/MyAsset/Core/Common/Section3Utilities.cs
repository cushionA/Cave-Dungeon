namespace Game.Core
{
    /// <summary>
    /// パーティ構成を管理する静的ヘルパー。
    /// プレイヤー(1) + 常駐仲間(1) + 召喚枠(最大2) = 最大4人。
    /// 混乱敵はパーティ枠外。
    /// </summary>
    public static class PartyManager
    {
        public const int k_MaxPartySize = 4;
        public const int k_MaxSummonSlots = 2;
        public const int k_MaxConfusedEnemies = 3;
    }

    /// <summary>
    /// ElementalRequirement と Element フラグ間のマッピングユーティリティ。
    /// 属性ゲートの判定で使用する。
    /// </summary>
    public static class ElementalRequirementMapper
    {
        /// <summary>
        /// ElementalRequirement を対応する Element フラグに変換する。
        /// </summary>
        public static Element ToElement(ElementalRequirement requirement)
        {
            switch (requirement)
            {
                case ElementalRequirement.Fire:    return Element.Fire;
                case ElementalRequirement.Thunder: return Element.Thunder;
                case ElementalRequirement.Light:   return Element.Light;
                case ElementalRequirement.Dark:    return Element.Dark;
                case ElementalRequirement.Slash:   return Element.Slash;
                case ElementalRequirement.Strike:  return Element.Strike;
                case ElementalRequirement.Pierce:  return Element.Pierce;
                default:                           return Element.None;
            }
        }

        /// <summary>
        /// 攻撃の Element フラグが ElementalRequirement を満たすか判定する。
        /// ビット演算で包含チェック。
        /// </summary>
        public static bool MatchesElement(ElementalRequirement requirement, Element attackElement)
        {
            Element required = ToElement(requirement);
            return (attackElement & required) == required;
        }
    }
}
