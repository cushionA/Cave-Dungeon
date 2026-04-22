// Enum.GetValues / 連番値配列の静的キャッシュ (UI 再構築ごとのアロケ削減) を保持する partial。
using System;
using Game.Core;

namespace Game.Runtime
{
    /// <summary>
    /// CompanionAISettingsController の partial。
    /// UI 再構築のたびに走る <c>Enum.GetValues</c> 呼び出しは、そのつど
    /// 配列を生成するため GC 圧を産む。頻出する enum だけを type initializer で
    /// 一度だけ実体化し、UI 生成コードは静的 readonly 参照を見るだけにする。
    /// ここには入力データが変わらない enum 配列のみを置く（AttackCategory ごとの
    /// <c>Dictionary</c> など、ランタイムの attackCatalog に依存するものは対象外）。
    /// </summary>
    public partial class CompanionAISettingsController
    {
        /// <summary>AIConditionType の全値（条件タイプドロップダウン用）。</summary>
        private static readonly AIConditionType[] s_CachedAIConditionTypes =
            (AIConditionType[])Enum.GetValues(typeof(AIConditionType));

        /// <summary>CharacterBelong の全値（ゼロ値を含む、呼び出し側でスキップ）。</summary>
        private static readonly CharacterBelong[] s_CachedCharacterBelongValues =
            (CharacterBelong[])Enum.GetValues(typeof(CharacterBelong));

        /// <summary>CharacterFeature の全値（ゼロ値を含む）。</summary>
        private static readonly CharacterFeature[] s_CachedCharacterFeatureValues =
            (CharacterFeature[])Enum.GetValues(typeof(CharacterFeature));

        /// <summary>Element の全値（ゼロ値を含む）。</summary>
        private static readonly Element[] s_CachedElementValues =
            (Element[])Enum.GetValues(typeof(Element));

        /// <summary>InstantAction の全値（UseItem を含む、呼び出し側で必要なら除外）。</summary>
        private static readonly InstantAction[] s_CachedInstantActionValues =
            (InstantAction[])Enum.GetValues(typeof(InstantAction));

        /// <summary>SustainedAction の全値。</summary>
        private static readonly SustainedAction[] s_CachedSustainedActionValues =
            (SustainedAction[])Enum.GetValues(typeof(SustainedAction));

        /// <summary>TargetSortKey の全値。</summary>
        private static readonly TargetSortKey[] s_CachedTargetSortKeyValues =
            (TargetSortKey[])Enum.GetValues(typeof(TargetSortKey));
    }
}
