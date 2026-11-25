using System;
using UnityEngine;

namespace Game.Core
{
    public enum AttackShape3D : byte
    {
        None,
        Melee,
        GroundArea,
        Projectile,
    }

    [Serializable]
    public struct AttackProfile3D
    {
        public AttackShape3D shape;
        [Tooltip("攻撃者から見た近接判定の中心、または飛翔体の発射位置")]
        public Vector3 localCenter;
        [Tooltip("近接判定の幅・高さ・奥行きの半分")]
        public Vector3 halfExtents;
        [Min(0f), Tooltip("接地点を中心とする範囲攻撃の半径")]
        public float radius;
        [Min(0f), Tooltip("3D飛翔体の衝突半径")]
        public float projectileRadius;
        public LayerMask targetLayers;
        public LayerMask obstructionLayers;
    }
}
