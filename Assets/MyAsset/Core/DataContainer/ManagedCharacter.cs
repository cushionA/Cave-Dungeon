using UnityEngine;

namespace Game.Core
{
    public abstract class ManagedCharacter : MonoBehaviour
    {
        public abstract int ObjectHash { get; }
        public abstract IDamageable Damageable { get; }
    }
}
