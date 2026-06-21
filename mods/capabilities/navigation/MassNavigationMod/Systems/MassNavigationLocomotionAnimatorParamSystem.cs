using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Presentation.Performers;
using MassNavigationMod.Runtime;

namespace MassNavigationMod.Systems;

internal sealed class MassNavigationLocomotionAnimatorParamSystem : BaseSystem<World, float>
{
    private readonly MassNavigationSimulationRuntime _simulation;
    private readonly int _speedParamKey;
    private readonly QueryDescription _performerQuery = new QueryDescription()
        .WithAll<PerformerState, PerformerFloatParams, PerformerCullState>();

    public MassNavigationLocomotionAnimatorParamSystem(World world, MassNavigationSimulationRuntime simulation)
        : base(world)
    {
        _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
        _speedParamKey = MassNavigationSimulationRuntime.ResolveAgentLocomotionSpeedParamKey();
    }

    public override void Update(in float dt)
    {
        foreach (ref var chunk in World.Query(in _performerQuery))
        {
            Span<PerformerState> states = chunk.GetSpan<PerformerState>();
            Span<PerformerFloatParams> floatParams = chunk.GetSpan<PerformerFloatParams>();
            Span<PerformerCullState> culls = chunk.GetSpan<PerformerCullState>();
            foreach (int index in chunk)
            {
                if (!culls[index].OwnerCullVisible ||
                    !TryResolveAgentIndex(in states[index], out int agentIndex))
                {
                    continue;
                }

                float speed = ResolveNormalizedSpeed(agentIndex);
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

    private bool TryResolveAgentIndex(in PerformerState state, out int agentIndex)
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
        return (uint)agentIndex < (uint)_simulation.MassFlow.UnitCount;
    }

    private float ResolveNormalizedSpeed(int agentIndex)
    {
        if (!_simulation.MassFlow.HasUnitTarget(agentIndex) ||
            _simulation.MassFlow.IsUnitSettled(agentIndex))
        {
            return 0f;
        }

        Vector2 velocity = _simulation.MassFlow.GetVelocityCmPerSecond(agentIndex);
        float authoredSpeed = _simulation.MassFlow.GetSpeedCmPerSecond(agentIndex);
        if (!(authoredSpeed > 0f))
        {
            return 0f;
        }

        float speed = velocity.Length() / authoredSpeed;
        return float.IsFinite(speed) ? MathF.Max(0f, speed) : 0f;
    }
}
