using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using Game.Core;
using Game.Runtime;
using CharacterInfo = Game.Core.CharacterInfo;

namespace Game.Editor
{
    /// <summary>
    /// 全機能テスト用シーンを自動構築するエディタツール。
    /// Section1-4の主要機能を手動プレイテストできるレイアウト。
    ///
    /// エリア構成:
    ///   1. スタートエリア (x:-8 ~ 0)   — 安全地帯、仲間と合流
    ///   2. 戦闘エリア (x:0 ~ 15)       — 敵x3、攻撃・コンボ・ガードをテスト
    ///   3. 機動テストエリア (x:15 ~ 35) — 段差、ギャップ、高台でジャンプ・ダッシュ
    ///   4. ボスエリア (x:35 ~ 50)      — 強敵、壁で囲まれた擬似ボスルーム
    /// </summary>
    public static class TestSceneBuilder
    {
        private const int k_SquareSize = 128;
        private const float k_Ppu = 128f;

        [MenuItem("Tools/Build Test Scene")]
        public static void BuildTestScene()
        {
            if (!EditorUtility.DisplayDialog(
                "テストシーン構築",
                "現在のシーンを破棄してテストシーンを新規作成します。よろしいですか？",
                "構築する", "キャンセル"))
            {
                return;
            }

            BuildTestSceneInternal();
        }

        /// <summary>
        /// ダイアログなしでテストシーンを構築する。MCP/自動化ツール向け。
        /// </summary>
        [MenuItem("Tools/MCP Internal/Build Test Scene Silent")]
        public static void BuildTestSceneNoDialog()
        {
            BuildTestSceneInternal();
        }

        private static void BuildTestSceneInternal()
        {
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // レイヤー
            SetupLayers();

            // SO読み込み
            CharacterInfo playerInfo = LoadOrWarnCharacterInfo("Assets/MyAsset/Data/PlayerInfo.asset");
            CharacterInfo enemyInfo = LoadOrWarnCharacterInfo("Assets/MyAsset/Data/BasicEnemyInfo.asset");
            CharacterInfo companionInfo = LoadOrWarnCharacterInfo("Assets/MyAsset/Data/CompanionInfo.asset");

            // アセット自動生成
            AttackInfo[] playerAttacks = CreatePlayerAttackInfos();
            AttackInfo[] enemyAttacks = CreateEnemyAttackInfos();
            AttackInfo[] bossAttacks = CreateBossAttackInfos();
            AnimatorController placeholderAnimController = CreateOrLoadPlaceholderAnimatorController();
            AttackInfo[] companionAttacks = CreateCompanionAttackInfos();
            AIInfo basicEnemyAI = CreateBasicEnemyAIInfo();
            AIInfo bossAI = CreateBossAIInfo();
            AIInfo companionAI = CreateCompanionAIInfo();

            // ===== システム =====
            GameObject gmObj = new GameObject("GameManager");
            gmObj.AddComponent<GameManager>();

            // GameManager 子マネージャー
            GameObject projectileMgrObj = new GameObject("ProjectileManager");
            projectileMgrObj.transform.SetParent(gmObj.transform);
            projectileMgrObj.AddComponent<ProjectileManager>();

            GameObject spawnerMgrObj = new GameObject("EnemySpawnerManager");
            spawnerMgrObj.transform.SetParent(gmObj.transform);
            spawnerMgrObj.AddComponent<EnemySpawnerManager>();

            GameObject levelStreamObj = new GameObject("LevelStreamingController");
            levelStreamObj.transform.SetParent(gmObj.transform);
            levelStreamObj.AddComponent<LevelStreamingController>();

            // ===== 環境: スタートエリア =====
            CreateGround(new Vector3(21f, -1f, 0f), 70f);    // 全体の床
            CreateWall("Wall_Left", new Vector3(-14.5f, 4f, 0f), new Vector2(1f, 12f));

            // ===== 環境: 戦闘エリア =====
            CreatePlatform("CombatPlatform_Low", new Vector3(5f, 1.5f, 0f), new Vector2(5f, 0.5f));
            CreatePlatform("CombatPlatform_High", new Vector3(10f, 3.5f, 0f), new Vector2(4f, 0.5f));

            // ===== 環境: 機動テストエリア =====
            // 段差ジャンプ (高さ1, 2, 3.5タイル)
            CreatePlatform("Step_1", new Vector3(17f, 1f, 0f), new Vector2(2f, 0.5f));
            CreatePlatform("Step_2", new Vector3(20f, 2f, 0f), new Vector2(2f, 0.5f));
            CreatePlatform("Step_3", new Vector3(23f, 3.5f, 0f), new Vector2(2f, 0.5f));
            // ジャンプ最大高度チェック用 (3.5タイル)
            CreatePlatform("JumpCeiling", new Vector3(23f, 7f, 0f), new Vector2(3f, 0.3f));

            // ダッシュ用ギャップ (3タイルの穴)
            CreateGap(new Vector3(27f, -1f, 0f), 3f);
            CreatePlatform("PostGap", new Vector3(30f, -0.25f, 0f), new Vector2(4f, 1.5f));

            // ジャンプ距離テスト (5タイルの穴)
            CreateGap(new Vector3(33.5f, -1f, 0f), 5f);
            CreatePlatform("LongJumpLanding", new Vector3(37f, -0.25f, 0f), new Vector2(2f, 1.5f));

            // ===== 環境: ボスエリア =====
            CreateWall("BossWall_Left", new Vector3(39f, 4f, 0f), new Vector2(0.5f, 10f));
            CreateWall("BossWall_Right", new Vector3(54.5f, 4f, 0f), new Vector2(1f, 12f));
            CreatePlatform("BossPlatform_L", new Vector3(43f, 2.5f, 0f), new Vector2(3f, 0.5f));
            CreatePlatform("BossPlatform_R", new Vector3(51f, 2.5f, 0f), new Vector2(3f, 0.5f));

            // ===== キャラクター =====
            GameObject player = CreatePlayer(playerInfo, placeholderAnimController, playerAttacks);
            CreateCompanion(companionInfo, player.transform, placeholderAnimController, companionAttacks, companionAI);

            // 戦闘エリア: 敵x3
            CreateEnemy(enemyInfo, "Enemy_Melee_1", new Vector3(4f, 2f, 0f),
                placeholderAnimController, enemyAttacks, basicEnemyAI);
            CreateEnemy(enemyInfo, "Enemy_Melee_2", new Vector3(8f, 2f, 0f),
                placeholderAnimController, enemyAttacks, basicEnemyAI);
            CreateEnemy(enemyInfo, "Enemy_Platform", new Vector3(10f, 5f, 0f),
                placeholderAnimController, enemyAttacks, basicEnemyAI);

            // ボスエリア: 強敵 (大きめ)
            CreateBossEnemy(enemyInfo, "BossEnemy", new Vector3(47f, 2f, 0f),
                placeholderAnimController, bossAttacks, bossAI);

            // ===== カメラ =====
            SetupCamera(player.transform);

            // ===== UI =====
            SetupHUD();
            SetupDamagePopups();

            // ===== デバッグオーバーレイ =====
            GameObject debugOverlay = new GameObject("[DEBUG]StatusOverlay");
            debugOverlay.AddComponent<DebugStatusOverlay>();

            // ===== 自動入力テスター =====
            AutoInputTester tester = gmObj.AddComponent<AutoInputTester>();
            tester.EnableOnStart = true;
            tester.LoopCount = 3;

            // ===== エリアラベル =====
            CreateAreaLabel("スタート", new Vector3(-6f, 6f, 0f));
            CreateAreaLabel("戦闘エリア", new Vector3(7f, 6f, 0f));
            CreateAreaLabel("機動テスト", new Vector3(22f, 6f, 0f));
            CreateAreaLabel("ボスルーム", new Vector3(47f, 6f, 0f));

            // ===== 物理レイヤー衝突設定 =====
            SetupPhysicsLayers();

            // ===== 保存 =====
            EnsureDirectoryExists("Assets/Scenes");
            EditorSceneManager.SaveScene(
                EditorSceneManager.GetActiveScene(),
                "Assets/Scenes/CoreTestScene.unity");

            Debug.Log("[TestSceneBuilder] テストシーン構築完了！");
            Debug.Log("[TestSceneBuilder] エリア構成:");
            Debug.Log("  1. スタート (x:-8~0) — 安全地帯、仲間合流");
            Debug.Log("  2. 戦闘 (x:0~15) — 敵x3、コンボ・ガードテスト");
            Debug.Log("  3. 機動テスト (x:15~35) — 段差・ギャップ・ジャンプ距離");
            Debug.Log("  4. ボスルーム (x:39~55) — 強敵、壁囲いアリーナ");
        }

