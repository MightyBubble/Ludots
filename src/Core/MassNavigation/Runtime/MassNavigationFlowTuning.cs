namespace Ludots.Core.MassNavigation.Runtime;

public sealed class MassNavigationFlowTuning
{
    public bool Enabled { get; set; }
    public int IterationsPerStep { get; set; }
    public int MaxIterationsPerStep { get; set; }
    public bool ForceRefreshFlow { get; set; }
    public bool ForceRefreshCrowd { get; set; }
    public bool ForceRefreshObstacles { get; set; }

    public void Validate()
    {
        if (IterationsPerStep < 0)
        {
            throw new System.InvalidOperationException("MassNavigation flow requires IterationsPerStep >= 0.");
        }

        if (MaxIterationsPerStep < 0)
        {
            throw new System.InvalidOperationException("MassNavigation flow requires MaxIterationsPerStep >= 0.");
        }

        if (IterationsPerStep > MaxIterationsPerStep)
        {
            throw new System.InvalidOperationException("MassNavigation flow requires IterationsPerStep <= MaxIterationsPerStep.");
        }

    }

}
