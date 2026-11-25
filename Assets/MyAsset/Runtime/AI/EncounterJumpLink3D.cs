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

        internal Transform Takeoff => takeoff;
        internal Transform Landing => landing;
        internal Collider LandingSurface => landingSurface;

        internal EncounterJumpLink3D(Transform takeoff, Transform landing, Collider landingSurface)
        {
            this.takeoff = takeoff;
            this.landing = landing;
            this.landingSurface = landingSurface;
        }
    }
}
