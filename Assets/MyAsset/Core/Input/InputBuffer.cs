namespace Game.Core
{
    /// <summary>
    /// 先行入力バッファ。有効時間内の入力を保持し、消費可能にする。
    /// </summary>
    public class InputBuffer
    {
        private AttackInputType? _bufferedInput;
        private float _bufferTime;
        private readonly float _maxBufferTime;

        public InputBuffer(float maxBufferTime)
        {
            _maxBufferTime = maxBufferTime;
            _bufferedInput = null;
            _bufferTime = 0f;
        }

        /// <summary>
        /// バッファに有効な入力があるかどうか。
        /// </summary>
        public bool HasInput
        {
            get { return _bufferedInput.HasValue && _bufferTime > 0f; }
        }

        /// <summary>
        /// 入力をバッファに保持する。有効時間をリセットする。
        /// </summary>
        public void Buffer(AttackInputType input)
        {
            _bufferedInput = input;
            _bufferTime = _maxBufferTime;
        }

        /// <summary>
        /// バッファから入力を消費して取得する。
        /// 有効な入力がある場合はtrueを返し、inputに値を設定する。
        /// </summary>
        public bool TryConsume(out AttackInputType input)
        {
            if (HasInput)
            {
                input = _bufferedInput.Value;
                _bufferedInput = null;
                _bufferTime = 0f;
                return true;
            }

            input = default;
            return false;
        }

        /// <summary>
        /// 時間経過処理。deltaTime分だけ残り有効時間を減少させる。
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (_bufferTime > 0f)
            {
                _bufferTime -= deltaTime;
            }
        }

        /// <summary>
        /// バッファをクリアする。
        /// </summary>
        public void Clear()
        {
            _bufferedInput = null;
            _bufferTime = 0f;
        }
    }
}
