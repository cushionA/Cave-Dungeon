using System;
using System.Collections.Generic;

namespace Game.Core
{
    [Serializable]
    public class SaveSlotData
    {
        public int slotIndex;
        public string timestamp;
        public Dictionary<string, object> entries;

        public SaveSlotData(int slotIndex)
        {
            this.slotIndex = slotIndex;
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            entries = new Dictionary<string, object>();
        }
    }
}
