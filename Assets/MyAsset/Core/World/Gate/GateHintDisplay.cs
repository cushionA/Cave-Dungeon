using System;

namespace Game.Core
{
    /// <summary>
    /// Provides hint text for locked gates and map icon updates.
    /// UI layer subscribes to events to display visual feedback.
    /// </summary>
    public class GateHintDisplay
    {
        /// <summary>
        /// Fired when a hint should be shown. Parameters: gateId, hintText.
        /// </summary>
        public event Action<string, string> OnHintRequested;

        /// <summary>
        /// Fired when a gate's map icon state changes. Parameters: gateId, isOpen.
        /// </summary>
        public event Action<string, bool> OnMapIconUpdated;

        /// <summary>
        /// Shows the hint for a closed gate. Does nothing if the gate is already open.
        /// </summary>
        public void ShowHint(GateStateController gate)
        {
            if (gate.IsOpen)
            {
                return;
            }

            OnHintRequested?.Invoke(gate.Definition.gateId, gate.HintText);
        }

        /// <summary>
        /// Notifies the map layer to update a gate icon.
        /// </summary>
        public void UpdateMapIcon(string gateId, bool isOpen)
        {
            OnMapIconUpdated?.Invoke(gateId, isOpen);
        }
    }
}
