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
        void Deserialize(object data);
    }

    /// <summary>旧スロットに保存項目がなければ初期値へ戻す。</summary>
    public interface ISaveableWithMissingEntry : ISaveable
    {
        void RestoreMissingEntry();
    }

    /// <summary>状態を変えずに保存項目を検証し、復元できない場合は例外を送出する。</summary>
    public interface ISaveableLoadPreflight : ISaveable
    {
        void ValidateLoad(object data);
    }

    /// <summary>ディスクに保存する値と区別して、ロード失敗時に必要な実行状態を保持する。</summary>
    public interface ISaveableWithLoadRollback : ISaveable
    {
        object CaptureLoadRollback();
        void RestoreLoadRollback(object snapshot);
    }

    public interface IEquippable
    {
        EquipSlot Slot { get; }
        int Weight { get; }
        void OnEquip(int ownerHash);
        void OnUnequip(int ownerHash);
        AbilityFlag GrantedFlags { get; }
        StatusEffectApplicationData[] OnHitEffects { get; }
    }
}
