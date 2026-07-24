using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;

namespace MobaDemoMod.GAS
{
    public sealed class StopOrderNavMoveCleanupSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription _query = new QueryDescription()
            .WithAll<OrderBuffer, GameplayTagContainer>();

        private readonly int _stopOrderTypeId;
        private readonly int _navMoveTagId;

        public StopOrderNavMoveCleanupSystem(
            World world,
            int stopOrderTypeId,
            int navMoveTagId = TagRegistry.InvalidId) : base(world)
        {
            _stopOrderTypeId = stopOrderTypeId;
            _navMoveTagId = navMoveTagId;
        }

        public override void Update(in float dt)
        {
            if (_navMoveTagId <= 0)
            {
                return;
            }

            foreach (ref var chunk in World.Query(in _query))
            {
                var buffers = chunk.GetSpan<OrderBuffer>();
                var tags = chunk.GetSpan<GameplayTagContainer>();
                ref var entityFirst = ref chunk.Entity(0);

                foreach (var index in chunk)
                {
                    var entity = Unsafe.Add(ref entityFirst, index);
                    if (!World.IsAlive(entity))
                    {
                        continue;
                    }

                    ref var buffer = ref buffers[index];
                    if (!buffer.HasActive || buffer.ActiveOrder.Order.OrderTypeId != _stopOrderTypeId)
                    {
                        continue;
                    }

                    ref var actorTags = ref tags[index];
                    if (actorTags.HasTag(_navMoveTagId))
                    {
                        actorTags.RemoveTag(_navMoveTagId);
                    }
                }
            }
        }
    }
}
