using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Game.Core
{

    [Serializable]
    public struct StatModifier
    {
        public int str;
        public int dex;
        public int intel;
        public int vit;
        public int mnd;
        public int end;
    }
}
