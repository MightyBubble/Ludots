using System;
using Ludots.Core.Networking.Protocol;

namespace Ludots.Core.Networking.FixedInput
{
    /// <summary>
    /// Fixed-capacity client fixed-input outbox. Builds redundant strictly ordered batches,
    /// applies acknowledgement masks, drops committed frames, and never allocates on the hot path.
    /// </summary>
    public sealed class FixedInputClientOutbox
    {
        private readonly FixedInputProtocolConfig _config;
        private readonly int _capacity;
        private readonly int _payloadBytes;

        private readonly uint[] _ticks;
        private readonly byte[] _payloads;
        private readonly bool[] _occupied;
        private readonly bool[] _needsSend;

        private int _count;
        private uint _highestEnqueuedTick;
        private bool _hasEnqueued;
        private uint _appliedCommittedThrough;
        private uint _appliedLatestReceived;
        private bool _hasAppliedAck;
        private ulong _appliedAcknowledgementVersion;

        public FixedInputClientOutbox(in FixedInputProtocolConfig config, int pendingFrameCapacity)
        {
            config.EnsureValid();
            if (pendingFrameCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(pendingFrameCapacity));
            }

            _config = config;
            _capacity = pendingFrameCapacity;
            _payloadBytes = config.FramePayloadBytes;
            _ticks = new uint[_capacity];
            _payloads = new byte[checked(_capacity * _payloadBytes)];
            _occupied = new bool[_capacity];
            _needsSend = new bool[_capacity];
        }

        public int PendingCount => _count;
        public int Capacity => _capacity;
        public bool HasEnqueued => _hasEnqueued;
        public uint HighestEnqueuedTick => _highestEnqueuedTick;
        public bool HasAppliedAcknowledgement => _hasAppliedAck;
        public uint AppliedCommittedThrough => _appliedCommittedThrough;
        public uint AppliedLatestReceived => _appliedLatestReceived;
        public ulong AppliedAcknowledgementVersion => _appliedAcknowledgementVersion;

        public FixedInputOutboxEnqueueStatus TryEnqueue(uint targetTick, ReadOnlySpan<byte> payload)
        {
            if (payload.Length != _payloadBytes)
            {
                return FixedInputOutboxEnqueueStatus.PayloadMismatch;
            }

            if (!FixedInputWireCodec.IsValidInputTargetTick(targetTick))
            {
                return FixedInputOutboxEnqueueStatus.InvalidInput;
            }

            if (_hasEnqueued && targetTick <= _highestEnqueuedTick)
            {
                return FixedInputOutboxEnqueueStatus.TickNotIncreasing;
            }

            if (_count >= _capacity)
            {
                return FixedInputOutboxEnqueueStatus.CapacityExceeded;
            }

            int slot = FindFreeSlot();
            _ticks[slot] = targetTick;
            payload.CopyTo(_payloads.AsSpan(slot * _payloadBytes, _payloadBytes));
            _occupied[slot] = true;
            _needsSend[slot] = true;
            _count++;
            _highestEnqueuedTick = targetTick;
            _hasEnqueued = true;
            return FixedInputOutboxEnqueueStatus.Enqueued;
        }

        public FixedInputAckApplyStatus TryApplyAcknowledgement(in NetworkFixedInputAcknowledgement acknowledgement)
        {
            if (acknowledgement.SessionEpoch != _config.SessionEpoch)
            {
                return FixedInputAckApplyStatus.EpochMismatch;
            }

            if (acknowledgement.SchemaId != _config.SchemaId)
            {
                return FixedInputAckApplyStatus.SchemaMismatch;
            }

            if (!FixedInputWireCodec.IsValidAcknowledgementSemantics(in acknowledgement))
            {
                return FixedInputAckApplyStatus.InvalidInput;
            }

            if (_hasAppliedAck)
            {
                if (acknowledgement.CommittedThroughTick < _appliedCommittedThrough)
                {
                    return FixedInputAckApplyStatus.RejectedRegression;
                }

                // LatestReceived must never regress within an epoch, even when CommittedThrough advances.
                if (acknowledgement.LatestReceivedTick < _appliedLatestReceived)
                {
                    return FixedInputAckApplyStatus.RejectedRegression;
                }
            }

            // Mark received-but-not-committed frames so they stop resending.
            if (acknowledgement.LatestReceivedTick != 0)
            {
                for (int slot = 0; slot < _capacity; slot++)
                {
                    if (!_occupied[slot] || !_needsSend[slot])
                    {
                        continue;
                    }

                    uint tick = _ticks[slot];
                    if (tick > acknowledgement.LatestReceivedTick)
                    {
                        continue;
                    }

                    uint delta = acknowledgement.LatestReceivedTick - tick;
                    if (delta >= 64)
                    {
                        continue;
                    }

                    if (((acknowledgement.ReceivedMask >> (int)delta) & 1UL) != 0UL)
                    {
                        _needsSend[slot] = false;
                    }
                }
            }

            // Remove committed frames.
            for (int slot = 0; slot < _capacity; slot++)
            {
                if (!_occupied[slot])
                {
                    continue;
                }

                if (_ticks[slot] <= acknowledgement.CommittedThroughTick)
                {
                    ClearSlot(slot);
                }
            }

            _appliedCommittedThrough = acknowledgement.CommittedThroughTick;
            _appliedLatestReceived = acknowledgement.LatestReceivedTick;
            _hasAppliedAck = true;
            _appliedAcknowledgementVersion = checked(_appliedAcknowledgementVersion + 1UL);
            return FixedInputAckApplyStatus.Applied;
        }

