using System;
using ODC.Runtime;

namespace Game.Core
{
    /// <summary>
    /// Tracks cumulative damage dealt by attackers with exponential time decay.
    /// Used by AI to determine threat/aggro priority.
    /// Backed by DecayingScoreContainer for fixed-capacity, GC-free operation.
    /// </summary>
    public class DamageScoreTracker : IDisposable
    {
        private const float k_DecayRate = 0.1f;
        private const int k_DefaultMaxAttackers = 16;
        private const int k_InternalOwnerHash = 1;

        private DecayingScoreContainer _container;

        public DamageScoreTracker() : this(k_DefaultMaxAttackers)
        {
        }

        public DamageScoreTracker(int maxAttackers)
        {
            _container = new DecayingScoreContainer(1, maxAttackers, k_DecayRate);
            _container.AddOwner(k_InternalOwnerHash);
        }

        /// <summary>
        /// Adds damage from an attacker. Decays existing score before accumulating.
        /// </summary>
        public void AddDamage(int attackerHash, float damage, float currentTime)
        {
            _container.AddScore(k_InternalOwnerHash, attackerHash, damage, currentTime);
        }

        /// <summary>
        /// Returns the decayed score for a given attacker at the specified time.
        /// Returns 0 if attacker is not tracked.
        /// </summary>
        public float GetScore(int attackerHash, float currentTime)
        {
            return _container.GetScore(k_InternalOwnerHash, attackerHash, currentTime);
        }

        /// <summary>
        /// Returns the hash of the attacker with the highest decayed score.
        /// Returns 0 if no attackers are tracked.
        /// </summary>
        public int GetHighestScoreAttacker(float currentTime)
        {
            return _container.GetHighest(k_InternalOwnerHash, currentTime);
        }

        /// <summary>
        /// Removes all damage records for the given attacker.
        /// </summary>
        public void RemoveAttacker(int attackerHash)
        {
            _container.RemoveTarget(k_InternalOwnerHash, attackerHash);
        }

        /// <summary>
        /// Clears all tracked damage scores.
        /// </summary>
        public void Clear()
        {
            _container.Clear();
            _container.AddOwner(k_InternalOwnerHash);
        }

        public void Dispose()
        {
            _container.Dispose();
        }
    }
}
