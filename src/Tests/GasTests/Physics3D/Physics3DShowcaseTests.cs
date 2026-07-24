using System;
using System.IO;
using System.Numerics;
using System.Text.Json.Nodes;
using Arch.Core;
using CapabilityStandardPhysics3DShowcaseMod.Runtime;
using Ludots.Core.Character3D;
using Ludots.Core.Physics3D;
using Ludots.Core.Traversal3D;
using NUnit.Framework;

namespace Ludots.Tests.Physics3D;

[TestFixture]
[NonParallelizable]
public sealed class Physics3DShowcaseTests
{
    [Test]
    public void ShowcaseConfig_IsStrictAndOfficialPresetsFitOwnedCapacity()
    {
        JsonObject json = LoadOfficialShowcaseJson();

        Physics3DShowcaseConfig config = Physics3DShowcaseConfig.Load(json);
        Assert.That(config.InitialScene, Is.EqualTo(Physics3DShowcaseScene.ScannerRange));
        Assert.That(config.MaximumBodies, Is.EqualTo(50_001));
        Assert.That(config.BenchmarkPresets, Is.EqualTo(new[] { 1_000, 10_000, 25_000, 50_000 }));

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
    public void AllPlayerScenes_CreateRunAndExposeTheirCapabilityEvidence()
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

        harness.SelectScene(Physics3DShowcaseScene.ScannerRange);
        harness.Step();
        int totalQueryHits = 0;
        for (int i = 0; i < 7; i++)
        {
            totalQueryHits += harness.Runtime.GetQueryHitCount(i);
        }

        Assert.That(totalQueryHits, Is.GreaterThan(0));

        harness.SelectScene(Physics3DShowcaseScene.ConstraintForge);
        Assert.That(harness.Runtime.ConstraintCount, Is.GreaterThan(0));
        Assert.That(harness.PhysicsWorld.ActiveConstraintCount, Is.EqualTo(harness.Runtime.ConstraintCount));
    }

    [Test]
    public void PlatformStation_PlayerStartsSupportedThenMovesAndJumps()
    {
        Physics3DShowcaseConfig config = CreateShowcaseConfig(
            maximumBodies: 1_200,
            benchmarkPresets: new[] { 100, 200, 500, 1_000 },
            replaySteps: 30);
        using var harness = new ShowcaseHarness(config, CreateWorldConfig(1_300, 256));
        harness.SelectScene(Physics3DShowcaseScene.PlatformStation);
        Character3DState initial = harness.Runtime.GetPlayerCharacterStateForTests();

        for (int i = 0; i < 6; i++)
        {
            harness.Runtime.SetCharacterIntentForTests(Vector2.UnitX, jumpRequested: false, traverseRequested: false);
            harness.Step();
        }

        Character3DState moved = harness.Runtime.GetPlayerCharacterStateForTests();
        harness.Runtime.SetCharacterIntentForTests(Vector2.UnitX, jumpRequested: true, traverseRequested: false);
        harness.Step();
        Character3DState jumped = harness.Runtime.GetPlayerCharacterStateForTests();

        Assert.Multiple(() =>
        {
            Assert.That(initial.IsGrounded, Is.True, "The player must begin on top of the authored start deck.");
            Assert.That(moved.PositionCm.X, Is.GreaterThan(initial.PositionCm.X + config.BodySizeCm));
            Assert.That(jumped.LinearVelocityCmPerSecond.Y, Is.GreaterThan(0f));
        });
    }

