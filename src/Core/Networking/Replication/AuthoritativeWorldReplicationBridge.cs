using System;
using System.Diagnostics;
using Arch.Core;
using Ludots.Core.Knowledge;

namespace Ludots.Core.Networking.Replication
{
    public sealed class ReplicationProjectionBuffer
    {
        private readonly ReplicatedEntityState[] _states;
        private readonly ReplicationDisclosureInput[] _disclosures;
        private int _stateCount;
        private int _disclosureCount;

        public ReplicationProjectionBuffer(int entityCapacity)
        {
            if (entityCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(entityCapacity));
            }

            _states = new ReplicatedEntityState[entityCapacity];
            _disclosures = new ReplicationDisclosureInput[entityCapacity];
        }

        public int EntityCapacity => _states.Length;
        public ReadOnlySpan<ReplicatedEntityState> States => _states.AsSpan(0, _stateCount);
        public ReadOnlySpan<ReplicationDisclosureInput> Disclosures => _disclosures.AsSpan(0, _disclosureCount);

        internal void Reset()
        {
            _stateCount = 0;
            _disclosureCount = 0;
        }

        internal bool TryAddState(in ReplicatedEntityState state)
        {
            if (_stateCount == _states.Length)
            {
                return false;
            }

            _states[_stateCount++] = state;
            return true;
        }

