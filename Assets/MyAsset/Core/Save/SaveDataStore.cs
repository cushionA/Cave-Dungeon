using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Game.Core
{
    /// <summary>
    /// SaveSlotDataのディスク永続化を担当する純ロジッククラス。
    /// JSON変換にはNewtonsoft.Jsonを使用。
    /// entries の Dictionary&lt;string, object&gt; を型情報付きで保存・復元する。
    /// </summary>
    public class SaveDataStore
    {
        public const int k_CurrentVersion = 1;

        private readonly string _basePath;
        private readonly string _savesDir;

        /// <summary>
        /// コンストラクタ。basePath配下に saves/ ディレクトリを作成する。
        /// </summary>
        public SaveDataStore(string basePath)
        {
            _basePath = basePath;
            _savesDir = Path.Combine(_basePath, "saves");
        }

        /// <summary>SaveSlotDataをJSONファイルとしてディスクに書き出す。</summary>
        public void WriteToDisk(SaveSlotData slotData)
        {
            if (slotData == null)
            {
                return;
            }

            if (!Directory.Exists(_savesDir))
            {
                Directory.CreateDirectory(_savesDir);
            }

            SaveFileData fileData = new SaveFileData
            {
                version = k_CurrentVersion,
                slotIndex = slotData.slotIndex,
                timestamp = slotData.timestamp,
                entries = new Dictionary<string, SaveEntryData>()
            };

            foreach (KeyValuePair<string, object> kvp in slotData.entries)
            {
                string json = JsonConvert.SerializeObject(kvp.Value);
                string typeName = kvp.Value != null ? kvp.Value.GetType().AssemblyQualifiedName : "null";

                fileData.entries[kvp.Key] = new SaveEntryData
                {
                    typeName = typeName,
                    json = json
                };
            }

            string fileJson = JsonConvert.SerializeObject(fileData, Formatting.Indented);
            string filePath = GetSlotFilePath(slotData.slotIndex);
            File.WriteAllText(filePath, fileJson);
        }

        /// <summary>ディスクからJSONファイルを読み込みSaveSlotDataに復元する。</summary>
        public SaveSlotData ReadFromDisk(int slotIndex)
        {
            string filePath = GetSlotFilePath(slotIndex);

            if (!File.Exists(filePath))
            {
                return null;
            }

            string fileJson = File.ReadAllText(filePath);
            SaveFileData fileData = JsonConvert.DeserializeObject<SaveFileData>(fileJson);

            if (fileData == null)
            {
                return null;
            }

            SaveSlotData slotData = new SaveSlotData(fileData.slotIndex);
            slotData.timestamp = fileData.timestamp;

            if (fileData.entries != null)
            {
                foreach (KeyValuePair<string, SaveEntryData> kvp in fileData.entries)
                {
                    SaveEntryData entryData = kvp.Value;
                    if (entryData.typeName == "null")
                    {
                        slotData.entries[kvp.Key] = null;
                        continue;
                    }

                    Type type = Type.GetType(entryData.typeName);
                    if (type != null)
                    {
                        object value = JsonConvert.DeserializeObject(entryData.json, type);
                        slotData.entries[kvp.Key] = value;
                    }
                    else
                    {
                        // 型が見つからない場合はJToken(JObject/JArray/JValue)のまま保持
                        slotData.entries[kvp.Key] = JsonConvert.DeserializeObject(entryData.json);
                    }
                }
            }

            return slotData;
        }

        /// <summary>スロットファイルを削除する。</summary>
        public void DeleteSlot(int slotIndex)
        {
            string filePath = GetSlotFilePath(slotIndex);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }

        /// <summary>スロットファイルが存在するか。</summary>
        public bool HasSlotFile(int slotIndex)
        {
            return File.Exists(GetSlotFilePath(slotIndex));
        }

        private string GetSlotFilePath(int slotIndex)
        {
            return Path.Combine(_savesDir, $"slot_{slotIndex}.json");
        }

        // ===== 内部データ構造 =====

        [Serializable]
        private class SaveFileData
        {
            public int version;
            public int slotIndex;
            public string timestamp;
            public Dictionary<string, SaveEntryData> entries;
        }

        [Serializable]
        private class SaveEntryData
        {
            public string typeName;
            public string json;
        }
    }
}
