namespace Game.Core
{
    /// <summary>
    /// ボタンホールド検出とChargeAttackLogic連携を管理する純C#クラス。
    /// PlayerInputHandlerから委譲される。
    /// </summary>
    public class ChargeInputHandler
    {
        private readonly ChargeAttackLogic _chargeLogic;
        private int _holdingButtonId = -1;
        private bool _isCharging;
        private float _chargeMultiplier = 1f;
        private bool _hasAttackInput;
        private int _attackButtonId = -1;

        public bool IsHolding => _holdingButtonId >= 0;
        public bool IsCharging => _isCharging;
        public float ChargeMultiplier => _chargeMultiplier;
        public bool HasAttackInput => _hasAttackInput;
        public int AttackButtonId => _attackButtonId;
        public int ChargeLevel => _chargeLogic.GetChargeLevel();

        public ChargeInputHandler(ChargeAttackLogic chargeLogic)
        {
            _chargeLogic = chargeLogic;
        }

        /// <summary>ボタン押下開始。チャージ計測を開始する。</summary>
        public void BeginHold(int buttonId)
        {
            // 別のボタンをホールド中なら先にキャンセル
            if (_holdingButtonId >= 0 && _holdingButtonId != buttonId)
            {
                _chargeLogic.CancelCharge();
            }

            _holdingButtonId = buttonId;
            _chargeLogic.StartCharge();
        }

        /// <summary>ボタンリリース。チャージレベルに応じて通常/チャージ攻撃を判定する。</summary>
        public void EndHold(int buttonId)
        {
            if (_holdingButtonId != buttonId)
            {
                return;
            }

            if (_chargeLogic.GetChargeLevel() >= 1)
            {
                _chargeMultiplier = _chargeLogic.ReleaseCharge();
                _isCharging = true;
            }
            else
            {
                _chargeLogic.CancelCharge();
                _isCharging = false;
                _chargeMultiplier = 1f;
            }

            _attackButtonId = buttonId;
            _hasAttackInput = true;
            _holdingButtonId = -1;
        }

        /// <summary>毎フレーム呼び出し。ホールド中のチャージ時間を加算する。</summary>
        public void UpdateHold(float deltaTime)
        {
            if (_holdingButtonId < 0)
            {
                return;
            }

            _chargeLogic.UpdateCharge(deltaTime);
        }

        /// <summary>チャージを強制キャンセルする（被ダメージ、ガード入力等）。</summary>
        public void CancelCharge()
        {
            _chargeLogic.CancelCharge();
            _holdingButtonId = -1;
            _isCharging = false;
            _chargeMultiplier = 1f;
            _hasAttackInput = false;
            _attackButtonId = -1;
        }

        /// <summary>LateUpdate相当。消費済みの攻撃入力をリセットする。</summary>
        public void ConsumeAttack()
        {
            if (!_hasAttackInput)
            {
                return;
            }

            _hasAttackInput = false;
            _isCharging = false;
            _chargeMultiplier = 1f;
            _attackButtonId = -1;
        }
    }
}
