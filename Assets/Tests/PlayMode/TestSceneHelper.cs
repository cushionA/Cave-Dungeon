using UnityEngine;
using Game.Core;
using Game.Runtime;
using CharacterInfo = Game.Core.CharacterInfo;

namespace Game.Tests.PlayMode
{
    /// <summary>
    /// PlayModeテスト用のシーン構築ヘルパー。
    /// </summary>
    public static class TestSceneHelper
    {
        /// <summary>
        /// テスト用GameManagerを生成する。
        /// </summary>
        public static GameObject CreateGameManager()
        {
            // 既存のGameManagerがあれば破棄
            if (GameManager.Instance != null)
            {
                Object.Destroy(GameManager.Instance.gameObject);
            }

            GameObject go = new GameObject("TestGameManager");
            go.AddComponent<GameManager>();
            return go;
        }

        /// <summary>
        /// テスト用の地面を生成する（Layer 6 = Ground）。
        /// </summary>
        public static GameObject CreateGround(float width = 50f)
        {
            GameObject go = new GameObject("TestGround");
            go.layer = 6; // Ground layer
            BoxCollider2D col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(width, 2f);
            go.transform.position = new Vector3(0, -1f, 0);
            return go;
        }

        /// <summary>
        /// テスト用CharacterInfoを生成する。
        /// </summary>
        public static CharacterInfo CreateTestCharacterInfo(
            CharacterBelong belong = CharacterBelong.Ally,
            CharacterFeature feature = CharacterFeature.Player,
            int maxHp = 100,
            float moveSpeed = 6f,
            float jumpHeight = 3.5f)
        {
            CharacterInfo info = ScriptableObject.CreateInstance<CharacterInfo>();
            info.belong = belong;
            info.feature = feature;
            info.maxHp = maxHp;
            info.maxMp = 50;
            info.maxStamina = 100f;
            info.staminaRecoveryRate = 20f;
            info.staminaRecoveryDelay = 0.5f;
            info.moveSpeed = moveSpeed;
            info.dashSpeed = 12f;
            info.jumpHeight = jumpHeight;
            info.walkSpeed = 3f;
            info.baseAttack = new ElementalStatus { slash = 10 };
            info.baseDefense = new ElementalStatus { slash = 5 };
            info.initialActState = ActState.Neutral;
            return info;
        }

        /// <summary>
        /// テスト用BaseCharacterを持つGameObjectを生成する。
        /// </summary>
        public static GameObject CreateBaseCharacterObject(CharacterInfo info, Vector3 position)
        {
            GameObject go = new GameObject("TestCharacter");
            go.transform.position = position;

            Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 3.0f;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;

            BoxCollider2D col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(0.6f, 0.9f);

            // BaseCharacterのCharacterInfoフィールドをリフレクションで設定
            BaseCharacter bc = go.AddComponent<BaseCharacter>();
            SetCharacterInfo(bc, info);

            return go;
        }

        /// <summary>
        /// テスト用BaseCharacter + DamageReceiver + CharacterAnimationController を持つGameObjectを生成する。
        /// BaseCharacter の Awake で GetComponent キャッシュが成功するよう、
        /// DamageReceiver / AnimationController を先に追加してから BaseCharacter を追加する。
        /// </summary>
        public static GameObject CreateBaseCharacterObjectWithDamageReceiver(CharacterInfo info, Vector3 position)
        {
            GameObject go = new GameObject("TestCharacter");
            go.transform.position = position;

            Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 3.0f;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;

            BoxCollider2D col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(0.6f, 0.9f);

            // BaseCharacter.Awake が GetComponent でキャッシュする順序を守るため、
            // BaseCharacter より前に DamageReceiver / CharacterAnimationController を追加する。
            // CharacterAnimationController は [RequireComponent(Animator)] により Animator も自動付与。
            go.AddComponent<DamageReceiver>();
            go.AddComponent<CharacterAnimationController>();

            // BaseCharacterのCharacterInfoフィールドをリフレクションで設定
            BaseCharacter bc = go.AddComponent<BaseCharacter>();
            SetCharacterInfo(bc, info);

            return go;
        }

        /// <summary>
        /// CharacterInfoを設定する。
        /// UNITY_INCLUDE_TESTS定義時はBaseCharacterの公開メソッドを使用。
        /// </summary>
        public static void SetCharacterInfo(BaseCharacter character, CharacterInfo info)
        {
#if UNITY_INCLUDE_TESTS
            character.SetCharacterInfoForTest(info);
#else
            // フォールバック: リフレクション
            System.Type type = typeof(BaseCharacter);
            System.Reflection.FieldInfo field = type.GetField("_characterInfo",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(character, info);
            }
#endif
        }

        /// <summary>
        /// テスト後のクリーンアップ。
        /// </summary>
        public static void Cleanup()
        {
            CharacterRegistry.Clear();

            if (GameManager.Instance != null)
            {
                Object.Destroy(GameManager.Instance.gameObject);
            }
        }
    }
}
