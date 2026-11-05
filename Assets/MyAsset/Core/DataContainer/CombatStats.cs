using System;
using UnityEngine;

namespace Game.Core
{

    /// <summary>
    /// 7属性別の攻撃力・防御力 + ガード + ノックバック耐性。
    /// 弱点ダメージボーナス・クリティカルヒット機構は仕様外のため非搭載
    /// (弱点は defense の属性別低さで表現)。
    /// </summary>
    [Serializable]
    public struct CombatStats
    {
        public ElementalStatus attack;    // 7属性別攻撃力
        public ElementalStatus defense;   // 7属性別防御力
        public GuardStats guardStats;
        public StatusEffectDefenseProfile statusDefense;

        /// <summary>
        /// ノックバック耐性（0.0-1.0）。HpArmorLogic.CalculateKnockback で
        /// knockbackForce に (1 - resistance) を乗じる。1.0 で完全無効化。
        /// </summary>
        public float knockbackResistance;
    }
}
