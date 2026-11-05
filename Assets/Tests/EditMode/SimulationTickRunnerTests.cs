using System;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    public class SimulationTickRunnerTests
    {
        private sealed class World : ISimulationTickWorld
        {
            public bool nextGrounded;
            public bool grounded;
            public bool jumpHeld;
            public bool jumpRequested;
            public bool observationGrounded;
            public float velocity;
            public float appliedVelocity;
            public int applies;
            public int captures;
            public Action onAdvance;
            public long observedTick;

            public void CaptureSimulationActors() { captures++; }
            public void CaptureSimulationPhysics() { grounded = nextGrounded; }
            public void PrepareSimulationInputs(float time) { jumpRequested = jumpHeld && grounded; }
            public void AdvanceSharedSimulation(float deltaTime, float time) { onAdvance?.Invoke(); }
            public void CaptureSimulationObservation(long tick, float time)
            {
                observedTick = tick;
                observationGrounded = grounded;
            }
            public void SimulateCharacters(float deltaTime, float time)
            {
                velocity = jumpRequested && observationGrounded ? 7f : 0f;
            }
            public void ApplySimulationPhysics() { appliedVelocity = velocity; applies++; }
        }

        [Test]
        public void SimulationTick_NewGroundContactAndInput_ReachPhysicsInTheSameTick()
        {
            World world = new World { nextGrounded = true, jumpHeld = true };
            SimulationTickRunner runner = new SimulationTickRunner();

            runner.Run(world, 0.02f, 1f);

            Assert.AreEqual(7f, world.appliedVelocity);
            Assert.AreEqual(1, world.applies);
            Assert.AreEqual(1, world.observedTick);
        }

        [Test]
        public void SimulationTick_NextTickInputRelease_DoesNotApplyPreviousCommandAgain()
        {
            World world = new World { nextGrounded = true, jumpHeld = true };
            SimulationTickRunner runner = new SimulationTickRunner();
            runner.Run(world, 0.02f, 1f);
            world.jumpHeld = false;

            runner.Run(world, 0.02f, 1.02f);

            Assert.AreEqual(0f, world.appliedVelocity);
            Assert.AreEqual(2, world.applies);
            Assert.AreEqual(2, world.observedTick);
        }

        [Test]
        public void SimulationTick_ReentrantRun_IsRejectedBeforeCapturingOrApplyingAnotherTick()
        {
            World world = new World();
            SimulationTickRunner runner = new SimulationTickRunner();
            world.onAdvance = () => Assert.Throws<InvalidOperationException>(() => runner.Run(world, 0.02f, 1f));

            runner.Run(world, 0.02f, 1f);

            Assert.AreEqual(1, world.captures);
            Assert.AreEqual(1, world.applies);
            Assert.AreEqual(1, runner.TickId);
        }

        [Test]
        public void SimulationTick_FailedAdvance_DoesNotApplyPartialOutputAndAllowsNextTick()
        {
            World world = new World();
            SimulationTickRunner runner = new SimulationTickRunner();
            world.onAdvance = () => throw new InvalidOperationException("test failure");
            Assert.Throws<InvalidOperationException>(() => runner.Run(world, 0.02f, 1f));
            Assert.AreEqual(0, world.applies);
            world.onAdvance = null;

            runner.Run(world, 0.02f, 1.02f);

            Assert.AreEqual(1, world.applies);
        }

        [TestCase(-1f, 0f)]
        [TestCase(float.NaN, 0f)]
        [TestCase(float.PositiveInfinity, 0f)]
        [TestCase(0f, float.NaN)]
        public void SimulationTick_InvalidTime_RejectsBeforeWorldMutation(float deltaTime, float time)
        {
            World world = new World();
            SimulationTickRunner runner = new SimulationTickRunner();

            Assert.Throws<ArgumentOutOfRangeException>(() => runner.Run(world, deltaTime, time));
            Assert.AreEqual(0, world.captures);
            Assert.AreEqual(0, world.applies);
        }
    }
}
