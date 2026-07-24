using Ludots.Core.Networking.Configuration;
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
            Assert.That(config.NetworkEntityCapacity, Is.EqualTo(256));
            Assert.That(config.MaxCommandBatchesPerSecondPerPlayer, Is.EqualTo(32));
            Assert.That(config.MaxActorsPerCommandBatch, Is.EqualTo(128));
            Assert.That(config.ReconnectWindowSeconds, Is.EqualTo(30));
            Assert.That(config.MaxServerOutboundBytesPerSecondPerClient, Is.EqualTo(256 * 1024));
            Assert.That(config.TickP95BudgetMicroseconds, Is.EqualTo(26_700));
            Assert.That(config.TickP99BudgetMicroseconds, Is.EqualTo(31_000));
        });
    }

    [Test]
    public void CapacityMismatch_IsRejectedAtStartup()
    {
        NetworkRuntimeConfig config = CreateRtsDuelConfig();
        config.ReplicationPacketEntityCapacity = 128;

        Assert.That(config.Validate, Throws.InvalidOperationException.With.Message.Contains("below network entity capacity"));
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
        ReplicationPacketEntityCapacity = 256,
        DisclosureChangeLogCapacity = 4096,
        DatagramQueueCapacity = 1024,
        ConnectionEventCapacity = 64,
        MaxDatagramPayloadBytes = 1200,
        TransportChannelCount = 8,
        MaxServerOutboundBytesPerSecondPerClient = 256 * 1024,
        TickP95BudgetMicroseconds = 26_700,
        TickP99BudgetMicroseconds = 31_000,
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
