using UnityEngine;

namespace Game.Core
{
    public interface IAbility
    {
        AbilityType Type { get; }
        AbilityExclusiveGroup ExclusiveGroup { get; }
        bool CanExecute();
        bool IsExecuting { get; }
        void Initialize(MonoBehaviour owner);
        void Execute(MovementInfo info);
        void Cancel();
        void Tick(float deltaTime);
    }

    public interface IDamageable
    {
        int ObjectHash { get; }
        bool IsAlive { get; }
        DamageResult ReceiveDamage(DamageData data);
    }

    public interface IInteractable
    {
        InteractionType InteractionType { get; }
        string InteractionPrompt { get; }
        bool CanInteract(int playerHash);
        void Interact(int playerHash);
    }

    public interface ISaveable
    {
        string SaveId { get; }
        object Serialize();
        void Deserialize(object data);
    }

    public interface IEquippable
    {
        EquipSlot Slot { get; }
        int Weight { get; }
        void OnEquip(int ownerHash);
        void OnUnequip(int ownerHash);
        AbilityFlag GrantedFlags { get; }
    }
}
