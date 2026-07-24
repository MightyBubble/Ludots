using System;
using System.Collections.Generic;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Knowledge;
using Ludots.Core.Networking.Protocol;

namespace Ludots.Core.Networking.Configuration
{
    public sealed class NetworkRuntimeConfig
    {
        public string ProfileId { get; set; } = string.Empty;
        public string ReferenceTransport { get; set; } = string.Empty;
        public ushort ProtocolMajor { get; set; }
        public ushort ProtocolMinor { get; set; }
        public int PlayerCapacity { get; set; }
        public int SimulationTickRateHz { get; set; }
        public int StatePublishRateHz { get; set; }
        public int NetworkEntityCapacity { get; set; }
        public int OrderQueueCapacity { get; set; }
        public int MaxCommandBatchesPerSecondPerPlayer { get; set; }
        public int CommandBurstBatchCapacity { get; set; }
        public int MaxActorsPerCommandBatch { get; set; }
        public int CommandSequenceHistoryCapacity { get; set; }
        public int MaxPastTargetTicks { get; set; }
        public int MaxFutureTargetTicks { get; set; }
        public int NetworkAdmissionResultCapacity { get; set; }
        public int EntityAdmissionResultCapacity { get; set; }
        public int ReconnectWindowSeconds { get; set; }
        public int BaselineCapacity { get; set; }
        public int ReplicationPacketEntityCapacity { get; set; }
        public int DisclosureChangeLogCapacity { get; set; }
        public int DatagramQueueCapacity { get; set; }
        public int ConnectionEventCapacity { get; set; }
        public int MaxDatagramPayloadBytes { get; set; }
        public int TransportChannelCount { get; set; }
        public int ControlChannelId { get; set; }
        public int CommandChannelId { get; set; }
        public int StateChannelId { get; set; }
        public int SnapshotChunkCapacity { get; set; }
        public int MaxServerOutboundBytesPerSecondPerClient { get; set; }
        public int TickP95BudgetMicroseconds { get; set; }
        public int TickP99BudgetMicroseconds { get; set; }
        public List<NetworkCommandSchemaConfig> CommandSchemas { get; set; } = new();
        public NetworkFaultProfileConfig NormalConnection { get; set; } = new();
        public NetworkFaultProfileConfig UnstableConnection { get; set; } = new();

