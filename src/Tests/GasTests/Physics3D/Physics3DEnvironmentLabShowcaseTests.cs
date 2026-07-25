using System;
using System.IO;
using System.Numerics;
using System.Text.Json.Nodes;
using Arch.Core;
using CapabilityStandardPhysics3DShowcaseMod.Runtime;
using Ludots.Core.Physics3D;
using NUnit.Framework;

namespace Ludots.Tests.Physics3D;

[TestFixture]
[NonParallelizable]
public sealed class Physics3DEnvironmentLabShowcaseTests
{
    [Test]
    public void Feature_MaterialHill_Scenario_IdenticalCratesRevealThreeSurfaceResponses()
    {
        // Given three authored lanes with one shared crate mass and launch impulse.
        Physics3DShowcaseConfig config = LoadRuntimeConfig();
        using var harness = new EnvironmentLabHarness(config);

        // When the player enters Material Hill, then explicitly launches all three crates.
        harness.PrepareScene(Physics3DShowcaseScene.MaterialHill);
        Assert.That(harness.Runtime.TryGetBodyVisual(1, out _, out _, out _, out _, out _, out Vector4 firstRampColor), Is.True);
        Assert.That(harness.Runtime.TryGetBodyVisual(2, out _, out _, out _, out _, out _, out Vector4 firstCrateColor), Is.True);
        Assert.That(harness.Runtime.TryGetBodyVisual(3, out _, out _, out _, out _, out _, out Vector4 secondRampColor), Is.True);
        Assert.That(harness.Runtime.TryGetBodyVisual(4, out _, out _, out _, out _, out _, out Vector4 secondCrateColor), Is.True);
        Assert.That(harness.Runtime.TryGetBodyVisual(6, out _, out _, out _, out _, out _, out Vector4 thirdCrateColor), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(harness.Runtime.BodyCount, Is.EqualTo(7));
            Assert.That(harness.Runtime.DynamicBodyCount, Is.EqualTo(3));
            Assert.That(harness.Runtime.StaticBodyCount, Is.EqualTo(4));
            Assert.That(harness.Runtime.MaterialHillImpulseSubmissionCount, Is.Zero);
            Assert.That(harness.PhysicsWorld.PendingActuationCommandCount, Is.Zero);
            Assert.That(firstRampColor, Is.Not.EqualTo(secondRampColor));
            Assert.That(firstCrateColor, Is.EqualTo(secondCrateColor));
            Assert.That(secondCrateColor, Is.EqualTo(thirdCrateColor));
        });
        harness.CompletePreparedStep();
        harness.PrepareImpact();
        Assert.Multiple(() =>
        {
            Assert.That(harness.Runtime.MaterialHillImpulseSubmissionCount, Is.EqualTo(3));
            Assert.That(harness.PhysicsWorld.PendingActuationCommandCount, Is.EqualTo(3));
        });
        harness.CompletePreparedStep();
        for (int step = 0;
             step < config.MaterialHill.CompletionTimeLimitTicks &&
             harness.Runtime.MaterialHillState.Status == Physics3DShowcaseChallengeStatus.Running;
             step++)
        {
            harness.Step();
        }

        // Then all three authoritative bodies are stable, the run completes, and the distances are ranked.
        float[] travelCm = new float[3];
        float[] friction = new float[3];
        for (int laneIndex = 0; laneIndex < 3; laneIndex++)
        {
            Assert.That(harness.Runtime.TryGetMaterialHillLaneState(
                laneIndex,
                out Physics3DBodyState state,
                out friction[laneIndex],
                out travelCm[laneIndex]), Is.True);
            Assert.That(
                state.LinearVelocityCmPerSecond.Length(),
                Is.LessThanOrEqualTo(config.MaterialHill.StableMaximumLinearSpeedCmPerSecond + 0.001f),
                $"lane {laneIndex}, status {harness.Runtime.MaterialHillState.Status}, position {state.PositionCm}, travel {travelCm[laneIndex]}");
            Assert.That(
                state.AngularVelocityRadiansPerSecond.Length(),
                Is.LessThanOrEqualTo(config.MaterialHill.StableMaximumAngularSpeedRadiansPerSecond + 0.001f));
        }

