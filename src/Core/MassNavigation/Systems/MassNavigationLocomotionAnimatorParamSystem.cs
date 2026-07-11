using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.MassNavigation.Runtime;

namespace Ludots.Core.MassNavigation.Systems;

internal sealed class MassNavigationLocomotionAnimatorParamSystem : BaseSystem<World, float>
{
    private readonly MassNavigationRuntimeBinding _binding;
    private readonly int _speedParamKey;
    private readonly QueryDescription _performerQuery = new QueryDescription()
        .WithAll<PerformerState, PerformerFloatParams, PerformerCullState>()
        .WithNone<SuspendedTag>();

    public MassNavigationLocomotionAnimatorParamSystem(World world, MassNavigationRuntimeBinding binding)
        : base(world)
    {
        _binding = binding ?? throw new ArgumentNullException(nameof(binding));
        _speedParamKey = MassNavigationSimulationRuntime.ResolveAgentLocomotionSpeedParamKey();
    }

    public override void Update(in float dt)
    {
        MassNavigationSimulationRuntime? simulation = _binding.Current;
        if (simulation == null || !simulation.IsReadyForWorldOperations)
        {
            return;
        }

        foreach (ref var chunk in World.Query(in _performerQuery))
        {
            Span<PerformerState> states = chunk.GetSpan<PerformerState>();
            Span<PerformerFloatParams> floatParams = chunk.GetSpan<PerformerFloatParams>();
            Span<PerformerCullState> culls = chunk.GetSpan<PerformerCullState>();
            foreach (int index in chunk)
            {
                if (!culls[index].OwnerCullVisible ||
                    !TryResolveAgentIndex(simulation, in states[index], out int agentIndex))
                {
                    continue;
                }

                float speed = ResolveNormalizedSpeed(simulation, agentIndex);
                ref PerformerFloatParams parameters = ref floatParams[index];
                if (parameters.TryGet(_speedParamKey, out float current) && MathF.Abs(current - speed) <= 0.0001f)
                {
                    continue;
                }

                parameters.Set(_speedParamKey, speed);
                states[index].Version++;
            }
        }
    }

    private bool TryResolveAgentIndex(
        MassNavigationSimulationRuntime simulation,
        in PerformerState state,
        out int agentIndex)
    {
        agentIndex = -1;
        Entity owner = state.OwnerEntity;
        if (owner == Entity.Null ||
            !World.IsAlive(owner) ||
            World.Has<SuspendedTag>(owner) ||
            !World.TryGet(owner, out MassNavigationAgentIndex authoredIndex))
        {
            return false;
        }

        agentIndex = authoredIndex.Value;
        return (uint)agentIndex < (uint)simulation.NavigationAgentCount;
    }

    private static float ResolveNormalizedSpeed(MassNavigationSimulationRuntime simulation, int agentIndex)
    {
        return simulation.TryGetAgentLocomotionSpeedNormalized(agentIndex, out float speed)
            ? speed
            : 0f;
    }
}
