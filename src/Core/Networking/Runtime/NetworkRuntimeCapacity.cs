using System;
using Ludots.Core.Networking.Configuration;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Transport;

namespace Ludots.Core.Networking.Runtime
{
    public readonly struct NetworkRuntimeCapacity
    {
        public NetworkRuntimeCapacity(
            int maxDatagramPayloadBytes,
            int connectionCapacity,
            int entityCapacity,
            int maxCommandEntries,
            int maxCommandPayloadBytes,
            int maxCommandFragments,
            int maxSnapshotBytes,
            int maxSnapshotFragments,
            int outboundQueueCapacity,
            int acknowledgementHistoryCapacity,
            int snapshotAcknowledgementTimeoutTicks,
            int commandCorrelationCapacity,
            ChannelId controlChannel,
            ChannelId commandChannel,
            ChannelId stateChannel,
            int statePublishIntervalTicks = 1,
            int simulationTickRateHz = 30,
            int maxFutureTargetTicks = 0)
        {
            if (maxDatagramPayloadBytes <= NetworkWireEnvelope.SizeInBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(maxDatagramPayloadBytes));
            }

            if (connectionCapacity <= 0 || entityCapacity <= 0 || maxCommandEntries <= 0 ||
                maxCommandPayloadBytes < CommandBatchWireCodec.GetPayloadSize(maxCommandEntries) ||
                maxCommandFragments <= 0 || maxSnapshotBytes <= 0 || maxSnapshotFragments <= 0 ||
                outboundQueueCapacity <= 0 || acknowledgementHistoryCapacity <= 0 ||
                snapshotAcknowledgementTimeoutTicks <= 0 ||
                commandCorrelationCapacity <= 0 ||
                statePublishIntervalTicks <= 0 ||
                simulationTickRateHz <= 0 ||
                maxFutureTargetTicks < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(connectionCapacity), "All runtime capacities must cover their declared payloads.");
            }

            if (ReplicationPacketWireCodec.GetPayloadSize(entityCapacity, entityCapacity, entityCapacity) > maxSnapshotBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(maxSnapshotBytes), "Snapshot capacity cannot hold the largest replication packet.");
            }

            if (controlChannel == commandChannel || controlChannel == stateChannel || commandChannel == stateChannel)
            {
                throw new ArgumentException("Control, command, and state channels must be distinct.");
            }

            _ = CommandFragmentWireCodec.GetMaxFragmentDataBytes(maxDatagramPayloadBytes);
            _ = SnapshotFragmentWireCodec.GetMaxFragmentDataBytes(maxDatagramPayloadBytes);

            MaxDatagramPayloadBytes = maxDatagramPayloadBytes;
            ConnectionCapacity = connectionCapacity;
            EntityCapacity = entityCapacity;
            MaxCommandEntries = maxCommandEntries;
            MaxCommandPayloadBytes = maxCommandPayloadBytes;
            MaxCommandFragments = maxCommandFragments;
            MaxSnapshotBytes = maxSnapshotBytes;
            MaxSnapshotFragments = maxSnapshotFragments;
            OutboundQueueCapacity = outboundQueueCapacity;
            AcknowledgementHistoryCapacity = acknowledgementHistoryCapacity;
            SnapshotAcknowledgementTimeoutTicks = snapshotAcknowledgementTimeoutTicks;
            CommandCorrelationCapacity = commandCorrelationCapacity;
            ControlChannel = controlChannel;
            CommandChannel = commandChannel;
            StateChannel = stateChannel;
            StatePublishIntervalTicks = statePublishIntervalTicks;
            SimulationTickRateHz = simulationTickRateHz;
            MaxFutureTargetTicks = maxFutureTargetTicks;
        }

        public int MaxDatagramPayloadBytes { get; }
        public int ConnectionCapacity { get; }
        public int EntityCapacity { get; }
        public int MaxCommandEntries { get; }
        public int MaxCommandPayloadBytes { get; }
        public int MaxCommandFragments { get; }
        public int MaxSnapshotBytes { get; }
        public int MaxSnapshotFragments { get; }
        public int OutboundQueueCapacity { get; }
        public int AcknowledgementHistoryCapacity { get; }
        public int SnapshotAcknowledgementTimeoutTicks { get; }
        public int CommandCorrelationCapacity { get; }
        public ChannelId ControlChannel { get; }
        public ChannelId CommandChannel { get; }
        public ChannelId StateChannel { get; }
        public int StatePublishIntervalTicks { get; }
        public int SimulationTickRateHz { get; }
        public int MaxFutureTargetTicks { get; }

        public static NetworkRuntimeCapacity FromConfig(NetworkRuntimeConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);
            config.Validate();

            int maxCommandPayloadBytes = CommandBatchWireCodec.GetPayloadSize(config.MaxActorsPerCommandBatch);
            int commandFragmentBytes = CommandFragmentWireCodec.GetMaxFragmentDataBytes(config.MaxDatagramPayloadBytes);
            int maxCommandFragments = DivideRoundUp(maxCommandPayloadBytes, commandFragmentBytes);

            int maxSnapshotBytes = ReplicationPacketWireCodec.GetPayloadSize(
                config.ReplicationPacketEntityCapacity,
                config.ReplicationPacketEntityCapacity,
                config.ReplicationPacketEntityCapacity);
            int snapshotFragmentBytes = SnapshotFragmentWireCodec.GetMaxFragmentDataBytes(config.MaxDatagramPayloadBytes);
            int maxSnapshotFragments = DivideRoundUp(maxSnapshotBytes, snapshotFragmentBytes);

            return new NetworkRuntimeCapacity(
                config.MaxDatagramPayloadBytes,
                config.PlayerCapacity,
                config.NetworkEntityCapacity,
                config.MaxActorsPerCommandBatch,
                maxCommandPayloadBytes,
                maxCommandFragments,
                maxSnapshotBytes,
                maxSnapshotFragments,
                config.DatagramQueueCapacity,
                config.BaselineCapacity,
                config.SnapshotAcknowledgementTimeoutTicks,
                config.CommandCorrelationCapacity,
                new ChannelId(checked((byte)config.ControlChannelId)),
                new ChannelId(checked((byte)config.CommandChannelId)),
                new ChannelId(checked((byte)config.StateChannelId)),
                config.SimulationTickRateHz / config.StatePublishRateHz,
                config.SimulationTickRateHz,
                config.MaxFutureTargetTicks);
        }

        private static int DivideRoundUp(int value, int divisor) =>
            checked((value + divisor - 1) / divisor);
    }
}
