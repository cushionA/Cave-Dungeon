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

        // 候補リスト再利用（GC回避）
        private static readonly List<int> s_Candidates = new List<int>(8);

        // AI → ActionExecutorController 橋渡し用
        private int _lastAIParamId = -1;
        private ActionExecType _lastAIExecType;
        private bool _aiActionPending;

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

            _enemyController = new EnemyController(ObjectHash, GameManager.Data);

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
                s_Candidates.Clear();
                int playerHash = CharacterRegistry.PlayerHash;
                if (playerHash != 0)
                {
                    s_Candidates.Add(playerHash);
                }
                List<int> allies = CharacterRegistry.AllyHashes;
                if (allies != null)
                {
                    for (int i = 0; i < allies.Count; i++)
                    {
                        s_Candidates.Add(allies[i]);
                    }
                }

                _enemyController.Tick(Time.fixedDeltaTime, s_Candidates, Time.time);

                // AI が選択したアクションを ActionExecutorController に橋渡し
                BridgeAIAction();

                // 簡易移動: ターゲットへ追尾（Sustained/MoveアクションはAI側で別途処理）
                UpdateMovement();
            }

            // 向き更新（ターゲット方向を向く）
            UpdateFacingToTarget();

            SyncPositionToData();
        }

        /// <summary>
        /// EnemyController内部のActionExecutorがAttack/Castを実行した場合、
        /// ActionExecutorController（MonoBehaviour側）に橋渡しする。
        /// 橋渡し成功後にAI側のアクションをForceCompleteして、
        /// AI再評価との二重実行を防ぐ。
        /// </summary>
        private void BridgeAIAction()
        {
            if (_actionExecutorController == null || _enemyController == null)
            {
                return;
            }

            ActionExecutor aiExecutor = _enemyController.JudgmentLoop?.Executor;
            if (aiExecutor == null || !aiExecutor.IsExecuting)
            {
                return;
            }

            // MonoBehaviour側が既に実行中なら橋渡ししない
            if (_actionExecutorController.IsExecuting)
            {
                return;
            }

            ActionBase current = aiExecutor.CurrentAction;
            if (current == null)
            {
                return;
            }

            // AttackActionHandler の場合、LastParamId で ActionSlot を再構築
            if (current is AttackActionHandler attackHandler)
            {
                ActionSlot slot = new ActionSlot
                {
                    execType = ActionExecType.Attack,
                    paramId = attackHandler.LastParamId,
                    paramValue = 1f
                };
                int targetHash = _enemyController.JudgmentLoop.CurrentTargetHash;
                bool result = _actionExecutorController.ExecuteAction(slot, targetHash);
                if (result)
                {
                    // AI側のアクションを完了としてマーク
                    // これにより次のAI評価で新しいアクションを選択可能になる
                    current.ForceComplete();
                }
            }
            else if (current is CastActionHandler castHandler)
            {
                ActionSlot slot = new ActionSlot
                {
                    execType = ActionExecType.Cast,
                    paramId = castHandler.LastParamId,
                    paramValue = 1f
                };
                int targetHash = _enemyController.JudgmentLoop.CurrentTargetHash;
                bool result = _actionExecutorController.ExecuteAction(slot, targetHash);
                if (result)
                {
                    current.ForceComplete();
                }
            }
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
            float attackRange = 1.5f; // デフォルト攻撃範囲

            if (absDist > attackRange)
            {
                float dir = diff > 0f ? 1f : -1f;
                float speed = moveParams.moveSpeed > 0f ? moveParams.moveSpeed : 3f;
                _rb.linearVelocity = new Vector2(dir * speed, _rb.linearVelocity.y);
            }
            else
            {
                _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
            }
        }

        protected override void OnDestroy()
        {
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
