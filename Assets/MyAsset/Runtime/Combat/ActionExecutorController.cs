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

        private ActionExecutor _executor;
        private ActionPhaseCoordinator _phaseCoordinator;

        // Runtime ハンドラ（ForceComplete可能）
        private RuntimeAttackHandler _attackHandler;
        private RuntimeCastHandler _castHandler;
        private RuntimeSustainedHandler _sustainedHandler;

        // 現在実行中の行動データ
        private AttackInfo _currentAttackInfo;
        private ActionSlot _currentSlot;

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

            _executor = new ActionExecutor();
            _phaseCoordinator = new ActionPhaseCoordinator();

            RegisterHandlers();
        }

        private void OnEnable()
        {
            if (_animController != null && _animController.Bridge != null)
            {
                _animController.Bridge.OnPhaseChanged += HandlePhaseChanged;
            }
            _executor.OnActionCompleted += HandleActionCompleted;
        }

        private void OnDisable()
        {
            if (_animController != null && _animController.Bridge != null)
            {
                _animController.Bridge.OnPhaseChanged -= HandlePhaseChanged;
            }
            _executor.OnActionCompleted -= HandleActionCompleted;
        }

        private void Update()
        {
            _executor.Tick(Time.deltaTime);

            // アニメーションフェーズがNeutralに到達したら行動を完了
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

            int ownerHash = _character.ObjectHash;

            // コスト検証（Attack / Cast の場合）
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
                _currentSlot = slot;

                // ActionEffect を DamageReceiver に設定
                if (_damageReceiver != null && info.motionInfo.actionEffects != null
                    && info.motionInfo.actionEffects.Length > 0)
                {
                    _damageReceiver.SetActionEffects(info.motionInfo.actionEffects);
                }

                // アニメーション開始
                if (_animController != null)
                {
                    _animController.StartActionPhase(info.motionInfo, (byte)slot.paramId);
                }

                _phaseCoordinator.BeginAction();
            }
            else
            {
                _currentAttackInfo = null;
                _currentSlot = slot;
            }

            // Core ActionExecutor で行動実行
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

        /// <summary>
        /// AttackInfo配列からparamIdで行動データを解決する。
        /// </summary>
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
            _attackHandler = new RuntimeAttackHandler();
            _castHandler = new RuntimeCastHandler();
            _sustainedHandler = new RuntimeSustainedHandler();

            _executor.Register(_attackHandler);
            _executor.Register(_castHandler);
            _executor.Register(new InstantActionHandler());
            _executor.Register(_sustainedHandler);
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

            AttackMotionData motionData = new AttackMotionData
            {
                actionName = _currentAttackInfo.attackName,
                motionValue = _currentAttackInfo.damageMultiplier,
                attackElement = _currentAttackInfo.attackElement,
                knockbackForce = _currentAttackInfo.knockbackInfo.hasKnockback
                    ? _currentAttackInfo.knockbackInfo.force
                    : Vector2.zero,
                armorBreakValue = _currentAttackInfo.armorBreakValue,
                maxHitCount = 1,
                staminaCost = _currentAttackInfo.staminaCost,
                mpCost = _currentAttackInfo.mpCost,
                statusEffect = _currentAttackInfo.statusEffectInfo,
                attackMoveDistance = _currentAttackInfo.attackMoveDistance,
                attackMoveDuration = _currentAttackInfo.attackMoveDuration
            };

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

        private void CompleteCurrentAction()
        {
            // フェーズ完了によりCore ActionBase を完了させる
            if (_currentSlot.execType == ActionExecType.Attack && _attackHandler.IsExecuting)
            {
                _attackHandler.ForceComplete();
            }
            else if (_currentSlot.execType == ActionExecType.Cast && _castHandler.IsExecuting)
            {
                _castHandler.ForceComplete();
            }

            ClearActionState();
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
        }
    }
}
