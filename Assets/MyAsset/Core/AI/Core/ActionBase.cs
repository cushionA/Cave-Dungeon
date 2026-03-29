using System;

namespace Game.Core
{
    /// <summary>
    /// 全実行パターン共通の基底クラス。
    /// ActionExecutorがDictionary&lt;ActionExecType, ActionBase&gt;で保持する。
    /// </summary>
    public abstract class ActionBase
    {
        public abstract ActionExecType ExecType { get; }

        private bool _isExecuting;
        public bool IsExecuting => _isExecuting;

        public event Action OnCompleted;

        /// <summary>行動を開始する</summary>
        public void Execute(int ownerHash, int targetHash, ActionSlot slot)
        {
            _isExecuting = true;
            OnExecute(ownerHash, targetHash, slot);
        }

        /// <summary>行動を中断する</summary>
        public void Cancel()
        {
            if (!_isExecuting)
            {
                return;
            }

            _isExecuting = false;
            OnCancel();
        }

        /// <summary>行動を完了としてマークする</summary>
        protected void Complete()
        {
            _isExecuting = false;
            OnCompleted?.Invoke();
        }

        /// <summary>
        /// 外部から行動を完了させる。
        /// ActionExecutorControllerがアニメーションフェーズ完了時に使用する。
        /// </summary>
        public void ForceComplete()
        {
            if (_isExecuting)
            {
                Complete();
            }
        }

        /// <summary>派生クラスで実行処理を実装する</summary>
        protected abstract void OnExecute(int ownerHash, int targetHash, ActionSlot slot);

        /// <summary>派生クラスでキャンセル処理を実装する</summary>
        protected virtual void OnCancel() { }

        /// <summary>毎フレーム更新。Sustainedで使用。</summary>
        public virtual void Tick(float deltaTime) { }
    }

    /// <summary>
    /// 近接攻撃系。AttackMotionData参照、ヒットボックス・motionValue・コンボ管理。
    /// paramIdでAttackMotionDataインデックスを引く。
    /// </summary>
    public class AttackActionHandler : ActionBase
    {
        public override ActionExecType ExecType => ActionExecType.Attack;

        public int LastOwnerHash { get; private set; }
        public int LastTargetHash { get; private set; }
        public int LastParamId { get; private set; }

        protected override void OnExecute(int ownerHash, int targetHash, ActionSlot slot)
        {
            LastOwnerHash = ownerHash;
            LastTargetHash = targetHash;
            LastParamId = slot.paramId;
        }
    }

    /// <summary>
    /// 詠唱→発動→ProjectileSystem。paramIdでMagicDefinition IDを引く。
    /// </summary>
    public class CastActionHandler : ActionBase
    {
        public override ActionExecType ExecType => ActionExecType.Cast;

        public int LastOwnerHash { get; private set; }
        public int LastTargetHash { get; private set; }
        public int LastParamId { get; private set; }

        protected override void OnExecute(int ownerHash, int targetHash, ActionSlot slot)
        {
            LastOwnerHash = ownerHash;
            LastTargetHash = targetHash;
            LastParamId = slot.paramId;
        }
    }

    /// <summary>
    /// アニメ1回再生して即完了。ワープ、回避、アイテム使用、環境物利用等。
    /// paramIdでInstantAction enum値を引く。
    /// </summary>
    public class InstantActionHandler : ActionBase
    {
        public override ActionExecType ExecType => ActionExecType.Instant;

        public int LastOwnerHash { get; private set; }
        public int LastParamId { get; private set; }

        protected override void OnExecute(int ownerHash, int targetHash, ActionSlot slot)
        {
            LastOwnerHash = ownerHash;
            LastParamId = slot.paramId;
            Complete();
        }
    }

    /// <summary>
    /// 開始→毎フレームTick→終了条件で終了。
    /// 移動、ガード、追従、挟撃維持、盾展開、隠密等。
    /// paramIdでSustainedAction enum値を引く。
    /// paramValueで持続時間を指定（0=無期限）。
    /// </summary>
    public class SustainedActionHandler : ActionBase
    {
        public override ActionExecType ExecType => ActionExecType.Sustained;

        public int LastOwnerHash { get; private set; }
        public int LastTargetHash { get; private set; }
        public int LastParamId { get; private set; }

        private float _duration;
        private float _elapsed;

        protected override void OnExecute(int ownerHash, int targetHash, ActionSlot slot)
        {
            LastOwnerHash = ownerHash;
            LastTargetHash = targetHash;
            LastParamId = slot.paramId;
            _duration = slot.paramValue;
            _elapsed = 0f;
        }

        public override void Tick(float deltaTime)
        {
            if (!IsExecuting)
            {
                return;
            }

            _elapsed += deltaTime;
            if (_duration > 0f && _elapsed >= _duration)
            {
                Complete();
            }
        }
    }

    /// <summary>
    /// 自分は何もしない。他キャラのAI状態を操作。
    /// ターゲット指示、集合、散開、挑発等。
    /// paramIdでBroadcastAction enum値を引く。
    /// </summary>
    public class BroadcastActionHandler : ActionBase
    {
        public override ActionExecType ExecType => ActionExecType.Broadcast;

        public int LastOwnerHash { get; private set; }
        public int LastParamId { get; private set; }

        protected override void OnExecute(int ownerHash, int targetHash, ActionSlot slot)
        {
            LastOwnerHash = ownerHash;
            LastParamId = slot.paramId;
            Complete();
        }
    }
}
