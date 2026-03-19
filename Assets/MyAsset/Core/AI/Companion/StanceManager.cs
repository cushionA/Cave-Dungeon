using System;

namespace Game.Core
{
    /// <summary>
    /// Weight multipliers applied to action evaluation based on the current stance.
    /// </summary>
    public struct StanceWeights
    {
        public float attackMultiplier;
        public float defenseMultiplier;
        public float healMultiplier;
        public float approachDistanceMultiplier;
    }

    /// <summary>
    /// Manages companion stance and provides weight multipliers for AI action evaluation.
    /// </summary>
    public class StanceManager
    {
        private CompanionStance _currentStance;

        public CompanionStance CurrentStance => _currentStance;
        public event Action<CompanionStance> OnStanceChanged;

        public StanceManager()
        {
            _currentStance = CompanionStance.Aggressive;
        }

        /// <summary>
        /// Changes the current stance. Fires OnStanceChanged if the stance actually changes.
        /// </summary>
        public void SetStance(CompanionStance stance)
        {
            if (_currentStance == stance)
            {
                return;
            }

            _currentStance = stance;
            OnStanceChanged?.Invoke(_currentStance);
        }

        /// <summary>
        /// Returns the weight multipliers for the current stance.
        /// </summary>
        public StanceWeights GetWeights()
        {
            switch (_currentStance)
            {
                case CompanionStance.Aggressive:
                    return new StanceWeights
                    {
                        attackMultiplier = 2.0f,
                        defenseMultiplier = 1.0f,
                        healMultiplier = 0.5f,
                        approachDistanceMultiplier = 1.0f
                    };
                case CompanionStance.Defensive:
                    return new StanceWeights
                    {
                        attackMultiplier = 1.0f,
                        defenseMultiplier = 2.0f,
                        healMultiplier = 1.0f,
                        approachDistanceMultiplier = 0.7f
                    };
                case CompanionStance.Supportive:
                    return new StanceWeights
                    {
                        attackMultiplier = 0.3f,
                        defenseMultiplier = 1.0f,
                        healMultiplier = 3.0f,
                        approachDistanceMultiplier = 1.2f
                    };
                case CompanionStance.Passive:
                    return new StanceWeights
                    {
                        attackMultiplier = 0f,
                        defenseMultiplier = 0f,
                        healMultiplier = 0f,
                        approachDistanceMultiplier = 1.0f
                    };
                default:
                    return new StanceWeights
                    {
                        attackMultiplier = 1.0f,
                        defenseMultiplier = 1.0f,
                        healMultiplier = 1.0f,
                        approachDistanceMultiplier = 1.0f
                    };
            }
        }

        /// <summary>
        /// Applies the current stance weight to a base action weight based on ActionExecType.
        /// </summary>
        public float ApplyWeight(float baseWeight, ActionExecType actionType)
        {
            StanceWeights weights = GetWeights();

            switch (actionType)
            {
                case ActionExecType.Attack:
                    return baseWeight * weights.attackMultiplier;
                case ActionExecType.Cast:
                    return baseWeight * weights.healMultiplier;
                case ActionExecType.Sustained:
                    return baseWeight * weights.defenseMultiplier;
                default:
                    return baseWeight;
            }
        }
    }
}
