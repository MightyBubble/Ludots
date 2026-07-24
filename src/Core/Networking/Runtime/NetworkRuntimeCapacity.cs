using System;
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
            int entityCapacity,
            int maxCommandEntries,
            int maxCommandPayloadBytes,
            int maxCommandFragments,
            int maxSnapshotBytes,
            int maxSnapshotFragments,
            int outboundQueueCapacity,
            int acknowledgementHistoryCapacity,
            ChannelId controlChannel,
            ChannelId commandChannel,
            ChannelId stateChannel)
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

            if (connectionCapacity <= 0 || entityCapacity <= 0 || maxCommandEntries <= 0 ||
                maxCommandPayloadBytes < CommandBatchWireCodec.GetPayloadSize(maxCommandEntries) ||
                maxCommandFragments <= 0 || maxSnapshotBytes <= 0 || maxSnapshotFragments <= 0 ||
                outboundQueueCapacity <= 0 || acknowledgementHistoryCapacity <= 0)
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

            SimulationTickRateHz = simulationTickRateHz;
            StatePublishRateHz = statePublishRateHz;
            StatePublishIntervalTicks = simulationTickRateHz / statePublishRateHz;
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
            ControlChannel = controlChannel;
            CommandChannel = commandChannel;
            StateChannel = stateChannel;
        }

        public int SimulationTickRateHz { get; }
        public int StatePublishRateHz { get; }
        public int StatePublishIntervalTicks { get; }
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
        public ChannelId ControlChannel { get; }
        public ChannelId CommandChannel { get; }
        public ChannelId StateChannel { get; }
    }
}
