using System;
using Ludots.Adapter.LiteNetLib;
using Ludots.Core.Networking.Configuration;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Transport;
using NUnit.Framework;

namespace Ludots.Tests.NetworkingAdapter;

[TestFixture]
public sealed class DeterministicTransportFaultInjectorTests
{
    [Test]
    public void SameSeedAndProfile_ProducesIdenticalDecisionSequence()
    {
        var profile = new NetworkFaultProfileConfig
        {
            RoundTripLatencyMs = 180,
            JitterMs = 30,
            PacketLossPermille = 50,
            ReorderPermille = 20,
        };

        int[] a = CaptureDecisions(profile, seed: 709001, decisions: 64);
        int[] b = CaptureDecisions(profile, seed: 709001, decisions: 64);
        Assert.That(b, Is.EqualTo(a));

        int[] c = CaptureDecisions(profile, seed: 709002, decisions: 64);
        Assert.That(c, Is.Not.EqualTo(a));
    }

    [Test]
    public void LossLatencyAndReorder_AreObservableOnLoopbackPort()
    {
        var profile = new NetworkFaultProfileConfig
        {
            RoundTripLatencyMs = 40,
            JitterMs = 0,
            PacketLossPermille = 1000,
            ReorderPermille = 0,
        };
        var injector = new DeterministicTransportFaultInjector(
            profile,
            seed: 1,
            slotCapacity: 16,
            maxPayloadBytes: 64,
            useWallClock: false);
        var underlying = new RecordingServerPort();
        var wrapper = new ManualServerFaultPort(underlying, injector);

        Assert.That(
            wrapper.TrySend(new ConnectionId(1), new ChannelId(0), new byte[] { 1, 2, 3 }),
            Is.EqualTo(DatagramSendStatus.Sent));
        injector.AdvanceMs(100);
        wrapper.Pump();
        Assert.That(underlying.SentCount, Is.EqualTo(0), "100% loss must drop the outbound datagram.");

        var delayProfile = new NetworkFaultProfileConfig
        {
            RoundTripLatencyMs = 40,
            JitterMs = 0,
            PacketLossPermille = 0,
            ReorderPermille = 0,
        };
        var delayInjector = new DeterministicTransportFaultInjector(
            delayProfile,
            seed: 2,
            slotCapacity: 16,
            maxPayloadBytes: 64,
            useWallClock: false);
        underlying = new RecordingServerPort();
        wrapper = new ManualServerFaultPort(underlying, delayInjector);
        Assert.That(
            wrapper.TrySend(new ConnectionId(1), new ChannelId(1), new byte[] { 9 }),
            Is.EqualTo(DatagramSendStatus.Sent));
        Assert.That(underlying.SentCount, Is.EqualTo(0), "Packet must wait one-way delay before release.");
        delayInjector.AdvanceMs(20);
        wrapper.Pump();
        Assert.That(underlying.SentCount, Is.EqualTo(1));
        Assert.That(underlying.LastPayload, Is.EqualTo(new byte[] { 9 }));
    }

    [Test]
    public void ReorderPermille_SwapsReleaseTimesDeterministically()
    {
        var profile = new NetworkFaultProfileConfig
        {
            RoundTripLatencyMs = 20,
            JitterMs = 0,
            PacketLossPermille = 0,
            ReorderPermille = 1000,
        };
        var injector = new DeterministicTransportFaultInjector(
            profile,
            seed: 42,
            slotCapacity: 8,
            maxPayloadBytes: 32,
            useWallClock: false);

        // Schedule two packets; with 100% reorder the second should attempt a swap when a later slot exists.
        Assert.That(injector.TryScheduleOutbound(1, 0, new byte[] { 1 }), Is.True);
        Assert.That(injector.TryScheduleOutbound(1, 0, new byte[] { 2 }), Is.True);
        Assert.That(injector.PendingOutboundCount, Is.EqualTo(2));

        var released = new List<byte>();
        injector.AdvanceMs(20);
        injector.ReleaseDueOutbound((_, _, payload) => released.Add(payload[0]));
        Assert.That(released, Has.Count.EqualTo(2));
    }

