using System;
using System.Collections.Generic;
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
    private const int DefaultLeadTicks = 1;
    private const int DefaultMaxFutureTicks = 8;

    [Test]
    public void SixtyHzRender_OneSecond_EmitsExactlyThirtyStrictlyIncreasingTicks()
    {
        using var harness = CreateHarness(maxFutureTicks: 64);
        ObserveConnectedThenApplyAck(harness, committedThroughTick: 0);
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
            Assert.That(harness.Client.EmittedTicks, Is.EqualTo(ExpectedTicks(start: 1, count: 30)));
            Assert.That(clock.PeekNextTargetTick(), Is.EqualTo(31u));
        });
    }

    [Test]
    public void OneFortyFourHzRender_OneSecond_EmitsExactlyThirtyFrames()
    {
        using var harness = CreateHarness(maxFutureTicks: 64);
        ObserveConnectedThenApplyAck(harness, committedThroughTick: 0);
        int emitted = 0;
        for (int frame = 0; frame < 144; frame++)
        {
            ReplicatedClientFixedInputClockAdvanceResult result = harness.Clock.Advance(1f / 144f);
            Assert.That(result.IsSuccess, Is.True);
            emitted += result.StepsEmitted;
        }

        Assert.That(emitted, Is.EqualTo(30));
        Assert.That(harness.Client.EmittedTicks, Is.EqualTo(ExpectedTicks(start: 1, count: 30)));
    }

    [Test]
    public void IrregularDeltas_TotalingOneSecond_EmitsExactlyThirtyFrames()
    {
        using var harness = CreateHarness(maxFutureTicks: 64);
        ObserveConnectedThenApplyAck(harness, committedThroughTick: 0);
        double[] deltas =
        {
            0.12, 0.01, 0.07, 0.03, 0.005, 0.045, 0.02, 0.08,
            0.015, 0.025, 0.09, 0.004, 0.036, 0.014, 0.06, 0.008,
            0.022, 0.018, 0.05, 0.04, 0.006, 0.034, 0.016, 0.024,
            0.002, 0.028, 0.032, 0.038, 0.042, 0.011,
        };
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
        ObserveConnectedThenApplyAck(harness, committedThroughTick: 0);
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
        ObserveConnectedThenApplyAck(harness, committedThroughTick: 0);
        Assert.That(() => harness.Clock.Advance(-0.01f), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => harness.Clock.Advance(float.NaN), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => harness.Clock.Advance(float.PositiveInfinity), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Constructor_RejectsLeadBeyondServerFutureWindow()
    {
        var client = new FakeFixedInputClient();
        var source = new FillPayloadSource(PayloadBytes);

        Assert.That(
            () => new ReplicatedClientFixedInputClock(
                client,
                source,
                TickRateHz,
                PayloadBytes,
                fixedInputLeadTicks: 9,
                fixedInputMaxFutureTicks: DefaultMaxFutureTicks,
                maxStepsPerAdvance: 8,
                maxAccumulatedSteps: 64),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void CatchUp_EmitsUpToMaxStepsPerAdvance_AndRetainsRemainingTime()
    {
        using var harness = CreateHarness(
            maxStepsPerAdvance: 5,
            maxAccumulatedSteps: 60,
            maxFutureTicks: 16);
        ObserveConnectedThenApplyAck(harness, committedThroughTick: 0);
        ReplicatedClientFixedInputClockAdvanceResult first = harness.Clock.Advance(1f);
        Assert.That(first.Status, Is.EqualTo(ReplicatedClientFixedInputClockAdvanceStatus.Stepped));
        Assert.That(first.StepsEmitted, Is.EqualTo(5));
        Assert.That(harness.Clock.AccumulatedSeconds, Is.EqualTo(25d / TickRateHz).Within(1e-9d));
        Assert.That(harness.Client.EmittedTicks, Is.EqualTo(ExpectedTicks(start: 1, count: 5)));

        ReplicatedClientFixedInputClockAdvanceResult second = harness.Clock.Advance(1e-4f);
        Assert.That(second.Status, Is.EqualTo(ReplicatedClientFixedInputClockAdvanceStatus.Stepped));
        Assert.That(second.StepsEmitted, Is.EqualTo(5));
        Assert.That(harness.Clock.AccumulatedSeconds, Is.EqualTo((20d / TickRateHz) + 1e-4d).Within(1e-9d));
        Assert.That(harness.Client.EmittedTicks, Is.EqualTo(ExpectedTicks(start: 1, count: 10)));
    }

    [Test]
    public void CatchUpBacklogExceeded_FailsExplicitly_AndDoesNotDiscardTimeOrEmit()
    {
        using var harness = CreateHarness(maxStepsPerAdvance: 4, maxAccumulatedSteps: 8);
        ObserveConnectedThenApplyAck(harness, committedThroughTick: 0);
        ReplicatedClientFixedInputClockAdvanceResult result = harness.Clock.Advance(1f);
        Assert.That(result.Status, Is.EqualTo(ReplicatedClientFixedInputClockAdvanceStatus.CatchUpBacklogExceeded));
        Assert.That(result.StepsEmitted, Is.Zero);
        Assert.That(harness.Client.SubmitCount, Is.Zero);
        Assert.That(harness.Clock.AccumulatedSeconds, Is.EqualTo(1d).Within(1e-6d));
    }

    [Test]
    public void SourceFailure_DoesNotEnqueuePulseOrAdvanceTick_AndRetainsDueTime()
    {
        using var harness = CreateHarness();
        ObserveConnectedThenApplyAck(harness, committedThroughTick: 0);
        harness.Source.FailNext = true;
        ReplicatedClientFixedInputClockAdvanceResult result = harness.Clock.Advance(1f / TickRateHz);
        Assert.That(result.Status, Is.EqualTo(ReplicatedClientFixedInputClockAdvanceStatus.SourceFailed));
        Assert.That(harness.Client.SubmitCount, Is.Zero);
        Assert.That(harness.Client.PulseCount, Is.Zero);
        Assert.That(harness.Source.CommitCount, Is.Zero);
        Assert.That(harness.Clock.PeekNextTargetTick(), Is.EqualTo(1u));
        Assert.That(harness.Clock.AccumulatedSeconds, Is.GreaterThan(0d));
    }

    [Test]
    public void SourceReceivesExactSelectedTargetTick()
    {
        using var harness = CreateHarness(leadTicks: 2);
        ObserveConnectedThenApplyAck(harness, committedThroughTick: 500);
        ReplicatedClientFixedInputClockAdvanceResult result = harness.Clock.Advance(1f / TickRateHz);
        Assert.That(result.Status, Is.EqualTo(ReplicatedClientFixedInputClockAdvanceStatus.Stepped));
        Assert.That(result.LastTargetTick, Is.EqualTo(502u));
        Assert.That(harness.Source.LastSampledTargetTick, Is.EqualTo(502u));
        Assert.That(harness.Source.SampledTicks, Is.EqualTo(new uint[] { 502 }));
    }

    [Test]
    public void EnqueueCapacityExceeded_FailsExplicitly_AndDoesNotPulseOrAdvanceTick()
    {
        using var harness = CreateHarness();
        ObserveConnectedThenApplyAck(harness, committedThroughTick: 0);
        harness.Client.EnqueueStatus = FixedInputOutboxEnqueueStatus.CapacityExceeded;
        ReplicatedClientFixedInputClockAdvanceResult result = harness.Clock.Advance(1f / TickRateHz);
        Assert.That(result.Status, Is.EqualTo(ReplicatedClientFixedInputClockAdvanceStatus.EnqueueRejected));
        Assert.That(result.EnqueueStatus, Is.EqualTo(FixedInputOutboxEnqueueStatus.CapacityExceeded));
        Assert.That(harness.Client.PulseCount, Is.Zero);
        Assert.That(harness.Source.CommitCount, Is.Zero);
        Assert.That(harness.Client.HasEnqueuedFixedInputTargetTick, Is.False);
        Assert.That(harness.Clock.PeekNextTargetTick(), Is.EqualTo(1u));
    }

    [Test]
    public void PredictionCommit_RunsOnlyAfterSuccessfulEnqueueAndPulse()
    {
        using var harness = CreateHarness();
        ObserveConnectedThenApplyAck(harness, committedThroughTick: 0);
        harness.Client.PulseResult = false;

        Assert.That(
            () => harness.Clock.Advance(1f / TickRateHz),
            Throws.TypeOf<NetworkRuntimeException>());
        Assert.That(harness.Client.SubmitCount, Is.EqualTo(1));
        Assert.That(harness.Source.CommitCount, Is.Zero);
        Assert.That(harness.Clock.IsTerminalPulseFaulted, Is.True);
    }

    [Test]
    public void PredictionCommitFailure_IsTerminal_AndDoesNotInventPrediction()
    {
        using var harness = CreateHarness();
        ObserveConnectedThenApplyAck(harness, committedThroughTick: 0);
        harness.Source.FailNextCommit = true;

        NetworkRuntimeException exception = Assert.Throws<NetworkRuntimeException>(
            () => harness.Clock.Advance(1f / TickRateHz))!;
        Assert.That(exception.Fault.Detail, Is.EqualTo((int)ReplicatedClientFixedInputClockAdvanceStatus.CommitFailed));
        Assert.That(harness.Client.SubmitCount, Is.EqualTo(1));
        Assert.That(harness.Client.PulseCount, Is.EqualTo(1));
        Assert.That(harness.Source.CommitCount, Is.Zero);
        Assert.That(harness.Clock.IsTerminalCommitFaulted, Is.True);
        Assert.That(
            () => harness.Clock.Advance(1f / TickRateHz),
            Throws.TypeOf<NetworkRuntimeException>());
    }

    [Test]
    public void MultiStepCatchUp_CommitsExactTickOrderWithExactSentPayloadBytes()
    {
        using var harness = CreateHarness(maxStepsPerAdvance: 3, maxAccumulatedSteps: 60);
        ObserveConnectedThenApplyAck(harness, committedThroughTick: 0);
        ReplicatedClientFixedInputClockAdvanceResult result = harness.Clock.Advance(3f / TickRateHz);
        Assert.That(result.Status, Is.EqualTo(ReplicatedClientFixedInputClockAdvanceStatus.Stepped));
        Assert.That(result.StepsEmitted, Is.EqualTo(3));
        Assert.That(harness.Client.EmittedTicks, Is.EqualTo(new uint[] { 1, 2, 3 }));
        Assert.That(harness.Source.CommittedTicks, Is.EqualTo(new uint[] { 1, 2, 3 }));
        Assert.That(harness.Source.CommittedPayloadHeads, Is.EqualTo(new byte[] { 0, 1, 2 }));
        Assert.That(harness.Source.CommitCount, Is.EqualTo(harness.Client.PulseCount));
    }

    [Test]
    public void PulseFailure_IsTerminalLocalContractFault_AndSubsequentCallCannotDuplicateTick()
    {
        using var harness = CreateHarness();
        ObserveConnectedThenApplyAck(harness, committedThroughTick: 0);
        harness.Client.PulseResult = false;

        Assert.That(
            () => harness.Clock.Advance(1f / TickRateHz),
            Throws.TypeOf<NetworkRuntimeException>());
        Assert.That(harness.Client.SubmitCount, Is.EqualTo(1));
        Assert.That(harness.Client.LastEnqueuedFixedInputTargetTick, Is.EqualTo(1u));
        Assert.That(harness.Clock.IsTerminalPulseFaulted, Is.True);

        int submitsAfterFault = harness.Client.SubmitCount;
        Assert.That(
            () => harness.Clock.Advance(1f / TickRateHz),
            Throws.TypeOf<NetworkRuntimeException>());
        Assert.That(harness.Client.SubmitCount, Is.EqualTo(submitsAfterFault));
        Assert.That(harness.Client.EmittedTicks, Is.EqualTo(new uint[] { 1 }));
    }

    [Test]
    public void AcceptedPulseWithoutCurrentTarget_IsTerminalAndDoesNotCommitPrediction()
    {
        using var harness = CreateHarness();
        ObserveConnectedThenApplyAck(harness, committedThroughTick: 0);
        harness.Client.SeedEnqueuedTick(1);
        harness.Client.AcceptedHighestTargetTickOverride = 1;

        Assert.That(
            () => harness.Clock.Advance(1f / TickRateHz),
            Throws.TypeOf<NetworkRuntimeException>());

        Assert.Multiple(() =>
        {
            Assert.That(harness.Clock.IsTerminalPulseFaulted, Is.True);
            Assert.That(harness.Client.LastEnqueuedFixedInputTargetTick, Is.EqualTo(2u));
            Assert.That(harness.Client.PulseCount, Is.EqualTo(1));
            Assert.That(harness.Source.CommitCount, Is.Zero);
        });
    }

    [Test]
    public void LateJoin_CommittedTick500_Lead2_Emits502Not1()
    {
        using var harness = CreateHarness(leadTicks: 2);
        Assert.That(
            harness.Clock.Advance(1f / TickRateHz).Status,
            Is.EqualTo(ReplicatedClientFixedInputClockAdvanceStatus.WaitingForAuthoritativeAcknowledgement));
        Assert.That(harness.Client.SubmitCount, Is.Zero);

        harness.Client.ApplyAuthoritativeAck(committedThroughTick: 500);
        ReplicatedClientFixedInputClockAdvanceResult result = harness.Clock.Advance(1f / TickRateHz);
        Assert.That(result.Status, Is.EqualTo(ReplicatedClientFixedInputClockAdvanceStatus.Stepped));
        Assert.That(result.LastTargetTick, Is.EqualTo(502u));
        Assert.That(harness.Client.EmittedTicks, Is.EqualTo(new uint[] { 502 }));
        Assert.That(harness.Source.LastSampledTargetTick, Is.EqualTo(502u));
    }

    [Test]
    public void NoInputBeforeFirstPostConnectAck()
    {
        using var harness = CreateHarness();
        for (int i = 0; i < 5; i++)
        {
            ReplicatedClientFixedInputClockAdvanceResult waiting = harness.Clock.Advance(1f / TickRateHz);
            Assert.That(
                waiting.Status,
                Is.EqualTo(ReplicatedClientFixedInputClockAdvanceStatus.WaitingForAuthoritativeAcknowledgement));
            Assert.That(harness.Clock.AccumulatedSeconds, Is.EqualTo(0d));
        }

        Assert.That(harness.Client.SubmitCount, Is.Zero);
        Assert.That(harness.Client.PulseCount, Is.Zero);
        Assert.That(harness.Source.SampleCount, Is.Zero);
    }

    [Test]
    public void SameEpochReconnect_PreservesOutboxTick_WaitsForFreshAck_ThenContinuesStrictlyAboveBoth()
    {
        using var harness = CreateHarness(leadTicks: 2);
        ObserveConnectedThenApplyAck(harness, committedThroughTick: 10);
        Assert.That(harness.Clock.Advance(1f / TickRateHz).LastTargetTick, Is.EqualTo(12u));
        Assert.That(harness.Clock.Advance(1f / TickRateHz).LastTargetTick, Is.EqualTo(13u));
        Assert.That(harness.Client.LastEnqueuedFixedInputTargetTick, Is.EqualTo(13u));

        harness.Client.State = ReplicatedClientConnectionState.Disconnected;
        Assert.That(
            harness.Clock.Advance(1f).Status,
            Is.EqualTo(ReplicatedClientFixedInputClockAdvanceStatus.NotConnected));

        harness.Client.State = ReplicatedClientConnectionState.Connected;
        harness.Client.SessionEpoch = new SessionEpoch(1);
        ReplicatedClientFixedInputClockAdvanceResult waiting = harness.Clock.Advance(1f / TickRateHz);
        Assert.That(
            waiting.Status,
            Is.EqualTo(ReplicatedClientFixedInputClockAdvanceStatus.WaitingForAuthoritativeAcknowledgement));
        Assert.That(harness.Client.SubmitCount, Is.EqualTo(2));
        Assert.That(harness.Client.LastEnqueuedFixedInputTargetTick, Is.EqualTo(13u));

        // Fresh ACK after Connected edge; committed stays behind outbox so next tick is outbox+1.
        harness.Client.ApplyAuthoritativeAck(committedThroughTick: 11);
        ReplicatedClientFixedInputClockAdvanceResult resumed = harness.Clock.Advance(1f / TickRateHz);
        Assert.That(resumed.Status, Is.EqualTo(ReplicatedClientFixedInputClockAdvanceStatus.Stepped));
        Assert.That(resumed.LastTargetTick, Is.EqualTo(14u));
        Assert.That(resumed.LastTargetTick, Is.GreaterThan(13u));
        Assert.That(resumed.LastTargetTick, Is.GreaterThan(11u + 2u));
        Assert.That(harness.Client.EmittedTicks, Is.EqualTo(new uint[] { 12, 13, 14 }));
    }

    [Test]
    public void NewEpoch_StartsFromThatEpochFreshAck_DoesNotInheritOldGenerationTarget()
    {
        using var harness = CreateHarness(leadTicks: 2);
        ObserveConnectedThenApplyAck(harness, committedThroughTick: 100);
        Assert.That(harness.Clock.Advance(1f / TickRateHz).LastTargetTick, Is.EqualTo(102u));

        harness.Client.State = ReplicatedClientConnectionState.Disconnected;
        Assert.That(harness.Clock.Advance(0f).Status, Is.EqualTo(ReplicatedClientFixedInputClockAdvanceStatus.NotConnected));

        harness.Client.ResetOutboxGeneration();
        harness.Client.State = ReplicatedClientConnectionState.Connected;
        harness.Client.SessionEpoch = new SessionEpoch(2);

        Assert.That(
            harness.Clock.Advance(1f / TickRateHz).Status,
            Is.EqualTo(ReplicatedClientFixedInputClockAdvanceStatus.WaitingForAuthoritativeAcknowledgement));
        Assert.That(harness.Client.HasEnqueuedFixedInputTargetTick, Is.False);

        harness.Client.ApplyAuthoritativeAck(committedThroughTick: 3);
        ReplicatedClientFixedInputClockAdvanceResult resumed = harness.Clock.Advance(1f / TickRateHz);
        Assert.That(resumed.Status, Is.EqualTo(ReplicatedClientFixedInputClockAdvanceStatus.Stepped));
        Assert.That(resumed.LastTargetTick, Is.EqualTo(5u));
        Assert.That(harness.Clock.ArmedSessionEpoch, Is.EqualTo(new SessionEpoch(2)));
        Assert.That(harness.Client.EmittedTicks[^1], Is.EqualTo(5u));
    }

    [Test]
    public void AckCommittedAdvance_JumpsTargetForward_WithoutBackfillingLateTicks()
    {
        using var harness = CreateHarness(leadTicks: 2);
        ObserveConnectedThenApplyAck(harness, committedThroughTick: 10);
        Assert.That(harness.Clock.Advance(1f / TickRateHz).LastTargetTick, Is.EqualTo(12u));
        Assert.That(harness.Clock.Advance(1f / TickRateHz).LastTargetTick, Is.EqualTo(13u));

        // Mid-session ACK advance is not a Connected edge; just apply newer ACK truth.
        harness.Client.ApplyAuthoritativeAck(committedThroughTick: 100);
        ReplicatedClientFixedInputClockAdvanceResult jumped = harness.Clock.Advance(1f / TickRateHz);
        Assert.That(jumped.Status, Is.EqualTo(ReplicatedClientFixedInputClockAdvanceStatus.Stepped));
        Assert.That(jumped.LastTargetTick, Is.EqualTo(102u));
        Assert.That(harness.Client.EmittedTicks, Is.EqualTo(new uint[] { 12, 13, 102 }));
        Assert.That(harness.Source.SampledTicks, Is.EqualTo(new uint[] { 12, 13, 102 }));
    }

    [Test]
    public void FutureWindowBoundary_AllowsBoundaryThenWaitsWithoutConsumingDueTime_AndResumesAfterAck()
    {
        using var harness = CreateHarness(leadTicks: 2, maxFutureTicks: 4);
        ObserveConnectedThenApplyAck(harness, committedThroughTick: 10);

        Assert.That(harness.Clock.Advance(1f / TickRateHz).LastTargetTick, Is.EqualTo(12u));
        Assert.That(harness.Clock.Advance(1f / TickRateHz).LastTargetTick, Is.EqualTo(13u));
        Assert.That(harness.Clock.Advance(1f / TickRateHz).LastTargetTick, Is.EqualTo(14u));

        int samplesBeforeWait = harness.Source.SampleCount;
        int submissionsBeforeWait = harness.Client.SubmitCount;
        ReplicatedClientFixedInputClockAdvanceResult waiting = harness.Clock.Advance(1f / TickRateHz);

        Assert.Multiple(() =>
        {
            Assert.That(
                waiting.Status,
                Is.EqualTo(ReplicatedClientFixedInputClockAdvanceStatus.WaitingForAuthoritativeAcknowledgement));
            Assert.That(waiting.StepsEmitted, Is.Zero);
            Assert.That(waiting.LastTargetTick, Is.EqualTo(14u));
            Assert.That(harness.Source.SampleCount, Is.EqualTo(samplesBeforeWait));
            Assert.That(harness.Client.SubmitCount, Is.EqualTo(submissionsBeforeWait));
            Assert.That(harness.Clock.AccumulatedSeconds, Is.EqualTo(1d / TickRateHz).Within(1e-6d));
        });

        harness.Client.ApplyAuthoritativeAck(committedThroughTick: 12);
        ReplicatedClientFixedInputClockAdvanceResult resumed = harness.Clock.Advance(1f / TickRateHz);

        Assert.Multiple(() =>
        {
            Assert.That(resumed.Status, Is.EqualTo(ReplicatedClientFixedInputClockAdvanceStatus.Stepped));
            Assert.That(resumed.StepsEmitted, Is.EqualTo(2));
            Assert.That(resumed.LastTargetTick, Is.EqualTo(16u));
            Assert.That(harness.Client.EmittedTicks, Is.EqualTo(new uint[] { 12, 13, 14, 15, 16 }));
            Assert.That(harness.Clock.AccumulatedSeconds, Is.Zero.Within(1e-6d));
        });
    }

    [Test]
    public void TargetDomainOverflow_FailsExplicitly()
    {
        using var harness = CreateHarness(leadTicks: 2);
        ObserveConnectedThenApplyAck(harness, committedThroughTick: FixedInputWireCodec.MaxSimulationTick);
        Assert.That(
            () => harness.Clock.Advance(1f / TickRateHz),
            Throws.TypeOf<NetworkRuntimeException>());
        Assert.That(harness.Client.SubmitCount, Is.Zero);

        using var harnessOutbox = CreateHarness(leadTicks: 1);
        ObserveConnectedThenApplyAck(harnessOutbox, committedThroughTick: 0);
        harnessOutbox.Client.SeedEnqueuedTick(FixedInputWireCodec.MaxSimulationTick);
        Assert.That(
            () => harnessOutbox.Clock.Advance(1f / TickRateHz),
            Throws.TypeOf<NetworkRuntimeException>());
        Assert.That(harnessOutbox.Client.SubmitCount, Is.Zero);
    }

    [Test]
    public void NotConnected_DoesNotAccumulateOrSend_AndReconnectRequiresFreshAck()
    {
        using var harness = CreateHarness();
        ObserveConnectedThenApplyAck(harness, committedThroughTick: 0);
        Assert.That(harness.Clock.Advance(1f / TickRateHz).StepsEmitted, Is.EqualTo(1));
        Assert.That(harness.Clock.PeekNextTargetTick(), Is.EqualTo(2u));

        harness.Client.State = ReplicatedClientConnectionState.Disconnected;
        ReplicatedClientFixedInputClockAdvanceResult paused = harness.Clock.Advance(1f);
        Assert.That(paused.Status, Is.EqualTo(ReplicatedClientFixedInputClockAdvanceStatus.NotConnected));
        Assert.That(paused.StepsEmitted, Is.Zero);
        Assert.That(harness.Clock.AccumulatedSeconds, Is.EqualTo(0d));
        Assert.That(harness.Clock.IsArmed, Is.False);

        harness.Client.ResetOutboxGeneration();
        harness.Client.State = ReplicatedClientConnectionState.Connected;
        harness.Client.SessionEpoch = new SessionEpoch(2);
        ReplicatedClientFixedInputClockAdvanceResult waiting = harness.Clock.Advance(1f / TickRateHz);
        Assert.That(
            waiting.Status,
            Is.EqualTo(ReplicatedClientFixedInputClockAdvanceStatus.WaitingForAuthoritativeAcknowledgement));

        harness.Client.ApplyAuthoritativeAck(committedThroughTick: 0);
        ReplicatedClientFixedInputClockAdvanceResult resumed = harness.Clock.Advance(1f / TickRateHz);
        Assert.That(resumed.Status, Is.EqualTo(ReplicatedClientFixedInputClockAdvanceStatus.Stepped));
        Assert.That(resumed.LastTargetTick, Is.EqualTo(1u));
        Assert.That(harness.Clock.ArmedSessionEpoch, Is.EqualTo(new SessionEpoch(2)));
    }

    [Test]
    public void ExplicitResetForSession_RequiresFreshAckBeforeSampling()
    {
        using var harness = CreateHarness();
        ObserveConnectedThenApplyAck(harness, committedThroughTick: 0);
        Assert.That(harness.Clock.Advance(1f / TickRateHz).LastTargetTick, Is.EqualTo(1u));

        harness.Client.SessionEpoch = new SessionEpoch(9);
        harness.Clock.ResetForSession(new SessionEpoch(9));
        Assert.That(harness.Clock.IsWaitingForAuthoritativeAcknowledgement, Is.True);
        Assert.That(
            harness.Clock.Advance(1f / TickRateHz).Status,
            Is.EqualTo(ReplicatedClientFixedInputClockAdvanceStatus.WaitingForAuthoritativeAcknowledgement));

        harness.Client.ApplyAuthoritativeAck(committedThroughTick: 4);
        Assert.That(harness.Clock.Advance(1f / TickRateHz).LastTargetTick, Is.EqualTo(5u));
    }

    [Test]
    public void SameSessionRemainsArmed_ContinuesStrictlyIncreasingTicks()
    {
        using var harness = CreateHarness();
        ObserveConnectedThenApplyAck(harness, committedThroughTick: 0);
        Assert.That(harness.Clock.Advance(1f / TickRateHz).LastTargetTick, Is.EqualTo(1u));
        Assert.That(harness.Clock.Advance(1f / TickRateHz).LastTargetTick, Is.EqualTo(2u));
        Assert.That(harness.Clock.ArmedSessionEpoch.Value, Is.EqualTo(1ul));
        Assert.That(harness.Client.EmittedTicks, Is.EqualTo(new uint[] { 1, 2 }));
    }

    [Test]
    public void PumpWithoutDueFixedStep_SendsNoInput_OnlyClockPulseSends()
    {
        using var harness = CreateHarness();
        ObserveConnectedThenApplyAck(harness, committedThroughTick: 0);
        Assert.That(harness.Clock.Advance(1f / TickRateHz / 2f).StepsEmitted, Is.Zero);
        Assert.That(harness.Client.PulseCount, Is.Zero);
        Assert.That(harness.Clock.Advance(1f / TickRateHz / 2f).StepsEmitted, Is.EqualTo(1));
        Assert.That(harness.Client.PulseCount, Is.EqualTo(1));
    }

    [Test]
    public void SteadyState_Advance_AllocatesZeroManagedBytesAfterWarmup()
    {
        using var harness = CreateHarness();
        ObserveConnectedThenApplyAck(harness, committedThroughTick: 0);
        for (int i = 0; i < 64; i++)
        {
            Assert.That(harness.Clock.Advance(1f / TickRateHz).IsSuccess, Is.True);
            harness.Client.ApplyAuthoritativeAck(harness.Client.LastEnqueuedFixedInputTargetTick);
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

            harness.Client.ApplyAuthoritativeAck(harness.Client.LastEnqueuedFixedInputTargetTick);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.EqualTo(0), $"Expected 0 B after warmup; observed {allocated} B.");
    }

    [Test]
    public void TryComputeNextTargetTick_MatchesSsotFormulaAndRejectsOverflow()
    {
        Assert.That(
            FixedInputWireCodec.TryComputeNextTargetTick(
                lastEnqueuedTargetTick: 0,
                hasEnqueued: false,
                acknowledgedCommittedThroughTick: 500,
                leadTicks: 2,
                out uint lateJoin),
            Is.True);
        Assert.That(lateJoin, Is.EqualTo(502u));

        Assert.That(
            FixedInputWireCodec.TryComputeNextTargetTick(
                lastEnqueuedTargetTick: 510,
                hasEnqueued: true,
                acknowledgedCommittedThroughTick: 500,
                leadTicks: 2,
                out uint fromOutbox),
            Is.True);
        Assert.That(fromOutbox, Is.EqualTo(511u));

        Assert.That(
            FixedInputWireCodec.TryComputeNextTargetTick(
                lastEnqueuedTargetTick: 0,
                hasEnqueued: false,
                acknowledgedCommittedThroughTick: FixedInputWireCodec.MaxSimulationTick,
                leadTicks: 1,
                out _),
            Is.False);
    }

    private static uint[] ExpectedTicks(uint start, int count)
    {
        var ticks = new uint[count];
        for (int i = 0; i < count; i++)
        {
            ticks[i] = start + (uint)i;
        }

        return ticks;
    }

    /// <summary>
    /// Observe the Connected edge first, then apply a NEW authoritative ACK after that edge.
    /// Matches production ordering: Connected ? post-connect ACK ? sample/enqueue.
    /// </summary>
    private static void ObserveConnectedThenApplyAck(ClockHarness harness, uint committedThroughTick)
    {
        ReplicatedClientFixedInputClockAdvanceResult waiting = harness.Clock.Advance(0f);
        Assert.That(
            waiting.Status,
            Is.EqualTo(ReplicatedClientFixedInputClockAdvanceStatus.WaitingForAuthoritativeAcknowledgement));
        harness.Client.ApplyAuthoritativeAck(committedThroughTick);
    }

    private static ClockHarness CreateHarness(
        int maxStepsPerAdvance = 8,
        int maxAccumulatedSteps = 64,
        int leadTicks = DefaultLeadTicks,
        int maxFutureTicks = DefaultMaxFutureTicks)
    {
        var client = new FakeFixedInputClient();
        var source = new FillPayloadSource(PayloadBytes);
        var clock = new ReplicatedClientFixedInputClock(
            client,
            source,
            TickRateHz,
            PayloadBytes,
            leadTicks,
            maxFutureTicks,
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
        private readonly uint[] _sampledTicks = new uint[16_384];
        private readonly uint[] _committedTicks = new uint[16_384];
        private readonly byte[] _committedPayloadHeads = new byte[16_384];
        private int _sampledCount;
        private int _committedCount;
        private byte _sequence;

        public FillPayloadSource(int payloadBytes)
        {
            _payloadBytes = payloadBytes;
        }

        public bool FailNext { get; set; }
        public bool FailNextCommit { get; set; }
        public int SampleCount => _sampledCount;
        public int CommitCount => _committedCount;
        public uint LastSampledTargetTick { get; private set; }
        public uint LastCommittedTargetTick { get; private set; }
        public IReadOnlyList<uint> SampledTicks => _sampledTicks.AsSpan(0, _sampledCount).ToArray();
        public IReadOnlyList<uint> CommittedTicks => _committedTicks.AsSpan(0, _committedCount).ToArray();
        public IReadOnlyList<byte> CommittedPayloadHeads =>
            _committedPayloadHeads.AsSpan(0, _committedCount).ToArray();

        public FixedInputPayloadSampleStatus TrySample(uint targetTick, Span<byte> destination)
        {
            if (FailNext)
            {
                FailNext = false;
                return FixedInputPayloadSampleStatus.Failed;
            }

            if (!FixedInputWireCodec.IsValidInputTargetTick(targetTick) ||
                destination.Length != _payloadBytes)
            {
                return FixedInputPayloadSampleStatus.Failed;
            }

            if (_sampledCount >= _sampledTicks.Length)
            {
                throw new InvalidOperationException("Payload source sample capacity exceeded.");
            }

            LastSampledTargetTick = targetTick;
            _sampledTicks[_sampledCount++] = targetTick;
            destination.Fill(_sequence++);
            return FixedInputPayloadSampleStatus.Sampled;
        }

        public FixedInputPayloadCommitStatus TryCommit(uint targetTick, ReadOnlySpan<byte> sentPayload)
        {
            if (FailNextCommit)
            {
                FailNextCommit = false;
                return FixedInputPayloadCommitStatus.Failed;
            }

            if (!FixedInputWireCodec.IsValidInputTargetTick(targetTick) ||
                sentPayload.Length != _payloadBytes)
            {
                return FixedInputPayloadCommitStatus.Failed;
            }

            if (_committedCount >= _committedTicks.Length)
            {
                throw new InvalidOperationException("Payload source commit capacity exceeded.");
            }

            LastCommittedTargetTick = targetTick;
            _committedTicks[_committedCount] = targetTick;
            _committedPayloadHeads[_committedCount] = sentPayload[0];
            _committedCount++;
            return FixedInputPayloadCommitStatus.Committed;
        }
    }

    private sealed class FakeFixedInputClient : IReplicatedClientFixedInputPort
    {
        private readonly uint[] _emitted = new uint[16_384];
        private int _emittedCount;
        private bool _hasEnqueued;
        private uint _lastEnqueued;
        private uint _acknowledgedCommitted;
        private ulong _ackObservationVersion;

        public ReplicatedClientConnectionState State { get; set; } = ReplicatedClientConnectionState.Connected;
        public SessionEpoch SessionEpoch { get; set; } = new(1);
        public FixedInputOutboxEnqueueStatus EnqueueStatus { get; set; } = FixedInputOutboxEnqueueStatus.Enqueued;
        public bool PulseResult { get; set; } = true;
        public uint AcceptedHighestTargetTickOverride { get; set; }
        public int SubmitCount { get; private set; }
        public int PulseCount { get; private set; }
        public IReadOnlyList<uint> EmittedTicks => _emitted.AsSpan(0, _emittedCount).ToArray();

        public uint FixedInputAcknowledgedCommittedTick => _acknowledgedCommitted;
        public ulong FixedInputAcknowledgementObservationVersion => _ackObservationVersion;
        public bool HasEnqueuedFixedInputTargetTick => _hasEnqueued;
        public uint LastEnqueuedFixedInputTargetTick => _lastEnqueued;

        public void ApplyAuthoritativeAck(uint committedThroughTick)
        {
            if (!FixedInputWireCodec.IsValidSimulationTickField(committedThroughTick))
            {
                throw new ArgumentOutOfRangeException(nameof(committedThroughTick));
            }

            _acknowledgedCommitted = committedThroughTick;
            _ackObservationVersion = checked(_ackObservationVersion + 1UL);
        }

        public void ResetOutboxGeneration()
        {
            _hasEnqueued = false;
            _lastEnqueued = 0;
            _acknowledgedCommitted = 0;
            _ackObservationVersion = 0;
            _emittedCount = 0;
            SubmitCount = 0;
            PulseCount = 0;
        }

        public void SeedEnqueuedTick(uint tick)
        {
            if (!FixedInputWireCodec.IsValidInputTargetTick(tick))
            {
                throw new ArgumentOutOfRangeException(nameof(tick));
            }

            _hasEnqueued = true;
            _lastEnqueued = tick;
        }

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

            if (!FixedInputWireCodec.IsValidInputTargetTick(targetTick))
            {
                return FixedInputOutboxEnqueueStatus.InvalidInput;
            }

            if (_hasEnqueued && targetTick <= _lastEnqueued)
            {
                return FixedInputOutboxEnqueueStatus.TickNotIncreasing;
            }

            if (_emittedCount >= _emitted.Length)
            {
                throw new InvalidOperationException("Fake fixed-input client tick capacity exceeded.");
            }

            SubmitCount++;
            _emitted[_emittedCount++] = targetTick;
            _lastEnqueued = targetTick;
            _hasEnqueued = true;
            return FixedInputOutboxEnqueueStatus.Enqueued;
        }

        public FixedInputSendPulseResult TryPulseFixedInputSend()
        {
            if (!PulseResult || State != ReplicatedClientConnectionState.Connected)
            {
                return new FixedInputSendPulseResult(
                    FixedInputSendPulseStatus.TransportRejected,
                    firstAcceptedTargetTick: 0,
                    highestAcceptedTargetTick: 0,
                    acceptedFrameCount: 0);
            }

            PulseCount++;
            uint highestAccepted = AcceptedHighestTargetTickOverride == 0
                ? _lastEnqueued
                : AcceptedHighestTargetTickOverride;
            return new FixedInputSendPulseResult(
                FixedInputSendPulseStatus.Accepted,
                highestAccepted,
                highestAccepted,
                acceptedFrameCount: 1);
        }
    }
}
