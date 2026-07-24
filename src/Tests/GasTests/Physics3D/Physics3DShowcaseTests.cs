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
public sealed class Physics3DShowcaseTests
{
    [Test]
    public void ShowcaseConfig_IsStrictAndOfficialPresetsFitOwnedCapacity()
    {
        string repoRoot = FindRepoRoot();
        string path = Path.Combine(
            repoRoot,
            "mods",
            "showcases",
            "capability_standard",
            "CapabilityStandardPhysics3DShowcaseMod",
            "assets",
            "CapabilityStandardPhysics3DShowcaseConfig.json");
        JsonObject json = JsonNode.Parse(File.ReadAllText(path))?.AsObject()
            ?? throw new InvalidOperationException("Physics3D showcase config is missing.");

        Physics3DShowcaseConfig config = Physics3DShowcaseConfig.Load(json);
        Assert.That(config.InitialScene, Is.EqualTo(Physics3DShowcaseScene.Stacking));
        Assert.That(config.MaximumBodies, Is.EqualTo(11_000));
        Assert.That(config.BenchmarkPresets, Is.EqualTo(new[] { 1_000, 2_000, 5_000, 10_000 }));

        JsonObject unknownField = (JsonObject)json.DeepClone();
        unknownField["silentFallback"] = true;
        Assert.Throws<System.Text.Json.JsonException>(() => Physics3DShowcaseConfig.Load(unknownField));

        JsonObject numericEnum = (JsonObject)json.DeepClone();
        numericEnum["initialScene"] = 3;
        Assert.Throws<System.Text.Json.JsonException>(() => Physics3DShowcaseConfig.Load(numericEnum));
    }

    [Test]
    public void SimulationSystem_PauseAndSingleStep_AdvanceExactlyOneThirtyHzStep()
    {
        using World ecsWorld = World.Create();
        using var physicsWorld = new Physics3DWorld(CreateWorldConfig(16, 4));
        var simulation = new Physics3DSimulationSystem(
            ecsWorld,
            physicsWorld,
            sourceFixedStepHz: 30,
            maximumPhysicsStepsPerSourceTick: 1);
        simulation.Enabled = false;

        simulation.Update(1f / 30f);
        Assert.That(simulation.PhysicsStepsLastUpdate, Is.Zero);
        Assert.That(simulation.TotalPhysicsSteps, Is.Zero);

        simulation.RequestManualSteps(1);
        simulation.Update(1f / 30f);
        Assert.That(simulation.PhysicsStepsLastUpdate, Is.EqualTo(1));
        Assert.That(simulation.TotalPhysicsSteps, Is.EqualTo(1));

        simulation.Update(1f / 30f);
        Assert.That(simulation.PhysicsStepsLastUpdate, Is.Zero);
        Assert.That(simulation.TotalPhysicsSteps, Is.EqualTo(1));

        simulation.Enabled = true;
        for (int i = 0; i < 10; i++)
        {
            simulation.Update(1f / 30f);
            Assert.That(simulation.PhysicsStepsLastUpdate, Is.EqualTo(1));
        }

        Assert.That(simulation.TotalPhysicsSteps, Is.EqualTo(11));
    }

