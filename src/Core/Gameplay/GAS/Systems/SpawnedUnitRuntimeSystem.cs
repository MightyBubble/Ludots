using System;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public sealed class SpawnedUnitRuntimeSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription _query = new QueryDescription().WithAll<SpawnedUnitState>();
        private readonly EffectRequestQueue _effectRequests;

        public SpawnedUnitRuntimeSystem(World world, EffectRequestQueue effectRequests) : base(world)
        {
            _effectRequests = effectRequests;
        }

        public override void Update(in float dt)
        {
            World.Query(in _query, (Entity e, ref SpawnedUnitState spawn) =>
            {
                if (!World.IsAlive(spawn.Spawner))
                {
                    World.Destroy(e);
                    return;
                }

                Fix64Vec2 basePos = default;
                if (World.Has<WorldPositionCm>(spawn.Spawner))
                {
                    basePos = World.Get<WorldPositionCm>(spawn.Spawner).Value;
                }

                var offset = ComputeOffsetCm(e.Id, spawn.OffsetRadius);
                var unitPos = basePos + Fix64Vec2.FromInt(offset.X, offset.Y);

                Entity unit = World.Create(
                    new Name { Value = $"Unit:{UnitTypeRegistry.GetName(spawn.UnitTypeId)}" },
                    new WorldPositionCm { Value = unitPos },
                    new PreviousWorldPositionCm { Value = unitPos },
                    new AttributeBuffer()
                );

                if (World.Has<Team>(spawn.Spawner))
                {
                    World.Add(unit, World.Get<Team>(spawn.Spawner));
                }

                if (_effectRequests != null && spawn.OnSpawnEffectTemplateId > 0)
                {
                    _effectRequests.Publish(new EffectRequest
                    {
                        RootId = 0,
                        Source = spawn.Spawner,
                        Target = unit,
                        TargetContext = default,
                        TemplateId = spawn.OnSpawnEffectTemplateId
                    });
                }

                World.Destroy(e);
            });
        }

        private static (int X, int Y) ComputeOffsetCm(int seed, int radiusCm)
        {
            if (radiusCm <= 0) return (0, 0);

            unchecked
            {
                uint x = (uint)seed;
                x ^= x << 13;
                x ^= x >> 17;
                x ^= x << 5;

                float angle = (x % 360u) * (MathF.PI / 180f);
                float r = radiusCm * (0.5f + ((x >> 9) & 1023) / 2047f);

                int ox = (int)MathF.Round(MathF.Cos(angle) * r);
                int oy = (int)MathF.Round(MathF.Sin(angle) * r);
                return (ox, oy);
            }
        }
    }
}
