using System;
using Ludots.Core.Networking.Configuration;
using Ludots.Core.Networking.FixedInput;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Transport;

namespace Ludots.Core.Networking.Runtime
{
    public readonly struct NetworkRuntimeCapacity
    {
        public NetworkRuntimeCapacity(
            int simulationTickRateHz,
            int statePublishRateHz,
            int maxDatagramPayloadBytes,
            int connectionCapacity,
            int globalEntityCapacity,
            int replicationEntityCapacityPerSeat,
            int maxCommandEntries,
            int maxCommandPayloadBytes,
            int maxCommandFragments,
            int maxSnapshotBytes,
            int maxSnapshotFragments,
            int outboundQueueCapacity,
            int acknowledgementHistoryCapacity,
            ChannelId controlChannel,
            ChannelId commandChannel,
            ChannelId stateChannel,
            ChannelId inputChannel,
            int fixedInputHistoryTicksPerSeat,
            ushort fixedInputSchemaId,
            ushort fixedInputFramePayloadBytes,
            int fixedInputMaxFutureTicks,
            int fixedInputMaxFramesPerBatch,
            int fixedInputPendingFrameCapacity)
        {
            if (simulationTickRateHz <= 0) throw new ArgumentOutOfRangeException(nameof(simulationTickRateHz));
            if (statePublishRateHz <= 0) throw new ArgumentOutOfRangeException(nameof(statePublishRateHz));
            if (simulationTickRateHz % statePublishRateHz != 0)
            {
                throw new ArgumentException(
                    $"State publish rate {statePublishRateHz} must divide simulation rate {simulationTickRateHz} exactly.",
                    nameof(statePublishRateHz));
            }

            if (maxDatagramPayloadBytes <= NetworkWireEnvelope.SizeInBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(maxDatagramPayloadBytes));
            }

            if (connectionCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(connectionCapacity));
            }

