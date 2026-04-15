using UnityEngine;
using Game.Core;

namespace Game.Runtime
{
    /// <summary>
    /// Core ActionExecutor をシーン上で駆動するMonoBehaviour橋渡し。
    /// AttackInfo[] からActionSlot.paramIdで行動データを解決し、
    /// AnimationBridgeのフェーズに基づいてヒットボックス・ActionEffectを管理する。
    /// </summary>
    public class ActionExecutorController : MonoBehaviour
    {
        [SerializeField] private AttackInfo[] _attackInfos;

        private BaseCharacter _character;
        private CharacterAnimationController _animController;
        private HitBox _hitBox;
        private DamageDealer _damageDealer;
        private DamageReceiver _damageReceiver;
        private CharacterCollisionController _collisionController;

        private ActionExecutor _executor;
        private ActionPhaseCoordinator _phaseCoordinator;

        // 現在実行中の行動データ
        private AttackInfo _currentAttackInfo;

        // Bridge参照キャッシュ（OnEnable/OnDisable間で安定させる）
        private AnimationBridge _cachedBridge;

        public ActionExecutor Executor => _executor;
        public bool IsExecuting => _executor != null && _executor.IsExecuting;

#if UNITY_INCLUDE_TESTS
        /// <summary>テスト専用: AttackInfosを設定する。</summary>
        public void SetAttackInfosForTest(AttackInfo[] infos) { _attackInfos = infos; }
#endif

        private void Awake()
        {
            _character = GetComponent<BaseCharacter>();
            _animController = GetComponent<CharacterAnimationController>();
            _hitBox = GetComponentInChildren<HitBox>();
            _damageDealer = GetComponentInChildren<DamageDealer>();
            _damageReceiver = GetComponent<DamageReceiver>();
            _collisionController = GetComponent<CharacterCollisionController>();

            _executor = new ActionExecutor();
            _phaseCoordinator = new ActionPhaseCoordinator();

            RegisterHandlers();
        }

        private void Start()
        {
            // Bridge購読はStart()で行う。
            // CharacterAnimationController.Awake()でBridgeが生成されるが、
            // ActionExecutorController.Awake()→OnEnable()時点ではまだnullの場合がある
            // （同一GameObjectのAwake実行順序は不定）。
            SubscribeToBridge();
        }

        private void OnEnable()
        {
            // Start()前のOnEnableではBridge未生成の可能性があるため、
            // Bridge購読はStart()で行い、ここではexecutorイベントのみ購読する。
            // OnDisable→OnEnable再有効化時はBridge再購読が必要。
            if (_cachedBridge != null)
            {
                // 再有効化時: OnDisableで解除済みのBridgeを再購読
                _cachedBridge.OnPhaseChanged += HandlePhaseChanged;
            }
            if (_executor != null)
            {
                _executor.OnActionCompleted += HandleActionCompleted;
            }
        }

        private void OnDisable()
        {
            if (_cachedBridge != null)
            {
                _cachedBridge.OnPhaseChanged -= HandlePhaseChanged;
            }
            if (_executor != null)
            {
                _executor.OnActionCompleted -= HandleActionCompleted;
            }
        }

        private void SubscribeToBridge()
        {
            if (_animController != null && _animController.Bridge != null)
            {
                _cachedBridge = _animController.Bridge;
                _cachedBridge.OnPhaseChanged += HandlePhaseChanged;
            }
        }

        private void Update()
        {
            if (_executor == null)
            {
                return;
            }

            _executor.Tick(Time.deltaTime);

            if (_phaseCoordinator.ShouldCompleteAction)
            {
                CompleteCurrentAction();
            }
        }

        /// <summary>
        /// ActionSlotで指定された行動を実行する。
        /// paramIdに対応するAttackInfoからデータを解決し、アニメーション・ヒットボックスを開始する。
        /// </summary>
        public bool ExecuteAction(ActionSlot slot, int targetHash = 0)
        {
            if (_character == null)
            {
                return false;
            }

            // フェーズロックアウト: 現在のアクションがキャンセル不可フェーズなら新アクション拒否
            if (IsExecuting && _cachedBridge != null && !_cachedBridge.CurrentState.isCancelable)
            {
                return false;
            }

            int ownerHash = _character.ObjectHash;

            if (slot.execType == ActionExecType.Attack || slot.execType == ActionExecType.Cast)
            {
                AttackInfo info = ResolveAttackInfo(slot.paramId);
                if (info == null)
                {
                    return false;
                }

                if (!CheckAndDeductCost(ownerHash, info.staminaCost, info.mpCost))
                {
                    return false;
                }

                _currentAttackInfo = info;

                if (_damageReceiver != null && info.motionInfo.actionEffects != null
                    && info.motionInfo.actionEffects.Length > 0)
                {
                    _damageReceiver.SetActionEffects(info.motionInfo.actionEffects);
                }

                if (_animController != null)
                {
                    _animController.StartActionPhase(info.motionInfo, (byte)slot.paramId, info.cancelPoint);
                }

                // 攻撃のcontactTypeに応じて衝突モードを切替
                if (_collisionController != null)
                {
                    _collisionController.SetCollisionMode(info.contactType);
                }

                _phaseCoordinator.BeginAction();
            }
            else
            {
                _currentAttackInfo = null;
            }

            return _executor.Execute(ownerHash, targetHash, slot);
        }

