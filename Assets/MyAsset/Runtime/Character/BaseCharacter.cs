using System.Collections.Generic;
using UnityEngine;
using Game.Core;
using CharacterInfo = Game.Core.CharacterInfo;

namespace Game.Runtime
{
    /// <summary>
    /// キャラクター基底MonoBehaviour。
    /// ObjectHash保持、SoAコンテナ登録、接地判定を担う。
    /// MonoBehaviour層は薄くし、ロジックは純ロジック層に委譲する。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(BoxCollider2D))]
    public class BaseCharacter : ManagedCharacter
    {
        private const float k_GroundCheckDistance = 0.1f;
        private const float k_GroundCheckWidthRatio = 0.9f;
        private const float k_WallCheckExtend = 0.05f;
        private const float k_WallCheckHeightRatio = 0.9f;

        [SerializeField] protected CharacterInfo _characterInfo;

        protected Rigidbody2D _rb;
        protected BoxCollider2D _collider;
        protected DamageReceiver _damageReceiver;
        protected CharacterAnimationController _animationController;

        private int _objectHash;
        private bool _isGrounded;
        private GroundInfo _groundInfo;
        private bool _isRegistered;

        public override int ObjectHash => _objectHash;
        public bool IsGrounded => _isGrounded;
        public GroundInfo GroundInfo => _groundInfo;
        // Unity bool 変換で破棄済み DamageReceiver (fake null) を C# native null に変換してから IDamageable に格納する。
        // 呼び出し側で `if (receiver == null)` が interface 経由でも正しく動作する (Issue #73)。
        public override IDamageable Damageable => _damageReceiver != null ? (IDamageable)_damageReceiver : null;
        public DamageReceiver DamageReceiver => _damageReceiver;
        public CharacterAnimationController AnimationController => _animationController;
        public CharacterInfo CharacterInfoRef => _characterInfo;
        public bool IsAlive
        {
            get
            {
                if (!GameManager.IsCharacterValid(_objectHash))
                {
                    return false;
                }
                ref CharacterVitals vitals = ref GameManager.Data.GetVitals(_objectHash);
                return vitals.currentHp > 0;
            }
        }


#if UNITY_INCLUDE_TESTS
        /// <summary>テスト専用: CharacterInfoを設定する。リフレクション回避用。</summary>
        public void SetCharacterInfoForTest(CharacterInfo info) { _characterInfo = info; }
#endif

        [Header("接地判定")]
        [SerializeField] protected LayerMask _groundLayer = 1 << 6; // Layer 6 = Ground

        protected virtual void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _collider = GetComponent<BoxCollider2D>();
            _damageReceiver = GetComponent<DamageReceiver>();
            _animationController = GetComponent<CharacterAnimationController>();
            _objectHash = gameObject.GetHashCode();

            // 物理設定
            _rb.gravityScale = GameConstants.k_GravityScale;
            _rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        protected virtual void Start()
        {
            if (_isRegistered)
            {
                return; // OnPoolAcquireで先行登録済み
            }

            if (GameManager.Instance == null)
            {
                Debug.LogError($"[BaseCharacter] GameManager.Instance is null. Cannot register {gameObject.name}");
                return;
            }

            if (_characterInfo == null)
            {
                Debug.LogError($"[BaseCharacter] CharacterInfo is null on {gameObject.name}");
                return;
            }

            GameManager.Instance.RegisterCharacter(this, _characterInfo);
            _isRegistered = true;

            // 名前→ハッシュのマッピングを登録（DialogueSystem等の外部連携用）
            CharacterRegistry.RegisterName(_characterInfo.name, _objectHash);

            if (_damageReceiver != null)
            {
                _damageReceiver.SetArmorRecoveryParams(
                    _characterInfo.maxArmor,
                    _characterInfo.armorRecoveryRate,
                    _characterInfo.armorRecoveryDelay);
            }

            if (_animationController != null)
            {
                _animationController.Initialize(_objectHash);
            }
        }

        protected virtual void OnDestroy()
        {
            if (_isRegistered && GameManager.Instance != null)
            {
                GameManager.Instance.UnregisterCharacter(_objectHash);
            }
        }

