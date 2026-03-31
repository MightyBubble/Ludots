using System;
using Arch.Core;
using Arch.System;
using RoadNetworkShowcaseMod.Gameplay;
using RoadNetworkShowcaseMod.Runtime;

namespace RoadNetworkShowcaseMod.Systems
{
    internal sealed class RoadMoveExecutionSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription Query = new QueryDescription()
            .WithAll<RoadColumnTag, RoadMoveOrderRuntime, RoadMoveExecutionIntent>();

        private readonly RoadRouteWalkStrategy _walk = new();

        public RoadMoveExecutionSystem(World world) : base(world)
        {
        }

        public override void Update(in float dt)
        {
            foreach (ref var chunk in World.Query(in Query))
            {
                Span<RoadMoveOrderRuntime> orderStates = chunk.GetSpan<RoadMoveOrderRuntime>();
                Span<RoadMoveExecutionIntent> intents = chunk.GetSpan<RoadMoveExecutionIntent>();
                ref Entity entityFirst = ref chunk.Entity(0);

                foreach (int index in chunk)
                {
                    Entity entity = System.Runtime.CompilerServices.Unsafe.Add(ref entityFirst, index);
                    ref var orderRuntime = ref orderStates[index];
                    ref var intent = ref intents[index];
                    if (orderRuntime.LifecycleState != RoadMoveLifecycleState.Active || intent.HasTarget == 0)
                    {
                        _walk.Clear(World, entity);
                        continue;
                    }

                    if (_walk.TryApply(World, entity, intent.Target, intent.SpeedCmPerSec, intent.StopRadiusCm))
                    {
                        continue;
                    }

                    orderRuntime.LifecycleState = RoadMoveLifecycleState.Failed;
                    orderRuntime.FailureReason = RoadMoveFailureReason.ExecutionUnavailable;
                    _walk.Clear(World, entity);
                }
            }
        }
    }
}
