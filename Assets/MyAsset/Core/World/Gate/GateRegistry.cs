using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// Tracks open/closed state of all gates in the current map.
    /// Supports serialization for save/load.
    /// </summary>
    public class GateRegistry : ISaveable
    {
        private Dictionary<string, bool> _gateStates;

        public int Count => _gateStates.Count;

        public GateRegistry()
        {
            _gateStates = new Dictionary<string, bool>();
        }

        public void Register(string gateId, bool isOpen = false)
        {
            _gateStates[gateId] = isOpen;
        }

        public bool IsOpen(string gateId)
        {
            if (_gateStates.TryGetValue(gateId, out bool isOpen))
            {
                return isOpen;
            }
            return false;
        }

        public void Open(string gateId)
        {
            _gateStates[gateId] = true;
        }

        public void Close(string gateId)
        {
            _gateStates[gateId] = false;
        }

        /// <summary>
        /// Returns a copy of all gate states for serialization.
        /// </summary>
        public Dictionary<string, bool> SerializeAll()
        {
            return new Dictionary<string, bool>(_gateStates);
        }

        /// <summary>
        /// Restores gate states from serialized data.
        /// </summary>
        public void DeserializeAll(Dictionary<string, bool> data)
        {
            _gateStates.Clear();
            foreach (KeyValuePair<string, bool> kvp in data)
            {
                _gateStates[kvp.Key] = kvp.Value;
            }
        }

        // ===== ISaveable =====

        public string SaveId => "GateRegistry";

        object ISaveable.Serialize()
        {
            return SerializeAll();
        }

        void ISaveable.Deserialize(object data)
        {
            if (data is Dictionary<string, bool> gateData)
            {
                DeserializeAll(gateData);
            }
        }
    }
}
