using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>キャラ能力の再導出・恒久能力・変更通知を所有する。装備は補正の入力元として読む。</summary>
    public sealed class CharacterStatsSystem
    {
        private readonly IEquipmentWorld _world;
        private readonly EquipmentStore _equipment;
        private readonly GameEvents _events;
        private readonly Dictionary<int, AbilityFlag> _notifiedFlags = new Dictionary<int, AbilityFlag>();

        internal CharacterStatsSystem(IEquipmentWorld world, EquipmentStore equipment, GameEvents events)
        {
            _world = world;
            _equipment = equipment;
            _events = events;
        }

        internal void Register(int ownerHash)
        {
            Recalculate(ownerHash, false);
            _notifiedFlags.Add(ownerHash, TryGetActor(ownerHash, out EquipmentActorState actor)
                ? actor.publishedFlags : AbilityFlag.None);
        }

        internal void Unregister(int ownerHash) => _notifiedFlags.Remove(ownerHash);

        internal void ClearEquipment(int ownerHash)
        {
            if (TryGetActor(ownerHash, out EquipmentActorState actor)) { Commit(ownerHash, null, ref actor); }
        }

        public bool Recalculate(int ownerHash) => Recalculate(ownerHash, true);

        internal bool Recalculate(int ownerHash, bool notify)
        {
            if (!_equipment.TryGetMutableHolder(ownerHash, out EquipmentHolder holder)
                || !TryGetActor(ownerHash, out EquipmentActorState actor)) { return false; }
            Commit(ownerHash, holder, ref actor);
            if (notify) { PublishChanges(ownerHash, holder); }
            return true;
        }

        public bool ApplyGrowth(int ownerHash, in CharacterGrowth growth)
        {
            if (!_equipment.TryGetMutableHolder(ownerHash, out EquipmentHolder holder)
                || !TryGetActor(ownerHash, out EquipmentActorState actor)) { return false; }
            actor.growth = growth;
            Commit(ownerHash, holder, ref actor);
            PublishChanges(ownerHash, holder);
            return true;
        }

        public bool GrantAbilityFlags(int ownerHash, AbilityFlag flags)
        {
            if (!_equipment.TryGetMutableHolder(ownerHash, out EquipmentHolder holder)
                || !TryGetActor(ownerHash, out EquipmentActorState actor)) { return false; }
            AbilityFlag intrinsic = actor.intrinsicFlags | flags;
            if (intrinsic == actor.intrinsicFlags) { return false; }
            actor.intrinsicFlags = intrinsic;
            Commit(ownerHash, holder, ref actor);
            PublishChanges(ownerHash, holder);
            return true;
        }

        internal void PublishChanges(int ownerHash, EquipmentHolder holder)
        {
            if (!_equipment.IsCurrentHolder(ownerHash, holder)) { return; }
            // 同期購読から再着脱・再登録しても、過去のフラグを通知し直さない。
            if (_notifiedFlags.TryGetValue(ownerHash, out AbilityFlag before)
                && TryGetActor(ownerHash, out EquipmentActorState actor) && before != actor.publishedFlags)
            {
                _notifiedFlags[ownerHash] = actor.publishedFlags;
                _events?.FireAbilityFlagsChanged(ownerHash, actor.publishedFlags);
            }
            if (_equipment.IsCurrentHolder(ownerHash, holder) && TryGetActor(ownerHash, out _))
            {
                _events?.FireCharacterStatsChanged(ownerHash);
            }
        }

        private bool TryGetActor(int ownerHash, out EquipmentActorState actor)
        {
            if (_world != null) { return _world.TryGetActor(ownerHash, out actor); }
            actor = default;
            return false;
        }

        private void Commit(int ownerHash, EquipmentHolder holder, ref EquipmentActorState actor)
        {
            EquipmentDerivedStats derived = CharacterStatCalculator.CalculateDerived(actor.basis, actor.growth,
                actor.vitals, GetModifiers(holder?.Weapon), GetModifiers(holder?.Shield), GetModifiers(holder?.Core));
            actor.vitals = derived.vitals;
            actor.combat = derived.combat;
            actor.equipment.attributeModifiers = derived.attributeModifiers;
            actor.equipment.finalAttack = derived.combat.attack;
            actor.equipment.finalDefense = derived.combat.defense;
            actor.equipment.finalGuardStats = derived.combat.guardStats;
            actor.equipment.activeFlags = holder != null ? holder.GetCombinedAbilityFlags() : AbilityFlag.None;
            actor.equipment.totalWeight = holder != null ? holder.GetTotalWeight() : 0;
            actor.publishedFlags = actor.intrinsicFlags | actor.equipment.activeFlags;
            _world.CommitActor(ownerHash, actor);
        }

        private static EquipmentStatModifiers GetModifiers(IEquippable item) =>
            item is IEquipmentStatsProvider provider ? provider.StatModifiers : default;
    }
}
