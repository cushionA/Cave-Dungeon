using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.SceneManagement;
using Game.Core;
using Game.Runtime;
using CharacterInfo = Game.Core.CharacterInfo;

namespace Game.Editor
{
    /// <summary>
    /// プロトタイプシーンのセットアップエディタツール。
    /// メニューから実行して、動作確認用シーンを自動構築する。
    /// </summary>
    public static class PrototypeSceneSetup
    {
        [MenuItem("Game/プロトタイプシーン構築")]
        public static void SetupPrototypeScene()
        {
            // 新規シーン作成
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // レイヤー設定確認
            SetupLayers();

            // GameManager
            GameObject gmObj = new GameObject("[PLACEHOLDER]GameManager");
            gmObj.AddComponent<GameManager>();

            // CharacterInfo SOアセット作成
            CharacterInfo playerInfo = CreateOrLoadCharacterInfo("Assets/MyAsset/Data/PlayerInfo.asset",
                CharacterBelong.Ally, CharacterFeature.Player, 100, 6f, 3.5f, 12f);
            CharacterInfo companionInfo = CreateOrLoadCharacterInfo("Assets/MyAsset/Data/CompanionInfo.asset",
                CharacterBelong.Ally, CharacterFeature.Companion, 80, 5f, 3.0f, 10f);
            CharacterInfo enemyInfo = CreateOrLoadCharacterInfo("Assets/MyAsset/Data/BasicEnemyInfo.asset",
                CharacterBelong.Enemy, CharacterFeature.Minion, 50, 3f, 2.0f, 6f);

            // 地面
            GameObject ground = CreateGround();

            // プレイヤー
            GameObject player = CreatePlayer(playerInfo);

            // 仲間
            GameObject companion = CreateCompanion(companionInfo, player.transform);

            // 敵
            GameObject enemy = CreateEnemy(enemyInfo);

            // カメラ設定
            SetupCamera(player.transform);

            // HUD
            SetupHUD();

            // ダメージポップアップ
            GameObject popupController = new GameObject("[PLACEHOLDER]DamagePopups");
            popupController.AddComponent<DamagePopupController>();

            // シーン保存
            EnsureDirectoryExists("Assets/Scenes");
            EditorSceneManager.SaveScene(
                EditorSceneManager.GetActiveScene(),
                "Assets/Scenes/PrototypeScene.unity");

            Debug.Log("[PrototypeSceneSetup] プロトタイプシーン構築完了！");
        }

        private static void SetupLayers()
        {
            // Layer 6 = Ground の設定（手動確認が必要な場合あり）
            SerializedObject tagManager = new SerializedObject(
                AssetDatabase.LoadMainAssetAtPath("ProjectSettings/TagManager.asset"));
            SerializedProperty layers = tagManager.FindProperty("layers");

            SetLayer(layers, 6, "Ground");
            SetLayer(layers, 7, "Player");
            SetLayer(layers, 8, "Enemy");
            SetLayer(layers, 9, "Companion");
            SetLayer(layers, 10, "PlayerHitbox");
            SetLayer(layers, 11, "EnemyHitbox");

            tagManager.ApplyModifiedProperties();
        }

        private static void SetLayer(SerializedProperty layers, int index, string name)
        {
            SerializedProperty layer = layers.GetArrayElementAtIndex(index);
            if (string.IsNullOrEmpty(layer.stringValue))
            {
                layer.stringValue = name;
            }
        }

        private static GameObject CreateGround()
        {
            GameObject ground = new GameObject("[PLACEHOLDER]Ground");
            ground.layer = 6; // Ground
            ground.transform.position = new Vector3(0, -1, 0);

            BoxCollider2D col = ground.AddComponent<BoxCollider2D>();
            col.size = new Vector2(50, 2);

            SpriteRenderer sr = ground.AddComponent<SpriteRenderer>();
            sr.sprite = CreatePlaceholderSprite(500, 200, new Color(0.3f, 0.3f, 0.3f));
            sr.sortingOrder = -10;

            return ground;
        }

