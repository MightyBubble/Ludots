using Ludots.Core.Networking.Configuration;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Knowledge;
using Ludots.Core.Networking.Protocol;
using NUnit.Framework;

namespace Ludots.Tests.Networking;

[TestFixture]
public sealed class NetworkRuntimeConfigTests
{
    [Test]
    public void RtsDuelAcceptanceProfile_IsExplicitAndValid()
    {
        NetworkRuntimeConfig config = CreateRtsDuelConfig();

        Assert.DoesNotThrow(config.Validate);
        Assert.Multiple(() =>
        {
            Assert.That(config.ProfileId, Is.EqualTo("rts_duel_v1"));
            Assert.That(config.PlayerCapacity, Is.EqualTo(2));
            Assert.That(config.SimulationTickRateHz, Is.EqualTo(30));
            Assert.That(config.StatePublishRateHz, Is.EqualTo(10));
            Assert.That(config.GlobalNetworkEntityCapacity, Is.EqualTo(100_000));
            Assert.That(config.ReplicationEntityCapacityPerSeat, Is.EqualTo(512));
            Assert.That(config.MaxCommandBatchesPerSecondPerPlayer, Is.EqualTo(32));
            Assert.That(config.MaxActorsPerCommandBatch, Is.EqualTo(128));
            Assert.That(config.CommandSchemas, Has.Count.EqualTo(3));
            Assert.That(config.ReconnectWindowSeconds, Is.EqualTo(30));
            Assert.That(config.ControlChannelId, Is.EqualTo(0));
            Assert.That(config.CommandChannelId, Is.EqualTo(1));
            Assert.That(config.StateChannelId, Is.EqualTo(2));
            Assert.That(config.InputChannelId, Is.EqualTo(3));
            Assert.That(config.FixedInputSchemaId, Is.EqualTo(1));
            Assert.That(config.FixedInputFramePayloadBytes, Is.EqualTo(12));
            Assert.That(config.FixedInputLeadTicks, Is.EqualTo(2));
            Assert.That(config.TransportMaxConnectAttempts, Is.EqualTo(10));
            Assert.That(config.TransportDisconnectTimeoutMilliseconds, Is.EqualTo(5_000));
            Assert.That(config.ReliableDisconnectFlushTimeoutMilliseconds, Is.EqualTo(4_000));
            Assert.That(config.MaxServerOutboundBytesPerSecondPerClient, Is.EqualTo(256 * 1024));
            Assert.That(config.TickP95BudgetMicroseconds, Is.EqualTo(26_700));
            Assert.That(config.TickP99BudgetMicroseconds, Is.EqualTo(31_000));
        });
    }

    [Test]
    public void CapacityMismatch_IsRejectedAtStartup()
    {
        NetworkRuntimeConfig config = CreateRtsDuelConfig();
        config.ReplicationEntityCapacityPerSeat = 100_001;

        Assert.That(config.Validate, Throws.InvalidOperationException.With.Message.Contains("exceeds global network entity capacity"));
    }

    [Test]
    public void PerSeatCapacity_MustFitReplicationWireCounts()
    {
        NetworkRuntimeConfig config = CreateRtsDuelConfig();
        config.ReplicationEntityCapacityPerSeat = 70_000;

        Assert.That(config.Validate, Throws.InvalidOperationException.With.Message.Contains("wire count limit"));
    }

    [Test]
    public void DisclosureLog_MustHoldOneCompleteAreaTransition()
    {
        NetworkRuntimeConfig config = CreateRtsDuelConfig();
        config.DisclosureChangeLogCapacity = (config.ReplicationEntityCapacityPerSeat * 2) - 1;

        Assert.That(
            config.Validate,
            Throws.InvalidOperationException.With.Message.Contains("maximum-area transition"));
    }

    [Test]
    public void StatePublishRate_MustDivideSimulationRateExactly()
    {
        NetworkRuntimeConfig config = CreateRtsDuelConfig();
        config.StatePublishRateHz = 7;

        Assert.That(
            config.Validate,
            Throws.InvalidOperationException.With.Message.Contains("must divide simulation rate"));
    }

    [Test]
    public void ReliableDisconnectFlushTimeout_MustPrecedeTransportDisconnectTimeout()
    {
        NetworkRuntimeConfig config = CreateRtsDuelConfig();
        config.ReliableDisconnectFlushTimeoutMilliseconds = config.TransportDisconnectTimeoutMilliseconds;

        Assert.That(
            config.Validate,
            Throws.InvalidOperationException.With.Message.Contains("must be below transport disconnect timeout"));
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
            SubmitMode = OrderSubmitMode.Queued,
        });
        Assert.That(
            duplicate.Validate,
            Throws.InvalidOperationException.With.Message.Contains("duplicated"));
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
        GlobalNetworkEntityCapacity = 100_000,
        ReplicationEntityCapacityPerSeat = 512,
        OrderQueueCapacity = 512,
        MaxCommandBatchesPerSecondPerPlayer = 32,
        CommandBurstBatchCapacity = 32,
        MaxActorsPerCommandBatch = 128,
        CommandSequenceHistoryCapacity = 128,
        MaxPastTargetTicks = 3,
        MaxFutureTargetTicks = 6,
        NetworkAdmissionResultCapacity = 512,
        EntityAdmissionResultCapacity = 1024,
        ReconnectWindowSeconds = 30,
        BaselineCapacity = 32,
        DisclosureChangeLogCapacity = 4096,
        DatagramQueueCapacity = 1024,
        ConnectionEventCapacity = 64,
        MaxDatagramPayloadBytes = 1200,
        TransportMaxConnectAttempts = 10,
        TransportDisconnectTimeoutMilliseconds = 5_000,
        ReliableDisconnectFlushTimeoutMilliseconds = 4_000,
        TransportChannelCount = 8,
        ControlChannelId = 0,
        CommandChannelId = 1,
        StateChannelId = 2,
        InputChannelId = 3,
        FixedInputHistoryTicksPerSeat = 8,
        FixedInputSchemaId = 1,
        FixedInputFramePayloadBytes = 12,
        FixedInputMaxFutureTicks = 4,
        FixedInputLeadTicks = 2,
        FixedInputMaxFramesPerBatch = 4,
        FixedInputPendingFrameCapacity = 8,
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
                SubmitMode = OrderSubmitMode.Queued,
            },
            new NetworkCommandSchemaConfig
            {
                OrderTypeKey = "attackTarget",
                TargetKind = NetworkCommandTargetKind.NetworkEntity,
                SubmitMode = OrderSubmitMode.Queued,
                RequiredTargetPositionAccess = KnowledgePositionAccess.LastKnown,
            },
            new NetworkCommandSchemaConfig
            {
                OrderTypeKey = "stop",
                TargetKind = NetworkCommandTargetKind.None,
                SubmitMode = OrderSubmitMode.Immediate,
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
