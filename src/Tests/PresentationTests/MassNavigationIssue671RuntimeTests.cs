using System;
using System.Collections.Generic;
using System.IO;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Map;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.MassNavigation.Systems;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation.Avoidance;
using Ludots.Core.Spatial;
using NUnit.Framework;
using Schedulers;
using Ludots.Platform.Abstractions;

namespace Ludots.Tests.Presentation;

[TestFixture]
[NonParallelizable]
public sealed class MassNavigationIssue671RuntimeTests
{
    private const int ScratchCapacityAgents = 10_000;
    private const int ParallelWorkerCount = 4;
    private const int AllocationMeasurementSteps = 128;

    [Test]
    public void BindSpawnedAgent_MissingOrInvalidAgentProfile_FailsBeforeCommit()
    {
        using World world = World.Create();
        MassNavigationSimulationRuntime simulation = CreateSimulation();
        MassNavigationAgentLayer layer = CreateAgentLayer();
        simulation.MassNavigationFlow.ResetAuthoredAgents(new[]
        {
            CreateSeed(localX: 1_000f, localY: 1_000f, layer),
        });

        Entity missingAgent = world.Create(new WorldPositionCm
        {
            Value = Fix64Vec2.FromInt(1_000, 1_000),
        });
        InvalidOperationException missing = Assert.Throws<InvalidOperationException>(() =>
            simulation.BindSpawnedAgent(world, missingAgent, agentIndex: 0, controllable: true))!;

        Entity invalidAgent = world.Create(
            new MassNavigationAgent { ProfileId = MassNavigationProfileRegistry.InvalidId },
            new WorldPositionCm { Value = Fix64Vec2.FromInt(1_000, 1_000) });
        InvalidOperationException invalid = Assert.Throws<InvalidOperationException>(() =>
            simulation.BindSpawnedAgent(world, invalidAgent, agentIndex: 0, controllable: true))!;

        Assert.Multiple(() =>
        {
            Assert.That(missing.Message, Does.Contain("MassNavigationAgent"));
            Assert.That(invalid.Message, Does.Contain("positive profileId"));
            Assert.That(world.Has<MassNavigationAgentIndex>(missingAgent), Is.False);
            Assert.That(world.Has<MassNavigationAgentProfile>(missingAgent), Is.False);
            Assert.That(world.Has<MassNavigationAgentIndex>(invalidAgent), Is.False);
            Assert.That(world.Has<MassNavigationAgentProfile>(invalidAgent), Is.False);
        });
    }

    [Test]
    public void BindSpawnedAgent_DuplicateAgentIndex_FailsBeforeEcsCommit()
    {
        using World world = World.Create();
        MassNavigationSimulationRuntime simulation = CreateSimulation();
        MassNavigationAgentLayer layer = CreateAgentLayer();
        simulation.MassNavigationFlow.ResetAuthoredAgents(new[]
        {
            CreateSeed(localX: 1_000f, localY: 1_000f, layer),
        });
        int profileId = MassNavigationProfileRegistry.Register("light");
        Entity first = world.Create(new MassNavigationAgent { ProfileId = profileId });
        Entity duplicate = world.Create(new MassNavigationAgent { ProfileId = profileId });

        simulation.BindSpawnedAgent(world, first, agentIndex: 0, controllable: true);
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            simulation.BindSpawnedAgent(world, duplicate, agentIndex: 0, controllable: true))!;

