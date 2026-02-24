using System.Collections.Generic;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Physics2D.Systems
{
    public sealed class SleepingSystem : BaseSystem<World, float>
    {
        private const int TimeToSleep = 60;

        private readonly Dictionary<int, List<Entity>> _islands = new();
        private readonly HashSet<int> _islandsToWake = new();

        private readonly QueryDescription _activeEntitiesQuery;
        private readonly QueryDescription _sleepingEntitiesQuery;
        private readonly QueryDescription _collisionPairQuery;

        public SleepingSystem(World world) : base(world)
        {
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

            World.Query(in _activeEntitiesQuery, (Entity entity, ref Island island, ref Mass2D mass) =>
            {
                if (mass.IsStatic) return;

                if (!_islands.TryGetValue(island.IslandId, out var entityList))
                {
                    entityList = new List<Entity>();
                    _islands[island.IslandId] = entityList;
                }

                entityList.Add(entity);
            });

            foreach (var kvp in _islands)
            {
                var entities = kvp.Value;
                if (entities.Count == 0) continue;

                bool canSleep = true;
                for (int i = 0; i < entities.Count; i++)
                {
                    var entity = entities[i];
                    if (!World.TryGet(entity, out Motion motion) || motion.SleepTimer < TimeToSleep)
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

            World.Query(in _collisionPairQuery, (ref CollisionPair pair) =>
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
                        _islandsToWake.Add(islandB.IslandId);
                    }
                }
                else if (isASleeping && !isBSleeping)
                {
                    if (World.TryGet(pair.EntityA, out Island islandA))
                    {
                        _islandsToWake.Add(islandA.IslandId);
                    }
                }
            });

            if (_islandsToWake.Count == 0) return;

            World.Query(in _sleepingEntitiesQuery, (Entity entity, ref Island island) =>
            {
                if (!_islandsToWake.Contains(island.IslandId)) return;

                World.Remove<SleepingTag>(entity);

                if (World.TryGet(entity, out Motion motion))
                {
                    motion.SleepTimer = 0;
                    World.Set(entity, motion);
                }
            });
        }
    }
}
