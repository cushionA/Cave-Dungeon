using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// State of the companion follow behavior.
    /// </summary>
    public enum FollowState : byte
    {
        Waiting,
        Following,
        Teleporting,
    }

    /// <summary>
    /// Evaluates companion follow behavior based on distance to the player.
    /// Determines whether to wait, follow, or teleport.
    /// </summary>
    public class FollowBehavior
    {
        private float _followDistance;
        private float _maxLeashDistance;

        public FollowState CurrentState { get; private set; }

        public FollowBehavior(float followDistance = 2.0f, float maxLeashDistance = 15.0f)
        {
            _followDistance = followDistance;
            _maxLeashDistance = maxLeashDistance;
        }

        /// <summary>
        /// Evaluates distance between companion and player to determine follow state.
        /// </summary>
        public FollowState Evaluate(Vector2 companionPos, Vector2 playerPos)
        {
            float distance = Vector2.Distance(companionPos, playerPos);

            if (distance < _followDistance)
            {
                CurrentState = FollowState.Waiting;
            }
            else if (distance >= _maxLeashDistance)
            {
                CurrentState = FollowState.Teleporting;
            }
            else
            {
                CurrentState = FollowState.Following;
            }

            return CurrentState;
        }

        /// <summary>
        /// Calculates the target position for the companion to move toward.
        /// </summary>
        public Vector2 GetFollowTarget(Vector2 companionPos, Vector2 playerPos)
        {
            Vector2 direction = (playerPos - companionPos).normalized;
            return playerPos - direction * _followDistance * 0.5f;
        }

        /// <summary>
        /// Updates the follow and leash distances.
        /// </summary>
        public void SetDistances(float followDistance, float maxLeashDistance)
        {
            _followDistance = followDistance;
            _maxLeashDistance = maxLeashDistance;
        }
    }
}
