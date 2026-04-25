using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// 壁蹴り左右交互制約ロジックのテスト。
    /// AdvancedMovementLogic.TryWallKickWithAlternation を対象とする。
    /// </summary>
    public class WallKickAlternationTests
    {
        private const int k_WallIdA = 100;
        private const int k_WallIdB = 200;

        // --- 基本: フラグ・入力チェック ---

        [Test]
        public void WallKickAlternation_NoAbilityFlag_ReturnsZero()
        {
            int newId;
            Vector2 result = AdvancedMovementLogic.TryWallKickWithAlternation(
                AbilityFlag.None, isTouchingWall: true, jumpPressed: true, isFacingRight: true,
                currentWallColliderId: k_WallIdA, lastKickedWallId: 0, out newId);

            Assert.AreEqual(Vector2.zero, result);
            Assert.AreEqual(0, newId);
        }

        [Test]
        public void WallKickAlternation_NotTouchingWall_ReturnsZero()
        {
            int newId;
            Vector2 result = AdvancedMovementLogic.TryWallKickWithAlternation(
                AbilityFlag.WallKick, isTouchingWall: false, jumpPressed: true, isFacingRight: true,
                currentWallColliderId: k_WallIdA, lastKickedWallId: 0, out newId);

            Assert.AreEqual(Vector2.zero, result);
            Assert.AreEqual(0, newId);
        }

        [Test]
        public void WallKickAlternation_JumpNotPressed_ReturnsZero()
        {
            int newId;
            Vector2 result = AdvancedMovementLogic.TryWallKickWithAlternation(
                AbilityFlag.WallKick, isTouchingWall: true, jumpPressed: false, isFacingRight: true,
                currentWallColliderId: k_WallIdA, lastKickedWallId: 0, out newId);

            Assert.AreEqual(Vector2.zero, result);
            Assert.AreEqual(0, newId);
        }

        // --- 交互制約 ---

        [Test]
        public void WallKickAlternation_FirstKick_LastIdZero_Succeeds()
        {
            int newId;
            Vector2 result = AdvancedMovementLogic.TryWallKickWithAlternation(
                AbilityFlag.WallKick, isTouchingWall: true, jumpPressed: true, isFacingRight: true,
                currentWallColliderId: k_WallIdA, lastKickedWallId: 0, out newId);

            Assert.AreNotEqual(Vector2.zero, result);
            Assert.AreEqual(k_WallIdA, newId);
        }

        [Test]
        public void WallKickAlternation_SameWallId_ReturnsZero()
        {
            // 前回と同じ壁IDは蹴れない
            int newId;
            Vector2 result = AdvancedMovementLogic.TryWallKickWithAlternation(
                AbilityFlag.WallKick, isTouchingWall: true, jumpPressed: true, isFacingRight: true,
                currentWallColliderId: k_WallIdA, lastKickedWallId: k_WallIdA, out newId);

            Assert.AreEqual(Vector2.zero, result);
            Assert.AreEqual(k_WallIdA, newId, "lastKickedWallId should remain unchanged on rejection");
        }

        [Test]
        public void WallKickAlternation_DifferentWallId_Succeeds()
        {
            // 異なる壁IDは蹴れる
            int newId;
            Vector2 result = AdvancedMovementLogic.TryWallKickWithAlternation(
                AbilityFlag.WallKick, isTouchingWall: true, jumpPressed: true, isFacingRight: true,
                currentWallColliderId: k_WallIdB, lastKickedWallId: k_WallIdA, out newId);

            Assert.AreNotEqual(Vector2.zero, result);
            Assert.AreEqual(k_WallIdB, newId);
        }

        [Test]
        public void WallKickAlternation_AfterGroundReset_AllowsSameWall()
        {
            // 着地リセット後（lastId=0）なら同じ壁IDも再び蹴れる
            int newId;
            Vector2 result = AdvancedMovementLogic.TryWallKickWithAlternation(
                AbilityFlag.WallKick, isTouchingWall: true, jumpPressed: true, isFacingRight: true,
                currentWallColliderId: k_WallIdA, lastKickedWallId: 0, out newId);

            Assert.AreNotEqual(Vector2.zero, result);
            Assert.AreEqual(k_WallIdA, newId);
        }

        [Test]
        public void WallKickAlternation_NoWallId_AlwaysAllowed()
        {
            // currentWallColliderId=0（壁ID不明）でも蹴り自体は可能（lastKickedWallId!=0）
            int newId;
            Vector2 result = AdvancedMovementLogic.TryWallKickWithAlternation(
                AbilityFlag.WallKick, isTouchingWall: true, jumpPressed: true, isFacingRight: true,
                currentWallColliderId: AdvancedMovementLogic.k_NoWallId, lastKickedWallId: k_WallIdA, out newId);

            Assert.AreNotEqual(Vector2.zero, result);
        }

        // --- 方向・力量 ---

        [Test]
        public void WallKickAlternation_FacingRight_ReturnsPositiveX()
        {
            int newId;
            Vector2 result = AdvancedMovementLogic.TryWallKickWithAlternation(
                AbilityFlag.WallKick, true, true, isFacingRight: true,
                k_WallIdA, 0, out newId);

            Assert.Greater(result.x, 0f);
            Assert.AreEqual(AdvancedMovementLogic.k_WallKickForceX, result.x, 0.001f);
        }

        [Test]
        public void WallKickAlternation_FacingLeft_ReturnsNegativeX()
        {
            int newId;
            Vector2 result = AdvancedMovementLogic.TryWallKickWithAlternation(
                AbilityFlag.WallKick, true, true, isFacingRight: false,
                k_WallIdA, 0, out newId);

            Assert.Less(result.x, 0f);
            Assert.AreEqual(-AdvancedMovementLogic.k_WallKickForceX, result.x, 0.001f);
        }

        [Test]
        public void WallKickAlternation_ReturnsCorrectYForce()
        {
            int newId;
            Vector2 result = AdvancedMovementLogic.TryWallKickWithAlternation(
                AbilityFlag.WallKick, true, true, true,
                k_WallIdA, 0, out newId);

            Assert.AreEqual(AdvancedMovementLogic.k_WallKickForceY, result.y, 0.001f);
        }

        // --- 連続シーケンス ---

        [Test]
        public void WallKickAlternation_AlternatingWalls_BothSucceed()
        {
            // 壁A → 壁B → 壁A の交互は全て成功する
            int newId1;
            Vector2 kick1 = AdvancedMovementLogic.TryWallKickWithAlternation(
                AbilityFlag.WallKick, true, true, true,
                k_WallIdA, 0, out newId1);

            int newId2;
            Vector2 kick2 = AdvancedMovementLogic.TryWallKickWithAlternation(
                AbilityFlag.WallKick, true, true, false,
                k_WallIdB, newId1, out newId2);

            int newId3;
            Vector2 kick3 = AdvancedMovementLogic.TryWallKickWithAlternation(
                AbilityFlag.WallKick, true, true, true,
                k_WallIdA, newId2, out newId3);

            Assert.AreNotEqual(Vector2.zero, kick1, "1st kick should succeed");
            Assert.AreNotEqual(Vector2.zero, kick2, "2nd kick (different wall) should succeed");
            Assert.AreNotEqual(Vector2.zero, kick3, "3rd kick (alternating back) should succeed");
        }

        [Test]
        public void WallKickAlternation_ConsecutiveSameWall_SecondFails()
        {
            // 壁A → 壁A（連続同壁）は2回目が失敗する
            int newId1;
            Vector2 kick1 = AdvancedMovementLogic.TryWallKickWithAlternation(
                AbilityFlag.WallKick, true, true, true,
                k_WallIdA, 0, out newId1);

            int newId2;
            Vector2 kick2 = AdvancedMovementLogic.TryWallKickWithAlternation(
                AbilityFlag.WallKick, true, true, true,
                k_WallIdA, newId1, out newId2);

            Assert.AreNotEqual(Vector2.zero, kick1, "1st kick should succeed");
            Assert.AreEqual(Vector2.zero, kick2, "2nd consecutive same wall kick should fail");
        }
    }
}
