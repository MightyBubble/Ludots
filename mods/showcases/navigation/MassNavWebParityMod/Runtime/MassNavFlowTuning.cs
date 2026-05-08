namespace MassNavWebParityMod.Runtime;

public sealed class MassNavFlowTuning
{
    public bool Enabled { get; set; }
    public int IterationsPerStep { get; set; } = 4096;
    public int StepIntervalTicks { get; set; } = 1;
    public int CrowdStampIntervalTicks { get; set; } = 1;
    public int ObstacleStampIntervalTicks { get; set; } = 1;
    public bool ForceRefreshFlow { get; set; }
    public bool ForceRefreshCrowd { get; set; }
    public bool ForceRefreshObstacles { get; set; }

    public void AdjustIterations(int delta)
    {
        IterationsPerStep = System.Math.Clamp(IterationsPerStep + delta, 0, 131072);
    }

}
