namespace Game.Core
{
    /// <summary>入力と物理の受渡しを含む固定tickの各段階。</summary>
    public interface ISimulationTickWorld
    {
        void CaptureSimulationActors();
        void CaptureSimulationPhysics();
        void PrepareSimulationInputs(float currentTime);
        void AdvanceSharedSimulation(float deltaTime, float currentTime);
        void CaptureSimulationObservation(long tick, float currentTime);
        void SimulateCharacters(float deltaTime, float currentTime);
        void ApplySimulationPhysics();
    }
}
