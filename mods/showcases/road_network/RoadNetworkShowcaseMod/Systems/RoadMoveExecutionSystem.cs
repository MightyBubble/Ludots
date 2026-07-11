using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.MovePlanning;
using RoadNetworkShowcaseMod.Runtime;

namespace RoadNetworkShowcaseMod.Systems
{
    internal sealed class RoadMoveExecutionSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription Query = new QueryDescription()
            .WithAll<RoadColumnTag, MovePlanOrderRuntime, MovePlanExecutionIntent>()
            .WithNone<SuspendedTag>();

        private readonly IMovePlanExecutionSink _sink;
        private readonly MassNavigationRuntimeBinding _binding;

        public RoadMoveExecutionSystem(World world, MassNavigationRuntimeBinding binding) : base(world)
        {
            _binding = binding ?? throw new ArgumentNullException(nameof(binding));
            _sink = new MassNavigationMovePlanExecutionSink(_binding);
        }

        public override void Update(in float dt)
        {
            if (!RoadNetworkShowcaseIds.TryResolveSimulation(_binding, out _))
            {
                return;
            }

            foreach (ref var chunk in World.Query(in Query))
            {
                Span<MovePlanOrderRuntime> orderStates = chunk.GetSpan<MovePlanOrderRuntime>();
                Span<MovePlanExecutionIntent> intents = chunk.GetSpan<MovePlanExecutionIntent>();
                ref Entity entityFirst = ref chunk.Entity(0);

                foreach (int index in chunk)
                {
                    Entity entity = System.Runtime.CompilerServices.Unsafe.Add(ref entityFirst, index);
                    ref var orderRuntime = ref orderStates[index];
                    ref var intent = ref intents[index];
                    if (orderRuntime.LifecycleState != MovePlanLifecycleState.Active || intent.HasTarget == 0)
                    {
                        _sink.Clear(World, entity);
                        continue;
                    }

                    if (_sink.TryApply(World, entity, in intent))
                    {
                        continue;
                    }

                    orderRuntime.LifecycleState = MovePlanLifecycleState.Failed;
                    orderRuntime.FailureReason = MovePlanFailureReason.ExecutionUnavailable;
                    _sink.Clear(World, entity);
                }
            }
        }
    }
}
