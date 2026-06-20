namespace Ludots.Core.MassCrowd.Runtime;

public sealed class MassFlowTuning
{
    public bool Enabled { get; set; }
    public int IterationsPerStep { get; set; } = 4096;
    public int MaxIterationsPerStep { get; set; }
    public int StepIntervalTicks { get; set; } = 1;
    public int CrowdStampIntervalTicks { get; set; } = 1;
    public int ObstacleStampIntervalTicks { get; set; } = 1;
    public bool ForceRefreshFlow { get; set; }
    public bool ForceRefreshCrowd { get; set; }
    public bool ForceRefreshObstacles { get; set; }

    public void Validate()
    {
        if (IterationsPerStep < 0)
        {
            throw new System.InvalidOperationException("Mass-nav flow requires IterationsPerStep >= 0.");
        }

        if (MaxIterationsPerStep < 0)
        {
            throw new System.InvalidOperationException("Mass-nav flow requires MaxIterationsPerStep >= 0.");
        }

        if (IterationsPerStep > MaxIterationsPerStep)
        {
            throw new System.InvalidOperationException("Mass-nav flow requires IterationsPerStep <= MaxIterationsPerStep.");
        }

        RequirePositive(StepIntervalTicks, nameof(StepIntervalTicks));
        RequirePositive(CrowdStampIntervalTicks, nameof(CrowdStampIntervalTicks));
        RequirePositive(ObstacleStampIntervalTicks, nameof(ObstacleStampIntervalTicks));
    }

    public void AdjustIterations(int delta)
    {
        IterationsPerStep = System.Math.Clamp(IterationsPerStep + delta, 0, MaxIterationsPerStep);
    }

    private static void RequirePositive(int value, string name)
    {
        if (value <= 0)
        {
            throw new System.InvalidOperationException($"Mass-nav flow requires {name} > 0.");
        }
    }
}
