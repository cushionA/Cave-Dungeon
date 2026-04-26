using System.Collections.Generic;
using UnityEngine;
using Game.Core;

namespace Game.Runtime
{
    /// <summary>
    /// 仲間キャラクターMonoBehaviour。
    /// CompanionController（ピュアロジック）を保持してフルAIを駆動する。
    /// FollowBehavior + JudgmentLoop + ModeController + StanceManager + CompanionMpManager を統合。
    ///
    /// AI判定フロー:
    ///   CompanionController.Tick() → MP回復 → FollowBehavior → ModeController → JudgmentLoop
    ///   → 内部ActionExecutor.Execute() → BridgeAIAction() で ActionExecutorController に橋渡し
    /// </summary>
    public class CompanionCharacter : BaseCharacter
    {
        [Header("AI設定")]
        [SerializeField] private AIInfo _aiInfo;
        [SerializeField] private CompanionMpSettings _mpSettings;

        [Header("追従設定")]
        [SerializeField] private float _followDistance = 2.0f;
        [SerializeField] private float _maxLeashDistance = 15.0f;
        [SerializeField] private Transform _playerTransform;

        private ActionExecutorController _actionExecutorController;
        private SpriteRenderer _spriteRenderer;

        // AI（ピュアロジック）
        private CompanionController _aiController;

        // 候補リスト再利用（GC回避、インスタンスごとに保持）
        private readonly List<int> _candidates = new List<int>(8);

        public CompanionController AIController => _aiController;

        /// <summary>テスト/エディタ用: AIInfoを設定する。</summary>
        public void SetAIInfo(AIInfo aiInfo) { _aiInfo = aiInfo; }

        /// <summary>テスト/エディタ用: CompanionMpSettingsを設定する。</summary>
        public void SetMpSettings(CompanionMpSettings settings) { _mpSettings = settings; }

        protected override void Awake()
        {
            base.Awake();
            _actionExecutorController = GetComponent<ActionExecutorController>();
            _spriteRenderer = GetComponent<SpriteRenderer>();
        }

        protected override void Start()
        {
            base.Start();
            CharacterRegistry.RegisterAlly(ObjectHash);

            // プレイヤーが指定されていなければハッシュ経由で取得 (GameManager 中央ハブ原則)。
            // CharacterRegistry.PlayerHash → GameManager.Data.GetManaged で O(1) アクセスし、
            // FindGameObjectWithTag (内部で全 GameObject 走査 O(n)) を回避する (Issue #75 HIGH-Hub-1)。
            if (_playerTransform == null)
            {
                int playerHash = CharacterRegistry.PlayerHash;
                if (playerHash != 0 && GameManager.Data != null)
                {
                    ManagedCharacter playerManaged = GameManager.Data.GetManaged(playerHash);
                    if (playerManaged != null)
                    {
                        _playerTransform = playerManaged.transform;
                    }
                }

                // PlayerCharacter.Start が未到達 (Unity の Start 実行順は不定) の場合のみ
                // Tag 検索を一度だけフォールバックとして使う
                if (_playerTransform == null)
                {
                    GameObject player = GameObject.FindGameObjectWithTag("Player");
                    if (player != null)
                    {
                        _playerTransform = player.transform;
                    }
                }
            }

            InitializeAI();
        }

