using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.MassNavigation.Runtime;

namespace Ludots.Core.MassNavigation.Systems;

internal sealed class MassNavigationLocomotionAnimatorParamSystem : BaseSystem<World, float>
{
    private readonly GameEngine _engine;
    private readonly int _speedParamKey;
    private readonly QueryDescription _presenterQuery = new QueryDescription()
        .WithAll<PresenterState, PresenterFloatParams, PresenterCullState>();

    public MassNavigationLocomotionAnimatorParamSystem(GameEngine engine)
        : base((engine ?? throw new ArgumentNullException(nameof(engine))).World)
    {
        _engine = engine;
        _speedParamKey = MassNavigationSimulationRuntime.ResolveAgentLocomotionSpeedParamKey();
    }

    public override void Update(in float dt)
    {
        if (!MassNavigationIds.TryGetCurrentNavigationRuntime(_engine, out MassNavigationSimulationRuntime simulation))
        {
            return;
        }

        foreach (ref var chunk in World.Query(in _presenterQuery))
        {
            Span<PresenterState> states = chunk.GetSpan<PresenterState>();
            Span<PresenterFloatParams> floatParams = chunk.GetSpan<PresenterFloatParams>();
            Span<PresenterCullState> culls = chunk.GetSpan<PresenterCullState>();
            foreach (int index in chunk)
            {
                if (!culls[index].OwnerCullVisible ||
                    !TryResolveAgentIndex(simulation, in states[index], out int agentIndex))
                {
                    continue;
                }

                float speed = ResolveNormalizedSpeed(simulation, agentIndex);
                ref PresenterFloatParams parameters = ref floatParams[index];
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
        in PresenterState state,
        out int agentIndex)
    {
        agentIndex = -1;
        Entity owner = state.OwnerEntity;
        if (owner == Entity.Null ||
            !World.IsAlive(owner) ||
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
