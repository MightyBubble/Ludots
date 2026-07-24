using System;
using Ludots.Core.Networking.FixedInput;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Session;
using NUnit.Framework;

namespace Ludots.Tests.Networking;

[TestFixture]
public sealed class ReplicatedClientFixedInputClockTests
{
    private const int TickRateHz = 30;
    private const ushort PayloadBytes = 12;

    [Test]
    public void SixtyHzRender_OneSecond_EmitsExactlyThirtyStrictlyIncreasingTicks()
    {
        using var harness = CreateHarness();
        var clock = harness.Clock;

        int emitted = 0;
        for (int frame = 0; frame < 60; frame++)
        {
            ReplicatedClientFixedInputClockAdvanceResult result = clock.Advance(1f / 60f);
            Assert.That(result.IsSuccess, Is.True, $"frame {frame}: {result.Status}");
            emitted += result.StepsEmitted;
        }

        Assert.Multiple(() =>
        {
            Assert.That(emitted, Is.EqualTo(30));
            Assert.That(harness.Client.SubmitCount, Is.EqualTo(30));
            Assert.That(harness.Client.PulseCount, Is.EqualTo(30));
            Assert.That(harness.Client.EmittedTicks, Is.EqualTo(ExpectedTicks(30)));
            Assert.That(clock.NextTargetTick, Is.EqualTo(31u));
        });
    }

    [Test]
    public void OneFortyFourHzRender_OneSecond_EmitsExactlyThirtyFrames()
    {
        using var harness = CreateHarness();
        int emitted = 0;
        for (int frame = 0; frame < 144; frame++)
        {
            ReplicatedClientFixedInputClockAdvanceResult result = harness.Clock.Advance(1f / 144f);
            Assert.That(result.IsSuccess, Is.True);
            emitted += result.StepsEmitted;
        }

        Assert.That(emitted, Is.EqualTo(30));
        Assert.That(harness.Client.EmittedTicks, Is.EqualTo(ExpectedTicks(30)));
    }

    [Test]
    public void IrregularDeltas_TotalingOneSecond_EmitsExactlyThirtyFrames()
    {
        using var harness = CreateHarness();
        // Irregular positive deltas constructed so the exact double sum is 1.0s.
        double[] deltas =
        {
            0.12, 0.01, 0.07, 0.03, 0.005, 0.045, 0.02, 0.08,
            0.015, 0.025, 0.09, 0.004, 0.036, 0.014, 0.06, 0.008,
            0.022, 0.018, 0.05, 0.04, 0.006, 0.034, 0.016, 0.024,
            0.002, 0.028, 0.032, 0.038, 0.042, 0.011,
        };
        // Force last sample to close the second exactly.
        double partial = 0d;
        for (int i = 0; i < deltas.Length - 1; i++)
        {
            partial += deltas[i];
        }

        deltas[^1] = 1d - partial;
        Assert.That(deltas[^1], Is.GreaterThan(0d));

        double sum = partial + deltas[^1];
        Assert.That(sum, Is.EqualTo(1d).Within(1e-12d));

        int emitted = 0;
        for (int i = 0; i < deltas.Length; i++)
        {
            ReplicatedClientFixedInputClockAdvanceResult result = harness.Clock.Advance((float)deltas[i]);
            Assert.That(result.IsSuccess, Is.True, $"delta[{i}]={deltas[i]} status={result.Status}");
            emitted += result.StepsEmitted;
        }

        Assert.That(emitted, Is.EqualTo(30));
        Assert.That(harness.Client.PulseCount, Is.EqualTo(30));
    }

    [Test]
    public void ZeroDelta_AdvancesNothing_AndDoesNotPulse()
    {
        using var harness = CreateHarness();
        ReplicatedClientFixedInputClockAdvanceResult result = harness.Clock.Advance(0f);
        Assert.That(result.Status, Is.EqualTo(ReplicatedClientFixedInputClockAdvanceStatus.Idle));
        Assert.That(result.StepsEmitted, Is.Zero);
        Assert.That(harness.Client.SubmitCount, Is.Zero);
        Assert.That(harness.Client.PulseCount, Is.Zero);
    }

