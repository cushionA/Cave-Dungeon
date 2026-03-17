using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// セーブデータ管理（スロット制）。
    /// ISaveableを登録し、スロット単位でシリアライズ/デシリアライズを行う。
    /// </summary>
    public class SaveManager
    {
        public const int k_MaxSlots = 3;

        private readonly SaveSlotData[] _slots;
        private readonly List<ISaveable> _saveables;
        private int _activeSlotIndex;

        public int ActiveSlotIndex => _activeSlotIndex;

        public SaveManager()
        {
            _slots = new SaveSlotData[k_MaxSlots];
            _saveables = new List<ISaveable>();
            _activeSlotIndex = 0;
        }

        /// <summary>ISaveableを登録</summary>
        public void Register(ISaveable saveable)
        {
            if (saveable == null)
            {
                return;
            }

            _saveables.Add(saveable);
        }

        /// <summary>全ISaveableをシリアライズしてスロットに保存</summary>
        public SaveSlotData Save(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= k_MaxSlots)
            {
                return null;
            }

            SaveSlotData slotData = new SaveSlotData(slotIndex);

            for (int i = 0; i < _saveables.Count; i++)
            {
                ISaveable saveable = _saveables[i];
                slotData.entries[saveable.SaveId] = saveable.Serialize();
            }

            _slots[slotIndex] = slotData;
            return slotData;
        }

        /// <summary>スロットからデータをロードして全ISaveableに復元</summary>
        public bool Load(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= k_MaxSlots)
            {
                return false;
            }

            SaveSlotData slotData = _slots[slotIndex];
            if (slotData == null)
            {
                return false;
            }

            for (int i = 0; i < _saveables.Count; i++)
            {
                ISaveable saveable = _saveables[i];
                if (slotData.entries.TryGetValue(saveable.SaveId, out object data))
                {
                    saveable.Deserialize(data);
                }
            }

            _activeSlotIndex = slotIndex;
            return true;
        }

        /// <summary>スロットにデータが存在するか</summary>
        public bool HasSaveData(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= k_MaxSlots)
            {
                return false;
            }

            return _slots[slotIndex] != null;
        }

        /// <summary>アクティブスロット切替</summary>
        public void SetActiveSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= k_MaxSlots)
            {
                return;
            }

            _activeSlotIndex = slotIndex;
        }

        /// <summary>スロットデータ取得（表示用）</summary>
        public SaveSlotData GetSlotData(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= k_MaxSlots)
            {
                return null;
            }

            return _slots[slotIndex];
        }
    }
}
