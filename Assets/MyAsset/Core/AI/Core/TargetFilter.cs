using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Game.Core
{

    [Serializable]
    public struct TargetFilter
    {
        /// <summary>各フィルターのAND/OR判定切替</summary>
        public FilterBitFlag filterFlags;

        /// <summary>陣営（複数指定可）</summary>
        public CharacterBelong belong;

        /// <summary>特徴（複数指定可）</summary>
        public CharacterFeature feature;

        /// <summary>弱点属性（複数指定可）</summary>
        public Element weakPoint;

        /// <summary>距離範囲 x=min, y=max（0,0で無制限）</summary>
        public UnityEngine.Vector2 distanceRange;

        /// <summary>自分を対象に含めるか</summary>
        public bool includeSelf;
    }
}
