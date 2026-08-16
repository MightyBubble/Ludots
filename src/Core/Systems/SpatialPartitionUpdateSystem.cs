using System;
using System.Runtime.CompilerServices;
using Arch.Buffer;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Systems
{
    public sealed class SpatialPartitionUpdateSystem : BaseSystem<World, float>
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
                    if (cellRef.Initialized != 0)
                    {
                        _partition.Remove(entity, cellRef.CellX, cellRef.CellY);
                    }

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
                    var worldCm = positions[index].Value.ToWorldCmInt2();
                    if (!_spec.Contains(worldCm)) ThrowWorldPositionOutOfBounds(entity, worldCm, _spec);
                    (int cx, int cy) = WorldToCell(worldCm, _spec.GridCellSizeCm);
                    _partition.Add(entity, cx, cy);
                    _commandBuffer.Add(entity, new SpatialCellRef { CellX = cx, CellY = cy, Initialized = 1 });
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
                var worldCm = pos.Value.ToWorldCmInt2();
                if (!Spec.Contains(worldCm)) ThrowWorldPositionOutOfBounds(entity, worldCm, Spec);

                if (cellRef.Initialized == 0)
                {
                    (int initialCellX, int initialCellY) = WorldToCell(worldCm, Spec.GridCellSizeCm);
                    Partition.Add(entity, initialCellX, initialCellY);
                    cellRef.CellX = initialCellX;
                    cellRef.CellY = initialCellY;
                    cellRef.Initialized = 1;
                    return;
                }

                if (IsInsideCell(worldCm, cellRef.CellX, cellRef.CellY, Spec.GridCellSizeCm))
                {
                    return;
                }

                (int nextCellX, int nextCellY) = WorldToCell(worldCm, Spec.GridCellSizeCm);
                if (cellRef.CellX == nextCellX && cellRef.CellY == nextCellY) return;

                Partition.Remove(entity, cellRef.CellX, cellRef.CellY);
                Partition.Add(entity, nextCellX, nextCellY);
                cellRef.CellX = nextCellX;
                cellRef.CellY = nextCellY;
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
    }
}
