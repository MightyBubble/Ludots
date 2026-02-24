using System;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public sealed class AbilityMoveWorldCmSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription _query = new QueryDescription()
            .WithAll<WorldPositionCm, GameplayTagContainer, AbilityExecInstance>();

        private readonly GameplayEventBus _eventBus;
        private readonly int _navMoveTagId;
        private readonly int _arrivedEventTagId;
        private readonly float _speedCmPerSec;
        private readonly float _stopRadiusCm;

        public AbilityMoveWorldCmSystem(World world, GameplayEventBus eventBus, float speedCmPerSec, float stopRadiusCm) : base(world)
        {
            _eventBus = eventBus;
            _navMoveTagId = TagRegistry.Register("Ability.Nav.Move");
            _arrivedEventTagId = TagRegistry.Register("Event.Nav.Arrived");
            _speedCmPerSec = Math.Max(0f, speedCmPerSec);
            _stopRadiusCm = Math.Max(0f, stopRadiusCm);
        }

        public override void Update(in float dt)
        {
            if (_eventBus == null) return;
            if (_speedCmPerSec <= 0f) return;

            float stepCm = _speedCmPerSec * Math.Max(0f, dt);
            if (stepCm <= 0f) return;

            foreach (ref var chunk in World.Query(in _query))
            {
                var pos = chunk.GetSpan<WorldPositionCm>();
                var tags = chunk.GetSpan<GameplayTagContainer>();
                var execs = chunk.GetSpan<AbilityExecInstance>();

                ref var entityFirst = ref chunk.Entity(0);
                foreach (var index in chunk)
                {
                    var entity = Unsafe.Add(ref entityFirst, index);
                    if (!World.IsAlive(entity)) continue;

                    ref var t = ref tags[index];
                    if (_navMoveTagId <= 0 || !t.HasTag(_navMoveTagId)) continue;

                    ref var exec = ref execs[index];
                    if (exec.HasTargetPos == 0) continue;

                    ref var wp = ref pos[index];
                    Fix64Vec2 current = wp.Value;
                    Fix64Vec2 target = exec.TargetPosCm;

                    var delta = target - current;
                    float dx = delta.X.ToFloat();
                    float dy = delta.Y.ToFloat();
                    float dist = MathF.Sqrt(dx * dx + dy * dy);

                    if (dist <= _stopRadiusCm || dist <= stepCm || dist <= 1f)
                    {
                        wp.Value = target;
                        _eventBus.Publish(new GameplayEvent
                        {
                            TagId = _arrivedEventTagId,
                            Source = entity,
                            Target = entity,
                            Magnitude = 0f,
                        });
                        continue;
                    }

                    float inv = 1f / dist;
                    float ux = dx * inv;
                    float uy = dy * inv;
                    wp.Value = current + Fix64Vec2.FromFloat(ux * stepCm, uy * stepCm);
                }
            }
        }
    }
}

