using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using Ludots.Core.Engine.Physics2D;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Physics2D.Systems
{
    public sealed class SleepingSystem : BaseSystem<World, float>
    {
        private readonly Dictionary<int, List<Entity>> _islands = new();
        private readonly HashSet<int> _islandsToWake = new();
        private readonly Physics2DSolverConfig _config;
        private readonly Physics2DTickPolicy _tickPolicy;

        private readonly QueryDescription _activeEntitiesQuery;
        private readonly QueryDescription _sleepingEntitiesQuery;
        private readonly QueryDescription _collisionPairQuery;

        public SleepingSystem(World world, Physics2DSolverConfig config, Physics2DTickPolicy tickPolicy) : base(world)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _tickPolicy = tickPolicy ?? throw new ArgumentNullException(nameof(tickPolicy));
            _activeEntitiesQuery = new QueryDescription().WithAll<Island, Motion, Mass2D>().WithNone<SleepingTag>();
            _sleepingEntitiesQuery = new QueryDescription().WithAll<Island, SleepingTag>();
            _collisionPairQuery = new QueryDescription().WithAll<CollisionPair, ActiveCollisionPairTag>();
        }

        public override void Update(in float deltaTime)
        {
            foreach (var list in _islands.Values)
            {
                list.Clear();
            }

            var collectJob = new CollectActiveIslandEntitiesJob
            {
                Islands = _islands
            };
            World.InlineEntityQuery<CollectActiveIslandEntitiesJob, Island, Motion, Mass2D>(in _activeEntitiesQuery, ref collectJob);

            foreach (var kvp in _islands)
            {
                var entities = kvp.Value;
                if (entities.Count == 0) continue;

                bool canSleep = true;
                for (int i = 0; i < entities.Count; i++)
                {
                    var entity = entities[i];
                    if (!World.TryGet(entity, out Motion motion) || motion.SleepTimer < SleepFrameThreshold)
                    {
                        canSleep = false;
                        break;
                    }
                }

                if (!canSleep) continue;

                for (int i = 0; i < entities.Count; i++)
                {
                    var entity = entities[i];
                    if (!World.Has<SleepingTag>(entity))
                    {
                        World.Add<SleepingTag>(entity);
                    }
                }
            }

            _islandsToWake.Clear();

            var wakeIslandJob = new CollectWakeIslandsJob
            {
                World = World,
                IslandsToWake = _islandsToWake
            };
            World.InlineQuery<CollectWakeIslandsJob, CollisionPair>(in _collisionPairQuery, ref wakeIslandJob);

            if (_islandsToWake.Count == 0) return;

            var wakeEntitiesJob = new WakeSleepingEntitiesJob
            {
                World = World,
                IslandsToWake = _islandsToWake
            };
            World.InlineEntityQuery<WakeSleepingEntitiesJob, Island>(in _sleepingEntitiesQuery, ref wakeEntitiesJob);
        }

        private int SleepFrameThreshold
        {
            get
            {
                if (_config.SleepTimeSeconds <= 0f)
                {
                    return 0;
                }

                int physicsHz = Math.Max(0, _tickPolicy.TargetHz);
                if (physicsHz == 0)
                {
                    return int.MaxValue;
                }

                return Math.Max(1, (int)MathF.Ceiling(_config.SleepTimeSeconds * physicsHz));
            }
        }

        private struct CollectActiveIslandEntitiesJob : IForEachWithEntity<Island, Motion, Mass2D>
        {
            public Dictionary<int, List<Entity>> Islands;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(Entity entity, ref Island island, ref Motion motion, ref Mass2D mass)
            {
                if (mass.IsStatic) return;

                if (!Islands.TryGetValue(island.IslandId, out var entityList))
                {
                    entityList = new List<Entity>();
                    Islands[island.IslandId] = entityList;
                }

                entityList.Add(entity);
            }
        }

        private struct CollectWakeIslandsJob : IForEach<CollisionPair>
        {
            public World World;
            public HashSet<int> IslandsToWake;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(ref CollisionPair pair)
            {
                if (!World.IsAlive(pair.EntityA) || !World.IsAlive(pair.EntityB)) return;
                if (pair.ContactCount == 0) return;

                bool isASleeping = World.Has<SleepingTag>(pair.EntityA);
                bool isBSleeping = World.Has<SleepingTag>(pair.EntityB);

                if (isASleeping && isBSleeping)
                {
                    return;
                }

                if (!isASleeping && isBSleeping)
                {
                    if (World.TryGet(pair.EntityB, out Island islandB))
                    {
                        IslandsToWake.Add(islandB.IslandId);
                    }
                }
                else if (isASleeping && !isBSleeping)
                {
                    if (World.TryGet(pair.EntityA, out Island islandA))
                    {
                        IslandsToWake.Add(islandA.IslandId);
                    }
                }
            }
        }

        private struct WakeSleepingEntitiesJob : IForEachWithEntity<Island>
        {
            public World World;
            public HashSet<int> IslandsToWake;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(Entity entity, ref Island island)
            {
                if (!IslandsToWake.Contains(island.IslandId)) return;

                World.Remove<SleepingTag>(entity);

                if (World.TryGet(entity, out Motion motion))
                {
                    motion.SleepTimer = 0;
                    World.Set(entity, motion);
                }
            }
        }
    }
}
