using NUnit.Framework;
using UnityEngine;
using Game.Core;

public class SlopeNormalTests
{
    private const float k_Tolerance = 0.001f;

    // ---- GroundInfo 構造体テスト ----

    [Test]
    public void GroundInfo_NotGrounded_IsFalseWithUpNormal()
    {
        GroundInfo info = GroundInfo.NotGrounded;
        Assert.IsFalse(info.isGrounded);
        Assert.AreEqual(Vector2.up, info.normal);
    }

    [Test]
    public void GroundInfo_Flat_IsTrueWithUpNormal()
    {
        GroundInfo info = GroundInfo.Flat;
        Assert.IsTrue(info.isGrounded);
        Assert.AreEqual(Vector2.up, info.normal);
    }

    // ---- ProjectVelocityOnSlope テスト ----

    [Test]
    public void ProjectVelocityOnSlope_FlatGround_ReturnsVelocityUnchanged()
    {
        Vector2 velocity = new Vector2(5f, 0f);
        Vector2 result = GroundMovementLogic.ProjectVelocityOnSlope(velocity, Vector2.up);
        Assert.AreEqual(velocity.x, result.x, k_Tolerance);
        Assert.AreEqual(velocity.y, result.y, k_Tolerance);
    }

    [Test]
    public void ProjectVelocityOnSlope_ZeroVelocity_ReturnsZero()
    {
        Vector2 result = GroundMovementLogic.ProjectVelocityOnSlope(Vector2.zero, Vector2.up);
        Assert.AreEqual(Vector2.zero, result);
    }

    [Test]
    public void ProjectVelocityOnSlope_Slope30Deg_RightMovement_ProjectsUpward()
    {
        Vector2 velocity = new Vector2(5f, 0f);
        // 右上がり30°斜面: 法線は左上を向く (-sin30, cos30)
        Vector2 normal30 = new Vector2(-Mathf.Sin(30f * Mathf.Deg2Rad), Mathf.Cos(30f * Mathf.Deg2Rad));
        Vector2 result = GroundMovementLogic.ProjectVelocityOnSlope(velocity, normal30);

        Assert.Greater(result.y, 0f, "右に移動して右上がり30°斜面を登るとY成分が正になるべき");
        Assert.Greater(result.x, 0f, "X成分は正を維持するべき");
        Assert.Less(result.x, velocity.x, "斜面上のX成分は入力速度より小さくなるべき");
    }

    [Test]
    public void ProjectVelocityOnSlope_Slope45Deg_BothComponentsEqual()
    {
        Vector2 velocity = new Vector2(5f, 0f);
        // 右上がり45°斜面: 法線 (-sin45, cos45) = (-0.707, 0.707)
        Vector2 normal45 = new Vector2(-Mathf.Sin(45f * Mathf.Deg2Rad), Mathf.Cos(45f * Mathf.Deg2Rad));
        Vector2 result = GroundMovementLogic.ProjectVelocityOnSlope(velocity, normal45);

        Assert.Greater(result.x, 0f, "X成分は正を維持するべき");
        Assert.AreEqual(result.x, result.y, k_Tolerance, "45°斜面ではX成分とY成分が等しくなるべき");
    }

    [Test]
    public void ProjectVelocityOnSlope_SlopeTooSteep_ReturnsZero()
    {
        Vector2 velocity = new Vector2(5f, 0f);
        // 60°斜面: 最大45°を超えるため Vector2.zero を返すべき
        Vector2 normal60 = new Vector2(-Mathf.Sin(60f * Mathf.Deg2Rad), Mathf.Cos(60f * Mathf.Deg2Rad));
        Vector2 result = GroundMovementLogic.ProjectVelocityOnSlope(velocity, normal60);

        Assert.AreEqual(Vector2.zero.x, result.x, k_Tolerance, "急勾配(60°)ではX=0を返すべき");
        Assert.AreEqual(Vector2.zero.y, result.y, k_Tolerance, "急勾配(60°)ではY=0を返すべき");
    }

    [Test]
    public void ProjectVelocityOnSlope_LeftMovement_LeftAscendingSlope_ProjectsUpward()
    {
        Vector2 velocity = new Vector2(-5f, 0f);
        // 左上がり30°斜面: 法線は右上を向く (sin30, cos30)
        Vector2 normalLeft30 = new Vector2(Mathf.Sin(30f * Mathf.Deg2Rad), Mathf.Cos(30f * Mathf.Deg2Rad));
        Vector2 result = GroundMovementLogic.ProjectVelocityOnSlope(velocity, normalLeft30);

        Assert.Less(result.x, 0f, "左上がり斜面を左に移動するとX成分は負を維持するべき");
        Assert.Greater(result.y, 0f, "斜面を登るY成分は正になるべき");
    }

    [Test]
    public void ProjectVelocityOnSlope_CustomMaxAngle_RespectsOverride()
    {
        Vector2 velocity = new Vector2(5f, 0f);
        // 30°斜面で最大角度を20°に制限した場合、Vector2.zero を返すべき
        Vector2 normal30 = new Vector2(-Mathf.Sin(30f * Mathf.Deg2Rad), Mathf.Cos(30f * Mathf.Deg2Rad));
        Vector2 result = GroundMovementLogic.ProjectVelocityOnSlope(velocity, normal30, 20f);

        Assert.AreEqual(Vector2.zero.x, result.x, k_Tolerance, "カスタム最大角度(20°)を超えた30°斜面はゼロを返すべき");
        Assert.AreEqual(Vector2.zero.y, result.y, k_Tolerance);
    }
}
