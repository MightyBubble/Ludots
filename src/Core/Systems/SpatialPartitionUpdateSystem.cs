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
            .WithNone<PresentationStaticTransform, SpatialPartitionExcluded, PresentationDestroyPending, SuspendedTag>();
        private readonly QueryDescription _untrackedQuery = new QueryDescription()
            .WithAll<WorldPositionCm>()
            .WithNone<SpatialCellRef, PresentationStaticTransform, SpatialPartitionExcluded, PresentationDestroyPending, SuspendedTag>();
        private readonly QueryDescription _excludedTrackedQuery = new QueryDescription()
            .WithAll<SpatialPartitionExcluded, SpatialCellRef>();
        private readonly QueryDescription _destroyPendingTrackedQuery = new QueryDescription()
            .WithAll<PresentationDestroyPending, SpatialCellRef>();
        private readonly QueryDescription _suspendedTrackedQuery = new QueryDescription()
            .WithAll<SuspendedTag, SpatialCellRef>();
        private readonly QueryDescription _activeMembershipQuery = new QueryDescription()
            .WithAll<SpatialCellRef>();
        private readonly QueryDescription _rebuildEligibleQuery = new QueryDescription()
            .WithAll<WorldPositionCm>()
            .WithNone<PresentationStaticTransform, SpatialPartitionExcluded, PresentationDestroyPending, SuspendedTag>();

        private readonly CommandBuffer _commandBuffer = new();

        public SpatialPartitionUpdateSystem(World world, ISpatialPartitionWorld partition, WorldSizeSpec spec) : base(world)
        {
            _partition = partition ?? throw new ArgumentNullException(nameof(partition));
            _spec = spec;
        }

        /// <summary>
        /// Hot-swap the spatial partition and world spec when the spatial config changes (e.g. on map load).
        /// ECS remains the SSOT: validate eligible entities, clear old memberships, then rebuild into the new partition.
        /// Called by GameEngine.ApplyMapSpatialConfig to prevent stale references.
        /// </summary>
        internal void SetPartition(ISpatialPartitionWorld partition, WorldSizeSpec spec)
        {
            ArgumentNullException.ThrowIfNull(partition);

            ValidateMembershipStates();
            ValidateEligiblePositions(in spec);

            ISpatialPartitionWorld oldPartition = _partition;
            ClearActiveMemberships(oldPartition);
            oldPartition.Clear();

            _partition = partition;
            _spec = spec;
            if (!ReferenceEquals(oldPartition, partition))
            {
                partition.Clear();
            }

            RebuildEligibleMemberships();
        }

        public override void Update(in float dt)
        {
            RemoveSpatialRefs(in _excludedTrackedQuery);
            RemoveSpatialRefs(in _destroyPendingTrackedQuery);
            ResetSuspendedMemberships();
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
                World.Has<PresentationDestroyPending>(entity) ||
                World.Has<SuspendedTag>(entity))
            {
                Remove(entity);
                return;
            }

            if (World.Has<SpatialCellRef>(entity))
            {
                ref SpatialCellRef cellRef = ref World.Get<SpatialCellRef>(entity);
                SynchronizeTracked(_partition, in _spec, entity, in position, ref cellRef, reactivateDeactivated: true);
                return;
            }

            SpatialCellRef created = CreateMembership(_partition, in _spec, entity, in position);
            World.Add(entity, in created);
        }

        public void Deactivate(Entity entity)
        {
            RequireLiveEntity(entity);
            if (!World.Has<SpatialCellRef>(entity))
            {
                SpatialCellRef deactivated = new SpatialCellRef
                {
                    CellX = 0,
                    CellY = 0,
                    State = SpatialMembershipState.Deactivated,
                };
                World.Add(entity, in deactivated);
                return;
            }

            ref SpatialCellRef cellRef = ref World.Get<SpatialCellRef>(entity);
            switch (cellRef.State)
            {
                case SpatialMembershipState.Active:
                    RemoveTracked(_partition, entity, in cellRef);
                    break;
                case SpatialMembershipState.Uninitialized:
                case SpatialMembershipState.Deactivated:
                    break;
                default:
                    ThrowInvalidMembershipState(entity, cellRef.State);
                    break;
            }

            cellRef.State = SpatialMembershipState.Deactivated;
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

        private void ValidateMembershipStates()
        {
            foreach (ref var chunk in World.Query(in _activeMembershipQuery))
            {
                ref var entityFirst = ref chunk.Entity(0);
                var refs = chunk.GetSpan<SpatialCellRef>();

                foreach (var index in chunk)
                {
                    SpatialMembershipState state = refs[index].State;
                    if (state != SpatialMembershipState.Uninitialized &&
                        state != SpatialMembershipState.Active &&
                        state != SpatialMembershipState.Deactivated)
                    {
                        var entity = Unsafe.Add(ref entityFirst, index);
                        ThrowInvalidMembershipState(entity, state);
                    }
                }
            }
        }

        private void ValidateEligiblePositions(in WorldSizeSpec spec)
        {
            foreach (ref var chunk in World.Query(in _rebuildEligibleQuery))
            {
                var positions = chunk.GetSpan<WorldPositionCm>();
                bool hasCellRefs = chunk.Has<SpatialCellRef>();
                var cellRefs = hasCellRefs ? chunk.GetSpan<SpatialCellRef>() : default;
                ref var entityFirst = ref chunk.Entity(0);

                foreach (var index in chunk)
                {
                    if (hasCellRefs && cellRefs[index].State == SpatialMembershipState.Deactivated)
                    {
                        continue;
                    }

                    var entity = Unsafe.Add(ref entityFirst, index);
                    WorldCmInt2 worldCm = positions[index].Value.ToWorldCmInt2();
                    if (!spec.Contains(worldCm))
                    {
                        ThrowWorldPositionOutOfBounds(entity, worldCm, spec);
                    }
                }
            }
        }

        private void ClearActiveMemberships(ISpatialPartitionWorld partition)
        {
            foreach (ref var chunk in World.Query(in _activeMembershipQuery))
            {
                ref var entityFirst = ref chunk.Entity(0);
                var refs = chunk.GetSpan<SpatialCellRef>();

                foreach (var index in chunk)
                {
                    ref SpatialCellRef cellRef = ref refs[index];
                    if (cellRef.State != SpatialMembershipState.Active)
                    {
                        continue;
                    }

                    var entity = Unsafe.Add(ref entityFirst, index);
                    RemoveTracked(partition, entity, in cellRef);
                    cellRef.State = SpatialMembershipState.Uninitialized;
                }
            }
        }

        private void RebuildEligibleMemberships()
        {
            foreach (ref var chunk in World.Query(in _rebuildEligibleQuery))
            {
                ref var entityFirst = ref chunk.Entity(0);
                var positions = chunk.GetSpan<WorldPositionCm>();
                bool hasCellRefs = chunk.Has<SpatialCellRef>();
                var cellRefs = hasCellRefs ? chunk.GetSpan<SpatialCellRef>() : default;

                foreach (var index in chunk)
                {
                    var entity = Unsafe.Add(ref entityFirst, index);
                    if (hasCellRefs)
                    {
                        ref SpatialCellRef cellRef = ref cellRefs[index];
                        if (cellRef.State == SpatialMembershipState.Deactivated)
                        {
                            continue;
                        }

                        ActivateMembership(_partition, in _spec, entity, in positions[index], ref cellRef);
                        continue;
                    }

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

        private void ResetSuspendedMemberships()
        {
            foreach (ref var chunk in World.Query(in _suspendedTrackedQuery))
            {
                ref var entityFirst = ref chunk.Entity(0);
                var refs = chunk.GetSpan<SpatialCellRef>();

                foreach (var index in chunk)
                {
                    ref SpatialCellRef cellRef = ref refs[index];
                    switch (cellRef.State)
                    {
                        case SpatialMembershipState.Active:
                        {
                            var entity = Unsafe.Add(ref entityFirst, index);
                            RemoveTracked(_partition, entity, in cellRef);
                            cellRef.State = SpatialMembershipState.Uninitialized;
                            break;
                        }
                        case SpatialMembershipState.Uninitialized:
                        case SpatialMembershipState.Deactivated:
                            break;
                        default:
                        {
                            var entity = Unsafe.Add(ref entityFirst, index);
                            ThrowInvalidMembershipState(entity, cellRef.State);
                            break;
                        }
                    }
                }
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
                switch (cellRef.State)
                {
                    case SpatialMembershipState.Uninitialized:
                    case SpatialMembershipState.Active:
                        SynchronizeTracked(Partition, in Spec, entity, in pos, ref cellRef, reactivateDeactivated: false);
                        return;
                    case SpatialMembershipState.Deactivated:
                        return;
                    default:
                        ThrowInvalidMembershipState(entity, cellRef.State);
                        return;
                }
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
            return new SpatialCellRef
            {
                CellX = cellX,
                CellY = cellY,
                State = SpatialMembershipState.Active,
            };
        }

        private static void ActivateMembership(
            ISpatialPartitionWorld partition,
            in WorldSizeSpec spec,
            Entity entity,
            in WorldPositionCm position,
            ref SpatialCellRef cellRef)
        {
            WorldCmInt2 worldCm = position.Value.ToWorldCmInt2();
            if (!spec.Contains(worldCm)) ThrowWorldPositionOutOfBounds(entity, worldCm, spec);
            (int cellX, int cellY) = WorldToCell(worldCm, spec.GridCellSizeCm);
            partition.Add(entity, cellX, cellY);
            cellRef.CellX = cellX;
            cellRef.CellY = cellY;
            cellRef.State = SpatialMembershipState.Active;
        }

        private static void SynchronizeTracked(
            ISpatialPartitionWorld partition,
            in WorldSizeSpec spec,
            Entity entity,
            in WorldPositionCm position,
            ref SpatialCellRef cellRef,
            bool reactivateDeactivated)
        {
            if (cellRef.State == SpatialMembershipState.Deactivated)
            {
                if (!reactivateDeactivated)
                {
                    return;
                }

                ActivateMembership(partition, in spec, entity, in position, ref cellRef);
                return;
            }

            if (cellRef.State != SpatialMembershipState.Uninitialized &&
                cellRef.State != SpatialMembershipState.Active)
            {
                ThrowInvalidMembershipState(entity, cellRef.State);
            }

            WorldCmInt2 worldCm = position.Value.ToWorldCmInt2();
            if (!spec.Contains(worldCm)) ThrowWorldPositionOutOfBounds(entity, worldCm, spec);

            if (cellRef.State == SpatialMembershipState.Uninitialized)
            {
                (int initialCellX, int initialCellY) = WorldToCell(worldCm, spec.GridCellSizeCm);
                partition.Add(entity, initialCellX, initialCellY);
                cellRef.CellX = initialCellX;
                cellRef.CellY = initialCellY;
                cellRef.State = SpatialMembershipState.Active;
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
            switch (cellRef.State)
            {
                case SpatialMembershipState.Active:
                    partition.Remove(entity, cellRef.CellX, cellRef.CellY);
                    return;
                case SpatialMembershipState.Uninitialized:
                case SpatialMembershipState.Deactivated:
                    return;
                default:
                    ThrowInvalidMembershipState(entity, cellRef.State);
                    return;
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

        private static void ThrowInvalidMembershipState(Entity entity, SpatialMembershipState state)
        {
            throw new InvalidOperationException(
                $"SPATIAL.ERR.InvalidMembershipState entity={entity.Id}:{entity.WorldId} state={(byte)state}");
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
