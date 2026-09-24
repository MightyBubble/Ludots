using System;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.MassNavigation.Runtime;

namespace Ludots.Core.MassNavigation.Systems;

internal sealed class MassNavigationLocomotionBlackboardSyncSystem : BaseSystem<World, float>
{
    private readonly GameEngine _engine;
    private readonly int _speedKey;
    private readonly QueryDescription _agentQuery = new QueryDescription()
        .WithAll<MassNavigationAgent, MassNavigationAgentIndex>()
        .WithNone<SuspendedTag>();

    public MassNavigationLocomotionBlackboardSyncSystem(GameEngine engine)
        : base((engine ?? throw new ArgumentNullException(nameof(engine))).World)
    {
        _engine = engine;
        _speedKey = MassNavigationBlackboardKeys.AgentLocomotionSpeed;
    }

    public override void Update(in float dt)
    {
        if (!MassNavigationIds.TryGetCurrentNavigationRuntime(_engine, out MassNavigationSimulationRuntime simulation))
        {
            return;
        }

        foreach (ref var chunk in World.Query(in _agentQuery))
        {
            if (!chunk.Has<BlackboardFloatBuffer>())
            {
                ref Entity firstEntity = ref chunk.Entity(0);
                throw new InvalidOperationException(
                    $"MassNavigation agent {firstEntity.Id} requires BlackboardFloatBuffer for locomotion speed blackboard publishing.");
            }

            ref Entity first = ref chunk.Entity(0);
            Span<MassNavigationAgentIndex> agentIndices = chunk.GetSpan<MassNavigationAgentIndex>();
            Span<BlackboardFloatBuffer> blackboards = chunk.GetSpan<BlackboardFloatBuffer>();
            foreach (int index in chunk)
            {
                int agentIndex = agentIndices[index].Value;
                if (!simulation.TryGetAgentLocomotionSpeedNormalized(agentIndex, out float speed))
                {
                    Entity entity = Unsafe.Add(ref first, index);
                    throw new InvalidOperationException(
                        $"MassNavigation agent {entity.Id} has invalid runtime index {agentIndex} for locomotion speed blackboard publishing.");
                }

                ref BlackboardFloatBuffer blackboard = ref blackboards[index];
                if (blackboard.TryGet(_speedKey, out float current) && MathF.Abs(current - speed) <= 0.0001f)
                {
                    continue;
                }

                MassNavigationBlackboardWriter.SetAgentLocomotionSpeed(ref blackboard, speed);
            }
        }
    }
}
