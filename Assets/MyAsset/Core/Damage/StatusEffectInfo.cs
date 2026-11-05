using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Game.Core
{

    /// <summary>
    /// 攻撃に付随する状態異常情報（蓄積モデル対応）。
    /// 1ヒットあたりの蓄積量を定義し、閾値超過で発症する。
    /// </summary>
    [Serializable]
    public struct StatusEffectInfo
    {
        public StatusEffectId effect;
        [MinValue(0)] public float accumulateValue;
        [MinValue(0)] public float duration;
        [MinValue(0)] public float tickDamage;
        [MinValue(0)] public float tickInterval;
        [Tooltip("1スタックあたりの割合。Slow=減速、Weakness=被ダメ増加、AttackUp/DefenseUp/SpeedUp=上昇。0.5=50%。")]
        [Range(0f, 1f)] public float modifier;
        [Tooltip("0/1=重複なし。再付与は持続時間を更新。")]
        public byte maxStack;
    }
}
