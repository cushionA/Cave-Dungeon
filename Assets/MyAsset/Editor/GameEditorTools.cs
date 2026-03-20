#if UNITY_EDITOR
using System.Linq;
using UnityEngine;
using UnityEditor;
using Game.Core;
using Game.Runtime;
using CharacterInfo = Game.Core.CharacterInfo;

namespace Game.Editor
{
    /// <summary>
    /// ゲーム開発用エディタユーティリティ。
    /// メニューとコンテキストメニューから各種便利機能を提供する。
    /// </summary>
    public static class GameEditorTools
    {
        // ─────────────────────────────────────────────
        //  ScriptableObject一括作成
        // ─────────────────────────────────────────────

        [MenuItem("Game/作成/CharacterInfo", priority = 100)]
        public static void CreateCharacterInfo()
        {
            CreateAssetInSelectedFolder<CharacterInfo>("NewCharacterInfo");
        }

        [MenuItem("Game/作成/AIInfo", priority = 101)]
        public static void CreateAIInfo()
        {
            CreateAssetInSelectedFolder<AIInfo>("NewAIInfo");
        }

        [MenuItem("Game/作成/AttackInfo", priority = 102)]
        public static void CreateAttackInfo()
        {
            CreateAssetInSelectedFolder<AttackInfo>("NewAttackInfo");
        }

        [MenuItem("Game/作成/ChallengeDefinition", priority = 103)]
        public static void CreateChallengeDefinition()
        {
            CreateAssetInSelectedFolder<ChallengeDefinition>("NewChallengeDefinition");
        }

        // ─────────────────────────────────────────────
        //  バリデーション
        // ─────────────────────────────────────────────

        [MenuItem("Game/検証/全CharacterInfoを検証", priority = 200)]
        public static void ValidateAllCharacterInfos()
        {
            string[] guids = AssetDatabase.FindAssets("t:CharacterInfo");
            int errorCount = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                CharacterInfo info = AssetDatabase.LoadAssetAtPath<CharacterInfo>(path);

                if (info.maxHp <= 0)
                {
                    Debug.LogError($"[検証エラー] {path}: maxHp が0以下", info);
                    errorCount++;
                }
                if (info.moveSpeed <= 0)
                {
                    Debug.LogWarning($"[検証警告] {path}: moveSpeed が0以下", info);
                }
            }

            Debug.Log($"CharacterInfo検証完了: {guids.Length}件中 {errorCount}件のエラー");
        }

        [MenuItem("Game/検証/全AIInfoを検証", priority = 201)]
        public static void ValidateAllAIInfos()
        {
            string[] guids = AssetDatabase.FindAssets("t:AIInfo");
            int errorCount = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                AIInfo info = AssetDatabase.LoadAssetAtPath<AIInfo>(path);

                if (info.modes == null || info.modes.Length == 0)
                {
                    Debug.LogError($"[検証エラー] {path}: モード未設定", info);
                    errorCount++;
                }

                if (info.actDataList != null)
                {
                    for (int i = 0; i < info.actDataList.Length; i++)
                    {
                        if (string.IsNullOrEmpty(info.actDataList[i].actName))
                        {
                            Debug.LogWarning($"[検証警告] {path}: actDataList[{i}] の名前が未設定", info);
                        }
                    }
                }
            }

