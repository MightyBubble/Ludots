using System;
using System.Runtime.CompilerServices;
using Arch.Buffer;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Mathematics;
using Ludots.Core.Spatial;

namespace Ludots.Core.Systems
{
    public sealed class SpatialPartitionUpdateSystem : BaseSystem<World, float>
    {
        private ISpatialPartitionWorld _partition;
        private WorldSizeSpec _spec;
        private readonly QueryDescription _trackedQuery = new QueryDescription().WithAll<WorldPositionCm, SpatialCellRef>();
        private readonly QueryDescription _untrackedQuery = new QueryDescription().WithAll<WorldPositionCm>().WithNone<SpatialCellRef>();

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
            AddMissingSpatialRefs();

            var moveJob = new MoveJob { Partition = _partition, Spec = _spec };
            World.InlineEntityQuery<MoveJob, WorldPositionCm, SpatialCellRef>(in _trackedQuery, ref moveJob);
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
                    if (!_spec.Contains(worldCm)) throw new InvalidOperationException("SPATIAL.ERR.WorldPositionOutOfBounds");
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
                if (!Spec.Contains(worldCm)) throw new InvalidOperationException("SPATIAL.ERR.WorldPositionOutOfBounds");
                (int cx, int cy) = WorldToCell(worldCm, Spec.GridCellSizeCm);

                if (cellRef.Initialized == 0)
                {
                    Partition.Add(entity, cx, cy);
                    cellRef.CellX = cx;
                    cellRef.CellY = cy;
                    cellRef.Initialized = 1;
                    return;
                }

                if (cellRef.CellX == cx && cellRef.CellY == cy) return;

                Partition.Remove(entity, cellRef.CellX, cellRef.CellY);
                Partition.Add(entity, cx, cy);
                cellRef.CellX = cx;
                cellRef.CellY = cy;
            }
        }

        private static (int x, int y) WorldToCell(in WorldCmInt2 world, int cellSizeCm)
        {
            return (MathUtil.FloorDiv(world.X, cellSizeCm), MathUtil.FloorDiv(world.Y, cellSizeCm));
        }
    }
}
