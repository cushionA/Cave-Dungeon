using UnityEngine;

namespace Game.Core
{
    public abstract class ManagedCharacter : MonoBehaviour
    {
        public abstract int ObjectHash { get; }
        public abstract IDamageable Damageable { get; }
        public virtual bool Uses3DPhysics => false;
    }
}
