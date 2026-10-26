using System;

namespace Game.Core
{
    /// <summary>同じtickの入力を制御・物理出力まで渡す固定更新の入口。</summary>
    public sealed class SimulationTickRunner
    {
        private bool _running;
        public long TickId { get; private set; }

        public void Run(ISimulationTickWorld world, float deltaTime, float currentTime)
        {
            if (world == null) { throw new ArgumentNullException(nameof(world)); }
            if (deltaTime < 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)
                || float.IsNaN(currentTime) || float.IsInfinity(currentTime))
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime), "Simulation time must be finite and deltaTime nonnegative.");
            }
            if (_running) { throw new InvalidOperationException("A simulation tick cannot run recursively."); }
            _running = true;
            try
            {
                world.CaptureSimulationActors();
                world.CaptureSimulationPhysics();
                world.PrepareSimulationInputs(currentTime);
                world.AdvanceSharedSimulation(deltaTime, currentTime);
                world.CaptureSimulationObservation(++TickId, currentTime);
                world.SimulateCharacters(deltaTime, currentTime);
                world.ApplySimulationPhysics();
            }
            finally
            {
                _running = false;
            }
        }
    }
}
