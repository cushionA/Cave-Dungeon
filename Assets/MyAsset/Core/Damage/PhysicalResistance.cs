using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Game.Core
{

    /// <summary>
    /// 物理タイプ別耐性（斬撃/打撃/刺突）。
    /// </summary>
    [Serializable, InlineProperty]
    public struct PhysicalResistance
    {
        [HorizontalGroup("Resist", LabelWidth = 40)]
        [LabelText("斬"), Range(0f, 1f)] public float slashResist;
        [HorizontalGroup("Resist", LabelWidth = 40)]
        [LabelText("打"), Range(0f, 1f)] public float strikeResist;
        [HorizontalGroup("Resist", LabelWidth = 40)]
        [LabelText("突"), Range(0f, 1f)] public float pierceResist;
    }
}
