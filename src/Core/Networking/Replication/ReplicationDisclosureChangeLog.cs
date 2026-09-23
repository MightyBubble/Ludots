using System;

namespace Ludots.Core.Networking.Replication
{
    public sealed class ReplicationDisclosureChangeLog
    {
        private readonly ReplicationDisclosureChange[] _records;
        private int _head;
        private int _count;
        private ulong _nextSequence = 1;

        public ReplicationDisclosureChangeLog(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _records = new ReplicationDisclosureChange[capacity];
        }

        public int Capacity => _records.Length;
        public int Count => _count;
        public int AvailableCapacity => Capacity - _count;
        public ulong NextSequence => _nextSequence;

        public bool TryAppend(
            ulong snapshotId,
            NetworkEntityHandle entity,
            ReplicationDisclosureChangeKind kind,
            out ReplicationDisclosureChange change)
        {
            change = default;
            if (_count == Capacity || snapshotId == 0 || !entity.IsValid ||
                (kind != ReplicationDisclosureChangeKind.Reveal && kind != ReplicationDisclosureChangeKind.Conceal))
            {
                return false;
            }

            change = new ReplicationDisclosureChange(_nextSequence++, snapshotId, entity, kind);
            int index = (_head + _count) % Capacity;
            _records[index] = change;
            _count++;
            return true;
        }

        /// <summary>
        /// Commits a previously prepared disclosure change. The sequence must equal <see cref="NextSequence"/>.
        /// </summary>
        public bool TryCommitPrepared(in ReplicationDisclosureChange change)
        {
            if (_count == Capacity ||
                change.Sequence != _nextSequence ||
                change.SnapshotId == 0 ||
                !change.Entity.IsValid ||
                (change.Kind != ReplicationDisclosureChangeKind.Reveal &&
                 change.Kind != ReplicationDisclosureChangeKind.Conceal))
            {
                return false;
            }

            int index = (_head + _count) % Capacity;
            _records[index] = change;
            _count++;
            _nextSequence++;
            return true;
        }

        public int CopyAfter(ulong sequence, Span<ReplicationDisclosureChange> destination)
        {
            int written = 0;
            for (int i = 0; i < _count && written < destination.Length; i++)
            {
                ReplicationDisclosureChange record = _records[(_head + i) % Capacity];
                if (record.Sequence > sequence)
                {
                    destination[written++] = record;
                }
            }

            return written;
        }

        public bool TryAcknowledgeThrough(ulong sequence)
        {
            if (_count == 0)
            {
                return false;
            }

            int removed = 0;
            while (removed < _count)
            {
                ReplicationDisclosureChange record = _records[(_head + removed) % Capacity];
                if (record.Sequence > sequence)
                {
                    break;
                }

                removed++;
            }

            if (removed == 0)
            {
                return false;
            }

            _head = (_head + removed) % Capacity;
            _count -= removed;
            return true;
        }
    }
}
