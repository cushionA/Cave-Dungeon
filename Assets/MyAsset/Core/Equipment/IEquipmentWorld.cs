namespace Game.Core
{
    /// <summary>装備由来の値と、装備とは独立した能力獲得元を分けて読み書きする境界。</summary>
    public struct EquipmentActorState
    {
        public CharacterBaseStats basis;
        public CharacterGrowth growth;
        public CharacterVitals vitals;
        public CombatStats combat;
        public AbilityFlag intrinsicFlags;
        public AbilityFlag publishedFlags;
        public EquipmentStatus equipment;
    }

    public interface IEquipmentWorld
    {
        bool TryGetActor(int ownerHash, out EquipmentActorState actor);
        void CommitActor(int ownerHash, in EquipmentActorState actor);
    }
}