        // ===== アセット自動生成 =====

        private static AttackInfo[] CreatePlayerAttackInfos()
        {
            // 3段コンボ: 弱1 → 弱2 → 弱3（フィニッシュ）
            AttackInfo combo1 = CreateOrLoadAttackInfo(
                "Assets/MyAsset/Data/PLACEHOLDER_PlayerCombo1.asset",
                info =>
                {
                    info.attackName = "Light_Combo1";
                    info.category = AttackCategory.Melee;
                    info.motionInfo = new MotionInfo
                    {
                        preMotionDuration = 0.05f,
                        activeMotionDuration = 0.2f,
                        recoveryDuration = 0.1f
                    };
                    info.baseDamage = new ElementalStatus { slash = 10 };
                    info.damageMultiplier = 1.0f;
                    info.attackElement = Element.Slash;
                    info.feature = AttackFeature.Light;
                    info.contactType = AttackContactType.StopOnHit;
                    info.attackMoveDistance = 1.0f;
                    info.attackMoveDuration = 0.12f;
                    info.aerialMoveDirection = new Vector2(0f, 0.3f);
                    info.isParriable = true;
                    info.staminaCost = 5f;
                    info.armorBreakValue = 10f;
                    info.inputWindow = 0.6f;
                    info.knockbackInfo = new KnockbackInfo
                    {
                        hasKnockback = true,
                        force = new Vector2(2f, 0.5f)
                    };
                });

            AttackInfo combo2 = CreateOrLoadAttackInfo(
                "Assets/MyAsset/Data/PLACEHOLDER_PlayerCombo2.asset",
                info =>
                {
                    info.attackName = "Light_Combo2";
                    info.category = AttackCategory.Melee;
                    info.motionInfo = new MotionInfo
                    {
                        preMotionDuration = 0.05f,
                        activeMotionDuration = 0.2f,
                        recoveryDuration = 0.12f
                    };
                    info.baseDamage = new ElementalStatus { slash = 12 };
                    info.damageMultiplier = 1.2f;
                    info.attackElement = Element.Slash;
                    info.feature = AttackFeature.Light;
                    info.contactType = AttackContactType.StopOnHit;
                    info.attackMoveDistance = 1.2f;
                    info.attackMoveDuration = 0.12f;
                    info.aerialMoveDirection = new Vector2(0f, 0.2f);
                    info.isParriable = true;
                    info.staminaCost = 7f;
                    info.armorBreakValue = 12f;
                    info.inputWindow = 0.6f;
                    info.knockbackInfo = new KnockbackInfo
                    {
                        hasKnockback = true,
                        force = new Vector2(3f, 0.5f)
                    };
                });

            AttackInfo combo3 = CreateOrLoadAttackInfo(
                "Assets/MyAsset/Data/PLACEHOLDER_PlayerCombo3.asset",
                info =>
                {
                    info.attackName = "Light_Combo3_Finish";
                    info.category = AttackCategory.Melee;
                    info.motionInfo = new MotionInfo
                    {
                        preMotionDuration = 0.08f,
                        activeMotionDuration = 0.25f,
                        recoveryDuration = 0.2f
                    };
                    info.baseDamage = new ElementalStatus { slash = 15, strike = 5 };
                    info.damageMultiplier = 1.8f;
                    info.attackElement = Element.Slash;
                    info.feature = AttackFeature.Heavy;
                    info.contactType = AttackContactType.StopOnHit;
                    info.attackMoveDistance = 1.8f;
                    info.attackMoveDuration = 0.15f;
                    info.aerialMoveDirection = new Vector2(0.3f, -0.5f);
                    info.isParriable = true;
                    info.staminaCost = 12f;
                    info.armorBreakValue = 20f;
                    info.isChainEndPoint = true;
                    info.inputWindow = 0f; // フィニッシュ: コンボ継続なし
                    info.knockbackInfo = new KnockbackInfo
                    {
                        hasKnockback = true,
                        force = new Vector2(6f, 2f),
                        stunDuration = 0.3f
                    };
                });

            return new AttackInfo[] { combo1, combo2, combo3 };
        }

