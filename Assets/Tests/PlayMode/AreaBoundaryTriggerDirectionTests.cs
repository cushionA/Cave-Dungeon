using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Game.Core;
using Game.Runtime;

namespace Game.Tests.PlayMode
{
    /// <summary>
    /// AreaBoundaryTrigger の進入方向判定を検証する。
    /// expectedEntryDirection が設定されているとき、ドット積が正の方向からの進入でのみ
    /// LevelStreamingController.RequestAreaLoad が呼ばれることを確認する。
    /// </summary>
    public class AreaBoundaryTriggerDirectionTests
    {
        private const string k_SceneToLoad = "Area_Forest";
        private const string k_PlayerTag = "Player";

        private List<Object> _spawnedObjects;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            _spawnedObjects = new List<Object>();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (int i = _spawnedObjects.Count - 1; i >= 0; i--)
            {
                if (_spawnedObjects[i] != null)
                {
                    Object.Destroy(_spawnedObjects[i]);
                }
            }
            _spawnedObjects.Clear();
            TestSceneHelper.Cleanup();
            yield return null;
        }

        /// <summary>
        /// GameManager + LevelStreamingController を構築し、
        /// SceneManager を叩かないダミーコールバックで差し替える。
        /// </summary>
        private LevelStreamingController CreateWiredLevelStreaming(out List<string> loadRequests)
        {
            // 既存のGameManagerがあれば破棄
            if (GameManager.Instance != null)
            {
                Object.DestroyImmediate(GameManager.Instance.gameObject);
            }

            GameObject gmGo = new GameObject("TestGameManager");
            _spawnedObjects.Add(gmGo);

            GameObject lsGo = new GameObject("LevelStreaming");
            lsGo.transform.SetParent(gmGo.transform);
            lsGo.AddComponent<LevelStreamingController>();

            gmGo.AddComponent<GameManager>();

            LevelStreamingController lsc = GameManager.LevelStreaming;
            Assert.IsNotNull(lsc, "LevelStreamingController が取得できているべき");

            List<string> requests = new List<string>();
            loadRequests = requests;
            lsc.SetSceneIOCallbacksForTest(
                loadCallback: sceneName => requests.Add(sceneName),
                unloadCallback: _ => { });

            return lsc;
        }

        /// <summary>
        /// AreaBoundaryTrigger を1つ構築して返す。
        /// テスト専用セッターで sceneToLoad と expectedEntryDirection を差し込む。
        /// </summary>
        private AreaBoundaryTrigger CreateTrigger(Vector2 expectedDirection)
        {
            GameObject go = new GameObject("TestAreaBoundaryTrigger");
            _spawnedObjects.Add(go);

            BoxCollider2D col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(2f, 2f);

            AreaBoundaryTrigger trigger = go.AddComponent<AreaBoundaryTrigger>();
            trigger.SetSceneToLoadForTest(k_SceneToLoad);
            trigger.SetExpectedEntryDirectionForTest(expectedDirection);
            return trigger;
        }

        /// <summary>
        /// Player タグ付きの Rigidbody2D 搭載キャラを生成する。
        /// </summary>
        private Collider2D CreatePlayerCollider(Vector2 linearVelocity)
        {
            GameObject go = new GameObject("TestPlayer");
            _spawnedObjects.Add(go);
            go.tag = k_PlayerTag;

            Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f; // テスト中は重力無効
            rb.linearVelocity = linearVelocity;

            BoxCollider2D col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(0.6f, 0.9f);
            return col;
        }

        [UnityTest]
        public IEnumerator ExpectedDirectionZero_AnyDirection_InvokesLoad()
        {
            LevelStreamingController lsc = CreateWiredLevelStreaming(out List<string> loadRequests);
            yield return null;

            // direction=zero は既定動作（全方向許容）
            AreaBoundaryTrigger trigger = CreateTrigger(Vector2.zero);
            // 右向き速度でも左向きでも関係なく発火すること
            Collider2D playerLeft = CreatePlayerCollider(new Vector2(-5f, 0f));

            trigger.InvokeOnTriggerEnter2DForTest(playerLeft);

            Assert.AreEqual(1, loadRequests.Count,
                "direction=zero では方向に関係なく RequestAreaLoad が呼ばれるべき");
            Assert.AreEqual(k_SceneToLoad, loadRequests[0]);
        }

        [UnityTest]
        public IEnumerator ExpectedDirectionRight_EnterFromRight_InvokesLoad()
        {
            LevelStreamingController lsc = CreateWiredLevelStreaming(out List<string> loadRequests);
            yield return null;

            // 右方向進入期待 × 右向き速度 → 発火
            AreaBoundaryTrigger trigger = CreateTrigger(Vector2.right);
            Collider2D playerMovingRight = CreatePlayerCollider(new Vector2(5f, 0f));

            trigger.InvokeOnTriggerEnter2DForTest(playerMovingRight);

            Assert.AreEqual(1, loadRequests.Count,
                "正方向進入では RequestAreaLoad が呼ばれるべき");
            Assert.AreEqual(k_SceneToLoad, loadRequests[0]);
        }

        [UnityTest]
        public IEnumerator ExpectedDirectionRight_EnterFromLeft_SkipsLoad()
        {
            LevelStreamingController lsc = CreateWiredLevelStreaming(out List<string> loadRequests);
            yield return null;

            // 右方向進入期待 × 左向き速度 → 退出方向なのでスキップ
            AreaBoundaryTrigger trigger = CreateTrigger(Vector2.right);
            Collider2D playerMovingLeft = CreatePlayerCollider(new Vector2(-5f, 0f));

            trigger.InvokeOnTriggerEnter2DForTest(playerMovingLeft);

            Assert.AreEqual(0, loadRequests.Count,
                "逆方向進入では RequestAreaLoad を呼ばないべき");
        }

        [UnityTest]
        public IEnumerator ExpectedDirectionSet_ZeroVelocity_InvokesLoadAsFallback()
        {
            LevelStreamingController lsc = CreateWiredLevelStreaming(out List<string> loadRequests);
            yield return null;

            // 方向指定あり × 速度ゼロ → 方向不定として全方向許容（既存挙動を壊さない）
            AreaBoundaryTrigger trigger = CreateTrigger(Vector2.right);
            Collider2D playerStopped = CreatePlayerCollider(Vector2.zero);

            trigger.InvokeOnTriggerEnter2DForTest(playerStopped);

            Assert.AreEqual(1, loadRequests.Count,
                "速度が極小の場合は方向判定不能として RequestAreaLoad を発火させるべき");
            Assert.AreEqual(k_SceneToLoad, loadRequests[0]);
        }
    }
}
