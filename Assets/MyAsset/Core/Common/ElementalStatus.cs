using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// 7属性（斬撃/打撃/刺突/炎/雷/聖/闇）のステータス値。
    /// 攻撃力・防御力・ダメージの各チャネルで使用する。
    /// </summary>
    [Serializable, InlineProperty]
    public struct ElementalStatus
    {
        [HorizontalGroup("Physical", LabelWidth = 40)]
        [LabelText("斬")]
        public int slash;
        [HorizontalGroup("Physical", LabelWidth = 40)]
        [LabelText("打")]
        public int strike;
        [HorizontalGroup("Physical", LabelWidth = 40)]
        [LabelText("突")]
        public int pierce;

        [HorizontalGroup("Magical", LabelWidth = 40)]
        [LabelText("炎")]
        public int fire;
        [HorizontalGroup("Magical", LabelWidth = 40)]
        [LabelText("雷")]
        public int thunder;
        [HorizontalGroup("Magical", LabelWidth = 40)]
        [LabelText("聖")]
        public int light;
        [HorizontalGroup("Magical", LabelWidth = 40)]
        [LabelText("闇")]
        public int dark;

        [ShowInInspector, ReadOnly, LabelText("合計")]
        [Tooltip("物理: 斬+打+突 / 魔法: 炎+雷+聖+闇")]
        public readonly int Total => slash + strike + pierce + fire + thunder + light + dark;

        [ShowInInspector, ReadOnly, LabelText("物理計")]
        public readonly int PhysicalTotal => slash + strike + pierce;

        [ShowInInspector, ReadOnly, LabelText("魔法計")]
        public readonly int MagicalTotal => fire + thunder + light + dark;

        public readonly int Get(Element element)
        {
            switch (element)
            {
                case Element.Slash:   return slash;
                case Element.Strike:  return strike;
                case Element.Pierce:  return pierce;
                case Element.Fire:    return fire;
                case Element.Thunder: return thunder;
                case Element.Light:   return light;
                case Element.Dark:    return dark;
                default:              return 0;
            }
        }
    }
}
