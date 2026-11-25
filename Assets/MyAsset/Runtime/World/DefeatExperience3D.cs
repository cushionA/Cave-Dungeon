using Game.Core;
using UnityEngine;

namespace Game.Runtime
{
    /// <summary>配置した敵一体の世代ごとに、撃破報酬を一度だけ確定する。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterActor3D))]
    public sealed class DefeatExperience3D : MonoBehaviour
    {
        [SerializeField, Min(0)] private int experience;

        private CharacterActor3D _actor;
        private ActorRef _claimed;

        private void Awake() => _actor = GetComponent<CharacterActor3D>();

        internal bool TryClaim(ActorRef defeated, out int amount)
        {
            amount = 0;
            if (!isActiveAndEnabled || experience <= 0 || !defeated.IsValid
                || _actor.Actor != defeated || _claimed == defeated) { return false; }
            _claimed = defeated;
            amount = experience;
            return true;
        }
    }
}
