using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Game.Core
{

    /// <summary>
    /// 7属性別のガードカット率。
    /// </summary>
    [Serializable, InlineProperty]
    public struct GuardStats
    {
        [HorizontalGroup("Cuts", LabelWidth = 40)]
        [LabelText("斬"), Range(0f, 1f)] public float slashCut;
        [HorizontalGroup("Cuts", LabelWidth = 40)]
        [LabelText("打"), Range(0f, 1f)] public float strikeCut;
        [HorizontalGroup("Cuts", LabelWidth = 40)]
        [LabelText("突"), Range(0f, 1f)] public float pierceCut;

        [HorizontalGroup("MagicCuts", LabelWidth = 40)]
        [LabelText("炎"), Range(0f, 1f)] public float fireCut;
        [HorizontalGroup("MagicCuts", LabelWidth = 40)]
        [LabelText("雷"), Range(0f, 1f)] public float thunderCut;
        [HorizontalGroup("MagicCuts", LabelWidth = 40)]
        [LabelText("聖"), Range(0f, 1f)] public float lightCut;
        [HorizontalGroup("MagicCuts", LabelWidth = 40)]
        [LabelText("闇"), Range(0f, 1f)] public float darkCut;

        [MinValue(0)] public float guardStrength;
        [Range(0f, 1f)] public float statusCut;
        [EnumToggleButtons] public GuardDirection guardDirection;
    }
}
