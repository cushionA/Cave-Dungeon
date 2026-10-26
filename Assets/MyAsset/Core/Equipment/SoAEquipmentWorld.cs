using System;

namespace Game.Core
{
    public sealed class SoAEquipmentWorld : IEquipmentWorld
    {
        private readonly SoACharaDataDic _data;

        public SoAEquipmentWorld(SoACharaDataDic data)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
        }

        public bool TryGetActor(int ownerHash, out EquipmentActorState actor)
        {
            if (!_data.TryGetValue(ownerHash, out _))
            {
                actor = default;
                return false;
            }
            actor = new EquipmentActorState
            {
                basis = _data.GetBaseStats(ownerHash),
                growth = _data.GetGrowth(ownerHash),
                vitals = _data.GetVitals(ownerHash),
                combat = _data.GetCombatStats(ownerHash),
                intrinsicFlags = _data.GetBaseStats(ownerHash).abilityFlags,
                publishedFlags = _data.GetFlags(ownerHash).AbilityFlags,
                equipment = _data.GetEquipmentStatus(ownerHash),
            };
            return true;
        }

        public void CommitActor(int ownerHash, in EquipmentActorState actor)
        {
            if (_data.TryGetValue(ownerHash, out _))
            {
                _data.GetBaseStats(ownerHash).abilityFlags = actor.intrinsicFlags;
                _data.GetGrowth(ownerHash) = actor.growth;
                _data.GetVitals(ownerHash) = actor.vitals;
                _data.GetCombatStats(ownerHash) = actor.combat;
                _data.GetDefenseState(ownerHash).maxArmor = actor.vitals.maxArmor;
                _data.GetFlags(ownerHash).AbilityFlags = actor.publishedFlags;
                _data.GetEquipmentStatus(ownerHash) = actor.equipment;
            }
        }
    }
}
