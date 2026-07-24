using System;

namespace Ludots.Core.Networking.Protocol
{
    /// <summary>
    /// Fixed-capacity out-of-order snapshot fragment reassembler.
    /// All managed storage is preallocated; rejected fragments never mutate assembler state.
    /// A completed snapshot requires an explicit <see cref="Reset"/> before accepting a new one.
    /// </summary>
    public sealed class SnapshotFragmentReassembler
    {
        private readonly byte[] _payload;
        private readonly bool[] _received;
        private readonly int _maxSnapshotBytes;
        private readonly int _maxFragments;

        private SnapshotReassemblyPhase _phase;
        private ulong _sessionEpoch;
        private ulong _snapshotId;
        private ushort _fragmentCount;
        private uint _totalPayloadLength;
        private uint _chunkSize;
        private bool _chunkSizeKnown;
        private int _receivedCount;

        public SnapshotFragmentReassembler(int maxSnapshotBytes, int maxFragments)
        {
            if (maxSnapshotBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxSnapshotBytes));
            }

            if (maxFragments <= 0 || maxFragments > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(maxFragments));
            }

            _maxSnapshotBytes = maxSnapshotBytes;
            _maxFragments = maxFragments;
            _payload = new byte[maxSnapshotBytes];
            _received = new bool[maxFragments];
            _phase = SnapshotReassemblyPhase.Empty;
        }

        public int MaxSnapshotBytes => _maxSnapshotBytes;
        public int MaxFragments => _maxFragments;
        public SnapshotReassemblyPhase Phase => _phase;
        public ulong SessionEpoch => _sessionEpoch;
        public ulong SnapshotId => _snapshotId;
        public ushort FragmentCount => _fragmentCount;
        public uint TotalPayloadLength => _totalPayloadLength;
        public int ReceivedFragmentCount => _receivedCount;

        /// <summary>
        /// Exact assembled payload. Only valid while <see cref="Phase"/> is <see cref="SnapshotReassemblyPhase.Completed"/>.
        /// </summary>
        public ReadOnlySpan<byte> AssembledPayload
        {
            get
            {
                if (_phase != SnapshotReassemblyPhase.Completed)
                {
                    throw new InvalidOperationException(
                        "Assembled payload is only available after reassembly completes.");
                }

                return _payload.AsSpan(0, (int)_totalPayloadLength);
            }
        }

        public void Reset()
        {
            ClearAssemblyState();
            _phase = SnapshotReassemblyPhase.Empty;
        }

        public SnapshotReassemblyStatus TryAcceptWirePayload(ReadOnlySpan<byte> source)
        {
            NetworkWireCodecStatus decode = SnapshotFragmentWireCodec.TryDecode(
                source,
                out NetworkSnapshotFragmentHeader header,
                out ReadOnlySpan<byte> fragmentData);
            if (decode != NetworkWireCodecStatus.Success)
            {
                return SnapshotReassemblyStatus.InvalidFragment;
            }

            return TryAccept(in header, fragmentData);
        }

        public SnapshotReassemblyStatus TryAccept(
            in NetworkSnapshotFragmentHeader header,
            ReadOnlySpan<byte> fragmentData)
        {
            if (_phase == SnapshotReassemblyPhase.Completed)
            {
                return SnapshotReassemblyStatus.StaleOrOutOfOrder;
            }

            if (header.FragmentCount == 0 || header.FragmentIndex >= header.FragmentCount)
            {
                return SnapshotReassemblyStatus.InvalidFragment;
            }

            if (header.FragmentPayloadLength != fragmentData.Length)
            {
                return SnapshotReassemblyStatus.InvalidFragment;
            }

            if (header.FragmentPayloadLength > header.TotalPayloadLength)
            {
                return SnapshotReassemblyStatus.InvalidFragment;
            }

            if (header.FragmentCount > _maxFragments || header.TotalPayloadLength > (uint)_maxSnapshotBytes)
            {
                return SnapshotReassemblyStatus.CapacityExceeded;
            }

            if (!TryDeriveChunkSize(in header, out uint chunkSize))
            {
                return SnapshotReassemblyStatus.InvalidFragment;
            }

            if (!TryGetFragmentPlacement(in header, chunkSize, out int absoluteOffset, out int expectedLength))
            {
                return SnapshotReassemblyStatus.InvalidFragment;
            }

            if (expectedLength != fragmentData.Length)
            {
                return SnapshotReassemblyStatus.InvalidFragment;
            }

            if (_phase == SnapshotReassemblyPhase.Empty)
            {
                return AcceptFirstFragment(in header, fragmentData, chunkSize, absoluteOffset);
            }

            if (header.SessionEpoch != _sessionEpoch || header.SnapshotId != _snapshotId)
            {
                return SnapshotReassemblyStatus.MixedMetadata;
            }

            if (header.FragmentCount != _fragmentCount || header.TotalPayloadLength != _totalPayloadLength)
            {
                return SnapshotReassemblyStatus.MixedMetadata;
            }

            if (_chunkSizeKnown && chunkSize != _chunkSize)
            {
                return SnapshotReassemblyStatus.MixedMetadata;
            }

            int index = header.FragmentIndex;
            if (_received[index])
            {
                ReadOnlySpan<byte> existing = _payload.AsSpan(absoluteOffset, expectedLength);
                if (existing.SequenceEqual(fragmentData))
                {
                    return SnapshotReassemblyStatus.Duplicate;
                }

                return SnapshotReassemblyStatus.InvalidFragment;
            }

            // Commit only after all validation succeeds.
            if (!_chunkSizeKnown)
            {
                _chunkSize = chunkSize;
                _chunkSizeKnown = true;
            }

            fragmentData.CopyTo(_payload.AsSpan(absoluteOffset, expectedLength));
            _received[index] = true;
            _receivedCount++;

            if (_receivedCount == _fragmentCount)
            {
                _phase = SnapshotReassemblyPhase.Completed;
                return SnapshotReassemblyStatus.Completed;
            }

            return SnapshotReassemblyStatus.Incomplete;
        }

        private SnapshotReassemblyStatus AcceptFirstFragment(
            in NetworkSnapshotFragmentHeader header,
            ReadOnlySpan<byte> fragmentData,
            uint chunkSize,
            int absoluteOffset)
        {
            _sessionEpoch = header.SessionEpoch;
            _snapshotId = header.SnapshotId;
            _fragmentCount = header.FragmentCount;
            _totalPayloadLength = header.TotalPayloadLength;
            _chunkSize = chunkSize;
            _chunkSizeKnown = true;
            _receivedCount = 0;

            // Clear only the slots we may use; payload bytes outside the snapshot are unused.
            for (int i = 0; i < _fragmentCount; i++)
            {
                _received[i] = false;
            }

            fragmentData.CopyTo(_payload.AsSpan(absoluteOffset, fragmentData.Length));
            _received[header.FragmentIndex] = true;
            _receivedCount = 1;
            _phase = SnapshotReassemblyPhase.Assembling;

            if (_receivedCount == _fragmentCount)
            {
                _phase = SnapshotReassemblyPhase.Completed;
                return SnapshotReassemblyStatus.Completed;
            }

            return SnapshotReassemblyStatus.Incomplete;
        }

        private void ClearAssemblyState()
        {
            _sessionEpoch = 0;
            _snapshotId = 0;
            _fragmentCount = 0;
            _totalPayloadLength = 0;
            _chunkSize = 0;
            _chunkSizeKnown = false;
            _receivedCount = 0;
            for (int i = 0; i < _received.Length; i++)
            {
                _received[i] = false;
            }
        }

        private static bool TryDeriveChunkSize(in NetworkSnapshotFragmentHeader header, out uint chunkSize)
        {
            chunkSize = 0;
            if (header.FragmentCount == 1)
            {
                if (header.FragmentPayloadLength != header.TotalPayloadLength)
                {
                    return false;
                }

                chunkSize = header.TotalPayloadLength;
                return true;
            }

            if (header.FragmentIndex + 1 < header.FragmentCount)
            {
                if (header.FragmentPayloadLength == 0)
                {
                    return false;
                }

                chunkSize = header.FragmentPayloadLength;
                ulong prefix = (ulong)(header.FragmentCount - 1u) * chunkSize;
                if (prefix >= header.TotalPayloadLength)
                {
                    return false;
                }

                uint lastLength = header.TotalPayloadLength - (uint)prefix;
                return lastLength > 0 && lastLength <= chunkSize;
            }

            uint last = header.FragmentPayloadLength;
            if (header.TotalPayloadLength < last)
            {
                return false;
            }

            uint prefixBytes = header.TotalPayloadLength - last;
            ushort nonFinalCount = (ushort)(header.FragmentCount - 1);
            if (prefixBytes % nonFinalCount != 0)
            {
                return false;
            }

            chunkSize = prefixBytes / nonFinalCount;
            if (chunkSize == 0)
            {
                return false;
            }

            return last <= chunkSize;
        }

        private static bool TryGetFragmentPlacement(
            in NetworkSnapshotFragmentHeader header,
            uint chunkSize,
            out int absoluteOffset,
            out int expectedLength)
        {
            absoluteOffset = 0;
            expectedLength = 0;

            if (header.FragmentCount == 1)
            {
                absoluteOffset = 0;
                expectedLength = (int)header.TotalPayloadLength;
                return expectedLength == header.FragmentPayloadLength;
            }

            if (header.FragmentIndex + 1 < header.FragmentCount)
            {
                ulong offset = (ulong)header.FragmentIndex * chunkSize;
                if (offset > int.MaxValue)
                {
                    return false;
                }

                absoluteOffset = (int)offset;
                expectedLength = (int)chunkSize;
                return expectedLength == header.FragmentPayloadLength;
            }

            ulong lastOffset = (ulong)(header.FragmentCount - 1u) * chunkSize;
            if (lastOffset > header.TotalPayloadLength || lastOffset > int.MaxValue)
            {
                return false;
            }

            absoluteOffset = (int)lastOffset;
            expectedLength = (int)(header.TotalPayloadLength - (uint)lastOffset);
            return expectedLength == header.FragmentPayloadLength;
        }
    }
}