        private static GameObject CreatePlayer(CharacterInfo info)
        {
            GameObject player = new GameObject("[PLACEHOLDER]PlayerCharacter");
            player.tag = "Player";
            player.layer = 7; // Player
            player.transform.position = new Vector3(0, 2, 0);

            Rigidbody2D rb = player.AddComponent<Rigidbody2D>();
            rb.gravityScale = 3.0f;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            BoxCollider2D col = player.AddComponent<BoxCollider2D>();
            col.size = new Vector2(0.6f, 0.9f);
            col.offset = new Vector2(0, 0.45f);

            SpriteRenderer sr = player.AddComponent<SpriteRenderer>();
            sr.sprite = CreatePlaceholderSprite(120, 180, new Color(0.2f, 0.4f, 1.0f));
            sr.sortingOrder = 10;

            // InputSystem
            UnityEngine.InputSystem.PlayerInput pi = player.AddComponent<UnityEngine.InputSystem.PlayerInput>();

            // MonoBehaviour
            PlayerInputHandler inputHandler = player.AddComponent<PlayerInputHandler>();
            PlayerCharacter pc = player.AddComponent<PlayerCharacter>();
            DamageReceiver dr = player.AddComponent<DamageReceiver>();

            // CharacterInfo設定
            SetCharacterInfoField(pc, info);

            // ヒットボックス子オブジェクト
            GameObject hitbox = new GameObject("[PLACEHOLDER]PlayerHitbox");
            hitbox.transform.SetParent(player.transform);
            hitbox.transform.localPosition = new Vector3(0.7f, 0.5f, 0);
            hitbox.layer = 10; // PlayerHitbox

            BoxCollider2D hitCol = hitbox.AddComponent<BoxCollider2D>();
            hitCol.isTrigger = true;
            hitCol.size = new Vector2(0.8f, 0.6f);

            hitbox.AddComponent<DamageDealer>();

            return player;
        }

        private static GameObject CreateCompanion(CharacterInfo info, Transform playerTransform)
        {
            GameObject companion = new GameObject("[PLACEHOLDER]CompanionCharacter");
            companion.layer = 9; // Companion
            companion.transform.position = new Vector3(-2, 2, 0);

            Rigidbody2D rb = companion.AddComponent<Rigidbody2D>();
            rb.gravityScale = 3.0f;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;

            BoxCollider2D col = companion.AddComponent<BoxCollider2D>();
            col.size = new Vector2(0.6f, 0.9f);
            col.offset = new Vector2(0, 0.45f);

            SpriteRenderer sr = companion.AddComponent<SpriteRenderer>();
            sr.sprite = CreatePlaceholderSprite(120, 180, new Color(0.2f, 0.8f, 0.3f));
            sr.sortingOrder = 9;

            CompanionCharacter cc = companion.AddComponent<CompanionCharacter>();
            DamageReceiver dr = companion.AddComponent<DamageReceiver>();

            SetCharacterInfoField(cc, info);

            // playerTransform設定
            System.Type type = typeof(CompanionCharacter);
            System.Reflection.FieldInfo field = type.GetField("_playerTransform",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(cc, playerTransform);
            }

            return companion;
        }

        private static GameObject CreateEnemy(CharacterInfo info)
        {
            GameObject enemy = new GameObject("[PLACEHOLDER]Enemy");
            enemy.layer = 8; // Enemy
            enemy.transform.position = new Vector3(8, 2, 0);

            Rigidbody2D rb = enemy.AddComponent<Rigidbody2D>();
            rb.gravityScale = 3.0f;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;

            BoxCollider2D col = enemy.AddComponent<BoxCollider2D>();
            col.size = new Vector2(0.5f, 0.8f);
            col.offset = new Vector2(0, 0.4f);

            SpriteRenderer sr = enemy.AddComponent<SpriteRenderer>();
            sr.sprite = CreatePlaceholderSprite(100, 160, new Color(1.0f, 0.2f, 0.2f));
            sr.sortingOrder = 8;

            EnemyCharacter ec = enemy.AddComponent<EnemyCharacter>();
            DamageReceiver dr = enemy.AddComponent<DamageReceiver>();

            SetCharacterInfoField(ec, info);

            // ヒットボックス子オブジェクト
            GameObject hitbox = new GameObject("[PLACEHOLDER]EnemyHitbox");
            hitbox.transform.SetParent(enemy.transform);
            hitbox.transform.localPosition = new Vector3(0.5f, 0.4f, 0);
            hitbox.layer = 11; // EnemyHitbox

            BoxCollider2D hitCol = hitbox.AddComponent<BoxCollider2D>();
            hitCol.isTrigger = true;
            hitCol.size = new Vector2(0.6f, 0.5f);

            hitbox.AddComponent<DamageDealer>();

            return enemy;
        }