        /// <summary>
        /// プールに返却される前に呼ぶ。SoAコンテナから登録解除する。
        /// SetActive(false)ではOnDestroyが呼ばれないため、明示的に解除が必要。
        /// </summary>
        public virtual void OnPoolReturn()
        {
            if (_isRegistered && GameManager.Instance != null)
            {
                GameManager.Instance.UnregisterCharacter(_objectHash);
                _isRegistered = false;
            }
        }

        /// <summary>
        /// プールから取得した後に呼ぶ。SoAコンテナに再登録し、ステータスをリセットする。
        /// SetActive(true)ではStart()が再実行されないため、明示的に登録が必要。
        /// </summary>
        public virtual void OnPoolAcquire()
        {
            if (_isRegistered || _characterInfo == null || GameManager.Instance == null)
            {
                return;
            }

            GameManager.Instance.RegisterCharacter(this, _characterInfo);
            _isRegistered = true;

            CharacterRegistry.RegisterName(_characterInfo.name, _objectHash);

            if (_damageReceiver != null)
            {
                // 前キャラの連続JG窓・ガード経過時間・行動特殊効果が残らないようクリア
                _damageReceiver.ResetInternalState();
                _damageReceiver.SetArmorRecoveryParams(
                    _characterInfo.maxArmor,
                    _characterInfo.armorRecoveryRate,
                    _characterInfo.armorRecoveryDelay);
            }
        }

        /// <summary>
        /// 指定方向への壁接触を判定する。接触していればコライダーのハッシュも返す。
        /// 壁蹴り / 壁張り付き判定の共通モジュール。敵・仲間・プレイヤー全キャラで使用可能。
        /// </summary>
        /// <param name="facingDir">1=右、-1=左</param>
        /// <param name="wallColliderId">接触壁のGetHashCode、未接触時は AdvancedMovementLogic.k_NoWallId</param>
        /// <returns>壁に接触しているか</returns>
        protected bool CheckWallContact(float facingDir, out int wallColliderId)
        {
            wallColliderId = AdvancedMovementLogic.k_NoWallId;
            if (_collider == null)
            {
                return false;
            }

            Bounds bounds = _collider.bounds;
            Vector2 origin = new Vector2(
                bounds.center.x + facingDir * (bounds.extents.x + k_WallCheckExtend),
                bounds.center.y);
            Vector2 size = new Vector2(k_WallCheckExtend, bounds.size.y * k_WallCheckHeightRatio);

            Collider2D hit = Physics2D.OverlapBox(origin, size, 0f, _groundLayer);
            if (hit == null)
            {
                return false;
            }

            wallColliderId = hit.GetHashCode();
            return true;
        }

        /// <summary>
        /// BoxCast接地判定。FixedUpdateで呼ぶ。
        /// </summary>
        public void UpdateGroundCheck()
        {
            Vector2 origin = (Vector2)transform.position + _collider.offset;
            origin.y -= _collider.size.y * 0.5f;

            Vector2 size = new Vector2(
                _collider.size.x * k_GroundCheckWidthRatio,
                0.05f);

            RaycastHit2D hit = Physics2D.BoxCast(
                origin, size, 0f, Vector2.down, k_GroundCheckDistance, _groundLayer);

            _isGrounded = hit.collider != null;
            _groundInfo = hit.collider != null
                ? new GroundInfo { isGrounded = true, normal = hit.normal }
                : GroundInfo.NotGrounded;
        }

