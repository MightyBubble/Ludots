namespace Ludots.Core.MassNavigation.Runtime;

public sealed class MassNavigationRuntimeBinding
{
    public MassNavigationSimulationRuntime? Current { get; private set; }
    public int Revision { get; private set; }

    public static MassNavigationRuntimeBinding CreateActivated(MassNavigationSimulationRuntime simulation)
    {
        var binding = new MassNavigationRuntimeBinding();
        binding.Activate(simulation);
        return binding;
    }

    public void Activate(MassNavigationSimulationRuntime simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        if (ReferenceEquals(Current, simulation))
        {
            return;
        }

        Current = simulation;
        Revision++;
    }

    public void Clear(MassNavigationSimulationRuntime simulation)
    {
        if (!ReferenceEquals(Current, simulation))
        {
            return;
        }

        Current = null;
        Revision++;
    }

    public MassNavigationSimulationRuntime RequireCurrent()
    {
        return Current
            ?? throw new InvalidOperationException("MassNavigation has no active map-bound simulation runtime.");
    }
}
