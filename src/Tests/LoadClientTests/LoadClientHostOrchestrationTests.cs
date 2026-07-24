using Ludots.App.LoadClients;
using Ludots.Core.Networking.FixedInput;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Session;
using NUnit.Framework;

namespace Ludots.Tests.LoadClients;

[TestFixture]
public sealed class LoadClientHostOrchestrationTests
{
    private string _credentialDirectory = null!;

    [SetUp]
    public void SetUp()
    {
        _credentialDirectory = Path.Combine(
            Path.GetTempPath(),
            nameof(LoadClientHostOrchestrationTests),
            Guid.NewGuid().ToString("N"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_credentialDirectory))
        {
            Directory.Delete(_credentialDirectory, recursive: true);
        }
    }

    [Test]
    public void Run_PartialConnect_IsFatalAndDisposesConstructedClients()
    {
        LoadClientHostConfig config = LoadClientHostConfig.ParseJson(
            LoadClientHostConfigTests.CreateValidJson(clientCount: 2)
                .Replace("\"credentialDirectory\": \"credentials\"",
                    $"\"credentialDirectory\": {JsonEscape(_credentialDirectory)}",
                    StringComparison.Ordinal));
        var factory = new PartialConnectSlotFactory();
        var host = new LoadClientHost(config, factory, baseDirectory: _credentialDirectory);
        LoadClientRunEvidence evidence = host.Run(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(evidence.Outcome, Is.EqualTo(LoadClientRunOutcome.Failed));
            Assert.That(evidence.FaultKind, Is.EqualTo(LoadClientFaultKind.PartialConnect));
            Assert.That(evidence.ConfiguredClients, Is.EqualTo(2));
            Assert.That(factory.DisposeCount, Is.EqualTo(2));
        });
    }

    [Test]
    public void Run_Cancellation_ReportsCancelledNotPassed()
    {
        LoadClientHostConfig config = LoadClientHostConfig.ParseJson(
            LoadClientHostConfigTests.CreateValidJson(clientCount: 2)
                .Replace("\"credentialDirectory\": \"credentials\"",
                    $"\"credentialDirectory\": {JsonEscape(_credentialDirectory)}",
                    StringComparison.Ordinal)
                .Replace("\"durationSeconds\": 2.0", "\"durationSeconds\": 30.0", StringComparison.Ordinal)
                .Replace("\"connectTimeoutSeconds\": 2.0", "\"connectTimeoutSeconds\": 30.0", StringComparison.Ordinal));
        using var cts = new CancellationTokenSource();
        var factory = new ImmediateCancelSlotFactory(cts);
        var host = new LoadClientHost(config, factory, baseDirectory: _credentialDirectory);
        LoadClientRunEvidence evidence = host.Run(cts.Token);

        Assert.Multiple(() =>
        {
            Assert.That(evidence.Outcome, Is.EqualTo(LoadClientRunOutcome.Cancelled));
            Assert.That(evidence.FaultKind, Is.EqualTo(LoadClientFaultKind.Cancelled));
            Assert.That(factory.DisposeCount, Is.EqualTo(factory.CreateCount));
            Assert.That(factory.CreateCount, Is.GreaterThan(0));
        });
    }

    [Test]
    public void DeterministicPayloadSource_AndClock_HoldThirtyHzOverOneSecond()
    {
        var client = new FakeFixedInputClient();
        var source = new DeterministicFixedInputPayloadSource(clientIndex: 7);
        var clock = new ReplicatedClientFixedInputClock(
            client,
            source,
            simulationTickRateHz: 30,
            payloadBytes: 12,
            fixedInputLeadTicks: 1,
            fixedInputMaxFutureTicks: 8,
            maxStepsPerAdvance: 8,
            maxAccumulatedSteps: 64);

        Assert.That(clock.Advance(0f).Status,
            Is.EqualTo(ReplicatedClientFixedInputClockAdvanceStatus.WaitingForAuthoritativeAcknowledgement));
        client.ApplyAuthoritativeAck(committedThroughTick: 0);

        int emitted = 0;
        for (int frame = 0; frame < 60; frame++)
        {
            ReplicatedClientFixedInputClockAdvanceResult result = clock.Advance(1f / 60f);
            Assert.That(result.IsSuccess, Is.True);
            emitted += result.StepsEmitted;
        }

        Assert.That(emitted, Is.EqualTo(30));
        Assert.That(client.PulseCount, Is.EqualTo(30));
        Assert.That(clock.SimulationTickRateHz, Is.EqualTo(30));
    }

    [Test]
    public void SteadyStatePump_DoesNotAllocate()
    {
        var client = new FakeFixedInputClient();
        var source = new DeterministicFixedInputPayloadSource(clientIndex: 0);
        var clock = new ReplicatedClientFixedInputClock(
            client,
            source,
            simulationTickRateHz: 30,
            payloadBytes: 12,
            fixedInputLeadTicks: 1,
            fixedInputMaxFutureTicks: 8,
            maxStepsPerAdvance: 8,
            maxAccumulatedSteps: 64);
        _ = clock.Advance(0f);
        client.ApplyAuthoritativeAck(0);

        for (int i = 0; i < 30; i++)
        {
            _ = clock.Advance(1f / 30f);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 120; i++)
        {
            ReplicatedClientFixedInputClockAdvanceResult result = clock.Advance(1f / 30f);
            if (!result.IsSuccess)
            {
                Assert.Fail($"Advance failed: {result.Status}");
            }
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.EqualTo(0));
    }

    private static string JsonEscape(string path) =>
        $"\"{path.Replace("\\", "\\\\", StringComparison.Ordinal)}\"";

    private sealed class PartialConnectSlotFactory : ILoadClientSlotFactory
    {
        private readonly LiteNetLibLoadClientSlotFactory _inner = new();
        public int DisposeCount { get; private set; }

        public LoadClientSlot Create(int clientIndex, LoadClientHostConfig config, string credentialDirectory)
        {
            LoadClientSlot slot = _inner.Create(clientIndex, config, credentialDirectory);
            slot.ConnectOverride = () => clientIndex == 0;
            slot.OnDisposed = () => DisposeCount++;
            return slot;
        }
    }

    private sealed class ImmediateCancelSlotFactory : ILoadClientSlotFactory
    {
        private readonly LiteNetLibLoadClientSlotFactory _inner = new();
        private readonly CancellationTokenSource _cts;
        public int CreateCount { get; private set; }
        public int DisposeCount { get; private set; }

        public ImmediateCancelSlotFactory(CancellationTokenSource cts) => _cts = cts;

        public LoadClientSlot Create(int clientIndex, LoadClientHostConfig config, string credentialDirectory)
        {
            CreateCount++;
            LoadClientSlot slot = _inner.Create(clientIndex, config, credentialDirectory);
            slot.OnDisposed = () => DisposeCount++;
            if (clientIndex >= 0)
            {
                // Cancel after first slot is constructed so Run observes cancellation before/during connect.
                _cts.Cancel();
            }

            return slot;
        }
    }

    private sealed class FakeFixedInputClient : IReplicatedClientFixedInputPort
    {
        private readonly SessionEpoch _epoch = new(1);
        private ulong _ackVersion;
        private uint _ackTick;
        private uint _lastEnqueued;
        private bool _hasEnqueued;

        public int PulseCount { get; private set; }

        public ReplicatedClientConnectionState State => ReplicatedClientConnectionState.Connected;
        public SessionEpoch SessionEpoch => _epoch;
        public uint FixedInputAcknowledgedCommittedTick => _ackTick;
        public ulong FixedInputAcknowledgementObservationVersion => _ackVersion;
        public bool HasEnqueuedFixedInputTargetTick => _hasEnqueued;
        public uint LastEnqueuedFixedInputTargetTick => _lastEnqueued;

        public void ApplyAuthoritativeAck(uint committedThroughTick)
        {
            _ackTick = committedThroughTick;
            _ackVersion++;
        }

        public FixedInputOutboxEnqueueStatus TrySubmitFixedInput(uint targetTick, ReadOnlySpan<byte> payload)
        {
            _lastEnqueued = targetTick;
            _hasEnqueued = true;
            return FixedInputOutboxEnqueueStatus.Enqueued;
        }

        public bool TryPulseFixedInputSend()
        {
            PulseCount++;
            return true;
        }
    }
}