        public void Validate()
        {
            RequireText(ProfileId, nameof(ProfileId));
            RequireText(ReferenceTransport, nameof(ReferenceTransport));
            RequirePositive(ProtocolMajor, nameof(ProtocolMajor));
            RequirePositive(PlayerCapacity, nameof(PlayerCapacity));
            RequirePositive(SimulationTickRateHz, nameof(SimulationTickRateHz));
            RequirePositive(StatePublishRateHz, nameof(StatePublishRateHz));
            if (SimulationTickRateHz % StatePublishRateHz != 0)
            {
                throw new InvalidOperationException(
                    $"Networking state publish rate {StatePublishRateHz} must divide simulation rate {SimulationTickRateHz} exactly.");
            }

            RequirePositive(NetworkEntityCapacity, nameof(NetworkEntityCapacity));
            RequirePositive(OrderQueueCapacity, nameof(OrderQueueCapacity));
            RequirePositive(MaxCommandBatchesPerSecondPerPlayer, nameof(MaxCommandBatchesPerSecondPerPlayer));
            RequirePositive(CommandBurstBatchCapacity, nameof(CommandBurstBatchCapacity));
            RequirePositive(MaxActorsPerCommandBatch, nameof(MaxActorsPerCommandBatch));
            if (OrderQueueCapacity < MaxActorsPerCommandBatch)
            {
                throw new InvalidOperationException(
                    $"Networking order queue capacity {OrderQueueCapacity} is below maximum command actor count {MaxActorsPerCommandBatch}.");
            }

            RequirePositive(CommandSequenceHistoryCapacity, nameof(CommandSequenceHistoryCapacity));
            RequireNonNegative(MaxPastTargetTicks, nameof(MaxPastTargetTicks));
            RequireNonNegative(MaxFutureTargetTicks, nameof(MaxFutureTargetTicks));
            int scheduledCommandBatchCapacity = checked(PlayerCapacity * CommandBurstBatchCapacity);
            if (CommandSequenceHistoryCapacity < scheduledCommandBatchCapacity)
            {
                throw new InvalidOperationException(
                    $"Networking command sequence history capacity {CommandSequenceHistoryCapacity} is below scheduled batch capacity {scheduledCommandBatchCapacity}.");
            }

            RequirePositive(NetworkAdmissionResultCapacity, nameof(NetworkAdmissionResultCapacity));
            if (NetworkAdmissionResultCapacity < scheduledCommandBatchCapacity)
            {
                throw new InvalidOperationException(
                    $"Networking admission result capacity {NetworkAdmissionResultCapacity} is below scheduled batch capacity {scheduledCommandBatchCapacity}.");
            }

            RequirePositive(EntityAdmissionResultCapacity, nameof(EntityAdmissionResultCapacity));
            RequirePositive(ReconnectWindowSeconds, nameof(ReconnectWindowSeconds));
            RequirePositive(BaselineCapacity, nameof(BaselineCapacity));
            RequirePositive(ReplicationPacketEntityCapacity, nameof(ReplicationPacketEntityCapacity));
            if (ReplicationPacketEntityCapacity < NetworkEntityCapacity)
            {
                throw new InvalidOperationException(
                    $"Replication packet entity capacity {ReplicationPacketEntityCapacity} is below network entity capacity {NetworkEntityCapacity}.");
            }

            RequirePositive(DisclosureChangeLogCapacity, nameof(DisclosureChangeLogCapacity));
            RequirePositive(DatagramQueueCapacity, nameof(DatagramQueueCapacity));
            RequirePositive(ConnectionEventCapacity, nameof(ConnectionEventCapacity));
            RequirePositive(MaxDatagramPayloadBytes, nameof(MaxDatagramPayloadBytes));
            if (MaxDatagramPayloadBytes > 1200)
            {
                throw new InvalidOperationException(
                    $"Configured datagram payload {MaxDatagramPayloadBytes} exceeds the IPv6-safe 1200 byte contract.");
            }

            if ((uint)(TransportChannelCount - 1) >= 64u)
            {
                throw new InvalidOperationException("Transport channel count must be between 1 and 64.");
            }

            ValidateChannel(ControlChannelId, nameof(ControlChannelId));
            ValidateChannel(CommandChannelId, nameof(CommandChannelId));
            ValidateChannel(StateChannelId, nameof(StateChannelId));
            if (ControlChannelId == CommandChannelId ||
                ControlChannelId == StateChannelId ||
                CommandChannelId == StateChannelId)
            {
                throw new InvalidOperationException(
                    "Networking control, command, and state channels must be distinct.");
            }

            RequirePositive(SnapshotChunkCapacity, nameof(SnapshotChunkCapacity));

            RequirePositive(MaxServerOutboundBytesPerSecondPerClient, nameof(MaxServerOutboundBytesPerSecondPerClient));
            RequirePositive(TickP95BudgetMicroseconds, nameof(TickP95BudgetMicroseconds));
            RequirePositive(TickP99BudgetMicroseconds, nameof(TickP99BudgetMicroseconds));
            if (TickP99BudgetMicroseconds < TickP95BudgetMicroseconds)
            {
                throw new InvalidOperationException("Tick P99 budget must be greater than or equal to P95 budget.");
            }

            ValidateCommandSchemas();

            NormalConnection.Validate(nameof(NormalConnection));
            UnstableConnection.Validate(nameof(UnstableConnection));
        }

