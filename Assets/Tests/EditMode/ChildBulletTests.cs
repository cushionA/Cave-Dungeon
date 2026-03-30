using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// 子弾システムの単体テスト。
    /// ChildBulletConfig、MagicDefinition差し替え、タイマーチェックを検証する。
    /// </summary>
    public class ChildBulletTests
    {
        private BulletProfile _parentProfile;
        private BulletProfile _childProfile;
        private MagicDefinition _parentMagic;
        private ChildBulletConfig _config;

        [SetUp]
        public void SetUp()
        {
            _parentProfile = new BulletProfile
            {
                moveType = BulletMoveType.Straight,
                speed = 15f,
                hitLimit = 1,
                lifeTime = 3f
            };

            _childProfile = new BulletProfile
            {
                moveType = BulletMoveType.Homing,
                speed = 8f,
                hitLimit = 1,
                lifeTime = 2f
            };

            _config = new ChildBulletConfig
            {
                trigger = ChildBulletTrigger.OnHit,
                profile = _childProfile,
                count = 3,
                spreadAngle = 60f
            };

            _parentMagic = new MagicDefinition
            {
                magicName = "Fireball",
                magicType = MagicType.Attack,
                bulletProfile = _parentProfile,
                motionValue = 1.2f,
                attackElement = Element.Fire,
                childBullet = _config
            };
        }

        [Test]
        public void ChildBulletConfig_デフォルト値_triggerがNone()
        {
            ChildBulletConfig defaultConfig = new ChildBulletConfig();
            Assert.AreEqual(ChildBulletTrigger.None, defaultConfig.trigger);
            Assert.AreEqual(0, defaultConfig.count);
        }

        [Test]
        public void 子弾MagicDefinition_親のmagicに子のBulletProfileが差し替わる()
        {
            MagicDefinition childMagic = ChildBulletHelper.CreateChildMagic(_parentMagic, _config);

            // 子のBulletProfileが使われる
            Assert.AreEqual(BulletMoveType.Homing, childMagic.bulletProfile.moveType);
            Assert.AreEqual(8f, childMagic.bulletProfile.speed);

            // 親のダメージ属性は継承
            Assert.AreEqual(1.2f, childMagic.motionValue);
            Assert.AreEqual(Element.Fire, childMagic.attackElement);
        }

        [Test]
        public void 子弾MagicDefinition_子弾の子弾設定はnullになる()
        {
            MagicDefinition childMagic = ChildBulletHelper.CreateChildMagic(_parentMagic, _config);

            // 無限再帰防止: 子弾のchildBulletはnull
            Assert.IsNull(childMagic.childBullet);
        }

        [Test]
        public void OnTimerチェック_emitInterval経過_trueを返す()
        {
            float emitInterval = 0.5f;
            float elapsedTime = 1.5f;
            float lastEmitTime = 0.8f;

            bool shouldEmit = ChildBulletHelper.ShouldEmitOnTimer(
                elapsedTime, lastEmitTime, emitInterval);

            Assert.IsTrue(shouldEmit);
        }

        [Test]
        public void OnTimerチェック_emitInterval未到達_falseを返す()
        {
            float emitInterval = 0.5f;
            float elapsedTime = 1.0f;
            float lastEmitTime = 0.8f;

            bool shouldEmit = ChildBulletHelper.ShouldEmitOnTimer(
                elapsedTime, lastEmitTime, emitInterval);

            Assert.IsFalse(shouldEmit);
        }
    }
}
