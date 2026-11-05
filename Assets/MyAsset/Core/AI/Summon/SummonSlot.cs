using System;

namespace Game.Core
{

    /// <summary>
    /// 召喚スロット情報。SummonManagerが管理する固定長配列の要素。
    /// </summary>
    [Serializable]
    public struct SummonSlot
    {
        public int summonHash;        // 召喚獣のhashCode
        public float remainingTime;   // 残り時間
        public SummonType summonType;
    }
}
