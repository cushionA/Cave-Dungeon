namespace Game.Core
{
    /// <summary>
    /// コンボチェーン管理。入力からコンボステップ遷移、入力ウィンドウ管理、チェーンリセットを担当。
    /// </summary>
    public class ComboManager
    {
        private int _currentStep;
        private int _maxSteps;
        private float _inputWindowTimer;
        private bool _inInputWindow;
        private bool _chainQueued;

        public int CurrentStep => _currentStep;
        public int MaxSteps => _maxSteps;
        public bool InInputWindow => _inInputWindow;

        public ComboManager(int maxSteps)
        {
            _maxSteps = maxSteps;
            _currentStep = 0;
            _inputWindowTimer = 0f;
            _inInputWindow = false;
            _chainQueued = false;
        }

        /// <summary>
        /// コンボの次ステップへ進む。maxSteps到達で進まない。
        /// </summary>
        /// <returns>ステップが進んだ場合true。</returns>
        public bool Advance()
        {
            if (_currentStep >= _maxSteps)
            {
                return false;
            }

            _currentStep++;
            return true;
        }

        /// <summary>
        /// 入力ウィンドウを開く。duration秒間入力受付。
        /// </summary>
        /// <param name="duration">入力受付時間（秒）。</param>
        public void OpenInputWindow(float duration)
        {
            _inputWindowTimer = duration;
            _inInputWindow = true;
            _chainQueued = false;
        }

        /// <summary>
        /// 時間経過。入力ウィンドウの残り時間を減らす。期限切れでコンボリセット。
        /// </summary>
        /// <param name="deltaTime">経過時間（秒）。</param>
        public void Tick(float deltaTime)
        {
            if (!_inInputWindow)
            {
                return;
            }

            _inputWindowTimer -= deltaTime;

            if (_inputWindowTimer <= 0f)
            {
                Reset();
            }
        }

        /// <summary>
        /// コンボを最初にリセット。
        /// </summary>
        public void Reset()
        {
            _currentStep = 0;
            _inputWindowTimer = 0f;
            _inInputWindow = false;
            _chainQueued = false;
        }

        /// <summary>
        /// 入力キュー。ウィンドウ中に入力があればキューする。
        /// </summary>
        public void QueueChain()
        {
            if (_inInputWindow)
            {
                _chainQueued = true;
            }
        }

        /// <summary>
        /// キューされた入力を消費してAdvance。
        /// </summary>
        /// <returns>Advanceが実行された場合true。</returns>
        public bool TryConsumeChain()
        {
            if (!_chainQueued)
            {
                return false;
            }

            _chainQueued = false;
            return Advance();
        }
    }
}