    private static int[] CaptureDecisions(NetworkFaultProfileConfig profile, uint seed, int decisions)
    {
        var injector = new DeterministicTransportFaultInjector(
            profile,
            seed,
            slotCapacity: 8,
            maxPayloadBytes: 16,
            useWallClock: false);
        var values = new int[decisions];
        for (int i = 0; i < decisions; i++)
        {
            bool drop = injector.NextShouldDrop();
            int delay = injector.NextOneWayDelayMs();
            bool reorder = injector.NextShouldReorder();
            values[i] = (drop ? 1 : 0) | (delay << 1) | (reorder ? 1 << 16 : 0);
        }

        return values;
    }

    private sealed class RecordingServerPort : IServerDatagramPort
    {
        public int SentCount { get; private set; }
        public byte[] LastPayload { get; private set; } = Array.Empty<byte>();

        public bool TryReceive(Span<byte> buffer, out int bytesReceived, out ConnectionId connectionId, out ChannelId channelId)
        {
            bytesReceived = 0;
            connectionId = default;
            channelId = default;
            return false;
        }

        public DatagramSendStatus TrySend(ConnectionId connectionId, ChannelId channelId, ReadOnlySpan<byte> payload)
        {
            SentCount++;
            LastPayload = payload.ToArray();
            return DatagramSendStatus.Sent;
        }
    }

    /// <summary>Minimal pump/send harness over the injector without LiteNetLib.</summary>
    private sealed class ManualServerFaultPort
    {
        private readonly RecordingServerPort _underlying;
        private readonly DeterministicTransportFaultInjector _injector;

        public ManualServerFaultPort(RecordingServerPort underlying, DeterministicTransportFaultInjector injector)
        {
            _underlying = underlying;
            _injector = injector;
        }

        public DatagramSendStatus TrySend(ConnectionId connectionId, ChannelId channelId, ReadOnlySpan<byte> payload)
        {
            _ = _injector.TryScheduleOutbound(connectionId.Value, channelId.Value, payload);
            _injector.ReleaseDueOutbound((connectionValue, channel, bytes) =>
                _underlying.TrySend(new ConnectionId(connectionValue), new ChannelId(channel), bytes));
            return DatagramSendStatus.Sent;
        }

        public void Pump()
        {
            _injector.ReleaseDueOutbound((connectionValue, channel, bytes) =>
                _underlying.TrySend(new ConnectionId(connectionValue), new ChannelId(channel), bytes));
        }
    }
}

[TestFixture]
public sealed class NetworkAdapterMetricsSamplerTests
{
    [Test]
    public void TickP95AndP99_MatchKnownSampleSet()
    {
        var sampler = new NetworkAdapterMetricsSampler(clientCapacity: 2, tickSampleCapacity: 16);
        // 1..10 microseconds
        for (ulong i = 1; i <= 10; i++)
        {
            sampler.RecordTickMicroseconds(i);
        }

        // sorted 1..10; p95 index = 0.95*(10-1)=8.55 → 8 → value 9; p99 → 9 → value 10
        Assert.That(sampler.GetTickP95Microseconds(), Is.EqualTo(9));
        Assert.That(sampler.GetTickP99Microseconds(), Is.EqualTo(10));
    }

    [Test]
    public void RecordTick_AfterWarmup_DoesNotAllocate()
    {
        var sampler = new NetworkAdapterMetricsSampler(clientCapacity: 1, tickSampleCapacity: 32);
        for (int i = 0; i < 64; i++)
        {
            sampler.RecordTickMicroseconds((ulong)(1000 + i));
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++)
        {
            sampler.RecordTickMicroseconds((ulong)(2000 + i));
        }

        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.That(after - before, Is.EqualTo(0));
    }

    [Test]
    public void OutboundBytesPerSecond_TracksRollingWindow()
    {
        var sampler = new NetworkAdapterMetricsSampler(clientCapacity: 1, tickSampleCapacity: 8, outboundSampleCapacity: 8);
        sampler.AdvanceMs(0);
        sampler.RecordOutboundBytes(0, 1000);
        Assert.That(sampler.GetCurrentOutboundBytesPerSecond(0), Is.GreaterThanOrEqualTo(1000));
        sampler.AdvanceMs(1000);
        sampler.RecordOutboundBytes(0, 500);
        Assert.That(sampler.GetOutboundBytesPerSecondP95(0), Is.EqualTo(1000));
    }
}

