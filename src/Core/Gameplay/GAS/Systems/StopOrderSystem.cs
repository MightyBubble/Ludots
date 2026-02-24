using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Navigation2D.Components;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public sealed class StopOrderSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription _query = new QueryDescription()
            .WithAll<GameplayTagContainer, OrderBuffer>();

        private readonly OrderTypeRegistry _orderTypeRegistry;
        private readonly int _navMoveTagId;

        public StopOrderSystem(World world, OrderTypeRegistry orderTypeRegistry) : base(world)
        {
            _orderTypeRegistry = orderTypeRegistry;
            _navMoveTagId = TagRegistry.Register("Ability.Nav.Move");
        }

        public override void Update(in float dt)
        {
            if (_orderTypeRegistry == null) return;

            foreach (ref var chunk in World.Query(in _query))
            {
                var tags = chunk.GetSpan<GameplayTagContainer>();
                ref var entityFirst = ref chunk.Entity(0);

                foreach (var index in chunk)
                {
                    var entity = Unsafe.Add(ref entityFirst, index);
                    if (!World.IsAlive(entity)) continue;

                    ref var t = ref tags[index];
                    if (!t.HasTag(OrderStateTags.Active_Stop)) continue;

                    if (_navMoveTagId > 0 && t.HasTag(_navMoveTagId))
                    {
                        t.RemoveTag(_navMoveTagId);
                    }

                    if (World.Has<AbilityExecInstance>(entity))
                    {
                        World.Remove<AbilityExecInstance>(entity);
                    }

                    if (World.Has<NavGoal2D>(entity))
                    {
                        ref var goal = ref World.Get<NavGoal2D>(entity);
                        goal.Kind = NavGoalKind2D.None;
                    }

                    OrderSubmitter.NotifyOrderComplete(World, entity, _orderTypeRegistry);
                }
            }
        }
    }
}

