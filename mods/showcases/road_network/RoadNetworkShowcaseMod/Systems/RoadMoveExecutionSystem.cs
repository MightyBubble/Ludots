using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.MovePlanning;
using RoadNetworkShowcaseMod.Runtime;

namespace RoadNetworkShowcaseMod.Systems
{
    internal sealed class RoadMoveExecutionSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription Query = new QueryDescription()
            .WithAll<RoadColumnTag, MovePlanOrderRuntime, MovePlanExecutionIntent>();

        private readonly IMovePlanExecutionSink _sink;

        public RoadMoveExecutionSystem(World world, MassNavigationSimulationRuntime simulation) : base(world)
        {
            _sink = new MassNavigationMovePlanExecutionSink(simulation ?? throw new ArgumentNullException(nameof(simulation)));
        }

        public override void Update(in float dt)
        {
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
