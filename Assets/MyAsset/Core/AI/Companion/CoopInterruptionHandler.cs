namespace Game.Core
{
    public class CoopInterruptionHandler
    {
        private ActionInterruptHandler _interruptHandler;
        private bool _isInterrupted;

        public bool IsInterrupted => _isInterrupted;

        public CoopInterruptionHandler()
        {
            _interruptHandler = new ActionInterruptHandler();
        }

        public bool InterruptForCoop(ActionSlot currentSlot, int currentTargetHash)
        {
            if (_isInterrupted)
            {
                return false;
            }

            _interruptHandler.Save(currentSlot, currentTargetHash);
            _isInterrupted = true;
            return true;
        }

        public (ActionSlot slot, int targetHash)? ResumeFromCoop()
        {
            if (!_isInterrupted)
            {
                return null;
            }

            _isInterrupted = false;
            return _interruptHandler.Restore();
        }

        public void ForceResume()
        {
            _isInterrupted = false;
            _interruptHandler.Clear();
        }
    }
}
