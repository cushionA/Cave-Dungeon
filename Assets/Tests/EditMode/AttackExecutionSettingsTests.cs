using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public class AttackExecutionSettingsTests
    {
        [Test]
        public void AttackSettings_Capture_SourceChangesDoNotChangeExecutionSettings()
        {
            AttackInfo source = ScriptableObject.CreateInstance<AttackInfo>();
            AudioClip landingSound = AudioClip.Create("CapturedLanding", 1, 1, 44100, false);
            try
            {
                source.inputWindow = 0.8f;
                source.airHangDuration = 0.4f;
                source.airHangGravityScale = 0.15f;
                source.aerialMoveSpeedDefault = 4f;
                source.aerialMoveDirection = new Vector2(0.5f, 1f);
                source.movement = new ActionMovementProfile { speed = 6f };
                source.divingGravityMultiplier = 4f;
                source.divingForwardSpeed = 2f;
                source.divingLandingRecoveryDuration = 0.5f;
                source.effectSoundInfo = new EffectSoundInfo { landingSound = landingSound };

                AttackExecutionSettings settings = AttackExecutionSettings.Capture(source);
                source.inputWindow = 9f;
                source.airHangDuration = 9f;
                source.airHangGravityScale = 9f;
                source.aerialMoveSpeedDefault = 9f;
                source.aerialMoveDirection = Vector2.left;
                source.movement = default;
                source.divingGravityMultiplier = 9f;
                source.divingForwardSpeed = 9f;
                source.divingLandingRecoveryDuration = 9f;
                source.effectSoundInfo = default;

                Assert.AreEqual(0.8f, settings.InputWindow);
                Assert.AreEqual(0.4f, settings.AirHangDuration);
                Assert.AreEqual(0.15f, settings.AirHangGravityScale);
                Assert.AreEqual(4f, settings.AerialMoveSpeedDefault);
                Assert.AreEqual(new Vector2(0.5f, 1f), settings.AerialMoveDirection);
                Assert.AreEqual(6f, settings.MovementSpeed);
                Assert.AreEqual(4f, settings.DivingGravityMultiplier);
                Assert.AreEqual(2f, settings.DivingForwardSpeed);
                Assert.AreEqual(0.5f, settings.DivingLandingRecoveryDuration);
                Assert.AreSame(landingSound, settings.LandingSound);
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(landingSound);
            }
        }

        [Test]
        public void AttackSettings_Recapture_DoesNotModifyEarlierSnapshot()
        {
            AttackInfo source = ScriptableObject.CreateInstance<AttackInfo>();
            try
            {
                source.inputWindow = 0.2f;
                AttackExecutionSettings first = AttackExecutionSettings.Capture(source);
                source.inputWindow = 1.2f;
                AttackExecutionSettings second = AttackExecutionSettings.Capture(source);

                Assert.AreEqual(0.2f, first.InputWindow);
                Assert.AreEqual(1.2f, second.InputWindow);
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void AttackSettings_DestroyAuthoringAsset_PreservesCapturedValues()
        {
            AttackInfo source = ScriptableObject.CreateInstance<AttackInfo>();
            source.airHangDuration = 0.7f;
            source.divingForwardSpeed = 2f;
            AttackExecutionSettings settings = AttackExecutionSettings.Capture(source);
            Object.DestroyImmediate(source);

            Assert.AreEqual(0.7f, settings.AirHangDuration);
            Assert.AreEqual(2f, settings.DivingForwardSpeed);
        }
    }
}