        /// <summary>
        /// 現在実行中の行動をキャンセルする。
        /// </summary>
        public void CancelAction()
        {
            _executor.CancelCurrent();
            DeactivateHitbox();
            ClearActionState();
        }

        private AttackInfo ResolveAttackInfo(int paramId)
        {
            if (_attackInfos == null || paramId < 0 || paramId >= _attackInfos.Length)
            {
                return null;
            }
            return _attackInfos[paramId];
        }

        private bool CheckAndDeductCost(int ownerHash, float staminaCost, float mpCost)
        {
            if (!GameManager.IsCharacterValid(ownerHash))
            {
                return false;
            }

            ref CharacterVitals vitals = ref GameManager.Data.GetVitals(ownerHash);

            if (!ActionCostValidator.CanAfford(
                vitals.currentStamina, vitals.currentMp,
                staminaCost, mpCost))
            {
                return false;
            }

            ActionCostValidator.DeductCost(
                ref vitals.currentStamina, ref vitals.currentMp,
                staminaCost, mpCost);
            return true;
        }

        private void RegisterHandlers()
        {
            _executor.Register(new AttackActionHandler());
            _executor.Register(new CastActionHandler());
            _executor.Register(new InstantActionHandler());
            _executor.Register(new SustainedActionHandler());
            _executor.Register(new BroadcastActionHandler());
        }

        private void HandlePhaseChanged(AnimationPhase phase)
        {
            ActionPhaseCoordinator.HitboxCommand cmd = _phaseCoordinator.OnPhaseChanged(phase);

            switch (cmd)
            {
                case ActionPhaseCoordinator.HitboxCommand.Activate:
                    ActivateHitbox();
                    break;
                case ActionPhaseCoordinator.HitboxCommand.Deactivate:
                    DeactivateHitbox();
                    break;
            }
        }

        private void ActivateHitbox()
        {
            if (_currentAttackInfo == null)
            {
                return;
            }

            int ownerHash = _character.ObjectHash;

            AttackMotionData motionData = CombatDataHelper.BuildMotionData(_currentAttackInfo);

            if (_hitBox != null)
            {
                _hitBox.Activate(motionData, ownerHash);
            }
            else if (_damageDealer != null)
            {
                _damageDealer.Activate(motionData, ownerHash);
            }
        }

        private void DeactivateHitbox()
        {
            if (_hitBox != null)
            {
                _hitBox.Deactivate();
            }
            if (_damageDealer != null)
            {
                _damageDealer.Deactivate();
            }
        }

        /// <summary>
        /// アニメーションフェーズ完了で呼ばれる。
        /// HandleActionCompleted との二重呼び出しを避けるため、一時的にイベント購読を外す。
        /// </summary>
        private void CompleteCurrentAction()
        {
            if (_currentAttackInfo == null)
            {
                return;
            }

            _executor.OnActionCompleted -= HandleActionCompleted;

            try
            {
                ActionBase current = _executor.CurrentAction;
                if (current != null && current.IsExecuting)
                {
                    current.ForceComplete();
                }

                DeactivateHitbox();
                ClearActionState();
            }
            finally
            {
                _executor.OnActionCompleted += HandleActionCompleted;
            }
        }

        private void HandleActionCompleted()
        {
            DeactivateHitbox();
            ClearActionState();
        }

        private void ClearActionState()
        {
            _currentAttackInfo = null;
            _phaseCoordinator.EndAction();

            if (_damageReceiver != null)
            {
                _damageReceiver.ClearActionEffects();
            }

            // 衝突モードをすり抜けに戻す
            if (_collisionController != null)
            {
                _collisionController.ClearCollisionMode();
            }
        }
    }
}
