using System;

namespace Game.Core
{
    /// <summary>
    /// ボスフェーズ遷移条件。
    /// </summary>
    [Serializable]
    public struct PhaseCondition
    {
        public PhaseConditionType type;
        public float threshold;    // HP割合 or 秒数 or 回数
    }
}
