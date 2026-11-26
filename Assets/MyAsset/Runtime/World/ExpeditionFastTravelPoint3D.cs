using UnityEngine;

namespace Game.Runtime
{
    /// <summary>遠征の区画に置く、同じ遠征内のファストトラベル地点。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NpcInteraction3D))]
    public sealed class ExpeditionFastTravelPoint3D : MonoBehaviour
    {
        [SerializeField] private string pointId;
        [SerializeField] private Transform arrivalAnchor;

        internal string PointId => pointId;
        internal Transform ArrivalAnchor => arrivalAnchor;

        internal bool IsAvailableTo(CharacterActor3D actor) => TryGetTravel(actor, out ExpeditionFastTravel3D travel)
            && travel.OwnsPoint(this);

        internal void RecordVisit(CharacterActor3D actor)
        {
            if (TryGetTravel(actor, out ExpeditionFastTravel3D travel)) { travel.RecordVisit(this); }
        }

        internal bool TryOpen(CharacterActor3D actor) => TryGetTravel(actor, out ExpeditionFastTravel3D travel)
            && travel.TryOpen(this);

        private bool TryGetTravel(CharacterActor3D actor, out ExpeditionFastTravel3D travel)
        {
            travel = null;
            return isActiveAndEnabled && actor != null && actor.TryGetComponent(out travel) && travel.isActiveAndEnabled;
        }
    }
}
