namespace MassNavPlaygroundMod.Runtime;

public sealed class MassNavFlowTuning
{
    public bool Enabled { get; set; }
    public int IterationsPerStep { get; private set; } = 4096;
    public int StepIntervalTicks { get; private set; } = 1;
    public int CrowdStampIntervalTicks { get; private set; } = 1;
    public int ObstacleStampIntervalTicks { get; private set; } = 1;

    public void AdjustIterations(int delta)
    {
        IterationsPerStep = System.Math.Clamp(IterationsPerStep + delta, 0, 131072);
    }

    public void AdjustStepInterval(int delta)
    {
        StepIntervalTicks = ClampInterval(StepIntervalTicks + delta);
    }

    public void AdjustCrowdStampInterval(int delta)
    {
        CrowdStampIntervalTicks = ClampInterval(CrowdStampIntervalTicks + delta);
    }

    public void AdjustObstacleStampInterval(int delta)
    {
        ObstacleStampIntervalTicks = ClampInterval(ObstacleStampIntervalTicks + delta);
    }

    private static int ClampInterval(int value)
    {
        return System.Math.Clamp(value, 1, 32);
    }
}
