using System;
using UnityEngine;

namespace Game.Runtime
{
    /// <summary>同じ遭遇で使う、踏切から指定した着地床への片方向リンク。</summary>
    [Serializable]
    public struct EncounterJumpLink3D
    {
        [SerializeField] private Transform takeoff;
        [SerializeField] private Transform landing;
        [SerializeField] private Collider landingSurface;
        [SerializeField, Min(0f), Tooltip("踏切から着地点まで許容する落差。既定値の0では従来の同程度の高さだけを使う。")]
        private float maxDescent;

        internal Transform Takeoff => takeoff;
        internal Transform Landing => landing;
        internal Collider LandingSurface => landingSurface;
        internal float MaxDescent => maxDescent;

        internal EncounterJumpLink3D(Transform takeoff, Transform landing, Collider landingSurface, float maxDescent = 0f)
        {
            this.takeoff = takeoff;
            this.landing = landing;
            this.landingSurface = landingSurface;
            this.maxDescent = maxDescent;
        }
    }
}
