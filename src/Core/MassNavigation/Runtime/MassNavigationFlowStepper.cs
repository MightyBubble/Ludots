using System;
using Arch.Core;

namespace Ludots.Core.MassNavigation.Runtime;

/// <summary>
/// Narrow execution facade for callers that own a standalone flow solver but do not own
/// MassNavigation group state. Group bookkeeping remains internal to the execution domain.
/// </summary>
public sealed class MassNavigationFlowStepper
{
    private readonly MassNavigationFlowSolverState _flow;
    private readonly MassNavigationGroupRuntime _groups;

    public MassNavigationFlowStepper(
        MassNavigationFlowSolverState flow,
        MassNavigationRuntimeCapacityConfig capacity)
    {
        _flow = flow ?? throw new ArgumentNullException(nameof(flow));
        ArgumentNullException.ThrowIfNull(capacity);
        _groups = new MassNavigationGroupRuntime(flow.Semantics.Group, capacity);
    }

    public void Step(
        World world,
        float deltaSeconds,
        bool runHardResolve = false,
        int hardResolveCandidateThresholdAgents = 1)
    {
        ArgumentNullException.ThrowIfNull(world);
        _flow.Step(
            deltaSeconds,
            world,
            _groups,
            runHardResolve,
            hardResolveCandidateThresholdAgents);
    }
}
