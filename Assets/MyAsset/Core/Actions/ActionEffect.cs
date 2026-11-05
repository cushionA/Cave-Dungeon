using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Game.Core
{

    /// <summary>
    /// 行動中に発生する特殊効果（アーマー、無敵、ダメージ軽減など）。
    /// 開始時間と持続時間で発生区間を制御する。
    /// </summary>
    [Serializable, InlineProperty]
    public struct ActionEffect
    {
        [EnumToggleButtons] public ActionEffectType type;
        [MinValue(0), LabelText("開始(秒)")] public float startTime;
        [MinValue(0), LabelText("持続(秒)")] public float duration;
        [LabelText("効果量")] public float value;

        public readonly float EndTime => startTime + duration;

        public readonly bool IsActive(float elapsedTime)
        {
            return elapsedTime >= startTime && elapsedTime < EndTime;
        }
    }
}
