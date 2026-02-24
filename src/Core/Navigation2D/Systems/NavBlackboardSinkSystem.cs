using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Gameplay.GAS.Registry;

namespace Ludots.Core.Navigation2D.Systems
{
    public sealed class NavBlackboardSinkSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription _query = new QueryDescription()
            .WithAll<NavAgent2D, GameplayTagContainer, AbilityExecInstance>();

        private readonly int _navMoveTagId;
        private readonly Fix64 _defaultRadiusCm;

        public NavBlackboardSinkSystem(World world, int defaultRadiusCm = 20) : base(world)
        {
            _navMoveTagId = TagRegistry.Register("Ability.Nav.Move");
            _defaultRadiusCm = Fix64.FromInt(defaultRadiusCm);
        }

        public override void Update(in float dt)
        {
            foreach (ref var chunk in World.Query(in _query))
            {
                var tags = chunk.GetSpan<GameplayTagContainer>();
                var execs = chunk.GetSpan<AbilityExecInstance>();

                ref var entityFirst = ref chunk.Entity(0);
                foreach (var index in chunk)
                {
                    var entity = Unsafe.Add(ref entityFirst, index);
                    if (!World.IsAlive(entity)) continue;

                    ref var t = ref tags[index];
                    ref var exec = ref execs[index];

                    bool moving = _navMoveTagId > 0 && t.HasTag(_navMoveTagId) && exec.HasTargetPos != 0;
                    if (moving)
                    {
                        if (!World.Has<NavGoal2D>(entity))
                        {
                            World.Add(entity, new NavGoal2D());
                        }

                        ref var goal = ref World.Get<NavGoal2D>(entity);
                        goal.Kind = NavGoalKind2D.Point;
                        goal.TargetCm = exec.TargetPosCm;
                        if (goal.RadiusCm <= Fix64.Zero)
                        {
                            goal.RadiusCm = _defaultRadiusCm;
                        }
                    }
                    else
                    {
                        if (World.Has<NavGoal2D>(entity))
                        {
                            ref var goal = ref World.Get<NavGoal2D>(entity);
                            goal.Kind = NavGoalKind2D.None;
                        }
                    }
                }
            }
        }
    }
}