        private static AttackInfo[] CreateEnemyAttackInfos()
        {
            AttackInfo attack = CreateOrLoadAttackInfo(
                "Assets/MyAsset/Data/PLACEHOLDER_EnemyAttack.asset",
                info =>
                {
                    info.attackName = "PLACEHOLDER_EnemyAttack";
                    info.category = AttackCategory.Melee;
                    info.motionInfo = new MotionInfo
                    {
                        preMotionDuration = 0.15f,
                        activeMotionDuration = 0.2f,
                        recoveryDuration = 0.2f
                    };
                    info.baseDamage = new ElementalStatus { slash = 8 };
                    info.damageMultiplier = 1.0f;
                    info.attackElement = Element.Slash;
                    info.contactType = AttackContactType.PassThrough;
                });

            return new AttackInfo[] { attack };
        }

        private static AttackInfo[] CreateBossAttackInfos()
        {
            AttackInfo slam = CreateOrLoadAttackInfo(
                "Assets/MyAsset/Data/PLACEHOLDER_BossSlam.asset",
                info =>
                {
                    info.attackName = "PLACEHOLDER_BossSlam";
                    info.category = AttackCategory.Melee;
                    info.motionInfo = new MotionInfo
                    {
                        preMotionDuration = 0.4f,
                        activeMotionDuration = 0.3f,
                        recoveryDuration = 0.5f
                    };
                    info.baseDamage = new ElementalStatus { strike = 15 };
                    info.damageMultiplier = 2.0f;
                    info.attackElement = Element.Strike;
                    info.contactType = AttackContactType.PassThrough;
                    info.armorBreakValue = 30f;
                    info.knockbackInfo = new KnockbackInfo
                    {
                        hasKnockback = true,
                        force = new Vector2(5f, 2f),
                        stunDuration = 0.5f
                    };
                });

            return new AttackInfo[] { slam };
        }

        private static AttackInfo[] CreateCompanionAttackInfos()
        {
            AttackInfo attack = CreateOrLoadAttackInfo(
                "Assets/MyAsset/Data/PLACEHOLDER_CompanionAttack.asset",
                info =>
                {
                    info.attackName = "PLACEHOLDER_CompanionAttack";
                    info.category = AttackCategory.Melee;
                    info.motionInfo = new MotionInfo
                    {
                        preMotionDuration = 0.1f,
                        activeMotionDuration = 0.15f,
                        recoveryDuration = 0.15f
                    };
                    info.baseDamage = new ElementalStatus { slash = 6 };
                    info.damageMultiplier = 1.0f;
                    info.attackElement = Element.Slash;
                    info.contactType = AttackContactType.PassThrough;
                });

            return new AttackInfo[] { attack };
        }

        private static AttackInfo CreateOrLoadAttackInfo(string path, System.Action<AttackInfo> configure)
        {
            AttackInfo existing = AssetDatabase.LoadAssetAtPath<AttackInfo>(path);
            if (existing != null)
            {
                // 既存アセットにも設定を再適用（新フィールド追加時にデフォルト値を確実に反映）
                configure(existing);
                EditorUtility.SetDirty(existing);
                return existing;
            }

            AttackInfo info = ScriptableObject.CreateInstance<AttackInfo>();
            configure(info);
            EnsureDirectoryExists(System.IO.Path.GetDirectoryName(path).Replace("\\", "/"));
            AssetDatabase.CreateAsset(info, path);
            return info;
        }

        // ===== AIInfo アセット自動生成 =====