        internal bool TryAddDisclosure(in ReplicationDisclosureInput disclosure)
        {
            if (_disclosureCount == _disclosures.Length)
            {
                return false;
            }

            _disclosures[_disclosureCount++] = disclosure;
            return true;
        }
    }

    public sealed class AuthoritativeWorldReplicationBridge
    {
        private readonly World _world;
        private readonly NetworkEntityTable _entities;
        private readonly KnowledgeProjectionStore _knowledge;
        private readonly Entity _viewer;
        private readonly ReplicationSchemaProjectorRegistry _projectors;
        private readonly int _replicationEntityCapacityPerSeat;

        public AuthoritativeWorldReplicationBridge(
            World world,
            NetworkEntityTable entities,
            KnowledgeProjectionStore knowledge,
            Entity viewer,
            ReplicationSchemaProjectorRegistry projectors,
            int replicationEntityCapacityPerSeat)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _entities = entities ?? throw new ArgumentNullException(nameof(entities));
            _knowledge = knowledge ?? throw new ArgumentNullException(nameof(knowledge));
            _projectors = projectors ?? throw new ArgumentNullException(nameof(projectors));
            if (viewer == Entity.Null)
            {
                throw new ArgumentException("Replication viewer entity is required.", nameof(viewer));
            }

            if (!projectors.IsFrozen)
            {
                throw new InvalidOperationException("Replication schema projector registry must be frozen before bridge construction.");
            }

            if (replicationEntityCapacityPerSeat <= 0 ||
                replicationEntityCapacityPerSeat > entities.Capacity)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(replicationEntityCapacityPerSeat),
                    "Per-seat replication capacity must be positive and cannot exceed the global network entity capacity.");
            }

            _viewer = viewer;
            _replicationEntityCapacityPerSeat = replicationEntityCapacityPerSeat;
        }

        public int GlobalEntityCapacity => _entities.Capacity;
        public int ReplicationEntityCapacityPerSeat => _replicationEntityCapacityPerSeat;
        public AuthoritativeReplicationBuildMetrics LastBuildMetrics { get; private set; }
        internal NetworkEntityTable EntityTable => _entities;

        public ReplicationBridgeResult Project(
            ReadOnlySpan<NetworkEntityHandle> interestHandles,
            int currentTick,
            ReplicationProjectionBuffer output)
        {
            if (output == null)
            {
                return ReplicationBridgeResult.InvalidInput;
            }

            output.Reset();
            if (currentTick < 0 || !_world.IsAlive(_viewer))
            {
                return ReplicationBridgeResult.InvalidInput;
            }

            if (interestHandles.Length > _replicationEntityCapacityPerSeat ||
                interestHandles.Length > output.EntityCapacity)
            {
                return ReplicationBridgeResult.CapacityContractViolated;
            }

            int previousSlot = -1;
            for (int i = 0; i < interestHandles.Length; i++)
            {
                NetworkEntityHandle handle = interestHandles[i];
                int slot = handle.Slot;
                if (!handle.IsValid ||
                    (uint)slot >= (uint)_entities.Capacity ||
                    slot <= previousSlot)
                {
                    return Fail(output, ReplicationBridgeResult.InvalidInput);
                }

                previousSlot = slot;
                if (!_entities.TryResolve(handle, out Entity entity) || !_world.IsAlive(entity))
                {
                    return Fail(output, ReplicationBridgeResult.EntityUnavailable);
                }

                if (!_knowledge.TryGet(_viewer, entity, currentTick, out KnowledgeDisclosureRecord disclosure))
                {
                    disclosure = default;
                }

                ReplicationBridgeResult projected = ProjectResolved(handle, entity, in disclosure, output);
                if (projected != ReplicationBridgeResult.Success)
                {
                    return Fail(output, projected);
                }
            }

            return ReplicationBridgeResult.Success;
        }

        internal ReplicationBridgeResult ProjectPrepared(
            ReadOnlySpan<NetworkEntityHandle> interestHandles,
            ReadOnlySpan<KnowledgeDisclosureRecord> knowledgeDisclosures,
            int currentTick,
            ReplicationProjectionBuffer output)
        {
            if (output == null ||
                currentTick < 0 ||
                !_world.IsAlive(_viewer) ||
                interestHandles.Length != knowledgeDisclosures.Length)
            {
                output?.Reset();
                return ReplicationBridgeResult.InvalidInput;
            }

            output.Reset();
            if (interestHandles.Length > _replicationEntityCapacityPerSeat ||
                interestHandles.Length > output.EntityCapacity)
            {
                return ReplicationBridgeResult.CapacityContractViolated;
            }

            int previousSlot = -1;
            for (int index = 0; index < interestHandles.Length; index++)
            {
                NetworkEntityHandle handle = interestHandles[index];
                int slot = handle.Slot;
                if (!handle.IsValid ||
                    (uint)slot >= (uint)_entities.Capacity ||
                    slot <= previousSlot)
                {
                    return Fail(output, ReplicationBridgeResult.InvalidInput);
                }

                previousSlot = slot;
                if (!_entities.TryResolve(handle, out Entity entity) || !_world.IsAlive(entity))
                {
                    return Fail(output, ReplicationBridgeResult.EntityUnavailable);
                }

                KnowledgeDisclosureRecord disclosure = knowledgeDisclosures[index];
                ReplicationBridgeResult projected = ProjectResolved(handle, entity, in disclosure, output);
                if (projected != ReplicationBridgeResult.Success)
                {
                    return Fail(output, projected);
                }
            }

            return ReplicationBridgeResult.Success;
        }

        private ReplicationBridgeResult ProjectResolved(
            NetworkEntityHandle handle,
            Entity entity,
            in KnowledgeDisclosureRecord disclosure,
            ReplicationProjectionBuffer output)
        {
            if (disclosure.Presence == KnowledgePresence.Unknown)
            {
                var unknown = new ReplicationDisclosureInput(handle, KnowledgePresence.Unknown);
                return output.TryAddDisclosure(in unknown)
                    ? ReplicationBridgeResult.Success
                    : ReplicationBridgeResult.CapacityContractViolated;
            }

            var disclosureInput = new ReplicationDisclosureInput(handle, disclosure.Presence);
            if (!output.TryAddDisclosure(in disclosureInput))
            {
                return ReplicationBridgeResult.CapacityContractViolated;
            }

            if (!disclosureInput.CanReplicateLiveState)
            {
                return ReplicationBridgeResult.Success;
            }

            if (!_world.TryGet(entity, out ReplicationSchemaRef schema) || schema.SchemaId <= 0)
            {
                return ReplicationBridgeResult.SchemaMissing;
            }

            if (!_projectors.TryGet(schema.SchemaId, out IReplicationSchemaProjector projector))
            {
                return ReplicationBridgeResult.SchemaNotRegistered;
            }

            if (!projector.TryProject(_world, entity, in disclosure, out ReplicationProjectedState projected))
            {
                return ReplicationBridgeResult.ProjectionFailed;
            }

            var state = new ReplicatedEntityState(
                handle,
                schema.SchemaId,
                projected.Revision,
                projected.Values,
                projected.Ownership);
            return output.TryAddState(in state)
                ? ReplicationBridgeResult.Success
                : ReplicationBridgeResult.CapacityContractViolated;
        }

        internal ReplicationBridgeResult PrepareFull(
            AuthoritativeReplicationChannel channel,
            ulong sessionEpoch,
            uint tick,
            ulong snapshotId,
            ReadOnlySpan<NetworkEntityHandle> interestHandles,
            ReadOnlySpan<KnowledgeDisclosureRecord> knowledgeDisclosures,
            ReplicationProjectionBuffer projection,
            ReplicationPacketBuffer packet)
        {
            EnsureSharedEntityTable(channel);
            _entities.EnterSnapshotPublication();
            try
            {
                if (channel == null || projection == null || packet == null || tick > int.MaxValue)
                {
                    LastBuildMetrics = default;
                    packet?.Reset(default);
                    projection?.Reset();
                    return ReplicationBridgeResult.InvalidInput;
                }

                long projectionStarted = Stopwatch.GetTimestamp();
                ReplicationBridgeResult projected = ProjectPrepared(
                    interestHandles,
                    knowledgeDisclosures,
                    (int)tick,
                    projection);
                long projectionElapsed = Stopwatch.GetTimestamp() - projectionStarted;
                if (projected != ReplicationBridgeResult.Success)
                {
                    LastBuildMetrics = new AuthoritativeReplicationBuildMetrics(projectionElapsed, 0);
                    packet.Reset(default);
                    return projected;
                }

                long channelBuildStarted = Stopwatch.GetTimestamp();
                ReplicationBridgeResult result = ReplicationBridgeResultMapper.FromBuild(
                    channel.PrepareFull(
                        sessionEpoch,
                        tick,
                        snapshotId,
                        projection.States,
                        projection.Disclosures,
                        packet));
                LastBuildMetrics = new AuthoritativeReplicationBuildMetrics(
                    projectionElapsed,
                    Stopwatch.GetTimestamp() - channelBuildStarted);
                if (result != ReplicationBridgeResult.Success)
                {
                    projection.Reset();
                }

                return result;
            }
            finally
            {
                _entities.ExitSnapshotPublication();
            }
        }

        internal ReplicationBridgeResult PrepareDelta(
            AuthoritativeReplicationChannel channel,
            ulong sessionEpoch,
            uint tick,
            ulong snapshotId,
            ulong acknowledgedBaselineId,
            ReadOnlySpan<NetworkEntityHandle> interestHandles,
            ReadOnlySpan<KnowledgeDisclosureRecord> knowledgeDisclosures,
            ReplicationProjectionBuffer projection,
            ReplicationPacketBuffer packet)
        {
            EnsureSharedEntityTable(channel);
            _entities.EnterSnapshotPublication();
            try
            {
                if (channel == null || projection == null || packet == null || tick > int.MaxValue)
                {
                    LastBuildMetrics = default;
                    packet?.Reset(default);
                    projection?.Reset();
                    return ReplicationBridgeResult.InvalidInput;
                }

                long projectionStarted = Stopwatch.GetTimestamp();
                ReplicationBridgeResult projected = ProjectPrepared(
                    interestHandles,
                    knowledgeDisclosures,
                    (int)tick,
                    projection);
                long projectionElapsed = Stopwatch.GetTimestamp() - projectionStarted;
                if (projected != ReplicationBridgeResult.Success)
                {
                    LastBuildMetrics = new AuthoritativeReplicationBuildMetrics(projectionElapsed, 0);
                    packet.Reset(default);
                    return projected;
                }

                long channelBuildStarted = Stopwatch.GetTimestamp();
                ReplicationBridgeResult result = ReplicationBridgeResultMapper.FromBuild(
                    channel.PrepareDelta(
                        sessionEpoch,
                        tick,
                        snapshotId,
                        acknowledgedBaselineId,
                        projection.States,
                        projection.Disclosures,
                        packet));
                LastBuildMetrics = new AuthoritativeReplicationBuildMetrics(
                    projectionElapsed,
                    Stopwatch.GetTimestamp() - channelBuildStarted);
                if (result != ReplicationBridgeResult.Success)
                {
                    projection.Reset();
                }

                return result;
            }
            finally
            {
                _entities.ExitSnapshotPublication();
            }
        }

        public ReplicationBridgeResult BuildFull(
            AuthoritativeReplicationChannel channel,
            ulong sessionEpoch,
            uint tick,
            ulong snapshotId,
            ReadOnlySpan<NetworkEntityHandle> interestHandles,
            ReplicationProjectionBuffer projection,
            ReplicationPacketBuffer packet)
        {
            EnsureSharedEntityTable(channel);
            _entities.EnterSnapshotPublication();
            try
            {
                return BuildFullCore(
                    channel,
                    sessionEpoch,
                    tick,
                    snapshotId,
                    interestHandles,
                    projection,
                    packet);
            }
            finally
            {
                _entities.ExitSnapshotPublication();
            }
        }

        private ReplicationBridgeResult BuildFullCore(
            AuthoritativeReplicationChannel channel,
            ulong sessionEpoch,
            uint tick,
            ulong snapshotId,
            ReadOnlySpan<NetworkEntityHandle> interestHandles,
            ReplicationProjectionBuffer projection,
            ReplicationPacketBuffer packet)
        {
            if (channel == null || projection == null || packet == null || tick > int.MaxValue)
            {
                LastBuildMetrics = default;
                packet?.Reset(default);
                projection?.Reset();
                return ReplicationBridgeResult.InvalidInput;
            }

            long projectionStarted = Stopwatch.GetTimestamp();
            ReplicationBridgeResult projected = Project(interestHandles, (int)tick, projection);
            long projectionElapsed = Stopwatch.GetTimestamp() - projectionStarted;
            if (projected != ReplicationBridgeResult.Success)
            {
                LastBuildMetrics = new AuthoritativeReplicationBuildMetrics(projectionElapsed, 0);
                packet.Reset(default);
                return projected;
            }

            long channelBuildStarted = Stopwatch.GetTimestamp();
            ReplicationBridgeResult result = ReplicationBridgeResultMapper.FromBuild(
                channel.BuildFull(sessionEpoch, tick, snapshotId, projection.States, projection.Disclosures, packet));
            LastBuildMetrics = new AuthoritativeReplicationBuildMetrics(
                projectionElapsed,
                Stopwatch.GetTimestamp() - channelBuildStarted);
            if (result != ReplicationBridgeResult.Success)
            {
                projection.Reset();
            }

            return result;
        }

        public ReplicationBridgeResult BuildDelta(
            AuthoritativeReplicationChannel channel,
            ulong sessionEpoch,
            uint tick,
            ulong snapshotId,
            ulong acknowledgedBaselineId,
            ReadOnlySpan<NetworkEntityHandle> interestHandles,
            ReplicationProjectionBuffer projection,
            ReplicationPacketBuffer packet)
        {
            EnsureSharedEntityTable(channel);
            _entities.EnterSnapshotPublication();
            try
            {
                return BuildDeltaCore(
                    channel,
                    sessionEpoch,
                    tick,
                    snapshotId,
                    acknowledgedBaselineId,
                    interestHandles,
                    projection,
                    packet);
            }
            finally
            {
                _entities.ExitSnapshotPublication();
            }
        }

        private ReplicationBridgeResult BuildDeltaCore(
            AuthoritativeReplicationChannel channel,
            ulong sessionEpoch,
            uint tick,
            ulong snapshotId,
            ulong acknowledgedBaselineId,
            ReadOnlySpan<NetworkEntityHandle> interestHandles,
            ReplicationProjectionBuffer projection,
            ReplicationPacketBuffer packet)
        {
            if (channel == null || projection == null || packet == null || tick > int.MaxValue)
            {
                LastBuildMetrics = default;
                packet?.Reset(default);
                projection?.Reset();
                return ReplicationBridgeResult.InvalidInput;
            }

            long projectionStarted = Stopwatch.GetTimestamp();
            ReplicationBridgeResult projected = Project(interestHandles, (int)tick, projection);
            long projectionElapsed = Stopwatch.GetTimestamp() - projectionStarted;
            if (projected != ReplicationBridgeResult.Success)
            {
                LastBuildMetrics = new AuthoritativeReplicationBuildMetrics(projectionElapsed, 0);
                packet.Reset(default);
                return projected;
            }

            long channelBuildStarted = Stopwatch.GetTimestamp();
            ReplicationBridgeResult result = ReplicationBridgeResultMapper.FromBuild(
                channel.BuildDelta(
                    sessionEpoch,
                    tick,
                    snapshotId,
                    acknowledgedBaselineId,
                    projection.States,
                    projection.Disclosures,
                    packet));
            LastBuildMetrics = new AuthoritativeReplicationBuildMetrics(
                projectionElapsed,
                Stopwatch.GetTimestamp() - channelBuildStarted);
            if (result != ReplicationBridgeResult.Success)
            {
                projection.Reset();
            }

            return result;
        }

        private void EnsureSharedEntityTable(AuthoritativeReplicationChannel channel)
        {
            if (channel != null && !ReferenceEquals(_entities, channel.EntityTable))
            {
                throw new ArgumentException(
                    "Replication bridge and channel must share the same network entity table.",
                    nameof(channel));
            }
        }

        private static ReplicationBridgeResult Fail(
            ReplicationProjectionBuffer output,
            ReplicationBridgeResult result)
        {
            output.Reset();
            return result;
        }
    }
}