        private static void SetupCamera(Transform playerTransform)
        {
            Camera mainCam = Camera.main;
            if (mainCam == null)
            {
                return;
            }

            mainCam.orthographic = true;
            mainCam.orthographicSize = 7f;
            mainCam.transform.position = new Vector3(0, 3, -10);

            CameraController cc = mainCam.gameObject.AddComponent<CameraController>();

            // ターゲット設定
            System.Type type = typeof(CameraController);
            System.Reflection.FieldInfo field = type.GetField("_target",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(cc, playerTransform);
            }
        }

        private static void SetupHUD()
        {
            GameObject hudObj = new GameObject("[PLACEHOLDER]HUD");

            UIDocument uiDoc = hudObj.AddComponent<UIDocument>();

            // UXMLアセットを読み込む
            VisualTreeAsset hudUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Assets/MyAsset/UI/HUD/HUD.uxml");
            if (hudUxml != null)
            {
                uiDoc.visualTreeAsset = hudUxml;
            }

            // Panel Settings（なければ作成）
            string panelSettingsPath = "Assets/MyAsset/UI/GamePanelSettings.asset";
            UnityEngine.UIElements.PanelSettings panelSettings =
                AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.PanelSettings>(panelSettingsPath);
            if (panelSettings == null)
            {
                EnsureDirectoryExists("Assets/MyAsset/UI");
                panelSettings = ScriptableObject.CreateInstance<UnityEngine.UIElements.PanelSettings>();
                panelSettings.scaleMode = UnityEngine.UIElements.PanelScaleMode.ScaleWithScreenSize;
                panelSettings.referenceResolution = new Vector2Int(1920, 1080);
                AssetDatabase.CreateAsset(panelSettings, panelSettingsPath);
            }
            uiDoc.panelSettings = panelSettings;

            // StyleSheet
            StyleSheet hudUss = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Assets/MyAsset/UI/HUD/HUD.uss");

            hudObj.AddComponent<HudController>();
        }

        private static void SetCharacterInfoField(BaseCharacter character, CharacterInfo info)
        {
            System.Type type = typeof(BaseCharacter);
            System.Reflection.FieldInfo field = type.GetField("_characterInfo",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(character, info);
            }
        }

        private static CharacterInfo CreateOrLoadCharacterInfo(string path,
            CharacterBelong belong, CharacterFeature feature,
            int maxHp, float moveSpeed, float jumpHeight, float dashSpeed)
        {
            CharacterInfo existing = AssetDatabase.LoadAssetAtPath<CharacterInfo>(path);
            if (existing != null)
            {
                return existing;
            }

            EnsureDirectoryExists("Assets/MyAsset/Data");

            CharacterInfo info = ScriptableObject.CreateInstance<CharacterInfo>();
            info.belong = belong;
            info.feature = feature;
            info.maxHp = maxHp;
            info.maxMp = 50;
            info.maxStamina = 100f;
            info.staminaRecoveryRate = 20f;
            info.staminaRecoveryDelay = 0.5f;
            info.moveSpeed = moveSpeed;
            info.walkSpeed = moveSpeed * 0.5f;
            info.dashSpeed = dashSpeed;
            info.jumpHeight = jumpHeight;
            info.baseAttack = new ElementalStatus { slash = 15 };
            info.baseDefense = new ElementalStatus { slash = 5 };
            info.initialActState = ActState.Neutral;

            AssetDatabase.CreateAsset(info, path);
            return info;
        }

        private static Sprite CreatePlaceholderSprite(int width, int height, Color color)
        {
            Texture2D tex = new Texture2D(width, height);
            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = color;
            }
            tex.SetPixels(pixels);
            tex.Apply();
            tex.filterMode = FilterMode.Point;

            return Sprite.Create(tex, new Rect(0, 0, width, height),
                new Vector2(0.5f, 0), 100f);
        }

        private static void EnsureDirectoryExists(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                string parent = System.IO.Path.GetDirectoryName(path).Replace("\\", "/");
                string folder = System.IO.Path.GetFileName(path);
                if (!AssetDatabase.IsValidFolder(parent))
                {
                    EnsureDirectoryExists(parent);
                }
                AssetDatabase.CreateFolder(parent, folder);
            }
        }
    }
}