        private static AIInfo CreateBasicEnemyAIInfo()
        {
            string path = "Assets/MyAsset/Data/PLACEHOLDER_BasicEnemyAI.asset";
            // 常に再生成（パラメータ変更を反映するため）
            AIInfo existing = AssetDatabase.LoadAssetAtPath<AIInfo>(path);
            if (existing != null)
            {
                AssetDatabase.DeleteAsset(path);
            }

            AIInfo ai = ScriptableObject.CreateInstance<AIInfo>();

            // 行動定義: Attack, Wait
            ai.actDataList = new ActData[]
            {
                new ActData
                {
                    actName = "BasicAttack",
                    actType = ActType.Attack,
                    attackInfoIndex = 0,
                    weight = 80f,
                    coolTime = 2f,
                    triggerJudge = new TriggerJudgeData
                    {
                        condition = TriggerConditionType.EnemyInRange,
                        value = 2.0f,
                        comparison = ComparisonOperator.LessOrEqual,
                    },
                },
                new ActData
                {
                    actName = "Wait",
                    actType = ActType.Wait,
                    weight = 10f,
                },
            };

            // モード定義: Idle, Combat
            ai.modes = new CharacterModeData[]
            {
                // Idle: 検出範囲外で待機（プレイヤー初期距離=10なので15に拡大）
                new CharacterModeData
                {
                    mode = CharacterMode.Idle,
                    detectionRange = 15f,
                    combatRange = 2.0f,
                    availableActIndices = new int[] { 1 }, // Wait のみ
                    transitions = new TransitionCondition[]
                    {
                        new TransitionCondition
                        {
                            targetMode = CharacterMode.Combat,
                            trigger = TriggerType.EnemyDetected,
                            threshold = 12f,
                        },
                    },
                },
                // Combat: 追尾+攻撃
                new CharacterModeData
                {
                    mode = CharacterMode.Combat,
                    detectionRange = 15f,
                    combatRange = 2.0f,
                    availableActIndices = new int[] { 0, 1 }, // Attack, Wait
                    transitions = new TransitionCondition[]
                    {
                        new TransitionCondition
                        {
                            targetMode = CharacterMode.Idle,
                            trigger = TriggerType.EnemyLost,
                            threshold = 18f,
                        },
                    },
                },
            };

            ai.coolTimeData = new CoolTimeData
            {
                globalCoolTime = 0.5f,
                attackCoolTime = 2f,
            };

            EnsureDirectoryExists("Assets/MyAsset/Data");
            AssetDatabase.CreateAsset(ai, path);
            return ai;
        }

        private static AIInfo CreateBossAIInfo()
        {
            string path = "Assets/MyAsset/Data/PLACEHOLDER_BossAI.asset";
            AIInfo existing = AssetDatabase.LoadAssetAtPath<AIInfo>(path);
            if (existing != null)
            {
                AssetDatabase.DeleteAsset(path);
            }

            AIInfo ai = ScriptableObject.CreateInstance<AIInfo>();

            // 行動定義: SlamAttack, Wait
            ai.actDataList = new ActData[]
            {
                new ActData
                {
                    actName = "BossSlam",
                    actType = ActType.Attack,
                    attackInfoIndex = 0,
                    weight = 90f,
                    coolTime = 1.5f,
                    triggerJudge = new TriggerJudgeData
                    {
                        condition = TriggerConditionType.EnemyInRange,
                        value = 2f,
                        comparison = ComparisonOperator.LessOrEqual,
                    },
                },
                new ActData
                {
                    actName = "Wait",
                    actType = ActType.Wait,
                    weight = 10f,
                },
            };

            // モード定義: Combat のみ（常時戦闘）
            ai.modes = new CharacterModeData[]
            {
                new CharacterModeData
                {
                    mode = CharacterMode.Combat,
                    detectionRange = 20f,
                    combatRange = 2.5f,
                    availableActIndices = new int[] { 0, 1 }, // BossSlam, Wait
                    transitions = null, // 遷移なし（常時Combat）
                },
            };

            ai.coolTimeData = new CoolTimeData
            {
                globalCoolTime = 0.3f,
                attackCoolTime = 1.5f,
            };

            EnsureDirectoryExists("Assets/MyAsset/Data");
            AssetDatabase.CreateAsset(ai, path);
            return ai;
        }

