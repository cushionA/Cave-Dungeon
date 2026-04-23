using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// レベルアップ機構のハードキャップ設定を保持する ScriptableObject。
    /// 現状は最大レベルのみ外出しし、他の定数 (k_PointsPerLevel, k_HpPerVit, k_MpPerMnd) は
    /// LevelUpLogic 側にハードコードで維持する。
    /// 将来、各ステータス上限からの動的算出最大レベルを導入する際は、
    /// この SO の maxLevel がハードキャップとして機能し、動的値が超えないことを保証する。
    /// </summary>
    [CreateAssetMenu(fileName = "LevelUpConfig", menuName = "Game/LevelUpConfig")]
    public class LevelUpConfig : ScriptableObject
    {
        /// <summary>最大レベルキャップ。到達後の余剰 exp は破棄される。</summary>
        [Tooltip("最大レベルキャップ。到達後の余剰 exp は破棄される。")]
        [Min(1)]
        public int maxLevel = 99;
    }
}