        private void InitializeAI()
        {
            if (_playerTransform == null || GameManager.Data == null)
            {
                return;
            }

            BaseCharacter playerBase = _playerTransform.GetComponent<BaseCharacter>();
            int playerHash = playerBase != null ? playerBase.ObjectHash : 0;
            if (playerHash == 0)
            {
                return;
            }

            // CharacterInfo から maxMp 取得
            float maxMp = CharacterInfoRef != null ? CharacterInfoRef.maxMp : 50f;
            int initialReserveMp = _mpSettings.maxReserveMp > 0 ? _mpSettings.maxReserveMp : 100;

            _aiController = new CompanionController(
                ObjectHash, playerHash, GameManager.Data,
                maxMp, initialReserveMp, _mpSettings, GameManager.Events);

            // AIモード設定
            if (_aiInfo != null)
            {
                AIMode[] modes = AIInfoConverter.ConvertModes(_aiInfo);
                ModeTransitionRule[] transitions = AIInfoConverter.ConvertTransitions(_aiInfo);

                // AIInfoConverterは敵AI用（belong=Ally）なので、
                // 仲間AI用にターゲットフィルタをEnemy陣営に反転する
                FlipTargetBelongToEnemy(modes);

                _aiController.SetAIModes(modes, transitions);
                _aiController.FollowBehavior.SetDistances(_followDistance, _maxLeashDistance);
            }

            // イベント購読
            _aiController.MpManager.OnVanish += OnCompanionVanish;
            _aiController.MpManager.OnReturn += OnCompanionReturn;
        }

        private void FixedUpdate()
        {
            if (!IsAlive || _playerTransform == null)
            {
                return;
            }

            UpdateGroundCheck();

            if (_aiController != null)
            {
                // 候補リスト構築（敵ハッシュ）
                PopulateAITargetCandidates(_candidates, targetAllyFaction: false);

                // AI Tick（MP回復 → 追従判定 → モード遷移 → 行動判定）
                _aiController.Tick(Time.fixedDeltaTime, _candidates, Time.time);

                // AI が選択したアクションを ActionExecutorController に橋渡し
                BridgeAIActionForJudgmentLoop(_aiController.JudgmentLoop);

                // 追従移動
                ApplyFollowMovement();
            }
            else
            {
                // AI未初期化時はシンプル追従のみ
                ApplySimpleFollow();
            }

            SyncPositionToData();
        }

        /// <summary>
        /// FollowBehaviorの状態に基づいてRigidbody移動を適用する。
        /// ActionExecutorControllerが攻撃中は移動しない。
        /// </summary>
        private void ApplyFollowMovement()
        {
            // 攻撃中は移動しない
            if (_actionExecutorController != null && _actionExecutorController.IsExecuting)
            {
                _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
                return;
            }

            FollowState followState = _aiController.FollowBehavior.CurrentState;
            Vector2 myPos = (Vector2)transform.position;
            Vector2 playerPos = (Vector2)_playerTransform.position;

            switch (followState)
            {
                case FollowState.Waiting:
                    // ターゲットがいる場合はターゲットに向かって移動
                    int targetHash = _aiController.JudgmentLoop?.CurrentTargetHash ?? 0;
                    if (targetHash != 0 && GameManager.IsCharacterValid(targetHash))
                    {
                        MoveTowardTarget(targetHash);
                    }
                    else
                    {
                        _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
                    }
                    break;

                case FollowState.Following:
                {
                    Vector2 target = _aiController.FollowBehavior.GetFollowTarget(myPos, playerPos);
                    ref MoveParams moveParams = ref GameManager.Data.GetMoveParams(ObjectHash);
                    float dir = target.x > myPos.x ? 1f : -1f;
                    _rb.linearVelocity = new Vector2(dir * moveParams.moveSpeed * 0.8f, _rb.linearVelocity.y);
                    SetFacing(dir > 0f);
                    break;
                }

                case FollowState.Teleporting:
                {
                    Vector2 teleportPos = playerPos + new Vector2(
                        _isFacingRight ? -_followDistance : _followDistance, 0f);
                    transform.position = (Vector3)teleportPos;
                    _rb.linearVelocity = Vector2.zero;
                    break;
                }
            }
        }