    [Test]
    public void PlatformStation_UsesFormalConveyorAndOneWayPlatformPolicies()
    {
        Physics3DShowcaseConfig config = CreateShowcaseConfig(
            maximumBodies: 1_200,
            benchmarkPresets: new[] { 100, 200, 500, 1_000 },
            replaySteps: 30);
        using var harness = new ShowcaseHarness(config, CreateWorldConfig(1_300, 256));
        harness.SelectScene(Physics3DShowcaseScene.PlatformStation);

        var bodyIds = new Physics3DBodyId[harness.PhysicsWorld.ActiveBodyCount];
        int bodyCount = harness.PhysicsWorld.CopyActiveBodyIds(bodyIds);
        int conveyorCount = 0;
        int oneWayCount = 0;
        for (int i = 0; i < bodyCount; i++)
        {
            Physics3DBodyContactPolicy policy = harness.PhysicsWorld.GetBodyContactPolicy(bodyIds[i]);
            if (policy.Kind == Physics3DBodyContactPolicyKind.SurfaceVelocity)
            {
                conveyorCount++;
                Assert.That(
                    policy.LocalSurfaceVelocityCmPerSecond,
                    Is.EqualTo(new Vector3(config.CharacterTraversal.PlatformStationConveyorSpeedCmPerSecond, 0f, 0f)));
            }
            else if (policy.Kind == Physics3DBodyContactPolicyKind.OneWayPlatform)
            {
                oneWayCount++;
                Assert.That(policy.LocalPlatformNormal, Is.EqualTo(Vector3.UnitY));
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(conveyorCount, Is.EqualTo(1), "Platform Station must contain one real surface-velocity conveyor.");
            Assert.That(oneWayCount, Is.EqualTo(1), "Platform Station must contain one real one-way platform.");
        });
    }