    [Test]
    public void NinePlayerScenes_CreateRunAndExposeTheirCapabilityEvidence()
    {
        Physics3DShowcaseConfig config = CreateShowcaseConfig(
            maximumBodies: 1_200,
            benchmarkPresets: new[] { 100, 200, 500, 1_000 },
            replaySteps: 30);
        using var harness = new ShowcaseHarness(config, CreateWorldConfig(1_300, 256));

        foreach (Physics3DShowcaseScene scene in Enum.GetValues<Physics3DShowcaseScene>())
        {
            harness.SelectScene(scene);
            Assert.That(harness.Runtime.ActiveScene, Is.EqualTo(scene));
            Assert.That(harness.Runtime.BodyCount, Is.GreaterThan(0), $"{scene} must create visible physics content.");
            harness.Step();
        }

        harness.SelectScene(Physics3DShowcaseScene.Queries);
        harness.Step();
        int totalQueryHits = 0;
        for (int i = 0; i < 7; i++)
        {
            totalQueryHits += harness.Runtime.GetQueryHitCount(i);
        }

        Assert.That(totalQueryHits, Is.GreaterThan(0));

        harness.SelectScene(Physics3DShowcaseScene.Joints);
        Assert.That(harness.Runtime.ConstraintCount, Is.GreaterThan(0));
        Assert.That(harness.PhysicsWorld.ActiveConstraintCount, Is.EqualTo(harness.Runtime.ConstraintCount));

        harness.SelectScene(Physics3DShowcaseScene.ContactEvents);
        for (int i = 0; i < 82; i++)
        {
            harness.Step();
        }

        Assert.That(harness.Runtime.ContactBeginCount, Is.GreaterThanOrEqualTo(2));
        Assert.That(harness.Runtime.ContactStayCount, Is.GreaterThan(0));
        Assert.That(harness.Runtime.ContactEndCount, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void StackingScene_RemainsReadableAfterEightSecondsOfAuthoritativeSimulation()
    {
        string repoRoot = FindRepoRoot();
        string path = Path.Combine(
            repoRoot,
            "mods",
            "showcases",
            "capability_standard",
            "CapabilityStandardPhysics3DShowcaseMod",
            "assets",
            "CapabilityStandardPhysics3DShowcaseConfig.json");
        JsonObject json = JsonNode.Parse(File.ReadAllText(path))?.AsObject()
            ?? throw new InvalidOperationException("Physics3D showcase config is missing.");
        Physics3DShowcaseConfig config = Physics3DShowcaseConfig.Load(json);
        using var harness = new ShowcaseHarness(config, CreateWorldConfig(config.MaximumBodies, 256));

        Vector3[] initialBoxes = CaptureDynamicPositions(harness.Runtime, Physics3DShapeKind.Box);
        Vector3[] initialSpheres = CaptureDynamicPositions(harness.Runtime, Physics3DShapeKind.Sphere);
        Vector3[] initialCapsules = CaptureDynamicPositions(harness.Runtime, Physics3DShapeKind.Capsule);
        for (int i = 0; i < 240; i++)
        {
            harness.Step();
        }

        Vector3[] settledBoxes = CaptureDynamicPositions(harness.Runtime, Physics3DShapeKind.Box);
        Vector3[] settledSpheres = CaptureDynamicPositions(harness.Runtime, Physics3DShapeKind.Sphere);
        Vector3[] settledCapsules = CaptureDynamicPositions(harness.Runtime, Physics3DShapeKind.Capsule);
        float maximumFootprintGrowthCm = config.BodySizeCm * 3f;
        float maximumCenterDriftCm = config.BodySizeCm * 1.5f;
        Assert.Multiple(() =>
        {
            Assert.That(
                HorizontalFootprint(settledBoxes),
                Is.LessThanOrEqualTo(HorizontalFootprint(initialBoxes) + maximumFootprintGrowthCm),
                "The blue box pyramid must not collapse into loose debris after eight seconds.");
            Assert.That(
                Vector2.Distance(HorizontalCenter(initialBoxes), HorizontalCenter(settledBoxes)),
                Is.LessThanOrEqualTo(maximumCenterDriftCm),
                "The blue box pyramid must stay at its authored display position.");
            Assert.That(
                MaximumHeight(settledBoxes),
                Is.GreaterThanOrEqualTo(MaximumHeight(initialBoxes) * 0.75f),
                "The blue box pyramid must retain its stepped silhouette.");
            Assert.That(
                HorizontalFootprint(settledSpheres),
                Is.LessThanOrEqualTo(HorizontalFootprint(initialSpheres) + maximumFootprintGrowthCm),
                "The gold sphere exhibit must not spread into loose debris after eight seconds.");
            Assert.That(
                Vector2.Distance(HorizontalCenter(initialSpheres), HorizontalCenter(settledSpheres)),
                Is.LessThanOrEqualTo(maximumCenterDriftCm),
                "The gold sphere exhibit must stay at its authored display position.");
            Assert.That(
                MaximumHeight(settledSpheres),
                Is.GreaterThanOrEqualTo(MaximumHeight(initialSpheres) * 0.75f),
                "The gold sphere exhibit must retain a visibly layered silhouette.");
            Assert.That(
                HorizontalFootprint(settledCapsules),
                Is.LessThanOrEqualTo(HorizontalFootprint(initialCapsules) + maximumFootprintGrowthCm),
                "The green capsule exhibit must not spread into loose debris after eight seconds.");
            Assert.That(
                Vector2.Distance(HorizontalCenter(initialCapsules), HorizontalCenter(settledCapsules)),
                Is.LessThanOrEqualTo(maximumCenterDriftCm),
                "The green capsule exhibit must stay at its authored display position.");
            Assert.That(
                MaximumHeight(settledCapsules),
                Is.GreaterThanOrEqualTo(MaximumHeight(initialCapsules) * 0.75f),
                "The green capsule exhibit must retain a visibly layered silhouette.");
        });
    }

    [Test]
    public void BenchmarkScene_RemainsVisiblyInMotionInsteadOfOnlyDroppingOnce()
    {
        Physics3DShowcaseConfig config = CreateShowcaseConfig(
            maximumBodies: 256,
            benchmarkPresets: new[] { 100, 150, 200, 250 },
            replaySteps: 16);
        using var harness = new ShowcaseHarness(config, CreateWorldConfig(320, 32));
        harness.SelectBenchmark(100);

        Vector3[] initial = CaptureDynamicPositions(harness.Runtime, Physics3DShapeKind.Box);
        for (int i = 0; i < 30; i++)
        {
            harness.Step();
        }

        Vector3[] afterOneSecond = CaptureDynamicPositions(harness.Runtime, Physics3DShapeKind.Box);
        int visiblyMoving = 0;
        for (int i = 0; i < initial.Length; i++)
        {
            if (MathF.Abs(afterOneSecond[i].X - initial[i].X) >= config.BodySizeCm)
            {
                visiblyMoving++;
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(
                visiblyMoving,
                Is.GreaterThanOrEqualTo((int)(initial.Length * 0.9f)),
                "The benchmark must remain visibly active instead of reading as one drop followed by a stop.");
            Assert.That(harness.PhysicsWorld.AwakeBodyCount, Is.EqualTo(initial.Length));
        });
    }

    [Test]
    public void DeterminismScene_StaysInCameraAndWaitsForPlayerBeforeReplay()
    {
        Physics3DShowcaseConfig config = CreateShowcaseConfig(
            maximumBodies: 256,
            benchmarkPresets: new[] { 32, 64, 96, 128 },
            replaySteps: 20);
        using var harness = new ShowcaseHarness(config, CreateWorldConfig(320, 32));
        harness.SelectScene(Physics3DShowcaseScene.Determinism);

        Vector3[] initial = CaptureDynamicPositions(harness.Runtime, Physics3DShapeKind.Box);
        Assert.That(
            MaximumHeight(initial),
            Is.LessThanOrEqualTo(3_000f),
            "Replay actors must begin inside the authored camera volume so the recording phase is visible.");

        for (int i = 0; i < config.ReplaySteps; i++)
        {
            harness.Step();
        }

        Assert.Multiple(() =>
        {
            Assert.That(
                harness.Simulation.Enabled,
                Is.False,
                "Recording completion must pause on the rebuilt scene before comparison starts.");
            Assert.That(
                harness.Runtime.ReplayStatus,
                Is.EqualTo(Physics3DShowcaseReplayStatus.ReadyToReplay),
                "Replay comparison must start from an explicit player action rather than an invisible automatic transition.");
        });
    }

    [Test]
    public void DeterminismScene_RebuildsAndReplaysOwnedStateWithoutGlobalStepIndex()
    {
        Physics3DShowcaseConfig config = CreateShowcaseConfig(
            maximumBodies: 256,
            benchmarkPresets: new[] { 32, 64, 96, 128 },
            replaySteps: 40);
        using var harness = new ShowcaseHarness(config, CreateWorldConfig(320, 32));
        harness.SelectScene(Physics3DShowcaseScene.Determinism);

        for (int i = 0; i < config.ReplaySteps; i++)
        {
            harness.Step();
        }

        Assert.That(harness.Runtime.ReplayStatus, Is.EqualTo(Physics3DShowcaseReplayStatus.ReadyToReplay));
        Assert.That(harness.Simulation.Enabled, Is.False);
        harness.StartReplayComparison();

        for (int i = 0; i < config.ReplaySteps + 8 &&
                        harness.Runtime.ReplayStatus is not Physics3DShowcaseReplayStatus.Passed and
                        not Physics3DShowcaseReplayStatus.Failed; i++)
        {
            harness.Step();
        }

        Assert.That(
            harness.Runtime.ReplayStatus,
            Is.EqualTo(Physics3DShowcaseReplayStatus.Passed),
            $"cursor={harness.Runtime.ReplayCursor}, expected={harness.Runtime.ReplayExpectedHash:X16}, actual={harness.Runtime.ReplayActualHash:X16}");
        Assert.That(harness.Simulation.Enabled, Is.False, "A completed replay should pause on its evidence frame.");
    }

    [Test]
    [Category("scale")]
    public void BenchmarkPresets_CreateExactAwakeServerBodyCounts()
    {
        int[] presets = { 1_000, 2_000, 5_000, 10_000 };
        Physics3DShowcaseConfig config = CreateShowcaseConfig(
            maximumBodies: 11_000,
            benchmarkPresets: presets,
            replaySteps: 16);
        using var harness = new ShowcaseHarness(config, CreateWorldConfig(12_000, 256));
        harness.Simulation.Enabled = false;

        for (int i = 0; i < presets.Length; i++)
        {
            harness.Runtime.EnqueueCommand(new Physics3DShowcaseCommand(
                Physics3DShowcaseCommandKind.SetBenchmarkBodies,
                presets[i]));
            harness.Runtime.PrepareFixedStep();
            Assert.That(harness.Runtime.ActiveScene, Is.EqualTo(Physics3DShowcaseScene.Benchmark));
            Assert.That(harness.Runtime.DynamicBodyCount, Is.EqualTo(presets[i]));
            Assert.That(harness.Runtime.BodyCount, Is.EqualTo(presets[i] + 1));
            Assert.That(harness.PhysicsWorld.ActiveMobileBodyCount, Is.EqualTo(presets[i]));
        }
    }

    [Test]
    [Explicit("Allocation gate is run deliberately after the functional suite.")]
    public void BenchmarkSteadyState_ThirtyHzFixedStepDoesNotAllocateOnCallingThread()
    {
        Physics3DShowcaseConfig config = CreateShowcaseConfig(
            maximumBodies: 2_100,
            benchmarkPresets: new[] { 500, 1_000, 1_500, 2_000 },
            replaySteps: 16);
        using var harness = new ShowcaseHarness(config, CreateWorldConfig(2_200, 32));
        harness.SelectBenchmark(2_000);
        const int fixedStepHz = 30;
        int completeTravelSteps = checked((int)MathF.Ceiling(
            (2f * config.BenchmarkTravelHalfWidthCm * fixedStepHz) /
            config.BenchmarkSpeedCmPerSecond));
        for (int i = 0; i < completeTravelSteps + fixedStepHz; i++)
        {
            harness.Step();
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < fixedStepHz; i++)
        {
            harness.Step();
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero);
    }

    private static Physics3DShowcaseConfig CreateShowcaseConfig(
        int maximumBodies,
        int[] benchmarkPresets,
        int replaySteps)
    {
        var config = new Physics3DShowcaseConfig
        {
            MapId = "capability_standard_physics3d_showcase",
            InitialScene = Physics3DShowcaseScene.Stacking,
            MaximumBodies = maximumBodies,
            VisibleBodyLimit = Math.Min(256, maximumBodies),
            PanelRefreshHz = 5,
            FloorSizeCm = 12_000,
            FloorThicknessCm = 40,
            BodySizeCm = 80,
            PyramidRows = 6,
            PyramidCenterXCm = -800,
            PyramidCenterZCm = 0,
            PyramidGapCm = 2,
            SpherePyramidRows = 3,
            SpherePyramidCenterXCm = 1_000,
            SpherePyramidCenterZCm = 0,
            SpherePyramidSpacingCm = 80,
            CapsulePyramidRows = 3,
            CapsulePyramidBaseColumns = 4,
            CapsulePyramidCenterXCm = 2_800,
            CapsulePyramidCenterZCm = 0,
            CapsulePyramidSpacingCm = 80,
            StackingRailThicknessCm = 24,
            StackingRailHeightCm = 60,
            StackingRailClearanceCm = 2,
            ChainLinkCount = 6,
            CcdSpeedCmPerSecond = 15_000,
            QueryDistanceCm = 5_000,
            QueryHitCapacity = 128,
            ContactEventCapacity = 4_096,
            ReplaySteps = replaySteps,
            ReplayGridSize = 6,
            ReplayBodySpacingCm = 180,
            ReplayCenterXCm = 1_000,
            ReplayBaseHeightCm = Math.Max(
                500,
                (int)MathF.Ceiling(0.5f * 981f * MathF.Pow(replaySteps / 30f, 2f)) + 80),
            ReplayLaneOffsetCm = 1_100,
            BenchmarkDefaultBodies = benchmarkPresets[0],
            BenchmarkPresets = benchmarkPresets,
            BenchmarkColumns = 20,
            BenchmarkDepth = 10,
            BenchmarkSpacingCm = 120,
            BenchmarkBaseHeightCm = 1_200,
            BenchmarkRecycleHeightCm = 120,
            BenchmarkTravelHalfWidthCm = 1_300,
            BenchmarkSpeedCmPerSecond = 500,
            ImpactSpeedCmPerSecond = 6_000,
            FrictionCoefficient = 0.8f,
            MaximumRecoveryVelocityCmPerSecond = 200f,
            SpringAngularFrequency = 30f,
            SpringTwiceDampingRatio = 1f
        };
        return config;
    }

    private static Physics3DWorldConfig CreateWorldConfig(int mobileBodies, int staticBodies)
    {
        return new Physics3DWorldConfig
        {
            MobileBodyCapacity = mobileBodies,
            StaticBodyCapacity = staticBodies,
            ShapeCapacity = 256,
            InactiveIslandCapacity = Math.Max(1, mobileBodies),
            ConstraintCapacity = Math.Max(256, mobileBodies * 2),
            ConstraintsPerTypeBatchCapacity = Math.Max(256, mobileBodies),
            ConstraintCountPerBodyEstimate = 8,
            ContactPairCapacityPerWorker = 65_536,
            WorkerCount = 2,
            FixedStepHz = 30,
            MaximumPhysicsStepsPerSourceTick = 1,
            SolverSubstepCount = 1,
            SolverVelocityIterationCount = 8,
            GravityCmPerSecondSquared = new Vector3(0f, -981f, 0f),
            LinearDamping = 0.03f,
            AngularDamping = 0.03f,
            MaximumSpeculativeMarginCm = 10f,
            SleepThreshold = 0.01f,
            MinimumTimestepCountUnderSleepThreshold = 255,
            ContinuousMinimumSweepTimestep = 0.001f,
            ContinuousSweepConvergenceThreshold = 0.001f,
            MaterialCombineMode = Physics3DMaterialCombineMode.GeometricMean
        };
    }

    private static Vector3[] CaptureDynamicPositions(
        Physics3DShowcaseRuntime runtime,
        Physics3DShapeKind expectedShape)
    {
        var positions = new System.Collections.Generic.List<Vector3>();
        for (int i = 0; i < runtime.BodyCount; i++)
        {
            Assert.That(runtime.TryGetBodyVisual(
                i,
                out Physics3DBodyState state,
                out Physics3DBodyKind bodyKind,
                out Physics3DShapeKind shapeKind,
                out _,
                out _,
                out _), Is.True);
            if (bodyKind == Physics3DBodyKind.Dynamic && shapeKind == expectedShape)
            {
                positions.Add(state.PositionCm);
            }
        }

        Assert.That(positions, Is.Not.Empty, $"Stacking must include dynamic {expectedShape} bodies.");
        return positions.ToArray();
    }

    private static float HorizontalFootprint(Vector3[] positions)
    {
        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float minZ = float.PositiveInfinity;
        float maxZ = float.NegativeInfinity;
        for (int i = 0; i < positions.Length; i++)
        {
            minX = MathF.Min(minX, positions[i].X);
            maxX = MathF.Max(maxX, positions[i].X);
            minZ = MathF.Min(minZ, positions[i].Z);
            maxZ = MathF.Max(maxZ, positions[i].Z);
        }

        return MathF.Max(maxX - minX, maxZ - minZ);
    }

    private static Vector2 HorizontalCenter(Vector3[] positions)
    {
        Vector2 total = Vector2.Zero;
        for (int i = 0; i < positions.Length; i++)
        {
            total += new Vector2(positions[i].X, positions[i].Z);
        }

        return total / positions.Length;
    }

    private static float MaximumHeight(Vector3[] positions)
    {
        float maximum = float.NegativeInfinity;
        for (int i = 0; i < positions.Length; i++)
        {
            maximum = MathF.Max(maximum, positions[i].Y);
        }

        return maximum;
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

    private sealed class ShowcaseHarness : IDisposable
    {
        public ShowcaseHarness(Physics3DShowcaseConfig config, Physics3DWorldConfig worldConfig)
        {
            EcsWorld = World.Create();
            PhysicsWorld = new Physics3DWorld(worldConfig);
            Simulation = new Physics3DSimulationSystem(EcsWorld, PhysicsWorld, 30, 1);
            Runtime = new Physics3DShowcaseRuntime();
            Runtime.ActivateForTests(EcsWorld, PhysicsWorld, Simulation, config);
        }

        public World EcsWorld { get; }
        public Physics3DWorld PhysicsWorld { get; }
        public Physics3DSimulationSystem Simulation { get; }
        public Physics3DShowcaseRuntime Runtime { get; }

        public void SelectScene(Physics3DShowcaseScene scene)
        {
            Runtime.EnqueueCommand(new Physics3DShowcaseCommand(
                Physics3DShowcaseCommandKind.SelectScene,
                (int)scene));
            Runtime.PrepareFixedStep();
        }

        public void SelectBenchmark(int bodies)
        {
            Runtime.EnqueueCommand(new Physics3DShowcaseCommand(
                Physics3DShowcaseCommandKind.SetBenchmarkBodies,
                bodies));
            Runtime.PrepareFixedStep();
        }

        public void StartReplayComparison()
        {
            Runtime.EnqueueCommand(new Physics3DShowcaseCommand(
                Physics3DShowcaseCommandKind.StartReplayComparison));
            Runtime.PrepareFixedStep();
        }

        public void Step()
        {
            Runtime.PrepareFixedStep();
            Simulation.Update(1f / 30f);
            Runtime.ObserveFixedStep();
        }

        public void Dispose()
        {
            Runtime.Dispose();
            PhysicsWorld.Dispose();
            EcsWorld.Dispose();
        }
    }
}
