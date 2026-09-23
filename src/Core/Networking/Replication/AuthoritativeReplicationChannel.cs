using System;

namespace Ludots.Core.Networking.Replication
{
    public sealed class AuthoritativeReplicationChannel
    {
        private readonly NetworkEntityTable _entities;
        private readonly int _replicationEntityCapacityPerSeat;
        private readonly int _baselineCapacity;
        private readonly ReplicationDisclosureChangeLog _disclosureLog;

        private readonly NetworkEntityHandle[] _currentEntities;
        private readonly int[] _currentSchemaIds;
        private readonly uint[] _currentRevisions;
        private readonly ReplicationStateVector[] _currentValues;
        private readonly ReplicationControlOwnership[] _currentOwnership;
        private int _currentCount;

        private readonly NetworkEntityHandle[] _baselineEntities;
        private readonly int[] _baselineSchemaIds;
        private readonly uint[] _baselineRevisions;
        private readonly ReplicationStateVector[] _baselineValues;
        private readonly ReplicationControlOwnership[] _baselineOwnership;
        private readonly int[] _baselineCounts;
        private readonly ulong[] _baselineIds;
        private int _nextBaselineSlot;
        private ulong _sessionEpoch;
        private ulong _lastSnapshotId;

        private ulong _pendingSessionEpoch;
        private ulong _pendingSnapshotId;
        private int _pendingDisclosureCount;
        private ReplicationPacketBuffer? _preparedPacket;
        private bool _prepared;

        public AuthoritativeReplicationChannel(
            NetworkEntityTable entities,
            int replicationEntityCapacityPerSeat,
            int baselineCapacity,
            ReplicationDisclosureChangeLog disclosureLog)
        {
            _entities = entities ?? throw new ArgumentNullException(nameof(entities));
            if (replicationEntityCapacityPerSeat <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(replicationEntityCapacityPerSeat));
            }

