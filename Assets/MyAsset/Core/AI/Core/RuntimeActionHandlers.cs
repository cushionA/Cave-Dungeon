namespace Game.Core
{
    /// <summary>
    /// Runtime用AttackActionHandler。
    /// アニメーションフェーズ完了時に外部からForceCompleteを呼べるようにする。
    /// </summary>
    public class RuntimeAttackHandler : AttackActionHandler
    {
        public void ForceComplete()
        {
            Complete();
        }
    }

    /// <summary>
    /// Runtime用CastActionHandler。
    /// 詠唱完了・飛翔体発射後に外部からForceCompleteを呼べるようにする。
    /// </summary>
    public class RuntimeCastHandler : CastActionHandler
    {
        public void ForceComplete()
        {
            Complete();
        }
    }

    /// <summary>
    /// Runtime用SustainedActionHandler。
    /// 外部から強制完了を呼べるようにする（ガード解除等）。
    /// Tick完了による自動終了は基底クラスが処理する。
    /// </summary>
    public class RuntimeSustainedHandler : SustainedActionHandler
    {
        public void ForceComplete()
        {
            Complete();
        }
    }
}