        /// <summary>
        /// ターゲットに向かって移動する（戦闘時）。
        /// </summary>
        private void MoveTowardTarget(int targetHash)
        {
            ref CharacterVitals targetVitals = ref GameManager.Data.GetVitals(targetHash);
            ref CharacterVitals myVitals = ref GameManager.Data.GetVitals(ObjectHash);
            ref MoveParams moveParams = ref GameManager.Data.GetMoveParams(ObjectHash);

            float diff = targetVitals.position.x - myVitals.position.x;
            float absDist = Mathf.Abs(diff);
            if (absDist > GameConstants.k_DefaultAttackRange)
            {
                float dir = diff > 0f ? 1f : -1f;
                float speed = moveParams.moveSpeed > 0f ? moveParams.moveSpeed : GameConstants.k_FallbackMoveSpeed;
                _rb.linearVelocity = new Vector2(dir * speed, _rb.linearVelocity.y);
                SetFacing(dir > 0f);
            }
            else
            {
                _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
                // 向きをターゲットに合わせる
                SetFacing(diff > 0f);
            }
        }

        /// <summary>
        /// AIInfoConverterが生成したモードのターゲットフィルタを
        /// Enemy陣営に差し替える（仲間AIは敵をターゲットにする）。
        /// </summary>
        private static void FlipTargetBelongToEnemy(AIMode[] modes)
        {
            for (int i = 0; i < modes.Length; i++)
            {
                if (modes[i].targetSelects == null)
                {
                    continue;
                }
                for (int j = 0; j < modes[i].targetSelects.Length; j++)
                {
                    modes[i].targetSelects[j].filter.belong = CharacterBelong.Enemy;
                }
            }
        }

        /// <summary>
        /// AI未初期化時のシンプル追従（フォールバック）。
        /// </summary>
        private void ApplySimpleFollow()
        {
            Vector2 myPos = (Vector2)transform.position;
            Vector2 playerPos = (Vector2)_playerTransform.position;
            float sqrDist = (myPos - playerPos).sqrMagnitude;

            if (sqrDist > _maxLeashDistance * _maxLeashDistance)
            {
                Vector2 teleportPos = playerPos + new Vector2(
                    _isFacingRight ? -_followDistance : _followDistance, 0f);
                transform.position = (Vector3)teleportPos;
                _rb.linearVelocity = Vector2.zero;
            }
            else if (sqrDist > _followDistance * _followDistance)
            {
                // 追従
                ref MoveParams moveParams = ref GameManager.Data.GetMoveParams(ObjectHash);
                float dir = playerPos.x > myPos.x ? 1f : -1f;
                _rb.linearVelocity = new Vector2(dir * moveParams.moveSpeed * 0.8f, _rb.linearVelocity.y);
                SetFacing(dir > 0f);
            }
            else
            {
                _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
            }
        }

        private void OnCompanionVanish()
        {
            // MP=0消滅: 半透明化
            SpriteRenderer sr = _spriteRenderer;
            if (sr != null)
            {
                Color c = sr.color;
                c.a = 0.3f;
                sr.color = c;
            }
        }

        private void OnCompanionReturn()
        {
            // 復帰: 不透明に戻す
            SpriteRenderer sr = _spriteRenderer;
            if (sr != null)
            {
                Color c = sr.color;
                c.a = 1f;
                sr.color = c;
            }
        }

        protected override void OnDestroy()
        {
            if (_aiController?.MpManager != null)
            {
                _aiController.MpManager.OnVanish -= OnCompanionVanish;
                _aiController.MpManager.OnReturn -= OnCompanionReturn;
                _aiController.MpManager.Dispose();
            }

            if (_aiController?.StanceManager != null)
            {
                _aiController.StanceManager.Dispose();
            }

            // GameEvents 購読解除（MpManager/StanceManager とは別リソース）
            _aiController?.Dispose();

            CharacterRegistry.Unregister(ObjectHash);
            base.OnDestroy();
        }

        public override void OnPoolReturn()
        {
            CharacterRegistry.Unregister(ObjectHash);
            base.OnPoolReturn();
        }

        public override void OnPoolAcquire()
        {
            base.OnPoolAcquire();
            CharacterRegistry.RegisterAlly(ObjectHash);
        }
    }
}
