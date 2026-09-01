using System;

namespace Ludots.Core.Networking.Protocol
{
    /// <summary>
    /// Fixed-capacity out-of-order command-batch fragment reassembler.
    /// Fragments travel on the ReliableOrdered command channel, but reassembly still accepts
    /// out-of-order arrivals for deterministic fault tests. All managed storage is preallocated;
    /// rejected fragments never mutate assembler state. A completed batch requires an explicit
    /// <see cref="Reset"/> before accepting a new one.
    /// </summary>
    public sealed class CommandFragmentReassembler
    {
        private readonly byte[] _payload;
        private readonly bool[] _received;
        private readonly int _maxCommandPayloadBytes;
        private readonly int _maxFragments;

        private CommandReassemblyPhase _phase;
        private ulong _sessionEpoch;
        private ulong _clientBatchSequence;
        private ushort _fragmentCount;
        private uint _totalPayloadLength;
        private uint _chunkSize;
        private bool _chunkSizeKnown;
        private int _receivedCount;

        public CommandFragmentReassembler(int maxCommandPayloadBytes, int maxFragments)
        {
            if (maxCommandPayloadBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxCommandPayloadBytes));
            }

            if (maxFragments <= 0 || maxFragments > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(maxFragments));
            }

            _maxCommandPayloadBytes = maxCommandPayloadBytes;
            _maxFragments = maxFragments;
            _payload = new byte[maxCommandPayloadBytes];
            _received = new bool[maxFragments];
            _phase = CommandReassemblyPhase.Empty;
        }

        public int MaxCommandPayloadBytes => _maxCommandPayloadBytes;
        public int MaxFragments => _maxFragments;
        public CommandReassemblyPhase Phase => _phase;
        public ulong SessionEpoch => _sessionEpoch;
        public ulong ClientBatchSequence => _clientBatchSequence;
        public ushort FragmentCount => _fragmentCount;
        public uint TotalPayloadLength => _totalPayloadLength;
        public int ReceivedFragmentCount => _receivedCount;

        /// <summary>
        /// Exact assembled command-batch payload. Only valid while <see cref="Phase"/> is
        /// <see cref="CommandReassemblyPhase.Completed"/>.
        /// </summary>
        public ReadOnlySpan<byte> AssembledPayload
        {
            get
            {
                if (_phase != CommandReassemblyPhase.Completed)
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
            _phase = CommandReassemblyPhase.Empty;
        }

        public CommandReassemblyStatus TryAcceptWirePayload(ReadOnlySpan<byte> source)
        {
            NetworkWireCodecStatus decode = CommandFragmentWireCodec.TryDecode(
                source,
                out NetworkCommandFragmentHeader header,
                out ReadOnlySpan<byte> fragmentData);
            if (decode != NetworkWireCodecStatus.Success)
            {
                return CommandReassemblyStatus.InvalidFragment;
            }

            return TryAccept(in header, fragmentData);
        }

        public CommandReassemblyStatus TryAccept(
            in NetworkCommandFragmentHeader header,
            ReadOnlySpan<byte> fragmentData)
        {
            if (_phase == CommandReassemblyPhase.Completed)
            {
                return CommandReassemblyStatus.StaleOrOutOfOrder;
            }

            if (header.SessionEpoch == 0 || header.ClientBatchSequence == 0)
            {
                return CommandReassemblyStatus.InvalidFragment;
            }

            if (header.FragmentCount == 0 || header.FragmentIndex >= header.FragmentCount)
            {
                return CommandReassemblyStatus.InvalidFragment;
            }

            if (header.FragmentPayloadLength != fragmentData.Length)
            {
                return CommandReassemblyStatus.InvalidFragment;
            }

            if (header.FragmentPayloadLength > header.TotalPayloadLength)
            {
                return CommandReassemblyStatus.InvalidFragment;
            }

            if (header.FragmentCount > _maxFragments || header.TotalPayloadLength > (uint)_maxCommandPayloadBytes)
            {
                return CommandReassemblyStatus.CapacityExceeded;
            }

            if (!TryDeriveChunkSize(in header, out uint chunkSize))
            {
                return CommandReassemblyStatus.InvalidFragment;
            }

            if (!TryGetFragmentPlacement(in header, chunkSize, out int absoluteOffset, out int expectedLength))
            {
                return CommandReassemblyStatus.InvalidFragment;
            }

            if (expectedLength != fragmentData.Length)
            {
                return CommandReassemblyStatus.InvalidFragment;
            }

            if (_phase == CommandReassemblyPhase.Empty)
            {
                return AcceptFirstFragment(in header, fragmentData, chunkSize, absoluteOffset);
            }

            if (header.SessionEpoch != _sessionEpoch || header.ClientBatchSequence != _clientBatchSequence)
            {
                return CommandReassemblyStatus.MixedMetadata;
            }

            if (header.FragmentCount != _fragmentCount || header.TotalPayloadLength != _totalPayloadLength)
            {
                return CommandReassemblyStatus.MixedMetadata;
            }

            if (_chunkSizeKnown && chunkSize != _chunkSize)
            {
                return CommandReassemblyStatus.MixedMetadata;
            }

            int index = header.FragmentIndex;
            if (_received[index])
            {
                ReadOnlySpan<byte> existing = _payload.AsSpan(absoluteOffset, expectedLength);
                if (existing.SequenceEqual(fragmentData))
                {
                    return CommandReassemblyStatus.Duplicate;
                }

                return CommandReassemblyStatus.InvalidFragment;
            }

            // Commit only after all validation succeeds — rejected paths never mutate state.
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
                _phase = CommandReassemblyPhase.Completed;
                return CommandReassemblyStatus.Completed;
            }

            return CommandReassemblyStatus.Incomplete;
        }

        private CommandReassemblyStatus AcceptFirstFragment(
            in NetworkCommandFragmentHeader header,
            ReadOnlySpan<byte> fragmentData,
            uint chunkSize,
            int absoluteOffset)
        {
            _sessionEpoch = header.SessionEpoch;
            _clientBatchSequence = header.ClientBatchSequence;
            _fragmentCount = header.FragmentCount;
            _totalPayloadLength = header.TotalPayloadLength;
            _chunkSize = chunkSize;
            _chunkSizeKnown = true;
            _receivedCount = 0;

            for (int i = 0; i < _fragmentCount; i++)
            {
                _received[i] = false;
            }

            fragmentData.CopyTo(_payload.AsSpan(absoluteOffset, fragmentData.Length));
            _received[header.FragmentIndex] = true;
            _receivedCount = 1;
            _phase = CommandReassemblyPhase.Assembling;

            if (_receivedCount == _fragmentCount)
            {
                _phase = CommandReassemblyPhase.Completed;
                return CommandReassemblyStatus.Completed;
            }

            return CommandReassemblyStatus.Incomplete;
        }

        private void ClearAssemblyState()
        {
            _sessionEpoch = 0;
            _clientBatchSequence = 0;
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

        private static bool TryDeriveChunkSize(in NetworkCommandFragmentHeader header, out uint chunkSize)
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
            in NetworkCommandFragmentHeader header,
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