    [Test]
    public void NegativeOrNonFiniteDelta_FailsFast()
    {
        using var harness = CreateHarness();
        Assert.That(() => harness.Clock.Advance(-0.01f), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => harness.Clock.Advance(float.NaN), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => harness.Clock.Advance(float.PositiveInfinity), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void CatchUp_EmitsUpToMaxStepsPerAdvance_AndRetainsRemainingTime()
    {
        using var harness = CreateHarness(maxStepsPerAdvance: 5, maxAccumulatedSteps: 60);
        ReplicatedClientFixedInputClockAdvanceResult first = harness.Clock.Advance(1f);
        Assert.That(first.Status, Is.EqualTo(ReplicatedClientFixedInputClockAdvanceStatus.Stepped));
        Assert.That(first.StepsEmitted, Is.EqualTo(5));
        Assert.That(harness.Clock.AccumulatedSeconds, Is.EqualTo(25d / TickRateHz).Within(1e-9d));
        Assert.That(harness.Client.EmittedTicks, Is.EqualTo(ExpectedTicks(5)));

        // Tiny positive delta must continue catch-up from retained time; never discard backlog.
        ReplicatedClientFixedInputClockAdvanceResult second = harness.Clock.Advance(1e-4f);
        Assert.That(second.Status, Is.EqualTo(ReplicatedClientFixedInputClockAdvanceStatus.Stepped));
        Assert.That(second.StepsEmitted, Is.EqualTo(5));
        Assert.That(harness.Clock.AccumulatedSeconds, Is.EqualTo((20d / TickRateHz) + 1e-4d).Within(1e-9d));
        Assert.That(harness.Client.EmittedTicks, Is.EqualTo(ExpectedTicks(10)));
    }

    [Test]
    public void CatchUpBacklogExceeded_FailsExplicitly_AndDoesNotDiscardTimeOrEmit()
    {
        using var harness = CreateHarness(maxStepsPerAdvance: 4, maxAccumulatedSteps: 8);
        ReplicatedClientFixedInputClockAdvanceResult result = harness.Clock.Advance(1f); // 30 due > 8
        Assert.That(result.Status, Is.EqualTo(ReplicatedClientFixedInputClockAdvanceStatus.CatchUpBacklogExceeded));
        Assert.That(result.StepsEmitted, Is.Zero);
        Assert.That(harness.Client.SubmitCount, Is.Zero);
        Assert.That(harness.Clock.AccumulatedSeconds, Is.EqualTo(1d).Within(1e-6d));
    }

    [Test]
    public void SourceFailure_DoesNotEnqueuePulseOrAdvanceTick_AndRetainsDueTime()
    {
        using var harness = CreateHarness();
        harness.Source.FailNext = true;
        ReplicatedClientFixedInputClockAdvanceResult result = harness.Clock.Advance(1f / TickRateHz);
        Assert.That(result.Status, Is.EqualTo(ReplicatedClientFixedInputClockAdvanceStatus.SourceFailed));
        Assert.That(harness.Client.SubmitCount, Is.Zero);
        Assert.That(harness.Client.PulseCount, Is.Zero);
        Assert.That(harness.Clock.NextTargetTick, Is.EqualTo(1u));
        Assert.That(harness.Clock.AccumulatedSeconds, Is.GreaterThan(0d));
    }

    [Test]
    public void EnqueueCapacityExceeded_FailsExplicitly_AndDoesNotPulseOrAdvanceTick()
    {
        using var harness = CreateHarness();
        harness.Client.EnqueueStatus = FixedInputOutboxEnqueueStatus.CapacityExceeded;
        ReplicatedClientFixedInputClockAdvanceResult result = harness.Clock.Advance(1f / TickRateHz);
        Assert.That(result.Status, Is.EqualTo(ReplicatedClientFixedInputClockAdvanceStatus.EnqueueRejected));
        Assert.That(result.EnqueueStatus, Is.EqualTo(FixedInputOutboxEnqueueStatus.CapacityExceeded));
        Assert.That(harness.Client.PulseCount, Is.Zero);
        Assert.That(harness.Clock.NextTargetTick, Is.EqualTo(1u));
    }

    [Test]
    public void PulseFailure_FailsExplicitly_AfterSuccessfulEnqueue()
    {
        using var harness = CreateHarness();
        harness.Client.PulseResult = false;
        ReplicatedClientFixedInputClockAdvanceResult result = harness.Clock.Advance(1f / TickRateHz);
        Assert.That(result.Status, Is.EqualTo(ReplicatedClientFixedInputClockAdvanceStatus.PulseFailed));
        Assert.That(harness.Client.SubmitCount, Is.EqualTo(1));
        Assert.That(harness.Clock.NextTargetTick, Is.EqualTo(1u));
    }

    [Test]
    public void NotConnected_DoesNotAccumulateOrSend_AndSessionResetRestartsTickSsot()
    {
        using var harness = CreateHarness();
        Assert.That(harness.Clock.Advance(1f / TickRateHz).StepsEmitted, Is.EqualTo(1));
        Assert.That(harness.Clock.NextTargetTick, Is.EqualTo(2u));

        harness.Client.State = ReplicatedClientConnectionState.Disconnected;
        ReplicatedClientFixedInputClockAdvanceResult paused = harness.Clock.Advance(1f);
        Assert.That(paused.Status, Is.EqualTo(ReplicatedClientFixedInputClockAdvanceStatus.NotConnected));
        Assert.That(paused.StepsEmitted, Is.Zero);
        Assert.That(harness.Clock.AccumulatedSeconds, Is.EqualTo(0d));
        Assert.That(harness.Clock.IsArmed, Is.False);

        harness.Client.State = ReplicatedClientConnectionState.Connected;
        harness.Client.SessionEpoch = new SessionEpoch(2);
        ReplicatedClientFixedInputClockAdvanceResult resumed = harness.Clock.Advance(1f / TickRateHz);
        Assert.That(resumed.Status, Is.EqualTo(ReplicatedClientFixedInputClockAdvanceStatus.Stepped));
        Assert.That(resumed.LastTargetTick, Is.EqualTo(1u));
        Assert.That(harness.Clock.ArmedSessionEpoch, Is.EqualTo(new SessionEpoch(2)));
    }

    [Test]
    public void ExplicitResetForSession_RestartsTargetTickSelection()
    {
        using var harness = CreateHarness();
        Assert.That(harness.Clock.Advance(1f / TickRateHz).LastTargetTick, Is.EqualTo(1u));
        harness.Clock.ResetForSession(new SessionEpoch(9));
        Assert.That(harness.Clock.NextTargetTick, Is.EqualTo(1u));
        Assert.That(harness.Clock.LastEmittedTargetTick, Is.EqualTo(0u));
        Assert.That(harness.Clock.Advance(1f / TickRateHz).LastTargetTick, Is.EqualTo(1u));
        Assert.That(harness.Client.EmittedTicks[^1], Is.EqualTo(1u));
    }

    [Test]
    public void SameSessionRemainsArmed_ContinuesStrictlyIncreasingTicks()
    {
        using var harness = CreateHarness();
        Assert.That(harness.Clock.Advance(1f / TickRateHz).LastTargetTick, Is.EqualTo(1u));
        Assert.That(harness.Clock.Advance(1f / TickRateHz).LastTargetTick, Is.EqualTo(2u));
        Assert.That(harness.Clock.ArmedSessionEpoch.Value, Is.EqualTo(1ul));
        Assert.That(harness.Client.EmittedTicks, Is.EqualTo(new uint[] { 1, 2 }));
    }

    [Test]
    public void PumpWithoutDueFixedStep_SendsNoInput_OnlyClockPulseSends()
    {
        using var harness = CreateHarness();
        // Half a tick: not due.
        Assert.That(harness.Clock.Advance(1f / TickRateHz / 2f).StepsEmitted, Is.Zero);
        Assert.That(harness.Client.PulseCount, Is.Zero);
        Assert.That(harness.Clock.Advance(1f / TickRateHz / 2f).StepsEmitted, Is.EqualTo(1));
        Assert.That(harness.Client.PulseCount, Is.EqualTo(1));
    }

    [Test]
    public void SteadyState_Advance_AllocatesZeroManagedBytesAfterWarmup()
    {
        using var harness = CreateHarness();
        for (int i = 0; i < 64; i++)
        {
            Assert.That(harness.Clock.Advance(1f / TickRateHz).IsSuccess, Is.True);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        GC.GetAllocatedBytesForCurrentThread();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            ReplicatedClientFixedInputClockAdvanceResult result = harness.Clock.Advance(1f / TickRateHz);
            if (!result.IsSuccess)
            {
                Assert.Fail($"Advance failed at i={i}: {result.Status}");
            }
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.EqualTo(0), $"Expected 0 B after warmup; observed {allocated} B.");
    }

    private static uint[] ExpectedTicks(int count)
    {
        var ticks = new uint[count];
        for (uint i = 0; i < count; i++)
        {
            ticks[i] = i + 1;
        }

        return ticks;
    }

    private static ClockHarness CreateHarness(int maxStepsPerAdvance = 8, int maxAccumulatedSteps = 64)
    {
        var client = new FakeFixedInputClient();
        var source = new FillPayloadSource(PayloadBytes);
        var clock = new ReplicatedClientFixedInputClock(
            client,
            source,
            TickRateHz,
            PayloadBytes,
            maxStepsPerAdvance,
            maxAccumulatedSteps);
        return new ClockHarness(client, source, clock);
    }

    private sealed class ClockHarness : IDisposable
    {
        public ClockHarness(FakeFixedInputClient client, FillPayloadSource source, ReplicatedClientFixedInputClock clock)
        {
            Client = client;
            Source = source;
            Clock = clock;
        }

        public FakeFixedInputClient Client { get; }
        public FillPayloadSource Source { get; }
        public ReplicatedClientFixedInputClock Clock { get; }

        public void Dispose()
        {
        }
    }

    private sealed class FillPayloadSource : IFixedInputPayloadSource
    {
        private readonly int _payloadBytes;
        private byte _sequence;

        public FillPayloadSource(int payloadBytes)
        {
            _payloadBytes = payloadBytes;
        }

        public bool FailNext { get; set; }

        public FixedInputPayloadSampleStatus TrySample(Span<byte> destination)
        {
            if (FailNext)
            {
                FailNext = false;
                return FixedInputPayloadSampleStatus.Failed;
            }

            if (destination.Length != _payloadBytes)
            {
                return FixedInputPayloadSampleStatus.Failed;
            }

            destination.Fill(_sequence++);
            return FixedInputPayloadSampleStatus.Sampled;
        }
    }

    private sealed class FakeFixedInputClient : IReplicatedClientFixedInputPort
    {
        private readonly uint[] _emitted = new uint[16_384];
        private int _emittedCount;

        public ReplicatedClientConnectionState State { get; set; } = ReplicatedClientConnectionState.Connected;
        public SessionEpoch SessionEpoch { get; set; } = new(1);
        public FixedInputOutboxEnqueueStatus EnqueueStatus { get; set; } = FixedInputOutboxEnqueueStatus.Enqueued;
        public bool PulseResult { get; set; } = true;
        public int SubmitCount { get; private set; }
        public int PulseCount { get; private set; }
        public IReadOnlyList<uint> EmittedTicks => _emitted.AsSpan(0, _emittedCount).ToArray();

        public FixedInputOutboxEnqueueStatus TrySubmitFixedInput(uint targetTick, ReadOnlySpan<byte> payload)
        {
            if (State != ReplicatedClientConnectionState.Connected)
            {
                return FixedInputOutboxEnqueueStatus.InvalidInput;
            }

            if (EnqueueStatus != FixedInputOutboxEnqueueStatus.Enqueued)
            {
                return EnqueueStatus;
            }

            if (_emittedCount >= _emitted.Length)
            {
                throw new InvalidOperationException("Fake fixed-input client tick capacity exceeded.");
            }

            SubmitCount++;
            _emitted[_emittedCount++] = targetTick;
            return FixedInputOutboxEnqueueStatus.Enqueued;
        }

        public bool TryPulseFixedInputSend()
        {
            if (!PulseResult || State != ReplicatedClientConnectionState.Connected)
            {
                return false;
            }

            PulseCount++;
            return true;
        }
    }
}
