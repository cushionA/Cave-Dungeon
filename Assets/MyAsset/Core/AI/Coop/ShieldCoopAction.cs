using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// Coop action that deploys a protective shield around the companion.
    /// The shield persists for a set duration and can reflect projectiles.
    /// </summary>
    public class ShieldCoopAction : CoopActionBase
    {
        public override string ActionName => "Shield";
        public override int MpCost => 20;
        public override float CooldownDuration => 18f;
        public override int MaxComboCount => 1;
        public override float ComboInputWindow => 0.5f;

        private SoACharaDataDic _data;
        private float _shieldDuration;
        private float _shieldTimer;
        private bool _shieldActive;
        private Vector2 _shieldPosition;
        private int _companionHash;

        public bool IsShieldActive => _shieldActive;
        public Vector2 ShieldPosition => _shieldPosition;
        public float ShieldDuration => _shieldDuration;

        public ShieldCoopAction(float shieldDuration = 5f)
            : this(null, shieldDuration)
        {
        }

        public ShieldCoopAction(SoACharaDataDic data, float shieldDuration = 5f)
        {
            _data = data;
            _shieldDuration = shieldDuration;
        }

        public override void ExecuteCombo(int comboIndex, int companionHash, int targetHash)
        {
            _shieldActive = true;
            _shieldTimer = _shieldDuration;
            _companionHash = companionHash;

            // コンパニオンの現在位置にシールドを展開
            if (_data != null && _data.TryGetValue(companionHash, out int _))
            {
                _shieldPosition = _data.GetVitals(companionHash).position;
            }
        }

        /// <summary>
        /// Updates the shield timer. Deactivates the shield when duration expires.
        /// </summary>
        public void TickShield(float deltaTime)
        {
            if (!_shieldActive)
            {
                return;
            }

            _shieldTimer -= deltaTime;
            if (_shieldTimer <= 0f)
            {
                _shieldActive = false;
                return;
            }

            // シールド位置をコンパニオンに追従
            if (_data != null && _data.TryGetValue(_companionHash, out int _))
            {
                _shieldPosition = _data.GetVitals(_companionHash).position;
            }
        }

        /// <summary>
        /// Checks whether a projectile at the given position is within reflect range.
        /// Returns true if the shield is active and the projectile is within radius.
        /// </summary>
        public bool CheckReflect(Vector2 projectilePos, float shieldRadius)
        {
            if (!_shieldActive)
            {
                return false;
            }

            return Vector2.Distance(projectilePos, _shieldPosition) <= shieldRadius;
        }

        public override void OnComboEnd(int companionHash)
        {
            // Shield stays active after combo ends, managed by TickShield
        }
    }
}
