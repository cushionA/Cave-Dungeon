using System;

namespace Game.Core
{
    [Serializable]
    public struct ItemEntry
    {
        public int itemId;
        public ItemCategory category;
        public int count;
        public int maxStack;
    }
}