        /// <summary>
        /// AI内部ExecutorのAttack/Castアクションを
        /// ActionExecutorController（MonoBehaviour側）に橋渡しする共通ヘルパー。
        /// 橋渡し成功後にAI側のアクションをForceCompleteして二重実行を防ぐ。
        /// </summary>
        protected static void BridgeAIActionToExecutor(
            ActionExecutor aiExecutor, ActionExecutorController monoExecutor, int targetHash)
        {
            if (aiExecutor == null || !aiExecutor.IsExecuting)
            {
                return;
            }

            if (monoExecutor.IsExecuting)
            {
                return;
            }

            ActionBase current = aiExecutor.CurrentAction;
            if (current == null)
            {
                return;
            }

            ActionSlot slot;
            if (current is AttackActionHandler attackHandler)
            {
                slot = new ActionSlot
                {
                    execType = ActionExecType.Attack,
                    paramId = attackHandler.LastParamId,
                    paramValue = 1f
                };
            }
            else if (current is CastActionHandler castHandler)
            {
                slot = new ActionSlot
                {
                    execType = ActionExecType.Cast,
                    paramId = castHandler.LastParamId,
                    paramValue = 1f
                };
            }
            else
            {
                return;
            }

            bool result = monoExecutor.ExecuteAction(slot, targetHash);
            if (result)
            {
                current.ForceComplete();
            }
        }

        /// <summary>
        /// JudgmentLoop からターゲット情報を抜き出して BridgeAIActionToExecutor に橋渡しする共通ラッパー。
        /// EnemyCharacter / CompanionCharacter で重複していた 8 行ロジックを集約 (Issue #79 M7-Reuse)。
        /// </summary>
        protected void BridgeAIActionForJudgmentLoop(JudgmentLoop loop)
        {
            if (_actionExecutorController == null || loop == null)
            {
                return;
            }

            ActionExecutor aiExecutor = loop.Executor;
            int targetHash = loop.CurrentTargetHash;
            BridgeAIActionToExecutor(aiExecutor, _actionExecutorController, targetHash);
        }

        /// <summary>
        /// AI ターゲット候補リストを CharacterRegistry から構築する共通ヘルパー。
        /// EnemyCharacter / CompanionCharacter の毎 FixedUpdate 重複を集約 (Issue #79 HIGH-Reuse-1)。
        /// </summary>
        /// <param name="candidates">出力先リスト。Clear から実行する。</param>
        /// <param name="targetAllyFaction">true = 敵 AI が味方陣営を狙う場合 (Player + Allies)、false = 仲間 AI が敵陣営を狙う場合 (Enemies)。</param>
        protected static void PopulateAITargetCandidates(List<int> candidates, bool targetAllyFaction)
        {
            candidates.Clear();
            if (targetAllyFaction)
            {
                // 敵 AI 視点: プレイヤー + 味方
                // 注: CharacterRegistry.RegisterPlayer は AllyHashes にも playerHash を入れるため、
                // 既存挙動 (PlayerHash 単独 Add → AllyHashes 全 Add) は player を二重に candidates へ入れる場合がある。
                // 動作変更を避けるためここでは旧ロジックをそのまま再現する。
                int playerHash = CharacterRegistry.PlayerHash;
                if (playerHash != 0)
                {
                    candidates.Add(playerHash);
                }
                List<int> allies = CharacterRegistry.AllyHashes;
                if (allies != null)
                {
                    for (int i = 0; i < allies.Count; i++)
                    {
                        candidates.Add(allies[i]);
                    }
                }
            }
            else
            {
                // 仲間 AI 視点: 敵
                List<int> enemies = CharacterRegistry.EnemyHashes;
                if (enemies != null)
                {
                    for (int i = 0; i < enemies.Count; i++)
                    {
                        candidates.Add(enemies[i]);
                    }
                }
            }
        }

        /// <summary>
        /// キャラクターの向きを設定する。localScale.xの符号で方向を表現。
        /// </summary>
        protected bool _isFacingRight = true;

        protected void SetFacing(bool facingRight)
        {
            if (facingRight == _isFacingRight)
            {
                return;
            }
            _isFacingRight = facingRight;
            Vector3 scale = transform.localScale;
            scale.x = _isFacingRight ? Mathf.Abs(scale.x) : -Mathf.Abs(scale.x);
            transform.localScale = scale;
        }

        /// <summary>
        /// SoAコンテナの位置情報を同期する。
        /// </summary>
        protected void SyncPositionToData()
        {
            if (!GameManager.IsCharacterValid(_objectHash))
            {
                return;
            }
            ref CharacterVitals vitals = ref GameManager.Data.GetVitals(_objectHash);
            vitals.position = (Vector2)transform.position;
        }
    }
}
