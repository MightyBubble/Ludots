using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using Ludots.Core.Engine.Physics2D;
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
        private readonly struct PairKey : IEquatable<PairKey>
        {
            public readonly int EntityAId;
            public readonly int EntityBId;
            public readonly byte ShapeSlotA;
            public readonly byte ShapeSlotB;

            public PairKey(int entityAId, byte shapeSlotA, int entityBId, byte shapeSlotB)
            {
                EntityAId = entityAId;
                ShapeSlotA = shapeSlotA;
                EntityBId = entityBId;
                ShapeSlotB = shapeSlotB;
            }

            public bool Equals(PairKey other)
            {
                return EntityAId == other.EntityAId &&
                    EntityBId == other.EntityBId &&
                    ShapeSlotA == other.ShapeSlotA &&
                    ShapeSlotB == other.ShapeSlotB;
            }

            public override bool Equals(object? obj)
            {
                return obj is PairKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(EntityAId, EntityBId, ShapeSlotA, ShapeSlotB);
            }
        }

        private readonly int _maxCollisionPairs;
        private readonly int _pairGrowthStep;

        private readonly BuildPhysicsWorldSystem2D _buildPhysicsWorld;
        private readonly List<(int, int)> _potentialPairs;
        private readonly Stack<Entity> _pairPool;
        private readonly Dictionary<PairKey, Entity> _pairMap;
        private readonly HashSet<PairKey> _usedPairKeys;
        private readonly List<PairKey> _unusedPairKeys;

        private ISpatialPartitionStrategy _currentStrategy = null!;
        private int _observedStaticBodyVersion = -1;
        private int _observedBroadphasePolicyVersion = -1;

        public CollisionPairOverflowPolicy2D OverflowPolicy { get; set; } = CollisionPairOverflowPolicy2D.Throw;
        public int DroppedPairsLastUpdate { get; private set; }
        public Physics2DBroadphaseStrategyKind CurrentStrategyKind { get; private set; } = Physics2DBroadphaseStrategyKind.SortAndSweep;
        public int CurrentCellSizeCm { get; private set; }

        public AdaptiveSpatialSystem2D(
            World world,
            BuildPhysicsWorldSystem2D buildPhysicsWorld,
            Physics2DSolverConfig solverConfig) : base(world)
        {
            _buildPhysicsWorld = buildPhysicsWorld ?? throw new ArgumentNullException(nameof(buildPhysicsWorld));
            ArgumentNullException.ThrowIfNull(solverConfig);

            _maxCollisionPairs = solverConfig.MaxCollisionPairs;
            _pairGrowthStep = solverConfig.CollisionPairGrowthStep;
            _potentialPairs = new List<(int, int)>(Math.Min(Math.Max(0, _maxCollisionPairs), 4096));
            _pairPool = new Stack<Entity>(Math.Max(0, solverConfig.CollisionPairInitialCapacity));
            _pairMap = new Dictionary<PairKey, Entity>(4096);
            _usedPairKeys = new HashSet<PairKey>();
            _unusedPairKeys = new List<PairKey>(4096);

            GrowCollisionPairPool(solverConfig.CollisionPairInitialCapacity);
            SetStrategy(new SortAndSweepStrategy());
        }

        public void SetStrategy(ISpatialPartitionStrategy strategy)
        {
            ArgumentNullException.ThrowIfNull(strategy);
            _currentStrategy?.Dispose();
            _currentStrategy = strategy;
        }

        public ISpatialPartitionStrategy CurrentStrategy => _currentStrategy;

        public void ApplyBroadphasePolicy(Physics2DBroadphasePolicy policy)
        {
            ArgumentNullException.ThrowIfNull(policy);
            if (_observedBroadphasePolicyVersion == policy.Version)
            {
                return;
            }

            ISpatialPartitionStrategy strategy = policy.Strategy switch
            {
                Physics2DBroadphaseStrategyKind.SortAndSweep => new SortAndSweepStrategy(),
                Physics2DBroadphaseStrategyKind.UniformGrid => new UniformGridStrategy(policy.CellSizeCm),
                _ => throw new ArgumentOutOfRangeException(nameof(policy), policy.Strategy, "Unknown Physics2D broadphase strategy.")
            };

            SetStrategy(strategy);
            CurrentStrategyKind = policy.Strategy;
            CurrentCellSizeCm = policy.CellSizeCm;
            _observedBroadphasePolicyVersion = policy.Version;
            _observedStaticBodyVersion = -1;
        }

        public override void Update(in float deltaTime)
        {
            DroppedPairsLastUpdate = 0;
            var dynamicBodies = _buildPhysicsWorld.DynamicRigidBodyDescriptors;
            var staticBodies = _buildPhysicsWorld.StaticRigidBodyDescriptors;
            bool rebuildStatic = _observedStaticBodyVersion != _buildPhysicsWorld.StaticBodyVersion;

            _currentStrategy.Build(
                CollectionsMarshal.AsSpan(dynamicBodies),
                CollectionsMarshal.AsSpan(staticBodies),
                rebuildStatic);
            _observedStaticBodyVersion = _buildPhysicsWorld.StaticBodyVersion;

            if (dynamicBodies.Count == 0)
            {
                _potentialPairs.Clear();
                _usedPairKeys.Clear();
                RecycleUnusedPairs();
                return;
            }

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
            var shapeSlots = _buildPhysicsWorld.ShapeSlots;
            int needed = 0;
            _usedPairKeys.Clear();

            for (int i = 0; i < pairs.Count; i++)
            {
                var (rigidBodyIndexA, rigidBodyIndexB) = pairs[i];
                if ((uint)rigidBodyIndexA >= (uint)entities.Count) continue;
                if ((uint)rigidBodyIndexB >= (uint)entities.Count) continue;

                var entityA = entities[rigidBodyIndexA];
                var entityB = entities[rigidBodyIndexB];
                if (entityA == entityB)
                {
                    continue;
                }

                byte shapeSlotA = shapeSlots[rigidBodyIndexA];
                byte shapeSlotB = shapeSlots[rigidBodyIndexB];
                if (!_buildPhysicsWorld.TryGetSnapshot(rigidBodyIndexA, out var snapshotA) ||
                    !_buildPhysicsWorld.TryGetSnapshot(rigidBodyIndexB, out var snapshotB))
                {
                    continue;
                }

                if (snapshotA.IsSleeping != 0 && snapshotB.IsSleeping != 0)
                {
                    continue;
                }

                // Kinematic bodies only ever solve against dynamic bodies (issue #732):
                // kinematic×kinematic and kinematic×static pairs have no solver meaning.
                if ((snapshotA.Mass.IsKinematic || snapshotB.Mass.IsKinematic) &&
                    !(snapshotA.Mass.IsDynamic || snapshotB.Mass.IsDynamic))
                {
                    continue;
                }

                if (entityB.Id < entityA.Id)
                {
                    (entityA, entityB) = (entityB, entityA);
                    (shapeSlotA, shapeSlotB) = (shapeSlotB, shapeSlotA);
                    (snapshotA, snapshotB) = (snapshotB, snapshotA);
                }

                var key = new PairKey(entityA.Id, shapeSlotA, entityB.Id, shapeSlotB);
                if (!_usedPairKeys.Add(key))
                {
                    continue;
                }

                needed++;
                if (_pairMap.TryGetValue(key, out var pairEntity) && World.IsAlive(pairEntity))
                {
                    ref var collisionPair = ref pairEntity.Get<CollisionPair>();
                    ResetActivePair(ref collisionPair, in snapshotA, in snapshotB);
                    if (!World.Has<ActiveCollisionPairTag>(pairEntity))
                    {
                        World.Add<ActiveCollisionPairTag>(pairEntity);
                    }
                }
                else
                {
                    if (_pairPool.Count == 0)
                    {
                        GrowCollisionPairPool(_pairGrowthStep);
                        if (_pairPool.Count == 0 && OverflowPolicy == CollisionPairOverflowPolicy2D.Throw)
                        {
                            throw new InvalidOperationException($"Collision pair pool exhausted. Needed={needed}, Available=0, Capacity={_maxCollisionPairs}");
                        }

                        if (_pairPool.Count == 0)
                        {
                            DroppedPairsLastUpdate++;
                            continue;
                        }
                    }

                    pairEntity = _pairPool.Pop();
                    ref var collisionPair = ref pairEntity.Get<CollisionPair>();
                    ResetActivePair(ref collisionPair, in snapshotA, in snapshotB);
                    collisionPair.AccumulatedNormalImpulse0 = Fix64.Zero;
                    collisionPair.AccumulatedTangentImpulse0 = Fix64.Zero;
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
                PairKey key = _unusedPairKeys[i];
                if (!_pairMap.TryGetValue(key, out var entity)) continue;
                _pairMap.Remove(key);
                if (!World.IsAlive(entity)) continue;
                World.Remove<ActiveCollisionPairTag>(entity);
                ref var pair = ref entity.Get<CollisionPair>();
                pair.IsActive = false;
                pair.EntityA = default;
                pair.EntityB = default;
                pair.ShapeSlotA = 0;
                pair.ShapeSlotB = 0;
                _pairPool.Push(entity);
            }
        }

        private void ResetActivePair(
            ref CollisionPair pair,
            in BuildPhysicsWorldSystem2D.BodySnapshot snapshotA,
            in BuildPhysicsWorldSystem2D.BodySnapshot snapshotB)
        {
            pair.IsActive = true;
            pair.EntityA = snapshotA.Entity;
            pair.EntityB = snapshotB.Entity;
            pair.ShapeSlotA = snapshotA.ShapeSlot;
            pair.ShapeSlotB = snapshotB.ShapeSlot;
            pair.PositionA = snapshotA.Position;
            pair.PositionB = snapshotB.Position;
            pair.RotationA = snapshotA.Rotation;
            pair.RotationB = snapshotB.Rotation;
            pair.ColliderA = snapshotA.Collider;
            pair.ColliderB = snapshotB.Collider;
            pair.VelocityA = snapshotA.Velocity;
            pair.VelocityB = snapshotB.Velocity;
            pair.MassA = snapshotA.Mass;
            pair.MassB = snapshotB.Mass;
            pair.MaterialA = snapshotA.Material;
            pair.MaterialB = snapshotB.Material;
            pair.HasMaterialA = snapshotA.HasMaterial;
            pair.HasMaterialB = snapshotB.HasMaterial;
            pair.IsSleepingA = snapshotA.IsSleeping;
            pair.IsSleepingB = snapshotB.IsSleeping;
            pair.IslandA = snapshotA.IslandId;
            pair.IslandB = snapshotB.IslandId;
            pair.ContactCount = 0;
            pair.Penetration = Fix64.Zero;
        }

        private void GrowCollisionPairPool(int requestedCount)
        {
            if (requestedCount <= 0)
            {
                return;
            }

            int existing = _pairPool.Count + _pairMap.Count;
            int count = Math.Min(requestedCount, _maxCollisionPairs - existing);
            for (int i = 0; i < count; i++)
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
