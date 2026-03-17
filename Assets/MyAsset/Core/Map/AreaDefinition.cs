using System;
using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// マップ上のエリア定義。矩形境界とID。
    /// </summary>
    [Serializable]
    public struct AreaDefinition
    {
        public string areaId;
        public Rect bounds;
        public string displayName;
    }
}