            if (globalEntityCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(globalEntityCapacity));
            }

            if (replicationEntityCapacityPerSeat <= 0 ||
                replicationEntityCapacityPerSeat > globalEntityCapacity ||
                replicationEntityCapacityPerSeat > ushort.MaxValue / 2)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(replicationEntityCapacityPerSeat),
                    "Per-seat replication capacity must fit the global table and leave wire capacity for conceal plus reveal changes.");
            }

            if (maxCommandEntries <= 0 ||
                maxCommandPayloadBytes < CommandBatchWireCodec.GetPayloadSize(maxCommandEntries) ||
                maxCommandFragments <= 0 || maxSnapshotBytes <= 0 || maxSnapshotFragments <= 0 ||
                outboundQueueCapacity <= 0 || acknowledgementHistoryCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxCommandEntries), "All runtime capacities must cover their declared payloads.");
            }

            if (ReplicationPacketWireCodec.GetPayloadSize(
                    replicationEntityCapacityPerSeat,
                    replicationEntityCapacityPerSeat,
                    checked(replicationEntityCapacityPerSeat * 2)) > maxSnapshotBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(maxSnapshotBytes), "Snapshot capacity cannot hold the largest replication packet.");
            }

            if (controlChannel == commandChannel ||
                controlChannel == stateChannel ||
                controlChannel == inputChannel ||
                commandChannel == stateChannel ||
                commandChannel == inputChannel ||
                stateChannel == inputChannel)
            {
                throw new ArgumentException("Control, command, state, and input channels must be distinct.");
            }

            if (fixedInputPendingFrameCapacity < fixedInputMaxFutureTicks)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(fixedInputPendingFrameCapacity),
                    fixedInputPendingFrameCapacity,
                    $"Fixed-input pending frame capacity must be >= max future ticks ({fixedInputMaxFutureTicks}).");
            }

            // Fail-fast SSOT: fixed-input datagram and ring contracts must hold before runtime construction.
            _ = new FixedInputProtocolConfig(
                connectionCapacity,
                fixedInputHistoryTicksPerSeat,
                fixedInputSchemaId,
                fixedInputFramePayloadBytes,
                fixedInputMaxFutureTicks,
                fixedInputMaxFramesPerBatch,
                maxDatagramPayloadBytes,
                sessionEpoch: 1);

            _ = CommandFragmentWireCodec.GetMaxFragmentDataBytes(maxDatagramPayloadBytes);
            _ = SnapshotFragmentWireCodec.GetMaxFragmentDataBytes(maxDatagramPayloadBytes);

            SimulationTickRateHz = simulationTickRateHz;
            StatePublishRateHz = statePublishRateHz;
            StatePublishIntervalTicks = simulationTickRateHz / statePublishRateHz;
            MaxDatagramPayloadBytes = maxDatagramPayloadBytes;
            ConnectionCapacity = connectionCapacity;
            GlobalEntityCapacity = globalEntityCapacity;
            ReplicationEntityCapacityPerSeat = replicationEntityCapacityPerSeat;
            MaxCommandEntries = maxCommandEntries;
            MaxCommandPayloadBytes = maxCommandPayloadBytes;
            MaxCommandFragments = maxCommandFragments;
            MaxSnapshotBytes = maxSnapshotBytes;
            MaxSnapshotFragments = maxSnapshotFragments;
            OutboundQueueCapacity = outboundQueueCapacity;
            AcknowledgementHistoryCapacity = acknowledgementHistoryCapacity;
            ControlChannel = controlChannel;
            CommandChannel = commandChannel;
            StateChannel = stateChannel;
            InputChannel = inputChannel;
            FixedInputHistoryTicksPerSeat = fixedInputHistoryTicksPerSeat;
            FixedInputSchemaId = fixedInputSchemaId;
            FixedInputFramePayloadBytes = fixedInputFramePayloadBytes;
            FixedInputMaxFutureTicks = fixedInputMaxFutureTicks;
            FixedInputMaxFramesPerBatch = fixedInputMaxFramesPerBatch;
            FixedInputPendingFrameCapacity = fixedInputPendingFrameCapacity;
        }

        public int SimulationTickRateHz { get; }
        public int StatePublishRateHz { get; }
        public int StatePublishIntervalTicks { get; }
        public int MaxDatagramPayloadBytes { get; }
        public int ConnectionCapacity { get; }
        public int GlobalEntityCapacity { get; }
        public int ReplicationEntityCapacityPerSeat { get; }
        public int MaxCommandEntries { get; }
        public int MaxCommandPayloadBytes { get; }
        public int MaxCommandFragments { get; }
        public int MaxSnapshotBytes { get; }
        public int MaxSnapshotFragments { get; }
        public int OutboundQueueCapacity { get; }
        public int AcknowledgementHistoryCapacity { get; }
        public ChannelId ControlChannel { get; }
        public ChannelId CommandChannel { get; }
        public ChannelId StateChannel { get; }
        public ChannelId InputChannel { get; }
        public int FixedInputHistoryTicksPerSeat { get; }
        public ushort FixedInputSchemaId { get; }
        public ushort FixedInputFramePayloadBytes { get; }
        public int FixedInputMaxFutureTicks { get; }
        public int FixedInputMaxFramesPerBatch { get; }
        public int FixedInputPendingFrameCapacity { get; }

        /// <summary>
        /// Single validated mapping from declarative networking config to runtime capacity.
        /// Fragment and snapshot ceilings are derived from codecs; callers must not hand-duplicate them.
        /// </summary>
        public static NetworkRuntimeCapacity FromConfig(NetworkRuntimeConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            config.Validate();

            int maxCommandEntries = config.MaxActorsPerCommandBatch;
            int maxCommandPayloadBytes = CommandBatchWireCodec.GetPayloadSize(maxCommandEntries);
            int commandFragmentDataBytes = CommandFragmentWireCodec.GetMaxFragmentDataBytes(config.MaxDatagramPayloadBytes);
            int maxCommandFragments = checked(
                (maxCommandPayloadBytes + commandFragmentDataBytes - 1) / commandFragmentDataBytes);
            if (maxCommandFragments <= 0)
            {
                throw new InvalidOperationException("Derived command fragment capacity must be positive.");
            }

            int maxSnapshotBytes = ReplicationPacketWireCodec.GetPayloadSize(
                config.ReplicationEntityCapacityPerSeat,
                config.ReplicationEntityCapacityPerSeat,
                checked(config.ReplicationEntityCapacityPerSeat * 2));
            int snapshotFragmentDataBytes = SnapshotFragmentWireCodec.GetMaxFragmentDataBytes(config.MaxDatagramPayloadBytes);
            int requiredSnapshotFragments = checked(
                (maxSnapshotBytes + snapshotFragmentDataBytes - 1) / snapshotFragmentDataBytes);
            if (config.SnapshotChunkCapacity < requiredSnapshotFragments)
            {
                throw new InvalidOperationException(
                    $"Networking SnapshotChunkCapacity {config.SnapshotChunkCapacity} is below required snapshot fragments {requiredSnapshotFragments}.");
            }

            return new NetworkRuntimeCapacity(
                config.SimulationTickRateHz,
                config.StatePublishRateHz,
                config.MaxDatagramPayloadBytes,
                config.PlayerCapacity,
                config.GlobalNetworkEntityCapacity,
                config.ReplicationEntityCapacityPerSeat,
                maxCommandEntries,
                maxCommandPayloadBytes,
                maxCommandFragments,
                maxSnapshotBytes,
                config.SnapshotChunkCapacity,
                config.DatagramQueueCapacity,
                config.BaselineCapacity,
                new ChannelId(checked((byte)config.ControlChannelId)),
                new ChannelId(checked((byte)config.CommandChannelId)),
                new ChannelId(checked((byte)config.StateChannelId)),
                new ChannelId(checked((byte)config.InputChannelId)),
                config.FixedInputHistoryTicksPerSeat,
                checked((ushort)config.FixedInputSchemaId),
                checked((ushort)config.FixedInputFramePayloadBytes),
                config.FixedInputMaxFutureTicks,
                config.FixedInputMaxFramesPerBatch,
                config.FixedInputPendingFrameCapacity);
        }

        /// <summary>
        /// Builds the fixed-input protocol config from this capacity, the accepted session epoch,
        /// and the authoritative seat table size.
        /// </summary>
        public FixedInputProtocolConfig CreateFixedInputProtocolConfig(ulong sessionEpoch, int seatCapacity)
        {
            if (seatCapacity <= 0 || seatCapacity > ConnectionCapacity)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(seatCapacity),
                    seatCapacity,
                    $"Fixed-input seat capacity must be in 1..{ConnectionCapacity}.");
            }

            return new FixedInputProtocolConfig(
                seatCapacity,
                FixedInputHistoryTicksPerSeat,
                FixedInputSchemaId,
                FixedInputFramePayloadBytes,
                FixedInputMaxFutureTicks,
                FixedInputMaxFramesPerBatch,
                MaxDatagramPayloadBytes,
                sessionEpoch);
        }
    }
}