        private static AIInfo CreateCompanionAIInfo()
        {
            string path = "Assets/MyAsset/Data/PLACEHOLDER_CompanionAI.asset";
            AIInfo existing = AssetDatabase.LoadAssetAtPath<AIInfo>(path);
            if (existing != null)
            {
                AssetDatabase.DeleteAsset(path);
            }

            AIInfo ai = ScriptableObject.CreateInstance<AIInfo>();

            // 行動定義: Attack, Wait
            ai.actDataList = new ActData[]
            {
                new ActData
                {
                    actName = "CompanionAttack",
                    actType = ActType.Attack,
                    attackInfoIndex = 0,
                    weight = 70f,
                    coolTime = 1.5f,
                    triggerJudge = new TriggerJudgeData
                    {
                        condition = TriggerConditionType.EnemyInRange,
                        value = 1.5f,
                        comparison = ComparisonOperator.LessOrEqual,
                    },
                },
                new ActData
                {
                    actName = "Wait",
                    actType = ActType.Wait,
                    weight = 20f,
                },
            };

            // モード定義: Follow（追従）, Combat（戦闘）
            ai.modes = new CharacterModeData[]
            {
                // Follow: 敵未検出時はプレイヤーに追従
                new CharacterModeData
                {
                    mode = CharacterMode.Idle,
                    detectionRange = 12f,
                    combatRange = 2.0f,
                    availableActIndices = new int[] { 1 }, // Wait のみ
                    transitions = new TransitionCondition[]
                    {
                        new TransitionCondition
                        {
                            targetMode = CharacterMode.Combat,
                            trigger = TriggerType.EnemyDetected,
                            threshold = 10f,
                        },
                    },
                },
                // Combat: 敵検出時は攻撃
                new CharacterModeData
                {
                    mode = CharacterMode.Combat,
                    detectionRange = 15f,
                    combatRange = 2.0f,
                    availableActIndices = new int[] { 0, 1 }, // Attack, Wait
                    transitions = new TransitionCondition[]
                    {
                        new TransitionCondition
                        {
                            targetMode = CharacterMode.Idle,
                            trigger = TriggerType.EnemyLost,
                            threshold = 15f,
                        },
                    },
                },
            };

            ai.coolTimeData = new CoolTimeData
            {
                globalCoolTime = 0.3f,
                attackCoolTime = 1.5f,
            };

            EnsureDirectoryExists("Assets/MyAsset/Data");
            AssetDatabase.CreateAsset(ai, path);
            return ai;
        }

        private static AnimatorController CreateOrLoadPlaceholderAnimatorController()
        {
            string path = "Assets/MyAsset/Data/PLACEHOLDER_CharacterAnimator.controller";
            AnimatorController existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (existing != null)
            {
                return existing;
            }

            EnsureDirectoryExists("Assets/MyAsset/Data");
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            return controller;
        }

        // ===== 環境 =====

        private static void CreateGround(Vector3 position, float width)
        {
            GameObject ground = new GameObject("[PLACEHOLDER]Ground");
            ground.layer = 6;
            ground.transform.position = position;

            BoxCollider2D col = ground.AddComponent<BoxCollider2D>();
            col.size = new Vector2(width, 2f);

            SpriteRenderer sr = ground.AddComponent<SpriteRenderer>();
            sr.sprite = CreateSquareSprite(new Color(0.25f, 0.55f, 0.25f));
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.size = new Vector2(width, 2f);
            sr.sortingOrder = -10;
        }

        private static void CreateGap(Vector3 centerPos, float gapWidth)
        {
            // ギャップ可視化 (赤い半透明の危険マーカー)
            GameObject marker = new GameObject($"[PLACEHOLDER]Gap_{gapWidth}m");
            marker.transform.position = centerPos + Vector3.down * 1.5f;

            SpriteRenderer sr = marker.AddComponent<SpriteRenderer>();
            sr.sprite = CreateSquareSprite(new Color(0.8f, 0.2f, 0.2f, 0.3f));
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.size = new Vector2(gapWidth, 0.5f);
            sr.sortingOrder = -8;

            // 穴の左右の地面端にコライダーブロック（地面を分断）
            // ※ メインの地面コライダーをそのまま使い、穴はキルゾーンで対応
            GameObject killZone = new GameObject($"KillZone_{gapWidth}m");
            killZone.transform.position = centerPos + Vector3.down * 4f;
            BoxCollider2D killCol = killZone.AddComponent<BoxCollider2D>();
            killCol.isTrigger = true;
            killCol.size = new Vector2(gapWidth, 4f);
        }

        private static void CreatePlatform(string name, Vector3 position, Vector2 size)
        {
            GameObject platform = new GameObject($"[PLACEHOLDER]{name}");
            platform.layer = 6;
            platform.transform.position = position;

            BoxCollider2D col = platform.AddComponent<BoxCollider2D>();
            col.size = size;

            SpriteRenderer sr = platform.AddComponent<SpriteRenderer>();
            sr.sprite = CreateSquareSprite(new Color(0.45f, 0.45f, 0.45f));
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.size = size;
            sr.sortingOrder = -9;
        }

        private static void CreateWall(string name, Vector3 position, Vector2 size)
        {
            GameObject wall = new GameObject($"[PLACEHOLDER]{name}");
            wall.layer = 6;
            wall.transform.position = position;

            BoxCollider2D col = wall.AddComponent<BoxCollider2D>();
            col.size = size;

            SpriteRenderer sr = wall.AddComponent<SpriteRenderer>();
            sr.sprite = CreateSquareSprite(new Color(0.35f, 0.35f, 0.35f));
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.size = size;
            sr.sortingOrder = -9;
        }

        // ===== キャラクター =====

        /// <summary>
        /// 摩擦ゼロのPhysicsMaterial2Dを生成する。
        /// 壁タイル継ぎ目への引っかかりを防止する。
        /// </summary>
        private static PhysicsMaterial2D CreateNoFrictionMaterial()
        {
            PhysicsMaterial2D mat = new PhysicsMaterial2D("NoFriction");
            mat.friction = 0f;
            mat.bounciness = 0f;
            return mat;
        }

