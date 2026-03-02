using System.Collections.Generic;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public sealed class SpawnedUnitRuntimeSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription _query = new QueryDescription().WithAll<SpawnedUnitState>();
        private readonly EffectRequestQueue _effectRequests;
        private readonly List<Entity> _toDestroy = new();
        private readonly List<PendingSpawn> _pendingSpawns = new();

        public SpawnedUnitRuntimeSystem(World world, EffectRequestQueue effectRequests) : base(world)
        {
            _effectRequests = effectRequests;
        }

        public override void Update(in float dt)
        {
            _toDestroy.Clear();
            _pendingSpawns.Clear();

            World.Query(in _query, (Entity e, ref SpawnedUnitState spawn) =>
            {
                if (!World.IsAlive(spawn.Spawner))
                {
                    _toDestroy.Add(e);
                    return;
                }

                Fix64Vec2 basePos = default;
                if (World.Has<WorldPositionCm>(spawn.Spawner))
                {
                    basePos = World.Get<WorldPositionCm>(spawn.Spawner).Value;
                }

                var offset = ComputeOffsetCm(e.Id, spawn.OffsetRadius);
                var unitPos = basePos + offset;

                bool hasTeam = World.Has<Team>(spawn.Spawner);
                Team spawnerTeam = hasTeam ? World.Get<Team>(spawn.Spawner) : default;

                _pendingSpawns.Add(new PendingSpawn
                {
                    UnitPos = unitPos,
                    Spawner = spawn.Spawner,
                    OnSpawnEffectTemplateId = spawn.OnSpawnEffectTemplateId,
                    HasTeam = hasTeam,
                    SpawnerTeam = spawnerTeam,
                });

                _toDestroy.Add(e);
            });

            // Deferred entity creation (outside query)
            for (int i = 0; i < _pendingSpawns.Count; i++)
            {
                var pending = _pendingSpawns[i];

                Entity unit = World.Create(
                    new WorldPositionCm { Value = pending.UnitPos },
                    new PreviousWorldPositionCm { Value = pending.UnitPos },
                    new AttributeBuffer()
                );

                if (pending.HasTeam)
                {
                    World.Add(unit, pending.SpawnerTeam);
                }

                if (_effectRequests != null && pending.OnSpawnEffectTemplateId > 0)
                {
                    _effectRequests.Publish(new EffectRequest
                    {
                        RootId = 0,
                        Source = pending.Spawner,
                        Target = unit,
                        TargetContext = default,
                        TemplateId = pending.OnSpawnEffectTemplateId
                    });
                }
            }

            // Deferred entity destruction (outside query)
            for (int i = 0; i < _toDestroy.Count; i++)
            {
                if (World.IsAlive(_toDestroy[i]))
                    World.Destroy(_toDestroy[i]);
            }
        }

        private static Fix64Vec2 ComputeOffsetCm(int seed, int radiusCm)
        {
            if (radiusCm <= 0) return Fix64Vec2.Zero;

            unchecked
            {
                uint x = (uint)seed;
                x ^= x << 13;
                x ^= x >> 17;
                x ^= x << 5;

                Fix64 angleDeg = Fix64.FromInt((int)(x % 360u));
                Fix64 angleRad = angleDeg * Fix64.Deg2Rad;

                Fix64 fraction = Fix64.HalfValue + Fix64.FromInt((int)((x >> 9) & 1023)) / Fix64.FromInt(2047);
                Fix64 r = Fix64.FromInt(radiusCm) * fraction;

                Fix64 ox = r * Fix64Math.Cos(angleRad);
                Fix64 oy = r * Fix64Math.Sin(angleRad);
                return new Fix64Vec2(ox, oy);
            }
        }

        private struct PendingSpawn
        {
            public Fix64Vec2 UnitPos;
            public Entity Spawner;
            public int OnSpawnEffectTemplateId;
            public bool HasTeam;
            public Team SpawnerTeam;
        }
    }
}
