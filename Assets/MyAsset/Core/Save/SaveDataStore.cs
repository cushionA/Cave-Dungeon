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
    /// 書き込み (3 段アトミック方式):
    /// 1. 一時ファイル (filePath + ".tmp") へ本体を書き込み。
    /// 2. 既存 filePath を backupPath (".bak") へ File.Move で退避 (初回は skip)。
    /// 3. 一時ファイルを filePath へ File.Move で昇格。
    /// これにより電源断や異常終了時でも filePath と backupPath のどちらかは
    /// 整合した状態で残る (filePath 破損時は .bak フォールバックで復旧可)。
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
        /// 3 段アトミック書き込み: .tmp に本体を書く → 既存ファイルを .bak へ Move → .tmp を filePath へ Move。
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

            // 3 段アトミック書き込み:
            //   1. 一時ファイルに本体を書く (ここで失敗しても filePath と .bak は無傷)
            //   2. 既存 filePath を .bak へ Move (.bak は前世代の内容で上書きされる / 初回は skip)
            //   3. 一時ファイルを filePath へ Move (この瞬間に本体が切り替わる)
            //
            // 途中で異常終了しても:
            //   - Step 1 中: filePath/.bak 無傷。.tmp 残骸は次回書き込みで自動的に上書きされる
            //     (起動時の明示的な残骸検知はしない。読み込み経路は filePath/.bak のみ参照する)
            //   - Step 2 後 / Step 3 前: filePath 不在、.bak に直前世代あり、.tmp に新バージョンあり
            //       → 読み込み時に filePath が無ければ .bak フォールバック経路で復旧
            //   - Step 3 後: 新 filePath + 直前 .bak が揃う (理想形)

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

            // Step 2: 既存 filePath を .bak へ退避 (初回書き込み時は filePath が存在しないので skip)
            if (File.Exists(filePath))
            {
                try
                {
                    // .NET Standard 2.0 互換: File.Move(src, dest, overwrite: true) が無いため
                    // 既存 .bak を明示削除してから Move する (前世代 .bak を上書きする意図を維持)
                    if (File.Exists(backupPath))
                    {
                        File.Delete(backupPath);
                    }
                    File.Move(filePath, backupPath);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[SaveDataStore] 既存ファイルの .bak 退避に失敗しました (slot={slotData.slotIndex}): {ex.Message}");
                    // filePath は無傷 (前世代データが残る)。.tmp はこのタイミングで明示削除し残骸を残さない
                    TryDeleteTemp(tempPath);
                    return;
                }
            }

            // Step 3: 一時ファイルを filePath へ昇格
            try
            {
                // Step 2 後の filePath は不在のはずだが、念のため明示削除で冪等性を確保 (NS2.0 互換)
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
                File.Move(tempPath, filePath);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveDataStore] 一時ファイルの本体昇格に失敗しました (slot={slotData.slotIndex}, temp='{tempPath}', dest='{filePath}'): {ex.Message}");
                // このとき filePath は存在しない (Step 2 で .bak へ移動済み)。.bak から復旧可能。
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
