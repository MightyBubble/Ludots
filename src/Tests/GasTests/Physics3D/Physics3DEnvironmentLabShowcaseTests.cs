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

        // When the player enters Material Hill and lets all three crates complete the slope.
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
            Assert.That(harness.Runtime.MaterialHillImpulseSubmissionCount, Is.EqualTo(3));
            Assert.That(harness.PhysicsWorld.PendingActuationCommandCount, Is.EqualTo(3));
            Assert.That(firstRampColor, Is.Not.EqualTo(secondRampColor));
            Assert.That(firstCrateColor, Is.EqualTo(secondCrateColor));
            Assert.That(secondCrateColor, Is.EqualTo(thirdCrateColor));
        });
        harness.CompletePreparedStep();
        harness.Step(179);

        // Then the low-friction crate travels farther and the panel explains the result as an exhibit.
        float[] travelCm = new float[3];
        float[] friction = new float[3];
        for (int laneIndex = 0; laneIndex < 3; laneIndex++)
        {
            Assert.That(harness.Runtime.TryGetMaterialHillLaneState(
                laneIndex,
                out _,
                out friction[laneIndex],
                out travelCm[laneIndex]), Is.True);
        }

        Physics3DShowcasePanelState panel = harness.Runtime.CapturePanelState();
        Assert.Multiple(() =>
        {
            Assert.That(friction, Is.Ordered.Ascending);
            Assert.That(travelCm[0], Is.GreaterThan(travelCm[1]));
            Assert.That(travelCm[1], Is.GreaterThan(travelCm[2]));
            Assert.That(panel.MaterialSummary, Does.Contain(config.MaterialHill.Lanes[0].Name));
            Assert.That(panel.MaterialSummary, Does.Contain(config.MaterialHill.Lanes[1].Name));
            Assert.That(panel.MaterialSummary, Does.Contain(config.MaterialHill.Lanes[2].Name));
            Assert.That(panel.MaterialSummary, Does.Contain("slid"));
        });
    }

    [Test]
    public void Feature_WindTunnel_Scenario_LightAndHeavyObjectsExposeAllThreeWindPatterns()
    {
        // Given steady wind, a fixed-tick gust, and a vortex in one authoritative world.
        Physics3DShowcaseConfig config = LoadRuntimeConfig();
        config.WindTunnel.AwakeBodyCapacity = 6;
        using var harness = new EnvironmentLabHarness(config);

        // When the player enters Wind Tunnel and watches two gust cycles.
        harness.PrepareScene(Physics3DShowcaseScene.WindTunnel);
        Assert.Multiple(() =>
        {
            Assert.That(harness.Runtime.WindTunnelFieldCount, Is.EqualTo(3));
            Assert.That(harness.Runtime.BodyCount, Is.EqualTo(7));
            Assert.That(harness.Runtime.DynamicBodyCount, Is.EqualTo(6));
            Assert.That(harness.Runtime.StaticBodyCount, Is.EqualTo(1));
            Assert.That(harness.PhysicsWorld.PendingActuationCommandCount, Is.EqualTo(6));
        });
        harness.CompletePreparedStep();
        int gustCycleTicks = config.WindTunnel.GustAttackTicks +
            config.WindTunnel.GustHoldTicks +
            config.WindTunnel.GustReleaseTicks +
            config.WindTunnel.GustCalmTicks;
        harness.Step((gustCycleTicks * 2) - 1);

        // Then each station remains observable and light objects react farther than heavy ones.
        float[] lightTravelCm = new float[3];
        float[] heavyTravelCm = new float[3];
        for (int zoneIndex = 0; zoneIndex < 3; zoneIndex++)
        {
            Assert.That(harness.Runtime.TryGetWindTunnelPairState(
                zoneIndex,
                out _,
                out _,
                out lightTravelCm[zoneIndex],
                out heavyTravelCm[zoneIndex]), Is.True);
        }

        Physics3DShowcasePanelState panel = harness.Runtime.CapturePanelState();
        Assert.Multiple(() =>
        {
            Assert.That(lightTravelCm[0], Is.GreaterThan(heavyTravelCm[0]));
            Assert.That(lightTravelCm[1], Is.GreaterThan(heavyTravelCm[1]));
            Assert.That(lightTravelCm[2], Is.GreaterThan(heavyTravelCm[2]));
            Assert.That(panel.WindSummary, Does.Contain("Steady"));
            Assert.That(panel.WindSummary, Does.Contain("Gust"));
            Assert.That(panel.WindSummary, Does.Contain("Vortex"));
            Assert.That(panel.WindSummary, Does.Contain("light"));
            Assert.That(panel.WindSummary, Does.Contain("heavy"));
        });
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

        public void Dispose()
        {
            Runtime.Dispose();
            PhysicsWorld.Dispose();
            EcsWorld.Dispose();
        }
    }
}
