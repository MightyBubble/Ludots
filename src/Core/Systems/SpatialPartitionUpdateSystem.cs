using System;
using System.Runtime.CompilerServices;
using Arch.Buffer;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Spatial;

namespace Ludots.Core.Systems
{
    public sealed class SpatialPartitionUpdateSystem : BaseSystem<World, float>, ISpatialPartitionMembership
    {
        private const int SpatialCellEdgeEpsilonCm = 1;

        private ISpatialPartitionWorld _partition;
        private WorldSizeSpec _spec;
        private readonly QueryDescription _trackedQuery = new QueryDescription()
            .WithAll<WorldPositionCm, SpatialCellRef>()
            .WithNone<PresentationStaticTransform, SpatialPartitionExcluded, PresentationDestroyPending>();
        private readonly QueryDescription _untrackedQuery = new QueryDescription()
            .WithAll<WorldPositionCm>()
            .WithNone<SpatialCellRef, SpatialPartitionExcluded, PresentationDestroyPending>();
        private readonly QueryDescription _excludedTrackedQuery = new QueryDescription()
            .WithAll<SpatialPartitionExcluded, SpatialCellRef>();
        private readonly QueryDescription _destroyPendingTrackedQuery = new QueryDescription()
            .WithAll<PresentationDestroyPending, SpatialCellRef>();

        private readonly CommandBuffer _commandBuffer = new();

        public SpatialPartitionUpdateSystem(World world, ISpatialPartitionWorld partition, WorldSizeSpec spec) : base(world)
        {
            _partition = partition ?? throw new ArgumentNullException(nameof(partition));
            _spec = spec;
        }

        /// <summary>
        /// Hot-swap the spatial partition and world spec when the spatial config changes (e.g. on map load).
        /// Called by GameEngine.ApplyMapSpatialConfig to prevent stale references.
        /// </summary>
        internal void SetPartition(ISpatialPartitionWorld partition, WorldSizeSpec spec)
        {
            _partition = partition ?? throw new ArgumentNullException(nameof(partition));
            _spec = spec;
        }

        public override void Update(in float dt)
        {
            RemoveSpatialRefs(in _excludedTrackedQuery);
            RemoveSpatialRefs(in _destroyPendingTrackedQuery);
            AddMissingSpatialRefs();

            var moveJob = new MoveJob { Partition = _partition, Spec = _spec };
            World.InlineEntityQuery<MoveJob, WorldPositionCm, SpatialCellRef>(in _trackedQuery, ref moveJob);
        }

        public void Synchronize(Entity entity)
        {
            RequireLiveEntity(entity);
            if (!World.TryGet(entity, out WorldPositionCm position) ||
                World.Has<PresentationStaticTransform>(entity) ||
                World.Has<SpatialPartitionExcluded>(entity) ||
                World.Has<PresentationDestroyPending>(entity))
            {
                Remove(entity);
                return;
            }

            if (World.Has<SpatialCellRef>(entity))
            {
                ref SpatialCellRef cellRef = ref World.Get<SpatialCellRef>(entity);
                SynchronizeTracked(_partition, in _spec, entity, in position, ref cellRef);
                return;
            }

            SpatialCellRef created = CreateMembership(_partition, in _spec, entity, in position);
            World.Add(entity, in created);
        }

        public void Remove(Entity entity)
        {
            RequireLiveEntity(entity);
            if (!World.TryGet(entity, out SpatialCellRef cellRef))
            {
                return;
            }

            RemoveTracked(_partition, entity, in cellRef);
            World.Remove<SpatialCellRef>(entity);
        }

        private void RemoveSpatialRefs(in QueryDescription queryDescription)
        {
            foreach (ref var chunk in World.Query(in queryDescription))
            {
                ref var entityFirst = ref chunk.Entity(0);
                var refs = chunk.GetSpan<SpatialCellRef>();

                foreach (var index in chunk)
                {
                    var entity = Unsafe.Add(ref entityFirst, index);
                    ref SpatialCellRef cellRef = ref refs[index];
                    RemoveTracked(_partition, entity, in cellRef);

                    _commandBuffer.Remove<SpatialCellRef>(in entity);
                }
            }

            if (_commandBuffer.Size > 0)
            {
                _commandBuffer.Playback(World);
            }
        }

        private void AddMissingSpatialRefs()
        {
            foreach (ref var chunk in World.Query(in _untrackedQuery))
            {
                ref var entityFirst = ref chunk.Entity(0);
                var positions = chunk.GetSpan<WorldPositionCm>();

                foreach (var index in chunk)
                {
                    var entity = Unsafe.Add(ref entityFirst, index);
                    SpatialCellRef created = CreateMembership(
                        _partition,
                        in _spec,
                        entity,
                        in positions[index]);
                    _commandBuffer.Add(entity, created);
                }
            }

            if (_commandBuffer.Size > 0)
            {
                _commandBuffer.Playback(World);
            }
        }

