using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Physics2D.Systems
{
    public sealed class BuildIslandsSystem : BaseSystem<World, float>
    {
        private int[] _parent = Array.Empty<int>();
        private int[] _rank = Array.Empty<int>();

        private readonly Dictionary<Entity, int> _entityToIndex = new();
        private readonly List<Entity> _indexToEntity = new();
        private readonly Dictionary<int, int> _rootToIslandId = new();

        private readonly QueryDescription _collisionPairQuery;
        private readonly QueryDescription _dynamicEntitiesQuery;

        public BuildIslandsSystem(World world) : base(world)
        {
            _collisionPairQuery = new QueryDescription().WithAll<CollisionPair, ActiveCollisionPairTag>();
            _dynamicEntitiesQuery = new QueryDescription().WithAll<Mass2D>().WithNone<SleepingTag>();
        }

        public override void Update(in float deltaTime)
        {
            _entityToIndex.Clear();
            _indexToEntity.Clear();

            int entityCount = 0;
            World.Query(in _dynamicEntitiesQuery, (Entity entity, ref Mass2D mass) =>
            {
                if (mass.IsStatic) return;
                _entityToIndex[entity] = entityCount;
                _indexToEntity.Add(entity);
                entityCount++;
            });

            if (entityCount == 0) return;

            if (_parent.Length < entityCount)
            {
                _parent = new int[entityCount * 2];
                _rank = new int[entityCount * 2];
            }

            for (int i = 0; i < entityCount; i++)
            {
                _parent[i] = i;
                _rank[i] = 0;
            }

            World.Query(in _collisionPairQuery, (ref CollisionPair pair) =>
            {
                if (!World.IsAlive(pair.EntityA) || !World.IsAlive(pair.EntityB)) return;
                if (pair.ContactCount == 0) return;

                if (!_entityToIndex.TryGetValue(pair.EntityA, out int indexA) ||
                    !_entityToIndex.TryGetValue(pair.EntityB, out int indexB))
                {
                    return;
                }

                Union(indexA, indexB);
            });

            _rootToIslandId.Clear();
            int nextIslandId = 0;

            for (int i = 0; i < entityCount; i++)
            {
                int root = Find(i);
                if (!_rootToIslandId.TryGetValue(root, out int islandId))
                {
                    islandId = nextIslandId++;
                    _rootToIslandId[root] = islandId;
                }

                Entity entity = _indexToEntity[i];
                if (World.TryGet(entity, out Island island))
                {
                    island.IslandId = islandId;
                    World.Set(entity, island);
                }
                else
                {
                    World.Add(entity, new Island { IslandId = islandId });
                }
            }
        }

        private int Find(int x)
        {
            if (_parent[x] != x)
            {
                _parent[x] = Find(_parent[x]);
            }
            return _parent[x];
        }

        private void Union(int x, int y)
        {
            int rootX = Find(x);
            int rootY = Find(y);
            if (rootX == rootY) return;

            if (_rank[rootX] < _rank[rootY])
            {
                _parent[rootX] = rootY;
            }
            else if (_rank[rootX] > _rank[rootY])
            {
                _parent[rootY] = rootX;
            }
            else
            {
                _parent[rootY] = rootX;
                _rank[rootX]++;
            }
        }
    }
}