        private static GameObject CreatePlayer(CharacterInfo info,
            AnimatorController animController, AttackInfo[] attacks)
        {
            GameObject player = new GameObject("[PLACEHOLDER]Player");
            player.tag = "Player";
            player.layer = GameConstants.k_LayerCharaPassThrough;
            player.transform.position = new Vector3(-6f, 2f, 0f);

            // 物理
            Rigidbody2D rb = player.AddComponent<Rigidbody2D>();
            rb.gravityScale = GameConstants.k_GravityScale;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            BoxCollider2D col = player.AddComponent<BoxCollider2D>();
            col.size = new Vector2(0.6f, 0.9f);
            col.offset = new Vector2(0f, 0.45f);
            col.sharedMaterial = CreateNoFrictionMaterial();

            // ビジュアル（白スクエア＝プレイヤー）
            SpriteRenderer sr = player.AddComponent<SpriteRenderer>();
            sr.sprite = CreateSquareSprite(Color.white);
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.size = new Vector2(0.6f, 0.9f);
            sr.sortingOrder = 10;

            // Animator + AnimationController
            Animator animator = player.AddComponent<Animator>();
            animator.runtimeAnimatorController = animController;
            player.AddComponent<CharacterAnimationController>();

            // InputSystem
            UnityEngine.InputSystem.PlayerInput pi = player.AddComponent<UnityEngine.InputSystem.PlayerInput>();
            pi.notificationBehavior = UnityEngine.InputSystem.PlayerNotifications.SendMessages;
            UnityEngine.InputSystem.InputActionAsset inputActions =
                AssetDatabase.LoadAssetAtPath<UnityEngine.InputSystem.InputActionAsset>(
                    "Assets/InputSystem_Actions.inputactions");
            if (inputActions != null)
            {
                pi.actions = inputActions;
                pi.defaultActionMap = "Player";
            }

            // コンポーネント
            player.AddComponent<PlayerInputHandler>();
            PlayerCharacter pc = player.AddComponent<PlayerCharacter>();
            player.AddComponent<DamageReceiver>();
            player.AddComponent<CharacterCollisionController>();
            player.AddComponent<AudioSource>(); // 落下攻撃着地SE等に使用

            // ActionExecutorController（攻撃実行の正規ルート）
            ActionExecutorController actionExec = player.AddComponent<ActionExecutorController>();
            SetPrivateField(actionExec, "_attackInfos", attacks);

            // PlayerCharacterにコンボ段数管理用データ参照をセット
            SetPrivateField(pc, "_lightAttacks", attacks);

            SetCharacterInfoField(pc, info);

            // 攻撃ヒットボックス（子オブジェクト）
            CreateHitbox(player, "[PLACEHOLDER]PlayerHitbox",
                new Vector3(0.7f, 0.5f, 0f), new Vector2(0.8f, 0.6f),
                10, new Color(1f, 1f, 0f, 0.3f));

            return player;
        }

