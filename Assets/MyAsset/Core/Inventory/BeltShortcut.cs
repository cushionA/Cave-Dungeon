namespace Game.Core
{
    /// <summary>
    /// ベルトショートカット。可変スロット数、ループ回転でアイテムを切り替える。
    /// </summary>
    public class BeltShortcut
    {
        private const int k_EmptySlot = -1;

        private int[] _slots;
        private int _activeIndex;

        public int SlotCount => _slots.Length;
        public int ActiveIndex => _activeIndex;
        public int ActiveItemId => _slots[_activeIndex];

        public BeltShortcut(int slotCount)
        {
            _slots = new int[slotCount];
            _activeIndex = 0;
            for (int i = 0; i < slotCount; i++)
            {
                _slots[i] = k_EmptySlot;
            }
        }

        /// <summary>スロットにアイテムIDをセット</summary>
        public void SetSlot(int slotIndex, int itemId)
        {
            _slots[slotIndex] = itemId;
        }

        /// <summary>スロットをクリア（-1に）</summary>
        public void ClearSlot(int slotIndex)
        {
            _slots[slotIndex] = k_EmptySlot;
        }

        /// <summary>次スロットへ（ループ）</summary>
        public void Next()
        {
            _activeIndex = (_activeIndex + 1) % _slots.Length;
        }

        /// <summary>前スロットへ（ループ）</summary>
        public void Prev()
        {
            _activeIndex = (_activeIndex - 1 + _slots.Length) % _slots.Length;
        }
    }
}
