using System;
using Ludots.Core.Networking.Configuration;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Physics3D;
using Ludots.Core.Physics3DNet;
using NUnit.Framework;

namespace Ludots.Tests.Physics3D;

[TestFixture]
public sealed class Physics3DNetworkClockValidatorTests
{
    [Test]
    public void Validate_MatchingThirtyHzContract_Succeeds()
    {
        Assert.DoesNotThrow(() => Physics3DNetworkClockValidator.Validate(
            engineFixedHz: 30,
            networkSimulationTickRateHz: 30,
            physicsFixedStepHz: 30,
            maximumPhysicsStepsPerSourceTick: 1));

        Assert.DoesNotThrow(() => Physics3DNetworkClockValidator.Validate(
            engineFixedHz: 30,
            CreateNetworkConfig(simulationTickRateHz: 30),
            CreatePhysicsConfig(fixedStepHz: 30, maximumPhysicsStepsPerSourceTick: 1)));

        Assert.DoesNotThrow(() => Physics3DNetworkClockValidator.Validate(
            engineFixedDeltaSeconds: 1f / 30f,
            CreateNetworkConfig(simulationTickRateHz: 30),
            CreatePhysicsConfig(fixedStepHz: 30, maximumPhysicsStepsPerSourceTick: 1)));
    }

