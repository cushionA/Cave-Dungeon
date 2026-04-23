using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Game.Core;
using Game.Runtime;
using CharacterInfo = Game.Core.CharacterInfo;

namespace Game.Tests.PlayMode
{
    /// <summary>
    /// PlayerCharacter の壁蹴り呼び出し経路 (IsTouchingWall + AdvancedMovementLogic.TryWallKick)
    /// を GameObject 配置で結合検証する。
    ///
    /// 観点:
    /// - 壁 GameObject (Ground レイヤー) + Player 配置 + AbilityFlag.WallKick 付与
    /// - 空中ジャンプ入力 (jumpBufferTimer) を発行
    /// - Rigidbody2D.linearVelocity が壁から離れる方向にキックされる
    ///
    /// 純ロジック単体テストは PlayerMovementAdvancedTests.cs に 5 本あるが、
    /// 本ファイルは PlayerCharacter.FixedUpdate の実経路 (壁接触の OverlapBox 検知 +
    /// SoA の AbilityFlags 取得 + Rigidbody2D への反映) が破壊されないことを保証する。
    /// </summary>
    public class Integration_WallKickTests
    {
        private const int k_GroundLayer = 6;

        private GameObject _gmObject;
        private GameObject _groundObject;
        private GameObject _wallObject;
        private GameObject _playerObject;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            _gmObject = TestSceneHelper.CreateGameManager();
            _groundObject = TestSceneHelper.CreateGround();
            yield return null; // Awake
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_playerObject != null)
            {
                Object.Destroy(_playerObject);
            }
            if (_wallObject != null)
            {
                Object.Destroy(_wallObject);
            }
            if (_groundObject != null)
            {
                Object.Destroy(_groundObject);
            }
            TestSceneHelper.Cleanup();
            yield return null;
        }

        /// <summary>右側に立つ壁 (Ground レイヤー) を作成する。</summary>
        private GameObject CreateWallRight(float x, float width = 0.4f, float height = 6f)
        {
            GameObject wall = new GameObject("TestWallRight");
            wall.layer = k_GroundLayer;
            BoxCollider2D col = wall.AddComponent<BoxCollider2D>();
            col.size = new Vector2(width, height);
            wall.transform.position = new Vector3(x, 2f, 0f);
            return wall;
        }

        /// <summary>
        /// 壁接触 + WallKick + 空中ジャンプ入力 → Rigidbody2D.linearVelocity が壁から離れる方向
        /// (x 成分が左向き負値) かつ上方向 (y 成分が正値) に更新されることを検証。
        /// </summary>
        [UnityTest]
        public IEnumerator PlayerCharacter_TouchingWallWithWallKickFlag_JumpPushesAwayFromWall()
        {
            // Arrange: 壁を右側に配置
            _wallObject = CreateWallRight(x: 0.6f);

            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo();
            // プレイヤーを空中位置 (y=2 = 壁の中央付近) にスポーン
            _playerObject = SpawnPlayer(info, new Vector3(0.05f, 2f, 0f));

            yield return null; // Awake + Start (SoA 登録)

            PlayerCharacter pc = _playerObject.GetComponent<PlayerCharacter>();
            Rigidbody2D rb = _playerObject.GetComponent<Rigidbody2D>();

            // 重力・接地状態に影響されないよう gravityScale=0、velocity=0 で空中待機
            rb.gravityScale = 0f;
            rb.linearVelocity = Vector2.zero;

            // WallKick Ability を付与
            ref CharacterFlags flags = ref GameManager.Data.GetFlags(pc.ObjectHash);
            flags.AbilityFlags = AbilityFlag.WallKick;

            // プレイヤーの向きは右 (デフォルト _isFacingRight = true) → 右側の壁に接触中
            // 壁から離れる方向キック = X 左向き (負値)
            // _jumpBufferTimer をリフレクションで直接セットして「空中ジャンプ入力」を再現
            SetJumpBufferTimer(pc, 0.1f);

            // FixedUpdate を 1 回回す
            yield return new WaitForFixedUpdate();

            // Assert: 壁から離れる方向 (-X) にキック、上方向 (+Y) も付与
            Vector2 velocity = rb.linearVelocity;
            Assert.Less(velocity.x, 0f,
                $"右の壁を蹴った場合 X 速度は負 (左向き) のはず。actual={velocity.x}");
            Assert.Greater(velocity.y, 0f,
                $"壁蹴りは上方向にも力を与えるはず。actual={velocity.y}");
            // AdvancedMovementLogic の k_WallKickForceX / k_WallKickForceY と一致することも確認
            Assert.AreEqual(-AdvancedMovementLogic.k_WallKickForceX, velocity.x, 0.01f,
                "X 速度は k_WallKickForceX の符号反転値 (壁から離れる方向)");
            Assert.AreEqual(AdvancedMovementLogic.k_WallKickForceY, velocity.y, 0.01f,
                "Y 速度は k_WallKickForceY");
        }

        /// <summary>
        /// 壁接触中でも WallKick Ability を持たない場合、空中ジャンプ入力で壁蹴りは発生しない。
        /// linearVelocity は変化しない (バッファドジャンプ経路に入るが TryWallKick が zero を返す)。
        /// </summary>
        [UnityTest]
        public IEnumerator PlayerCharacter_TouchingWallWithoutWallKickFlag_JumpDoesNotKick()
        {
            _wallObject = CreateWallRight(x: 0.6f);

            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo();
            _playerObject = SpawnPlayer(info, new Vector3(0.05f, 2f, 0f));
            yield return null;

            PlayerCharacter pc = _playerObject.GetComponent<PlayerCharacter>();
            Rigidbody2D rb = _playerObject.GetComponent<Rigidbody2D>();

            rb.gravityScale = 0f;
            rb.linearVelocity = Vector2.zero;

            // WallKick Ability を**付与しない** (AbilityFlag.None のまま)
            ref CharacterFlags flags = ref GameManager.Data.GetFlags(pc.ObjectHash);
            flags.AbilityFlags = AbilityFlag.None;

            SetJumpBufferTimer(pc, 0.1f);

            yield return new WaitForFixedUpdate();

            // Assert: 壁蹴り非成立 → Y 速度は正にならない
            Vector2 velocity = rb.linearVelocity;
            Assert.LessOrEqual(velocity.y, 0.01f,
                $"WallKick 非所持時は壁蹴り成立せず、Y 速度は上向きにならない。actual={velocity.y}");
        }

        /// <summary>
        /// WallKick を持っていても壁に接触していなければ壁蹴り不成立。
        /// IsTouchingWall の OverlapBox 経路が機能していることを保証する。
        /// </summary>
        [UnityTest]
        public IEnumerator PlayerCharacter_NotTouchingWall_JumpDoesNotKick()
        {
            // 壁を作らない or 遠くに配置
            CharacterInfo info = TestSceneHelper.CreateTestCharacterInfo();
            _playerObject = SpawnPlayer(info, new Vector3(0f, 2f, 0f));
            yield return null;

            PlayerCharacter pc = _playerObject.GetComponent<PlayerCharacter>();
            Rigidbody2D rb = _playerObject.GetComponent<Rigidbody2D>();

            rb.gravityScale = 0f;
            rb.linearVelocity = Vector2.zero;

            ref CharacterFlags flags = ref GameManager.Data.GetFlags(pc.ObjectHash);
            flags.AbilityFlags = AbilityFlag.WallKick;

            SetJumpBufferTimer(pc, 0.1f);

            yield return new WaitForFixedUpdate();

            Vector2 velocity = rb.linearVelocity;
            Assert.LessOrEqual(velocity.y, 0.01f,
                $"壁未接触時は IsTouchingWall=false により壁蹴り不成立。actual Y={velocity.y}");
        }

        // ---------------------- Helpers ----------------------

        /// <summary>
        /// PlayerCharacter 付き GameObject を生成する。BaseCharacter.Start が SoA 登録するため、
        /// CharacterInfo を設定済みで返す。PlayerInputHandler は付与せず、
        /// _inputHandler == null ケースに沿って _jumpBufferTimer を直接リフレクションで操作する。
        /// </summary>
        private static GameObject SpawnPlayer(CharacterInfo info, Vector3 position)
        {
            GameObject go = new GameObject("TestPlayer");
            go.transform.position = position;

            Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = GameConstants.k_GravityScale;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;

            BoxCollider2D col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(0.6f, 0.9f);

            PlayerCharacter pc = go.AddComponent<PlayerCharacter>();
            TestSceneHelper.SetCharacterInfo(pc, info);
            return go;
        }

        /// <summary>
        /// PlayerCharacter._jumpBufferTimer を直接セットする。
        /// PlayerInputHandler を介さずに「空中ジャンプ入力がバッファされている状態」を再現する。
        /// </summary>
        private static void SetJumpBufferTimer(PlayerCharacter pc, float value)
        {
            FieldInfo field = typeof(PlayerCharacter).GetField(
                "_jumpBufferTimer",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "PlayerCharacter._jumpBufferTimer が存在する");
            field.SetValue(pc, value);
        }
    }
}
