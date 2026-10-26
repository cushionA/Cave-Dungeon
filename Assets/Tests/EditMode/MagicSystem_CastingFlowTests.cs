using NUnit.Framework;
using Game.Core;

namespace Game.Tests.EditMode
{
    public class MagicSystem_CastingFlowTests
    {
        private SoACharaDataDic _data;
        private int _casterHash;

        [SetUp]
        public void SetUp()
        {
            _data = new SoACharaDataDic();
            _casterHash = 1;
            _data.Add(_casterHash,
                new CharacterVitals { currentMp = 50, maxMp = 100, currentHp = 100, maxHp = 100 },
                default, default, default);
        }

        [TearDown]
        public void TearDown()
        {
            _data.Dispose();
        }

        [Test]
        public void MagicCaster_InsufficientMp_CannotCast()
        {
            MagicCaster caster = new MagicCaster();
            MagicDefinition magic = new MagicDefinition { magicId = 1, mpCost = 999 };

            bool result = caster.StartCast(magic, _casterHash, _data, 0f);

            Assert.IsFalse(result);
        }

        [Test]
        public void MagicCaster_StartCast_ConsumeMp()
        {
            MagicCaster caster = new MagicCaster();
            MagicDefinition magic = new MagicDefinition { magicId = 1, mpCost = 20, castTime = 1f };

            caster.StartCast(magic, _casterHash, _data, 0f);

            ref CharacterVitals v = ref _data.GetVitals(_casterHash);
            Assert.AreEqual(30, v.currentMp);
        }

        [TestCase(50.5f, 100, 20, 30.5f, 30)]
        [TestCase(50f, 100, 50, 0f, 0)]
        [TestCase(0f, 0, 0, 0f, 0)]
        public void MagicCaster_StartCast_PublishesSynchronizedMpRatio(float currentMp, int maxMp,
            int cost, float expectedMp, int expectedRatio)
        {
            _data.GetVitals(_casterHash).currentMp = currentMp;
            _data.GetVitals(_casterHash).maxMp = maxMp;
            using MagicCaster caster = new MagicCaster();
            MagicDefinition magic = new MagicDefinition { magicId = 1, mpCost = cost };
            int started = 0;
            int fired = 0;
            caster.OnCastStarted += (hash, definition) =>
            {
                started++;
                Assert.AreEqual(expectedMp, _data.GetVitals(hash).currentMp);
                Assert.AreEqual(expectedRatio, _data.GetVitals(hash).mpRatio);
            };
            caster.OnFired += (hash, definition) =>
            {
                fired++;
                Assert.AreEqual(expectedRatio, _data.GetVitals(hash).mpRatio);
            };

            Assert.IsTrue(caster.StartCast(magic, _casterHash, _data, 0f));

            Assert.AreEqual(1, started);
            Assert.AreEqual(1, fired);
            Assert.AreEqual(expectedRatio, _data.GetVitals(_casterHash).mpRatio);
        }

        [Test]
        public void MagicCaster_CastTime_FiresAfterDelay()
        {
            MagicCaster caster = new MagicCaster();
            MagicDefinition magic = new MagicDefinition { magicId = 1, mpCost = 10, castTime = 0.5f };
            bool fired = false;
            caster.OnFired += (h, m) => fired = true;

            caster.StartCast(magic, _casterHash, _data, 0f);
            Assert.AreEqual(CastState.Casting, caster.State);

            caster.Tick(0.3f);
            Assert.IsFalse(fired);

            caster.Tick(0.3f);
            Assert.IsTrue(fired);
        }

        [Test]
        public void MagicCaster_Cooldown_BlocksSecondCast()
        {
            MagicCaster caster = new MagicCaster();
            MagicDefinition magic = new MagicDefinition { magicId = 1, mpCost = 5, cooldownDuration = 10f };

            Assert.IsTrue(caster.StartCast(magic, _casterHash, _data, 0f));
            Assert.IsFalse(caster.StartCast(magic, _casterHash, _data, 5f));
            Assert.IsTrue(caster.StartCast(magic, _casterHash, _data, 10f));
        }

        [Test]
        public void MagicCaster_Interrupt_StopsCasting()
        {
            MagicCaster caster = new MagicCaster();
            MagicDefinition magic = new MagicDefinition { magicId = 1, mpCost = 5, castTime = 2f };
            bool interrupted = false;
            caster.OnCastInterrupted += () => interrupted = true;

            caster.StartCast(magic, _casterHash, _data, 0f);
            caster.Interrupt();

            Assert.IsTrue(interrupted);
            Assert.AreEqual(CastState.Idle, caster.State);
        }
    }
}
