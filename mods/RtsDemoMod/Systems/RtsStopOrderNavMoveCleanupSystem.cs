using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;

namespace RtsDemoMod.Systems
{
    public sealed class RtsStopOrderNavMoveCleanupSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription Query = new QueryDescription().WithAll<Ludots.Core.Gameplay.GAS.Components.OrderBuffer, GameplayTagContainer>();
        private readonly int _stopOrderTagId;
        private readonly int _navMoveTagId;

        public RtsStopOrderNavMoveCleanupSystem(World world, int stopOrderTagId) : base(world)
        {
            _stopOrderTagId = stopOrderTagId;
            _navMoveTagId = TagRegistry.Register("Ability.Nav.Move");
        }

        public override void Update(in float dt)
        {
            if (_navMoveTagId <= 0)
            {
                return;
            }

            foreach (ref var chunk in World.Query(in Query))
            {
                var buffers = chunk.GetSpan<Ludots.Core.Gameplay.GAS.Components.OrderBuffer>();
                var tags = chunk.GetSpan<GameplayTagContainer>();
                ref var first = ref chunk.Entity(0);
                foreach (var index in chunk)
                {
                    var entity = Unsafe.Add(ref first, index);
                    if (!World.IsAlive(entity))
                    {
                        continue;
                    }

                    ref var buffer = ref buffers[index];
                    if (!buffer.HasActive || buffer.ActiveOrder.Order.OrderTagId != _stopOrderTagId)
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