        private static GameObject CreateCompanion(CharacterInfo info,
            Transform playerTransform, AnimatorController animController,
            AttackInfo[] attacks, AIInfo aiInfo)
        {
            GameObject companion = new GameObject("[PLACEHOLDER]Companion");
            companion.layer = GameConstants.k_LayerCharaPassThrough;
            companion.transform.position = new Vector3(-8f, 2f, 0f);

            // 物理
            Rigidbody2D rb = companion.AddComponent<Rigidbody2D>();
            rb.gravityScale = GameConstants.k_GravityScale;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            BoxCollider2D col = companion.AddComponent<BoxCollider2D>();
            col.size = new Vector2(0.5f, 0.8f);
            col.offset = new Vector2(0f, 0.4f);
            col.sharedMaterial = CreateNoFrictionMaterial();

            // ビジュアル（水色スクエア＝仲間）
            SpriteRenderer sr = companion.AddComponent<SpriteRenderer>();
            sr.sprite = CreateSquareSprite(new Color(0.3f, 0.7f, 1f));
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.size = new Vector2(0.5f, 0.8f);
            sr.sortingOrder = 9;

            // Animator + AnimationController
            Animator animator = companion.AddComponent<Animator>();
            animator.runtimeAnimatorController = animController;
            companion.AddComponent<CharacterAnimationController>();

            // コンポーネント
            CompanionCharacter cc = companion.AddComponent<CompanionCharacter>();
            companion.AddComponent<DamageReceiver>();
            companion.AddComponent<CharacterCollisionController>();

            // AI: CompanionController（フルAIシステム）で駆動
            cc.SetAIInfo(aiInfo);

            // CompanionMpSettings（デフォルト値）
            CompanionMpSettings mpSettings = new CompanionMpSettings
            {
                baseRecoveryRate = 3f,
                mpRecoverActionRate = 8f,
                vanishRecoveryMultiplier = 1.3f,
                returnThresholdRatio = 0.5f,
                maxReserveMp = 100,
            };
            cc.SetMpSettings(mpSettings);

            // ActionExecutorController（ヒットボックス・アニメーション駆動）
            ActionExecutorController actionExec = companion.AddComponent<ActionExecutorController>();
            SetPrivateField(actionExec, "_attackInfos", attacks);

            SetCharacterInfoField(cc, info);

            // _playerTransformをリフレクションで設定
            System.Type type = typeof(CompanionCharacter);
            System.Reflection.FieldInfo playerField = type.GetField("_playerTransform",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (playerField != null)
            {
                playerField.SetValue(cc, playerTransform);
            }

            // 攻撃ヒットボックス
            CreateHitbox(companion, "[PLACEHOLDER]CompanionHitbox",
                new Vector3(0.5f, 0.4f, 0f), new Vector2(0.6f, 0.5f),
                10, new Color(0.3f, 0.7f, 1f, 0.2f));

            return companion;
        }

        private static GameObject CreateEnemy(CharacterInfo info, string name, Vector3 position,
            AnimatorController animController, AttackInfo[] attacks, AIInfo aiInfo)
        {
            GameObject enemy = new GameObject($"[PLACEHOLDER]{name}");
            enemy.layer = GameConstants.k_LayerCharaPassThrough;
            enemy.transform.position = position;

            // 物理
            Rigidbody2D rb = enemy.AddComponent<Rigidbody2D>();
            rb.gravityScale = GameConstants.k_GravityScale;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            BoxCollider2D col = enemy.AddComponent<BoxCollider2D>();
            col.size = new Vector2(0.5f, 0.8f);
            col.offset = new Vector2(0f, 0.4f);
            col.sharedMaterial = CreateNoFrictionMaterial();

            // ビジュアル（赤スクエア＝敵）
            SpriteRenderer sr = enemy.AddComponent<SpriteRenderer>();
            sr.sprite = CreateSquareSprite(new Color(0.9f, 0.2f, 0.2f));
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.size = new Vector2(0.5f, 0.8f);
            sr.sortingOrder = 8;

            // Animator + AnimationController
            Animator animator = enemy.AddComponent<Animator>();
            animator.runtimeAnimatorController = animController;
            enemy.AddComponent<CharacterAnimationController>();

            // コンポーネント
            EnemyCharacter ec = enemy.AddComponent<EnemyCharacter>();
            enemy.AddComponent<DamageReceiver>();
            enemy.AddComponent<CharacterCollisionController>();

            // AI: EnemyController（ピュアロジック）で駆動
            ec.SetAIInfo(aiInfo);

            // ActionExecutorController（ヒットボックス・アニメーション駆動）
            ActionExecutorController actionExec = enemy.AddComponent<ActionExecutorController>();
            SetPrivateField(actionExec, "_attackInfos", attacks);

            SetCharacterInfoField(ec, info);

            // 攻撃ヒットボックス
            CreateHitbox(enemy, $"[PLACEHOLDER]{name}Hitbox",
                new Vector3(0.5f, 0.4f, 0f), new Vector2(0.6f, 0.5f),
                11, new Color(1f, 0.3f, 0.3f, 0.2f));

            return enemy;
        }

        private static GameObject CreateBossEnemy(CharacterInfo info, string name, Vector3 position,
            AnimatorController animController, AttackInfo[] attacks, AIInfo aiInfo)
        {
            GameObject boss = new GameObject($"[PLACEHOLDER]{name}");
            boss.layer = GameConstants.k_LayerCharaPassThrough;
            boss.transform.position = position;

            // 物理
            Rigidbody2D rb = boss.AddComponent<Rigidbody2D>();
            rb.gravityScale = GameConstants.k_GravityScale;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            BoxCollider2D col = boss.AddComponent<BoxCollider2D>();
            col.size = new Vector2(1.2f, 1.6f);
            col.offset = new Vector2(0f, 0.8f);
            col.sharedMaterial = CreateNoFrictionMaterial();

            // ビジュアル（濃い赤＝ボス、大きめ）
            SpriteRenderer sr = boss.AddComponent<SpriteRenderer>();
            sr.sprite = CreateSquareSprite(new Color(0.7f, 0.1f, 0.15f));
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.size = new Vector2(1.2f, 1.6f);
            sr.sortingOrder = 8;

            // Animator + AnimationController
            Animator animator = boss.AddComponent<Animator>();
            animator.runtimeAnimatorController = animController;
            boss.AddComponent<CharacterAnimationController>();

            // コンポーネント
            EnemyCharacter ec = boss.AddComponent<EnemyCharacter>();
            boss.AddComponent<DamageReceiver>();
            boss.AddComponent<CharacterCollisionController>();

            // AI: EnemyController（ピュアロジック）で駆動（検出範囲はAIInfoで設定）
            ec.SetAIInfo(aiInfo);

            // ActionExecutorController（ヒットボックス・アニメーション駆動）
            ActionExecutorController actionExec = boss.AddComponent<ActionExecutorController>();
            SetPrivateField(actionExec, "_attackInfos", attacks);

            SetCharacterInfoField(ec, info);

            // 攻撃ヒットボックス（大きめ）
            CreateHitbox(boss, $"[PLACEHOLDER]{name}Hitbox",
                new Vector3(0.8f, 0.8f, 0f), new Vector2(1.0f, 0.8f),
                11, new Color(1f, 0.2f, 0.2f, 0.25f));

            return boss;
        }

        private static void CreateHitbox(GameObject parent, string name,
            Vector3 localPos, Vector2 size, int layer, Color debugColor)
        {
            GameObject hitbox = new GameObject(name);
            hitbox.transform.SetParent(parent.transform);
            hitbox.transform.localPosition = localPos;
            hitbox.layer = layer;

            BoxCollider2D hitCol = hitbox.AddComponent<BoxCollider2D>();
            hitCol.isTrigger = true;
            hitCol.size = size;

            hitbox.AddComponent<HitBox>();
            hitbox.AddComponent<DamageDealer>();

            // デバッグ可視化
            SpriteRenderer hitSr = hitbox.AddComponent<SpriteRenderer>();
            hitSr.sprite = CreateSquareSprite(debugColor);
            hitSr.drawMode = SpriteDrawMode.Tiled;
            hitSr.size = size;
            hitSr.sortingOrder = 11;
        }

        // ===== カメラ =====

        private static void SetupCamera(Transform playerTransform)
        {
            Camera mainCam = Camera.main;
            if (mainCam == null)
            {
                return;
            }

            mainCam.orthographic = true;
            mainCam.orthographicSize = 7f;
            mainCam.backgroundColor = new Color(0.12f, 0.12f, 0.18f);
            mainCam.transform.position = new Vector3(0f, 3f, -10f);

            CameraController cc = mainCam.gameObject.AddComponent<CameraController>();

            // EditorモードではAwake()が走らないため、SerializedObjectでフィールドを設定
            SerializedObject so = new SerializedObject(cc);
            SerializedProperty targetProp = so.FindProperty("_target");
            targetProp.objectReferenceValue = playerTransform;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ===== UI =====

        private static void SetupHUD()
        {
            GameObject hudObj = new GameObject("[PLACEHOLDER]HUD");

            UIDocument uiDoc = hudObj.AddComponent<UIDocument>();

            // UXML
            VisualTreeAsset hudUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Assets/MyAsset/UI/HUD/HUD.uxml");
            if (hudUxml != null)
            {
                uiDoc.visualTreeAsset = hudUxml;
            }
            else
            {
                Debug.LogWarning("[TestSceneBuilder] HUD.uxml が見つかりません。HUDは空になります。");
            }

            // PanelSettings
            PanelSettings panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(
                "Assets/MyAsset/UI/GamePanelSettings.asset");
            if (panelSettings != null)
            {
                uiDoc.panelSettings = panelSettings;
            }

            hudObj.AddComponent<HudController>();
        }

        private static void SetupDamagePopups()
        {
            GameObject popupObj = new GameObject("[PLACEHOLDER]DamagePopups");
            popupObj.AddComponent<DamagePopupController>();
        }

        // ===== エリアラベル =====

        private static void CreateAreaLabel(string text, Vector3 position)
        {
            GameObject label = new GameObject($"Label_{text}");
            label.transform.position = position;

            TextMesh tm = label.AddComponent<TextMesh>();
            tm.text = text;
            tm.fontSize = 24;
            tm.characterSize = 0.15f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = new Color(1f, 1f, 1f, 0.5f);

            // SortingOrderが効くようにMeshRendererを設定
            MeshRenderer mr = label.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.sortingOrder = 20;
            }
        }

        // ===== 物理レイヤー衝突設定 =====

        private static void SetupPhysicsLayers()
        {
            // CollisionMatrixSetup の正規設定を適用
            // 全キャラクターは CharaPassThrough(12) / CharaCollide(13) / CharaInvincible(14) を使用
            // 味方/敵の区別はレイヤーではなくHitBox内の陣営チェック(CharacterBelong)で行う
            CollisionMatrixSetup.SetupCollisionMatrix();

            // 追加: Hitbox同士はGround(6)と衝突しない
            Physics2D.IgnoreLayerCollision(10, 6, true);
            Physics2D.IgnoreLayerCollision(11, 6, true);
            // PlayerHitbox(10) と EnemyHitbox(11) は衝突しない
            Physics2D.IgnoreLayerCollision(10, 11, true);
        }

        // ===== ユーティリティ =====

        private static void SetupLayers()
        {
            SerializedObject tagManager = new SerializedObject(
                AssetDatabase.LoadMainAssetAtPath("ProjectSettings/TagManager.asset"));
            SerializedProperty layers = tagManager.FindProperty("layers");

            SetLayerIfEmpty(layers, 6, "Ground");
            SetLayerIfEmpty(layers, 7, "Player");
            SetLayerIfEmpty(layers, 8, "Enemy");
            SetLayerIfEmpty(layers, 9, "Companion");
            SetLayerIfEmpty(layers, 10, "PlayerHitbox");
            SetLayerIfEmpty(layers, 11, "EnemyHitbox");
            SetLayerIfEmpty(layers, 12, "CharaPassThrough");
            SetLayerIfEmpty(layers, 13, "CharaCollide");
            SetLayerIfEmpty(layers, 14, "CharaInvincible");

            tagManager.ApplyModifiedProperties();
        }

        private static void SetLayerIfEmpty(SerializedProperty layers, int index, string name)
        {
            SerializedProperty layer = layers.GetArrayElementAtIndex(index);
            if (string.IsNullOrEmpty(layer.stringValue))
            {
                layer.stringValue = name;
            }
        }

        private static Sprite CreateSquareSprite(Color color)
        {
            Texture2D tex = new Texture2D(k_SquareSize, k_SquareSize);
            Color[] pixels = new Color[k_SquareSize * k_SquareSize];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = color;
            }
            tex.SetPixels(pixels);
            tex.Apply();
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Repeat;

            return Sprite.Create(tex,
                new Rect(0, 0, k_SquareSize, k_SquareSize),
                new Vector2(0.5f, 0f),
                k_Ppu);
        }

        private static void SetCharacterInfoField(BaseCharacter character, CharacterInfo info)
        {
            if (info == null)
            {
                return;
            }

            System.Type type = typeof(BaseCharacter);
            System.Reflection.FieldInfo field = type.GetField("_characterInfo",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(character, info);
            }
        }

        private static void SetPrivateField<T>(object target, string fieldName, T value)
        {
            System.Type type = target.GetType();
            System.Reflection.FieldInfo field = type.GetField(fieldName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(target, value);
            }
        }

        private static CharacterInfo LoadOrWarnCharacterInfo(string path)
        {
            CharacterInfo info = AssetDatabase.LoadAssetAtPath<CharacterInfo>(path);
            if (info == null)
            {
                Debug.LogWarning($"[TestSceneBuilder] {path} が見つかりません。シーン構築後に手動で設定してください。");
            }
            return info;
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