        Assert.Multiple(() =>
        {
            Assert.That(ex.Message, Does.Contain("already registered"));
            Assert.That(world.Has<MassNavigationAgentIndex>(duplicate), Is.False);
            Assert.That(world.Has<MassNavigationAgentProfile>(duplicate), Is.False);
            Assert.That(simulation.AgentState.TryGetAgentEntity(0, out Entity registered), Is.True);
            Assert.That(registered, Is.EqualTo(first));
        });
    }

    [Test]
    public void PreallocatedAvoidanceScratch_IsSizedByWorkersInsteadOfAgents()
    {
        MassNavigationConfig config = MassNavigationOrderChainTests.CreateConfigForTests();
        config.Solver.ParallelWorkerCount = ParallelWorkerCount;
        var flow = new MassNavigationFlowSolverState(config.Solver);

        flow.PreallocateAgentCapacity(ScratchCapacityAgents);

        Assert.Multiple(() =>
        {
            Assert.That(
                flow.AvoidanceNeighborScratchCapacity,
                Is.EqualTo(ParallelWorkerCount * MassNavigationFlowAvoidanceTuning.MaxKernelNeighbors));
            Assert.That(
                flow.OrcaLineScratchCapacity,
                Is.EqualTo(ParallelWorkerCount * MassNavigationFlowAvoidanceTuning.MaxKernelNeighbors));
            Assert.That(
                flow.OrcaProjectionLineScratchCapacity,
                Is.EqualTo(ParallelWorkerCount * OrcaSolver2D.MaxProjectionLines));
            Assert.That(
                flow.SonarIntervalScratchCapacity,
                Is.EqualTo(ParallelWorkerCount * SonarSolver2D.MaxIntervals));
        });
    }

    [TestCase("Orca")]
    [TestCase("Sonar")]
    public void WorkerLocalAvoidanceScratch_PreservesSerialAndParallelResults(string mode)
    {
        World.SharedJobScheduler ??= new JobScheduler(new JobScheduler.Config
        {
            ThreadPrefixName = "MassNavIssue671",
            ThreadCount = 0,
            MaxExpectedConcurrentJobs = 64,
            StrictAllocationMode = false,
        });

        MassNavigationAgentSeed[] seeds = CreateAvoidanceSeeds(agentCount: 32);
        MassNavigationFlowSolverState serial = CreateAvoidanceFlow(workerCount: 1, mode, seeds);
        MassNavigationFlowSolverState parallel = CreateAvoidanceFlow(ParallelWorkerCount, mode, seeds);
        MassNavigationGroupRuntime serialGroups = CreateGroupRuntime(seeds.Length);
        MassNavigationGroupRuntime parallelGroups = CreateGroupRuntime(seeds.Length);
        using World world = World.Create();

        for (int i = 0; i < seeds.Length; i++)
        {
            serial.SetUnitTarget(i, 8_500f, 5_000f + ((i % 4) * 30f), resetRecovery: true);
            parallel.SetUnitTarget(i, 8_500f, 5_000f + ((i % 4) * 30f), resetRecovery: true);
        }

        for (int step = 0; step < 8; step++)
        {
            serial.Step(1f / 60f, world, serialGroups, runHardResolve: false, hardResolveCandidateThresholdAgents: 1);
            parallel.Step(1f / 60f, world, parallelGroups, runHardResolve: false, hardResolveCandidateThresholdAgents: 1);
        }

        for (int i = 0; i < seeds.Length; i++)
        {
            Assert.Multiple(() =>
            {
                Assert.That(parallel.GetPositionX(i), Is.EqualTo(serial.GetPositionX(i)).Within(0.0001f));
                Assert.That(parallel.GetPositionY(i), Is.EqualTo(serial.GetPositionY(i)).Within(0.0001f));
            });
        }
    }

    [Test]
    public void SimulationStep_AfterWarmup_AllocatesZeroBytesForTelemetryCallbacks()
    {
        MassNavigationSimulationRuntime simulation = CreateReadySimulation(out GameEngine engine);
        using (engine)
        {
            MassNavigationAgentLayer layer = CreateAgentLayer();
            int profileId = MassNavigationProfileRegistry.Register("light");
            Entity entity = engine.World.Create(
                new MassNavigationAgent { ProfileId = profileId },
                new WorldPositionCm { Value = Fix64Vec2.FromInt(1_000, 1_000) });
            simulation.RebuildFromAuthoredAgents(
                engine.World,
                new[] { entity },
                new[] { CreateSeed(localX: 1_000f, localY: 1_000f, layer) },
                new[] { true });
            var system = new MassNavigationSimulationStepSystem(engine);
            float dt = 1f / 60f;

            for (int i = 0; i < 16; i++)
            {
                system.Update(in dt);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < AllocationMeasurementSteps; i++)
            {
                system.Update(in dt);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero, "Fixed-step telemetry observer wiring must not allocate after warmup.");
        }
    }

    [Test]
    public void SimulationRuntime_ResolvesInitialHotZoneWithoutMutatingWorldConfig()
    {
        MassNavigationConfig config = MassNavigationOrderChainTests.CreateConfigForTests();
        MassNavigationWorldConfig worldConfig = config.World
            ?? throw new InvalidOperationException("Test config requires MassNavigation world config.");
        string authoredId = worldConfig.ActiveHotZoneId;
        MassNavigationHotZoneConfig authoredZone = worldConfig.GetRequiredHotZone(authoredId);

        var simulation = new MassNavigationSimulationRuntime(config);

        worldConfig.ActiveHotZoneId = "mutated";
        authoredZone.Id = "mutated";
        authoredZone.Label = "Mutated";
        authoredZone.CenterXCm = -1;
        authoredZone.CenterYCm = -2;

        Assert.Multiple(() =>
        {
            Assert.That(worldConfig.ActiveHotZoneId, Is.EqualTo("mutated"));
            Assert.That(simulation.ActiveHotZoneId, Is.EqualTo(authoredId));
            Assert.That(simulation.ActiveHotZoneLabel, Is.EqualTo("Center"));
            Assert.That(simulation.ActiveHotZoneCenterXCm, Is.EqualTo(5_000));
            Assert.That(simulation.ActiveHotZoneCenterYCm, Is.EqualTo(5_000));
            Assert.That(simulation.SolverWindowCenterXCm, Is.EqualTo(5_000f));
            Assert.That(simulation.SolverWindowCenterYCm, Is.EqualTo(5_000f));
        });
    }

    private static MassNavigationSimulationRuntime CreateSimulation()
    {
        return new MassNavigationSimulationRuntime(MassNavigationOrderChainTests.CreateConfigForTests());
    }

    private static MassNavigationSimulationRuntime CreateReadySimulation(out GameEngine engine)
    {
        engine = new GameEngine();
        string repoRoot = FindRepoRoot();
        engine.InitializeWithConfigPipeline(
            new List<string> { Path.Combine(repoRoot, "mods", "LudotsCoreMod") },
            Path.Combine(repoRoot, "assets"));

        MassNavigationConfig config = MassNavigationOrderChainTests.CreateConfigForTests();
        var simulation = new MassNavigationSimulationRuntime(config);
        simulation.BindBoardWorld(
            new WorldSizeSpec(new WorldAabbCm(0, 0, 25_000, 25_000), 100),
            MassNavigationOrderChainTests.CreateLoadedChunksForTests(simulation));
        var session = new MapSession(new MapId(config.MapId), new MapConfig { Id = config.MapId });
        engine.SetCurrentMapSessionForTests(session);
        var binding = new MassNavigationRuntimeBinding();
        binding.Activate(session.MapId, simulation);
        binding.MarkPrepared(session.MapId, simulation);
        engine.SetService(MassNavigationKeys.RuntimeBinding, binding);
        return simulation;
    }

    [Test]
    public void SetUnitRuntimeProfile_UnregisteredTeamLayerCombination_RejectsHotPathAllocation()
    {
        MassNavigationAgentLayer registeredLayer = CreateAgentLayer();
        MassNavigationAgentLayer unregisteredLayer = new(categoryMask: 1u << 3, interactionMask: 1u << 3);
        MassNavigationFlowSolverState flow = CreateAvoidanceFlow(
            workerCount: 1,
            mode: "Orca",
            seeds: new[]
            {
                CreateSeed(localX: 1_000f, localY: 1_000f, registeredLayer),
            });

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            flow.SetUnitRuntimeProfile(
                index: 0,
                teamId: 1,
                navMass: 1f,
                visualScale: 1f,
                bodyRadiusCm: 20f,
                speedCmPerSecond: 800f,
                layer: unregisteredLayer))!;

        Assert.That(ex.Message, Does.Contain("SetUnitRuntimeProfile must not allocate"));
        Assert.That(ex.Message, Does.Contain("ResetAuthoredAgents"));
        Assert.That(ex.Message, Does.Contain("AppendAuthoredAgents"));
    }

    private static MassNavigationFlowSolverState CreateAvoidanceFlow(
        int workerCount,
        string mode,
        ReadOnlySpan<MassNavigationAgentSeed> seeds)
    {
        MassNavigationConfig config = MassNavigationOrderChainTests.CreateConfigForTests();
        config.Solver.ParallelWorkerCount = workerCount;
        config.Avoidance.Mode = mode;
        config.Avoidance.Validate();
        var flow = new MassNavigationFlowSolverState(config.Solver);
        flow.ArrivalTuning.CopyFrom(config.Arrival);
        flow.AvoidanceTuning.CopyFrom(config.Avoidance);
        flow.Semantics.CopyFrom(config.Semantics);
        flow.Semantics.Solver.ParallelStepMinAgents = 2;
        flow.PreallocateAgentCapacity(seeds.Length);
        flow.ResetAuthoredAgents(seeds);
        return flow;
    }

    private static MassNavigationGroupRuntime CreateGroupRuntime(int agentCapacity)
    {
        MassNavigationConfig config = MassNavigationOrderChainTests.CreateConfigForTests();
        config.ScenarioRuntime.RuntimeCapacity.GroupMembershipAgentCapacity = agentCapacity;
        config.ScenarioRuntime.RuntimeCapacity.GroupMemberCapacity = agentCapacity;
        return new MassNavigationGroupRuntime(config.Semantics.Group, config.ScenarioRuntime.RuntimeCapacity);
    }

    private static MassNavigationAgentSeed[] CreateAvoidanceSeeds(int agentCount)
    {
        MassNavigationAgentLayer layer = CreateAgentLayer();
        var seeds = new MassNavigationAgentSeed[agentCount];
        for (int i = 0; i < seeds.Length; i++)
        {
            seeds[i] = CreateSeed(
                localX: 1_000f + ((i % 8) * 45f),
                localY: 5_000f + ((i / 8) * 45f),
                layer);
        }

        return seeds;
    }

    private static MassNavigationAgentSeed CreateSeed(
        float localX,
        float localY,
        MassNavigationAgentLayer layer)
    {
        return new MassNavigationAgentSeed(
            teamId: 1,
            localPositionXCm: localX,
            localPositionYCm: localY,
            heavy: false,
            navMass: 1f,
            visualScale: 1f,
            bodyRadiusCm: 20f,
            speedCmPerSecond: 800f,
            layer);
    }

    private static MassNavigationAgentLayer CreateAgentLayer()
    {
        return new MassNavigationAgentLayer(categoryMask: 1u, interactionMask: 1u);
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src", "Core")) &&
                Directory.Exists(Path.Combine(current.FullName, "mods", "LudotsCoreMod")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Ludots repository root.");
    }
}
