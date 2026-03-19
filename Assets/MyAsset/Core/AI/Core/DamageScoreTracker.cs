using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// Tracks cumulative damage dealt by attackers with exponential time decay.
    /// Used by AI to determine threat/aggro priority.
    /// Decay formula: score * e^(-k_DecayRate * elapsed)
    /// </summary>
    public class DamageScoreTracker
    {
        private const float k_DecayRate = 0.1f;

        private Dictionary<int, DamageScoreEntry> _scores;

        public DamageScoreTracker()
        {
            _scores = new Dictionary<int, DamageScoreEntry>();
        }

        /// <summary>
        /// Adds damage from an attacker. Decays existing score before accumulating.
        /// </summary>
        public void AddDamage(int attackerHash, float damage, float currentTime)
        {
            if (_scores.TryGetValue(attackerHash, out DamageScoreEntry entry))
            {
                float decayed = DecayScore(entry.score, entry.lastUpdateTime, currentTime);
                entry.score = decayed + damage;
                entry.lastUpdateTime = currentTime;
                _scores[attackerHash] = entry;
            }
            else
            {
                _scores[attackerHash] = new DamageScoreEntry
                {
                    attackerHash = attackerHash,
                    score = damage,
                    lastUpdateTime = currentTime
                };
            }
        }

        /// <summary>
        /// Returns the decayed score for a given attacker at the specified time.
        /// Returns 0 if attacker is not tracked.
        /// </summary>
        public float GetScore(int attackerHash, float currentTime)
        {
            if (!_scores.TryGetValue(attackerHash, out DamageScoreEntry entry))
            {
                return 0f;
            }

            return DecayScore(entry.score, entry.lastUpdateTime, currentTime);
        }

        /// <summary>
        /// Returns the hash of the attacker with the highest decayed score.
        /// Returns 0 if no attackers are tracked.
        /// </summary>
        public int GetHighestScoreAttacker(float currentTime)
        {
            int bestHash = 0;
            float bestScore = 0f;

            foreach (KeyValuePair<int, DamageScoreEntry> kvp in _scores)
            {
                float score = DecayScore(kvp.Value.score, kvp.Value.lastUpdateTime, currentTime);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestHash = kvp.Key;
                }
            }

            return bestHash;
        }

        /// <summary>
        /// Removes all damage records for the given attacker.
        /// </summary>
        public void RemoveAttacker(int attackerHash)
        {
            _scores.Remove(attackerHash);
        }

        /// <summary>
        /// Clears all tracked damage scores.
        /// </summary>
        public void Clear()
        {
            _scores.Clear();
        }

        private float DecayScore(float score, float lastTime, float currentTime)
        {
            float elapsed = currentTime - lastTime;
            if (elapsed <= 0f)
            {
                return score;
            }

            return score * UnityEngine.Mathf.Exp(-k_DecayRate * elapsed);
        }
    }
}
