using System;
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
                    var unknown = new ReplicationDisclosureInput(handle, KnowledgePresence.Unknown);
                    if (!output.TryAddDisclosure(in unknown))
                    {
                        return Fail(output, ReplicationBridgeResult.CapacityContractViolated);
                    }

                    continue;
                }

                if (disclosure.Presence == KnowledgePresence.Unknown)
                {
                    var unknown = new ReplicationDisclosureInput(handle, KnowledgePresence.Unknown);
                    if (!output.TryAddDisclosure(in unknown))
                    {
                        return Fail(output, ReplicationBridgeResult.CapacityContractViolated);
                    }

                    continue;
                }

                var disclosureInput = new ReplicationDisclosureInput(handle, disclosure.Presence);
                if (!output.TryAddDisclosure(in disclosureInput))
                {
                    return Fail(output, ReplicationBridgeResult.CapacityContractViolated);
                }

                if (!disclosureInput.CanReplicateLiveState)
                {
                    continue;
                }

                if (!_world.TryGet(entity, out ReplicationSchemaRef schema) || schema.SchemaId <= 0)
                {
                    return Fail(output, ReplicationBridgeResult.SchemaMissing);
                }

                if (!_projectors.TryGet(schema.SchemaId, out IReplicationSchemaProjector projector))
                {
                    return Fail(output, ReplicationBridgeResult.SchemaNotRegistered);
                }

                if (!projector.TryProject(_world, entity, in disclosure, out ReplicationProjectedState projected))
                {
                    return Fail(output, ReplicationBridgeResult.ProjectionFailed);
                }

                var state = new ReplicatedEntityState(handle, schema.SchemaId, projected.Revision, projected.Values);
                if (!output.TryAddState(in state))
                {
                    return Fail(output, ReplicationBridgeResult.CapacityContractViolated);
                }
            }

            return ReplicationBridgeResult.Success;
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
            if (channel == null || projection == null || packet == null || tick > int.MaxValue)
            {
                packet?.Reset(default);
                projection?.Reset();
                return ReplicationBridgeResult.InvalidInput;
            }

            ReplicationBridgeResult projected = Project(interestHandles, (int)tick, projection);
            if (projected != ReplicationBridgeResult.Success)
            {
                packet.Reset(default);
                return projected;
            }

            ReplicationBridgeResult result = ReplicationBridgeResultMapper.FromBuild(
                channel.BuildFull(sessionEpoch, tick, snapshotId, projection.States, projection.Disclosures, packet));
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
            if (channel == null || projection == null || packet == null || tick > int.MaxValue)
            {
                packet?.Reset(default);
                projection?.Reset();
                return ReplicationBridgeResult.InvalidInput;
            }

            ReplicationBridgeResult projected = Project(interestHandles, (int)tick, projection);
            if (projected != ReplicationBridgeResult.Success)
            {
                packet.Reset(default);
                return projected;
            }

            ReplicationBridgeResult result = ReplicationBridgeResultMapper.FromBuild(
                channel.BuildDelta(
                    sessionEpoch,
                    tick,
                    snapshotId,
                    acknowledgedBaselineId,
                    projection.States,
                    projection.Disclosures,
                    packet));
            if (result != ReplicationBridgeResult.Success)
            {
                projection.Reset();
            }

            return result;
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
