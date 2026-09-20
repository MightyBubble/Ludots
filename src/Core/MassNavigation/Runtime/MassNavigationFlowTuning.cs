namespace Ludots.Core.MassNavigation.Runtime;

public sealed class MassNavigationFlowTuning
{
    public bool Enabled { get; set; }
    public int IterationsPerStep { get; set; }

    public void Validate()
    {
        if (IterationsPerStep < 0)
        {
            throw new System.InvalidOperationException("MassNavigation flow requires IterationsPerStep >= 0.");
        }

    }

}
