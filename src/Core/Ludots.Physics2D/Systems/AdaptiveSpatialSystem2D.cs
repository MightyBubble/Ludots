using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Physics.Broadphase;
using Ludots.Physics.Broadphase.Strategies;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Physics2D.Systems
{
    public enum CollisionPairOverflowPolicy2D
    {
        Throw = 0,
        Drop = 1
    }

    public sealed class AdaptiveSpatialSystem2D : BaseSystem<World, float>
    {
        private readonly int _maxCollisionPairs;

        private readonly BuildPhysicsWorldSystem2D _buildPhysicsWorld;
        private readonly List<(int, int)> _potentialPairs;
        private readonly Stack<Entity> _pairPool;
        private readonly Dictionary<long, Entity> _pairMap;
        private readonly HashSet<long> _usedPairKeys;
        private readonly List<long> _unusedPairKeys;

        private ISpatialPartitionStrategy _currentStrategy;

        public CollisionPairOverflowPolicy2D OverflowPolicy { get; set; } = CollisionPairOverflowPolicy2D.Throw;
        public int DroppedPairsLastUpdate { get; private set; }

        public AdaptiveSpatialSystem2D(World world, BuildPhysicsWorldSystem2D buildPhysicsWorld, int maxCollisionPairs = 100_000) : base(world)
        {
            _buildPhysicsWorld = buildPhysicsWorld ?? throw new ArgumentNullException(nameof(buildPhysicsWorld));

            _maxCollisionPairs = maxCollisionPairs;
            _potentialPairs = new List<(int, int)>(_maxCollisionPairs);
            _pairPool = new Stack<Entity>(_maxCollisionPairs);
            _pairMap = new Dictionary<long, Entity>(4096);
            _usedPairKeys = new HashSet<long>();
            _unusedPairKeys = new List<long>(4096);

            InitializeCollisionPairPool();
            SetStrategy(new SortAndSweepStrategy());
        }

        public void SetStrategy(ISpatialPartitionStrategy strategy)
        {
            ArgumentNullException.ThrowIfNull(strategy);
            _currentStrategy?.Dispose();
            _currentStrategy = strategy;
        }

        public ISpatialPartitionStrategy CurrentStrategy => _currentStrategy;

        public override void Update(in float deltaTime)
        {
            DroppedPairsLastUpdate = 0;
            var rigidBodies = _buildPhysicsWorld.RigidBodyDescriptors;
            if (rigidBodies.Count == 0) return;

            _currentStrategy.Build(CollectionsMarshal.AsSpan(rigidBodies));

            _potentialPairs.Clear();
            _currentStrategy.QueryPotentialCollisions(_potentialPairs);

            if (_potentialPairs.Count > 0)
            {
                ActivateCollisionPairs(_potentialPairs);
            }
            else
            {
                _usedPairKeys.Clear();
                RecycleUnusedPairs();
            }
        }

        private void ActivateCollisionPairs(List<(int indexA, int indexB)> pairs)
        {
            var entities = _buildPhysicsWorld.Entities;
            int needed = 0;
            _usedPairKeys.Clear();

            for (int i = 0; i < pairs.Count; i++)
            {
                var (rigidBodyIndexA, rigidBodyIndexB) = pairs[i];
                if ((uint)rigidBodyIndexA >= (uint)entities.Count) continue;
                if ((uint)rigidBodyIndexB >= (uint)entities.Count) continue;

                var entityA = entities[rigidBodyIndexA];
                var entityB = entities[rigidBodyIndexB];

                if (World.Has<SleepingTag>(entityA) && World.Has<SleepingTag>(entityB))
                {
                    continue;
                }

                if (entityB.Id < entityA.Id)
                {
                    (entityA, entityB) = (entityB, entityA);
                }

                long key = MakePairKey(entityA.Id, entityB.Id);
                if (!_usedPairKeys.Add(key))
                {
                    continue;
                }

                needed++;
                if (_pairMap.TryGetValue(key, out var pairEntity) && World.IsAlive(pairEntity))
                {
                    ref var collisionPair = ref pairEntity.Get<CollisionPair>();
                    collisionPair.IsActive = true;
                    collisionPair.EntityA = entityA;
                    collisionPair.EntityB = entityB;
                    collisionPair.ContactCount = 0;
                    collisionPair.Penetration = Fix64.Zero;
                    if (!World.Has<ActiveCollisionPairTag>(pairEntity))
                    {
                        World.Add<ActiveCollisionPairTag>(pairEntity);
                    }
                }
                else
                {
                    if (_pairPool.Count == 0)
                    {
                        if (OverflowPolicy == CollisionPairOverflowPolicy2D.Throw)
                        {
                            throw new InvalidOperationException($"Collision pair pool exhausted. Needed={needed}, Available=0, Capacity={_maxCollisionPairs}");
                        }

                        DroppedPairsLastUpdate++;
                        continue;
                    }

                    pairEntity = _pairPool.Pop();
                    ref var collisionPair = ref pairEntity.Get<CollisionPair>();
                    collisionPair.IsActive = true;
                    collisionPair.EntityA = entityA;
                    collisionPair.EntityB = entityB;
                    collisionPair.ContactCount = 0;
                    collisionPair.Penetration = Fix64.Zero;
                    collisionPair.AccumulatedNormalImpulse0 = Fix64.Zero;
                    collisionPair.AccumulatedTangentImpulse0 = Fix64.Zero;
                    collisionPair.AccumulatedNormalImpulse1 = Fix64.Zero;
                    collisionPair.AccumulatedTangentImpulse1 = Fix64.Zero;
                    World.Add<ActiveCollisionPairTag>(pairEntity);
                    _pairMap[key] = pairEntity;
                }
            }

            RecycleUnusedPairs();
        }

        private void RecycleUnusedPairs()
        {
            _unusedPairKeys.Clear();
            foreach (var kvp in _pairMap)
            {
                if (!_usedPairKeys.Contains(kvp.Key))
                {
                    _unusedPairKeys.Add(kvp.Key);
                }
            }

            for (int i = 0; i < _unusedPairKeys.Count; i++)
            {
                long key = _unusedPairKeys[i];
                if (!_pairMap.TryGetValue(key, out var entity)) continue;
                _pairMap.Remove(key);
                if (!World.IsAlive(entity)) continue;
                World.Remove<ActiveCollisionPairTag>(entity);
                ref var pair = ref entity.Get<CollisionPair>();
                pair.IsActive = false;
                pair.EntityA = default;
                pair.EntityB = default;
                _pairPool.Push(entity);
            }
        }

        private static long MakePairKey(int idA, int idB)
        {
            return ((long)idA << 32) | (uint)idB;
        }

        private void InitializeCollisionPairPool()
        {
            for (int i = 0; i < _maxCollisionPairs; i++)
            {
                var e = World.Create(new CollisionPair { IsActive = false });
                _pairPool.Push(e);
            }
        }

        public override void Dispose()
        {
            _currentStrategy?.Dispose();
            base.Dispose();
        }
    }
}
