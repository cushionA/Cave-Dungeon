namespace Game.Core
{
    public sealed class SoAExecutionWorld : IExecutionWorld
    {
        private readonly SoACharaDataDic _data;

        public SoAExecutionWorld(SoACharaDataDic data) { _data = data; }

        public bool TryGetActor(int hash, out ActionActorState actor)
        {
            if (!_data.TryGetValue(hash, out _))
            {
                actor = default;
                return false;
            }
            actor = new ActionActorState
            {
                vitals = _data.GetVitals(hash), flags = _data.GetFlags(hash),
                statusEffects = _data.GetStatusEffects(hash),
            };
            return true;
        }

        public void CommitActor(int hash, in ActionActorState actor)
        {
            if (_data.TryGetValue(hash, out _))
            {
                _data.GetVitals(hash) = actor.vitals;
                _data.GetFlags(hash) = actor.flags;
            }
        }
    }
}
