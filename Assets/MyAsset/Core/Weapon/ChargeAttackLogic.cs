namespace Game.Core
{
    /// <summary>
    /// チャージ攻撃のロジック。
    /// チャージ時間に応じて段階(0/1/2)を判定し、段階に応じた攻撃力倍率を返す。
    /// </summary>
    public class ChargeAttackLogic
    {
        public const float k_ChargeLevel1Time = 0.5f;
        public const float k_ChargeLevel2Time = 1.5f;
        public const float k_ChargeLevel1Multiplier = 1.5f;
        public const float k_ChargeLevel2Multiplier = 2.5f;

        private float _chargeTimer;
        private bool _isCharging;

        public bool IsCharging => _isCharging;
        public float ChargeTime => _chargeTimer;

        /// <summary>チャージ開始</summary>
        public void StartCharge()
        {
            _isCharging = true;
            _chargeTimer = 0f;
        }

        /// <summary>チャージ更新。deltaTime加算。</summary>
        public void UpdateCharge(float deltaTime)
        {
            if (!_isCharging)
            {
                return;
            }

            _chargeTimer += deltaTime;
        }

        /// <summary>チャージ解放。チャージ段階に応じた倍率を返す。未チャージ→1.0。</summary>
        public float ReleaseCharge()
        {
            int level = GetChargeLevel();
            _isCharging = false;
            _chargeTimer = 0f;

            switch (level)
            {
                case 1:
                    return k_ChargeLevel1Multiplier;
                case 2:
                    return k_ChargeLevel2Multiplier;
                default:
                    return 1.0f;
            }
        }

        /// <summary>チャージキャンセル。倍率1.0で終了。</summary>
        public void CancelCharge()
        {
            _isCharging = false;
            _chargeTimer = 0f;
        }

        /// <summary>チャージ段階判定。0=なし、1=Lv1、2=Lv2。</summary>
        public int GetChargeLevel()
        {
            if (_chargeTimer >= k_ChargeLevel2Time)
            {
                return 2;
            }

            if (_chargeTimer >= k_ChargeLevel1Time)
            {
                return 1;
            }

            return 0;
        }
    }
}
