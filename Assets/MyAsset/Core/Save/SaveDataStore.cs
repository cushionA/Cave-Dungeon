using System;
using System.Collections.Generic;
using System.IO;
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
    /// 書き込み (アトミック方式):
    /// 1. 一時ファイル (filePath + ".tmp") へ本体を書き込み。
    /// 2. filePath が既存なら File.Replace(temp, filePath, backup) で置換 + 旧ファイルを .bak に退避 (単一 OS 命令)。
    ///    filePath 不在 (初回 / 復旧後) なら .tmp → filePath を File.Move。.bak は既存があれば手付かずで残す。
    /// これにより電源断や異常終了時でも filePath と backupPath のどちらかは整合した状態で残る。
    /// File.Replace は OS が提供するアトミック置換であり、旧実装の「Step 2 で .bak を delete してから Move」
    /// による .bak 自殺シナリオ (Issue #74) を構造的に排除する。
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
        public const string k_TempExtension = ".tmp";

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
        /// アトミック書き込み: .tmp に本体を書く → File.Replace で filePath を置換 + 旧ファイルを .bak に退避。
        /// filePath 不在時 (初回 / 復旧後) は .tmp → filePath を File.Move し、既存 .bak は破壊しない。
        /// 途中で電源断しても filePath または .bak のどちらかは整合した状態で残る。</summary>
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
            string backupPath = GetBackupFilePath(slotData.slotIndex);
            string tempPath = GetTempFilePath(slotData.slotIndex);

            // アトミック書き込み:
            //   1. 一時ファイルに本体を書く (ここで失敗しても filePath と .bak は無傷)
            //   2. filePath が既存なら File.Replace(temp, file, bak) で 1 命令置換 + バックアップ
            //      filePath 不在なら File.Move(temp → file) のみ (既存 .bak は手付かずで残す)
            //
            // File.Replace は OS が提供するアトミック置換であり、旧実装の「.bak を delete してから Move」
            // 中間状態を作らない。これにより前回 commit が中途半端に終わって filePath 不在 + .bak だけ
            // 残った状態でも、次の保存が .bak を破壊することがない (Issue #74)。
            //
            // 途中で異常終了しても:
            //   - Step 1 中: filePath/.bak 無傷。.tmp 残骸は次回書き込みで上書きされる
            //   - Step 2 中の crash:
            //       - File.Replace 中: OS の atomic 性により filePath は旧 or 新のどちらか整合状態
            //       - File.Move (filePath 不在経路) 中: filePath が一時的に不在だが .bak は残る → 読み込み時に .bak フォールバック

            // Step 1: 一時ファイルへ書き込み (前回残骸があれば上書きされる)
            try
            {
                File.WriteAllText(tempPath, fileJson);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveDataStore] 一時ファイル書き込みに失敗しました (slot={slotData.slotIndex}, path='{tempPath}'): {ex.Message}");
                return;
            }

            // Step 2: アトミック置換
            try
            {
                if (File.Exists(filePath))
                {
                    // 既存ファイルあり: File.Replace で「filePath を tempPath で置換 + 旧 filePath を backupPath へ退避」を 1 命令で行う。
                    // backupPath が既に存在する場合は File.Replace が暗黙的に上書きする (前世代 .bak は最新の前世代に更新される)。
                    File.Replace(tempPath, filePath, backupPath);
                }
                else
                {
                    // filePath 不在 (初回 / 前回 commit が中途半端に終わった状態) は File.Replace が使えない (FileNotFoundException)。
                    // .tmp → filePath を Move するのみで、既存 .bak は手付かずで残す。
                    // これにより「filePath 不在 + .bak 残存」状態での再保存で .bak が破壊されない (Issue #74)。
                    File.Move(tempPath, filePath);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveDataStore] アトミック置換に失敗しました (slot={slotData.slotIndex}, temp='{tempPath}', dest='{filePath}'): {ex.Message}");
                TryDeleteTemp(tempPath);
            }
        }

        /// <summary>書き込み失敗後の .tmp 残骸を best-effort で削除する。</summary>
        private static void TryDeleteTemp(string tempPath)
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveDataStore] 一時ファイルの削除にも失敗しました (path='{tempPath}'): {ex.Message}");
            }
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
        /// 許可リスト制で typeName を Type に解決する。<see cref="SaveTypeRegistry"/> に委譲する。
        ///
        /// なぜ許可リスト制か:
        /// 旧実装は全アセンブリから任意型を Assembly.GetType で検索していたため、save ファイルを
        /// 偽造されれば任意型のインスタンス化経路ができた (Newtonsoft.Json TypeNameHandling.All 相当)。
        /// SaveTypeRegistry は ISaveable 実装型 + primitive/BCL 型のみ許可することで危険型の
        /// インスタンス化を遮断する。未許可型は null を返し、呼び出し側で JToken フォールバックに乗せる。
        /// </summary>
        private static Type ResolveType(string typeName)
        {
            if (SaveTypeRegistry.TryResolve(typeName, out Type type))
            {
                return type;
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

        private string GetTempFilePath(int slotIndex)
        {
            return GetSlotFilePath(slotIndex) + k_TempExtension;
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