    [Test]
    public void TraversalCourse_PlayerRunsToLadderAttachesAndClimbs()
    {
        Physics3DShowcaseConfig config = CreateShowcaseConfig(
            maximumBodies: 1_200,
            benchmarkPresets: new[] { 100, 200, 500, 1_000 },
            replaySteps: 30);
        using var harness = new ShowcaseHarness(config, CreateWorldConfig(1_300, 256));
        harness.SelectScene(Physics3DShowcaseScene.TraversalCourse);

        float attachReadyX = config.CharacterTraversal.LadderCenterXCm -
                             (config.CharacterTraversal.AttachProbeDistanceCm * 0.9f);
        int steps = 0;
        while (harness.Runtime.GetPlayerCharacterStateForTests().PositionCm.X < attachReadyX && steps < 300)
        {
            harness.Runtime.SetCharacterIntentForTests(Vector2.UnitX, jumpRequested: false, traverseRequested: false);
            harness.Step();
            steps++;
        }

        Character3DState atLadder = harness.Runtime.GetPlayerCharacterStateForTests();
        Assert.That(atLadder.PositionCm.X, Is.GreaterThanOrEqualTo(attachReadyX),
            $"The authored route must let a new player reach the ladder without teleporting. " +
            $"position={atLadder.PositionCm}, velocity={atLadder.LinearVelocityCmPerSecond}, " +
            $"grounded={atLadder.IsGrounded}, stepAssist={atLadder.StepAssistActive}, steps={steps}.");

        harness.Runtime.SetCharacterIntentForTests(Vector2.Zero, jumpRequested: false, traverseRequested: true);
        harness.Step();
        Assert.That(
            harness.Runtime.GetPlayerTraversalStatusForTests().State,
            Is.EqualTo(Traversal3DState.Attached));

        harness.Runtime.SetCharacterIntentForTests(Vector2.Zero, jumpRequested: false, traverseRequested: false);
        harness.Step();
        Assert.That(
            harness.Runtime.GetPlayerTraversalStatusForTests().State,
            Is.EqualTo(Traversal3DState.Climbing));

        harness.Runtime.SetCharacterIntentForTests(Vector2.UnitY, jumpRequested: false, traverseRequested: false);
        harness.Step();

        int ladderClimbSteps = 0;
        while (harness.Runtime.GetPlayerTraversalStatusForTests().State != Traversal3DState.LedgeHang &&
               ladderClimbSteps < 120)
        {
            harness.Runtime.SetCharacterIntentForTests(Vector2.UnitY, jumpRequested: false, traverseRequested: false);
            harness.Step();
            ladderClimbSteps++;
        }

        Assert.That(
            harness.Runtime.GetPlayerTraversalStatusForTests().State,
            Is.EqualTo(Traversal3DState.LedgeHang),
            "Climbing the ladder must reach a validated ledge hang instead of stopping below the deck.");
        harness.Runtime.SetCharacterIntentForTests(Vector2.UnitY, jumpRequested: false, traverseRequested: false);
        harness.Step();
        Assert.That(harness.Runtime.GetPlayerTraversalStatusForTests().State, Is.EqualTo(Traversal3DState.Mantling));

        int ladderMantleSteps = 0;
        while (harness.Runtime.GetPlayerTraversalStatusForTests().State != Traversal3DState.NormalMovement &&
               ladderMantleSteps < 120)
        {
            harness.Runtime.SetCharacterIntentForTests(Vector2.UnitY, jumpRequested: false, traverseRequested: false);
            harness.Step();
            ladderMantleSteps++;
        }

        Character3DState afterLadder = harness.Runtime.GetPlayerCharacterStateForTests();
        Assert.Multiple(() =>
        {
            Assert.That(harness.Runtime.GetPlayerTraversalStatusForTests().State, Is.EqualTo(Traversal3DState.NormalMovement));
            Assert.That(afterLadder.PositionCm.Y, Is.GreaterThan(config.CharacterTraversal.LadderDeckCenterYCm));
        });

        float wallAttachReadyX = config.CharacterTraversal.WallCenterXCm -
                                 (config.CharacterTraversal.AttachProbeDistanceCm * 0.9f);
        bool gapJumped = false;
        int wallApproachSteps = 0;
        while (harness.Runtime.GetPlayerCharacterStateForTests().PositionCm.X < wallAttachReadyX &&
               wallApproachSteps < 180)
        {
            float x = harness.Runtime.GetPlayerCharacterStateForTests().PositionCm.X;
            bool jump = !gapJumped &&
                        x >= config.CharacterTraversal.LadderDeckCenterXCm +
                             (config.CharacterTraversal.LadderDeckLengthCm * 0.3f);
            gapJumped |= jump;
            harness.Runtime.SetCharacterIntentForTests(Vector2.UnitX, jump, traverseRequested: false);
            harness.Step();
            wallApproachSteps++;
        }

        Assert.That(
            harness.Runtime.GetPlayerCharacterStateForTests().PositionCm.X,
            Is.GreaterThanOrEqualTo(wallAttachReadyX),
            "The authored deck gap must be jumpable on the way from the ladder to the climbing wall.");
        harness.Runtime.SetCharacterIntentForTests(Vector2.Zero, jumpRequested: false, traverseRequested: true);
        harness.Step();
        Assert.That(harness.Runtime.GetPlayerTraversalStatusForTests().State, Is.EqualTo(Traversal3DState.Attached));
        harness.Runtime.SetCharacterIntentForTests(Vector2.Zero, jumpRequested: false, traverseRequested: false);
        harness.Step();
        Assert.That(harness.Runtime.GetPlayerTraversalStatusForTests().State, Is.EqualTo(Traversal3DState.Climbing));

        int wallClimbSteps = 0;
        while (harness.Runtime.GetPlayerTraversalStatusForTests().State != Traversal3DState.LedgeHang &&
               wallClimbSteps < 150)
        {
            harness.Runtime.SetCharacterIntentForTests(Vector2.UnitY, jumpRequested: false, traverseRequested: false);
            harness.Step();
            wallClimbSteps++;
        }

        Assert.That(
            harness.Runtime.GetPlayerTraversalStatusForTests().State,
            Is.EqualTo(Traversal3DState.LedgeHang),
            "Climbing the wall must finish at the authored ledge.");
        harness.Runtime.SetCharacterIntentForTests(Vector2.UnitY, jumpRequested: false, traverseRequested: false);
        harness.Step();
        int wallMantleSteps = 0;
        while (harness.Runtime.GetPlayerTraversalStatusForTests().State != Traversal3DState.NormalMovement &&
               wallMantleSteps < 120)
        {
            harness.Runtime.SetCharacterIntentForTests(Vector2.UnitY, jumpRequested: false, traverseRequested: false);
            harness.Step();
            wallMantleSteps++;
        }

        Assert.Multiple(() =>
        {
            Assert.That(harness.Runtime.GetPlayerTraversalStatusForTests().State, Is.EqualTo(Traversal3DState.NormalMovement));
            Assert.That(
                harness.Runtime.GetPlayerCharacterStateForTests().PositionCm.Y,
                Is.GreaterThan(config.CharacterTraversal.WallDeckCenterYCm));
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
        bool observedRelaunch = false;
        for (int i = 0; i < config.BenchmarkCycleSteps + 30; i++)
        {
            harness.Step();
            observedRelaunch |= harness.Runtime.BenchmarkRecycledBodiesLastStep > 0;
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
            Assert.That(observedRelaunch, Is.True, "The benchmark never relaunched its completed stream wave.");
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
        harness.SelectScene(Physics3DShowcaseScene.ReplayTheater);

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
        harness.SelectScene(Physics3DShowcaseScene.ReplayTheater);

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
        int[] presets = { 1_000, 10_000, 25_000, 50_000 };
        Physics3DShowcaseConfig config = CreateShowcaseConfig(
            maximumBodies: 50_001,
            benchmarkPresets: presets,
            replaySteps: 16);
        using var harness = new ShowcaseHarness(config, CreateWorldConfig(52_000, 256));
        harness.Simulation.Enabled = false;

        for (int i = 0; i < presets.Length; i++)
        {
            harness.Runtime.EnqueueCommand(new Physics3DShowcaseCommand(
                Physics3DShowcaseCommandKind.SetBenchmarkBodies,
                presets[i]));
            harness.Runtime.PrepareFixedStep();
            Assert.That(harness.Runtime.ActiveScene, Is.EqualTo(Physics3DShowcaseScene.ScaleCity));
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
        Physics3DShowcaseConfig config = Physics3DShowcaseConfig.Load(LoadOfficialShowcaseJson());
        config.MaximumBodies = maximumBodies;
        config.VisibleBodyLimit = Math.Min(256, maximumBodies);
        config.PyramidRows = 6;
        config.SpherePyramidRows = 3;
        config.CapsulePyramidRows = 3;
        config.CapsulePyramidBaseColumns = 4;
        config.ChainLinkCount = 6;
        config.ReplaySteps = replaySteps;
        config.ReplayBaseHeightCm = Math.Max(
            500,
            (int)MathF.Ceiling(0.5f * 981f * MathF.Pow(replaySteps / 30f, 2f)) + config.BodySizeCm);
        config.BenchmarkDefaultBodies = benchmarkPresets[0];
        config.BenchmarkPresets = benchmarkPresets;
        config.BenchmarkLaneDecks = (benchmarkPresets[^1] + config.BenchmarkLaneColumns - 1) /
                                    config.BenchmarkLaneColumns;
        return config;
    }

    private static JsonObject LoadOfficialShowcaseJson()
    {
        string path = Path.Combine(
            FindRepoRoot(),
            "mods",
            "showcases",
            "capability_standard",
            "CapabilityStandardPhysics3DShowcaseMod",
            "assets",
            "CapabilityStandardPhysics3DShowcaseConfig.json");
        return JsonNode.Parse(File.ReadAllText(path))?.AsObject()
            ?? throw new InvalidOperationException("Physics3D showcase config is missing.");
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
            ActuationCommandCapacity = Math.Max(256, mobileBodies * 2),
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
            Step();
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
            CompletePreparedStep();
        }

        private void CompletePreparedStep()
        {
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
