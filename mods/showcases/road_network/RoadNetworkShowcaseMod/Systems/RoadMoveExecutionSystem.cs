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

        private readonly RoadNetworkMassNavigationRuntimeAccessor _navigation;

        public RoadMoveExecutionSystem(World world, MassNavigationRuntimeBinding binding) : base(world)
        {
            _navigation = new RoadNetworkMassNavigationRuntimeAccessor(binding);
        }

        public override void Update(in float dt)
        {
            foreach (ref var chunk in World.Query(in Query))
            {
                Span<MovePlanOrderRuntime> orderStates = chunk.GetSpan<MovePlanOrderRuntime>();
                Span<MovePlanExecutionIntent> intents = chunk.GetSpan<MovePlanExecutionIntent>();
                ref Entity entityFirst = ref chunk.Entity(0);
                MassNavigationMovePlanExecutionSink? sink = null;

                foreach (int index in chunk)
                {
                    Entity entity = System.Runtime.CompilerServices.Unsafe.Add(ref entityFirst, index);
                    ref var orderRuntime = ref orderStates[index];
                    ref var intent = ref intents[index];
                    if (orderRuntime.LifecycleState != MovePlanLifecycleState.Active || intent.HasTarget == 0)
                    {
                        sink ??= _navigation.RequireExecutionSink(nameof(RoadMoveExecutionSystem));
                        sink.Clear(World, entity);
                        continue;
                    }

                    sink ??= _navigation.RequireExecutionSink(nameof(RoadMoveExecutionSystem));
                    if (sink.TryApply(World, entity, in intent))
                    {
                        continue;
                    }

                    orderRuntime.LifecycleState = MovePlanLifecycleState.Failed;
                    orderRuntime.FailureReason = MovePlanFailureReason.ExecutionUnavailable;
                    sink.Clear(World, entity);
                }
            }
        }
    }
}