        private struct MoveJob : IForEachWithEntity<WorldPositionCm, SpatialCellRef>
        {
            public ISpatialPartitionWorld Partition;
            public WorldSizeSpec Spec;

            public void Update(Entity entity, ref WorldPositionCm pos, ref SpatialCellRef cellRef)
            {
                SynchronizeTracked(Partition, in Spec, entity, in pos, ref cellRef);
            }
        }

        private static SpatialCellRef CreateMembership(
            ISpatialPartitionWorld partition,
            in WorldSizeSpec spec,
            Entity entity,
            in WorldPositionCm position)
        {
            WorldCmInt2 worldCm = position.Value.ToWorldCmInt2();
            if (!spec.Contains(worldCm)) ThrowWorldPositionOutOfBounds(entity, worldCm, spec);
            (int cellX, int cellY) = WorldToCell(worldCm, spec.GridCellSizeCm);
            partition.Add(entity, cellX, cellY);
            return new SpatialCellRef { CellX = cellX, CellY = cellY, Initialized = 1 };
        }

        private static void SynchronizeTracked(
            ISpatialPartitionWorld partition,
            in WorldSizeSpec spec,
            Entity entity,
            in WorldPositionCm position,
            ref SpatialCellRef cellRef)
        {
            WorldCmInt2 worldCm = position.Value.ToWorldCmInt2();
            if (!spec.Contains(worldCm)) ThrowWorldPositionOutOfBounds(entity, worldCm, spec);

            if (cellRef.Initialized == 0)
            {
                (int initialCellX, int initialCellY) = WorldToCell(worldCm, spec.GridCellSizeCm);
                partition.Add(entity, initialCellX, initialCellY);
                cellRef.CellX = initialCellX;
                cellRef.CellY = initialCellY;
                cellRef.Initialized = 1;
                return;
            }

            if (IsInsideCell(worldCm, cellRef.CellX, cellRef.CellY, spec.GridCellSizeCm))
            {
                return;
            }

            (int nextCellX, int nextCellY) = WorldToCell(worldCm, spec.GridCellSizeCm);
            if (cellRef.CellX == nextCellX && cellRef.CellY == nextCellY) return;

            partition.Remove(entity, cellRef.CellX, cellRef.CellY);
            partition.Add(entity, nextCellX, nextCellY);
            cellRef.CellX = nextCellX;
            cellRef.CellY = nextCellY;
        }

        private static void RemoveTracked(
            ISpatialPartitionWorld partition,
            Entity entity,
            in SpatialCellRef cellRef)
        {
            if (cellRef.Initialized != 0)
            {
                partition.Remove(entity, cellRef.CellX, cellRef.CellY);
            }
        }

        private static (int x, int y) WorldToCell(in WorldCmInt2 world, int cellSizeCm)
        {
            return (MathUtil.FloorDiv(world.X, cellSizeCm), MathUtil.FloorDiv(world.Y, cellSizeCm));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsInsideCell(in WorldCmInt2 world, int cellX, int cellY, int cellSizeCm)
        {
            long minX = (long)cellX * cellSizeCm;
            long minY = (long)cellY * cellSizeCm;
            long maxX = minX + cellSizeCm;
            long maxY = minY + cellSizeCm;
            return world.X >= minX + SpatialCellEdgeEpsilonCm &&
                   world.X < maxX - SpatialCellEdgeEpsilonCm &&
                   world.Y >= minY + SpatialCellEdgeEpsilonCm &&
                   world.Y < maxY - SpatialCellEdgeEpsilonCm;
        }

        private static void ThrowWorldPositionOutOfBounds(Entity entity, in WorldCmInt2 worldCm, in WorldSizeSpec spec)
        {
            throw new InvalidOperationException(
                $"SPATIAL.ERR.WorldPositionOutOfBounds entity={entity.Id}:{entity.WorldId} pos=({worldCm.X},{worldCm.Y}) bounds={spec.Bounds} cell={spec.GridCellSizeCm}");
        }

        private void RequireLiveEntity(Entity entity)
        {
            if (entity == Entity.Null || !World.IsAlive(entity))
            {
                throw new InvalidOperationException(
                    $"SPATIAL.ERR.MembershipRequiresLiveEntity entity={entity.Id}:{entity.WorldId}");
            }
        }
    }
}
