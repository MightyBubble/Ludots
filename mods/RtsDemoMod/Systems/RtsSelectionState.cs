namespace RtsDemoMod.Systems
{
    public enum RtsScenarioId : byte
    {
        PassThrough = 1,
        Bottleneck = 2,
        Formation = 3,
    }

    public sealed class RtsSelectionState
    {
        public bool ShowVelocityVectors { get; set; } = true;
        public RtsScenarioId CurrentScenario { get; set; } = RtsScenarioId.PassThrough;
        public bool HasLastCommand { get; set; }
        public Ludots.Core.Mathematics.WorldCmInt2 LastCommandPointCm { get; set; }
    }
}
