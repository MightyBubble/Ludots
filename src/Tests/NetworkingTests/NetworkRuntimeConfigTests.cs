using Ludots.Core.Networking.Configuration;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Knowledge;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.Networking;

[TestFixture]
public sealed class NetworkRuntimeConfigTests
{
    [Test]
    public void RtsDuelAcceptanceProfile_IsExplicitAndValid()
    {
        NetworkRuntimeConfig config = CreateRtsDuelConfig();
        NetworkRuntimeCapacity capacity = NetworkRuntimeCapacity.FromConfig(config);

        Assert.DoesNotThrow(config.Validate);
        Assert.Multiple(() =>
        {
            Assert.That(config.ProfileId, Is.EqualTo("rts_duel_v1"));
            Assert.That(config.PlayerCapacity, Is.EqualTo(2));
            Assert.That(config.SimulationTickRateHz, Is.EqualTo(30));
            Assert.That(config.StatePublishRateHz, Is.EqualTo(10));
            Assert.That(config.NetworkEntityCapacity, Is.EqualTo(256));
            Assert.That(config.ReplicationSchemaCapacity, Is.EqualTo(8));
            Assert.That(config.MaxCommandBatchesPerSecondPerPlayer, Is.EqualTo(32));
            Assert.That(config.MaxActorsPerCommandBatch, Is.EqualTo(128));
            Assert.That(config.CommandSchemas, Has.Count.EqualTo(3));
            Assert.That(config.ReconnectWindowSeconds, Is.EqualTo(30));
            Assert.That(config.ReadyCountdownTicks, Is.EqualTo(90));
            Assert.That(config.ClientReconnectRetryMilliseconds, Is.EqualTo(500));
            Assert.That(config.SnapshotAcknowledgementTimeoutTicks, Is.EqualTo(15));
            Assert.That(config.ControlChannelId, Is.EqualTo(0));
            Assert.That(config.CommandChannelId, Is.EqualTo(1));
            Assert.That(config.StateChannelId, Is.EqualTo(2));
            Assert.That(config.MaxServerOutboundBytesPerSecondPerClient, Is.EqualTo(256 * 1024));
            Assert.That(config.TickP95BudgetMicroseconds, Is.EqualTo(26_700));
            Assert.That(config.TickP99BudgetMicroseconds, Is.EqualTo(31_000));
            Assert.That(capacity.SimulationTickRateHz, Is.EqualTo(config.SimulationTickRateHz));
            Assert.That(capacity.MaxFutureTargetTicks, Is.EqualTo(config.MaxFutureTargetTicks));
        });
    }

    [Test]
    public void CapacityMismatch_IsRejectedAtStartup()
    {
        NetworkRuntimeConfig config = CreateRtsDuelConfig();
        config.ReplicationPacketEntityCapacity = 128;

        Assert.That(config.Validate, Throws.InvalidOperationException.With.Message.Contains("below network entity capacity"));
    }

    [Test]
    public void MissingOrDuplicateCommandSchemas_AreRejectedAtStartup()
    {
        NetworkRuntimeConfig missing = CreateRtsDuelConfig();
        missing.CommandSchemas.Clear();
        Assert.That(
            missing.Validate,
            Throws.InvalidOperationException.With.Message.Contains("must explicitly expose"));

        NetworkRuntimeConfig duplicate = CreateRtsDuelConfig();
        duplicate.CommandSchemas.Add(new NetworkCommandSchemaConfig
        {
            OrderTypeKey = "moveTo",
            TargetKind = NetworkCommandTargetKind.WorldPositionCm,
            AllowedSubmitModes = { OrderSubmitMode.Queued },
        });
        Assert.That(
            duplicate.Validate,
            Throws.InvalidOperationException.With.Message.Contains("duplicated"));
    }

    [Test]
    public void DatagramCapacityBelowRoomSnapshot_IsRejectedAtStartup()
    {
        NetworkRuntimeConfig config = CreateRtsDuelConfig();
        config.MaxDatagramPayloadBytes = 63;

        Assert.That(
            config.Validate,
            Throws.InvalidOperationException.With.Message.Contains("cannot carry the 64-byte room snapshot"));
    }

    [Test]
    public void MissingSnapshotAcknowledgementTimeout_IsRejectedAtStartup()
    {
        NetworkRuntimeConfig config = CreateRtsDuelConfig();
        config.SnapshotAcknowledgementTimeoutTicks = 0;

        Assert.That(
            config.Validate,
            Throws.InvalidOperationException.With.Message.Contains(nameof(NetworkRuntimeConfig.SnapshotAcknowledgementTimeoutTicks)));
    }

    private static NetworkRuntimeConfig CreateRtsDuelConfig() => new()
    {
        ProfileId = "rts_duel_v1",
        ReferenceTransport = "LiteNetLib/2.1.4",
        ProtocolMajor = 1,
        ProtocolMinor = 0,
        PlayerCapacity = 2,
        SimulationTickRateHz = 30,
        StatePublishRateHz = 10,
        NetworkEntityCapacity = 256,
        MaxCommandBatchesPerSecondPerPlayer = 32,
        CommandBurstBatchCapacity = 32,
        MaxActorsPerCommandBatch = 128,
        CommandSequenceHistoryCapacity = 128,
        MaxPastTargetTicks = 3,
        MaxFutureTargetTicks = 6,
        NetworkAdmissionResultCapacity = 512,
        CommandCorrelationCapacity = 4096,
        ReconnectWindowSeconds = 30,
        ReadyCountdownTicks = 90,
        ClientReconnectRetryMilliseconds = 500,
        BaselineCapacity = 32,
        SnapshotAcknowledgementTimeoutTicks = 15,
        ReplicationPacketEntityCapacity = 256,
        ReplicationSchemaCapacity = 8,
        DisclosureChangeLogCapacity = 4096,
        DatagramQueueCapacity = 1024,
        ConnectionEventCapacity = 64,
        MaxDatagramPayloadBytes = 1200,
        TransportChannelCount = 8,
        ControlChannelId = 0,
        CommandChannelId = 1,
        StateChannelId = 2,
        SnapshotChunkCapacity = 256,
        MaxServerOutboundBytesPerSecondPerClient = 256 * 1024,
        TickP95BudgetMicroseconds = 26_700,
        TickP99BudgetMicroseconds = 31_000,
        CommandSchemas =
        {
            new NetworkCommandSchemaConfig
            {
                OrderTypeKey = "moveTo",
                TargetKind = NetworkCommandTargetKind.WorldPositionCm,
                AllowedSubmitModes = { OrderSubmitMode.Immediate, OrderSubmitMode.Queued },
            },
            new NetworkCommandSchemaConfig
            {
                OrderTypeKey = "attackTarget",
                TargetKind = NetworkCommandTargetKind.NetworkEntity,
                AllowedSubmitModes = { OrderSubmitMode.Immediate, OrderSubmitMode.Queued },
                RequiredTargetPositionAccess = KnowledgePositionAccess.LastKnown,
            },
            new NetworkCommandSchemaConfig
            {
                OrderTypeKey = "stop",
                TargetKind = NetworkCommandTargetKind.None,
                AllowedSubmitModes = { OrderSubmitMode.Immediate },
            },
        },
        NormalConnection = new NetworkFaultProfileConfig(),
        UnstableConnection = new NetworkFaultProfileConfig
        {
            RoundTripLatencyMs = 180,
            JitterMs = 30,
            PacketLossPermille = 50,
            ReorderPermille = 20,
        },
    };
}
