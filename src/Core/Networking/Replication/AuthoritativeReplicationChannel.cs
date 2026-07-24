using System;

namespace Ludots.Core.Networking.Replication
{
    public sealed class AuthoritativeReplicationChannel
    {
        private readonly int _entityCapacity;
        private readonly int _baselineCapacity;
        private readonly ReplicationDisclosureChangeLog _disclosureLog;
        private readonly bool[] _allowed;
        private readonly uint[] _allowedGenerations;
        private readonly bool[] _stateSeen;
        private readonly bool[] _currentActive;
        private readonly ReplicatedEntityState[] _currentStates;
        private readonly bool[] _baselineActive;
        private readonly ReplicatedEntityState[] _baselineStates;
        private readonly ulong[] _baselineIds;
        private int _nextBaselineSlot;
        private ulong _sessionEpoch;
        private ulong _lastSnapshotId;

        public AuthoritativeReplicationChannel(
            int entityCapacity,
            int baselineCapacity,
            ReplicationDisclosureChangeLog disclosureLog)
        {
            if (entityCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(entityCapacity));
            }

            if (baselineCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(baselineCapacity));
            }

            _entityCapacity = entityCapacity;
            _baselineCapacity = baselineCapacity;
            _disclosureLog = disclosureLog ?? throw new ArgumentNullException(nameof(disclosureLog));
            _allowed = new bool[entityCapacity];
            _allowedGenerations = new uint[entityCapacity];
            _stateSeen = new bool[entityCapacity];
            _currentActive = new bool[entityCapacity];
            _currentStates = new ReplicatedEntityState[entityCapacity];
            _baselineActive = new bool[checked(entityCapacity * baselineCapacity)];
            _baselineStates = new ReplicatedEntityState[checked(entityCapacity * baselineCapacity)];
            _baselineIds = new ulong[baselineCapacity];
        }

        public ReplicationBuildResult BuildFull(
            ulong sessionEpoch,
            uint tick,
            ulong snapshotId,
            ReadOnlySpan<ReplicatedEntityState> states,
            ReadOnlySpan<ReplicationDisclosureInput> disclosures,
            ReplicationPacketBuffer packet)
        {
            if (packet == null)
            {
                return ReplicationBuildResult.InvalidInput;
            }

            packet.Reset(default);
            ReplicationBuildResult headerResult = ValidateHeader(sessionEpoch, snapshotId);
            if (headerResult != ReplicationBuildResult.Success)
            {
                return headerResult;
            }

            ReplicationBuildResult viewResult = BuildCurrentView(states, disclosures, out int visibleCount);
            if (viewResult != ReplicationBuildResult.Success)
            {
                return viewResult;
            }

            if (visibleCount > packet.EntityCapacity)
            {
                return ReplicationBuildResult.PacketCapacityExceeded;
            }

            if (visibleCount > _disclosureLog.AvailableCapacity)
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

            for (int slot = 0; slot < _entityCapacity; slot++)
            {
                if (!_currentActive[slot])
                {
                    continue;
                }

                ReplicatedEntityState state = _currentStates[slot];
                packet.AddUpsert(in state);
                if (!_disclosureLog.TryAppend(
                        snapshotId,
                        state.Entity,
                        ReplicationDisclosureChangeKind.Reveal,
                        out ReplicationDisclosureChange change))
                {
                    throw new InvalidOperationException("Disclosure log capacity changed during full snapshot construction.");
                }

                packet.AddDisclosureChange(in change);
            }

            StoreBaseline(snapshotId);
            CommitHeader(sessionEpoch, snapshotId);
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
            if (packet == null)
            {
                return ReplicationBuildResult.InvalidInput;
            }

            packet.Reset(default);
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

            ReplicationBuildResult viewResult = BuildCurrentView(states, disclosures, out _);
            if (viewResult != ReplicationBuildResult.Success)
            {
                return viewResult;
            }

            int baselineOffset = baselineSlot * _entityCapacity;
            int upsertCount = 0;
            int removalCount = 0;
            int disclosureChangeCount = 0;
            for (int slot = 0; slot < _entityCapacity; slot++)
            {
                bool currentActive = _currentActive[slot];
                bool baselineActive = _baselineActive[baselineOffset + slot];
                if (currentActive)
                {
                    ReplicatedEntityState current = _currentStates[slot];
                    if (!baselineActive)
                    {
                        upsertCount++;
                        disclosureChangeCount++;
                        continue;
                    }

                    ReplicatedEntityState baseline = _baselineStates[baselineOffset + slot];
                    if (baseline.Entity != current.Entity)
                    {
                        removalCount++;
                        upsertCount++;
                        disclosureChangeCount++;
                    }
                    else if (baseline != current)
                    {
                        upsertCount++;
                    }

                    continue;
                }

                if (!baselineActive)
                {
                    continue;
                }

                ReplicatedEntityState baselineState = _baselineStates[baselineOffset + slot];
                if (_allowed[slot] && _allowedGenerations[slot] == baselineState.Entity.Generation)
                {
                    removalCount++;
                }
                else
                {
                    disclosureChangeCount++;
                }
            }

            if (upsertCount > packet.EntityCapacity ||
                removalCount > packet.EntityCapacity ||
                disclosureChangeCount > packet.EntityCapacity)
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

            for (int slot = 0; slot < _entityCapacity; slot++)
            {
                bool currentActive = _currentActive[slot];
                bool baselineActive = _baselineActive[baselineOffset + slot];
                if (currentActive)
                {
                    ReplicatedEntityState current = _currentStates[slot];
                    if (!baselineActive)
                    {
                        AppendDisclosureChange(packet, snapshotId, current.Entity, ReplicationDisclosureChangeKind.Reveal);
                        packet.AddUpsert(in current);
                        continue;
                    }

                    ReplicatedEntityState baseline = _baselineStates[baselineOffset + slot];
                    if (baseline.Entity != current.Entity)
                    {
                        packet.AddRemoval(baseline.Entity);
                        AppendDisclosureChange(packet, snapshotId, current.Entity, ReplicationDisclosureChangeKind.Reveal);
                        packet.AddUpsert(in current);
                    }
                    else if (baseline != current)
                    {
                        packet.AddUpsert(in current);
                    }

                    continue;
                }

                if (!baselineActive)
                {
                    continue;
                }

                ReplicatedEntityState baselineState = _baselineStates[baselineOffset + slot];
                if (_allowed[slot] && _allowedGenerations[slot] == baselineState.Entity.Generation)
                {
                    packet.AddRemoval(baselineState.Entity);
                }
                else
                {
                    AppendDisclosureChange(packet, snapshotId, baselineState.Entity, ReplicationDisclosureChangeKind.Conceal);
                }
            }

            StoreBaseline(snapshotId);
            CommitHeader(sessionEpoch, snapshotId);
            return ReplicationBuildResult.Success;
        }

        private ReplicationBuildResult BuildCurrentView(
            ReadOnlySpan<ReplicatedEntityState> states,
            ReadOnlySpan<ReplicationDisclosureInput> disclosures,
            out int visibleCount)
        {
            Array.Clear(_allowed);
            Array.Clear(_allowedGenerations);
            Array.Clear(_stateSeen);
            Array.Clear(_currentActive);
            visibleCount = 0;

            for (int i = 0; i < disclosures.Length; i++)
            {
                ReplicationDisclosureInput disclosure = disclosures[i];
                int slot = disclosure.Entity.Slot;
                if (!disclosure.Entity.IsValid ||
                    (uint)slot >= (uint)_entityCapacity ||
                    _allowedGenerations[slot] != 0)
                {
                    return ReplicationBuildResult.InvalidInput;
                }

                _allowedGenerations[slot] = disclosure.Entity.Generation;
                _allowed[slot] = disclosure.CanReplicateLiveState;
            }

            for (int i = 0; i < states.Length; i++)
            {
                ReplicatedEntityState state = states[i];
                int slot = state.Entity.Slot;
                if (!state.Entity.IsValid ||
                    state.SchemaId <= 0 ||
                    (uint)slot >= (uint)_entityCapacity ||
                    _stateSeen[slot])
                {
                    return ReplicationBuildResult.InvalidInput;
                }

                _stateSeen[slot] = true;
                if (!_allowed[slot] || _allowedGenerations[slot] != state.Entity.Generation)
                {
                    continue;
                }

                _currentStates[slot] = state;
                _currentActive[slot] = true;
                visibleCount++;
            }

            return ReplicationBuildResult.Success;
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

        private void StoreBaseline(ulong snapshotId)
        {
            int baselineSlot = _nextBaselineSlot;
            int offset = baselineSlot * _entityCapacity;
            for (int slot = 0; slot < _entityCapacity; slot++)
            {
                _baselineActive[offset + slot] = _currentActive[slot];
                if (_currentActive[slot])
                {
                    _baselineStates[offset + slot] = _currentStates[slot];
                }
            }

            _baselineIds[baselineSlot] = snapshotId;
            _nextBaselineSlot = (baselineSlot + 1) % _baselineCapacity;
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

        private void AppendDisclosureChange(
            ReplicationPacketBuffer packet,
            ulong snapshotId,
            NetworkEntityHandle entity,
            ReplicationDisclosureChangeKind kind)
        {
            if (!_disclosureLog.TryAppend(snapshotId, entity, kind, out ReplicationDisclosureChange change))
            {
                throw new InvalidOperationException("Disclosure log capacity changed during delta construction.");
            }

            packet.AddDisclosureChange(in change);
        }

        private void CommitHeader(ulong sessionEpoch, ulong snapshotId)
        {
            _sessionEpoch = sessionEpoch;
            _lastSnapshotId = snapshotId;
        }
    }
}
