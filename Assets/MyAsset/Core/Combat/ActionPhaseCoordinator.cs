namespace Game.Core
{
    /// <summary>
    /// アニメーションフェーズに基づくヒットボックス制御の判定ロジック。
    /// MonoBehaviour非依存。ActionExecutorControllerが使用する。
    /// </summary>
    public class ActionPhaseCoordinator
    {
        public enum HitboxCommand : byte
        {
            None,
            Activate,
            Deactivate
        }

        private bool _isActionInProgress;
        private bool _shouldCompleteAction;

        public bool IsActionInProgress => _isActionInProgress;
        public bool ShouldCompleteAction => _shouldCompleteAction;

        public void BeginAction()
        {
            _isActionInProgress = true;
            _shouldCompleteAction = false;
        }

        public void EndAction()
        {
            _isActionInProgress = false;
            _shouldCompleteAction = false;
        }

        /// <summary>
        /// AnimationBridge.OnPhaseChanged から呼ばれる。
        /// フェーズに応じたヒットボックス操作コマンドを返す。
        /// </summary>
        public HitboxCommand OnPhaseChanged(AnimationPhase phase)
        {
            if (!_isActionInProgress)
            {
                return HitboxCommand.None;
            }

            switch (phase)
            {
                case AnimationPhase.Active:
                    return HitboxCommand.Activate;

                case AnimationPhase.Recovery:
                    return HitboxCommand.Deactivate;

                case AnimationPhase.Neutral:
                    _shouldCompleteAction = true;
                    return HitboxCommand.Deactivate;

                default:
                    return HitboxCommand.None;
            }
        }
    }
}
