using Ludots.Adapter.LiteNetLib;
using Ludots.Core.Hosting;
using Ludots.Core.Networking.Configuration;
using Ludots.Core.Networking.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.NetworkingAdapter;

[TestFixture]
public sealed class LiteNetLibFaultInjectionTests
{
    [Test]
    public void UnstableProfile_ConsumesEveryConfiguredFaultDimension()
    {
        var config = new NetworkRuntimeConfig
        {
            StatePublishRateHz = 10,
            NormalConnection = new NetworkFaultProfileConfig(),
            UnstableConnection = new NetworkFaultProfileConfig
            {
                RoundTripLatencyMs = 180,
                JitterMs = 30,
                PacketLossPermille = 50,
                ReorderPermille = 20,
            },
        };
        var host = new NetworkHostBootstrapConfig
        {
            FaultProfile = NetworkHostBootstrapConfig.UnstableFaultProfile,
            FaultSeed = 709,
        };

        LiteNetLibFaultInjectionSettings settings = LiteNetLibFaultInjectionSettings.Create(config, host);

        Assert.Multiple(() =>
        {
            Assert.That(settings.ProfileId, Is.EqualTo("unstable"));
            Assert.That(settings.Seed, Is.EqualTo(709));
            Assert.That(settings.RoundTripLatencyMs, Is.EqualTo(180));
            Assert.That(settings.JitterMs, Is.EqualTo(30));
            Assert.That(settings.PacketLossPermille, Is.EqualTo(50));
            Assert.That(settings.PacketLossPercent, Is.EqualTo(5));
            Assert.That(settings.ReorderPermille, Is.EqualTo(20));
            Assert.That(settings.ReorderHoldTimeoutMilliseconds, Is.EqualTo(405));
        });

        NetworkFaultInjectionConfigurationSnapshot snapshot = settings.CaptureConfiguration();
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.TransportIdentity, Is.EqualTo(LiteNetLibTransportFactory.TransportIdentity));
            Assert.That(snapshot.ProfileId, Is.EqualTo("unstable"));
            Assert.That(snapshot.Seed, Is.EqualTo(709));
            Assert.That(snapshot.PacketLossPermille, Is.EqualTo(50));
            Assert.That(snapshot.IsEnabled, Is.True);
        });
    }

    [Test]
    public void UnsupportedSubPercentLoss_FailsInsteadOfRounding()
    {
        var config = new NetworkRuntimeConfig
        {
            NormalConnection = new NetworkFaultProfileConfig
            {
                PacketLossPermille = 5,
            },
        };
        var host = new NetworkHostBootstrapConfig
        {
            FaultProfile = NetworkHostBootstrapConfig.NormalFaultProfile,
            FaultSeed = 1,
        };

        Assert.That(
            () => LiteNetLibFaultInjectionSettings.Create(config, host),
            Throws.InvalidOperationException.With.Message.Contains("whole-percent"));
    }

    [TestCase(11, 0, false)]
    [TestCase(12, 0, true)]
    [TestCase(21, 10, false)]
    [TestCase(22, 10, true)]
    public void MinimumRepresentableLatencyBoundary_IsEnforced(
        int roundTripLatencyMs,
        int jitterMs,
        bool expectedAccepted)
    {
        var config = new NetworkRuntimeConfig
        {
            NormalConnection = new NetworkFaultProfileConfig
            {
                RoundTripLatencyMs = roundTripLatencyMs,
                JitterMs = jitterMs,
            },
        };
        var host = new NetworkHostBootstrapConfig
        {
            FaultProfile = NetworkHostBootstrapConfig.NormalFaultProfile,
            FaultSeed = 1,
        };

        if (expectedAccepted)
        {
            Assert.DoesNotThrow(() => LiteNetLibFaultInjectionSettings.Create(config, host));
        }
        else
        {
            Assert.That(
                () => LiteNetLibFaultInjectionSettings.Create(config, host),
                Throws.InvalidOperationException.With.Message.Contains("cannot represent"));
        }
    }

    [Test]
    public void SequencedReorder_HoldsAcrossHighFrequencyPumpsAndDeliversNewStateBeforeOldState()
    {
        const byte stateChannel = 2;
        var filter = new DeterministicSequencedReorderFilter(
            connectionCapacity: 1,
            maxPayloadBytes: 32,
            stateChannel,
            reorderPermille: 1000,
            seed: 709,
            holdTimeoutMilliseconds: 405);
        var destination = new FixedDatagramQueue(capacity: 4, maxPayloadBytes: 32);

        filter.BeginPump(monotonicMilliseconds: 0);
        filter.Enqueue(connection: 1, stateChannel, new byte[] { 1 }, destination);
        for (long milliseconds = 16; milliseconds < 100; milliseconds += 16)
        {
            filter.BeginPump(milliseconds);
            filter.FlushExpired(destination);
        }

        byte[] receive = new byte[32];
        Assert.That(destination.TryDequeue(receive, out _, out _, out _), Is.False);

        filter.BeginPump(monotonicMilliseconds: 100);
        filter.Enqueue(connection: 1, stateChannel, new byte[] { 2 }, destination);

        Assert.That(destination.TryDequeue(receive, out int length, out _, out byte channel), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(length, Is.EqualTo(1));
            Assert.That(channel, Is.EqualTo(stateChannel));
            Assert.That(receive[0], Is.EqualTo(2));
            Assert.That(filter.ReorderedStateDatagramCount, Is.EqualTo(1));
        });
        Assert.That(destination.TryDequeue(receive, out length, out _, out channel), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(length, Is.EqualTo(1));
            Assert.That(channel, Is.EqualTo(stateChannel));
            Assert.That(receive[0], Is.EqualTo(1));
            Assert.That(destination.TryDequeue(receive, out _, out _, out _), Is.False);
        });
    }

    [Test]
    public void SequencedReorder_ExpiresOrDiscardsHeldStateExplicitly()
    {
        const byte stateChannel = 2;
        var filter = new DeterministicSequencedReorderFilter(
            connectionCapacity: 1,
            maxPayloadBytes: 32,
            stateChannel,
            reorderPermille: 1000,
            seed: 709,
            holdTimeoutMilliseconds: 250);
        var destination = new FixedDatagramQueue(capacity: 4, maxPayloadBytes: 32);
        byte[] receive = new byte[32];

        filter.BeginPump(monotonicMilliseconds: 0);
        filter.Enqueue(connection: 1, stateChannel, new byte[] { 1 }, destination);
        filter.BeginPump(monotonicMilliseconds: 249);
        filter.FlushExpired(destination);
        Assert.That(destination.TryDequeue(receive, out _, out _, out _), Is.False);

        filter.BeginPump(monotonicMilliseconds: 250);
        filter.FlushExpired(destination);
        Assert.That(destination.TryDequeue(receive, out int length, out _, out _), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(length, Is.EqualTo(1));
            Assert.That(receive[0], Is.EqualTo(1));
            Assert.That(filter.ReorderedStateDatagramCount, Is.Zero);
        });

        filter.BeginPump(monotonicMilliseconds: 300);
        filter.Enqueue(connection: 1, stateChannel, new byte[] { 2 }, destination);
        filter.DiscardConnection(1);
        filter.BeginPump(monotonicMilliseconds: 1000);
        filter.FlushExpired(destination);
        Assert.That(destination.TryDequeue(receive, out _, out _, out _), Is.False);
    }

    [Test]
    public void SequencedReorder_SteadyStateAllocatesZeroBytes()
    {
        const byte stateChannel = 2;
        var filter = new DeterministicSequencedReorderFilter(
            connectionCapacity: 1,
            maxPayloadBytes: 32,
            stateChannel,
            reorderPermille: 1000,
            seed: 709,
            holdTimeoutMilliseconds: 250);
        var destination = new FixedDatagramQueue(capacity: 4, maxPayloadBytes: 32);
        byte[] first = { 1 };
        byte[] second = { 2 };
        byte[] receive = new byte[32];

        RunReorderCycles(
            filter,
            destination,
            first,
            second,
            receive,
            stateChannel,
            startMilliseconds: 0,
            cycleCount: 1000);
        GC.GetAllocatedBytesForCurrentThread();
        long before = GC.GetAllocatedBytesForCurrentThread();
        RunReorderCycles(
            filter,
            destination,
            first,
            second,
            receive,
            stateChannel,
            startMilliseconds: 1_000_000,
            cycleCount: 10_000);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.That(allocated, Is.Zero, $"Expected 0 B allocation, observed {allocated} B.");
    }

    private static void RunReorderCycles(
        DeterministicSequencedReorderFilter filter,
        FixedDatagramQueue destination,
        byte[] first,
        byte[] second,
        byte[] receive,
        byte stateChannel,
        long startMilliseconds,
        int cycleCount)
    {
        for (int i = 0; i < cycleCount; i++)
        {
            long start = startMilliseconds + (i * 200L);
            filter.BeginPump(start);
            filter.Enqueue(connection: 1, stateChannel, first, destination);
            filter.FlushExpired(destination);
            filter.BeginPump(start + 100L);
            filter.Enqueue(connection: 1, stateChannel, second, destination);
            filter.FlushExpired(destination);
            if (!destination.TryDequeue(receive, out _, out _, out _) ||
                !destination.TryDequeue(receive, out _, out _, out _))
            {
                throw new InvalidOperationException("Expected reordered datagrams were not available.");
            }
        }
    }
}
