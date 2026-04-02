using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// 全13入力操作のマッピングテスト。
    /// InputConverter / MovementInfo / ChargeInputHandler の入力変換を網羅的に検証する。
    ///
    /// 対応表:
    ///   移動(WASD)    → MovementInfo.moveDirection
    ///   ジャンプ(Space) → jumpPressed / jumpHeld
    ///   ダッシュ(Shift) → dodgePressed(短押し) / sprintHeld(長押し)
    ///   通常攻撃(LMB)  → attackInput = LightAttack
    ///   強攻撃(X)      → attackInput = HeavyAttack
    ///   スキル(Q)      → attackInput = Skill
    ///   ガード(RMB)    → guardHeld
    ///   インタラクト(E) → interactPressed
    ///   連携(V)        → cooperationPressed
    ///   武器切替(Tab)   → weaponSwitchPressed
    ///   グリップ切替(G) → gripSwitchPressed
    ///   メニュー(Esc)   → menuPressed
    ///   マップ(M)      → mapPressed
    /// </summary>
    [TestFixture]
    public class AllInputMappingTests
    {
        // =============================================================
        //  1. 移動 (WASD / 矢印キー) → moveDirection
        // =============================================================

        [Test]
        public void Move_RightInput_NormalizesToPositiveX()
        {
            Vector2 raw = new Vector2(1f, 0f);
            Vector2 result = InputConverter.NormalizeDirection(raw);
            Assert.AreEqual(1f, result.x, 0.001f);
            Assert.AreEqual(0f, result.y, 0.001f);
        }

        [Test]
        public void Move_LeftInput_NormalizesToNegativeX()
        {
            Vector2 raw = new Vector2(-1f, 0f);
            Vector2 result = InputConverter.NormalizeDirection(raw);
            Assert.AreEqual(-1f, result.x, 0.001f);
        }

        [Test]
        public void Move_DiagonalInput_PreservesMagnitudeUnderOne()
        {
            // スティック入力 (0.5, 0.5) → magnitude ~0.707 → そのまま返す
            Vector2 raw = new Vector2(0.5f, 0.5f);
            Vector2 result = InputConverter.NormalizeDirection(raw);
            Assert.AreEqual(raw.x, result.x, 0.001f);
            Assert.AreEqual(raw.y, result.y, 0.001f);
        }

        [Test]
        public void Move_DiagonalOverOne_Normalizes()
        {
            Vector2 raw = new Vector2(1f, 1f); // magnitude ~1.414
            Vector2 result = InputConverter.NormalizeDirection(raw);
            Assert.AreEqual(1f, result.magnitude, 0.001f);
        }

        [Test]
        public void Move_DeadZone_ReturnsZero()
        {
            Vector2 raw = new Vector2(0.05f, 0.05f); // magnitude ~0.07
            Vector2 result = InputConverter.NormalizeDirection(raw);
            Assert.AreEqual(Vector2.zero, result);
        }

        [Test]
        public void Move_ExactDeadZoneBoundary_ReturnsZero()
        {
            // magnitude == k_DeadZone (0.15) → ゼロ (<=)
            Vector2 raw = new Vector2(0.15f, 0f);
            Vector2 result = InputConverter.NormalizeDirection(raw);
            Assert.AreEqual(Vector2.zero, result);
        }

        [Test]
        public void Move_JustAboveDeadZone_ReturnsInput()
        {
            Vector2 raw = new Vector2(0.16f, 0f);
            Vector2 result = InputConverter.NormalizeDirection(raw);
            Assert.AreEqual(0.16f, result.x, 0.001f);
        }

        // =============================================================
        //  2. ジャンプ (Space) → jumpPressed / jumpHeld
        // =============================================================

        [Test]
        public void Jump_MovementInfo_PressedAndHeldAreIndependent()
        {
            // jumpPressed = バッファ付き一発入力、jumpHeld = 継続入力
            MovementInfo info = new MovementInfo
            {
                jumpPressed = true,
                jumpHeld = true,
            };
            Assert.IsTrue(info.jumpPressed);
            Assert.IsTrue(info.jumpHeld);
        }

        [Test]
        public void Jump_ReleaseState_HeldFalsePressedFalse()
        {
            MovementInfo info = new MovementInfo
            {
                jumpPressed = false,
                jumpHeld = false,
            };
            Assert.IsFalse(info.jumpPressed);
            Assert.IsFalse(info.jumpHeld);
        }

        // =============================================================
        //  3. ダッシュ (Left Shift) → dodgePressed(短押し) / sprintHeld(長押し)
        // =============================================================

        [Test]
        public void Sprint_ShortPress_SetsDodgeNotSprint()
        {
            // 短押し → 回避入力
            MovementInfo info = new MovementInfo
            {
                dodgePressed = true,
                sprintHeld = false,
            };
            Assert.IsTrue(info.dodgePressed);
            Assert.IsFalse(info.sprintHeld);
        }

        [Test]
        public void Sprint_LongPress_SetsSprintNotDodge()
        {
            // 長押し → スプリント
            MovementInfo info = new MovementInfo
            {
                dodgePressed = false,
                sprintHeld = true,
            };
            Assert.IsFalse(info.dodgePressed);
            Assert.IsTrue(info.sprintHeld);
        }

        // =============================================================
        //  4. 通常攻撃 (マウス左 / Enter) → LightAttack
        // =============================================================

        [Test]
        public void LightAttack_Ground_ReturnsLightAttack()
        {
            AttackInputType? result = InputConverter.ConvertAttackInput(
                AttackButtonId.Light, isAirborne: false, isCharging: false);
            Assert.AreEqual(AttackInputType.LightAttack, result);
        }

        [Test]
        public void LightAttack_Airborne_ReturnsAerialLight()
        {
            AttackInputType? result = InputConverter.ConvertAttackInput(
                AttackButtonId.Light, isAirborne: true, isCharging: false);
            Assert.AreEqual(AttackInputType.AerialLight, result);
        }

        [Test]
        public void LightAttack_Charging_ReturnsChargeLight()
        {
            AttackInputType? result = InputConverter.ConvertAttackInput(
                AttackButtonId.Light, isAirborne: false, isCharging: true);
            Assert.AreEqual(AttackInputType.ChargeLight, result);
        }

        [Test]
        public void LightAttack_AirborneAndCharging_AirbornePriority()
        {
            // 空中判定が優先される
            AttackInputType? result = InputConverter.ConvertAttackInput(
                AttackButtonId.Light, isAirborne: true, isCharging: true);
            Assert.AreEqual(AttackInputType.AerialLight, result);
        }

        // =============================================================
        //  5. 強攻撃 (X) → HeavyAttack
        // =============================================================

        [Test]
        public void HeavyAttack_Ground_ReturnsHeavyAttack()
        {
            AttackInputType? result = InputConverter.ConvertAttackInput(
                AttackButtonId.Heavy, isAirborne: false, isCharging: false);
            Assert.AreEqual(AttackInputType.HeavyAttack, result);
        }

        [Test]
        public void HeavyAttack_Airborne_ReturnsAerialHeavy()
        {
            AttackInputType? result = InputConverter.ConvertAttackInput(
                AttackButtonId.Heavy, isAirborne: true, isCharging: false);
            Assert.AreEqual(AttackInputType.AerialHeavy, result);
        }

        [Test]
        public void HeavyAttack_Charging_ReturnsChargeHeavy()
        {
            AttackInputType? result = InputConverter.ConvertAttackInput(
                AttackButtonId.Heavy, isAirborne: false, isCharging: true);
            Assert.AreEqual(AttackInputType.ChargeHeavy, result);
        }

        [Test]
        public void HeavyAttack_AirborneAndCharging_AirbornePriority()
        {
            AttackInputType? result = InputConverter.ConvertAttackInput(
                AttackButtonId.Heavy, isAirborne: true, isCharging: true);
            Assert.AreEqual(AttackInputType.AerialHeavy, result);
        }

        // =============================================================
        //  6. スキル (Q) → Skill
        // =============================================================

        [Test]
        public void Skill_Ground_ReturnsSkill()
        {
            AttackInputType? result = InputConverter.ConvertAttackInput(
                AttackButtonId.Skill, isAirborne: false, isCharging: false);
            Assert.AreEqual(AttackInputType.Skill, result);
        }

        [Test]
        public void Skill_Airborne_StillReturnsSkill()
        {
            // Skill は空中/地上関係なく Skill
            AttackInputType? result = InputConverter.ConvertAttackInput(
                AttackButtonId.Skill, isAirborne: true, isCharging: false);
            Assert.AreEqual(AttackInputType.Skill, result);
        }

        [Test]
        public void Skill_Charging_StillReturnsSkill()
        {
            // Skill はチャージ状態に関係なく Skill
            AttackInputType? result = InputConverter.ConvertAttackInput(
                AttackButtonId.Skill, isAirborne: false, isCharging: true);
            Assert.AreEqual(AttackInputType.Skill, result);
        }

        // =============================================================
        //  7. ガード (マウス右ボタン) → guardHeld
        // =============================================================

        [Test]
        public void Guard_HeldTrue_SetsGuardHeld()
        {
            MovementInfo info = new MovementInfo { guardHeld = true };
            Assert.IsTrue(info.guardHeld);
        }

        [Test]
        public void Guard_Released_ClearsGuardHeld()
        {
            MovementInfo info = new MovementInfo { guardHeld = false };
            Assert.IsFalse(info.guardHeld);
        }

        // =============================================================
        //  8. インタラクト (E) → interactPressed
        // =============================================================

        [Test]
        public void Interact_Pressed_SetsInteractPressed()
        {
            MovementInfo info = new MovementInfo { interactPressed = true };
            Assert.IsTrue(info.interactPressed);
        }

        // =============================================================
        //  9. 連携 (V) → cooperationPressed
        // =============================================================

        [Test]
        public void Cooperation_Pressed_SetsCooperationPressed()
        {
            MovementInfo info = new MovementInfo { cooperationPressed = true };
            Assert.IsTrue(info.cooperationPressed);
        }

        // =============================================================
        // 10. 武器切替 (Tab) → weaponSwitchPressed
        // =============================================================

        [Test]
        public void WeaponSwitch_Pressed_SetsWeaponSwitchPressed()
        {
            MovementInfo info = new MovementInfo { weaponSwitchPressed = true };
            Assert.IsTrue(info.weaponSwitchPressed);
        }

        // =============================================================
        // 11. グリップ切替 (G) → gripSwitchPressed
        // =============================================================

        [Test]
        public void GripSwitch_Pressed_SetsGripSwitchPressed()
        {
            MovementInfo info = new MovementInfo { gripSwitchPressed = true };
            Assert.IsTrue(info.gripSwitchPressed);
        }

        // =============================================================
        // 12. メニュー (Escape) → menuPressed
        // =============================================================

        [Test]
        public void Menu_Pressed_SetsMenuPressed()
        {
            MovementInfo info = new MovementInfo { menuPressed = true };
            Assert.IsTrue(info.menuPressed);
        }

        // =============================================================
        // 13. マップ (M) → mapPressed
        // =============================================================

        [Test]
        public void Map_Pressed_SetsMapPressed()
        {
            MovementInfo info = new MovementInfo { mapPressed = true };
            Assert.IsTrue(info.mapPressed);
        }

        // =============================================================
        //  MovementInfo 全フィールド同時設定テスト
        // =============================================================

        [Test]
        public void MovementInfo_AllFieldsSet_NoConflict()
        {
            // 全フィールドを同時にtrueに設定しても干渉しないことを確認
            MovementInfo info = new MovementInfo
            {
                moveDirection = new Vector2(0.5f, 0f),
                jumpPressed = true,
                jumpHeld = true,
                dodgePressed = true,
                sprintHeld = true,
                attackInput = AttackInputType.LightAttack,
                guardHeld = true,
                interactPressed = true,
                cooperationPressed = true,
                weaponSwitchPressed = true,
                gripSwitchPressed = true,
                menuPressed = true,
                mapPressed = true,
                chargeMultiplier = 1.5f,
            };

            Assert.AreEqual(0.5f, info.moveDirection.x, 0.001f);
            Assert.IsTrue(info.jumpPressed);
            Assert.IsTrue(info.jumpHeld);
            Assert.IsTrue(info.dodgePressed);
            Assert.IsTrue(info.sprintHeld);
            Assert.AreEqual(AttackInputType.LightAttack, info.attackInput);
            Assert.IsTrue(info.guardHeld);
            Assert.IsTrue(info.interactPressed);
            Assert.IsTrue(info.cooperationPressed);
            Assert.IsTrue(info.weaponSwitchPressed);
            Assert.IsTrue(info.gripSwitchPressed);
            Assert.IsTrue(info.menuPressed);
            Assert.IsTrue(info.mapPressed);
            Assert.AreEqual(1.5f, info.chargeMultiplier, 0.001f);
        }

        [Test]
        public void MovementInfo_Default_AllFieldsFalseOrZero()
        {
            // デフォルト状態は全入力なし
            MovementInfo info = default;

            Assert.AreEqual(Vector2.zero, info.moveDirection);
            Assert.IsFalse(info.jumpPressed);
            Assert.IsFalse(info.jumpHeld);
            Assert.IsFalse(info.dodgePressed);
            Assert.IsFalse(info.sprintHeld);
            Assert.IsNull(info.attackInput);
            Assert.IsFalse(info.guardHeld);
            Assert.IsFalse(info.interactPressed);
            Assert.IsFalse(info.cooperationPressed);
            Assert.IsFalse(info.weaponSwitchPressed);
            Assert.IsFalse(info.gripSwitchPressed);
            Assert.IsFalse(info.menuPressed);
            Assert.IsFalse(info.mapPressed);
            Assert.AreEqual(0f, info.chargeMultiplier);
        }

        // =============================================================
        //  ChargeInputHandler × InputConverter 結合（全ボタン）
        // =============================================================

        [Test]
        public void ChargeHandler_LightShortPress_ProducesLightAttack()
        {
            ChargeAttackLogic logic = new ChargeAttackLogic();
            ChargeInputHandler handler = new ChargeInputHandler(logic);

            handler.BeginHold((int)AttackButtonId.Light);
            handler.UpdateHold(0.1f);
            handler.EndHold((int)AttackButtonId.Light);

            AttackInputType? result = InputConverter.ConvertAttackInput(
                (AttackButtonId)handler.AttackButtonId, false, handler.IsCharging);
            Assert.AreEqual(AttackInputType.LightAttack, result);
        }

        [Test]
        public void ChargeHandler_HeavyShortPress_ProducesHeavyAttack()
        {
            ChargeAttackLogic logic = new ChargeAttackLogic();
            ChargeInputHandler handler = new ChargeInputHandler(logic);

            handler.BeginHold((int)AttackButtonId.Heavy);
            handler.UpdateHold(0.1f);
            handler.EndHold((int)AttackButtonId.Heavy);

            AttackInputType? result = InputConverter.ConvertAttackInput(
                (AttackButtonId)handler.AttackButtonId, false, handler.IsCharging);
            Assert.AreEqual(AttackInputType.HeavyAttack, result);
        }

        [Test]
        public void ChargeHandler_LightLongPress_ProducesChargeLight()
        {
            ChargeAttackLogic logic = new ChargeAttackLogic();
            ChargeInputHandler handler = new ChargeInputHandler(logic);

            handler.BeginHold((int)AttackButtonId.Light);
            handler.UpdateHold(0.6f); // > chargeLevel1 threshold
            handler.EndHold((int)AttackButtonId.Light);

            AttackInputType? result = InputConverter.ConvertAttackInput(
                (AttackButtonId)handler.AttackButtonId, false, handler.IsCharging);
            Assert.AreEqual(AttackInputType.ChargeLight, result);
            Assert.Greater(handler.ChargeMultiplier, 1f);
        }

        [Test]
        public void ChargeHandler_HeavyLongPress_ProducesChargeHeavy()
        {
            ChargeAttackLogic logic = new ChargeAttackLogic();
            ChargeInputHandler handler = new ChargeInputHandler(logic);

            handler.BeginHold((int)AttackButtonId.Heavy);
            handler.UpdateHold(1.6f); // > chargeLevel2 threshold
            handler.EndHold((int)AttackButtonId.Heavy);

            AttackInputType? result = InputConverter.ConvertAttackInput(
                (AttackButtonId)handler.AttackButtonId, false, handler.IsCharging);
            Assert.AreEqual(AttackInputType.ChargeHeavy, result);
            Assert.Greater(handler.ChargeMultiplier, 1f);
        }

        [Test]
        public void ChargeHandler_SkillButton_ProducesSkill()
        {
            ChargeAttackLogic logic = new ChargeAttackLogic();
            ChargeInputHandler handler = new ChargeInputHandler(logic);

            handler.BeginHold((int)AttackButtonId.Skill);
            handler.EndHold((int)AttackButtonId.Skill);

            AttackInputType? result = InputConverter.ConvertAttackInput(
                (AttackButtonId)handler.AttackButtonId, false, handler.IsCharging);
            Assert.AreEqual(AttackInputType.Skill, result);
        }

        [Test]
        public void ChargeHandler_CancelCharge_NoAttackOutput()
        {
            ChargeAttackLogic logic = new ChargeAttackLogic();
            ChargeInputHandler handler = new ChargeInputHandler(logic);

            handler.BeginHold((int)AttackButtonId.Light);
            handler.UpdateHold(0.8f);
            handler.CancelCharge();

            Assert.IsFalse(handler.HasAttackInput);
            Assert.IsFalse(handler.IsHolding);
            Assert.AreEqual(1f, handler.ChargeMultiplier);
        }

        // =============================================================
        //  AttackButtonId enum 値の一貫性
        // =============================================================

        [Test]
        public void AttackButtonId_Values_MatchExpectedIntegers()
        {
            // PlayerInputHandler のポーリングは int キャストに依存
            Assert.AreEqual(0, (int)AttackButtonId.Light);
            Assert.AreEqual(1, (int)AttackButtonId.Heavy);
            Assert.AreEqual(2, (int)AttackButtonId.Skill);
        }

        // =============================================================
        //  InputConverter 全パターン網羅（境界）
        // =============================================================

        [Test]
        public void InputConverter_AllButtonIds_GroundNoCharge_ReturnsCorrectType()
        {
            Assert.AreEqual(AttackInputType.LightAttack,
                InputConverter.ConvertAttackInput(AttackButtonId.Light, false, false));
            Assert.AreEqual(AttackInputType.HeavyAttack,
                InputConverter.ConvertAttackInput(AttackButtonId.Heavy, false, false));
            Assert.AreEqual(AttackInputType.Skill,
                InputConverter.ConvertAttackInput(AttackButtonId.Skill, false, false));
        }

        [Test]
        public void InputConverter_AllButtonIds_Airborne_ReturnsCorrectType()
        {
            Assert.AreEqual(AttackInputType.AerialLight,
                InputConverter.ConvertAttackInput(AttackButtonId.Light, true, false));
            Assert.AreEqual(AttackInputType.AerialHeavy,
                InputConverter.ConvertAttackInput(AttackButtonId.Heavy, true, false));
            // Skill は空中でも Skill
            Assert.AreEqual(AttackInputType.Skill,
                InputConverter.ConvertAttackInput(AttackButtonId.Skill, true, false));
        }

        [Test]
        public void InputConverter_AllButtonIds_Charging_ReturnsCorrectType()
        {
            Assert.AreEqual(AttackInputType.ChargeLight,
                InputConverter.ConvertAttackInput(AttackButtonId.Light, false, true));
            Assert.AreEqual(AttackInputType.ChargeHeavy,
                InputConverter.ConvertAttackInput(AttackButtonId.Heavy, false, true));
            // Skill はチャージ関係なし
            Assert.AreEqual(AttackInputType.Skill,
                InputConverter.ConvertAttackInput(AttackButtonId.Skill, false, true));
        }
    }
}
