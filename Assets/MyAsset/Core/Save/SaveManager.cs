using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// セーブデータ管理（スロット制）。
    /// ISaveableを登録し、スロット単位でシリアライズ/デシリアライズを行う。
    /// ディスク永続化は SaveDataStore に分離。SaveManager はインメモリの Save/Load のみ責務を持つ。
    /// IGameSubManagerとしてGameManagerCoreに登録可能。
    /// </summary>
    public class SaveManager : IGameSubManager
    {
        public const int k_MaxSlots = 3;
        private const int k_InitOrder = 900;

        private readonly SaveSlotData[] _slots;
        private readonly List<ISaveable> _saveables;
        private readonly HashSet<string> _registeredIds;
        private int _activeSlotIndex;

        public int ActiveSlotIndex => _activeSlotIndex;
        public int InitOrder => k_InitOrder;

        public SaveManager()
        {
            _slots = new SaveSlotData[k_MaxSlots];
            _saveables = new List<ISaveable>();
            _registeredIds = new HashSet<string>();
            _activeSlotIndex = 0;
        }

        /// <summary>ISaveableを登録。SaveIdの重複は例外。同一インスタンスの再登録は無視。</summary>
        public void Register(ISaveable saveable)
        {
            if (saveable == null)
            {
                return;
            }

            // 同一インスタンスの再登録は無視
            if (_saveables.Contains(saveable))
            {
                return;
            }

            // SaveIdの重複チェック
            if (_registeredIds.Contains(saveable.SaveId))
            {
                throw new InvalidOperationException(
                    $"SaveId '{saveable.SaveId}' is already registered. Each ISaveable must have a unique SaveId.");
            }

            _saveables.Add(saveable);
            _registeredIds.Add(saveable.SaveId);
        }

        /// <summary>ISaveableを解除。シーン遷移時などに使用。</summary>
        public void Unregister(ISaveable saveable)
        {
            if (saveable == null)
            {
                return;
            }

            if (_saveables.Remove(saveable))
            {
                _registeredIds.Remove(saveable.SaveId);
            }
        }

        /// <summary>全ISaveableをシリアライズしてスロットに保存（インメモリのみ）。
        /// ディスク永続化が必要な呼び出し側は、返却された SaveSlotData を SaveDataStore に渡す。</summary>
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

        /// <summary>スロットからデータをロードして全ISaveableに復元（インメモリのみ）。
        /// ディスクからのロードは SaveDataStore.ReadFromDisk + SetSlotData 経由で行う。</summary>
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

        /// <summary>スロットにデータが存在するか（メモリのみ。ディスクの存在確認は SaveDataStore に委譲）</summary>
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

        /// <summary>スロットデータを外部から設定（SaveDataStoreからのロード時に使用）</summary>
        public void SetSlotData(int slotIndex, SaveSlotData data)
        {
            if (slotIndex < 0 || slotIndex >= k_MaxSlots)
            {
                return;
            }

            _slots[slotIndex] = data;
        }

        /// <summary>セーブデータをメモリから削除する。ディスクの削除は SaveDataStore に委譲する。</summary>
        public bool DeleteSaveData(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= k_MaxSlots)
            {
                return false;
            }

            _slots[slotIndex] = null;
            return true;
        }

        // ===== IGameSubManager =====

        public void Initialize(SoACharaDataDic data, GameEvents events)
        {
            // SaveManagerはSoAデータに依存しないが、
            // GameEventsのセーブ関連イベントを購読する場所として使用可能
        }

        public void Dispose()
        {
            _saveables.Clear();
            _registeredIds.Clear();
            for (int i = 0; i < _slots.Length; i++)
            {
                _slots[i] = null;
            }
        }
    }
}
