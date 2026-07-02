using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Arch.Core;
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

            var collectJob = new CollectDynamicEntitiesJob
            {
                EntityToIndex = _entityToIndex,
                IndexToEntity = _indexToEntity
            };
            World.InlineEntityQuery<CollectDynamicEntitiesJob, Mass2D>(in _dynamicEntitiesQuery, ref collectJob);
            int entityCount = collectJob.EntityCount;

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

            var unionJob = new UnionPairsJob
            {
                World = World,
                EntityToIndex = _entityToIndex,
                Parent = _parent,
                Rank = _rank
            };
            World.InlineQuery<UnionPairsJob, CollisionPair>(in _collisionPairQuery, ref unionJob);

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

        private struct CollectDynamicEntitiesJob : IForEachWithEntity<Mass2D>
        {
            public Dictionary<Entity, int> EntityToIndex;
            public List<Entity> IndexToEntity;
            public int EntityCount;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(Entity entity, ref Mass2D mass)
            {
                if (mass.IsStatic) return;
                EntityToIndex[entity] = EntityCount;
                IndexToEntity.Add(entity);
                EntityCount++;
            }
        }

        private struct UnionPairsJob : IForEach<CollisionPair>
        {
            public World World;
            public Dictionary<Entity, int> EntityToIndex;
            public int[] Parent;
            public int[] Rank;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(ref CollisionPair pair)
            {
                if (!World.IsAlive(pair.EntityA) || !World.IsAlive(pair.EntityB)) return;
                if (pair.ContactCount == 0) return;

                if (!EntityToIndex.TryGetValue(pair.EntityA, out int indexA) ||
                    !EntityToIndex.TryGetValue(pair.EntityB, out int indexB))
                {
                    return;
                }

                Union(indexA, indexB);
            }

            private int Find(int x)
            {
                if (Parent[x] != x)
                {
                    Parent[x] = Find(Parent[x]);
                }
                return Parent[x];
            }

            private void Union(int x, int y)
            {
                int rootX = Find(x);
                int rootY = Find(y);
                if (rootX == rootY) return;

                if (Rank[rootX] < Rank[rootY])
                {
                    Parent[rootX] = rootY;
                }
                else if (Rank[rootX] > Rank[rootY])
                {
                    Parent[rootY] = rootX;
                }
                else
                {
                    Parent[rootY] = rootX;
                    Rank[rootX]++;
                }
            }
        }
    }
}
