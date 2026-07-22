using Ludots.Core.Map;

namespace Ludots.Core.MassNavigation.Runtime;

/// <summary>
/// Single source of truth for the currently focused MassNavigation map runtime.
/// A runtime becomes consumable only after the same binding revision is prepared.
/// </summary>
public sealed class MassNavigationRuntimeBinding
{
    public MapId CurrentMapId { get; private set; }
    public MassNavigationSimulationRuntime? Current { get; private set; }
    public int Revision { get; private set; }
    public int PreparedRevision { get; private set; } = -1;

    public bool IsReady => Current != null && PreparedRevision == Revision;

    public void Activate(MapId mapId, MassNavigationSimulationRuntime simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        if (Current != null &&
            !ReferenceEquals(Current, simulation))
        {
            throw new InvalidOperationException(
                $"MassNavigation runtime binding cannot replace active map '{CurrentMapId.Value}' before it is suspended.");
        }

        if (ReferenceEquals(Current, simulation) && CurrentMapId == mapId)
        {
            return;
        }

        CurrentMapId = mapId;
        Current = simulation;
        Revision++;
        PreparedRevision = -1;
    }

    public void MarkPrepared(MapId mapId, MassNavigationSimulationRuntime simulation)
    {
        if (!ReferenceEquals(Current, simulation) || CurrentMapId != mapId)
        {
            throw new InvalidOperationException(
                $"MassNavigation cannot prepare map '{mapId.Value}' because that runtime is not the active binding.");
        }

        PreparedRevision = Revision;
    }

    public void Clear(MapId mapId, MassNavigationSimulationRuntime simulation)
    {
        if (!ReferenceEquals(Current, simulation) || CurrentMapId != mapId)
        {
            return;
        }

        CurrentMapId = default;
        Current = null;
        Revision++;
        PreparedRevision = -1;
    }

    public MassNavigationSimulationRuntime RequireCurrent()
    {
        if (!IsReady)
        {
            throw new InvalidOperationException("MassNavigation has no prepared current map runtime.");
        }

        return Current!;
    }
}