            Debug.Log($"AIInfo検証完了: {guids.Length}件中 {errorCount}件のエラー");
        }

        [MenuItem("Game/検証/全AttackInfoを検証", priority = 202)]
        public static void ValidateAllAttackInfos()
        {
            string[] guids = AssetDatabase.FindAssets("t:AttackInfo");
            int errorCount = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                AttackInfo info = AssetDatabase.LoadAssetAtPath<AttackInfo>(path);

                if (string.IsNullOrEmpty(info.attackName))
                {
                    Debug.LogError($"[検証エラー] {path}: attackName が未設定", info);
                    errorCount++;
                }
                if (info.damageMultiplier <= 0)
                {
                    Debug.LogError($"[検証エラー] {path}: damageMultiplier が0以下", info);
                    errorCount++;
                }
                if (info.projectileInfo.hasProjectile && info.projectileInfo.projectilePrefab == null)
                {
                    Debug.LogError($"[検証エラー] {path}: 飛翔体有効だが prefab 未設定", info);
                    errorCount++;
                }
            }

            Debug.Log($"AttackInfo検証完了: {guids.Length}件中 {errorCount}件のエラー");
        }

        // ─────────────────────────────────────────────
        //  シーンユーティリティ
        // ─────────────────────────────────────────────

        [MenuItem("Game/シーン/プレースホルダー一覧", priority = 300)]
        public static void ListPlaceholders()
        {
            GameObject[] allObjects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            GameObject[] placeholders = allObjects
                .Where(go => go.name.StartsWith("[PLACEHOLDER]"))
                .ToArray();

            if (placeholders.Length == 0)
            {
                Debug.Log("プレースホルダーはありません。");
                return;
            }

            Debug.Log($"プレースホルダー: {placeholders.Length}件");
            foreach (GameObject ph in placeholders)
            {
                Debug.Log($"  - {ph.name} ({GetHierarchyPath(ph)})", ph);
            }
        }

        [MenuItem("Game/シーン/CharacterInfo未設定のキャラクター検出", priority = 301)]
        public static void FindCharactersWithoutInfo()
        {
            BaseCharacter[] characters = Object.FindObjectsByType<BaseCharacter>(FindObjectsSortMode.None);
            int missingCount = 0;

            foreach (BaseCharacter character in characters)
            {
                SerializedObject so = new SerializedObject(character);
                SerializedProperty infoProp = so.FindProperty("_characterInfo");
                if (infoProp != null && infoProp.objectReferenceValue == null)
                {
                    Debug.LogWarning($"CharacterInfo未設定: {GetHierarchyPath(character.gameObject)}", character);
                    missingCount++;
                }
            }

            Debug.Log($"検出完了: {characters.Length}キャラ中 {missingCount}件がCharacterInfo未設定");
        }

        [MenuItem("Game/シーン/Missing参照を検出", priority = 302)]
        public static void FindMissingReferences()
        {
            GameObject[] allObjects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            int missingCount = 0;

            foreach (GameObject go in allObjects)
            {
                Component[] components = go.GetComponents<Component>();
                for (int i = 0; i < components.Length; i++)
                {
                    if (components[i] == null)
                    {
                        Debug.LogError($"Missing Component: {GetHierarchyPath(go)} [index {i}]", go);
                        missingCount++;
                        continue;
                    }

                    SerializedObject so = new SerializedObject(components[i]);
                    SerializedProperty prop = so.GetIterator();
                    while (prop.NextVisible(true))
                    {
                        if (prop.propertyType == SerializedPropertyType.ObjectReference
                            && prop.objectReferenceValue == null
                            && prop.objectReferenceInstanceIDValue != 0)
                        {
                            Debug.LogWarning(
                                $"Missing参照: {GetHierarchyPath(go)} → " +
                                $"{components[i].GetType().Name}.{prop.name}", go);
                            missingCount++;
                        }
                    }
                }
            }

            Debug.Log($"Missing参照検出完了: {missingCount}件");
        }

        // ─────────────────────────────────────────────
        //  選択オブジェクト操作
        // ─────────────────────────────────────────────

        [MenuItem("Game/選択/コンポーネント一覧をログ出力", priority = 400)]
        public static void LogSelectedComponents()
        {
            if (Selection.activeGameObject == null)
            {
                Debug.LogWarning("GameObjectを選択してください。");
                return;
            }

            GameObject go = Selection.activeGameObject;
            Component[] components = go.GetComponents<Component>();
            Debug.Log($"--- {go.name} のコンポーネント ({components.Length}個) ---");
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] == null)
                {
                    Debug.LogWarning($"  [{i}] (Missing)");
                }
                else
                {
                    Debug.Log($"  [{i}] {components[i].GetType().FullName}");
                }
            }
        }

        [MenuItem("Game/選択/コンポーネント一覧をログ出力", true)]
        public static bool LogSelectedComponentsValidation()
        {
            return Selection.activeGameObject != null;
        }

        // ─────────────────────────────────────────────
        //  ヘルパー
        // ─────────────────────────────────────────────

        private static void CreateAssetInSelectedFolder<T>(string defaultName) where T : ScriptableObject
        {
            string folderPath = "Assets/MyAsset/Data";
            if (Selection.activeObject != null)
            {
                string selectedPath = AssetDatabase.GetAssetPath(Selection.activeObject);
                if (AssetDatabase.IsValidFolder(selectedPath))
                {
                    folderPath = selectedPath;
                }
                else if (!string.IsNullOrEmpty(selectedPath))
                {
                    folderPath = System.IO.Path.GetDirectoryName(selectedPath);
                }
            }

            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                folderPath = "Assets/MyAsset/Data";
                if (!AssetDatabase.IsValidFolder(folderPath))
                {
                    AssetDatabase.CreateFolder("Assets/MyAsset", "Data");
                }
            }

            T asset = ScriptableObject.CreateInstance<T>();
            string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{folderPath}/{defaultName}.asset");
            AssetDatabase.CreateAsset(asset, assetPath);
            AssetDatabase.SaveAssets();
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = asset;
            Debug.Log($"{typeof(T).Name} を作成: {assetPath}");
        }

        private static string GetHierarchyPath(GameObject go)
        {
            string path = go.name;
            Transform parent = go.transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }
    }
}
#endif