        Physics3DShowcasePanelState panel = harness.Runtime.CapturePanelState();
        Assert.Multiple(() =>
        {
            Assert.That(panel.MaterialHill.Status, Is.EqualTo(Physics3DShowcaseChallengeStatus.Complete));
            Assert.That(panel.MaterialHill.StableTicks, Is.EqualTo(config.MaterialHill.RequiredStableTicks));
            Assert.That(panel.MaterialHill.FirstPlaceLaneIndex, Is.EqualTo(0));
            Assert.That(panel.MaterialHill.SecondPlaceLaneIndex, Is.EqualTo(1));
            Assert.That(panel.MaterialHill.ThirdPlaceLaneIndex, Is.EqualTo(2));
            Assert.That(panel.MaterialHill.WinningMarginCm, Is.GreaterThan(0f));
            Assert.That(friction, Is.Ordered.Ascending);
            Assert.That(travelCm[0], Is.GreaterThan(travelCm[1]));
            Assert.That(travelCm[1], Is.GreaterThan(travelCm[2]));
            Assert.That(panel.MaterialSummary, Does.Contain(config.MaterialHill.Lanes[0].Name));
            Assert.That(panel.MaterialSummary, Does.Contain(config.MaterialHill.Lanes[1].Name));
            Assert.That(panel.MaterialSummary, Does.Contain(config.MaterialHill.Lanes[2].Name));
            Assert.That(panel.MaterialSummary, Does.StartWith("COMPLETE").And.Contain("slid").And.Contain("(-"));
            Assert.That(harness.Simulation.Enabled, Is.True);
        });
    }

    [Test]
    public void Feature_MaterialHill_Scenario_CratesWaitForOnePlayerLaunchAndRequireResetBeforeAnother()
    {
        // Given a player has entered Material Hill but has not pressed Push Crates.
        Physics3DShowcaseConfig config = LoadRuntimeConfig();
        using var harness = new EnvironmentLabHarness(config);
        harness.SelectScene(Physics3DShowcaseScene.MaterialHill);
        var startPositions = new Vector3[3];
        for (int laneIndex = 0; laneIndex < startPositions.Length; laneIndex++)
        {
            Assert.That(
                harness.Runtime.TryGetMaterialHillLaneState(
                    laneIndex,
                    out Physics3DBodyState state,
                    out _,
                    out _),
                Is.True);
            startPositions[laneIndex] = state.PositionCm;
        }

        // When several authoritative fixed steps pass without player input.
        harness.Step(8);

        // Then every crate remains asleep at its authored start and no impulse was submitted.
        Assert.Multiple(() =>
        {
            Assert.That(harness.Runtime.MaterialHillImpulseSubmissionCount, Is.Zero);
            Assert.That(harness.Runtime.MaterialHillState.Status, Is.EqualTo(Physics3DShowcaseChallengeStatus.Ready));
            Assert.That(harness.PhysicsWorld.PendingActuationCommandCount, Is.Zero);
            for (int laneIndex = 0; laneIndex < startPositions.Length; laneIndex++)
            {
                Assert.That(
                    harness.Runtime.TryGetMaterialHillLaneState(
                        laneIndex,
                        out Physics3DBodyState state,
                        out _,
                        out float travelCm),
                    Is.True);
                Assert.That(state.Awake, Is.False, $"Lane {laneIndex} must wait asleep for Push Crates.");
                Assert.That(state.PositionCm, Is.EqualTo(startPositions[laneIndex]));
                Assert.That(travelCm, Is.EqualTo(0f).Within(0.001f));
            }
        });

        // When the player presses Push Crates, the formal command queues exactly one impulse per lane.
        harness.PrepareImpact();
        Assert.Multiple(() =>
        {
            Assert.That(harness.Runtime.MaterialHillImpulseSubmissionCount, Is.EqualTo(3));
            Assert.That(harness.PhysicsWorld.PendingActuationCommandCount, Is.EqualTo(3));
            Assert.That(harness.Runtime.MaterialHillState.Status, Is.EqualTo(Physics3DShowcaseChallengeStatus.Running));
        });
        harness.CompletePreparedStep();

        // And pressing Push Crates again without Reset is rejected explicitly and queues nothing.
        harness.PrepareImpact();
        Assert.Multiple(() =>
        {
            Assert.That(harness.Runtime.MaterialHillImpulseSubmissionCount, Is.EqualTo(3));
            Assert.That(harness.PhysicsWorld.PendingActuationCommandCount, Is.Zero);
            Assert.That(harness.Runtime.CapturePanelState().LastAction, Does.Contain("Reset"));
        });
        harness.CompletePreparedStep();

        // When the player uses the formal Reset command, the station returns to its launch-ready state.
        harness.PrepareReset();
        Assert.Multiple(() =>
        {
            Assert.That(harness.Runtime.MaterialHillImpulseSubmissionCount, Is.Zero);
            Assert.That(harness.PhysicsWorld.PendingActuationCommandCount, Is.Zero);
            Assert.That(harness.Runtime.MaterialHillState.Status, Is.EqualTo(Physics3DShowcaseChallengeStatus.Ready));
            for (int laneIndex = 0; laneIndex < startPositions.Length; laneIndex++)
            {
                Assert.That(
                    harness.Runtime.TryGetMaterialHillLaneState(
                        laneIndex,
                        out Physics3DBodyState state,
                        out _,
                        out float travelCm),
                    Is.True);
                Assert.That(state.Awake, Is.False);
                Assert.That(state.PositionCm, Is.EqualTo(startPositions[laneIndex]));
                Assert.That(travelCm, Is.EqualTo(0f).Within(0.001f));
            }
        });
        harness.CompletePreparedStep();

        // Then Push Crates is accepted once again after that reset.
        harness.PrepareImpact();
        Assert.Multiple(() =>
        {
            Assert.That(harness.Runtime.MaterialHillImpulseSubmissionCount, Is.EqualTo(3));
            Assert.That(harness.PhysicsWorld.PendingActuationCommandCount, Is.EqualTo(3));
        });
        harness.CompletePreparedStep();
    }

    [Test]
    public void Feature_WindTunnel_Scenario_PlayerSelectsReversesRelaunchesAndResetsAComparisonZone()
    {
        // Given a player enters the authored Steady comparison with all three formal wind fields active.
        Physics3DShowcaseConfig config = LoadRuntimeConfig();
        config.WindTunnel.AwakeBodyCapacity = 6;
        using var harness = new EnvironmentLabHarness(config);
        harness.PrepareScene(Physics3DShowcaseScene.WindTunnel);
        Assert.Multiple(() =>
        {
            Assert.That(harness.Runtime.WindTunnelFieldCount, Is.EqualTo(3));
            Assert.That(harness.Runtime.BodyCount, Is.EqualTo(7));
            Assert.That(harness.Runtime.DynamicBodyCount, Is.EqualTo(6));
            Assert.That(harness.Runtime.StaticBodyCount, Is.EqualTo(1));
            Assert.That(harness.PhysicsWorld.PendingActuationCommandCount, Is.EqualTo(6));
            Assert.That(harness.Runtime.WindTunnelZone, Is.EqualTo(config.WindTunnel.InitialZone));
            Assert.That(harness.Runtime.WindTunnelDirection, Is.EqualTo(config.WindTunnel.InitialDirection));
        });
        harness.CompletePreparedStep();
        harness.Step(30);

        // When the player selects Vortex, reverses every formal field, and relaunches that light/heavy pair.
        harness.Runtime.EnqueueCommand(new Physics3DShowcaseCommand(
            Physics3DShowcaseCommandKind.SetWindZone,
            (int)Physics3DShowcaseWindZone.Vortex));
        harness.Runtime.EnqueueCommand(new Physics3DShowcaseCommand(Physics3DShowcaseCommandKind.ReverseWindDirection));
        harness.Runtime.EnqueueCommand(new Physics3DShowcaseCommand(Physics3DShowcaseCommandKind.RelaunchWindPair));
        harness.Runtime.PrepareFixedStep();
        Physics3DShowcasePanelState relaunched = harness.Runtime.CapturePanelState();
        Assert.Multiple(() =>
        {
            Assert.That(relaunched.WindZone, Is.EqualTo(Physics3DShowcaseWindZone.Vortex));
            Assert.That(relaunched.WindDirection, Is.EqualTo(Physics3DShowcaseDriveDirection.Reverse));
            Assert.That(relaunched.WindLightTravelCm, Is.EqualTo(0f).Within(0.001f));
            Assert.That(relaunched.WindHeavyTravelCm, Is.EqualTo(0f).Within(0.001f));
            Assert.That(relaunched.WindComparisonStatus, Is.EqualTo(Physics3DShowcaseChallengeStatus.Running));
            Assert.That(relaunched.LastAction, Does.Contain("Relaunched").And.Contain("Vortex"));
            Assert.That(harness.Runtime.TryGetWindTunnelZoneVisual(out _, out _, out Vector3 direction), Is.True);
            Assert.That(direction.Z, Is.LessThan(0f));
        });
        harness.CompletePreparedStep();
        harness.Step(59);
        Physics3DShowcasePanelState moved = harness.Runtime.CapturePanelState();

        // Then the selected zone and direction stay visible, and the light body travels farther than the heavy body.
        Assert.Multiple(() =>
        {
            Assert.That(moved.WindSummary, Does.Contain("Vortex").And.Contain("REVERSE"));
            Assert.That(moved.WindComparisonStatus, Is.EqualTo(Physics3DShowcaseChallengeStatus.Complete));
            Assert.That(moved.WindComparisonTicksRemaining, Is.Zero);
            Assert.That(moved.WindSummary, Does.StartWith("COMPLETE"));
            Assert.That(moved.WindLightTravelCm, Is.GreaterThan(moved.WindHeavyTravelCm));
            Assert.That(moved.WindHeavyTravelCm, Is.GreaterThan(0f));
        });

        // When Reset is pressed, then the authored selected zone, direction, and launch positions return.
        harness.PrepareReset();
        Physics3DShowcasePanelState reset = harness.Runtime.CapturePanelState();
        Assert.Multiple(() =>
        {
            Assert.That(reset.WindZone, Is.EqualTo(config.WindTunnel.InitialZone));
            Assert.That(reset.WindDirection, Is.EqualTo(config.WindTunnel.InitialDirection));
            Assert.That(reset.WindLightTravelCm, Is.EqualTo(0f).Within(0.001f));
            Assert.That(reset.WindHeavyTravelCm, Is.EqualTo(0f).Within(0.001f));
            Assert.That(reset.WindComparisonStatus, Is.EqualTo(Physics3DShowcaseChallengeStatus.Running));
            Assert.That(reset.LastAction, Does.Contain("Reset Wind Tunnel"));
        });
        harness.CompletePreparedStep();
    }

    [Test]
    public void Feature_WindTunnel_Scenario_InsufficientAwakeCapacityFailsBeforePlay()
    {
        // Given a wind configuration that cannot snapshot all six comparison bodies.
        Physics3DShowcaseConfig config = LoadRuntimeConfig();
        config.WindTunnel.AwakeBodyCapacity = 5;

        // When activation validates its fixed-capacity storage, then it rejects the scene explicitly.
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => new EnvironmentLabHarness(config))!;
        Assert.That(exception.Message, Does.Contain("AwakeBodyCapacity"));
    }

    [Test]
    [Explicit("Allocation gate is run deliberately after the functional environment-lab suite.")]
    public void WindTunnel_WarmedThirtyHzStepsAllocateZeroBytesOnCallingThread()
    {
        Physics3DShowcaseConfig config = LoadRuntimeConfig();
        using var harness = new EnvironmentLabHarness(config);
        harness.SelectScene(Physics3DShowcaseScene.WindTunnel);
        harness.Step(60);

        long before = GC.GetAllocatedBytesForCurrentThread();
        harness.Step(30);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.That(allocated, Is.Zero);
    }

    private static Physics3DShowcaseConfig LoadRuntimeConfig()
    {
        string path = Path.Combine(
            FindRepoRoot(),
            "mods",
            "showcases",
            "capability_standard",
            "CapabilityStandardPhysics3DShowcaseMod",
            "assets",
            "CapabilityStandardPhysics3DShowcaseConfig.json");
        JsonObject json = JsonNode.Parse(File.ReadAllText(path))?.AsObject()
            ?? throw new InvalidOperationException("Physics3D showcase config is missing.");
        Physics3DShowcaseConfig config = Physics3DShowcaseConfig.Load(json);
        config.MaximumBodies = 256;
        config.VisibleBodyLimit = 256;
        return config;
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "launcher.config.json")) &&
                Directory.Exists(Path.Combine(directory.FullName, "mods")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Ludots repository root.");
    }

    private static Physics3DWorldConfig CreateWorldConfig() => new()
    {
        MobileBodyCapacity = 64,
        StaticBodyCapacity = 64,
        ShapeCapacity = 256,
        InactiveIslandCapacity = 64,
        ConstraintCapacity = 256,
        ConstraintsPerTypeBatchCapacity = 256,
        ConstraintCountPerBodyEstimate = 8,
        ContactPairCapacityPerWorker = 4_096,
        ActuationCommandCapacity = 128,
        WorkerCount = 2,
        FixedStepHz = 30,
        MaximumPhysicsStepsPerSourceTick = 1,
        SolverSubstepCount = 1,
        SolverVelocityIterationCount = 8,
        GravityCmPerSecondSquared = new Vector3(0f, -981f, 0f),
        LinearDamping = 0f,
        AngularDamping = 0.03f,
        MaximumSpeculativeMarginCm = 10f,
        SleepThreshold = 0.01f,
        MinimumTimestepCountUnderSleepThreshold = 255,
        ContinuousMinimumSweepTimestep = 0.001f,
        ContinuousSweepConvergenceThreshold = 0.001f,
        MaterialCombineMode = Physics3DMaterialCombineMode.GeometricMean
    };

    private sealed class EnvironmentLabHarness : IDisposable
    {
        public EnvironmentLabHarness(Physics3DShowcaseConfig config)
        {
            EcsWorld = World.Create();
            try
            {
                PhysicsWorld = new Physics3DWorld(CreateWorldConfig());
                Simulation = new Physics3DSimulationSystem(EcsWorld, PhysicsWorld, 30, 1);
                Runtime = new Physics3DShowcaseRuntime();
                Runtime.ActivateForTests(EcsWorld, PhysicsWorld, Simulation, config);
            }
            catch
            {
                PhysicsWorld?.Dispose();
                EcsWorld.Dispose();
                throw;
            }
        }

        public World EcsWorld { get; }
        public Physics3DWorld PhysicsWorld { get; private set; } = null!;
        public Physics3DSimulationSystem Simulation { get; private set; } = null!;
        public Physics3DShowcaseRuntime Runtime { get; private set; } = null!;

        public void SelectScene(Physics3DShowcaseScene scene)
        {
            PrepareScene(scene);
            CompletePreparedStep();
        }

        public void PrepareScene(Physics3DShowcaseScene scene)
        {
            Runtime.EnqueueCommand(new Physics3DShowcaseCommand(
                Physics3DShowcaseCommandKind.SelectScene,
                (int)scene));
            Runtime.PrepareFixedStep();
        }

        public void CompletePreparedStep()
        {
            Simulation.Update(1f / 30f);
            Runtime.ObserveFixedStep();
        }

        public void Step(int count = 1)
        {
            for (int step = 0; step < count; step++)
            {
                Runtime.PrepareFixedStep();
                CompletePreparedStep();
            }
        }

        public void PrepareImpact()
        {
            Runtime.EnqueueCommand(new Physics3DShowcaseCommand(Physics3DShowcaseCommandKind.Impact));
            Runtime.PrepareFixedStep();
        }

        public void PrepareReset()
        {
            Runtime.EnqueueCommand(new Physics3DShowcaseCommand(Physics3DShowcaseCommandKind.Reset));
            Runtime.PrepareFixedStep();
        }

        public void Dispose()
        {
            Runtime.Dispose();
            PhysicsWorld.Dispose();
            EcsWorld.Dispose();
        }
    }
}
