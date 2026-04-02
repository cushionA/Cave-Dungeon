using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Game.Runtime;

namespace Game.Editor
{
    /// <summary>
    /// CLIInternal: ダイアログなしで実行可能なメニュー項目。
    /// UniCli の Menu.Execute 経由で自動化ツールから呼び出す。
    /// </summary>
    public static class CLIInternalCommands
    {
        // --- AutoInputTester 設定 ---

        [MenuItem("Tools/CLIInternal/Run Auto Input All")]
        public static void RunAutoInputAll()
        {
            ConfigureAutoInputTester(all: true);
        }

        [MenuItem("Tools/CLIInternal/Run Auto Input Combat")]
        public static void RunAutoInputCombat()
        {
            ConfigureAutoInputTester(
                lightAttack: true, heavyAttack: true, skill: true,
                guard: true, stamina: true, aerialAttack: true);
        }

        [MenuItem("Tools/CLIInternal/Run Auto Input Movement")]
        public static void RunAutoInputMovement()
        {
            ConfigureAutoInputTester(
                move: true, jump: true, dodge: true, sprint: true);
        }

        [MenuItem("Tools/CLIInternal/Run Auto Input Composite")]
        public static void RunAutoInputComposite()
        {
            ConfigureAutoInputTester(composite: true);
        }

        // --- シーン操作 ---

        [MenuItem("Tools/CLIInternal/Open Core Test Scene")]
        public static void OpenCoreTestScene()
        {
            string path = "Assets/Scenes/CoreTestScene.unity";
            if (System.IO.File.Exists(
                System.IO.Path.Combine(Application.dataPath, "../", path)))
            {
                EditorSceneManager.OpenScene(path);
            }
            else
            {
                Debug.LogWarning("[CLIInternal] CoreTestScene.unity not found. Build test scene first.");
            }
        }

        [MenuItem("Tools/CLIInternal/Build And Enter Play")]
        public static void BuildAndEnterPlay()
        {
            TestSceneBuilder.BuildTestSceneNoDialog();
            EditorApplication.isPlaying = true;
        }

        // --- ヘルパー ---

        private static void ConfigureAutoInputTester(
            bool all = false,
            bool move = false, bool jump = false,
            bool lightAttack = false, bool heavyAttack = false,
            bool skill = false, bool dodge = false,
            bool sprint = false, bool guard = false,
            bool buttons = false, bool stamina = false,
            bool aerialAttack = false, bool composite = false)
        {
            // シーン内のAutoInputTesterを探す
            AutoInputTester tester = Object.FindFirstObjectByType<AutoInputTester>();
            if (tester == null)
            {
                Debug.LogWarning("[CLIInternal] AutoInputTester not found in scene. Build test scene first.");
                return;
            }

#if UNITY_EDITOR
            tester.EnableOnStart = true;
            tester.TestMove = all || move;
            tester.TestJump = all || jump;
            tester.TestLightAttack = all || lightAttack;
            tester.TestHeavyAttack = all || heavyAttack;
            tester.TestSkill = all || skill;
            tester.TestDodge = all || dodge;
            tester.TestSprint = all || sprint;
            tester.TestGuard = all || guard;
            tester.TestButtons = all || buttons;
            tester.TestStamina = all || stamina;
            tester.TestAerialAttack = all || aerialAttack;
            tester.TestComposite = all || composite;
#endif

            Debug.Log($"[CLIInternal] AutoInputTester configured. All={all}");
        }
    }
}
