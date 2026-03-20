using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// ディスク永続化の抽象インターフェース。
    /// テスト時はインメモリ実装に差し替え可能。
    /// </summary>
    public interface ISaveFileIO
    {
        string ReadAll(string path);
        void WriteAll(string path, string content);
        bool Exists(string path);
        void Delete(string path);
    }

    /// <summary>
    /// セーブデータ管理（スロット制）。
    /// ISaveableを登録し、スロット単位でシリアライズ/デシリアライズを行う。
    /// ISaveFileIOが設定されている場合、ディスクに永続化する。
    /// </summary>
    public class SaveManager
    {
        public const int k_MaxSlots = 3;

        private readonly SaveSlotData[] _slots;
        private readonly List<ISaveable> _saveables;
        private int _activeSlotIndex;
        private ISaveFileIO _fileIO;
        private string _saveDirectory;

        public int ActiveSlotIndex => _activeSlotIndex;

        public SaveManager()
        {
            _slots = new SaveSlotData[k_MaxSlots];
            _saveables = new List<ISaveable>();
            _activeSlotIndex = 0;
        }

        /// <summary>ディスク永続化を有効にする</summary>
        public void SetFileIO(ISaveFileIO fileIO, string saveDirectory)
        {
            _fileIO = fileIO;
            _saveDirectory = saveDirectory;
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

            // ディスクに永続化
            WriteToDisk(slotIndex, slotData);

            return slotData;
        }

        /// <summary>スロットからデータをロードして全ISaveableに復元</summary>
        public bool Load(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= k_MaxSlots)
            {
                return false;
            }

            // メモリになければディスクから読み込み
            if (_slots[slotIndex] == null)
            {
                _slots[slotIndex] = ReadFromDisk(slotIndex);
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

        /// <summary>スロットにデータが存在するか（メモリまたはディスク）</summary>
        public bool HasSaveData(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= k_MaxSlots)
            {
                return false;
            }

            if (_slots[slotIndex] != null)
            {
                return true;
            }

            // ディスクにファイルが存在するか確認
            if (_fileIO != null && _saveDirectory != null)
            {
                return _fileIO.Exists(GetSlotPath(slotIndex));
            }

            return false;
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

        /// <summary>セーブデータを削除する</summary>
        public bool DeleteSaveData(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= k_MaxSlots)
            {
                return false;
            }

            _slots[slotIndex] = null;

            if (_fileIO != null && _saveDirectory != null)
            {
                string path = GetSlotPath(slotIndex);
                if (_fileIO.Exists(path))
                {
                    _fileIO.Delete(path);
                }
            }

            return true;
        }

        private string GetSlotPath(int slotIndex)
        {
            return _saveDirectory + "/save_slot_" + slotIndex + ".json";
        }

        private void WriteToDisk(int slotIndex, SaveSlotData slotData)
        {
            if (_fileIO == null || _saveDirectory == null)
            {
                return;
            }

            string json = UnityEngine.JsonUtility.ToJson(new SaveSlotWrapper(slotData), true);
            _fileIO.WriteAll(GetSlotPath(slotIndex), json);
        }

        private SaveSlotData ReadFromDisk(int slotIndex)
        {
            if (_fileIO == null || _saveDirectory == null)
            {
                return null;
            }

            string path = GetSlotPath(slotIndex);
            if (!_fileIO.Exists(path))
            {
                return null;
            }

            string json = _fileIO.ReadAll(path);
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            SaveSlotWrapper wrapper = UnityEngine.JsonUtility.FromJson<SaveSlotWrapper>(json);
            return wrapper?.ToSlotData();
        }
    }

    /// <summary>
    /// JsonUtility用のシリアライズ可能ラッパー。
    /// SaveSlotDataのDictionary&lt;string, object&gt;はJsonUtilityでは直接シリアライズ不可のため、
    /// キー・値ペアのリストに変換する。
    /// </summary>
    [System.Serializable]
    internal class SaveSlotWrapper
    {
        public int slotIndex;
        public string timestamp;
        public string[] keys;
        public string[] values;

        public SaveSlotWrapper() { }

        public SaveSlotWrapper(SaveSlotData data)
        {
            slotIndex = data.slotIndex;
            timestamp = data.timestamp;
            int count = data.entries.Count;
            keys = new string[count];
            values = new string[count];
            int idx = 0;
            foreach (System.Collections.Generic.KeyValuePair<string, object> kvp in data.entries)
            {
                keys[idx] = kvp.Key;
                values[idx] = kvp.Value != null ? UnityEngine.JsonUtility.ToJson(kvp.Value) : "";
                idx++;
            }
        }

        public SaveSlotData ToSlotData()
        {
            SaveSlotData data = new SaveSlotData(slotIndex);
            data.timestamp = timestamp;
            if (keys != null)
            {
                for (int i = 0; i < keys.Length; i++)
                {
                    data.entries[keys[i]] = values[i];
                }
            }
            return data;
        }
    }
}
