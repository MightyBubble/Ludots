using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Navigation2D.Systems
{
    public sealed class NavArrivalSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription _query = new QueryDescription()
            .WithAll<NavAgent2D, NavGoal2D, Position2D, GameplayTagContainer>();

        private readonly GameplayEventBus _eventBus;
        private readonly int _navMoveTagId;
        private readonly int _arrivedEventTagId;

        public NavArrivalSystem(World world, GameplayEventBus eventBus) : base(world)
        {
            _eventBus = eventBus;
            _navMoveTagId = TagRegistry.Register("Ability.Nav.Move");
            _arrivedEventTagId = TagRegistry.Register("Event.Nav.Arrived");
        }

        public override void Update(in float dt)
        {
            if (_eventBus == null) return;

            foreach (ref var chunk in World.Query(in _query))
            {
                var goals = chunk.GetSpan<NavGoal2D>();
                var positions = chunk.GetSpan<Position2D>();
                var tags = chunk.GetSpan<GameplayTagContainer>();

                ref var entityFirst = ref chunk.Entity(0);
                foreach (var index in chunk)
                {
                    var entity = System.Runtime.CompilerServices.Unsafe.Add(ref entityFirst, index);
                    if (!World.IsAlive(entity)) continue;

                    ref var goal = ref goals[index];
                    if (goal.Kind != NavGoalKind2D.Point) continue;

                    ref var t = ref tags[index];
                    if (_navMoveTagId > 0 && !t.HasTag(_navMoveTagId)) continue;

                    var delta = goal.TargetCm - positions[index].Value;
                    var d2 = delta.LengthSquared();
                    if (d2 > goal.RadiusCm * goal.RadiusCm) continue;

                    goal.Kind = NavGoalKind2D.None;
                    _eventBus.Publish(new GameplayEvent
                    {
                        TagId = _arrivedEventTagId,
                        Source = entity,
                        Target = entity,
                        Magnitude = 0f,
                    });
                }
            }
        }
    }
}