        /// <summary>
        /// Builds one redundant strictly ordered batch from frames that still need send.
        /// Returns <see cref="FixedInputBatchBuildStatus.NoData"/> when nothing needs sending;
        /// that is not a successful encode path.
        /// </summary>
        public FixedInputBatchBuildStatus TryBuildBatch(
            uint acknowledgedCommittedTick,
            Span<uint> targetTicks,
            Span<byte> payloads,
            out NetworkFixedInputBatchHeader header,
            out int frameCount)
        {
            header = default;
            frameCount = 0;
            if (!FixedInputWireCodec.IsValidSimulationTickField(acknowledgedCommittedTick))
            {
                return FixedInputBatchBuildStatus.InvalidInput;
            }

            int maxFrames = Math.Min(_config.MaxFramesPerBatch, targetTicks.Length);
            if (maxFrames <= 0)
            {
                return FixedInputBatchBuildStatus.InvalidInput;
            }

            long payloadCapacity = (long)maxFrames * _payloadBytes;
            if (payloads.Length < payloadCapacity)
            {
                return FixedInputBatchBuildStatus.CapacityExceeded;
            }

            // Collect needing-send slots sorted by tick without allocating.
            Span<int> orderedSlots = stackalloc int[maxFrames];
            int selected = 0;
            for (int slot = 0; slot < _capacity; slot++)
            {
                if (!_occupied[slot] || !_needsSend[slot])
                {
                    continue;
                }

                if (selected == maxFrames)
                {
                    // Keep the earliest ticks; replace if this tick is older than the current max.
                    int worst = 0;
                    for (int i = 1; i < selected; i++)
                    {
                        if (_ticks[orderedSlots[i]] > _ticks[orderedSlots[worst]])
                        {
                            worst = i;
                        }
                    }

                    if (_ticks[slot] >= _ticks[orderedSlots[worst]])
                    {
                        continue;
                    }

                    orderedSlots[worst] = slot;
                    continue;
                }

                orderedSlots[selected++] = slot;
            }

            if (selected == 0)
            {
                return FixedInputBatchBuildStatus.NoData;
            }

            // Insertion-sort selected slots by tick ascending.
            for (int i = 1; i < selected; i++)
            {
                int value = orderedSlots[i];
                int j = i - 1;
                while (j >= 0 && _ticks[orderedSlots[j]] > _ticks[value])
                {
                    orderedSlots[j + 1] = orderedSlots[j];
                    j--;
                }

                orderedSlots[j + 1] = value;
            }

            for (int i = 0; i < selected; i++)
            {
                int slot = orderedSlots[i];
                targetTicks[i] = _ticks[slot];
                _payloads.AsSpan(slot * _payloadBytes, _payloadBytes)
                    .CopyTo(payloads.Slice(i * _payloadBytes, _payloadBytes));
            }

            frameCount = selected;
            header = new NetworkFixedInputBatchHeader(
                _config.SessionEpoch,
                _config.SchemaId,
                (ushort)_payloadBytes,
                acknowledgedCommittedTick,
                (ushort)selected);
            return FixedInputBatchBuildStatus.Built;
        }

        private int FindFreeSlot()
        {
            for (int i = 0; i < _capacity; i++)
            {
                if (!_occupied[i])
                {
                    return i;
                }
            }

            throw new InvalidOperationException("Fixed-input outbox occupancy is inconsistent with its count.");
        }

        private void ClearSlot(int slot)
        {
            _occupied[slot] = false;
            _needsSend[slot] = false;
            _ticks[slot] = 0;
            _payloads.AsSpan(slot * _payloadBytes, _payloadBytes).Clear();
            _count--;
        }
    }
}
