using System.Collections.Generic;
using UnityEngine;
using Game.Core;

namespace Game.Runtime
{
    /// <summary>
    /// 敵キャラクターMonoBehaviour。
    /// SoA登録・接地判定・位置同期に加え、
    /// EnemyController（ピュアロジック）を保持してAI行動を駆動する。
    ///
    /// AI判定フロー:
    ///   EnemyController → JudgmentLoop → 内部ActionExecutor.Execute()
    ///   → AttackActionHandler.LastParamId を検出
    ///   → ActionExecutorController.ExecuteAction() でアニメ・ヒットボックスを駆動
    /// </summary>
    public class EnemyCharacter : BaseCharacter
    {
        [SerializeField] private AIInfo _aiInfo;

        private DamageDealer _damageDealer;
        private ActionExecutorController _actionExecutorController;

        // AI（ピュアロジック）
        private EnemyController _enemyController;

        // 候補リスト再利用（GC回避、インスタンスごとに保持）
        private readonly List<int> _candidates = new List<int>(8);

        public DamageDealer DamageDealer => _damageDealer;

        /// <summary>テスト/エディタ用: AIInfoを設定する。</summary>
        public void SetAIInfo(AIInfo aiInfo) { _aiInfo = aiInfo; }

        protected override void Awake()
        {
            base.Awake();
            _damageDealer = GetComponentInChildren<DamageDealer>();
            _actionExecutorController = GetComponent<ActionExecutorController>();
        }

        protected override void Start()
        {
            base.Start();
            CharacterRegistry.RegisterEnemy(ObjectHash);
            InitializeAI();
        }

        private void InitializeAI()
        {
            if (_aiInfo == null || GameManager.Data == null)
            {
                return;
            }

            _enemyController = new EnemyController(ObjectHash, GameManager.Data, GameManager.Events);

            AIMode[] modes = AIInfoConverter.ConvertModes(_aiInfo);
            ModeTransitionRule[] transitions = AIInfoConverter.ConvertTransitions(_aiInfo);
            _enemyController.SetAIModes(modes, transitions);

            // AI内部Executorのアクション実行を検知するために
            // AttackActionHandlerを監視する
        }

        private void FixedUpdate()
        {
            if (!IsAlive)
            {
                return;
            }

            UpdateGroundCheck();

            // AI Tick
            if (_enemyController != null)
            {
                // 候補リスト構築（プレイヤー + 味方）
                PopulateAITargetCandidates(_candidates, targetAllyFaction: true);

                _enemyController.Tick(Time.fixedDeltaTime, _candidates, Time.time);

                // AI が選択したアクションを ActionExecutorController に橋渡し
                BridgeAIActionForJudgmentLoop(_enemyController.JudgmentLoop);

                // 簡易移動: ターゲットへ追尾（Sustained/MoveアクションはAI側で別途処理）
                UpdateMovement();
            }

            // 向き更新（ターゲット方向を向く）
            UpdateFacingToTarget();

            SyncPositionToData();
        }

        /// <summary>
        /// ターゲット方向に向く。
        /// </summary>
        private void UpdateFacingToTarget()
        {
            if (_enemyController == null || _enemyController.JudgmentLoop == null)
            {
                return;
            }

            int targetHash = _enemyController.JudgmentLoop.CurrentTargetHash;
            if (targetHash == 0 || !GameManager.IsCharacterValid(targetHash))
            {
                return;
            }

            ref CharacterVitals targetVitals = ref GameManager.Data.GetVitals(targetHash);
            ref CharacterVitals myVitals = ref GameManager.Data.GetVitals(ObjectHash);
            float diff = targetVitals.position.x - myVitals.position.x;

            if (diff > 0.1f)
            {
                SetFacing(true);
            }
            else if (diff < -0.1f)
            {
                SetFacing(false);
            }
        }

        /// <summary>
        /// ターゲットへの簡易追尾移動。
        /// AI側がSustainedAction(Move)を選択した場合に対応。
        /// ActionExecutorControllerが攻撃中は移動しない。
        /// </summary>
        private void UpdateMovement()
        {
            // 攻撃中は移動しない
            if (_actionExecutorController != null && _actionExecutorController.IsExecuting)
            {
                _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
                return;
            }

            int targetHash = _enemyController.JudgmentLoop?.CurrentTargetHash ?? 0;
            if (targetHash == 0 || !GameManager.IsCharacterValid(targetHash))
            {
                _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
                return;
            }

            ref CharacterVitals targetVitals = ref GameManager.Data.GetVitals(targetHash);
            ref CharacterVitals myVitals = ref GameManager.Data.GetVitals(ObjectHash);
            ref MoveParams moveParams = ref GameManager.Data.GetMoveParams(ObjectHash);

            float diff = targetVitals.position.x - myVitals.position.x;
            float absDist = Mathf.Abs(diff);

            // 攻撃範囲の外ならターゲットへ移動
            // AIInfoの検出範囲内かつ攻撃範囲外の場合に追尾
            if (absDist > GameConstants.k_DefaultAttackRange)
            {
                float dir = diff > 0f ? 1f : -1f;
                float speed = moveParams.moveSpeed > 0f ? moveParams.moveSpeed : GameConstants.k_FallbackMoveSpeed;
                _rb.linearVelocity = new Vector2(dir * speed, _rb.linearVelocity.y);
            }
            else
            {
                _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
            }
        }

        protected override void OnDestroy()
        {
            if (_enemyController != null)
            {
                _enemyController.Dispose();
                _enemyController = null;
            }
            CharacterRegistry.Unregister(ObjectHash);
            base.OnDestroy();
        }

        public override void OnPoolReturn()
        {
            if (_enemyController != null)
            {
                _enemyController.Deactivate();
            }
            CharacterRegistry.Unregister(ObjectHash);
            base.OnPoolReturn();
        }

        public override void OnPoolAcquire()
        {
            base.OnPoolAcquire();
            CharacterRegistry.RegisterEnemy(ObjectHash);
            if (_enemyController != null)
            {
                _enemyController.Activate();
            }
            else
            {
                InitializeAI();
            }
        }
    }
}