        private void ValidateCommandSchemas()
        {
            if (CommandSchemas == null || CommandSchemas.Count == 0)
            {
                throw new InvalidOperationException("Networking CommandSchemas must explicitly expose at least one order type.");
            }

            var keys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < CommandSchemas.Count; i++)
            {
                NetworkCommandSchemaConfig schema = CommandSchemas[i]
                    ?? throw new InvalidOperationException($"Networking CommandSchemas[{i}] must not be null.");
                RequireText(schema.OrderTypeKey, $"CommandSchemas[{i}].OrderTypeKey");
                if (!keys.Add(schema.OrderTypeKey))
                {
                    throw new InvalidOperationException(
                        $"Networking command schema order type key '{schema.OrderTypeKey}' is duplicated.");
                }

                if ((uint)schema.TargetKind > (uint)NetworkCommandTargetKind.WorldPositionAndEntity)
                {
                    throw new InvalidOperationException(
                        $"Networking CommandSchemas[{i}].TargetKind is invalid: {schema.TargetKind}.");
                }

                if ((uint)schema.SubmitMode > (uint)OrderSubmitMode.Queued)
                {
                    throw new InvalidOperationException(
                        $"Networking CommandSchemas[{i}].SubmitMode is invalid: {schema.SubmitMode}.");
                }

                if ((uint)schema.RequiredTargetPositionAccess > (uint)KnowledgePositionAccess.Live)
                {
                    throw new InvalidOperationException(
                        $"Networking CommandSchemas[{i}].RequiredTargetPositionAccess is invalid: {schema.RequiredTargetPositionAccess}.");
                }

                bool hasEntityTarget = schema.TargetKind is NetworkCommandTargetKind.NetworkEntity or
                    NetworkCommandTargetKind.WorldPositionAndEntity;
                if (!hasEntityTarget && schema.RequiredTargetPositionAccess != KnowledgePositionAccess.None)
                {
                    throw new InvalidOperationException(
                        $"Networking command schema '{schema.OrderTypeKey}' requires entity position knowledge without an entity target.");
                }
            }
        }

        private void ValidateChannel(int channelId, string name)
        {
            if ((uint)channelId >= (uint)TransportChannelCount)
            {
                throw new InvalidOperationException(
                    $"Networking {name} {channelId} must be within transport channel count {TransportChannelCount}.");
            }
        }

        private static void RequireText(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"Networking {name} is required.");
        }

        private static void RequirePositive(int value, string name)
        {
            if (value <= 0) throw new InvalidOperationException($"Networking {name} must be positive; got {value}.");
        }

        private static void RequireNonNegative(int value, string name)
        {
            if (value < 0) throw new InvalidOperationException($"Networking {name} must not be negative; got {value}.");
        }
    }

    public sealed class NetworkCommandSchemaConfig
    {
        public string OrderTypeKey { get; set; } = string.Empty;
        public NetworkCommandTargetKind TargetKind { get; set; }
        public bool AllowArg0 { get; set; }
        public bool AllowArg1 { get; set; }
        public OrderSubmitMode SubmitMode { get; set; }
        public KnowledgePositionAccess RequiredTargetPositionAccess { get; set; }
    }

    public sealed class NetworkFaultProfileConfig
    {
        public int RoundTripLatencyMs { get; set; }
        public int JitterMs { get; set; }
        public int PacketLossPermille { get; set; }
        public int ReorderPermille { get; set; }

        public void Validate(string owner)
        {
            if (RoundTripLatencyMs < 0) throw new InvalidOperationException($"Networking {owner}.RoundTripLatencyMs must not be negative.");
            if (JitterMs < 0) throw new InvalidOperationException($"Networking {owner}.JitterMs must not be negative.");
            if ((uint)PacketLossPermille > 1000u) throw new InvalidOperationException($"Networking {owner}.PacketLossPermille must be between 0 and 1000.");
            if ((uint)ReorderPermille > 1000u) throw new InvalidOperationException($"Networking {owner}.ReorderPermille must be between 0 and 1000.");
        }
    }
}
