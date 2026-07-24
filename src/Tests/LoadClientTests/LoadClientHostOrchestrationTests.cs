using System.Diagnostics;
using System.Globalization;
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
        AssertZeroAllocated(allocated);
    }

    [Test]
    public void Run_StaggeredReadiness_DoesNotConsumeWarmupOrDuration()
    {
        const double readyDelaySeconds = 0.55d;
        const double durationSeconds = 0.45d;
        const double warmUpSeconds = 0.1d;
        LoadClientHostConfig config = ParseTimedConfig(
            clientCount: 2,
            durationSeconds: durationSeconds,
            warmUpSeconds: warmUpSeconds,
            connectTimeoutSeconds: 5d,
            readyTimeoutSeconds: 5d);
        var factory = new ScriptedSlotFactory(
            new ScriptedClientSpec(ReadyAfterSeconds: 0.05d, StepsPerSecond: 30d),
            new ScriptedClientSpec(ReadyAfterSeconds: readyDelaySeconds, StepsPerSecond: 30d));
        var host = new LoadClientHost(config, factory, baseDirectory: _credentialDirectory);
        var wall = Stopwatch.StartNew();
        LoadClientRunEvidence evidence = host.Run(CancellationToken.None);
        wall.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(evidence.Outcome, Is.EqualTo(LoadClientRunOutcome.Passed), evidence.FaultDetail);
            Assert.That(evidence.HeldThirtyHzContract, Is.True);
            Assert.That(evidence.ReadyClients, Is.EqualTo(2));
            // Completion is readyAt + warmUp + duration. If readiness ate the window, or if warm-up
            // were omitted from the deadline, elapsed would undershoot this floor (~1.05s).
            double minElapsed = readyDelaySeconds + warmUpSeconds + durationSeconds - 0.05d;
            Assert.That(evidence.ElapsedSeconds, Is.GreaterThanOrEqualTo(minElapsed));
            Assert.That(wall.Elapsed.TotalSeconds, Is.GreaterThanOrEqualTo(minElapsed));
            Assert.That(evidence.FixedInputsGenerated, Is.GreaterThan(0));
        });
    }

    [Test]
    public void Run_OneLaggingClient_FailsEvenWhenAggregateCouldPass()
    {
        const double durationSeconds = 0.6d;
        LoadClientHostConfig config = ParseTimedConfig(
            clientCount: 2,
            durationSeconds: durationSeconds,
            warmUpSeconds: 0d,
            connectTimeoutSeconds: 5d,
            readyTimeoutSeconds: 5d);
        var factory = new ScriptedSlotFactory(
            new ScriptedClientSpec(ReadyAfterSeconds: 0d, StepsPerSecond: 30d),
            new ScriptedClientSpec(ReadyAfterSeconds: 0d, StepsPerSecond: 8d));
        var host = new LoadClientHost(config, factory, baseDirectory: _credentialDirectory);
        LoadClientRunEvidence evidence = host.Run(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(evidence.Outcome, Is.EqualTo(LoadClientRunOutcome.Failed));
            Assert.That(evidence.FaultKind, Is.EqualTo(LoadClientFaultKind.TickRateContractBroken));
            Assert.That(evidence.HeldThirtyHzContract, Is.False);
            Assert.That(evidence.FaultDetail, Does.Contain("client 1"));
            Assert.That(evidence.FaultDetail, Does.Contain("measurementGenerated="));
            Assert.That(evidence.FaultDetail, Does.Contain("expected=["));
            // Aggregate across both clients can still look healthy while client 1 lagged.
            Assert.That(evidence.FixedInputsGenerated, Is.GreaterThan(0));
        });
    }

    // Isolates the exact 0B assert from NUnit/tiered JIT instrumentation of the test method body.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void AssertZeroAllocated(long allocated)
    {
        Assert.That(allocated, Is.EqualTo(0));
    }

    private LoadClientHostConfig ParseTimedConfig(
        int clientCount,
        double durationSeconds,
        double warmUpSeconds,
        double connectTimeoutSeconds,
        double readyTimeoutSeconds)
    {
        string json = LoadClientHostConfigTests.CreateValidJson(clientCount: clientCount)
            .Replace("\"credentialDirectory\": \"credentials\"",
                $"\"credentialDirectory\": {JsonEscape(_credentialDirectory)}",
                StringComparison.Ordinal)
            .Replace(
                "\"durationSeconds\": 2.0",
                FormattableString.Invariant($"\"durationSeconds\": {durationSeconds.ToString("0.###", CultureInfo.InvariantCulture)}"),
                StringComparison.Ordinal)
            .Replace(
                "\"warmUpSeconds\": 0.5",
                FormattableString.Invariant($"\"warmUpSeconds\": {warmUpSeconds.ToString("0.###", CultureInfo.InvariantCulture)}"),
                StringComparison.Ordinal)
            .Replace(
                "\"connectTimeoutSeconds\": 2.0",
                FormattableString.Invariant($"\"connectTimeoutSeconds\": {connectTimeoutSeconds.ToString("0.###", CultureInfo.InvariantCulture)}"),
                StringComparison.Ordinal)
            .Replace(
                "\"readyTimeoutSeconds\": 2.0",
                FormattableString.Invariant($"\"readyTimeoutSeconds\": {readyTimeoutSeconds.ToString("0.###", CultureInfo.InvariantCulture)}"),
                StringComparison.Ordinal);
        return LoadClientHostConfig.ParseJson(json);
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

    private readonly record struct ScriptedClientSpec(double ReadyAfterSeconds, double StepsPerSecond);

    private sealed class ScriptedSlotFactory : ILoadClientSlotFactory
    {
        private readonly LiteNetLibLoadClientSlotFactory _inner = new();
        private readonly ScriptedClientSpec[] _specs;
        private readonly Stopwatch _wall = new();

        public ScriptedSlotFactory(params ScriptedClientSpec[] specs)
        {
            _specs = specs ?? throw new ArgumentNullException(nameof(specs));
        }

        public LoadClientSlot Create(int clientIndex, LoadClientHostConfig config, string credentialDirectory)
        {
            if ((uint)clientIndex >= (uint)_specs.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(clientIndex));
            }

            if (!_wall.IsRunning)
            {
                _wall.Start();
            }

            LoadClientSlot slot = _inner.Create(clientIndex, config, credentialDirectory);
            ScriptedClientSpec spec = _specs[clientIndex];
            slot.ConnectOverride = static () => true;
            slot.TestDriver = new ScriptedLoadClientDriver(
                _wall,
                spec.ReadyAfterSeconds,
                spec.StepsPerSecond,
                config.MaxStepsPerAdvance);
            return slot;
        }
    }

    private sealed class ScriptedLoadClientDriver : ILoadClientSlotTestDriver
    {
        private readonly Stopwatch _wall;
        private readonly double _readyAfterSeconds;
        private readonly double _stepsPerSecond;
        private readonly int _maxStepsPerAdvance;
        private double _accumulator;
        private bool _readyArmed;

        public ScriptedLoadClientDriver(
            Stopwatch wall,
            double readyAfterSeconds,
            double stepsPerSecond,
            int maxStepsPerAdvance)
        {
            _wall = wall;
            _readyAfterSeconds = readyAfterSeconds;
            _stepsPerSecond = stepsPerSecond;
            _maxStepsPerAdvance = maxStepsPerAdvance;
        }

        public ReplicatedClientConnectionState ConnectionState => ReplicatedClientConnectionState.Connected;
        public bool IsFaulted => false;
        public NetworkRuntimeFault LastFault => default;
        public bool IsWaitingForAuthoritativeAcknowledgement => !_readyArmed;
        public ulong FixedInputAcknowledgementObservationVersion => _readyArmed ? 1UL : 0UL;
        public uint FixedInputAcknowledgedCommittedTick => _readyArmed ? 1u : 0u;

        public void Pump(float deltaSeconds)
        {
            if (!_readyArmed && _wall.Elapsed.TotalSeconds >= _readyAfterSeconds)
            {
                _readyArmed = true;
            }
        }

        public ReplicatedClientFixedInputClockAdvanceResult Advance(float deltaSeconds)
        {
            if (!_readyArmed || deltaSeconds <= 0f || _stepsPerSecond <= 0d)
            {
                return new ReplicatedClientFixedInputClockAdvanceResult(
                    ReplicatedClientFixedInputClockAdvanceStatus.Idle,
                    stepsEmitted: 0,
                    lastTargetTick: 0,
                    enqueueStatus: FixedInputOutboxEnqueueStatus.Enqueued);
            }

            _accumulator += deltaSeconds * _stepsPerSecond;
            int steps = (int)_accumulator;
            if (steps <= 0)
            {
                return new ReplicatedClientFixedInputClockAdvanceResult(
                    ReplicatedClientFixedInputClockAdvanceStatus.Idle,
                    stepsEmitted: 0,
                    lastTargetTick: 0,
                    enqueueStatus: FixedInputOutboxEnqueueStatus.Enqueued);
            }

            if (steps > _maxStepsPerAdvance)
            {
                steps = _maxStepsPerAdvance;
            }

            _accumulator -= steps;
            return new ReplicatedClientFixedInputClockAdvanceResult(
                ReplicatedClientFixedInputClockAdvanceStatus.Stepped,
                stepsEmitted: steps,
                lastTargetTick: 0,
                enqueueStatus: FixedInputOutboxEnqueueStatus.Enqueued);
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
