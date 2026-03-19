using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// Key identifying an unlockable action type by its execution type and parameter.
    /// </summary>
    public struct ActionUnlockKey : IEquatable<ActionUnlockKey>
    {
        public ActionExecType execType;
        public int paramId;

        public bool Equals(ActionUnlockKey other)
        {
            return execType == other.execType && paramId == other.paramId;
        }

        public override bool Equals(object obj)
        {
            return obj is ActionUnlockKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return ((int)execType * 397) ^ paramId;
        }
    }

    /// <summary>
    /// Tracks which action types the companion has unlocked.
    /// Initializes with a set of default actions and allows unlocking new ones.
    /// </summary>
    public class ActionTypeRegistry
    {
        private HashSet<ActionUnlockKey> _unlockedActions;

        public event Action<ActionUnlockKey> OnActionUnlocked;

        public int UnlockedCount => _unlockedActions.Count;

        public ActionTypeRegistry()
        {
            _unlockedActions = new HashSet<ActionUnlockKey>();
            InitializeDefaults();
        }

        private void InitializeDefaults()
        {
            Unlock(new ActionUnlockKey { execType = ActionExecType.Attack, paramId = 0 });
            Unlock(new ActionUnlockKey { execType = ActionExecType.Attack, paramId = 1 });
            Unlock(new ActionUnlockKey { execType = ActionExecType.Attack, paramId = 2 });
            Unlock(new ActionUnlockKey { execType = ActionExecType.Instant, paramId = (int)InstantAction.Dodge });
            Unlock(new ActionUnlockKey { execType = ActionExecType.Sustained, paramId = (int)SustainedAction.Follow });
            Unlock(new ActionUnlockKey { execType = ActionExecType.Sustained, paramId = (int)SustainedAction.MoveToTarget });
            Unlock(new ActionUnlockKey { execType = ActionExecType.Sustained, paramId = (int)SustainedAction.Guard });
        }

        /// <summary>
        /// Unlocks an action type. Fires OnActionUnlocked if it was newly added.
        /// </summary>
        public void Unlock(ActionUnlockKey key)
        {
            if (_unlockedActions.Add(key))
            {
                OnActionUnlocked?.Invoke(key);
            }
        }

        /// <summary>
        /// Returns true if the given action type has been unlocked.
        /// </summary>
        public bool IsUnlocked(ActionUnlockKey key)
        {
            return _unlockedActions.Contains(key);
        }

        /// <summary>
        /// Returns a list of all currently unlocked action keys.
        /// </summary>
        public List<ActionUnlockKey> GetAllUnlocked()
        {
            return new List<ActionUnlockKey>(_unlockedActions);
        }
    }
}