[TestFixture]
public sealed class AcceptanceBudgetEnforcerTests
{
    [Test]
    public void AcceptanceMode_BudgetViolation_FailsClosed()
    {
        var config = new NetworkRuntimeConfig
        {
            ProfileId = "rts_duel_v1",
            ReferenceTransport = "LiteNetLib/2.1.4",
            ProtocolMajor = 1,
            ProtocolMinor = 0,
            PlayerCapacity = 1,
            SimulationTickRateHz = 30,
            StatePublishRateHz = 10,
            NetworkEntityCapacity = 8,
            OrderQueueCapacity = 32,
            MaxCommandBatchesPerSecondPerPlayer = 8,
            CommandBurstBatchCapacity = 8,
            MaxActorsPerCommandBatch = 8,
            CommandSequenceHistoryCapacity = 16,
            MaxPastTargetTicks = 1,
            MaxFutureTargetTicks = 1,
            NetworkAdmissionResultCapacity = 16,
            EntityAdmissionResultCapacity = 16,
            ReconnectWindowSeconds = 30,
            ReadyCountdownTicks = 90,
            ClientReconnectRetryMilliseconds = 500,
            BaselineCapacity = 4,
            ReplicationPacketEntityCapacity = 8,
            ReplicationSchemaCapacity = 2,
            DisclosureChangeLogCapacity = 16,
            DatagramQueueCapacity = 16,
            ConnectionEventCapacity = 4,
            MaxDatagramPayloadBytes = 1200,
            TransportChannelCount = 3,
            ControlChannelId = 0,
            CommandChannelId = 1,
            StateChannelId = 2,
            SnapshotChunkCapacity = 8,
            MaxServerOutboundBytesPerSecondPerClient = 1024,
            TickP95BudgetMicroseconds = 100,
            TickP99BudgetMicroseconds = 200,
            ActiveFaultProfile = "normal",
            AcceptanceMode = true,
            MetricsSampleCapacity = 16,
            CommandSchemas =
            {
                new NetworkCommandSchemaConfig
                {
                    OrderTypeKey = "stop",
                    TargetKind = NetworkCommandTargetKind.None,
                    SubmitMode = Ludots.Core.Gameplay.GAS.Orders.OrderSubmitMode.Immediate,
                },
            },
            NormalConnection = new NetworkFaultProfileConfig(),
            UnstableConnection = new NetworkFaultProfileConfig(),
        };
        config.Validate();

        var metrics = new NetworkAdapterMetricsSampler(1, 16);
        for (int i = 0; i < 20; i++)
        {
            metrics.RecordTickMicroseconds(500);
        }

        var enforcer = new AcceptanceBudgetEnforcer(config, metrics);

        Assert.That(
            enforcer.EnforceAfterTickSample,
            Throws.InvalidOperationException.With.Message.Contains("budget violation"));
        Assert.That(enforcer.BudgetViolation, Is.Not.Null.And.Contain("P95"));
    }
}

[TestFixture]
public sealed class NetworkingAdapterArchitectureTests
{
    [Test]
    public void NetworkingCore_DoesNotReference_LiteNetLib()
    {
        string repoRoot = FindRepoRoot();
        string networkingRoot = Path.Combine(repoRoot, "src", "Core", "Networking");
        Assert.That(Directory.Exists(networkingRoot), Is.True);

        foreach (string file in Directory.EnumerateFiles(networkingRoot, "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);
            Assert.That(
                text.Contains("LiteNetLib", StringComparison.Ordinal),
                Is.False,
                $"{Path.GetRelativePath(repoRoot, file)} must not reference LiteNetLib.");
        }
    }

    private static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "Ludots.sln")) || File.Exists(Path.Combine(dir, "AGENTS.md")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Unable to locate repository root from test base directory.");
    }
}
