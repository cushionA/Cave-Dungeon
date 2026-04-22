using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Game.Core;
using Game.Runtime;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// PlayerCharacter.ResetInternalState の各内部フラグが
    /// 「変更 → Reset → 初期値に戻る」ことを 1 テスト 1 フラグ単位で直接検証する。
    ///
    /// ResetInternalState は AutoInputTester から呼ばれる開発用状態クリア API。
    /// 本テストは UNITY_EDITOR / DEVELOPMENT_BUILD でのみ有効な API であるため
    /// EditMode (UNITY_EDITOR) 前提でリフレクション経由で private フィールドを操作する。
    /// </summary>
    [TestFixture]
    public class PlayerCharacterResetInternalStateTests
    {
        private GameObject _go;
        private PlayerCharacter _player;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("PlayerCharacterResetTest");
            // [RequireComponent] により Rigidbody2D / BoxCollider2D は AddComponent 時に自動追加される
            _player = _go.AddComponent<PlayerCharacter>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
            {
                Object.DestroyImmediate(_go);
            }
        }

        // =========================================================================
        // 各フラグ: 変更 → Reset → 初期値復帰 を 1 テストずつ検証
        // =========================================================================

        [Test]
        public void PlayerCharacter_ResetInternalState_WhenIsDivingAttackIsTrue_ResetsToFalse()
        {
            SetPrivate(_player, "_isDivingAttack", true);
            _player.ResetInternalState();
            Assert.IsFalse(GetPrivate<bool>(_player, "_isDivingAttack"));
        }

        [Test]
        public void PlayerCharacter_ResetInternalState_WhenIsDivingLandingIsTrue_ResetsToFalse()
        {
            SetPrivate(_player, "_isDivingLanding", true);
            _player.ResetInternalState();
            Assert.IsFalse(GetPrivate<bool>(_player, "_isDivingLanding"));
        }

        [Test]
        public void PlayerCharacter_ResetInternalState_WhenDivingLandingTimerNonZero_ResetsToZero()
        {
            SetPrivate(_player, "_divingLandingTimer", 0.55f);
            _player.ResetInternalState();
            Assert.AreEqual(0f, GetPrivate<float>(_player, "_divingLandingTimer"));
        }

        [Test]
        public void PlayerCharacter_ResetInternalState_WhenIsAirAttackingIsTrue_ResetsToFalse()
        {
            SetPrivate(_player, "_isAirAttacking", true);
            _player.ResetInternalState();
            Assert.IsFalse(GetPrivate<bool>(_player, "_isAirAttacking"));
        }

        [Test]
        public void PlayerCharacter_ResetInternalState_WhenAerialComboUsedIsTrue_ResetsToFalse()
        {
            SetPrivate(_player, "_aerialComboUsed", true);
            _player.ResetInternalState();
            Assert.IsFalse(GetPrivate<bool>(_player, "_aerialComboUsed"));
        }

        [Test]
        public void PlayerCharacter_ResetInternalState_WhenComboStepNonZero_ResetsToZero()
        {
            SetPrivate(_player, "_comboStep", 3);
            _player.ResetInternalState();
            Assert.AreEqual(0, GetPrivate<int>(_player, "_comboStep"));
        }

        [Test]
        public void PlayerCharacter_ResetInternalState_WhenComboQueuedIsTrue_ResetsToFalse()
        {
            SetPrivate(_player, "_comboQueued", true);
            _player.ResetInternalState();
            Assert.IsFalse(GetPrivate<bool>(_player, "_comboQueued"));
        }

        [Test]
        public void PlayerCharacter_ResetInternalState_WhenComboWindowTimerNonZero_ResetsToZero()
        {
            SetPrivate(_player, "_comboWindowTimer", 0.4f);
            _player.ResetInternalState();
            Assert.AreEqual(0f, GetPrivate<float>(_player, "_comboWindowTimer"));
        }

        [Test]
        public void PlayerCharacter_ResetInternalState_WhenAttackConsumedIsTrue_ResetsToFalse()
        {
            SetPrivate(_player, "_attackConsumed", true);
            _player.ResetInternalState();
            Assert.IsFalse(GetPrivate<bool>(_player, "_attackConsumed"));
        }

        [Test]
        public void PlayerCharacter_ResetInternalState_WhenIsExhaustedIsTrue_ResetsToFalse()
        {
            SetPrivate(_player, "_isExhausted", true);
            _player.ResetInternalState();
            Assert.IsFalse(GetPrivate<bool>(_player, "_isExhausted"));
        }

        [Test]
        public void PlayerCharacter_ResetInternalState_WhenStaminaRecoveryDelayTimerNonZero_ResetsToZero()
        {
            SetPrivate(_player, "_staminaRecoveryDelayTimer", 1.5f);
            _player.ResetInternalState();
            Assert.AreEqual(0f, GetPrivate<float>(_player, "_staminaRecoveryDelayTimer"));
        }

        [Test]
        public void PlayerCharacter_ResetInternalState_WhenIsChargingIsTrue_ResetsToFalse()
        {
            SetPrivate(_player, "_isCharging", true);
            _player.ResetInternalState();
            Assert.IsFalse(GetPrivate<bool>(_player, "_isCharging"));
        }

        [Test]
        public void PlayerCharacter_ResetInternalState_WhenWasDodgingIsTrue_ResetsToFalse()
        {
            SetPrivate(_player, "_wasDodging", true);
            _player.ResetInternalState();
            Assert.IsFalse(GetPrivate<bool>(_player, "_wasDodging"));
        }

        [Test]
        public void PlayerCharacter_ResetInternalState_WhenRigidbodyGravityScaleModified_RestoresToDefault()
        {
            // Awake で k_GravityScale が設定されている想定。別値に変えてから Reset で戻るか確認
            Rigidbody2D rb = _go.GetComponent<Rigidbody2D>();
            Assert.IsNotNull(rb, "Rigidbody2D が [RequireComponent] で自動追加されている");
            rb.gravityScale = 99f;
            _player.ResetInternalState();
            Assert.AreEqual(GameConstants.k_GravityScale, rb.gravityScale);
        }

        // =========================================================================
        // リフレクションヘルパー
        // =========================================================================

        private static void SetPrivate(object target, string fieldName, object value)
        {
            FieldInfo f = target.GetType().GetField(
                fieldName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, $"private field not found: {fieldName}");
            f.SetValue(target, value);
        }

        private static T GetPrivate<T>(object target, string fieldName)
        {
            FieldInfo f = target.GetType().GetField(
                fieldName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, $"private field not found: {fieldName}");
            return (T)f.GetValue(target);
        }
    }
}
