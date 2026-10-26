using System;
using UnityEngine;

namespace Game.Core
{
    /// <summary>入力受付と空中物理が使う、登録時に確定した攻撃設定。</summary>
    public readonly struct AttackExecutionSettings
    {
        public float InputWindow { get; }
        public float AirHangDuration { get; }
        public float AirHangGravityScale { get; }
        public float AerialMoveSpeedDefault { get; }
        public Vector2 AerialMoveDirection { get; }
        public float MovementSpeed { get; }
        public float DivingGravityMultiplier { get; }
        public float DivingForwardSpeed { get; }
        public float DivingLandingRecoveryDuration { get; }
        public AudioClip LandingSound { get; }

        private AttackExecutionSettings(AttackInfo source)
        {
            InputWindow = source.inputWindow;
            AirHangDuration = source.airHangDuration;
            AirHangGravityScale = source.airHangGravityScale;
            AerialMoveSpeedDefault = source.aerialMoveSpeedDefault;
            AerialMoveDirection = source.aerialMoveDirection;
            MovementSpeed = source.movement.speed;
            DivingGravityMultiplier = source.divingGravityMultiplier;
            DivingForwardSpeed = source.divingForwardSpeed;
            DivingLandingRecoveryDuration = source.divingLandingRecoveryDuration;
            LandingSound = source.effectSoundInfo.landingSound;
        }

        public static AttackExecutionSettings Capture(AttackInfo source)
        {
            if (source == null) { throw new ArgumentNullException(nameof(source)); }
            return new AttackExecutionSettings(source);
        }
    }
}
