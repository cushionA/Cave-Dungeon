using System;
using UnityEngine;

namespace Game.Core
{

    /// <summary>
    /// BoxCast 接地判定の結果。坂道法線を保持し、velocity 射影に使用する。
    /// </summary>
    [Serializable]
    public struct GroundInfo
    {
        public bool isGrounded;
        /// <summary>地面の法線。未接地時は Vector2.up。</summary>
        public Vector2 normal;

        public static GroundInfo NotGrounded => new GroundInfo { isGrounded = false, normal = Vector2.up };
        public static GroundInfo Flat => new GroundInfo { isGrounded = true, normal = Vector2.up };
    }
}