    [Test]
    public void Validate_EngineFixedHzMismatch_Fails()
    {
        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Physics3DNetworkClockValidator.Validate(
                engineFixedHz: 60,
                networkSimulationTickRateHz: 30,
                physicsFixedStepHz: 30,
                maximumPhysicsStepsPerSourceTick: 1))!;
        Assert.That(ex.ActualValue, Is.EqualTo(60));
    }

    [Test]
    public void Validate_NetworkSimulationTickRateMismatch_Fails()
    {
        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Physics3DNetworkClockValidator.Validate(
                engineFixedHz: 30,
                networkSimulationTickRateHz: 20,
                physicsFixedStepHz: 30,
                maximumPhysicsStepsPerSourceTick: 1))!;
        Assert.That(ex.ActualValue, Is.EqualTo(20));
    }

    [Test]
    public void Validate_AllRatesMatchingAtTwentyHz_StillFailsTheNetworkContract()
    {
        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Physics3DNetworkClockValidator.Validate(
                engineFixedHz: 20,
                networkSimulationTickRateHz: 20,
                physicsFixedStepHz: 20,
                maximumPhysicsStepsPerSourceTick: 1))!;
        Assert.That(ex.ActualValue, Is.EqualTo(20));
    }

    [Test]
    public void Validate_PhysicsFixedStepHzMismatch_Fails()
    {
        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Physics3DNetworkClockValidator.Validate(
                engineFixedHz: 30,
                networkSimulationTickRateHz: 30,
                physicsFixedStepHz: 60,
                maximumPhysicsStepsPerSourceTick: 1))!;
        Assert.That(ex.ActualValue, Is.EqualTo(60));
    }

    [Test]
    public void Validate_MaximumPhysicsStepsPerSourceTickMismatch_Fails()
    {
        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Physics3DNetworkClockValidator.Validate(
                engineFixedHz: 30,
                networkSimulationTickRateHz: 30,
                physicsFixedStepHz: 30,
                maximumPhysicsStepsPerSourceTick: 2))!;
        Assert.That(ex.ActualValue, Is.EqualTo(2));
    }

    [Test]
    public void Validate_EngineDeltaNotRepresentableAsIntegerHz_Fails()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Physics3DNetworkClockValidator.Validate(
                engineFixedDeltaSeconds: 0.03f, // not exactly 1/30 or 1/33
                CreateNetworkConfig(simulationTickRateHz: 30),
                CreatePhysicsConfig(fixedStepHz: 30, maximumPhysicsStepsPerSourceTick: 1)));
    }

    [Test]
    public void Validate_RepresentableEngineDeltaButWrongHz_Fails()
    {
        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Physics3DNetworkClockValidator.Validate(
                engineFixedDeltaSeconds: 1f / 60f,
                CreateNetworkConfig(simulationTickRateHz: 30),
                CreatePhysicsConfig(fixedStepHz: 30, maximumPhysicsStepsPerSourceTick: 1)))!;
        Assert.That(ex.ActualValue, Is.EqualTo(60));
    }

    [Test]
    public void RequireRepresentableIntegerHz_RejectsNonFiniteAndNonPositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Physics3DNetworkClockValidator.RequireRepresentableIntegerHz(0f, "dt"));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Physics3DNetworkClockValidator.RequireRepresentableIntegerHz(-1f, "dt"));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Physics3DNetworkClockValidator.RequireRepresentableIntegerHz(float.NaN, "dt"));
    }

    private static NetworkRuntimeConfig CreateNetworkConfig(int simulationTickRateHz) => new()
    {
        ProfileId = "physics3d_clock_validator",
        ReferenceTransport = "test",
        ProtocolMajor = 1,
        ProtocolMinor = 0,
        PlayerCapacity = 2,
        SimulationTickRateHz = simulationTickRateHz,
        StatePublishRateHz = 10,
        GlobalNetworkEntityCapacity = 8,
        ReplicationEntityCapacityPerSeat = 4,
        OrderQueueCapacity = 8,
        MaxCommandBatchesPerSecondPerPlayer = 4,
        CommandBurstBatchCapacity = 4,
        MaxActorsPerCommandBatch = 2,
        CommandSequenceHistoryCapacity = 16,
        MaxPastTargetTicks = 1,
        MaxFutureTargetTicks = 2,
        NetworkAdmissionResultCapacity = 16,
        EntityAdmissionResultCapacity = 8,
        ReconnectWindowSeconds = 10,
        BaselineCapacity = 4,
        DisclosureChangeLogCapacity = 16,
        DatagramQueueCapacity = 16,
        ConnectionEventCapacity = 8,
        MaxDatagramPayloadBytes = 1200,
        TransportMaxConnectAttempts = 3,
        TransportDisconnectTimeoutMilliseconds = 5_000,
        ReliableDisconnectFlushTimeoutMilliseconds = 4_000,
        TransportChannelCount = 4,
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
        SnapshotChunkCapacity = 8,
        MaxServerOutboundBytesPerSecondPerClient = 64 * 1024,
        TickP95BudgetMicroseconds = 26_700,
        TickP99BudgetMicroseconds = 31_000,
        CommandSchemas =
        {
            new NetworkCommandSchemaConfig
            {
                OrderTypeKey = "moveTo",
                TargetKind = NetworkCommandTargetKind.WorldPositionCm,
            },
        },
    };

    private static Physics3DWorldConfig CreatePhysicsConfig(int fixedStepHz, int maximumPhysicsStepsPerSourceTick) => new()
    {
        MobileBodyCapacity = 16,
        StaticBodyCapacity = 16,
        ShapeCapacity = 32,
        InactiveIslandCapacity = 8,
        ConstraintCapacity = 32,
        ConstraintsPerTypeBatchCapacity = 16,
        ConstraintCountPerBodyEstimate = 4,
        ContactPairCapacityPerWorker = 64,
        ActuationCommandCapacity = 32,
        WorkerCount = 1,
        FixedStepHz = fixedStepHz,
        MaximumPhysicsStepsPerSourceTick = maximumPhysicsStepsPerSourceTick,
        SolverSubstepCount = 1,
        SolverVelocityIterationCount = 1,
        GravityCmPerSecondSquared = new System.Numerics.Vector3(0f, -980f, 0f),
        LinearDamping = 0f,
        AngularDamping = 0f,
        MaximumSpeculativeMarginCm = 1f,
        SleepThreshold = 0.01f,
        MinimumTimestepCountUnderSleepThreshold = 1,
        ContinuousMinimumSweepTimestep = 1e-4f,
        ContinuousSweepConvergenceThreshold = 1e-4f,
        MaterialCombineMode = Physics3DMaterialCombineMode.GeometricMean,
    };
}
