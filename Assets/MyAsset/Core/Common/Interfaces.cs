namespace Game.Core
{
    public interface IAbility
    {
        AbilityType Type { get; }
        AbilityExclusiveGroup ExclusiveGroup { get; }
        bool CanExecute();
        bool IsExecuting { get; }
        /// <summary>
        /// ownerHashでSoAコンテナ経由のデータアクセスを初期化する。
        /// MonoBehaviour参照はRuntime層で別途キャッシュすること。
        /// </summary>
        void Initialize(int ownerHash);
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

        /// <summary>
        /// セーブデータからインスタンス状態を復元する。
        /// 契約 (Issue #80 L2): <paramref name="data"/> が null の場合は「対象スロットに entry が無い」を意味し、
        /// 内部状態を**初期状態にリセット**しなければならない。これにより SaveManager.Load 時にスロット切替で
        /// 前 state を引きずらないことを保証する。
        /// </summary>
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
