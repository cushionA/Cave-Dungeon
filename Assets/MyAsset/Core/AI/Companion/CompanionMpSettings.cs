using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Game.Core
{
    [Serializable]
    public struct CompanionMpSettings
    {
        [LabelText("MP自然回復速度(/秒)"), MinValue(0)]
        public float baseRecoveryRate;

        [LabelText("MP回復行動時の回復速度(/秒)"), MinValue(0)]
        public float mpRecoverActionRate;

        [LabelText("消滅中の回復倍率"), MinValue(1f)]
        public float vanishRecoveryMultiplier;

        [LabelText("復帰閾値(maxMP比率)"), Range(0f, 1f)]
        public float returnThresholdRatio;

        [LabelText("reserveMP最大値"), MinValue(0)]
        public int maxReserveMp;
    }
}
