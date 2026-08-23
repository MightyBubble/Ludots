using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using Arch.Core;
using CoreInputMod.Systems;
using DynamicNavBakeShowcaseMod;
using DynamicNavBakeShowcaseMod.Runtime;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Engine.Pacemaker;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.MovePlanning;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Surface;
using Ludots.Core.Navigation.Pathing;
using Ludots.Core.Navigation.Pathing.Config;
using Ludots.Platform.Abstractions;
using Ludots.Core.Presentation.Minimap;
using Ludots.Core.Presentation.Navigation;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Knowledge;
using Ludots.Core.Scripting;
using Ludots.NavBake.Recast;
using Ludots.UI;
using Ludots.UI.Input;
using Ludots.UI.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

[TestFixture]
[NonParallelizable]
[Category("acceptance")]
public sealed class DynamicNavBakeShowcaseAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;

    private static readonly string[] SharedMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "CameraProfilesMod",
        "MassNavigationMod",
        "DynamicNavBakeShowcaseMod",
    };

    // Feature: RTS dynamic fortress — close the gate, then restore a route
    // Given a player is in the RTS fort showcase with a selected squad and a goal flag
    // When they switch bake algorithm, deploy, seal the central gate, and command a move
    // Then the direct corridor is blocked or diverted, demolition restores a usable route, and units actually move
    [TestCase(NavBakeAlgorithmKind.Recast)]
    [TestCase(NavBakeAlgorithmKind.Cdt)]
    [TestCase(NavBakeAlgorithmKind.LayeredSpan)]
    public void Feature_RtsDynamicFortress_CloseGateThenSideRoute_ReachesGoal(NavBakeAlgorithmKind algorithm)
    {
        Stopwatch wall = Stopwatch.StartNew();
        using GameEngine engine = CreateEngine("NavBakeDynamicRtsShowcaseMod", registerRecast: algorithm == NavBakeAlgorithmKind.Recast);
        engine.LoadMap(DynamicNavBakeShowcaseIds.RtsMapId);
        Assert.That(engine.LastNavBootstrapUriResolveCount, Is.EqualTo(0));

        DynamicNavBakeShowcaseActions actions = WaitForActions(engine, DynamicNavBakeShowcaseIds.RtsMapId);
        DrainSpawnAndNavBootstrap(engine, actions);

        engine.TryGetService(CoreServiceKeys.NavTriangleSurface, out NavTriangleSurfaceTileIndex triangleSurface);
        Assert.That(triangleSurface?.Surface.TriangleCount, Is.EqualTo(128));

        AssertAlgorithmSwitch(engine, actions, algorithm);
        Assert.That(actions.SquadDeployed, Is.True);
        AssertPathingHumanoidMapsToLight(engine);
        Assert.That(actions.TryCommandMoveToGoal(engine, out _), Is.True, "Initial RTS route should be ready before the gate seals.");
        AssertFormalNavMeshRouteEventually(engine, actions, expectedSquadCount: actions.ActiveConfig.Squad.Count);
        int openPathPoints = actions.LastPathPointCount;
        Assert.That(openPathPoints, Is.GreaterThan(1));
        ulong openRouteSignature = actions.CaptureEvidence(engine).PlayerRouteSignature;
        Assert.That(openRouteSignature, Is.Not.EqualTo(0UL), "Initial RTS formal route geometry signature must be nonzero.");

        var positionsBefore = CaptureSquadPositions(engine, actions);
        Assert.That(actions.TryStageBuilding(engine, out string stageBuildingError), Is.True, stageBuildingError);
        Assert.That(actions.HasStagedEdit, Is.True);
        Assert.That(actions.WallDeployedCount, Is.EqualTo(0), "Building placement remains a preview until Bake.");
        Assert.That(actions.TryBake(engine, out string buildBakeError), Is.True, buildBakeError);
        actions.DrainUntilIdle(engine, maxTicks: 4096);
        Assert.That(actions.PlayerNavState, Is.EqualTo(DynamicNavBakePlayerNavState.RouteUpdated));
        Assert.That(actions.WallDeployedCount, Is.GreaterThan(0));
        Assert.That(actions.TryCommandMoveToGoal(engine, out string blockedMoveError), Is.True, blockedMoveError);
        Assert.That(actions.LastPathStatus, Is.EqualTo(NavPathStatus.Ok), "Sealed gate must leave a usable side route for all three bake algorithms.");
        Assert.That(actions.LastPathPointCount, Is.GreaterThan(1), "Sealed side route must remain a multi-point path.");
        AssertFormalNavMeshRouteEventually(engine, actions, expectedSquadCount: actions.ActiveConfig.Squad.Count);
        ulong sealedSignature = actions.CaptureEvidence(engine).PlayerRouteSignature;
        Assert.That(sealedSignature, Is.Not.EqualTo(0UL), "Sealed RTS formal route geometry signature must be nonzero.");
        Assert.That(
            sealedSignature,
            Is.Not.EqualTo(openRouteSignature),
            "Sealed side-route geometry signature must differ from the initial open corridor (Recast may keep the same waypoint count).");

        Assert.That(actions.TryRestore(engine, out string stageRestoreError), Is.True, stageRestoreError);
        Assert.That(actions.HasStagedEdit, Is.True);
        Assert.That(actions.WallDeployedCount, Is.GreaterThan(0), "Restore remains a preview until Bake.");
        Assert.That(actions.TryBake(engine, out string restoreBakeError), Is.True, restoreBakeError);
        actions.DrainUntilIdle(engine, maxTicks: 4096);
        Assert.That(actions.PlayerNavState, Is.EqualTo(DynamicNavBakePlayerNavState.RouteUpdated));
        Assert.That(actions.WallDeployedCount, Is.EqualTo(0));
        Assert.That(actions.TryCommandMoveToGoal(engine, out _), Is.True, "Opened gate or side route should restore reachability.");
        AssertFormalNavMeshRouteEventually(engine, actions);
        TickForMotion(engine, actions, ticks: 180);
        var positionsAfter = CaptureSquadPositions(engine, actions);
        AssertSquadMoved(positionsBefore, positionsAfter);

        DynamicNavBakeShowcaseEvidence evidence = actions.CaptureEvidence(engine);
        AssertEvidence(evidence, algorithm, expectedTriangleSnapshotCount: 128, expectCorridor: false);
        AssertStructuralStability(engine, actions);
        TestContext.WriteLine($"RTS/{algorithm} elapsed={wall.Elapsed}");
        Assert.That(wall.Elapsed, Is.LessThan(TimeSpan.FromMinutes(3)), "Single RTS algorithm scenario must stay bounded.");
    }

    // Feature: structural nav change cancels outstanding formal move
    // Given a deployed RTS squad with an active formal MassNavigation route through the fort
    // When the player seals then demolishes the gate (topology changes under an outstanding march)
    // Then outstanding formal move orders and route-sink state are cleared before any rebuild tick
    // And draining the rebuild does not keep a live formal route that can SolveFailed/NoPath mid-generation
    // And the player can issue a new move after the opened generation commits
    [Test]
    public void Feature_RtsStructuralNavChange_CancelsOutstandingFormalMoveBeforeRebuildTicks()
    {
        Stopwatch wall = Stopwatch.StartNew();
        using GameEngine engine = CreateEngine("NavBakeDynamicRtsShowcaseMod", registerRecast: true);
        engine.LoadMap(DynamicNavBakeShowcaseIds.RtsMapId);
        DynamicNavBakeShowcaseActions actions = WaitForActions(engine, DynamicNavBakeShowcaseIds.RtsMapId);
        DrainSpawnAndNavBootstrap(engine, actions);
        AssertAlgorithmSwitch(engine, actions, NavBakeAlgorithmKind.Recast);

        Assert.That(actions.SquadDeployed, Is.True);
        Assert.That(actions.TryCommandMoveToGoal(engine, out string moveError), Is.True, moveError);
        AssertFormalNavMeshRouteEventually(engine, actions);
        Assert.That(CountActiveSquadMoveOrders(engine), Is.GreaterThan(0));
        Assert.That(actions.CaptureEvidence(engine).FormalRouteAgentCount, Is.GreaterThan(0));

        RuntimeIncrementalNavMeshRebuildQueue queue = engine.GetService(CoreServiceKeys.RuntimeNavMeshRebuildQueue)!;
        ulong generationBeforeBuild = actions.CaptureEvidence(engine).LastGeneration;
        Assert.That(actions.TryBuildWall(engine, out _), Is.True);
        Assert.That(
            actions.CaptureEvidence(engine).FormalRouteAgentCount,
            Is.EqualTo(0),
            "Building the gate must cancel outstanding formal MassNavigation routes before any rebuild tick.");
        Assert.That(CountActiveSquadMoveOrders(engine), Is.EqualTo(0));
        Assert.That(
            CountSquadMovePlanIntentsWithTarget(engine, actions),
            Is.EqualTo(0),
            "Building the gate must clear MovePlanExecutionIntent atomically with the formal cancel.");
        Assert.That(
            engine.GetService(MassNavigationKeys.RouteExecutionSink)?.ActiveRouteCount ?? 0,
            Is.EqualTo(0),
            "Route sink state for the showcase squad must be released with the cancelled formal move.");

        Assert.DoesNotThrow(
            () => actions.DrainUntilIdle(engine, maxTicks: 4096),
            "Rebuild ticks before the sealed generation commits must not treat NoPath as a fatal live route retry.");
        Assert.That(actions.CaptureEvidence(engine).LastGeneration, Is.GreaterThan(generationBeforeBuild));
        Assert.That(actions.CaptureEvidence(engine).FormalRouteAgentCount, Is.EqualTo(0));
        Assert.That(queue.Status, Is.EqualTo(RuntimeNavMeshRebuildStatus.Idle));

        ulong generationBeforeDemolish = actions.CaptureEvidence(engine).LastGeneration;
        Assert.That(actions.TryDemolishWall(engine, out _), Is.True);
        Assert.That(
            actions.CaptureEvidence(engine).FormalRouteAgentCount,
            Is.EqualTo(0),
            "Demolishing the gate must cancel outstanding formal MassNavigation routes before any rebuild tick.");
        Assert.That(CountActiveSquadMoveOrders(engine), Is.EqualTo(0));
        Assert.That(CountSquadMovePlanIntentsWithTarget(engine, actions), Is.EqualTo(0));
        Assert.DoesNotThrow(
            () => actions.DrainUntilIdle(engine, maxTicks: 4096),
            "Rebuild ticks before the opened generation commits must not throw SolveFailed/NoPath.");
        Assert.That(actions.CaptureEvidence(engine).LastGeneration, Is.GreaterThan(generationBeforeDemolish));
        Assert.That(actions.CaptureEvidence(engine).FormalRouteAgentCount, Is.EqualTo(0));

        Assert.That(
            actions.TryCommandMoveToGoal(engine, out string reopenError),
            Is.True,
            $"Player must issue a new move after the opened generation commits. error='{reopenError}'");
        AssertFormalNavMeshRouteEventually(engine, actions);
        TestContext.WriteLine($"RtsStructuralCancel/Recast elapsed={wall.Elapsed}");
    }

    // Feature: sealing the fort mid-march on the real host frame clock does not crash the march systems
    // Given a deployed RTS squad with a live formal MassNavigation route and MovePlanExecutionIntent
    // When the player seals the gate on a presentation/host frame (no DrainUntilIdle)
    // Then formal orders, route-sink rows, and MovePlanExecutionIntent are cleared before the next FixedStep
    // And later host-frame FixedSteps through the rebuild do not throw SolveFailed/NoPath
    [Test]
    public void Feature_RtsStructuralWall_OnHostFrameCadence_ClearsIntentBeforeNextFixedStep()
    {
        using GameEngine engine = CreateEngine("NavBakeDynamicRtsShowcaseMod", registerRecast: true);
        engine.LoadMap(DynamicNavBakeShowcaseIds.RtsMapId);
        DynamicNavBakeShowcaseActions actions = WaitForActions(engine, DynamicNavBakeShowcaseIds.RtsMapId);
        DrainSpawnAndNavBootstrap(engine, actions);
        AssertAlgorithmSwitch(engine, actions, NavBakeAlgorithmKind.Recast);

        Assert.That(actions.SquadDeployed, Is.True);
        Assert.That(actions.TryCommandMoveToGoal(engine, out string moveError), Is.True, moveError);
        AssertFormalNavMeshRouteEventually(engine, actions);
        Assert.That(
            CountSquadMovePlanIntentsWithTarget(engine, actions),
            Is.GreaterThan(0),
            "Precondition: squad must carry live MovePlanExecutionIntent before the structural wall.");

        // Presentation/host seam: cancel happens outside DrainUntilIdle, then later FixedSteps run via Tick.
        Assert.That(actions.TryBuildWall(engine, out string buildError), Is.True, buildError);
        Assert.That(CountActiveSquadMoveOrders(engine), Is.EqualTo(0));
        Assert.That(
            actions.CaptureEvidence(engine).FormalRouteAgentCount,
            Is.EqualTo(0),
            "Host-frame wall must release formal MassNavigation routes immediately.");
        Assert.That(
            CountSquadMovePlanIntentsWithTarget(engine, actions),
            Is.EqualTo(0),
            "Host-frame wall must clear MovePlanExecutionIntent atomically so a later FixedStep cannot re-track.");

        RuntimeIncrementalNavMeshRebuildQueue queue = engine.GetService(CoreServiceKeys.RuntimeNavMeshRebuildQueue)!;
        float fixedDt = Time.FixedDeltaTime;
        Assert.That(fixedDt, Is.GreaterThan(0f));

        Assert.DoesNotThrow(
            () =>
            {
                for (int frame = 0; frame < 240; frame++)
                {
                    engine.SetService(CoreServiceKeys.HostFrameIndex, frame);
                    engine.Tick(fixedDt);
                    if (queue.Status == RuntimeNavMeshRebuildStatus.Idle &&
                        queue.PendingTileCount == 0 &&
                        !queue.HasResidentWindowTransition &&
                        CountSquadMovePlanIntentsWithTarget(engine, actions) == 0 &&
                        (engine.GetService(MassNavigationKeys.RouteExecutionSink)?.ActiveRouteCount ?? 0) == 0 &&
                        frame >= 8)
                    {
                        // Allow a few FixedSteps after first idle so late dirty capture settles.
                        break;
                    }
                }
            },
            "Host-frame rebuild after structural wall must not throw MassNavigation SolveFailed/NoPath.");

        Assert.That(CountSquadMovePlanIntentsWithTarget(engine, actions), Is.EqualTo(0));
        Assert.That(actions.CaptureEvidence(engine).FormalRouteAgentCount, Is.EqualTo(0));
    }

    // Feature: RTS sealed channel clears stale path
    // Given a deployed squad with a live route through the fort
    // When the player seals the central gate and asks for a new route
    // Then the old open path is not kept as-is
    [TestCase(NavBakeAlgorithmKind.Recast)]
    [TestCase(NavBakeAlgorithmKind.Cdt)]
    [TestCase(NavBakeAlgorithmKind.LayeredSpan)]
    public void Feature_RtsSealCentralChannel_StalePathClears(NavBakeAlgorithmKind algorithm)
    {
        Stopwatch wall = Stopwatch.StartNew();
        using GameEngine engine = CreateEngine("NavBakeDynamicRtsShowcaseMod", registerRecast: algorithm == NavBakeAlgorithmKind.Recast);
        engine.LoadMap(DynamicNavBakeShowcaseIds.RtsMapId);
        DynamicNavBakeShowcaseActions actions = WaitForActions(engine, DynamicNavBakeShowcaseIds.RtsMapId);
        DrainSpawnAndNavBootstrap(engine, actions);
        AssertAlgorithmSwitch(engine, actions, algorithm);
        Assert.That(actions.SquadDeployed, Is.True);
        Assert.That(actions.TryCommandMoveToGoal(engine, out _), Is.True);
        int openPathPoints = actions.LastPathPointCount;
        Assert.That(openPathPoints, Is.GreaterThan(1));
        ulong openSignature = actions.CaptureEvidence(engine).PlayerRouteSignature;
        Assert.That(openSignature, Is.Not.EqualTo(0UL), "Initial RTS formal route geometry signature must be nonzero.");

        Assert.That(actions.TryBuildWall(engine, out _), Is.True);
        actions.DrainUntilIdle(engine, maxTicks: 4096);
        Assert.That(actions.TryCommandMoveToGoal(engine, out string blockedError), Is.True, blockedError);
        Assert.That(actions.LastPathStatus, Is.EqualTo(NavPathStatus.Ok), "Sealed gate must leave a usable side route.");
        Assert.That(actions.LastPathPointCount, Is.GreaterThan(1), "Sealed side route must remain a multi-point path.");
        ulong sealedSignature = actions.CaptureEvidence(engine).PlayerRouteSignature;
        Assert.That(sealedSignature, Is.Not.EqualTo(0UL), "Sealed RTS formal route geometry signature must be nonzero.");
        Assert.That(
            sealedSignature,
            Is.Not.EqualTo(openSignature),
            "Sealed gate must change the deterministic player-route geometry signature (Recast may keep the same waypoint count).");

        TestContext.WriteLine($"RTS-stale/{algorithm} elapsed={wall.Elapsed}");
    }

    // Feature: HexGrid runtime dirty update shows the same player loop under every bake algorithm
    // Given a player enters the HexGrid fortress with visible relief terrain and a selected squad
    // When they switch to Recast, CDT, or LayeredSpan, build a wall, demolish it, then raise terrain
    // Then every edit publishes a local runtime generation, the resident window stays bounded, and the squad can still route
    [TestCase(NavBakeAlgorithmKind.Recast)]
    [TestCase(NavBakeAlgorithmKind.Cdt)]
    [TestCase(NavBakeAlgorithmKind.LayeredSpan)]
    public void Feature_HexRtsRuntimeDirtyUpdate_BuildRestoreAndTerrainRaise(NavBakeAlgorithmKind algorithm)
    {
        Stopwatch wall = Stopwatch.StartNew();
        using GameEngine engine = CreateEngine("NavBakeDynamicRtsHexShowcaseMod", registerRecast: algorithm == NavBakeAlgorithmKind.Recast);
        engine.LoadMap(DynamicNavBakeShowcaseIds.RtsHexMapId);
        DynamicNavBakeShowcaseActions actions = WaitForActions(engine, DynamicNavBakeShowcaseIds.RtsHexMapId);
        DrainSpawnAndNavBootstrap(engine, actions);
        AssertAlgorithmSwitch(engine, actions, algorithm);

        DynamicNavBakeShowcaseConfig config = actions.ActiveConfig;
        Assert.That(config.MapId, Is.EqualTo(DynamicNavBakeShowcaseIds.RtsHexMapId));
        Assert.That(config.ResidentWidthChunks * config.ResidentHeightChunks, Is.EqualTo(36));
        Assert.That(config.SurfaceTileWidthCm, Is.EqualTo(22176));
        Assert.That(config.SurfaceTileHeightCm, Is.EqualTo(19200));

        var visualHeightmap = engine.GetService(CoreServiceKeys.VisualHeightmap)
            ?? throw new InvalidOperationException("Hex Dynamic NavBake showcase must bind VisualHeightmap.");
        if (visualHeightmap is not IVisualHeightmapRenderSource visualHeightmapSource)
        {
            throw new InvalidOperationException("Hex Dynamic NavBake VisualHeightmap must be renderable so the relief is visible.");
        }

        Assert.That(visualHeightmapSource.Bounds.Left, Is.LessThanOrEqualTo(config.WorldOriginXCm));
        Assert.That(visualHeightmapSource.Bounds.Right, Is.GreaterThanOrEqualTo(config.WorldMaxXCm));
        Assert.That(visualHeightmapSource.Bounds.Top, Is.LessThanOrEqualTo(config.WorldOriginZCm));
        Assert.That(visualHeightmapSource.Bounds.Bottom, Is.GreaterThanOrEqualTo(config.WorldMaxZCm));

        NavTriangleSurfaceTileIndex triangleSurface = engine.GetService(CoreServiceKeys.NavTriangleSurface)
            ?? throw new InvalidOperationException("Hex Dynamic NavBake requires NavTriangleSurface.");
        Assert.That(triangleSurface.Grid.TileWidthCm, Is.EqualTo(config.SurfaceTileWidthCm));
        Assert.That(triangleSurface.Grid.TileHeightCm, Is.EqualTo(config.SurfaceTileHeightCm));
        Assert.That(triangleSurface.Grid.OriginXcm, Is.EqualTo(config.WorldOriginXCm));
        Assert.That(triangleSurface.Grid.OriginZcm, Is.EqualTo(config.WorldOriginZCm));
        Assert.That(triangleSurface.Surface.TriangleCount, Is.GreaterThan(0));

        RuntimeIncrementalNavMeshRebuildQueue queue = engine.GetService(CoreServiceKeys.RuntimeNavMeshRebuildQueue)!;
        int expectedResidentTileCount = checked(config.ResidentWidthChunks * config.ResidentHeightChunks);
        Assert.That(queue.CommittedResidentWindowCount, Is.EqualTo(expectedResidentTileCount));

        Assert.That(actions.SquadDeployed, Is.True);
        Assert.That(
            actions.TryCommandMoveToGoal(engine, out string moveError),
            Is.True,
            BuildHexRtsMoveDiagnostic(engine, actions, queue, moveError));
        Assert.That(actions.LastPathStatus, Is.EqualTo(NavPathStatus.Ok));
        AssertFormalNavMeshRouteEventually(engine, actions, expectedSquadCount: config.Squad.Count);
        ulong initialRouteSignature = actions.CaptureEvidence(engine).PlayerRouteSignature;
        Assert.That(initialRouteSignature, Is.Not.EqualTo(0UL));

        ulong generationBeforeBuild = actions.CaptureEvidence(engine).LastGeneration;
        Assert.That(actions.TryBuildWall(engine, out string buildError), Is.True, buildError);
        actions.DrainUntilIdle(engine, maxTicks: 8192);
        DynamicNavBakeShowcaseEvidence builtEvidence = actions.CaptureEvidence(engine);
        Assert.That(builtEvidence.LastGeneration, Is.GreaterThan(generationBeforeBuild));
        Assert.That(queue.LastDirtyVisitedCandidateCount, Is.GreaterThan(0));
        Assert.That(queue.LastDirtyVisitedCandidateCount, Is.LessThanOrEqualTo(config.Benchmark.MaxDirtyVisitedCandidateCount));
        Assert.That(queue.CommittedResidentWindowCount, Is.EqualTo(expectedResidentTileCount));
        Assert.That(actions.WallDeployedCount, Is.EqualTo(1));
        AssertDeployedWallNavigationFootprints(engine, config);
        Assert.That(
            actions.TryCommandMoveToGoal(engine, out string walledMoveError),
            Is.True,
            BuildHexRtsBakeDiagnostic(engine, queue, walledMoveError));
        Assert.That(actions.LastPathStatus, Is.EqualTo(NavPathStatus.Ok));
        AssertFormalNavMeshRouteEventually(engine, actions, expectedSquadCount: config.Squad.Count);

        ulong generationBeforeDemolish = actions.CaptureEvidence(engine).LastGeneration;
        Assert.That(actions.TryDemolishWall(engine, out string demolishError), Is.True, demolishError);
        actions.DrainUntilIdle(engine, maxTicks: 8192);
        DynamicNavBakeShowcaseEvidence demolishedEvidence = actions.CaptureEvidence(engine);
        Assert.That(demolishedEvidence.LastGeneration, Is.GreaterThan(generationBeforeDemolish));
        Assert.That(queue.LastDirtyVisitedCandidateCount, Is.GreaterThan(0));
        Assert.That(queue.LastDirtyVisitedCandidateCount, Is.LessThanOrEqualTo(config.Benchmark.MaxDirtyVisitedCandidateCount));
        Assert.That(actions.WallDeployedCount, Is.EqualTo(0));
        Assert.That(actions.TryCommandMoveToGoal(engine, out string restoredMoveError), Is.True, restoredMoveError);
        Assert.That(actions.LastPathStatus, Is.EqualTo(NavPathStatus.Ok));
        AssertFormalNavMeshRouteEventually(engine, actions, expectedSquadCount: config.Squad.Count);

        RuntimeNavTriangleSurfaceService runtimeSurface = engine.GetService(CoreServiceKeys.RuntimeNavTriangleSurface)
            ?? throw new InvalidOperationException("Hex terrain raise requires RuntimeNavTriangleSurface.");
        NavTriangleSurfaceTileIndex beforeTerrain = runtimeSurface.Published;
        string beforeTerrainHash = DynamicNavBakeShowcaseEvidenceCapture.ComputeInputHash(beforeTerrain);
        ulong surfaceGenerationBeforeRaise = runtimeSurface.ContentGeneration;
        ulong navGenerationBeforeRaise = actions.CaptureEvidence(engine).LastGeneration;
        Assert.That(actions.TryStageTerrainRaise(engine, out string stageTerrainError), Is.True, stageTerrainError);
        Assert.That(actions.TryBake(engine, out string terrainBakeError), Is.True, terrainBakeError);
        actions.DrainUntilIdle(engine, maxTicks: 8192);
        Assert.That(runtimeSurface.ContentGeneration, Is.GreaterThan(surfaceGenerationBeforeRaise));
        Assert.That(runtimeSurface.Published, Is.Not.SameAs(beforeTerrain));
        Assert.That(
            DynamicNavBakeShowcaseEvidenceCapture.ComputeInputHash(runtimeSurface.Published),
            Is.Not.EqualTo(beforeTerrainHash),
            "Hex terrain raise must change authoritative triangle geometry, not only generation state.");
        DynamicNavBakeShowcaseEvidence raisedEvidence = actions.CaptureEvidence(engine);
        Assert.That(raisedEvidence.LastGeneration, Is.GreaterThan(navGenerationBeforeRaise));
        Assert.That(queue.LastDirtyVisitedCandidateCount, Is.GreaterThan(0));
        Assert.That(queue.LastDirtyVisitedCandidateCount, Is.LessThanOrEqualTo(config.Benchmark.MaxDirtyVisitedCandidateCount));
        Assert.That(queue.CommittedResidentWindowCount, Is.EqualTo(expectedResidentTileCount));

        ulong surfaceGenerationBeforeRestore = runtimeSurface.ContentGeneration;
        Assert.That(actions.TryRestore(engine, out string stageRestoreError), Is.True, stageRestoreError);
        Assert.That(actions.TryBake(engine, out string restoreBakeError), Is.True, restoreBakeError);
        actions.DrainUntilIdle(engine, maxTicks: 8192);
        Assert.That(runtimeSurface.ContentGeneration, Is.GreaterThan(surfaceGenerationBeforeRestore));
        Assert.That(runtimeSurface.Published, Is.SameAs(beforeTerrain));
        Assert.That(DynamicNavBakeShowcaseEvidenceCapture.ComputeInputHash(runtimeSurface.Published), Is.EqualTo(beforeTerrainHash));
        Assert.That(actions.TryCommandMoveToGoal(engine, out string finalMoveError), Is.True, finalMoveError);
        Assert.That(actions.LastPathStatus, Is.EqualTo(NavPathStatus.Ok));
        AssertFormalNavMeshRouteEventually(engine, actions, expectedSquadCount: config.Squad.Count);

        TestContext.WriteLine(
            $"HexRTS-dirty/{algorithm} elapsed={wall.Elapsed} dirtyCandidates={queue.LastDirtyVisitedCandidateCount}");
        Assert.That(wall.Elapsed, Is.LessThan(TimeSpan.FromMinutes(4)));
    }

    // Feature: Open-world hotspot wall follows the focused hotspot
    // Given the player starts at Central Gate and moves to the next hotspot
    // When the resident window finishes sliding and they build the wall
    // Then every deployed segment sits inside the new window around that hotspot (not around the old central gate),
    // dirty rebuild stays local, generation advances, and demolition parks the same pool
    [Test]
    public void Feature_OpenWorldNextHotspot_BuildWallPlacesSegmentsAroundActiveHotspot()
    {
        using GameEngine engine = CreateEngine("NavBakeOpenWorld64x64ShowcaseMod", registerRecast: false);
        engine.LoadMap(DynamicNavBakeShowcaseIds.OpenWorldMapId);
        DynamicNavBakeShowcaseActions actions = WaitForActions(engine, DynamicNavBakeShowcaseIds.OpenWorldMapId);
        DrainSpawnAndNavBootstrap(engine, actions);
        AssertAlgorithmSwitch(engine, actions, NavBakeAlgorithmKind.LayeredSpan);

        DynamicNavBakeShowcaseConfig config = actions.ActiveConfig;
        Assert.That(config.OpenWorld!.InitialHotspotIndex, Is.EqualTo(1));
        DynamicNavBakeShowcaseHotspotConfig nextHotspot = config.OpenWorld.Hotspots[2];
        Assert.That(nextHotspot.Id, Is.EqualTo("east_reach"));
        Assert.That(nextHotspot.WallCenterXCm, Is.Not.EqualTo(0));

        Assert.That(actions.TryNextHotspot(engine, out string hotspotError), Is.True, hotspotError);
        actions.DrainUntilIdle(engine, maxTicks: 8192);
        RuntimeIncrementalNavMeshRebuildQueue queue = engine.GetService(CoreServiceKeys.RuntimeNavMeshRebuildQueue)!;
        Assert.That(queue.HasResidentWindowTransition, Is.False);
        Assert.That(queue.CommittedResidentWindowCount, Is.EqualTo(64));

        ulong generationBefore = actions.CaptureEvidence(engine).LastGeneration;
        Assert.That(actions.TryBuildWall(engine, out string buildError), Is.True, buildError);
        Assert.That(actions.LastStatus, Does.Contain(nextHotspot.Label));
        Assert.That(actions.WallDeployedCount, Is.EqualTo(config.Gate.SegmentCount));
        AssertDeployedWallNavigationFootprints(engine, config);

        ResolveAuthoredResidentWindowBounds(config, nextHotspot, out int winMinX, out int winMinY, out int winMaxX, out int winMaxY);
        var deployed = CaptureDeployedWallPositions(engine, config);
        Assert.That(deployed.Count, Is.EqualTo(config.Gate.SegmentCount));
        int half = (config.Gate.SegmentCount - 1) / 2;
        int maxOffset = half * config.Gate.SegmentSpacingCm;
        for (int i = 0; i < deployed.Count; i++)
        {
            (int X, int Y) pos = deployed[i];
            Assert.That(pos.X, Is.GreaterThanOrEqualTo(winMinX), $"Segment {i} X must stay inside east resident window.");
            Assert.That(pos.X, Is.LessThan(winMaxX), $"Segment {i} X must stay inside east resident window.");
            Assert.That(pos.Y, Is.GreaterThanOrEqualTo(winMinY), $"Segment {i} Y must stay inside east resident window.");
            Assert.That(pos.Y, Is.LessThan(winMaxY), $"Segment {i} Y must stay inside east resident window.");
            Assert.That(Math.Abs(pos.X - nextHotspot.WallCenterXCm), Is.LessThanOrEqualTo(maxOffset));
            Assert.That(pos.Y, Is.EqualTo(nextHotspot.WallCenterYCm));
            Assert.That(Math.Abs(pos.X), Is.GreaterThan(10000), "Deployed east hotspot wall must not sit around central (0,0).");
        }

        actions.DrainUntilIdle(engine, maxTicks: 4096);
        Assert.That(queue.LastDirtyVisitedCandidateCount, Is.GreaterThan(0));
        Assert.That(queue.LastDirtyVisitedCandidateCount, Is.LessThan(4096));
        ulong generationAfter = actions.CaptureEvidence(engine).LastGeneration;
        Assert.That(generationAfter, Is.GreaterThan(generationBefore));

        Assert.That(actions.TryDemolishWall(engine, out string demolishError), Is.True, demolishError);
        Assert.That(actions.LastStatus, Does.Contain(nextHotspot.Label));
        Assert.That(actions.WallDeployedCount, Is.EqualTo(0));
        actions.DrainUntilIdle(engine, maxTicks: 4096);
    }

    // Feature: Open-world hotspot wall rebuild stays local
    // Given the player is focused on the open-world resident window
    // When they build a wall near the gate
    // Then only the resident window is dirtied (<4096 candidates) and the local march geometry changes
    [TestCase(NavBakeAlgorithmKind.Recast)]
    [TestCase(NavBakeAlgorithmKind.Cdt)]
    [TestCase(NavBakeAlgorithmKind.LayeredSpan)]
    public void Feature_OpenWorldHotspotBuildWall_OnlyResidentWindowRebuilds(NavBakeAlgorithmKind algorithm)
    {
        Stopwatch wall = Stopwatch.StartNew();
        using GameEngine engine = CreateEngine("NavBakeOpenWorld64x64ShowcaseMod", registerRecast: algorithm == NavBakeAlgorithmKind.Recast);
        engine.LoadMap(DynamicNavBakeShowcaseIds.OpenWorldMapId);
        DynamicNavBakeShowcaseActions actions = WaitForActions(engine, DynamicNavBakeShowcaseIds.OpenWorldMapId);
        DrainSpawnAndNavBootstrap(engine, actions);
        AssertAlgorithmSwitch(engine, actions, algorithm);
        Assert.That(actions.SquadDeployed, Is.True);

        RuntimeIncrementalNavMeshRebuildQueue queue = engine.GetService(CoreServiceKeys.RuntimeNavMeshRebuildQueue)!;
        Assert.That(queue.CommittedResidentWindowCount, Is.EqualTo(64), "Open world resident nav window must remain 8x8.");

        bool openMoveOk = actions.TryCommandMoveToGoal(engine, out string openMoveError);
        Assert.That(
            openMoveOk,
            Is.True,
            $"Local segment inside the committed window must be queryable before the wall. error='{openMoveError}' status='{actions.LastStatus}' path={actions.LastPathStatus} orch={actions.PathOrchestrationState} points={actions.LastPathPointCount} corridor={actions.LastCoarseCorridorNodeCount} queue={queue.Status} transition={queue.HasResidentWindowTransition}");
        AssertFormalNavMeshRouteEventually(engine, actions, expectedSquadCount: actions.ActiveConfig.Squad.Count);
        DynamicNavBakeShowcaseEvidence openEvidence = actions.CaptureEvidence(engine);
        ulong openRouteSignature = openEvidence.PlayerRouteSignature;
        Assert.That(openRouteSignature, Is.Not.EqualTo(0UL), "Initial open-world formal route signature must be nonzero.");
        Assert.That(openEvidence.PathStatus, Is.EqualTo(nameof(NavPathStatus.Ok)));
        Assert.That(queue.CommittedResidentWindowCount, Is.EqualTo(64));

        Assert.That(actions.TryBuildWall(engine, out _), Is.True);
        actions.DrainUntilIdle(engine, maxTicks: 4096);
        Assert.That(queue.LastDirtyVisitedCandidateCount, Is.LessThan(4096));
        Assert.That(queue.LastDirtyVisitedCandidateCount, Is.GreaterThan(0));
        Assert.That(queue.CommittedResidentWindowCount, Is.EqualTo(64));

        bool blockedMoveOk = actions.TryCommandMoveToGoal(engine, out string blockedMoveError);
        Assert.That(
            blockedMoveOk,
            Is.True,
            $"Central wall rebuild must leave a usable local side route. error='{blockedMoveError}' status='{actions.LastStatus}' path={actions.LastPathStatus} orch={actions.PathOrchestrationState} dirtyCandidates={queue.LastDirtyVisitedCandidateCount}");
        Assert.That(
            actions.LastPathStatus,
            Is.EqualTo(NavPathStatus.Ok),
            $"Open-world gate wall must leave a usable local side route. error='{blockedMoveError}' status='{actions.LastStatus}' orch={actions.PathOrchestrationState} dirtyCandidates={queue.LastDirtyVisitedCandidateCount}");
        AssertFormalNavMeshRouteEventually(engine, actions, expectedSquadCount: actions.ActiveConfig.Squad.Count);
        DynamicNavBakeShowcaseEvidence blockedEvidence = actions.CaptureEvidence(engine);
        ulong blockedRouteSignature = blockedEvidence.PlayerRouteSignature;
        Assert.That(blockedRouteSignature, Is.Not.EqualTo(0UL), "Post-wall open-world formal route signature must be nonzero.");
        Assert.That(
            blockedRouteSignature,
            Is.Not.EqualTo(openRouteSignature),
            $"Open-world gate wall must change the formal player-route geometry signature (Recast may keep the same waypoint count). openSig={openRouteSignature} blockedSig={blockedRouteSignature} dirtyCandidates={queue.LastDirtyVisitedCandidateCount}");
        Assert.That(blockedEvidence.PathPointCount, Is.GreaterThan(0), "Post-wall local route must remain usable.");
        Assert.That(queue.CommittedResidentWindowCount, Is.EqualTo(64));
        Assert.That(queue.LastDirtyVisitedCandidateCount, Is.GreaterThan(0));
        Assert.That(queue.LastDirtyVisitedCandidateCount, Is.LessThan(4096));

        Assert.That(actions.TryDemolishWall(engine, out _), Is.True);
        actions.DrainUntilIdle(engine, maxTicks: 4096);
        Assert.That(actions.TryCommandMoveToGoal(engine, out _), Is.True, "Demolition must restore a usable local segment.");
        TestContext.WriteLine($"OpenWall/{algorithm} elapsed={wall.Elapsed} dirtyCandidates={queue.LastDirtyVisitedCandidateCount}");
        Assert.That(wall.Elapsed, Is.LessThan(TimeSpan.FromMinutes(4)));
    }

    // Feature: Open-world long march uses corridor + local resident window
    // Given a west-spawned squad and a far east goal on the 64x64 world
    // When the player commands a long move
    // Then a real 4096-node corridor is kept, only 64 local tiles are resident, and the first local segment is inside the window
    [TestCase(NavBakeAlgorithmKind.LayeredSpan)]
    [TestCase(NavBakeAlgorithmKind.Cdt)]
    [TestCase(NavBakeAlgorithmKind.Recast)]
    public void Feature_OpenWorldLongMove_UsesCorridorAndResidentWindow(NavBakeAlgorithmKind algorithm)
    {
        Stopwatch wall = Stopwatch.StartNew();
        using GameEngine engine = CreateEngine(
            "NavBakeOpenWorld64x64ShowcaseMod",
            registerRecast: algorithm == NavBakeAlgorithmKind.Recast);
        engine.LoadMap(DynamicNavBakeShowcaseIds.OpenWorldMapId);
        DynamicNavBakeShowcaseActions actions = WaitForActions(engine, DynamicNavBakeShowcaseIds.OpenWorldMapId);
        DrainSpawnAndNavBootstrap(engine, actions);
        AssertAlgorithmSwitch(engine, actions, algorithm);

        Assert.That(actions.SquadDeployed, Is.True);
        AssertPathingHumanoidMapsToLight(engine);
        var positionsBefore = CaptureSquadPositions(engine, actions);
        bool moveOk = actions.TryCommandMoveToGoal(engine, out string moveError);
        Assert.That(
            moveOk,
            Is.True,
            $"Move failed: error='{moveError}' status='{actions.LastStatus}' path={actions.LastPathStatus} orch={actions.PathOrchestrationState} points={actions.LastPathPointCount} corridor={actions.LastCoarseCorridorNodeCount}");
        Assert.That(actions.LastCoarseCorridorNodeCount, Is.GreaterThan(2));
        Assert.That(actions.PathOrchestrationState, Is.EqualTo(DynamicNavBakePathOrchestrationState.LocalSegmentReady));
        Assert.That(actions.LastPathPointCount, Is.GreaterThan(1));
        AssertFormalNavMeshRouteEventually(engine, actions);
        Assert.That(actions.CaptureEvidence(engine).CommittedResidentWindowCount, Is.EqualTo(64));
        Assert.That(actions.CaptureEvidence(engine).ResidentWindowCount, Is.EqualTo(64));
        AssertFormalNavMeshRouteEventually(engine, actions);

        TickForMotion(engine, actions, ticks: 240);
        var positionsAfter = CaptureSquadPositions(engine, actions);
        AssertSquadMoved(positionsBefore, positionsAfter);
        TestContext.WriteLine(
            $"OpenLong/{algorithm} elapsed={wall.Elapsed} corridor={actions.LastCoarseCorridorNodeCount} localPoints={actions.LastPathPointCount}");
        Assert.That(wall.Elapsed, Is.LessThan(TimeSpan.FromMinutes(5)));
    }

    // Feature: ordinary FixedSteps advance an open-world march without DrainUntilIdle
    // Given a west-spawned squad with an active long open-world command and a 4096-node corridor
    // When the player leaves the simulation running on ordinary engine FixedSteps only
    // Then the FixedStep system advances the first corridor checkpoint and commits a resident-window slide
    // And after Idle (no in-flight transition) the next formal local NavMesh march continues far from the final goal
    // And residency stays exactly 64 requested/committed tiles with dirty candidates always under 4096
    // And every living squad member stands inside the newly committed resident window (no trailer left outside)
    [Test]
    public void Feature_OpenWorldFixedStepSystem_AdvancesCorridorWithoutDrainUntilIdle()
    {
        using GameEngine engine = CreateEngine("NavBakeOpenWorld64x64ShowcaseMod", registerRecast: false);
        engine.LoadMap(DynamicNavBakeShowcaseIds.OpenWorldMapId);
        DynamicNavBakeShowcaseActions actions = WaitForActions(engine, DynamicNavBakeShowcaseIds.OpenWorldMapId);
        DrainSpawnAndNavBootstrap(engine, actions);
        AssertAlgorithmSwitch(engine, actions, NavBakeAlgorithmKind.LayeredSpan);

        Assert.That(actions.SquadDeployed, Is.True);
        Assert.That(actions.TryCommandMoveToGoal(engine, out string moveError), Is.True, moveError);
        Assert.That(actions.MoveCommandActive, Is.True);
        Assert.That(actions.LastCoarseCorridorNodeCount, Is.GreaterThan(2));
        int expectedSquadCount = actions.ActiveConfig.Squad.Count;
        AssertFormalNavMeshRouteEventually(engine, actions, expectedSquadCount: expectedSquadCount);

        RuntimeIncrementalNavMeshRebuildQueue queue = engine.GetService(CoreServiceKeys.RuntimeNavMeshRebuildQueue)!;
        int corridorBefore = actions.LastCoarseCorridorNodeCount;
        int cursorBefore = actions.OpenWorldCorridorCursor;
        ulong windowFingerprintBefore = HashCommittedResidentWindow(queue);
        ulong generationBefore = actions.CaptureEvidence(engine).LastGeneration;
        ulong routeSignatureBefore = actions.CaptureEvidence(engine).PlayerRouteSignature;
        Assert.That(routeSignatureBefore, Is.Not.EqualTo(0UL));
        Assert.That(queue.ResidentWindowCount, Is.EqualTo(64));
        Assert.That(queue.CommittedResidentWindowCount, Is.EqualTo(64));
        Assert.That(generationBefore, Is.GreaterThan(0UL));

        // Bootstrap + algorithm switch already consumed telemetry samples. Reset the evidence epoch
        // before the long FixedStep march so the first resident-window slide can publish its generation.
        DynamicNavBakeShowcaseAcceptanceHarness.BeginEvidenceEpoch(engine);

        float fixedDt = Time.FixedDeltaTime;
        Assert.That(fixedDt, Is.GreaterThan(0f));

        bool formalRouteContinued = false;
        int ticksRun = 0;
        const int maxTicks = 2048;
        DynamicNavBakeShowcaseEvidence evidence = default!;
        for (int tick = 0; tick < maxTicks; tick++)
        {
            ticksRun = tick + 1;
            engine.Tick(fixedDt);
            Assert.That(
                engine.Pacemaker is not RealtimePacemaker realtime || !realtime.IsBudgetFused,
                Is.True,
                $"Simulation budget fused at FixedStep {tick}; corridor advance cannot continue.");
            Assert.That(queue.ResidentWindowCount, Is.EqualTo(64));
            Assert.That(queue.CommittedResidentWindowCount, Is.EqualTo(64));
            Assert.That(queue.LastDirtyVisitedCandidateCount, Is.LessThan(4096));

            // Do not treat WindowRebuilding, an in-flight slide, generic squad motion, or Arrived as success.
            // The first open-world window transition is far from the authored final goal: require a committed
            // cursor+window change that settles to Idle LocalSegmentReady with a new formal route signature.
            if (actions.OpenWorldCorridorCursor <= cursorBefore)
            {
                continue;
            }

            if (HashCommittedResidentWindow(queue) == windowFingerprintBefore)
            {
                continue;
            }

            if (queue.Status != RuntimeNavMeshRebuildStatus.Idle || queue.HasResidentWindowTransition)
            {
                continue;
            }

            if (!actions.MoveCommandActive ||
                actions.PathOrchestrationState != DynamicNavBakePathOrchestrationState.LocalSegmentReady)
            {
                continue;
            }

            evidence = actions.CaptureEvidence(engine);
            if (evidence.LastGeneration <= generationBefore)
            {
                continue;
            }

            if (evidence.FormalRouteAgentCount != expectedSquadCount ||
                evidence.FormalRouteDomain != PathDomain.NavMesh ||
                evidence.FormalRouteMinWaypointCount <= 0)
            {
                continue;
            }

            if (evidence.PlayerRouteSignature == 0UL ||
                evidence.PlayerRouteSignature == routeSignatureBefore)
            {
                continue;
            }

            formalRouteContinued = true;
            break;
        }

        Assert.That(corridorBefore, Is.GreaterThan(2), "Authored open-world command must keep the global corridor.");
        Assert.That(actions.LastCoarseCorridorNodeCount, Is.GreaterThan(2));
        Assert.That(queue.ResidentWindowCount, Is.EqualTo(64));
        Assert.That(queue.CommittedResidentWindowCount, Is.EqualTo(64));
        Assert.That(queue.Status, Is.EqualTo(RuntimeNavMeshRebuildStatus.Idle));
        Assert.That(queue.HasResidentWindowTransition, Is.False);
        Assert.That(
            formalRouteContinued,
            Is.True,
            $"Ordinary FixedSteps must commit the first corridor/window advance and resume a formal local march (Idle LocalSegmentReady, not WindowRebuilding/Arrived). " +
            $"ticks={ticksRun}/{maxTicks} fixedDt={fixedDt} cursorBefore={cursorBefore} cursorNow={actions.OpenWorldCorridorCursor} " +
            $"windowChanged={HashCommittedResidentWindow(queue) != windowFingerprintBefore} " +
            $"orch={actions.PathOrchestrationState} moveActive={actions.MoveCommandActive} " +
            $"genBefore={generationBefore} genNow={actions.CaptureEvidence(engine).LastGeneration} " +
            $"formalAgents={actions.CaptureEvidence(engine).FormalRouteAgentCount}/{expectedSquadCount} " +
            $"routeSigBefore={routeSignatureBefore} routeSigNow={actions.CaptureEvidence(engine).PlayerRouteSignature} " +
            $"pathPoints={actions.LastPathPointCount} status={actions.LastStatus}.");
        Assert.That(actions.OpenWorldCorridorCursor, Is.GreaterThan(cursorBefore));
        Assert.That(HashCommittedResidentWindow(queue), Is.Not.EqualTo(windowFingerprintBefore));
        Assert.That(actions.MoveCommandActive, Is.True);
        Assert.That(actions.PathOrchestrationState, Is.EqualTo(DynamicNavBakePathOrchestrationState.LocalSegmentReady));
        Assert.That(evidence.LastGeneration, Is.GreaterThan(generationBefore));
        AssertFormalNavMeshRouteEvidence(evidence, expectedSquadCount);
        Assert.That(evidence.PlayerRouteSignature, Is.Not.EqualTo(0UL));
        Assert.That(evidence.PlayerRouteSignature, Is.Not.EqualTo(routeSignatureBefore));
        AssertAllSquadMembersInsideCommittedResidentBounds(engine, actions, queue);
    }

    // Feature: both scenes expose only the minimal playable controls
    // Given the player enters an RTS fort or open-world nav bake scene
    // When they toggle NavMesh, enter construction, place a building, and issue a formal move
    // Then NavMesh visibility, obstacle dirty rebuild, and route evidence update without a tech-demo command deck
    [TestCase("NavBakeDynamicRtsShowcaseMod", DynamicNavBakeShowcaseIds.RtsMapId)]
    [TestCase("NavBakeOpenWorld64x64ShowcaseMod", DynamicNavBakeShowcaseIds.OpenWorldMapId)]
    public void Feature_BothScenes_PlayerUsesVisibleNavBakeControls(string sceneModId, string mapId)
    {
        bool openWorld = string.Equals(mapId, DynamicNavBakeShowcaseIds.OpenWorldMapId, StringComparison.Ordinal);
        using GameEngine engine = CreateEngine(sceneModId, registerRecast: false, installUi: true);
        UIRoot uiRoot = RequireUiRoot(engine);
        engine.LoadMap(mapId);
        DynamicNavBakeShowcaseActions actions = WaitForActions(engine, mapId);
        DrainSpawnAndNavBootstrap(engine, actions);
        TickFrames(engine, 2);

        AssertMinimalPlayerPanelMounted(uiRoot);
        Assert.That(actions.SquadDeployed, Is.True, "Units must be ready for box-select without a Deploy button.");
        Assert.That(
            engine.GlobalContext.TryGetValue(CoreServiceKeys.CommandSourceAcquisitionSuppressed.Name, out _),
            Is.False);

        NavMeshPresentationState navMeshPresentation = engine.GetService(CoreServiceKeys.NavMeshPresentationState)
            ?? throw new InvalidOperationException("Dynamic NavBake UI requires the Core NavMeshPresentationState.");
        Assert.That(navMeshPresentation.Enabled, Is.True);
        ClickElement(uiRoot, DynamicNavBakeShowcaseIds.NavMeshVisibilityButtonElementId);
        TickFrames(engine, 1);
        Assert.That(navMeshPresentation.Enabled, Is.False, "Hide NavMesh must update the Core presentation state.");
        ClickElement(uiRoot, DynamicNavBakeShowcaseIds.NavMeshVisibilityButtonElementId);
        TickFrames(engine, 1);
        Assert.That(navMeshPresentation.Enabled, Is.True, "Show NavMesh must restore the same Core presentation state.");
        Assert.That(RequireElementText(uiRoot, DynamicNavBakeShowcaseIds.StatusTextElementId), Is.Not.Empty);

        if (openWorld)
        {
            MinimapRuntime minimap = engine.GetService(CoreServiceKeys.MinimapRuntime)
                ?? throw new InvalidOperationException("Open-world scene must expose MinimapRuntime.");
            Assert.That(minimap.Visible, Is.True, "Open-world focus must show the existing MinimapRuntime.");
        }

        RuntimeIncrementalNavMeshRebuildQueue queue = engine.GetService(CoreServiceKeys.RuntimeNavMeshRebuildQueue)
            ?? throw new InvalidOperationException("Player construction requires RuntimeNavMeshRebuildQueue.");
        int residentBefore = queue.CommittedResidentWindowCount;
        ulong generationBeforeWall = actions.CaptureEvidence(engine).LastGeneration;

        ClickElement(uiRoot, DynamicNavBakeShowcaseIds.BuildBuildingButtonElementId);
        TickFrames(engine, 1);
        Assert.That(actions.ConstructionMode, Is.True);
        Assert.That(engine.GetService(CoreServiceKeys.CommandSourceAcquisitionSuppressed), Is.True);

        ResolveActiveEditPoint(actions, out int editXCm, out int editZCm);
        actions.Runtime.EditTransaction.SetPreview(editXCm, editZCm, DynamicNavBakePlacementLegality.Legal);
        Assert.That(actions.TryPlaceBuildingAtPreview(engine, out string placeError), Is.True, placeError);
        TickFrames(engine, 1);
        Assert.That(actions.ConstructionMode, Is.False, "Successful place must leave construction mode.");
        Assert.That(actions.WallDeployedCount, Is.GreaterThan(0));
        actions.DrainUntilIdle(engine, maxTicks: 4096);
        TickFrames(engine, 1);
        Assert.That(actions.PlayerNavState, Is.EqualTo(DynamicNavBakePlayerNavState.RouteUpdated));
        Assert.That(actions.CaptureEvidence(engine).LastGeneration, Is.GreaterThan(generationBeforeWall));
        Assert.That(
            queue.CommittedResidentWindowCount,
            Is.EqualTo(residentBefore),
            "A local building place must keep the authored resident window size.");
        Assert.That(
            queue.IsWorldPointInCommittedResidentWindow(editXCm, editZCm),
            Is.True,
            "Placed building must remain inside the committed resident window.");

        Assert.That(actions.TryCommandMoveToGoal(engine, out string moveError), Is.True, moveError);
        TickFrames(engine, 1);
        Assert.That(actions.LastPathStatus, Is.EqualTo(NavPathStatus.Ok));
        Assert.That(actions.LastPathPointCount, Is.GreaterThan(1));
        Assert.That(RequireElementText(uiRoot, DynamicNavBakeShowcaseIds.StatusTextElementId), Is.Not.Empty);
    }

    // Feature: right-click ground move uses the installed command-intent route, not a tech-demo API
    // Given the player enters either nav bake scene with squad members selected
    // When the Core command-intent profile evaluates a ground target
    // Then the winning route is massNavigationMove and NavMesh presentation is already publishing tiles
    [TestCase("NavBakeDynamicRtsShowcaseMod", DynamicNavBakeShowcaseIds.RtsMapId)]
    [TestCase("NavBakeOpenWorld64x64ShowcaseMod", DynamicNavBakeShowcaseIds.OpenWorldMapId)]
    public void Feature_BothScenes_RuntimeCommandIntentRoutesMassNavigationMove(string sceneModId, string mapId)
    {
        using GameEngine engine = CreateEngine(sceneModId, registerRecast: false, installUi: true);
        engine.LoadMap(mapId);
        DynamicNavBakeShowcaseActions actions = WaitForActions(engine, mapId);
        DrainSpawnAndNavBootstrap(engine, actions);
        TickFrames(engine, 2);

        Assert.That(actions.ConstructionMode, Is.False, "Play mode must not suppress right-click move.");
        Assert.That(
            engine.GlobalContext.TryGetValue(CoreServiceKeys.CommandSourceAcquisitionSuppressed.Name, out object? suppressed) &&
            suppressed is true,
            Is.False,
            "Command-source acquisition must not stay suppressed after spawn.");

        OrderTypeRegistry orderTypes = engine.GetService(CoreServiceKeys.OrderTypeRegistry)
            ?? throw new InvalidOperationException("OrderTypeRegistry is required for command-intent routing.");
        Assert.That(orderTypes.TryGetId(MassNavigationOrderKeys.Move, out int massMoveOrderTypeId), Is.True);
        Assert.That(orderTypes.TryGetId("moveTo", out int moveToOrderTypeId), Is.True);

        CommandIntentProfileRegistry intents = engine.GetService(CoreServiceKeys.CommandIntentProfileRegistry)
            ?? throw new InvalidOperationException("CommandIntentProfileRegistry is required for right-click routing.");
        Assert.That(
            intents.ProfileIdRegistry.TryGetId("intent.command.default", out int profileId),
            Is.True,
            "intent.command.default must be installed after showcase config merge.");
        Assert.That(intents.IsInstalled(profileId), Is.True);

        Entity actor = actions.SquadEntities[0];
        Assert.That(engine.World.IsAlive(actor), Is.True);
        var groundFacts = new CommandIntentTargetFacts(Entity.Null, HasEntity: false);
        Assert.That(
            intents.TryRoute(profileId, actor, Entity.Null, in groundFacts, out CommandIntentRoute groundRoute),
            Is.True,
            "Ground Command must match a command-intent rule.");
        Assert.That(
            groundRoute.OrderTypeId,
            Is.EqualTo(massMoveOrderTypeId),
            "Installed intent.command.default must route ground Command to massNavigationMove.");
        Assert.That(
            groundRoute.OrderTypeId,
            Is.Not.EqualTo(moveToOrderTypeId),
            "moveTo remains a no-op on mass-nav agents; the merged base profile must not win.");

        NavMeshPresentationState navMeshPresentation = engine.GetService(CoreServiceKeys.NavMeshPresentationState)
            ?? throw new InvalidOperationException("NavMeshPresentationState is required.");
        NavMeshPresentationBuffer navMeshBuffer = engine.GetService(CoreServiceKeys.NavMeshPresentationBuffer)
            ?? throw new InvalidOperationException("NavMeshPresentationBuffer is required.");
        Assert.That(navMeshPresentation.Enabled, Is.True, "NavMesh presentation must start enabled for the player.");
        Assert.That(navMeshBuffer.Tiles.Length, Is.GreaterThan(0), "NavMesh presentation must publish resident tiles.");
    }

    // Feature: left-drag box select can pick the authored squad
    // Given the player enters either nav bake scene with the squad ready
    // When the formal CommandSource eligibility check runs for the local player
    // Then every squad member is acquirable (LiveVisible + selectable), not only pre-stuffed into the collection
    [TestCase("NavBakeDynamicRtsShowcaseMod", DynamicNavBakeShowcaseIds.RtsMapId)]
    [TestCase("NavBakeOpenWorld64x64ShowcaseMod", DynamicNavBakeShowcaseIds.OpenWorldMapId)]
    public void Feature_BothScenes_SquadMembersAreCommandSourceAcquirable(string sceneModId, string mapId)
    {
        using GameEngine engine = CreateEngine(sceneModId, registerRecast: false, installUi: true);
        engine.LoadMap(mapId);
        DynamicNavBakeShowcaseActions actions = WaitForActions(engine, mapId);
        DrainSpawnAndNavBootstrap(engine, actions);
        TickFrames(engine, 2);

        Entity localPlayer = engine.GetService(CoreServiceKeys.LocalPlayerEntity);
        Assert.That(localPlayer, Is.Not.EqualTo(Entity.Null));
        Assert.That(
            engine.GlobalContext.TryGetValue(CoreServiceKeys.CommandSourceAcquisitionSuppressed.Name, out object? suppressed) &&
            suppressed is true,
            Is.False,
            "Command-source acquisition must not stay suppressed after spawn.");

        var commandSourceConfig = engine.GetService(CoreServiceKeys.CommandSourceAcquisitionConfig)
            ?? throw new InvalidOperationException("CommandSourceAcquisitionConfig is required.");
        RelationshipFilter relationFilter =
            (commandSourceConfig.TargetFilter
             ?? throw new InvalidOperationException("commandSource.targetFilter is missing."))
            .ParseRelationFilter();

        Assert.That(actions.SquadEntities.Length, Is.GreaterThan(0));
        for (int i = 0; i < actions.SquadEntities.Length; i++)
        {
            Entity actor = actions.SquadEntities[i];
            Assert.That(engine.World.IsAlive(actor), Is.True, $"squad[{i}] must be alive");
            Assert.That(
                engine.World.Has<CommandSourceSelectableTag>(actor),
                Is.True,
                $"squad[{i}] must author CommandSourceSelectableTag");
            Assert.That(
                CommandSourceEligibility.CanAcquire(
                    engine.World,
                    engine.GlobalContext,
                    localPlayer,
                    actor,
                    relationFilter),
                Is.True,
                $"squad[{i}] entity {actor.Id} must be CommandSource-acquirable for left-drag box select.");
        }
    }

    // Feature: right-click ground issues a formal massNavigationMove through the live input chain
    // Given the player enters the RTS nav bake scene with the squad selected and controllable
    // When they press the mouse right button on open ground through the production AuthoritativeInput path
    // Then CoreInput records a massNavigationMove order and squad members start executing a move plan
    [Test]
    public void Feature_RtsScene_FormalRightClickGroundMovesSelectedSquad()
    {
        using GameEngine engine = CreateEngineWithMutableInput("NavBakeDynamicRtsShowcaseMod", registerRecast: false);
        engine.LoadMap(DynamicNavBakeShowcaseIds.RtsMapId);
        DynamicNavBakeShowcaseActions actions = WaitForActions(engine, DynamicNavBakeShowcaseIds.RtsMapId);
        DrainSpawnAndNavBootstrap(engine, actions);
        TickFrames(engine, 2);

        Assert.That(actions.ConstructionMode, Is.False);
        Assert.That(actions.SquadDeployed, Is.True);
        Assert.That(
            engine.GlobalContext.ContainsKey(CoreServiceKeys.ActiveInputOrderMapping.Name),
            Is.True,
            "Local order mapping must be installed for formal right-click.");

        Entity localPlayer = engine.GetService(CoreServiceKeys.LocalPlayerEntity);
        Assert.That(localPlayer, Is.Not.EqualTo(Entity.Null));
        ControlDomainQuery controlDomains = engine.GetService(CoreServiceKeys.ControlDomainQuery)
            ?? throw new InvalidOperationException("ControlDomainQuery is required.");
        EntityCollectionStore collections = engine.GetService(CoreServiceKeys.EntityCollectionStore)
            ?? throw new InvalidOperationException("EntityCollectionStore is required.");
        Assert.That(
            collections.TryGetView(localPlayer, EntityCollectionKeys.CommandSource, out EntityCollectionView commandView),
            Is.True,
            "Squad must already be selected into collection.command.source.");
        Entity[] commandActors = new Entity[commandView.Count];
        int actorCount = collections.CopyEntities(localPlayer, EntityCollectionKeys.CommandSource, commandActors);
        Assert.That(actorCount, Is.EqualTo(commandView.Count));
        Assert.That(actorCount, Is.GreaterThan(0));
        for (int i = 0; i < actorCount; i++)
        {
            Assert.That(
                controlDomains.IsControllableBy(localPlayer, commandActors[i]),
                Is.True,
                $"Selected actor {commandActors[i].Id} must be controllable before formal Command can authorize.");
        }

        OrderTypeRegistry orderTypes = engine.GetService(CoreServiceKeys.OrderTypeRegistry)
            ?? throw new InvalidOperationException("OrderTypeRegistry is required.");
        Assert.That(orderTypes.TryGetId(MassNavigationOrderKeys.Move, out int massMoveOrderTypeId), Is.True);

        // Click toward +Y (goal side) from the authored squad. Board LoadedChunkCapacity must absorb
        // MassNav command-focus streaming without throwing (was 64 and crashed the live window).
        float targetXCm = actions.ActiveConfig.Squad.CenterXCm;
        float targetYCm = actions.ActiveConfig.Squad.CenterYCm + 2400f;
        Vector2 goalWorldCm = new(targetXCm, targetYCm);
        MutableInputBackend backend = RequireMutableInputBackend(engine);
        int activeOrdersBefore = CountActiveMoveOrders(engine, commandActors.AsSpan(0, actorCount));
        Vector2[] positionsBefore = CaptureWorldPositions(engine, commandActors.AsSpan(0, actorCount));
        float centroidYBefore = AverageAxis(positionsBefore, axisY: true);

        Assert.DoesNotThrow(
            () => DriveRightClickWorld(engine, backend, goalWorldCm),
            "Right-click ground must not crash MassNav streaming / loaded-chunk capacity.");

        Assert.That(
            engine.GlobalContext.TryGetValue(LocalOrderSourceHelper.LastOrderDebugKey, out object? lastOrderObj) &&
            lastOrderObj is string lastOrder &&
            !string.IsNullOrWhiteSpace(lastOrder),
            Is.True,
            "Formal right-click must submit through LocalOrderSourceHelper (CoreInputMod.Debug.LastOrder).");
        string lastOrderText = lastOrderObj!.ToString() ?? string.Empty;
        object? groundDebug = engine.GlobalContext.TryGetValue(
            LocalOrderSourceHelper.LastGroundWorldDebugKey,
            out object? groundObj)
            ? groundObj
            : "<missing>";
        Assert.That(
            lastOrderText.Contains(MassNavigationOrderKeys.Move, StringComparison.OrdinalIgnoreCase) ||
            lastOrderText.Contains(massMoveOrderTypeId.ToString(), StringComparison.Ordinal),
            Is.True,
            $"Last formal order must be massNavigationMove. lastOrder='{lastOrderText}' ground='{groundDebug}'");
        Assert.That(
            CountActiveMoveOrders(engine, commandActors.AsSpan(0, actorCount)),
            Is.GreaterThan(activeOrdersBefore),
            $"Formal right-click must activate move orders. lastOrder='{lastOrderText}' ground='{groundDebug}'");

        Vector2 squadProbeStart = positionsBefore[0];
        string openCorridorPath = CaptureDirectPathProbe(
            engine.GetService(CoreServiceKeys.PathService),
            engine.GetService(CoreServiceKeys.PathStore),
            commandActors[0],
            squadProbeStart,
            goalWorldCm);
        Assert.That(
            openCorridorPath.Contains("(-6400,0)", StringComparison.Ordinal) ||
            openCorridorPath.Contains("(6400,0)", StringComparison.Ordinal) ||
            openCorridorPath.Contains("(0,-6400)", StringComparison.Ordinal) ||
            openCorridorPath.Contains("(0,6400)", StringComparison.Ordinal) ||
            openCorridorPath.Contains("(6400,-6400)", StringComparison.Ordinal) ||
            openCorridorPath.Contains("(-6400,-6400)", StringComparison.Ordinal) ||
            openCorridorPath.Contains("(6400,6400)", StringComparison.Ordinal) ||
            openCorridorPath.Contains("(-6400,6400)", StringComparison.Ordinal),
            Is.False,
            $"Open-gate formal march must not detour through chunk-corner portals. path={openCorridorPath} ground='{groundDebug}'.");

        TickFrames(engine, 90);
        Vector2[] positionsAfter = CaptureWorldPositions(engine, commandActors.AsSpan(0, actorCount));
        Assert.That(
            CountMovedActors(engine, commandActors.AsSpan(0, actorCount), positionsBefore),
            Is.GreaterThan(0),
            "Selected squad members must actually leave their spawn after a formal right-click move.");
        float centroidYAfter = AverageAxis(positionsAfter, axisY: true);
        Assert.That(
            centroidYAfter,
            Is.GreaterThan(centroidYBefore + 200f),
            $"Squad must advance toward the clicked +Y ground point. beforeY={centroidYBefore} afterY={centroidYAfter} targetY={targetYCm} ground='{groundDebug}' path={openCorridorPath}.");
    }

    // Feature: placing a building mid-march keeps the march going
    // Given the RTS squad is mid-formal right-click move
    // When the player places the pooled building on the live march corridor
    // Then move orders stay active and bake ticks do not throw MassNavigation route apply
    [Test]
    public void Feature_RtsScene_PlaceBuildingDuringFormalMove_KeepsMarchAlive()
    {
        using GameEngine engine = CreateEngineWithMutableInput("NavBakeDynamicRtsShowcaseMod", registerRecast: false);
        engine.LoadMap(DynamicNavBakeShowcaseIds.RtsMapId);
        DynamicNavBakeShowcaseActions actions = WaitForActions(engine, DynamicNavBakeShowcaseIds.RtsMapId);
        DrainSpawnAndNavBootstrap(engine, actions);
        TickFrames(engine, 2);

        MutableInputBackend backend = RequireMutableInputBackend(engine);
        Entity[] commandActors = actions.SquadEntities.ToArray();
        Assert.That(commandActors.Length, Is.GreaterThan(0));
        Vector2 goalWorldCm = new(actions.ActiveConfig.Squad.CenterXCm, actions.ActiveConfig.Squad.CenterYCm + 2400f);
        Assert.DoesNotThrow(() => DriveRightClickWorld(engine, backend, goalWorldCm));
        AssertFormalNavMeshRouteEventually(engine, actions, expectedSquadCount: actions.ActiveConfig.Squad.Count);
        TickFrames(engine, 30);
        int activeOrdersBeforePlace = CountActiveMoveOrders(engine, commandActors);
        Assert.That(activeOrdersBeforePlace, Is.GreaterThan(0), "March must be live before placing a building.");

        Assert.That(actions.TryEnterConstructionMode(engine, out string enterError), Is.True, enterError);
        // Gate (0,0) is off the +Y march line; place ahead on the corridor so the bake dirties the live route.
        actions.Runtime.EditTransaction.SetPreview(
            actions.ActiveConfig.Squad.CenterXCm,
            actions.ActiveConfig.Squad.CenterYCm + 1200,
            DynamicNavBakePlacementLegality.Legal);
        Assert.DoesNotThrow(
            () =>
            {
                Assert.That(actions.TryPlaceBuildingAtPreview(engine, out string placeError), Is.True, placeError);
                Assert.That(
                    CountActiveMoveOrders(engine, commandActors),
                    Is.EqualTo(activeOrdersBeforePlace),
                    "Placing a building must not cancel the player's formal march orders.");
                for (int frame = 0; frame < 512; frame++)
                {
                    engine.SetService(CoreServiceKeys.HostFrameIndex, frame);
                    TickFrames(engine, 1);
                    if (actions.PlayerNavState != DynamicNavBakePlayerNavState.Baking)
                    {
                        break;
                    }
                }
            },
            "Placing a building on the march corridor during an active formal march must not throw MassNavigation route apply.");
        Assert.That(actions.WallDeployedCount, Is.EqualTo(1));
        Assert.That(
            CountActiveMoveOrders(engine, commandActors),
            Is.EqualTo(activeOrdersBeforePlace),
            "After nav bake completes, the original march orders must still be active.");
    }

    // Feature: baked navigation lifecycle is visible through the shared Core presentation
    // Given the player enters either navigation showcase with a pooled building, a 64-tile resident window, and a four-tile bake budget
    // When they place that building through the formal construction place path
    // Then they first see thirteen local tiles pending and four rebuilding without an early generation commit
    // And after the bake completes they see all seventeen old/new footprint tiles committed
    [TestCase("NavBakeDynamicRtsShowcaseMod", DynamicNavBakeShowcaseIds.RtsMapId)]
    [TestCase("NavBakeOpenWorld64x64ShowcaseMod", DynamicNavBakeShowcaseIds.OpenWorldMapId)]
    public void Feature_BothScenes_PlayerBakeShowsCoreTileLifecycle(string sceneModId, string mapId)
    {
        using GameEngine engine = CreateEngine(sceneModId, registerRecast: false, installUi: true);
        _ = RequireUiRoot(engine);
        engine.LoadMap(mapId);
        DynamicNavBakeShowcaseActions actions = WaitForActions(engine, mapId);
        DrainSpawnAndNavBootstrap(engine, actions);
        TickFrames(engine, 2);

        RuntimeIncrementalNavMeshRebuildQueue queue = engine.GetService(CoreServiceKeys.RuntimeNavMeshRebuildQueue)
            ?? throw new InvalidOperationException("Player bake lifecycle requires RuntimeIncrementalNavMeshRebuildQueue.");
        NavMeshPresentationBuffer buffer = engine.GetService(CoreServiceKeys.NavMeshPresentationBuffer)
            ?? throw new InvalidOperationException("Player bake lifecycle requires the Core NavMeshPresentationBuffer.");
        Assert.That(actions.CaptureEvidence(engine).TileBudgetPerFixedTick, Is.EqualTo(4));
        Assert.That(queue.CommittedResidentWindowCount, Is.EqualTo(64));
        Assert.That(buffer.TileCount, Is.EqualTo(64));

        NavQueryTileSpace tileSpace = buffer.TileSpace;
        int gateTileX = (int)Math.Floor(
            (actions.ActiveConfig.Gate.CenterXCm - tileSpace.OriginXcm) /
            (double)tileSpace.TileWidthCm);
        int gateTileZ = (int)Math.Floor(
            (actions.ActiveConfig.Gate.CenterYCm - tileSpace.OriginZcm) /
            (double)tileSpace.TileHeightCm);
        int editXCm = checked(tileSpace.OriginXcm + gateTileX * tileSpace.TileWidthCm + tileSpace.TileWidthCm / 2);
        int editZCm = checked(tileSpace.OriginZcm + gateTileZ * tileSpace.TileHeightCm + tileSpace.TileHeightCm / 2);

        Assert.That(actions.TryEnterConstructionMode(engine, out string enterError), Is.True, enterError);
        actions.Runtime.EditTransaction.SetPreview(
            editXCm,
            editZCm,
            DynamicNavBakePlacementLegality.Legal);
        ulong generationBeforeBake = buffer.StoreGeneration;
        Assert.That(actions.TryPlaceBuildingAtPreview(engine, out string placeError), Is.True, placeError);
        Assert.That(actions.WallDeployedCount, Is.EqualTo(1));
        Assert.That(actions.PlayerNavState, Is.EqualTo(DynamicNavBakePlayerNavState.Baking));

        bool sawBudgetSlice = false;
        int maxPending = 0;
        int maxRebuilding = 0;
        int maxLifecycleCount = 0;
        for (int frame = 0; frame < 30; frame++)
        {
            TickFrames(engine, 1);
            int pending = CountTileState(buffer, NavMeshPresentationTileState.Pending);
            int rebuilding = CountTileState(buffer, NavMeshPresentationTileState.Rebuilding);
            maxPending = Math.Max(maxPending, pending);
            maxRebuilding = Math.Max(maxRebuilding, rebuilding);
            maxLifecycleCount = Math.Max(maxLifecycleCount, buffer.TileStateCount);
            if (pending != 13 || rebuilding != 4)
            {
                continue;
            }

            sawBudgetSlice = true;
            Assert.That(
                buffer.StoreGeneration,
                Is.EqualTo(generationBeforeBake),
                "The player must keep seeing the last committed NavMesh until the whole edit generation is ready.");
            AssertLifecycleCoordsInsideCommittedResidentWindow(buffer, queue, includeCommitted: false);
            break;
        }

        Assert.That(
            sawBudgetSlice,
            Is.True,
            $"Player never saw the authored 4/17 pooled-building bake slice. status={queue.Status} pending={queue.PendingTileCount} " +
            $"sealed={queue.SealedRemainingCount} maxPending={maxPending} maxRebuilding={maxRebuilding} " +
            $"maxLifecycle={maxLifecycleCount} generationBefore={generationBeforeBake} generationNow={buffer.StoreGeneration} " +
            $"lastRebuilt={actions.CaptureEvidence(engine).LastRebuiltTileCount} " +
            $"lastPublished={actions.CaptureEvidence(engine).LastPublishedCount}.");

        actions.DrainUntilIdle(engine, maxTicks: 4096);
        TickFrames(engine, 1);

        Assert.That(CountTileState(buffer, NavMeshPresentationTileState.Pending), Is.Zero);
        Assert.That(CountTileState(buffer, NavMeshPresentationTileState.Rebuilding), Is.Zero);
        Assert.That(CountTileState(buffer, NavMeshPresentationTileState.Committed), Is.EqualTo(17));
        Assert.That(buffer.StoreGeneration, Is.GreaterThan(generationBeforeBake));
        Assert.That(buffer.TileCount, Is.EqualTo(64), "A local edit must not evict the surrounding resident NavMesh window.");
        AssertLifecycleCoordsInsideCommittedResidentWindow(buffer, queue, includeCommitted: true);
    }

    // Feature: harness map switching keeps Core NavMesh presentation ownership
    // Given the player entered the RTS fortress and its navigation is ready
    // When the harness focuses Open World and then returns to RTS
    // Then each map becomes playable with its authored resident window and the same Core NavMesh presentation service
    [Test]
    public void Feature_PlayerSwitchesBetweenRtsAndOpenWorld_ResidentNavAndCorePresentationFollowFocus()
    {
        using GameEngine engine = CreateEngine("NavBakeDynamicRtsShowcaseMod", registerRecast: false, installUi: true);
        _ = RequireUiRoot(engine);
        engine.LoadMap(DynamicNavBakeShowcaseIds.RtsMapId);
        DynamicNavBakeShowcaseActions actions = WaitForActions(engine, DynamicNavBakeShowcaseIds.RtsMapId);
        DrainSpawnAndNavBootstrap(engine, actions);
        TickFrames(engine, 2);

        NavMeshPresentationState corePresentation = engine.GetService(CoreServiceKeys.NavMeshPresentationState)
            ?? throw new InvalidOperationException("Map switching requires the Core NavMeshPresentationState.");

        Assert.That(
            actions.TrySwitchMap(engine, DynamicNavBakeShowcaseIds.OpenWorldMapId, out string openError),
            Is.True,
            openError);
        WaitForShowcaseMap(engine, actions, DynamicNavBakeShowcaseIds.OpenWorldMapId);
        DrainSpawnAndNavBootstrap(engine, actions);
        TickFrames(engine, 2);

        Assert.That(engine.CurrentMapSession?.MapId.Value, Is.EqualTo(DynamicNavBakeShowcaseIds.OpenWorldMapId));
        Assert.That(
            engine.MapSessions.GetSession(new Ludots.Core.Map.MapId(DynamicNavBakeShowcaseIds.RtsMapId)),
            Is.Null,
            "Switching maps must unload the previous showcase instead of retaining out-of-bounds entities.");
        Assert.That(actions.ActiveConfig.MapId, Is.EqualTo(DynamicNavBakeShowcaseIds.OpenWorldMapId));
        Assert.That(actions.CaptureEvidence(engine).CommittedResidentWindowCount, Is.EqualTo(64));
        Assert.That(engine.GetService(CoreServiceKeys.NavMeshPresentationState), Is.SameAs(corePresentation));
        Assert.That(corePresentation.Enabled, Is.True);

        Assert.That(
            actions.TrySwitchMap(engine, DynamicNavBakeShowcaseIds.RtsMapId, out string rtsError),
            Is.True,
            rtsError);
        WaitForShowcaseMap(engine, actions, DynamicNavBakeShowcaseIds.RtsMapId);
        DrainSpawnAndNavBootstrap(engine, actions);
        TickFrames(engine, 2);

        Assert.That(engine.CurrentMapSession?.MapId.Value, Is.EqualTo(DynamicNavBakeShowcaseIds.RtsMapId));
        Assert.That(
            engine.MapSessions.GetSession(new Ludots.Core.Map.MapId(DynamicNavBakeShowcaseIds.OpenWorldMapId)),
            Is.Null,
            "Returning to RTS must unload the 64x64 world and its entities.");
        Assert.That(actions.ActiveConfig.MapId, Is.EqualTo(DynamicNavBakeShowcaseIds.RtsMapId));
        Assert.That(actions.CaptureEvidence(engine).CommittedResidentWindowCount, Is.EqualTo(64));
        Assert.That(engine.GetService(CoreServiceKeys.NavMeshPresentationState), Is.SameAs(corePresentation));
        Assert.That(corePresentation.Enabled, Is.True);
    }

    // Feature: Open-world scout map shows every authored squad pin after disclosure
    // Given the player enters the 64x64 open-world Dynamic NavBake battlefield with ten scouts
    // When the local player knowledge disclosure finishes and the minimap presents markers
    // Then the scout map shows 10/10 pins instead of collecting ten and painting zero
    // And leaving the battlefield removes only this showcase's disclosure pairs
    [Test]
    public void Feature_OpenWorld_SquadKnowledgeDisclosure_ShowsTenMinimapPinsThenCleansUp()
    {
        using GameEngine engine = CreateEngine("NavBakeOpenWorld64x64ShowcaseMod", registerRecast: false, installUi: true);
        engine.LoadMap(DynamicNavBakeShowcaseIds.OpenWorldMapId);
        DynamicNavBakeShowcaseActions actions = WaitForActions(engine, DynamicNavBakeShowcaseIds.OpenWorldMapId);
        DrainSpawnAndNavBootstrap(engine, actions);
        TickFrames(engine, 4);

        Assert.That(actions.SquadEntities.Length, Is.EqualTo(10));
        Entity viewer = engine.GetService(CoreServiceKeys.LocalPlayerEntity);
        Assert.That(viewer, Is.Not.EqualTo(Entity.Null));
        Assert.That(engine.World.IsAlive(viewer), Is.True);

        var squadSnapshot = new Entity[actions.SquadEntities.Length];
        actions.SquadEntities.CopyTo(squadSnapshot);

        KnowledgeProjectionStore store = engine.GetService(CoreServiceKeys.KnowledgeProjectionStore)
            ?? throw new InvalidOperationException("Open-world scene requires KnowledgeProjectionStore.");
        int tick = KnowledgeProjectionConsumer.ResolveCurrentTick(engine.GlobalContext);
        for (int i = 0; i < squadSnapshot.Length; i++)
        {
            Entity target = squadSnapshot[i];
            Assert.That(
                store.TryGet(viewer, target, tick, out KnowledgeDisclosureRecord record),
                Is.True,
                $"Squad member[{i}] must be LiveVisible to the local player.");
            Assert.That(record.Presence, Is.EqualTo(KnowledgePresence.LiveVisible));
            Assert.That(record.Position, Is.EqualTo(KnowledgePositionAccess.Live));
        }

        MinimapRuntime minimap = engine.GetService(CoreServiceKeys.MinimapRuntime)
            ?? throw new InvalidOperationException("Open-world scene requires MinimapRuntime.");
        Assert.That(minimap.Visible, Is.True);
        Assert.That(
            minimap.MarkerCount,
            Is.EqualTo(10),
            "Scout map must collect all ten authored squad pins (Markers N/N footer total).");
        Assert.That(
            minimap.VisibleMarkerCount,
            Is.EqualTo(10),
            "Scout map must paint all ten disclosed squad pins (Markers N/N footer visible).");

        engine.UnloadMap(DynamicNavBakeShowcaseIds.OpenWorldMapId);
        TickFrames(engine, 1);

        for (int i = 0; i < squadSnapshot.Length; i++)
        {
            Entity target = squadSnapshot[i];
            Assert.That(
                store.TryGet(viewer, target, tick, out _),
                Is.False,
                $"Unload must remove showcase-owned disclosure for squad member[{i}] without ClearViewer.");
        }
    }

    // Feature: Leaving the open-world battlefield restores prior scout knowledge
    // Given another system already marked a scout as Known before this showcase took over LiveVisible
    // And a different system later overwrote one scout with a foreign disclosure
    // When the player leaves the battlefield
    // Then Known scouts are restored, foreign overwrites stay, and Markers 10/10 still held while owned
    [Test]
    public void Feature_OpenWorld_SquadKnowledgeDisclosure_RestoresPreviousRecordOnUnload()
    {
        using GameEngine engine = CreateEngine("NavBakeOpenWorld64x64ShowcaseMod", registerRecast: false, installUi: true);
        engine.LoadMap(DynamicNavBakeShowcaseIds.OpenWorldMapId);
        DynamicNavBakeShowcaseActions actions = WaitForActions(engine, DynamicNavBakeShowcaseIds.OpenWorldMapId);
        DrainSpawnAndNavBootstrap(engine, actions);
        TickFrames(engine, 4);

        Assert.That(actions.SquadEntities.Length, Is.EqualTo(10));
        Entity viewer = engine.GetService(CoreServiceKeys.LocalPlayerEntity);
        Assert.That(viewer, Is.Not.EqualTo(Entity.Null));

        var squadSnapshot = new Entity[actions.SquadEntities.Length];
        actions.SquadEntities.CopyTo(squadSnapshot);

        KnowledgeProjectionStore store = engine.GetService(CoreServiceKeys.KnowledgeProjectionStore)
            ?? throw new InvalidOperationException("Open-world scene requires KnowledgeProjectionStore.");
        int tick = KnowledgeProjectionConsumer.ResolveCurrentTick(engine.GlobalContext);

        MinimapRuntime minimap = engine.GetService(CoreServiceKeys.MinimapRuntime)
            ?? throw new InvalidOperationException("Open-world scene requires MinimapRuntime.");
        Assert.That(minimap.MarkerCount, Is.EqualTo(10));
        Assert.That(minimap.VisibleMarkerCount, Is.EqualTo(10));

        // Release showcase ownership, seed Known as the prior system record, then re-take LiveVisible.
        actions.Runtime.ClearOpenWorldSquadKnowledge(engine);
        var empty = KnowledgeIdMask256.Empty;
        var known = new KnowledgeDisclosureRecord(
            KnowledgePresence.Known,
            KnowledgePositionAccess.LastKnown,
            empty,
            empty,
            empty,
            viewer,
            observedTick: tick,
            expiryTick: 0,
            confidencePermille: 750,
            revision: 0);
        for (int i = 0; i < squadSnapshot.Length; i++)
        {
            store.Upsert(viewer, squadSnapshot[i], in known);
        }

        actions.Runtime.RefreshOpenWorldSquadKnowledge(engine);
        for (int i = 0; i < squadSnapshot.Length; i++)
        {
            Assert.That(store.TryGet(viewer, squadSnapshot[i], tick, out KnowledgeDisclosureRecord live), Is.True);
            Assert.That(live.Presence, Is.EqualTo(KnowledgePresence.LiveVisible));
        }

        Assert.That(minimap.MarkerCount, Is.EqualTo(10));
        Assert.That(minimap.VisibleMarkerCount, Is.EqualTo(10));

        var foreign = new KnowledgeDisclosureRecord(
            KnowledgePresence.HiddenWithSource,
            KnowledgePositionAccess.None,
            empty,
            empty,
            empty,
            viewer,
            observedTick: tick + 1,
            expiryTick: 0,
            confidencePermille: 100,
            revision: 0);
        store.Upsert(viewer, squadSnapshot[9], in foreign);

        engine.UnloadMap(DynamicNavBakeShowcaseIds.OpenWorldMapId);
        TickFrames(engine, 1);

        for (int i = 0; i < 9; i++)
        {
            Assert.That(
                store.TryGet(viewer, squadSnapshot[i], tick, out KnowledgeDisclosureRecord restored),
                Is.True,
                $"Unload must restore the prior Known disclosure for squad member[{i}].");
            Assert.That(restored.Presence, Is.EqualTo(KnowledgePresence.Known));
            Assert.That(restored.Position, Is.EqualTo(KnowledgePositionAccess.LastKnown));
            Assert.That(restored.ConfidencePermille, Is.EqualTo(750));
        }

        Assert.That(
            store.TryGet(viewer, squadSnapshot[9], tick, out KnowledgeDisclosureRecord keptForeign),
            Is.True,
            "Unload must leave foreign semantic overwrites untouched.");
        Assert.That(keptForeign.Presence, Is.EqualTo(KnowledgePresence.HiddenWithSource));
        Assert.That(keptForeign.ConfidencePermille, Is.EqualTo(100));
    }

    private static ulong HashCommittedResidentWindow(RuntimeIncrementalNavMeshRebuildQueue queue)
    {
        int advertised = queue.CommittedResidentWindowCount;
        if (advertised <= 0)
        {
            throw new InvalidOperationException(
                "HashCommittedResidentWindow requires a nonempty committed resident window.");
        }

        var tiles = new NavBakeTileCoord[advertised];
        int copied = queue.CopyCommittedResidentWindow(tiles);
        if (copied != advertised)
        {
            throw new InvalidOperationException(
                $"Committed resident window advertised {advertised} tiles but copied {copied}.");
        }

        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offset;
        hash ^= (ulong)(uint)copied;
        hash *= prime;
        for (int i = 0; i < copied; i++)
        {
            hash ^= unchecked((ulong)(uint)tiles[i].ChunkX);
            hash *= prime;
            hash ^= unchecked((ulong)(uint)tiles[i].ChunkY);
            hash *= prime;
        }

        return hash;
    }

    private static void AssertAllSquadMembersInsideCommittedResidentBounds(
        GameEngine engine,
        DynamicNavBakeShowcaseActions actions,
        RuntimeIncrementalNavMeshRebuildQueue queue)
    {
        ResolveCommittedResidentWorldBounds(engine, queue, out int minX, out int minZ, out int maxX, out int maxZ);
        ReadOnlySpan<Entity> squad = actions.SquadEntities;
        Assert.That(squad.Length, Is.GreaterThan(0), "Open-world continuation requires a bound squad.");
        for (int i = 0; i < squad.Length; i++)
        {
            Entity entity = squad[i];
            Assert.That(entity, Is.Not.EqualTo(Entity.Null), $"Squad member[{i}] must remain bound.");
            Assert.That(engine.World.IsAlive(entity), Is.True, $"Squad member[{i}] must remain alive.");
            Assert.That(
                engine.World.TryGet(entity, out WorldPositionCm position),
                Is.True,
                $"Squad member[{i}] must expose WorldPositionCm.");
            WorldCmInt2 world = position.ToWorldCmInt2();
            Assert.That(
                world.X >= minX && world.X < maxX && world.Y >= minZ && world.Y < maxZ,
                Is.True,
                $"After the corridor slide, every squad member must stand inside the committed resident window. " +
                $"member[{i}]=({world.X},{world.Y}) window=[{minX},{minZ}]-[{maxX},{maxZ}] (exclusive max).");
        }
    }

    private static void ResolveCommittedResidentWorldBounds(
        GameEngine engine,
        RuntimeIncrementalNavMeshRebuildQueue queue,
        out int minX,
        out int minZ,
        out int maxX,
        out int maxZ)
    {
        if (!engine.TryGetService(CoreServiceKeys.NavTriangleSurface, out NavTriangleSurfaceTileIndex? triangleSurface) ||
            triangleSurface == null)
        {
            throw new InvalidOperationException(
                "Asserting squad residency requires NavTriangleSurfaceTileIndex.");
        }

        int advertised = queue.CommittedResidentWindowCount;
        if (advertised <= 0)
        {
            throw new InvalidOperationException(
                "Asserting squad residency requires a nonempty committed resident window.");
        }

        var tiles = new NavBakeTileCoord[advertised];
        int copied = queue.CopyCommittedResidentWindow(tiles);
        if (copied != advertised)
        {
            throw new InvalidOperationException(
                $"Committed resident window advertised {advertised} tiles but copied {copied}.");
        }

        NavTriangleSurfaceTileGrid grid = triangleSurface.Grid;
        int minChunkX = int.MaxValue;
        int minChunkZ = int.MaxValue;
        int maxChunkX = int.MinValue;
        int maxChunkZ = int.MinValue;
        for (int i = 0; i < copied; i++)
        {
            NavBakeTileCoord tile = tiles[i];
            minChunkX = Math.Min(minChunkX, tile.ChunkX);
            minChunkZ = Math.Min(minChunkZ, tile.ChunkY);
            maxChunkX = Math.Max(maxChunkX, tile.ChunkX);
            maxChunkZ = Math.Max(maxChunkZ, tile.ChunkY);
        }

        minX = checked(grid.OriginXcm + checked(minChunkX * grid.TileWidthCm));
        minZ = checked(grid.OriginZcm + checked(minChunkZ * grid.TileHeightCm));
        maxX = checked(grid.OriginXcm + checked((maxChunkX + 1) * grid.TileWidthCm));
        maxZ = checked(grid.OriginZcm + checked((maxChunkZ + 1) * grid.TileHeightCm));
    }

    private static string BuildHexRtsMoveDiagnostic(
        GameEngine engine,
        DynamicNavBakeShowcaseActions actions,
        RuntimeIncrementalNavMeshRebuildQueue queue,
        string moveError)
    {
        DynamicNavBakeShowcaseConfig config = actions.ActiveConfig;
        ResolveCommittedResidentWorldBounds(engine, queue, out int minX, out int minZ, out int maxX, out int maxZ);
        NavQueryServiceRegistry registry = engine.GetService(CoreServiceKeys.NavQueryServices)
            ?? throw new InvalidOperationException("Hex RTS move diagnostic requires NavQueryServices.");
        if (!registry.TryGetStore(0, 0, out NavTileStore store))
        {
            throw new InvalidOperationException("Hex RTS move diagnostic requires layer 0 profile 0 NavTileStore.");
        }

        NavTile[] tiles = store.SnapshotLoadedTiles();
        int nonEmptyTiles = 0;
        int totalTriangles = 0;
        int minTileX = int.MaxValue;
        int minTileZ = int.MaxValue;
        int maxTileX = int.MinValue;
        int maxTileZ = int.MinValue;
        for (int i = 0; i < tiles.Length; i++)
        {
            NavTile tile = tiles[i];
            if (tile.TriangleCount <= 0 || tile.VertexCount <= 0)
            {
                continue;
            }

            nonEmptyTiles++;
            totalTriangles = checked(totalTriangles + tile.TriangleCount);
            minTileX = Math.Min(minTileX, tile.TileId.ChunkX);
            minTileZ = Math.Min(minTileZ, tile.TileId.ChunkY);
            maxTileX = Math.Max(maxTileX, tile.TileId.ChunkX);
            maxTileZ = Math.Max(maxTileZ, tile.TileId.ChunkY);
        }

        bool queryCreated = registry.TryCreateQuery(0, 0, NavAreaCostTable.CreateDefault(), out NavQueryService query);
        ResolveSquadCentroid(engine, actions, out int liveStartX, out int liveStartZ);
        bool authoredStartProjected = queryCreated && query.TryProject(config.Squad.CenterXCm, config.Squad.CenterYCm, out _);
        bool liveStartProjected = queryCreated && query.TryProject(liveStartX, liveStartZ, out _);
        bool goalProjected = queryCreated && query.TryProject(config.Goal.XCm, config.Goal.YCm, out _);
        NavPathStatus authoredDirectStatus = queryCreated
            ? query.TryFindPath(config.Squad.CenterXCm, config.Squad.CenterYCm, config.Goal.XCm, config.Goal.YCm).Status
            : NavPathStatus.NotReady;
        NavPathStatus liveDirectStatus = queryCreated
            ? query.TryFindPath(liveStartX, liveStartZ, config.Goal.XCm, config.Goal.YCm).Status
            : NavPathStatus.NotReady;
        string rawDetour = "<not-created>";
        if (queryCreated)
        {
            try
            {
                NavPathResult rawPath = DetourNavQueryEngine.FindPath(
                    tiles,
                    layer: 0,
                    areaCosts: NavAreaCostTable.CreateDefault(),
                    tileWidthCm: registry.TileSpace.TileWidthCm,
                    tileHeightCm: registry.TileSpace.TileHeightCm,
                    startXcm: liveStartX,
                    startZcm: liveStartZ,
                    goalXcm: config.Goal.XCm,
                    goalZcm: config.Goal.YCm,
                    maxPortals: 256);
                rawDetour = rawPath.Status.ToString();
            }
            catch (Exception ex)
            {
                rawDetour = ex.Message;
            }
        }

        string tileRange = nonEmptyTiles > 0
            ? $"[{minTileX},{minTileZ}]-[{maxTileX},{maxTileZ}]"
            : "<empty>";
        string routeTileSummary = BuildHexRouteTileSummary(
            tiles,
            registry.TileSpace,
            config.Squad.CenterXCm,
            config.Squad.CenterYCm,
            config.Gate.CenterXCm,
            config.Gate.CenterYCm,
            config.Goal.XCm,
            config.Goal.YCm);
        return
            $"Hex RTS move failed: {moveError}; " +
            $"status={actions.LastStatus}; path={actions.LastPathStatus}; orch={actions.PathOrchestrationState}; " +
            $"queue={queue.Status}; pending={queue.PendingTileCount}; transition={queue.HasResidentWindowTransition}; " +
            $"committed={queue.CommittedResidentWindowCount}; resident={queue.ResidentWindowCount}; " +
            $"window=[{minX},{minZ}]-[{maxX},{maxZ}]; " +
            $"authoredStart=({config.Squad.CenterXCm},{config.Squad.CenterYCm}) projected={authoredStartProjected}; " +
            $"liveStart=({liveStartX},{liveStartZ}) projected={liveStartProjected}; " +
            $"goal=({config.Goal.XCm},{config.Goal.YCm}) projected={goalProjected}; " +
            $"authoredDirect={authoredDirectStatus}; liveDirect={liveDirectStatus}; rawDetour={rawDetour}; " +
            $"storeResident={store.ResidentCount}; snapshot={tiles.Length}; nonEmpty={nonEmptyTiles}; tris={totalTriangles}; tiles={tileRange}; " +
            $"routeTiles={routeTileSummary}.";
    }

    private static string BuildHexRouteTileSummary(
        IReadOnlyList<NavTile> tiles,
        NavQueryTileSpace tileSpace,
        int startXcm,
        int startZcm,
        int wallXcm,
        int wallZcm,
        int goalXcm,
        int goalZcm)
    {
        var requested = new HashSet<NavTileId>
        {
            LocateTile(tileSpace, startXcm, startZcm),
            LocateTile(tileSpace, wallXcm, wallZcm),
            LocateTile(tileSpace, goalXcm, goalZcm)
        };
        var summary = new List<string>(requested.Count);
        for (int i = 0; i < tiles.Count; i++)
        {
            NavTile tile = tiles[i];
            if (!requested.Contains(tile.TileId))
            {
                continue;
            }

            int west = 0;
            int south = 0;
            int east = 0;
            int north = 0;
            ReadOnlySpan<NavBorderPortal> portals = tile.ActivePortals;
            for (int p = 0; p < portals.Length; p++)
            {
                switch (portals[p].Side)
                {
                    case NavPortalSide.West: west++; break;
                    case NavPortalSide.South: south++; break;
                    case NavPortalSide.East: east++; break;
                    case NavPortalSide.North: north++; break;
                }
            }

            summary.Add($"({tile.TileId.ChunkX},{tile.TileId.ChunkY}):tri={tile.TriangleCount},portals=W{west}/S{south}/E{east}/N{north}");
        }

        return summary.Count == 0 ? "<missing>" : string.Join(",", summary);
    }

    private static NavTileId LocateTile(in NavQueryTileSpace tileSpace, int worldXcm, int worldZcm)
    {
        int chunkX = MathUtil.FloorDiv(checked(worldXcm - tileSpace.OriginXcm), tileSpace.TileWidthCm);
        int chunkZ = MathUtil.FloorDiv(checked(worldZcm - tileSpace.OriginZcm), tileSpace.TileHeightCm);
        return new NavTileId(chunkX, chunkZ, layer: 0);
    }

    private static string BuildHexRtsBakeDiagnostic(
        GameEngine engine,
        RuntimeIncrementalNavMeshRebuildQueue queue,
        string moveError)
    {
        RuntimeNavTriangleSurfaceService surfaceService = engine.GetService(CoreServiceKeys.RuntimeNavTriangleSurface)
            ?? throw new InvalidOperationException("Hex RTS bake diagnostic requires RuntimeNavTriangleSurface.");
        NavTriangleSurfaceTileIndex surface = surfaceService.Published;
        RuntimeNavObstacleSnapshot obstacles = engine.GetService(CoreServiceKeys.RuntimeNavMeshObstacles)
            ?? throw new InvalidOperationException("Hex RTS bake diagnostic requires RuntimeNavMeshObstacles.");
        NavQueryServiceRegistry registry = engine.GetService(CoreServiceKeys.NavQueryServices)
            ?? throw new InvalidOperationException("Hex RTS bake diagnostic requires NavQueryServices.");
        if (!registry.TryGetStore(0, 0, out NavTileStore store))
        {
            throw new InvalidOperationException("Hex RTS bake diagnostic requires layer 0 profile 0 NavTileStore.");
        }

        NavTile[] tiles = store.SnapshotLoadedTiles();
        int nonEmptyTiles = 0;
        int totalTriangles = 0;
        int surfaceTrianglesInResident = 0;
        for (int i = 0; i < tiles.Length; i++)
        {
            NavTile tile = tiles[i];
            if (tile.TriangleCount > 0 && tile.VertexCount > 0)
            {
                nonEmptyTiles++;
                totalTriangles = checked(totalTriangles + tile.TriangleCount);
            }

            surfaceTrianglesInResident = checked(
                surfaceTrianglesInResident + surface.GetTriangleIndices(tile.TileId.ChunkX, tile.TileId.ChunkY).Length);
        }

        return
            $"Hex RTS walled move failed: {moveError}; queue={queue.Status}; pending={queue.PendingTileCount}; " +
            $"committed={queue.CommittedResidentWindowCount}; storeResident={store.ResidentCount}; " +
            $"storeNonEmpty={nonEmptyTiles}; storeTriangles={totalTriangles}; " +
            $"surfaceTriangles={surface.Surface.TriangleCount}; surfaceTrianglesInResident={surfaceTrianglesInResident}; " +
            $"obstacles={obstacles.ObstacleCount}; surfaceGeneration={surfaceService.ContentGeneration}.";
    }

    private static void ResolveSquadCentroid(
        GameEngine engine,
        DynamicNavBakeShowcaseActions actions,
        out int xCm,
        out int zCm)
    {
        ReadOnlySpan<Entity> squad = actions.SquadEntities;
        if (squad.Length <= 0)
        {
            xCm = actions.ActiveConfig.Squad.CenterXCm;
            zCm = actions.ActiveConfig.Squad.CenterYCm;
            return;
        }

        long sumX = 0;
        long sumZ = 0;
        int count = 0;
        for (int i = 0; i < squad.Length; i++)
        {
            Entity entity = squad[i];
            if (entity == Entity.Null || !engine.World.IsAlive(entity) ||
                !engine.World.TryGet(entity, out WorldPositionCm position))
            {
                continue;
            }

            WorldCmInt2 world = position.ToWorldCmInt2();
            sumX += world.X;
            sumZ += world.Y;
            count++;
        }

        if (count <= 0)
        {
            xCm = actions.ActiveConfig.Squad.CenterXCm;
            zCm = actions.ActiveConfig.Squad.CenterYCm;
            return;
        }

        xCm = checked((int)(sumX / count));
        zCm = checked((int)(sumZ / count));
    }

    private static void AssertAlgorithmSwitch(GameEngine engine, DynamicNavBakeShowcaseActions actions, NavBakeAlgorithmKind algorithm)
    {
        RuntimeIncrementalNavMeshRebuildQueue queue = engine.GetService(CoreServiceKeys.RuntimeNavMeshRebuildQueue)!;
        if (queue.CurrentAlgorithm == algorithm && !queue.HasRequestedAlgorithm && queue.Status == RuntimeNavMeshRebuildStatus.Idle)
        {
            return;
        }

        if (!actions.TrySwitchAlgorithm(engine, algorithm, out string error))
        {
            Assert.Fail($"Algorithm switch failed for '{NavBakeNames.FormatAlgorithm(algorithm)}': {error}");
        }

        actions.DrainUntilIdle(engine, maxTicks: 8192);
        Assert.That(queue.CurrentAlgorithm, Is.EqualTo(algorithm));
        Assert.That(queue.HasRequestedAlgorithm, Is.False);
    }

    // Feature: Dynamic NavBake squad move becomes a formal NavMesh route
    // Given either dynamic NavBake scene with squad profile light and PreferMesh Humanoid pathing
    // When the player deploys and commands a move to the goal
    // Then PathingConfig resolves Humanoid to light and the move becomes an active formal NavMesh route
    [TestCase("NavBakeDynamicRtsShowcaseMod", DynamicNavBakeShowcaseIds.RtsMapId)]
    [TestCase("NavBakeOpenWorld64x64ShowcaseMod", DynamicNavBakeShowcaseIds.OpenWorldMapId)]
    public void Feature_BothScenes_HumanoidLightPathing_SubmittedMoveCreatesFormalNavMeshRoute(
        string sceneModId,
        string mapId)
    {
        using GameEngine engine = CreateEngine(sceneModId, registerRecast: false);
        engine.LoadMap(mapId);
        DynamicNavBakeShowcaseActions actions = WaitForActions(engine, mapId);
        DrainSpawnAndNavBootstrap(engine, actions);
        AssertAlgorithmSwitch(engine, actions, NavBakeAlgorithmKind.Cdt);
        Assert.That(actions.SquadDeployed, Is.True);
        AssertPathingHumanoidMapsToLight(engine);
        Assert.That(actions.TryCommandMoveToGoal(engine, out string moveError), Is.True, moveError);
        AssertFormalNavMeshRouteEventually(engine, actions);
    }

    private static void AssertPathingHumanoidMapsToLight(GameEngine engine)
    {
        PathingConfig pathing = engine.GetService(CoreServiceKeys.PathingConfig)
            ?? throw new InvalidOperationException("PathingConfig service is required for DynamicNavBake formal routes.");
        PathingAgentTypeConfig? humanoid = null;
        for (int i = 0; i < pathing.AgentTypes.Count; i++)
        {
            PathingAgentTypeConfig agent = pathing.AgentTypes[i];
            if (string.Equals(agent.Id, "Humanoid", StringComparison.Ordinal))
            {
                humanoid = agent;
                break;
            }
        }

        Assert.That(humanoid, Is.Not.Null, "DynamicNavBake scenes must keep Humanoid pathing agent type.");
        Assert.That(humanoid!.ProfileId, Is.EqualTo("light"));
        Assert.That(humanoid.Selection.Mode, Is.EqualTo(PathSelectionMode.PreferMesh));
    }

    private static void AssertFormalNavMeshRouteEvidence(DynamicNavBakeShowcaseEvidence evidence, int? expectedSquadCount = null)
    {
        if (expectedSquadCount.HasValue)
        {
            Assert.That(
                evidence.FormalRouteAgentCount,
                Is.EqualTo(expectedSquadCount.Value),
                "All authored squad members must carry a ready formal MassNavigation route.");
        }
        else
        {
            Assert.That(
                evidence.FormalRouteAgentCount,
                Is.GreaterThan(0),
                "Immediately after the command, at least one bound squad member must have an active MassNavigation route.");
        }

        Assert.That(
            evidence.FormalRouteDomain,
            Is.EqualTo(PathDomain.NavMesh),
            "Formal MassNavigation route execution must resolve PreferMesh Humanoid paths on NavMesh.");
        Assert.That(
            evidence.FormalRouteMinWaypointCount,
            Is.GreaterThan(0),
            "Formal routes must expose a positive waypoint count (min across active squad routes).");
    }

    private static void AssertFormalNavMeshRouteEventually(
        GameEngine engine,
        DynamicNavBakeShowcaseActions actions,
        int maxTicks = 30,
        int? expectedSquadCount = null)
    {
        float fixedDt = Time.FixedDeltaTime;
        if (fixedDt <= 0f)
        {
            throw new InvalidOperationException(
                $"AssertFormalNavMeshRouteEventually requires Time.FixedDeltaTime > 0; got {fixedDt}.");
        }

        int requiredAgents = expectedSquadCount ?? 1;
        for (int tick = 0; tick < maxTicks; tick++)
        {
            DynamicNavBakeShowcaseEvidence evidence = actions.CaptureEvidence(engine);
            if (evidence.FormalRouteAgentCount >= requiredAgents &&
                evidence.FormalRouteDomain == PathDomain.NavMesh &&
                evidence.FormalRouteMinWaypointCount > 0)
            {
                AssertFormalNavMeshRouteEvidence(evidence, expectedSquadCount);
                return;
            }

            // PreferMesh route resolution runs on FixedStep; Tick(1/60) can skip it under FixedDeltaTime=0.02.
            engine.Tick(fixedDt);
        }

        DynamicNavBakeShowcaseEvidence finalEvidence = actions.CaptureEvidence(engine);
        AssertFormalNavMeshRouteEvidence(finalEvidence, expectedSquadCount);
    }

    private static int CountActiveSquadMoveOrders(GameEngine engine)
    {
        OrderTypeRegistry registry = engine.GetService(CoreServiceKeys.OrderTypeRegistry)
            ?? throw new InvalidOperationException("OrderTypeRegistry is required to count formal move orders.");
        if (!registry.TryGetId(MassNavigationOrderKeys.Move, out int moveOrderTypeId))
        {
            throw new InvalidOperationException(
                $"Formal move count requires order type '{MassNavigationOrderKeys.Move}'.");
        }

        int count = 0;
        var query = new QueryDescription().WithAll<OrderBuffer, CommandSourceSelectableState>();
        engine.World.Query(in query, (Entity entity, ref OrderBuffer buffer, ref CommandSourceSelectableState selectable) =>
        {
            _ = entity;
            if (selectable.IsEnabled == 0 || !buffer.HasActive)
            {
                return;
            }

            if (buffer.ActiveOrder.Order.OrderTypeId == moveOrderTypeId)
            {
                count++;
            }
        });
        return count;
    }

    private static int CountSquadMovePlanIntentsWithTarget(GameEngine engine, DynamicNavBakeShowcaseActions actions)
    {
        int count = 0;
        ReadOnlySpan<Entity> squad = actions.SquadEntities;
        for (int i = 0; i < squad.Length; i++)
        {
            Entity entity = squad[i];
            if (entity == Entity.Null || !engine.World.IsAlive(entity))
            {
                continue;
            }

            if (!engine.World.TryGet(entity, out MovePlanExecutionIntent intent))
            {
                continue;
            }

            if (intent.HasTarget != 0)
            {
                count++;
            }
        }

        return count;
    }

    private static void AssertEvidence(
        DynamicNavBakeShowcaseEvidence evidence,
        NavBakeAlgorithmKind algorithm,
        int expectedTriangleSnapshotCount,
        bool expectCorridor)
    {
        Assert.That(evidence.Algorithm, Is.EqualTo(NavBakeNames.FormatAlgorithm(algorithm)));
        Assert.That(evidence.TriangleSnapshotCount, Is.EqualTo(expectedTriangleSnapshotCount));
        Assert.That(evidence.LastNavBootstrapUriResolveCount, Is.EqualTo(0));
        Assert.That(evidence.FallbackCount, Is.EqualTo(0));
        Assert.That(evidence.MixedGenerationCount, Is.EqualTo(0));
        Assert.That(evidence.MapId, Is.Not.Empty);
        Assert.That(evidence.PathStatus, Is.Not.Empty);
        Assert.That(evidence.PathOrchestrationState, Is.Not.Empty);
        Assert.That(evidence.CommittedResidentWindowCount, Is.GreaterThan(0));
        Assert.That(evidence.LastGeneration, Is.GreaterThan(0UL));
        Assert.That(evidence.LastPublishedCount, Is.GreaterThan(0));
        Assert.That(evidence.LastDurationMs, Is.GreaterThanOrEqualTo(0.0));
        if (expectCorridor)
        {
            Assert.That(evidence.CoarseCorridorNodeCount, Is.GreaterThan(2));
        }
    }

    private static void AssertStructuralStability(GameEngine engine, DynamicNavBakeShowcaseActions actions)
    {
        var before = CollectStructuralEntities(engine);
        Assert.That(actions.TryBuildWall(engine, out _), Is.True);
        actions.DrainUntilIdle(engine, maxTicks: 2048);
        var built = CollectStructuralEntities(engine);
        Assert.That(built.Keys, Is.EquivalentTo(before.Keys));
        Assert.That(actions.TryDemolishWall(engine, out _), Is.True);
        actions.DrainUntilIdle(engine, maxTicks: 2048);
        var after = CollectStructuralEntities(engine);
        Assert.That(after.Keys, Is.EquivalentTo(before.Keys));
        foreach (Entity entity in before.Keys)
        {
            Assert.That(engine.World.Has<RuntimeNavMeshStructuralObstacle>(entity), Is.True);
            Assert.That(engine.World.Has<ManifestationObstacleIntent2D>(entity), Is.True);
        }
    }

    private static Dictionary<Entity, WorldPositionCm> CollectStructuralEntities(GameEngine engine)
    {
        var result = new Dictionary<Entity, WorldPositionCm>();
        var query = new QueryDescription().WithAll<RuntimeNavMeshStructuralObstacle, ManifestationObstacleIntent2D, WorldPositionCm>();
        engine.World.Query(in query, (Entity entity, ref WorldPositionCm position) =>
        {
            result[entity] = position;
        });
        return result;
    }

    private static void ResolveAuthoredResidentWindowBounds(
        DynamicNavBakeShowcaseConfig config,
        DynamicNavBakeShowcaseHotspotConfig hotspot,
        out int minX,
        out int minY,
        out int maxX,
        out int maxY)
    {
        int originX = checked(-config.WorldWidthCm / 2);
        int originY = checked(-config.WorldHeightCm / 2);
        minX = checked(originX + hotspot.ResidentOriginChunkX * config.ChunkSizeCm);
        minY = checked(originY + hotspot.ResidentOriginChunkZ * config.ChunkSizeCm);
        maxX = checked(minX + config.ResidentWidthChunks * config.ChunkSizeCm);
        maxY = checked(minY + config.ResidentHeightChunks * config.ChunkSizeCm);
    }

    private static List<(int X, int Y)> CaptureDeployedWallPositions(
        GameEngine engine,
        DynamicNavBakeShowcaseConfig config)
    {
        var result = new List<(int X, int Y)>();
        var query = new QueryDescription().WithAll<RuntimeNavMeshStructuralObstacle, WorldPositionCm>();
        engine.World.Query(in query, (Entity entity, ref WorldPositionCm position) =>
        {
            _ = entity;
            WorldCmInt2 world = position.ToWorldCmInt2();
            if (world.X == config.Parking.XCm && world.Y == config.Parking.YCm)
            {
                return;
            }

            result.Add((world.X, world.Y));
        });
        return result;
    }

    private static void AssertDeployedWallNavigationFootprints(GameEngine engine, DynamicNavBakeShowcaseConfig config)
    {
        int deployedCount = 0;
        var query = new QueryDescription().WithAll<RuntimeNavMeshStructuralObstacle, ManifestationObstacleIntent2D, WorldPositionCm>();
        engine.World.Query(in query, (Entity entity, ref ManifestationObstacleIntent2D intent, ref WorldPositionCm position) =>
        {
            _ = entity;
            WorldCmInt2 world = position.ToWorldCmInt2();
            if (world.X == config.Parking.XCm && world.Y == config.Parking.YCm)
            {
                return;
            }

            deployedCount++;
            Assert.That(intent.NavRadiusCm, Is.EqualTo(config.Gate.NavRadiusCm));
            Assert.That(intent.RadiusCm, Is.EqualTo(config.Gate.NavRadiusCm));
            Assert.That(intent.NavMinYcm, Is.EqualTo(config.Gate.NavMinYcm));
            Assert.That(intent.NavMaxYcm, Is.EqualTo(config.Gate.NavMaxYcm));
        });

        Assert.That(deployedCount, Is.EqualTo(config.Gate.SegmentCount));
    }

    private static Dictionary<Entity, (int X, int Y)> CaptureSquadPositions(GameEngine engine, DynamicNavBakeShowcaseActions actions)
    {
        _ = actions;
        var result = new Dictionary<Entity, (int X, int Y)>();
        if (!engine.GlobalContext.TryGetValue(DynamicNavBakeShowcaseIds.RuntimeServiceKey, out object? value) ||
            value is not DynamicNavBakeShowcaseActions)
        {
            return result;
        }

        // Positions are captured from mass-navigation agent templates on the showcase map.
        var query = new QueryDescription().WithAll<WorldPositionCm, MapEntity, EntityTemplateKeyRef>();
        engine.World.Query(in query, (Entity entity, ref WorldPositionCm position, ref MapEntity map, ref EntityTemplateKeyRef template) =>
        {
            _ = map;
            _ = template;
            if (!engine.World.Has<CommandSourceSelectableState>(entity))
            {
                return;
            }

            WorldCmInt2 world = position.ToWorldCmInt2();
            result[entity] = (world.X, world.Y);
        });
        return result;
    }

    private static void AssertSquadMoved(
        Dictionary<Entity, (int X, int Y)> before,
        Dictionary<Entity, (int X, int Y)> after)
    {
        Assert.That(SquadMoved(before, after), Is.True, "At least one squad member must move along the ordered local segment.");
    }

    private static bool SquadMoved(
        Dictionary<Entity, (int X, int Y)> before,
        Dictionary<Entity, (int X, int Y)> after)
    {
        if (before.Count <= 0 || after.Count != before.Count)
        {
            return false;
        }

        foreach (KeyValuePair<Entity, (int X, int Y)> pair in before)
        {
            if (!after.TryGetValue(pair.Key, out (int X, int Y) next))
            {
                return false;
            }

            long dx = next.X - pair.Value.X;
            long dy = next.Y - pair.Value.Y;
            if (dx * dx + dy * dy > 100L * 100L)
            {
                return true;
            }
        }

        return false;
    }

    private static void TickForMotion(GameEngine engine, DynamicNavBakeShowcaseActions actions, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            engine.Tick(DeltaTime);
            if (i % 30 == 0)
            {
                actions.DrainUntilIdle(engine, maxTicks: 1);
            }
        }
    }

    private static void DrainSpawnAndNavBootstrap(GameEngine engine, DynamicNavBakeShowcaseActions actions)
        => DynamicNavBakeShowcaseAcceptanceHarness.DrainSpawnAndNavBootstrap(engine, actions);

    private static DynamicNavBakeShowcaseActions WaitForActions(GameEngine engine, string mapId)
    {
        for (int i = 0; i < 30; i++)
        {
            if (engine.GlobalContext.TryGetValue(DynamicNavBakeShowcaseIds.RuntimeServiceKey, out object? value) &&
                value is DynamicNavBakeShowcaseActions actions)
            {
                return actions;
            }

            engine.Tick(DeltaTime);
        }

        throw new InvalidOperationException($"DynamicNavBakeShowcaseActions was not registered for map '{mapId}'.");
    }

    private static void WaitForShowcaseMap(
        GameEngine engine,
        DynamicNavBakeShowcaseActions actions,
        string mapId)
    {
        for (int i = 0; i < 120; i++)
        {
            if (actions.IsActive &&
                string.Equals(engine.CurrentMapSession?.MapId.Value, mapId, StringComparison.Ordinal) &&
                string.Equals(actions.ActiveConfig.MapId, mapId, StringComparison.Ordinal))
            {
                return;
            }

            engine.Tick(DeltaTime);
        }

        throw new InvalidOperationException(
            $"Dynamic NavBake map switch did not focus '{mapId}' within 120 frames; " +
            $"current='{engine.CurrentMapSession?.MapId.Value ?? "<none>"}'.");
    }

    private static GameEngine CreateEngine(string sceneModId, bool registerRecast, bool installUi = false)
    {
        string repoRoot = FindRepoRoot();
        var mods = new List<string>(SharedMods) { sceneModId };
        List<string> modPaths = RepoModPaths.ResolveExplicit(repoRoot, mods);
        var engine = new GameEngine();
        if (registerRecast)
        {
            // Test host composition only — production showcase never news RecastNavBakeAlgorithm.
            engine.RegisterExternalNavBakeAdapters(new RecastNavBakeAlgorithm());
        }

        engine.InitializeWithConfigPipeline(modPaths, Path.Combine(repoRoot, "assets"));
        InstallInput(engine);
        engine.RegisterPresentationAdapterCapabilities(
            new PresentationAdapterCapabilities(PresentationVisualCapabilities.NavMeshTileGeometry));
        if (installUi)
        {
            AcceptanceUiHostInstaller.Install(engine);
        }

        engine.Start();
        return engine;
    }

    private static GameEngine CreateEngineWithMutableInput(string sceneModId, bool registerRecast)
    {
        string repoRoot = FindRepoRoot();
        var mods = new List<string>(SharedMods) { sceneModId };
        List<string> modPaths = RepoModPaths.ResolveExplicit(repoRoot, mods);
        var engine = new GameEngine();
        if (registerRecast)
        {
            engine.RegisterExternalNavBakeAdapters(new RecastNavBakeAlgorithm());
        }

        engine.InitializeWithConfigPipeline(modPaths, Path.Combine(repoRoot, "assets"));
        InstallMutableProductionInput(engine);
        engine.RegisterPresentationAdapterCapabilities(
            new PresentationAdapterCapabilities(PresentationVisualCapabilities.NavMeshTileGeometry));
        engine.Start();
        return engine;
    }

    private static void InstallMutableProductionInput(GameEngine engine)
    {
        var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline!).Load();
        var backend = new MutableInputBackend();
        var inputHandler = new PlayerInputHandler(backend, inputConfig);
        for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
        {
            inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
        }

        // Production Raylib path: only InputHandler/Backend are host-owned. AuthoritativeInput stays
        // the FrozenInputActionReader owned by AuthoritativeInputSnapshotSystem.
        engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
        engine.SetService(CoreServiceKeys.InputBackend, (IInputBackend)backend);
        engine.SetService(CoreServiceKeys.UiCaptured, false);
    }

    private static MutableInputBackend RequireMutableInputBackend(GameEngine engine)
    {
        return engine.GetService(CoreServiceKeys.InputBackend) as MutableInputBackend
            ?? throw new InvalidOperationException("Formal right-click acceptance requires MutableInputBackend.");
    }

    private static void DriveRightClickWorld(GameEngine engine, MutableInputBackend backend, Vector2 worldCm)
    {
        backend.SetMousePosition(new Vector2(960f, 540f));
        backend.SetButton("<Mouse>/RightButton", false);
        TickFrames(engine, 1);

        AuthoritativeGroundPointerOverride groundOverride = engine.GetService(CoreServiceKeys.AuthoritativeGroundPointerOverride)
            ?? throw new InvalidOperationException("AuthoritativeGroundPointerOverride is required for formal right-click.");
        InteractionActionBindings bindings = InteractionActionBindingsResolver.Require(
            engine.GlobalContext,
            nameof(Feature_RtsScene_FormalRightClickGroundMovesSelectedSquad));
        groundOverride.Set(bindings.CommandActionId, worldCm);
        backend.SetButton("<Mouse>/RightButton", true);
        TickFrames(engine, 1);

        backend.SetButton("<Mouse>/RightButton", false);
        TickFrames(engine, 2);
    }

    private static int CountActiveMoveOrders(GameEngine engine, ReadOnlySpan<Entity> actors)
    {
        int count = 0;
        for (int i = 0; i < actors.Length; i++)
        {
            Entity entity = actors[i];
            if (engine.World.IsAlive(entity) &&
                engine.World.TryGet(entity, out OrderBuffer orders) &&
                orders.HasActive)
            {
                count++;
            }
        }

        return count;
    }

    private static Vector2[] CaptureWorldPositions(GameEngine engine, ReadOnlySpan<Entity> actors)
    {
        var positions = new Vector2[actors.Length];
        for (int i = 0; i < actors.Length; i++)
        {
            Entity entity = actors[i];
            if (!engine.World.IsAlive(entity) || !engine.World.TryGet(entity, out WorldPositionCm world))
            {
                positions[i] = Vector2.Zero;
                continue;
            }

            WorldCmInt2 point = world.ToWorldCmInt2();
            positions[i] = new Vector2(point.X, point.Y);
        }

        return positions;
    }

    private static string CaptureDirectPathProbe(
        IPathService? pathService,
        PathStore? pathStore,
        Entity actor,
        Vector2 startWorldCm,
        Vector2 goalWorldCm)
    {
        if (pathService == null || pathStore == null)
        {
            return "path=<no-service>";
        }

        var request = new PathRequest(
            requestId: 9000 + actor.Id,
            actor,
            PathDomain.NavMesh,
            agentTypeId: "Humanoid",
            PathEndpoint.FromWorldCm((int)MathF.Round(startWorldCm.X), (int)MathF.Round(startWorldCm.Y)),
            PathEndpoint.FromWorldCm((int)MathF.Round(goalWorldCm.X), (int)MathF.Round(goalWorldCm.Y)),
            new PathBudget(maxExpanded: 4096, maxPoints: 64));
        if (!pathService.TrySolve(in request, out PathResult path) ||
            path.Status != PathStatus.Found ||
            !path.Handle.IsValid)
        {
            return $"path=status={path.Status}/err={path.ErrorCode}/domain={path.ResolvedDomain}";
        }

        Span<int> xs = stackalloc int[64];
        Span<int> ys = stackalloc int[64];
        bool copied = pathService.TryCopyPath(in path.Handle, xs, ys, out int count);
        pathStore.Release(in path.Handle);
        if (!copied || count <= 0)
        {
            return "path=copy-failed";
        }

        var points = new List<string>(Math.Min(count, 6));
        for (int i = 0; i < count && i < 5; i++)
        {
            points.Add($"({xs[i]},{ys[i]})");
        }

        if (count > 5)
        {
            points.Add($"...({xs[count - 1]},{ys[count - 1]}) n={count}");
        }

        return "path=" + string.Join(">", points);
    }

    private static int CountMovedActors(GameEngine engine, ReadOnlySpan<Entity> actors, ReadOnlySpan<Vector2> before)
    {
        int moved = 0;
        for (int i = 0; i < actors.Length; i++)
        {
            Entity entity = actors[i];
            if (!engine.World.IsAlive(entity) || !engine.World.TryGet(entity, out WorldPositionCm world))
            {
                continue;
            }

            WorldCmInt2 point = world.ToWorldCmInt2();
            float dx = point.X - before[i].X;
            float dy = point.Y - before[i].Y;
            if ((dx * dx) + (dy * dy) >= 100f * 100f)
            {
                moved++;
            }
        }

        return moved;
    }

    private static float AverageAxis(ReadOnlySpan<Vector2> positions, bool axisY)
    {
        if (positions.Length <= 0)
        {
            return 0f;
        }

        float sum = 0f;
        for (int i = 0; i < positions.Length; i++)
        {
            sum += axisY ? positions[i].Y : positions[i].X;
        }

        return sum / positions.Length;
    }

    private sealed class MutableInputBackend : IInputBackend
    {
        private readonly HashSet<string> _pressedButtons = new(StringComparer.Ordinal);
        private Vector2 _mousePosition;

        public float GetAxis(string devicePath) => 0f;
        public bool GetButton(string devicePath) => _pressedButtons.Contains(devicePath);
        public Vector2 GetMousePosition() => _mousePosition;
        public float GetMouseWheel() => 0f;
        public void EnableIME(bool enable) { }
        public void SetIMECandidatePosition(int x, int y) { }
        public string GetCharBuffer() => string.Empty;

        public void SetMousePosition(Vector2 mousePosition) => _mousePosition = mousePosition;

        public void SetButton(string devicePath, bool pressed)
        {
            if (pressed)
            {
                _pressedButtons.Add(devicePath);
                return;
            }

            _pressedButtons.Remove(devicePath);
        }
    }

    private static UIRoot RequireUiRoot(GameEngine engine)
    {
        return engine.GetService(CoreServiceKeys.UIRoot) as UIRoot
            ?? throw new InvalidOperationException("DynamicNavBake UI acceptance requires UIRoot.");
    }

    private static void TickFrames(GameEngine engine, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.Tick(DeltaTime);
        }
    }

    private static int CountTileState(
        NavMeshPresentationBuffer buffer,
        NavMeshPresentationTileState expected)
    {
        int count = 0;
        ReadOnlySpan<NavMeshPresentationTileState> states = buffer.TileStates;
        for (int i = 0; i < states.Length; i++)
        {
            if (states[i] == expected)
            {
                count++;
            }
        }

        return count;
    }

    private static void AssertLifecycleCoordsInsideCommittedResidentWindow(
        NavMeshPresentationBuffer buffer,
        RuntimeIncrementalNavMeshRebuildQueue queue,
        bool includeCommitted)
    {
        var resident = new NavBakeTileCoord[queue.CommittedResidentWindowCount];
        int residentCount = queue.CopyCommittedResidentWindow(resident);
        ReadOnlySpan<NavBakeTileCoord> stateCoords = buffer.TileStateCoords;
        ReadOnlySpan<NavMeshPresentationTileState> states = buffer.TileStates;
        for (int i = 0; i < stateCoords.Length; i++)
        {
            if (!includeCommitted && states[i] == NavMeshPresentationTileState.Committed)
            {
                continue;
            }

            bool found = false;
            for (int residentIndex = 0; residentIndex < residentCount; residentIndex++)
            {
                if (resident[residentIndex].Equals(stateCoords[i]))
                {
                    found = true;
                    break;
                }
            }

            Assert.That(
                found,
                Is.True,
                $"Lifecycle tile ({stateCoords[i].ChunkX},{stateCoords[i].ChunkY}) is outside the player's committed resident window.");
        }
    }

    private static void AssertMinimalPlayerPanelMounted(UIRoot root)
    {
        UiScene scene = root.Scene ?? throw new InvalidOperationException("UI scene is not mounted.");
        scene.Layout(root.Width, root.Height);

        Assert.That(scene.FindByElementId(DynamicNavBakeShowcaseIds.PanelElementId), Is.Not.Null);
        Assert.That(scene.FindByElementId(DynamicNavBakeShowcaseIds.StatusTextElementId), Is.Not.Null);
        AssertButtonExposesAction(scene, DynamicNavBakeShowcaseIds.BuildBuildingButtonElementId);
        AssertButtonExposesAction(scene, DynamicNavBakeShowcaseIds.NavMeshVisibilityButtonElementId);

        Assert.That(scene.FindByElementId(DynamicNavBakeShowcaseIds.RecastButtonElementId), Is.Null);
        Assert.That(scene.FindByElementId(DynamicNavBakeShowcaseIds.CdtButtonElementId), Is.Null);
        Assert.That(scene.FindByElementId(DynamicNavBakeShowcaseIds.LayeredSpanButtonElementId), Is.Null);
        Assert.That(scene.FindByElementId(DynamicNavBakeShowcaseIds.RtsMapButtonElementId), Is.Null);
        Assert.That(scene.FindByElementId(DynamicNavBakeShowcaseIds.OpenWorldMapButtonElementId), Is.Null);
        Assert.That(scene.FindByElementId(DynamicNavBakeShowcaseIds.BuildingToolButtonElementId), Is.Null);
        Assert.That(scene.FindByElementId(DynamicNavBakeShowcaseIds.TerrainToolButtonElementId), Is.Null);
        Assert.That(scene.FindByElementId(DynamicNavBakeShowcaseIds.PlaceEditButtonElementId), Is.Null);
        Assert.That(scene.FindByElementId(DynamicNavBakeShowcaseIds.BakeButtonElementId), Is.Null);
        Assert.That(scene.FindByElementId(DynamicNavBakeShowcaseIds.RestoreButtonElementId), Is.Null);
        Assert.That(scene.FindByElementId(DynamicNavBakeShowcaseIds.DeploySquadButtonElementId), Is.Null);
        Assert.That(scene.FindByElementId(DynamicNavBakeShowcaseIds.MoveToGoalButtonElementId), Is.Null);
        Assert.That(scene.FindByElementId(DynamicNavBakeShowcaseIds.NextHotspotButtonElementId), Is.Null);
        Assert.That(scene.FindByElementId(DynamicNavBakeShowcaseIds.ReturnButtonElementId), Is.Null);
        Assert.That(scene.FindByElementId(DynamicNavBakeShowcaseIds.HotspotTextElementId), Is.Null);
        Assert.That(scene.FindByElementId(DynamicNavBakeShowcaseIds.ResidencyTextElementId), Is.Null);
        Assert.That(scene.FindByElementId(DynamicNavBakeShowcaseIds.PerformanceTextElementId), Is.Null);
    }

    private static void ClickTerrainRaiseBakeAndRestore(
        UIRoot uiRoot,
        GameEngine engine,
        DynamicNavBakeShowcaseActions actions)
    {
        _ = uiRoot;
        RuntimeNavTriangleSurfaceService surface = engine.GetService(CoreServiceKeys.RuntimeNavTriangleSurface)
            ?? throw new InvalidOperationException("Terrain UI acceptance requires RuntimeNavTriangleSurfaceService.");
        NavTriangleSurfaceTileIndex beforeImage = surface.Published;
        string inputHashBeforeRaise = DynamicNavBakeShowcaseEvidenceCapture.ComputeInputHash(beforeImage);
        ulong generationBeforeRaise = surface.ContentGeneration;

        Assert.That(actions.TrySetEditTool(engine, DynamicNavBakeEditTool.Terrain, out string toolError), Is.True, toolError);
        ResolveActiveEditPoint(actions, out int editXCm, out int editZCm);
        actions.Runtime.EditTransaction.SetPreview(
            editXCm,
            editZCm,
            DynamicNavBakePlacementLegality.Legal);
        Assert.That(actions.TryConfirmPlacement(engine, out string stageError), Is.True, stageError);
        TickFrames(engine, 1);
        Assert.That(actions.SelectedEditTool, Is.EqualTo(DynamicNavBakeEditTool.Terrain));
        Assert.That(actions.HasStagedEdit, Is.True, $"Terrain placement did not stage: {actions.LastStatus}");
        Assert.That(actions.TryBake(engine, out string bakeError), Is.True, bakeError);
        TickFrames(engine, 1);
        actions.DrainUntilIdle(engine, maxTicks: 4096);
        TickFrames(engine, 1);

        Assert.That(surface.ContentGeneration, Is.GreaterThan(generationBeforeRaise));
        Assert.That(surface.Published, Is.Not.SameAs(beforeImage));
        Assert.That(
            DynamicNavBakeShowcaseEvidenceCapture.ComputeInputHash(surface.Published),
            Is.Not.EqualTo(inputHashBeforeRaise),
            "Terrain Raise must change authoritative triangle geometry, not only bump generation state.");
        Assert.That(actions.PlayerNavState, Is.EqualTo(DynamicNavBakePlayerNavState.RouteUpdated));
        Assert.That(actions.WallDeployedCount, Is.EqualTo(0));
        ulong raisedGeneration = surface.ContentGeneration;

        Assert.That(actions.TryRestore(engine, out string restoreError), Is.True, restoreError);
        TickFrames(engine, 1);
        Assert.That(actions.HasStagedEdit, Is.True);
        Assert.That(actions.TryBake(engine, out string restoreBakeError), Is.True, restoreBakeError);
        Assert.That(
            surface.ContentGeneration,
            Is.GreaterThan(raisedGeneration),
            $"Terrain Restore Bake must publish the exact before-image immediately. status='{actions.LastStatus}' navState={actions.PlayerNavState} staged={actions.HasStagedEdit} bakeError='{restoreBakeError}'");
        TickFrames(engine, 1);
        actions.DrainUntilIdle(engine, maxTicks: 4096);
        TickFrames(engine, 1);

        Assert.That(surface.ContentGeneration, Is.GreaterThan(raisedGeneration));
        Assert.That(surface.Published, Is.SameAs(beforeImage),
            "Terrain Restore must republish the exact immutable before-image.");
        Assert.That(
            DynamicNavBakeShowcaseEvidenceCapture.ComputeInputHash(surface.Published),
            Is.EqualTo(inputHashBeforeRaise));
        Assert.That(actions.PlayerNavState, Is.EqualTo(DynamicNavBakePlayerNavState.RouteUpdated));
    }

    private static void ResolveActiveEditPoint(
        DynamicNavBakeShowcaseActions actions,
        out int xCm,
        out int zCm)
    {
        DynamicNavBakeShowcaseConfig config = actions.ActiveConfig;
        // Place inside the authored gate / initial resident focus, not a distant hotspot.
        xCm = config.Gate.CenterXCm;
        zCm = config.Gate.CenterYCm;
    }

    private static void AssertButtonExposesAction(UiScene scene, string elementId)
    {
        UiNode node = scene.FindByElementId(elementId)
            ?? throw new InvalidOperationException($"UI element '{elementId}' was not found.");
        Assert.That(node.ActionHandles.Count, Is.GreaterThan(0), $"UI element '{elementId}' must expose an action handle.");
    }

    private static string RequireElementText(UIRoot root, string elementId)
    {
        UiScene scene = root.Scene ?? throw new InvalidOperationException("UI scene is not mounted.");
        scene.Layout(root.Width, root.Height);
        UiNode node = scene.FindByElementId(elementId)
            ?? throw new InvalidOperationException($"UI element '{elementId}' was not found.");
        return node.TextContent ?? string.Empty;
    }

    private static void ClickElement(UIRoot root, string elementId)
    {
        UiScene scene = root.Scene ?? throw new InvalidOperationException("UI scene is not mounted.");
        scene.Layout(root.Width, root.Height);
        UiNode node = scene.FindByElementId(elementId)
            ?? throw new InvalidOperationException($"UI element '{elementId}' was not found.");
        Assert.That(node.ActionHandles.Count, Is.GreaterThan(0), $"UI element '{elementId}' must be clickable.");

        float x = node.LayoutRect.X + (node.LayoutRect.Width * 0.5f);
        float y = node.LayoutRect.Y + (node.LayoutRect.Height * 0.5f);
        UiNode? hitNode = scene.HitTest(x, y);
        Assert.That(
            hitNode?.ElementId,
            Is.EqualTo(elementId),
            $"Pointer click for '{elementId}' hit '{hitNode?.ElementId ?? hitNode?.TagName ?? "<none>"}' instead.");
        bool downHandled = root.HandleInput(new PointerEvent
        {
            PointerId = 0,
            Action = PointerAction.Down,
            Button = PointerButton.Left,
            X = x,
            Y = y
        });
        bool upHandled = root.HandleInput(new PointerEvent
        {
            PointerId = 0,
            Action = PointerAction.Up,
            Button = PointerButton.Left,
            X = x,
            Y = y
        });

        Assert.That(downHandled || upHandled, Is.True, $"UI element '{elementId}' did not handle pointer click.");
    }

    private static void InstallInput(GameEngine engine)
    {
        var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline!).Load();
        var inputHandler = new PlayerInputHandler(new NullInputBackend(), inputConfig);
        for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
        {
            inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
        }

        engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
        engine.SetService(CoreServiceKeys.AuthoritativeInput, inputHandler);
        engine.SetService(CoreServiceKeys.UiCaptured, false);
    }

    private sealed class NullInputBackend : IInputBackend
    {
        public float GetAxis(string devicePath) => 0f;
        public bool GetButton(string devicePath) => false;
        public System.Numerics.Vector2 GetMousePosition() => System.Numerics.Vector2.Zero;
        public float GetMouseWheel() => 0f;
        public void EnableIME(bool enable) { }
        public void SetIMECandidatePosition(int x, int y) { }
        public string GetCharBuffer() => string.Empty;
    }

    private static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "Ludots.sln")) || File.Exists(Path.Combine(dir, "showcase.registry.json")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Unable to locate repository root.");
    }
}