            if (replicationEntityCapacityPerSeat > entities.Capacity)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(replicationEntityCapacityPerSeat),
                    "Per-seat replication capacity cannot exceed the global network entity capacity.");
            }

            if (baselineCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(baselineCapacity));
            }

            _replicationEntityCapacityPerSeat = replicationEntityCapacityPerSeat;
            _baselineCapacity = baselineCapacity;
            _disclosureLog = disclosureLog ?? throw new ArgumentNullException(nameof(disclosureLog));

            _currentEntities = new NetworkEntityHandle[replicationEntityCapacityPerSeat];
            _currentSchemaIds = new int[replicationEntityCapacityPerSeat];
            _currentRevisions = new uint[replicationEntityCapacityPerSeat];
            _currentValues = new ReplicationStateVector[replicationEntityCapacityPerSeat];
            _currentOwnership = new ReplicationControlOwnership[replicationEntityCapacityPerSeat];
            int baselineStateCapacity = checked(replicationEntityCapacityPerSeat * baselineCapacity);
            _baselineEntities = new NetworkEntityHandle[baselineStateCapacity];
            _baselineSchemaIds = new int[baselineStateCapacity];
            _baselineRevisions = new uint[baselineStateCapacity];
            _baselineValues = new ReplicationStateVector[baselineStateCapacity];
            _baselineOwnership = new ReplicationControlOwnership[baselineStateCapacity];
            _baselineCounts = new int[baselineCapacity];
            _baselineIds = new ulong[baselineCapacity];
        }

        public int ReplicationEntityCapacityPerSeat => _replicationEntityCapacityPerSeat;
        public int BaselineCapacity => _baselineCapacity;
        public int DisclosureChangeLogCapacity => _disclosureLog.Capacity;
        public int ReservedCurrentStateCapacity => _currentEntities.Length;
        public int ReservedBaselineStateCapacity => _baselineEntities.Length;
        public bool IsPrepared => _prepared;
        internal NetworkEntityTable EntityTable => _entities;
        internal bool IsPristine
        {
            get
            {
                if (_prepared ||
                    _currentCount != 0 ||
                    _nextBaselineSlot != 0 ||
                    _sessionEpoch != 0 ||
                    _lastSnapshotId != 0 ||
                    _disclosureLog.Count != 0)
                {
                    return false;
                }

                for (int i = 0; i < _baselineIds.Length; i++)
                {
                    if (_baselineIds[i] != 0 || _baselineCounts[i] != 0)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        public bool TryAcknowledgeDisclosureChangesThrough(ulong sequence)
        {
            if (_prepared)
            {
                throw new InvalidOperationException(
                    "Disclosure acknowledgement is rejected while a replication publication is prepared.");
            }

            return _disclosureLog.TryAcknowledgeThrough(sequence);
        }

        public ReplicationBuildResult BuildFull(
            ulong sessionEpoch,
            uint tick,
            ulong snapshotId,
            ReadOnlySpan<ReplicatedEntityState> states,
            ReadOnlySpan<ReplicationDisclosureInput> disclosures,
            ReplicationPacketBuffer packet)
        {
            ReplicationBuildResult prepared = PrepareFull(
                sessionEpoch,
                tick,
                snapshotId,
                states,
                disclosures,
                packet);
            if (prepared != ReplicationBuildResult.Success)
            {
                return prepared;
            }

            CommitPrepared();
            return ReplicationBuildResult.Success;
        }

        public ReplicationBuildResult PrepareFull(
            ulong sessionEpoch,
            uint tick,
            ulong snapshotId,
            ReadOnlySpan<ReplicatedEntityState> states,
            ReadOnlySpan<ReplicationDisclosureInput> disclosures,
            ReplicationPacketBuffer packet)
        {
            bool entered = false;
            if (!_entities.IsInSnapshotPublication)
            {
                _entities.EnterSnapshotPublication();
                entered = true;
            }

            try
            {
                return PrepareFullCore(sessionEpoch, tick, snapshotId, states, disclosures, packet);
            }
            finally
            {
                if (entered)
                {
                    _entities.ExitSnapshotPublication();
                }
            }
        }

        private ReplicationBuildResult PrepareFullCore(
            ulong sessionEpoch,
            uint tick,
            ulong snapshotId,
            ReadOnlySpan<ReplicatedEntityState> states,
            ReadOnlySpan<ReplicationDisclosureInput> disclosures,
            ReplicationPacketBuffer packet)
        {
            if (packet == null || _prepared)
            {
                return ReplicationBuildResult.InvalidInput;
            }

            packet.Reset(default);
            _pendingDisclosureCount = 0;
            ReplicationBuildResult headerResult = ValidateHeader(sessionEpoch, snapshotId);
            if (headerResult != ReplicationBuildResult.Success)
            {
                return headerResult;
            }

            ReplicationBuildResult viewResult = BuildCurrentView(states, disclosures);
            if (viewResult != ReplicationBuildResult.Success)
            {
                return viewResult;
            }

            if (_currentCount > packet.EntityCapacity ||
                _currentCount > packet.DisclosureCapacity)
            {
                return ReplicationBuildResult.PacketCapacityExceeded;
            }

            if (_currentCount > _disclosureLog.AvailableCapacity)
            {
                return ReplicationBuildResult.DisclosureLogCapacityExceeded;
            }

            var header = new ReplicationPacketHeader(
                ReplicationPacketKind.Full,
                sessionEpoch,
                tick,
                snapshotId,
                baselineSnapshotId: 0);
            packet.Reset(in header);

            for (int i = 0; i < _currentCount; i++)
            {
                ReplicatedEntityState state = GetCurrentState(i);
                packet.AddUpsert(in state);
                AppendPreparedDisclosureChange(packet, snapshotId, state.Entity, ReplicationDisclosureChangeKind.Reveal);
            }

            StagePrepared(sessionEpoch, snapshotId, packet);
            return ReplicationBuildResult.Success;
        }

        public ReplicationBuildResult BuildDelta(
            ulong sessionEpoch,
            uint tick,
            ulong snapshotId,
            ulong acknowledgedBaselineId,
            ReadOnlySpan<ReplicatedEntityState> states,
            ReadOnlySpan<ReplicationDisclosureInput> disclosures,
            ReplicationPacketBuffer packet)
        {
            ReplicationBuildResult prepared = PrepareDelta(
                sessionEpoch,
                tick,
                snapshotId,
                acknowledgedBaselineId,
                states,
                disclosures,
                packet);
            if (prepared != ReplicationBuildResult.Success)
            {
                return prepared;
            }

            CommitPrepared();
            return ReplicationBuildResult.Success;
        }

        public ReplicationBuildResult PrepareDelta(
            ulong sessionEpoch,
            uint tick,
            ulong snapshotId,
            ulong acknowledgedBaselineId,
            ReadOnlySpan<ReplicatedEntityState> states,
            ReadOnlySpan<ReplicationDisclosureInput> disclosures,
            ReplicationPacketBuffer packet)
        {
            bool entered = false;
            if (!_entities.IsInSnapshotPublication)
            {
                _entities.EnterSnapshotPublication();
                entered = true;
            }

            try
            {
                return PrepareDeltaCore(
                    sessionEpoch,
                    tick,
                    snapshotId,
                    acknowledgedBaselineId,
                    states,
                    disclosures,
                    packet);
            }
            finally
            {
                if (entered)
                {
                    _entities.ExitSnapshotPublication();
                }
            }
        }

        private ReplicationBuildResult PrepareDeltaCore(
            ulong sessionEpoch,
            uint tick,
            ulong snapshotId,
            ulong acknowledgedBaselineId,
            ReadOnlySpan<ReplicatedEntityState> states,
            ReadOnlySpan<ReplicationDisclosureInput> disclosures,
            ReplicationPacketBuffer packet)
        {
            if (packet == null || _prepared)
            {
                return ReplicationBuildResult.InvalidInput;
            }

            packet.Reset(default);
            _pendingDisclosureCount = 0;
            if (acknowledgedBaselineId == 0)
            {
                return ReplicationBuildResult.InvalidInput;
            }

            ReplicationBuildResult headerResult = ValidateHeader(sessionEpoch, snapshotId);
            if (headerResult != ReplicationBuildResult.Success)
            {
                return headerResult;
            }

            int baselineSlot = FindBaseline(acknowledgedBaselineId);
            if (baselineSlot < 0)
            {
                return ReplicationBuildResult.BaselineUnavailable;
            }

            ReplicationBuildResult viewResult = BuildCurrentView(states, disclosures);
            if (viewResult != ReplicationBuildResult.Success)
            {
                return viewResult;
            }

            CountDelta(
                baselineSlot,
                out int upsertCount,
                out int removalCount,
                out int disclosureChangeCount);
            if (upsertCount > packet.EntityCapacity ||
                removalCount > packet.EntityCapacity ||
                disclosureChangeCount > packet.DisclosureCapacity)
            {
                return ReplicationBuildResult.PacketCapacityExceeded;
            }

            if (disclosureChangeCount > _disclosureLog.AvailableCapacity)
            {
                return ReplicationBuildResult.DisclosureLogCapacityExceeded;
            }

            var header = new ReplicationPacketHeader(
                ReplicationPacketKind.Delta,
                sessionEpoch,
                tick,
                snapshotId,
                acknowledgedBaselineId);
            packet.Reset(in header);
            WriteDelta(baselineSlot, snapshotId, packet);

            StagePrepared(sessionEpoch, snapshotId, packet);
            return ReplicationBuildResult.Success;
        }

        public void CommitPrepared()
        {
            if (!_prepared || _preparedPacket == null)
            {
                throw new InvalidOperationException("Authoritative replication channel has no prepared publication to commit.");
            }

            ReadOnlySpan<ReplicationDisclosureChange> disclosures = _preparedPacket.DisclosureChanges;
            if (disclosures.Length != _pendingDisclosureCount)
            {
                throw new InvalidOperationException("Prepared disclosure count diverged from the packet buffer.");
            }

            for (int i = 0; i < disclosures.Length; i++)
            {
                if (!_disclosureLog.TryCommitPrepared(in disclosures[i]))
                {
                    throw new InvalidOperationException("Prepared disclosure sequences diverged from the disclosure log.");
                }
            }

            int baselineSlot = _nextBaselineSlot;
            int offset = baselineSlot * _replicationEntityCapacityPerSeat;
            _currentEntities.AsSpan(0, _currentCount).CopyTo(_baselineEntities.AsSpan(offset));
            _currentSchemaIds.AsSpan(0, _currentCount).CopyTo(_baselineSchemaIds.AsSpan(offset));
            _currentRevisions.AsSpan(0, _currentCount).CopyTo(_baselineRevisions.AsSpan(offset));
            _currentValues.AsSpan(0, _currentCount).CopyTo(_baselineValues.AsSpan(offset));
            _currentOwnership.AsSpan(0, _currentCount).CopyTo(_baselineOwnership.AsSpan(offset));
            _baselineCounts[baselineSlot] = _currentCount;
            _baselineIds[baselineSlot] = _pendingSnapshotId;
            _nextBaselineSlot = (baselineSlot + 1) % _baselineCapacity;
            _sessionEpoch = _pendingSessionEpoch;
            _lastSnapshotId = _pendingSnapshotId;
            ClearPreparedState();
        }

        public void CancelPrepared()
        {
            if (!_prepared)
            {
                return;
            }

            _preparedPacket?.Reset(default);
            ClearPreparedState();
        }

        private void StagePrepared(ulong sessionEpoch, ulong snapshotId, ReplicationPacketBuffer packet)
        {
            _pendingSessionEpoch = sessionEpoch;
            _pendingSnapshotId = snapshotId;
            _pendingDisclosureCount = packet.DisclosureChanges.Length;
            _preparedPacket = packet;
            _prepared = true;
        }

        private void ClearPreparedState()
        {
            _pendingSessionEpoch = 0;
            _pendingSnapshotId = 0;
            _pendingDisclosureCount = 0;
            _preparedPacket = null;
            _prepared = false;
            _currentCount = 0;
        }

        private ReplicationBuildResult BuildCurrentView(
            ReadOnlySpan<ReplicatedEntityState> states,
            ReadOnlySpan<ReplicationDisclosureInput> disclosures)
        {
            _currentCount = 0;
            if (states.Length > _replicationEntityCapacityPerSeat ||
                disclosures.Length > _replicationEntityCapacityPerSeat)
            {
                return ReplicationBuildResult.InvalidInput;
            }

            int previousDisclosureSlot = -1;
            for (int i = 0; i < disclosures.Length; i++)
            {
                ReplicationDisclosureInput disclosure = disclosures[i];
                if (!disclosure.Entity.IsValid || disclosure.Entity.Slot <= previousDisclosureSlot)
                {
                    return ReplicationBuildResult.InvalidInput;
                }

                previousDisclosureSlot = disclosure.Entity.Slot;
            }

            int disclosureIndex = 0;
            int previousStateSlot = -1;
            for (int i = 0; i < states.Length; i++)
            {
                ReplicatedEntityState state = states[i];
                if (!state.Entity.IsValid || state.SchemaId <= 0 || state.Entity.Slot <= previousStateSlot)
                {
                    _currentCount = 0;
                    return ReplicationBuildResult.InvalidInput;
                }

                previousStateSlot = state.Entity.Slot;
                while (disclosureIndex < disclosures.Length &&
                       disclosures[disclosureIndex].Entity.Slot < state.Entity.Slot)
                {
                    disclosureIndex++;
                }

                if (disclosureIndex == disclosures.Length ||
                    disclosures[disclosureIndex].Entity != state.Entity ||
                    !disclosures[disclosureIndex].CanReplicateLiveState)
                {
                    continue;
                }

                int write = _currentCount++;
                _currentEntities[write] = state.Entity;
                _currentSchemaIds[write] = state.SchemaId;
                _currentRevisions[write] = state.Revision;
                _currentValues[write] = state.Values;
                _currentOwnership[write] = state.Ownership;
            }

            return ReplicationBuildResult.Success;
        }

        private void CountDelta(
            int baselineSlot,
            out int upsertCount,
            out int removalCount,
            out int disclosureChangeCount)
        {
            upsertCount = 0;
            removalCount = 0;
            disclosureChangeCount = 0;
            int baselineOffset = baselineSlot * _replicationEntityCapacityPerSeat;
            int baselineCount = _baselineCounts[baselineSlot];
            int baselineIndex = 0;
            int currentIndex = 0;

            while (baselineIndex < baselineCount || currentIndex < _currentCount)
            {
                if (baselineIndex == baselineCount)
                {
                    upsertCount++;
                    disclosureChangeCount++;
                    currentIndex++;
                    continue;
                }

                if (currentIndex == _currentCount)
                {
                    CountAbsentBaseline(
                        _baselineEntities[baselineOffset + baselineIndex],
                        ref removalCount,
                        ref disclosureChangeCount);
                    baselineIndex++;
                    continue;
                }

                int baselineStorageIndex = baselineOffset + baselineIndex;
                NetworkEntityHandle baselineEntity = _baselineEntities[baselineStorageIndex];
                NetworkEntityHandle currentEntity = _currentEntities[currentIndex];
                if (baselineEntity.Slot < currentEntity.Slot)
                {
                    CountAbsentBaseline(
                        baselineEntity,
                        ref removalCount,
                        ref disclosureChangeCount);
                    baselineIndex++;
                }
                else if (currentEntity.Slot < baselineEntity.Slot)
                {
                    upsertCount++;
                    disclosureChangeCount++;
                    currentIndex++;
                }
                else
                {
                    if (baselineEntity != currentEntity)
                    {
                        removalCount++;
                        upsertCount++;
                        disclosureChangeCount++;
                    }
                    else if (!StateEquals(baselineStorageIndex, currentIndex))
                    {
                        upsertCount++;
                    }

                    baselineIndex++;
                    currentIndex++;
                }
            }
        }

        private void WriteDelta(int baselineSlot, ulong snapshotId, ReplicationPacketBuffer packet)
        {
            int baselineOffset = baselineSlot * _replicationEntityCapacityPerSeat;
            int baselineCount = _baselineCounts[baselineSlot];
            int baselineIndex = 0;
            int currentIndex = 0;

            while (baselineIndex < baselineCount || currentIndex < _currentCount)
            {
                if (baselineIndex == baselineCount)
                {
                    AppendCurrentReveal(currentIndex++, snapshotId, packet);
                    continue;
                }

                if (currentIndex == _currentCount)
                {
                    AppendAbsentBaseline(
                        _baselineEntities[baselineOffset + baselineIndex],
                        snapshotId,
                        packet);
                    baselineIndex++;
                    continue;
                }

                int baselineStorageIndex = baselineOffset + baselineIndex;
                NetworkEntityHandle baselineEntity = _baselineEntities[baselineStorageIndex];
                NetworkEntityHandle currentEntity = _currentEntities[currentIndex];
                if (baselineEntity.Slot < currentEntity.Slot)
                {
                    AppendAbsentBaseline(baselineEntity, snapshotId, packet);
                    baselineIndex++;
                }
                else if (currentEntity.Slot < baselineEntity.Slot)
                {
                    AppendCurrentReveal(currentIndex++, snapshotId, packet);
                }
                else
                {
                    if (baselineEntity != currentEntity)
                    {
                        packet.AddRemoval(baselineEntity);
                        AppendCurrentReveal(currentIndex, snapshotId, packet);
                    }
                    else if (!StateEquals(baselineStorageIndex, currentIndex))
                    {
                        ReplicatedEntityState state = GetCurrentState(currentIndex);
                        packet.AddUpsert(in state);
                    }

                    baselineIndex++;
                    currentIndex++;
                }
            }
        }

        private void CountAbsentBaseline(
            NetworkEntityHandle baselineEntity,
            ref int removalCount,
            ref int disclosureChangeCount)
        {
            if (_entities.TryResolve(baselineEntity, out _))
            {
                disclosureChangeCount++;
            }
            else
            {
                removalCount++;
            }
        }

        private void AppendAbsentBaseline(
            NetworkEntityHandle baselineEntity,
            ulong snapshotId,
            ReplicationPacketBuffer packet)
        {
            if (_entities.TryResolve(baselineEntity, out _))
            {
                AppendPreparedDisclosureChange(
                    packet,
                    snapshotId,
                    baselineEntity,
                    ReplicationDisclosureChangeKind.Conceal);
            }
            else
            {
                packet.AddRemoval(baselineEntity);
            }
        }

        private void AppendCurrentReveal(int currentIndex, ulong snapshotId, ReplicationPacketBuffer packet)
        {
            ReplicatedEntityState state = GetCurrentState(currentIndex);
            AppendPreparedDisclosureChange(
                packet,
                snapshotId,
                state.Entity,
                ReplicationDisclosureChangeKind.Reveal);
            packet.AddUpsert(in state);
        }

        private bool StateEquals(int baselineStorageIndex, int currentIndex)
        {
            return _baselineSchemaIds[baselineStorageIndex] == _currentSchemaIds[currentIndex] &&
                   _baselineRevisions[baselineStorageIndex] == _currentRevisions[currentIndex] &&
                   _baselineValues[baselineStorageIndex] == _currentValues[currentIndex] &&
                   _baselineOwnership[baselineStorageIndex] == _currentOwnership[currentIndex];
        }

        private ReplicatedEntityState GetCurrentState(int index)
        {
            return new ReplicatedEntityState(
                _currentEntities[index],
                _currentSchemaIds[index],
                _currentRevisions[index],
                _currentValues[index],
                _currentOwnership[index]);
        }

        private ReplicationBuildResult ValidateHeader(ulong sessionEpoch, ulong snapshotId)
        {
            if (sessionEpoch == 0 || snapshotId == 0)
            {
                return ReplicationBuildResult.InvalidInput;
            }

            if (_sessionEpoch != 0 && _sessionEpoch != sessionEpoch)
            {
                return ReplicationBuildResult.EpochMismatch;
            }

            if (_lastSnapshotId != 0 && snapshotId <= _lastSnapshotId)
            {
                return ReplicationBuildResult.SnapshotOutOfOrder;
            }

            return ReplicationBuildResult.Success;
        }

        private int FindBaseline(ulong snapshotId)
        {
            for (int i = 0; i < _baselineCapacity; i++)
            {
                if (_baselineIds[i] == snapshotId)
                {
                    return i;
                }
            }

            return -1;
        }

        private void AppendPreparedDisclosureChange(
            ReplicationPacketBuffer packet,
            ulong snapshotId,
            NetworkEntityHandle entity,
            ReplicationDisclosureChangeKind kind)
        {
            ulong sequence = checked(_disclosureLog.NextSequence + (ulong)_pendingDisclosureCount);
            var change = new ReplicationDisclosureChange(sequence, snapshotId, entity, kind);
            packet.AddDisclosureChange(in change);
            _pendingDisclosureCount++;
        }
    }
}
