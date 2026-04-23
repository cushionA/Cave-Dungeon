using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// SaveSlotDataのディスク永続化を担当する純ロジッククラス。
    /// JSON変換にはNewtonsoft.Jsonを使用。
    /// entries の Dictionary&lt;string, object&gt; を型情報付きで保存・復元する。
    ///
    /// 破損対策:
    /// - 保存前に既存ファイルを .bak にコピーして一世代バックアップを残す。
    /// - 読み込みでJSONパース失敗 / 必須フィールド欠落を検知した場合は .bak からフォールバック読み込みする。
    /// - .bak も壊れていれば null を返す（呼び出し側が空データとして扱う従来動作）。
    /// </summary>
    public class SaveDataStore
    {
        public const int k_CurrentVersion = 1;
        public const string k_BackupExtension = ".bak";

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

        /// <summary>SaveSlotDataをJSONファイルとしてディスクに書き出す。
        /// 既存ファイルがあれば .bak にコピーしてから上書きする。</summary>
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
                string typeName = kvp.Value != null ? kvp.Value.GetType().FullName : "null";

                fileData.entries[kvp.Key] = new SaveEntryData
                {
                    typeName = typeName,
                    json = json
                };
            }

            string fileJson = JsonConvert.SerializeObject(fileData, Formatting.Indented);
            string filePath = GetSlotFilePath(slotData.slotIndex);

            // 既存ファイルがある場合は .bak を作成してから上書きする
            if (File.Exists(filePath))
            {
                string backupPath = GetBackupFilePath(slotData.slotIndex);
                try
                {
                    File.Copy(filePath, backupPath, true);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[SaveDataStore] バックアップ作成に失敗しました (slot={slotData.slotIndex}): {ex.Message}");
                }
            }

            // TODO (R6): 書き込み中の電源断で filePath が破損するリスクあり。
            //   アトミック書き込みは以下の 3 段で実現できるが、実装優先度は低いため後続 PR。
            //   1. temp パス (filePath + ".tmp") に書き込み
            //   2. File.Move(filePath, backupPath) で現行ファイルを bak へ移動
            //   3. File.Move(tempPath, filePath) で temp を正ファイル名へ確定
            //   現状の .bak フォールバックで前回成功状態へ復旧可能なため致命的ではない。
            File.WriteAllText(filePath, fileJson);
        }

        /// <summary>ディスクからJSONファイルを読み込みSaveSlotDataに復元する。
        /// メインファイルのパース/検証に失敗したら .bak からフォールバック読み込みする。
        /// 両方とも失敗した場合は null を返す。</summary>
        public SaveSlotData ReadFromDisk(int slotIndex)
        {
            string filePath = GetSlotFilePath(slotIndex);
            string backupPath = GetBackupFilePath(slotIndex);

            // まずメインファイルから読み込みを試みる
            if (File.Exists(filePath))
            {
                SaveSlotData mainData = TryReadSlotFile(filePath, out string mainError);
                if (mainData != null)
                {
                    return mainData;
                }

                Debug.LogError($"[SaveDataStore] セーブデータの読み込みに失敗しました (slot={slotIndex}, path='{filePath}'): {mainError}");
            }
            else if (!File.Exists(backupPath))
            {
                // メインも .bak もなければ「セーブ未存在」なので null (エラーではない)
                return null;
            }

            // メインが壊れている/存在しないので .bak からフォールバック
            if (File.Exists(backupPath))
            {
                SaveSlotData backupData = TryReadSlotFile(backupPath, out string backupError);
                if (backupData != null)
                {
                    Debug.LogError($"[SaveDataStore] .bak からフォールバック復元しました (slot={slotIndex}, path='{backupPath}')");
                    return backupData;
                }

                Debug.LogError($"[SaveDataStore] .bak も読み込みに失敗しました (slot={slotIndex}, path='{backupPath}'): {backupError}");
            }

            return null;
        }

        /// <summary>スロットファイルを削除する。.bak も同時に削除する。</summary>
        public void DeleteSlot(int slotIndex)
        {
            string filePath = GetSlotFilePath(slotIndex);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            string backupPath = GetBackupFilePath(slotIndex);
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }
        }

        /// <summary>スロットファイルが存在するか。</summary>
        public bool HasSlotFile(int slotIndex)
        {
            return File.Exists(GetSlotFilePath(slotIndex));
        }

        /// <summary>
        /// 指定パスのJSONファイルを読み込んで SaveSlotData に復元する。
        /// 成功時は SaveSlotData、失敗時は null を返し errorMessage にエラー内容を格納する。
        /// </summary>
        private static SaveSlotData TryReadSlotFile(string filePath, out string errorMessage)
        {
            errorMessage = null;

            string fileJson;
            try
            {
                fileJson = File.ReadAllText(filePath);
            }
            catch (Exception ex)
            {
                errorMessage = $"ファイル読み込み例外: {ex.Message}";
                return null;
            }

            if (string.IsNullOrWhiteSpace(fileJson))
            {
                errorMessage = "ファイルが空です";
                return null;
            }

            SaveFileData fileData;
            try
            {
                fileData = JsonConvert.DeserializeObject<SaveFileData>(fileJson);
            }
            catch (Exception ex)
            {
                errorMessage = $"JSONパース例外: {ex.Message}";
                return null;
            }

            if (fileData == null)
            {
                errorMessage = "JSONパース結果がnull";
                return null;
            }

            // 必須フィールド検証 (entriesがnullなら壊れていると判断)
            if (fileData.entries == null)
            {
                errorMessage = "必須フィールド 'entries' がnull";
                return null;
            }

            SaveSlotData slotData = new SaveSlotData(fileData.slotIndex);
            slotData.timestamp = fileData.timestamp;

            foreach (KeyValuePair<string, SaveEntryData> kvp in fileData.entries)
            {
                SaveEntryData entryData = kvp.Value;
                if (entryData == null)
                {
                    continue;
                }

                if (entryData.typeName == "null")
                {
                    slotData.entries[kvp.Key] = null;
                    continue;
                }

                Type type = ResolveType(entryData.typeName);
                if (type != null)
                {
                    try
                    {
                        object value = JsonConvert.DeserializeObject(entryData.json, type);
                        slotData.entries[kvp.Key] = value;
                    }
                    catch (Exception ex)
                    {
                        errorMessage = $"エントリー'{kvp.Key}'のデシリアライズに失敗: {ex.Message}";
                        return null;
                    }
                }
                else
                {
                    // 型が見つからない場合はJToken(JObject/JArray/JValue)のまま保持
                    try
                    {
                        slotData.entries[kvp.Key] = JsonConvert.DeserializeObject(entryData.json);
                    }
                    catch (Exception ex)
                    {
                        errorMessage = $"エントリー'{kvp.Key}'のJToken変換に失敗: {ex.Message}";
                        return null;
                    }
                }
            }

            return slotData;
        }

        /// <summary>
        /// FullNameから型を解決する。Type.GetTypeで見つからない場合は
        /// 全アセンブリを検索するフォールバックを行う。
        ///
        /// ⚠ セキュリティ (R2): 任意の typeName を受け入れて全アセンブリから型を検索するため、
        ///   save ファイルを偽造されれば任意型のインスタンス化経路が作れる (Newtonsoft.Json
        ///   TypeNameHandling.All 相当のリスク)。
        ///   現状はローカルセーブ前提で現実的リスクは低いが、以下の場面では許可リスト制へ移行すること:
        ///     - クラウドセーブ導入 (他クライアント由来の save を読む)
        ///     - MOD 導入 (ユーザー提供 dll から型を解決可能にする必要がある場合)
        ///   実装案: ISaveable 実装型または [Serializable] 付き特定型のみ許可する SaveTypeRegistry。
        ///   詳細は docs/FUTURE_TASKS.md を参照。
        /// </summary>
        private static Type ResolveType(string typeName)
        {
            Type type = Type.GetType(typeName);
            if (type != null)
            {
                return type;
            }

            // FullNameの場合Type.GetTypeが失敗することがあるので全アセンブリから検索
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                type = assemblies[i].GetType(typeName);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private string GetSlotFilePath(int slotIndex)
        {
            return Path.Combine(_savesDir, $"slot_{slotIndex}.json");
        }

        private string GetBackupFilePath(int slotIndex)
        {
            return GetSlotFilePath(slotIndex) + k_BackupExtension;
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
