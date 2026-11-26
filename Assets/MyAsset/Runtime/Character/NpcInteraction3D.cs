using Game.Core;
using UnityEngine;
using UnityEngine.Events;

namespace Game.Runtime
{
    /// <summary>近くにいる騎士からの操作を、NPCに設定した会話や取引へ渡す。</summary>
    [DisallowMultipleComponent]
    public sealed class NpcInteraction3D : MonoBehaviour, IInteractable
    {
        [SerializeField] private InteractionType interactionType = InteractionType.NpcDialog;
        [SerializeField] private string interactionPrompt = "話す";
        [SerializeField, Min(0.1f)] private float interactionRadius = 2f;
        [SerializeField] private UnityEvent onInteracted = new UnityEvent();

        public InteractionType InteractionType => interactionType;
        public string InteractionPrompt => interactionPrompt;

        public bool CanInteract(int playerHash)
        {
            return TryGetDistanceSquared(GameManager.Data?.GetManaged(playerHash) as CharacterActor3D, out _);
        }

        public void Interact(int playerHash)
        {
            TryInteract(GameManager.Data?.GetManaged(playerHash) as CharacterActor3D);
        }

        internal bool TryGetDistanceSquared(CharacterActor3D player, out float distanceSquared)
        {
            distanceSquared = 0f;
            if (!isActiveAndEnabled || player == null || !player.isActiveAndEnabled || !player.IsAlive
                || !(interactionRadius > 0f)
                || !float.IsFinite(interactionRadius)
                || (GameManager.Data.GetFlags(player.Actor.Hash).Feature & CharacterFeature.Player) == 0)
            {
                return false;
            }
            if (player.gameObject.scene != gameObject.scene && (interactionType != InteractionType.SavePoint
                || !TryGetComponent(out ExpeditionFastTravelPoint3D point) || !point.IsAvailableTo(player))) { return false; }

            distanceSquared = (player.Motor.FeetPosition - transform.position).sqrMagnitude;
            return distanceSquared <= interactionRadius * interactionRadius;
        }

        internal bool TryInteract(CharacterActor3D player)
        {
            if (!TryGetDistanceSquared(player, out _)) { return false; }
            if (interactionType == InteractionType.SavePoint && TryGetComponent(out ExpeditionFastTravelPoint3D point)
                && !TryGetComponent(out ExpeditionSavePoint3D _))
            {
                return point.TryOpen(player);
            }
            onInteracted.Invoke();
            return true;
        }
    }
}
