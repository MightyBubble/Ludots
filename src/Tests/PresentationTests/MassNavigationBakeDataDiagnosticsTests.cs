using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Mathematics;
using Ludots.Core.Modding;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.Diagnostics;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.Pathing.Config;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Core.Engine;
using Ludots.Core.Navigation.Pathing;
using MassNavigationMod;
using MassNavigationMod.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class MassNavigationBakeDataDiagnosticsTests
    {
        [Test]
        public void BakeDataDiagnostics_ExposeLargeWorldMacroChunkContract()
        {
            LoadedConfigs loaded = LoadConfigs();
            string repoRoot = FindRepoRoot();
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", Path.Combine(repoRoot, "assets"));
            vfs.Mount("MassNavigationMod", Path.Combine(repoRoot, "mods", "capabilities", "navigation", "MassNavigationMod"));
            var worldSize = new WorldSizeSpec(new WorldAabbCm(-3_200_000, -3_200_000, 6_400_000, 6_400_000), 100);

            MassNavigationBakeDataDiagnostics diagnostics = MassNavigationBakeDataDiagnostics.Create(
                loaded.MassNavigation.MapId,
                worldSize,
                loaded.MassNavigation.BakeData,
                loaded.MassNavigation.World!,
                loaded.NavMesh,
                loaded.Pathing,
                navBakeDiagnostics: null,
                vfs,
                new[] { "MassNavigationMod" });

            Assert.That(MassNavigationBakeDataDiagnostics.SchemaVersion, Is.EqualTo("mass-navigation.bake-data-diagnostics.v1"));
            Assert.That(diagnostics.WorldWidthCm, Is.EqualTo(6_400_000));
            Assert.That(diagnostics.WorldHeightCm, Is.EqualTo(6_400_000));
            Assert.That(diagnostics.MacroChunkColumns, Is.EqualTo(256));
            Assert.That(diagnostics.MacroChunkRows, Is.EqualTo(256));
            Assert.That(diagnostics.MacroChunkCount, Is.EqualTo(65_536));
            Assert.That(diagnostics.MacroChunkSizeXCm, Is.EqualTo(25_000));
            Assert.That(diagnostics.MacroChunkSizeYCm, Is.EqualTo(25_000));
            Assert.That(diagnostics.ExpectedMacroAdjacencyEdgeCount, Is.EqualTo(130_560));

            AssertNotLoadedContract(diagnostics.NavMesh);
            AssertNotLoadedContract(diagnostics.RoadGraph);
            AssertNotLoadedContract(diagnostics.FlowField);
            Assert.That(diagnostics.StaticObstacle.TotalChunks, Is.EqualTo(65_536));
            Assert.That(diagnostics.StaticObstacle.BakedChunks, Is.EqualTo(40_000));
            Assert.That(diagnostics.StaticObstacle.NotLoadedChunks, Is.EqualTo(25_536));
            Assert.That(diagnostics.StaticObstacle.CoveragePercent, Is.EqualTo(61));
            Assert.That(diagnostics.TargetStaticObstacleCount, Is.EqualTo(40_000));
            Assert.That(diagnostics.AuthoredStaticObstacleCount, Is.EqualTo(40_000));
            Assert.That(diagnostics.StaticObstacleWorld, Is.Not.Null);
            Assert.That(diagnostics.StaticObstacleWorld!.MacroChunkCoverageCount, Is.EqualTo(40_000));
            Assert.That(diagnostics.StaticObstacleWorld.DistributionStrategy, Is.EqualTo("deterministic_macro_chunk_hash_permutation"));
            Assert.That(diagnostics.StaticObstacleWorld.RuntimeActivation.Strategy, Is.EqualTo("active_window_subset_to_mass_flow_solver"));
            Assert.That(diagnostics.HpaOverlayRequired, Is.True);
            Assert.That(diagnostics.PathInspectorRequired, Is.True);
            Assert.That(diagnostics.BakeOverlayRequired, Is.True);
        }

        [Test]
        public void BakeDataDiagnostics_ReadNavigationProfilesLayersAndCostsFromOfficialConfigs()
        {
            LoadedConfigs loaded = LoadConfigs();
            var worldSize = new WorldSizeSpec(new WorldAabbCm(-3_200_000, -3_200_000, 6_400_000, 6_400_000), 100);

            MassNavigationBakeDataDiagnostics diagnostics = MassNavigationBakeDataDiagnostics.Create(
                loaded.MassNavigation.MapId,
                worldSize,
                loaded.MassNavigation.BakeData,
                loaded.MassNavigation.World!,
                loaded.NavMesh,
                loaded.Pathing);

            Assert.That(diagnostics.NavMeshProfileCount, Is.GreaterThanOrEqualTo(5));
            Assert.That(diagnostics.NavMeshLayerCount, Is.GreaterThanOrEqualTo(4));
            Assert.That(diagnostics.NavMeshAreaCostCount, Is.GreaterThanOrEqualTo(7));

            Assert.That(loaded.NavMesh.Layers.Select(layer => layer.Id).ToArray(), Is.SupersetOf(new[] { "Ground", "Water", "Air", "Mountain" }));
            Assert.That(loaded.NavMesh.Profiles.Select(profile => profile.Id).ToArray(), Is.SupersetOf(new[] { "GroundLight", "GroundLarge", "Mountain", "Naval", "Air" }));

            AssertProfile(diagnostics.Profiles, "Infantry", "GroundLight", layer: 0, "AutoCheapest");
            AssertProfile(diagnostics.Profiles, "LargeVehicle", "GroundLarge", layer: 0, "PreferGraph");
            AssertProfile(diagnostics.Profiles, "Mountain", "Mountain", layer: 3, "AutoCheapest");
            AssertProfile(diagnostics.Profiles, "Naval", "Naval", layer: 1, "PreferMesh");
            AssertProfile(diagnostics.Profiles, "Air", "Air", layer: 2, "PreferMesh");

            MassNavigationBakeDataProfileSummary naval = diagnostics.Profiles.First(profile => profile.AgentTypeId == "Naval");
            Assert.That(naval.AreaCostSamples, Does.Contain("5:0.75"));
            Assert.That(naval.GraphRuleSummary, Does.Contain("shipping_lane"));
            Assert.That(naval.ForbiddenTagSummary, Does.Contain("land_only"));
        }

        [Test]
        public void BakeDataDiagnostics_FailFastWhenMacroChunksDoNotDivideBoard()
        {
            LoadedConfigs loaded = LoadConfigs();
            var worldSize = new WorldSizeSpec(new WorldAabbCm(-3_200_001, -3_200_000, 6_400_001, 6_400_000), 100);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                MassNavigationBakeDataDiagnostics.Create(
                    loaded.MassNavigation.MapId,
                    worldSize,
                    loaded.MassNavigation.BakeData,
                    loaded.MassNavigation.World!,
                    loaded.NavMesh,
                    loaded.Pathing))!;

            Assert.That(ex.Message, Does.Contain("must divide board world extent exactly"));
        }

        [Test]
        public void BakeDataDiagnostics_FailFastWhenPathingReferencesUnknownNavProfileLayerOrArea()
        {
            LoadedConfigs loaded = LoadConfigs();
            var worldSize = new WorldSizeSpec(new WorldAabbCm(-3_200_000, -3_200_000, 6_400_000, 6_400_000), 100);
            loaded.Pathing.AgentTypes[0].ProfileId = "MissingProfile";

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                MassNavigationBakeDataDiagnostics.Create(
                    loaded.MassNavigation.MapId,
                    worldSize,
                    loaded.MassNavigation.BakeData,
                    loaded.MassNavigation.World!,
                    loaded.NavMesh,
                    loaded.Pathing))!;

            Assert.That(ex.Message, Does.Contain("unknown navmesh profile"));
        }

        [Test]
        public void BakeDataDiagnostics_ReflectsNavBakeDiagnosticsDocumentWhenAvailable()
        {
            LoadedConfigs loaded = LoadConfigs();
            var worldSize = new WorldSizeSpec(new WorldAabbCm(-3_200_000, -3_200_000, 6_400_000, 6_400_000), 100);
            var document = new NavBakeDiagnosticsDocument
            {
                SchemaVersion = NavBakeDiagnosticsContract.SchemaVersion,
                MapId = loaded.MassNavigation.MapId,
                TargetChunkCount = 65_536,
            };
            document.LayerProfiles.Add(NavBakeLayerProfileSummary.Create(
                layer: 0,
                layerId: "Ground",
                profileId: "GroundLight",
                targetChunks: 65_536,
                bakedTiles: 60_000,
                failedTiles: 5,
                missingTiles: 6,
                dirtyTiles: 7,
                notLoadedTiles: 5_518));
            document.LayerProfiles.Add(NavBakeLayerProfileSummary.Create(
                layer: 1,
                layerId: "Water",
                profileId: "Naval",
                targetChunks: 65_536,
                bakedTiles: 30_000,
                failedTiles: 1,
                missingTiles: 2,
                dirtyTiles: 3,
                notLoadedTiles: 35_530));

            MassNavigationBakeDataDiagnostics diagnostics = MassNavigationBakeDataDiagnostics.Create(
                loaded.MassNavigation.MapId,
                worldSize,
                loaded.MassNavigation.BakeData,
                loaded.MassNavigation.World!,
                loaded.NavMesh,
                loaded.Pathing,
                document);

            Assert.That(diagnostics.NavMesh.TotalChunks, Is.EqualTo(131_072));
            Assert.That(diagnostics.NavMesh.BakedChunks, Is.EqualTo(90_000));
            Assert.That(diagnostics.NavMesh.FailedChunks, Is.EqualTo(6));
            Assert.That(diagnostics.NavMesh.MissingChunks, Is.EqualTo(8));
            Assert.That(diagnostics.NavMesh.DirtyChunks, Is.EqualTo(10));
            Assert.That(diagnostics.NavMesh.NotLoadedChunks, Is.EqualTo(41_048));
            Assert.That(diagnostics.NavMesh.IsComplete, Is.False);
        }

        [Test]
        public void BakeDataDiagnostics_ReflectsPartialActiveWindowNavBakeWithoutProductionOverclaim()
        {
            LoadedConfigs loaded = LoadConfigs();
            var worldSize = new WorldSizeSpec(new WorldAabbCm(-3_200_000, -3_200_000, 6_400_000, 6_400_000), 100);
            var document = new NavBakeDiagnosticsDocument
            {
                SchemaVersion = NavBakeDiagnosticsContract.SchemaVersion,
                MapId = loaded.MassNavigation.MapId,
                TargetChunkCount = 25,
                WorldChunkCount = 65_536,
                ActiveWindowMinChunkX = 126,
                ActiveWindowMinChunkY = 126,
                ActiveWindowMaxChunkX = 130,
                ActiveWindowMaxChunkY = 130,
                ActiveWindowChunkCount = 25,
                IsPartialCoverage = true,
            };
            document.LayerProfiles.Add(NavBakeLayerProfileSummary.Create(
                layer: 0,
                layerId: "Ground",
                profileId: "GroundLight",
                targetChunks: 25,
                bakedTiles: 25,
                failedTiles: 0,
                missingTiles: 0,
                dirtyTiles: 0,
                notLoadedTiles: 0));

            MassNavigationBakeDataDiagnostics diagnostics = MassNavigationBakeDataDiagnostics.Create(
                loaded.MassNavigation.MapId,
                worldSize,
                loaded.MassNavigation.BakeData,
                loaded.MassNavigation.World!,
                loaded.NavMesh,
                loaded.Pathing,
                document);

            Assert.That(diagnostics.NavMesh.TotalChunks, Is.EqualTo(65_536));
            Assert.That(diagnostics.NavMesh.BakedChunks, Is.EqualTo(25));
            Assert.That(diagnostics.NavMesh.NotLoadedChunks, Is.EqualTo(65_511));
            Assert.That(diagnostics.NavMesh.CoveragePercent, Is.EqualTo(0));
            Assert.That(diagnostics.NavMesh.IsComplete, Is.False);
        }

        [Test]
        public void RuntimeBindsBakeDataDiagnosticsFromActualMassNavigationBoard()
        {
            using GameEngine engine = PerformerBlacksmithShowcaseTestHarness.CreateEngine(
                "LudotsCoreMod",
                "CoreInputMod",
                "CameraProfilesMod",
                "PerformerBlacksmithShowcaseMod",
                "MassNavigationMod");

            engine.LoadMap("mass_navigation");

            MassNavigationSimulationRuntime simulation = engine.GetService(MassNavigationKeys.SimulationRuntime)
                ?? throw new InvalidOperationException("MassNavigation runtime missing.");
            MassNavigationBakeDataDiagnostics diagnostics = simulation.BakeDataDiagnostics
                ?? throw new InvalidOperationException("Bake/data diagnostics missing.");

            Assert.That(diagnostics.WorldWidthCm, Is.EqualTo(6_400_000));
            Assert.That(diagnostics.WorldHeightCm, Is.EqualTo(6_400_000));
            Assert.That(diagnostics.MacroChunkSizeXCm, Is.EqualTo(25_000));
            Assert.That(diagnostics.NavMesh.BakedChunks, Is.GreaterThanOrEqualTo(25));
            Assert.That(diagnostics.NavMesh.NotLoadedChunks, Is.EqualTo(diagnostics.NavMesh.TotalChunks - diagnostics.NavMesh.BakedChunks));
            Assert.That(diagnostics.NavMesh.IsComplete, Is.False);
            Assert.That(diagnostics.AuthoredStaticObstacleCount, Is.EqualTo(40_000));
            Assert.That(diagnostics.TargetStaticObstacleCount, Is.EqualTo(40_000));
            Assert.That(diagnostics.StaticObstacle.BakedChunks, Is.EqualTo(40_000));
            Assert.That(diagnostics.StaticObstacleWorld, Is.Not.Null);

            MassNavigationObstacleDiagnostics obstacles = simulation.AcceptanceDiagnostics.Obstacles;
            MassNavigationStaticObstacleWorldDiagnostics obstacleWorld = simulation.AcceptanceDiagnostics.StaticObstacleWorld;
            Assert.That(obstacles.AuthoredStaticObstacleCount, Is.EqualTo(40_000));
            Assert.That(obstacles.BakedStaticObstacleCount, Is.EqualTo(40_000));
            Assert.That(obstacles.LoadedStaticObstacleCount, Is.EqualTo(40_000));
            Assert.That(obstacles.SolverActiveStaticObstacleCount, Is.EqualTo(5));
            Assert.That(obstacles.SolverActiveStaticObstacleCount, Is.LessThanOrEqualTo(obstacles.SolverStaticObstacleCapacity));
            Assert.That(obstacleWorld.WorldDistributionReady, Is.True);
            Assert.That(obstacleWorld.DataSource, Is.EqualTo("static_obstacle_world_asset"));
            Assert.That(obstacleWorld.RuntimeActivationStrategy, Is.EqualTo("active_window_subset_to_mass_flow_solver"));
            Assert.That(obstacleWorld.MacroChunkCoverageCount, Is.EqualTo(40_000));
            Assert.That(obstacleWorld.ActiveWindowLoadedCount, Is.EqualTo(5));
        }

        [Test]
        public void RuntimePathOnlyDiagnostics_UseSharedPathServiceRouterWithoutSubmittingOrders()
        {
            using GameEngine engine = PerformerBlacksmithShowcaseTestHarness.CreateEngine(
                "LudotsCoreMod",
                "CoreInputMod",
                "CameraProfilesMod",
                "PerformerBlacksmithShowcaseMod",
                "MassNavigationMod");

            engine.LoadMap("mass_navigation");

            IPathService pathService = engine.GetService(CoreServiceKeys.PathService)
                ?? throw new InvalidOperationException("PathService missing.");
            Assert.That(pathService.GetType().Name, Is.EqualTo("PathServiceRouter"));
            Assert.That(engine.GetService(CoreServiceKeys.NavQueryServices), Is.InstanceOf<NavQueryServiceRegistry>());
            Assert.That(engine.GetService(CoreServiceKeys.NavMeshProfiles), Is.InstanceOf<NavMeshProfileRegistry>());

            MassNavigationSimulationRuntime simulation = engine.GetService(MassNavigationKeys.SimulationRuntime)
                ?? throw new InvalidOperationException("MassNavigation runtime missing.");
            MassNavigationPathOnlyQueryDiagnostics query = simulation.AcceptanceDiagnostics.PathOnlyQuery;

            Assert.That(query.Available, Is.True);
            Assert.That(query.Status, Is.EqualTo("Ok"));
            Assert.That(query.NoOrderSubmitted, Is.True);
            Assert.That(query.PreviewMode, Is.EqualTo("path_preview"));
            Assert.That(query.InputContract, Is.EqualTo("pick_start_world_point_then_goal_world_point"));
            Assert.That(query.RoutePreviewState, Is.EqualTo("highlighted_route_ready"));
            Assert.That(query.HighlightRouteVisible, Is.True);
            Assert.That(query.OrderSuppressionReason, Is.EqualTo("preview_query_does_not_enqueue_massNavigationMove"));
            Assert.That(query.PathPointContract, Is.EqualTo("immutable_query_result"));
            Assert.That(query.WaypointContract, Is.EqualTo("editable_order_intent"));
            Assert.That(query.RouteProvenance, Is.EqualTo("PathServiceRouter/Auto"));
            Assert.That(query.QuerySource, Is.EqualTo("PathServiceRouter"));
            Assert.That(query.WaypointCount, Is.EqualTo(2));
            Assert.That(query.PathPointCount, Is.GreaterThan(1));
            Assert.That(query.MacroRouteChunkCount, Is.GreaterThan(0));
            Assert.That(query.TravelCost, Is.GreaterThan(0f));

            MassNavigationWaypointPathDiagnostics waypointPath = simulation.AcceptanceDiagnostics.WaypointPath;
            Assert.That(waypointPath.WaypointCount, Is.EqualTo(query.WaypointCount));
            Assert.That(waypointPath.PathPointCount, Is.EqualTo(query.PathPointCount));
            Assert.That(waypointPath.WaypointsEditable, Is.True);
            Assert.That(waypointPath.PathPointsImmutable, Is.True);
            Assert.That(waypointPath.PathPointsCanSeedWaypoints, Is.True);
        }

        [Test]
        public void RuntimeStrategySwitchDiagnostics_ExposeConfiguredProfilesAndQueryStatus()
        {
            using GameEngine engine = PerformerBlacksmithShowcaseTestHarness.CreateEngine(
                "LudotsCoreMod",
                "CoreInputMod",
                "CameraProfilesMod",
                "PerformerBlacksmithShowcaseMod",
                "MassNavigationMod");

            engine.LoadMap("mass_navigation");

            MassNavigationSimulationRuntime simulation = engine.GetService(MassNavigationKeys.SimulationRuntime)
                ?? throw new InvalidOperationException("MassNavigation runtime missing.");
            MassNavigationStrategySwitchDiagnostics[] strategies = simulation.AcceptanceDiagnostics.StrategySwitches.ToArray();

            Assert.That(strategies.Length, Is.GreaterThanOrEqualTo(5));
            Assert.That(strategies.Select(strategy => strategy.AgentTypeId).ToArray(), Is.SupersetOf(new[] { "Infantry", "LargeVehicle", "Mountain", "Naval", "Air" }));
            Assert.That(strategies.Any(strategy => strategy.GraphQueryAvailable), Is.True);
            Assert.That(strategies.Any(strategy =>
                strategy.GraphStatus == "Ok" ||
                strategy.GraphStatus == "OkViaPathServiceRouter"), Is.True);
            Assert.That(strategies.Any(strategy =>
                strategy.MeshQueryAvailable &&
                strategy.MeshStatus == "Ok" &&
                strategy.MeshQuerySource == "active_window_navmesh_query"), Is.True);
            Assert.That(strategies.First(strategy => strategy.AgentTypeId == "LargeVehicle").RequestedMode, Is.EqualTo("PreferGraph"));
            Assert.That(strategies.First(strategy => strategy.AgentTypeId == "Naval").RequestedMode, Is.EqualTo("PreferMesh"));
            MassNavigationStrategySwitchDiagnostics infantry = strategies.First(strategy => strategy.AgentTypeId == "Infantry");
            Assert.That(infantry.MeshTouchedTileCount, Is.GreaterThan(0));
            Assert.That(infantry.MeshStartChunkX, Is.InRange(126, 130));
            Assert.That(infantry.MeshGoalChunkX, Is.InRange(126, 130));
            foreach (string agentType in new[] { "Infantry", "Mountain", "Naval", "Air" })
            {
                MassNavigationStrategySwitchDiagnostics strategy = strategies.First(item => item.AgentTypeId == agentType);
                Assert.That(strategy.MeshQueryAvailable, Is.True, agentType);
                Assert.That(strategy.MeshStatus, Is.EqualTo("Ok"), agentType);
                Assert.That(strategy.MeshQuerySource, Is.EqualTo("active_window_navmesh_query"), agentType);
                Assert.That(strategy.MeshTouchedTileCount, Is.GreaterThan(0), agentType);
            }

            Assert.That(strategies.First(strategy => strategy.AgentTypeId == "Air").AcceptanceProof, Does.Contain("active_window_navmesh_query_passed_with_tile_route_layer_profile_costs_and_touched_tile_provenance"));
            Assert.That(strategies.First(strategy => strategy.AgentTypeId == "Air").CostBreakdown, Does.Contain("6:12"));
        }

        [Test]
        public void RuntimeTargetAllocationDiagnostics_ExposeReachabilityAndReuseProvenance()
        {
            using GameEngine engine = PerformerBlacksmithShowcaseTestHarness.CreateEngine(
                "LudotsCoreMod",
                "CoreInputMod",
                "CameraProfilesMod",
                "PerformerBlacksmithShowcaseMod",
                "MassNavigationMod");

            engine.LoadMap("mass_navigation");

            MassNavigationSimulationRuntime simulation = engine.GetService(MassNavigationKeys.SimulationRuntime)
                ?? throw new InvalidOperationException("MassNavigation runtime missing.");
            using World world = World.Create();
            const int selectedCount = 128;
            simulation.MassFlow.Reset(new[] { 1 }, unitsPerTeam: selectedCount, simulation.Config.World!.Obstacles);
            Entity[] selected = new Entity[selectedCount];
            for (int i = 0; i < selected.Length; i++)
            {
                Entity agent = world.Create(
                    default(MassNavigationAgentTag),
                    new MassNavigationAgentIndex { Value = i },
                    new Team { Id = 1 },
                    OrderBuffer.CreateEmpty());
                simulation.AgentState.RegisterAgent(agent, controllable: true);
                selected[i] = agent;
            }

            var destination = new System.Numerics.Vector2(1_050_000f, -780_000f);
            int assigned = simulation.NavGroupRuntime.IssueSelectionMoveCommand(
                simulation.MassFlow,
                simulation.AgentState,
                selected,
                destination,
                MassNavigationFormationMode.Square);
            Assert.That(assigned, Is.EqualTo(selectedCount));
            int orderId = simulation.AllocateSharedOrderId();
            simulation.AcceptanceDiagnostics.RecordSubmittedOrder(
                orderId,
                selectedCount,
                destination,
                MassNavigationFormationMode.Square,
                simulation.AcceptanceDiagnostics.ResolveDefaultStrategy());
            simulation.AcceptanceDiagnostics.RecordTargetAllocation(
                selectedCount,
                selectedCount,
                blockedSlotCount: 0,
                fallbackSlotCount: 0,
                destination,
                MassNavigationFormationMode.Square,
                simulation.MassFlow,
                Enumerable.Range(0, selectedCount).ToArray());

            MassNavigationTargetAllocationDiagnostics allocation = simulation.AcceptanceDiagnostics.TargetAllocation;
            Assert.That(allocation.HasAllocation, Is.True);
            Assert.That(allocation.SelectedCount, Is.EqualTo(selectedCount));
            Assert.That(allocation.SlotCount, Is.EqualTo(selectedCount));
            Assert.That(allocation.ReachableSlotCount, Is.EqualTo(selectedCount));
            Assert.That(allocation.ReachabilityFanoutCount, Is.EqualTo(selectedCount));
            Assert.That(allocation.BlockedSlotCount, Is.EqualTo(0));
            Assert.That(allocation.FallbackSlotCount, Is.EqualTo(0));
            Assert.That(allocation.ReachabilityProbeStatus, Is.EqualTo("Ok"));
            Assert.That(allocation.ReachabilitySource, Does.Contain("formation_slot_projection"));
            Assert.That(allocation.ReachabilitySource, Does.Contain("shared_order_fanout"));
            Assert.That(allocation.ReachabilitySource, Does.Contain("path_only_route_reachability_smoke"));
            Assert.That(allocation.ReachabilitySource, Does.Contain("active_window_navmesh_query"));
            Assert.That(allocation.AllocationRouteId, Is.GreaterThan(0));
            Assert.That(allocation.AllocationRouteReuseKey, Does.Contain("goalBucket"));
            Assert.That(allocation.AllocationRouteCacheSource, Is.EqualTo("acceptance_route_bucket"));
            MassNavigationOrderReuseDiagnostics reuse = simulation.AcceptanceDiagnostics.OrderReuse;
            Assert.That(reuse.ReuseScope, Is.EqualTo("cold_order_bucket"));
            Assert.That(reuse.PathRouteSignature, Does.StartWith("path:"));
            Assert.That(reuse.PathRoutePointCount, Is.GreaterThan(1));
            Assert.That(reuse.PathRouteTouchedTileCount, Is.GreaterThan(0));
            Assert.That(reuse.MeshRouteSignature, Does.StartWith("mesh:"));
            Assert.That(reuse.MeshRouteSource, Is.EqualTo("active_window_navmesh_query"));
            Assert.That(reuse.MeshRouteStatus, Is.EqualTo("Ok"));
            Assert.That(reuse.MeshRouteTouchedTileCount, Is.GreaterThan(0));
            Assert.That(reuse.ProductionGap, Does.Contain("normalized_bucket_route_reuse_passed"));
            Assert.That(allocation.MeshReachabilitySource, Is.EqualTo("active_window_navmesh_query"));
            Assert.That(allocation.MeshReachabilityStatus, Is.EqualTo("Ok"));
            Assert.That(allocation.MeshReachabilityTouchedTileCount, Is.GreaterThan(0));
            Assert.That(allocation.BlockedReasonSummary, Is.EqualTo("none"));
            Assert.That(allocation.FallbackReasonSummary, Is.EqualTo("none"));
            Assert.That(allocation.ProductionGap, Does.Contain("target_slots_reachability_passed"));
            Assert.That(allocation.ActualTargetSampleCount, Is.GreaterThan(0));
            Assert.That(allocation.ActualTargetSampleSource, Is.EqualTo("mass_flow_unit_targets_sample"));
            Assert.That(simulation.AcceptanceDiagnostics.TargetSlotSamples.Length, Is.GreaterThan(0));
        }

        [Test]
        public void RuntimeHpaMacroDiagnostics_ExposeMacroRouteWithoutClaimingProductionAsset()
        {
            using GameEngine engine = PerformerBlacksmithShowcaseTestHarness.CreateEngine(
                "LudotsCoreMod",
                "CoreInputMod",
                "CameraProfilesMod",
                "PerformerBlacksmithShowcaseMod",
                "MassNavigationMod");

            engine.LoadMap("mass_navigation");

            MassNavigationSimulationRuntime simulation = engine.GetService(MassNavigationKeys.SimulationRuntime)
                ?? throw new InvalidOperationException("MassNavigation runtime missing.");
            MassNavigationHpaMacroDiagnostics hpa = simulation.AcceptanceDiagnostics.HpaMacro;

            Assert.That(hpa.Available, Is.True);
            Assert.That(hpa.MacroChunkColumns, Is.EqualTo(256));
            Assert.That(hpa.MacroChunkRows, Is.EqualTo(256));
            Assert.That(hpa.MacroChunkCount, Is.EqualTo(65_536));
            Assert.That(hpa.ExpectedAdjacencyEdgeCount, Is.EqualTo(130_560));
            Assert.That(hpa.SampleRouteChunkCount, Is.GreaterThan(0));
            Assert.That(hpa.SamplePortalCount, Is.GreaterThan(0));
            Assert.That(hpa.RouteSource, Does.StartWith("PathServiceRouter"));
            Assert.That(hpa.RouteSource, Does.Contain("navtile_portal_graph_active_window"));
            Assert.That(hpa.UsesSyntheticMacroGridTarget, Is.False);
            Assert.That(hpa.ProductionGap, Is.EqualTo("active_window_hpa_graph_route_passed_streaming_contract"));

            MassNavigationHpaGraphAssetDiagnostics graph = simulation.AcceptanceDiagnostics.HpaGraph;
            Assert.That(graph.Available, Is.True);
            Assert.That(graph.ActiveWindowMinChunkX, Is.EqualTo(126));
            Assert.That(graph.ActiveWindowMinChunkY, Is.EqualTo(126));
            Assert.That(graph.ActiveWindowMaxChunkX, Is.EqualTo(130));
            Assert.That(graph.ActiveWindowMaxChunkY, Is.EqualTo(130));
            Assert.That(graph.ActiveWindowChunkCount, Is.EqualTo(25));
            Assert.That(graph.LoadedTileCount, Is.EqualTo(25));
            Assert.That(graph.GraphNodeCount, Is.GreaterThan(0));
            Assert.That(graph.GraphEdgeCount, Is.GreaterThan(0));
            Assert.That(graph.ActiveWindowRouteAvailable, Is.True);
            Assert.That(graph.ActiveWindowRoutePortalCount, Is.GreaterThanOrEqualTo(2));
            Assert.That(graph.ActiveWindowRouteCrossTileStepCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(graph.RouteStartChunkX, Is.InRange(126, 130));
            Assert.That(graph.RouteStartChunkY, Is.InRange(126, 130));
            Assert.That(graph.RouteGoalChunkX, Is.InRange(126, 130));
            Assert.That(graph.RouteGoalChunkY, Is.InRange(126, 130));
            Assert.That(graph.RouteSignature, Does.Contain("->"));
            Assert.That(graph.Source, Is.EqualTo("navtile_portal_graph_active_window_route"));
            Assert.That(graph.Gap, Is.EqualTo("active_window_hpa_graph_route_passed_streaming_contract"));
        }

        [Test]
        public void BakeDataConfig_DoesNotOwnObservedBakeChunkCounts()
        {
            string modRoot = Path.Combine(FindRepoRoot(), "mods", "capabilities", "navigation", "MassNavigationMod");
            string json = File.ReadAllText(Path.Combine(modRoot, "assets", "MassNavigationConfig.json"));

            Assert.That(json, Does.Not.Contain("\"bakedChunks\""));
            Assert.That(json, Does.Not.Contain("\"missingChunks\""));
            Assert.That(json, Does.Not.Contain("\"dirtyChunks\""));
            Assert.That(json, Does.Not.Contain("\"failedChunks\""));
            Assert.That(json, Does.Not.Contain("\"notLoadedChunks\""));
        }

        private static void AssertNotLoadedContract(MassNavigationBakeDataDomainSummary summary)
        {
            Assert.That(summary.TotalChunks, Is.EqualTo(65_536));
            Assert.That(summary.BakedChunks, Is.EqualTo(0), summary.Domain.ToString());
            Assert.That(summary.MissingChunks, Is.EqualTo(0), summary.Domain.ToString());
            Assert.That(summary.DirtyChunks, Is.EqualTo(0), summary.Domain.ToString());
            Assert.That(summary.FailedChunks, Is.EqualTo(0), summary.Domain.ToString());
            Assert.That(summary.NotLoadedChunks, Is.EqualTo(65_536), summary.Domain.ToString());
            Assert.That(summary.CoveragePercent, Is.EqualTo(0), summary.Domain.ToString());
            Assert.That(summary.IsComplete, Is.False, summary.Domain.ToString());
        }

        private static void AssertProfile(
            IReadOnlyCollection<MassNavigationBakeDataProfileSummary> profiles,
            string agentTypeId,
            string navProfileId,
            int layer,
            string selectionMode)
        {
            MassNavigationBakeDataProfileSummary profile = profiles.FirstOrDefault(item =>
                string.Equals(item.AgentTypeId, agentTypeId, StringComparison.OrdinalIgnoreCase));
            Assert.That(profile.AgentTypeId, Is.EqualTo(agentTypeId));
            Assert.That(profile.NavProfileId, Is.EqualTo(navProfileId));
            Assert.That(profile.Layer, Is.EqualTo(layer));
            Assert.That(profile.SelectionMode, Is.EqualTo(selectionMode));
            Assert.That(profile.NavAreaCostCount, Is.GreaterThan(0), agentTypeId);
        }

        private static LoadedConfigs LoadConfigs()
        {
            string repoRoot = FindRepoRoot();
            string modRoot = Path.Combine(repoRoot, "mods", "capabilities", "navigation", "MassNavigationMod");

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", Path.Combine(repoRoot, "assets"));
            vfs.Mount("MassNavigationMod", modRoot);

            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            modLoader.LoadedModIds.Add("MassNavigationMod");

            var pipeline = new ConfigPipeline(vfs, modLoader);
            ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);
            var report = new ConfigConflictReport();

            return new LoadedConfigs(
                new MassNavigationConfigLoader(pipeline).Load(catalog, report),
                new NavMeshBakeConfigLoader(pipeline).Load(catalog, report),
                new PathingConfigLoader(pipeline).Load(catalog, report));
        }

        private static string FindRepoRoot()
        {
            string current = TestContext.CurrentContext.WorkDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (Directory.Exists(Path.Combine(current, "mods")) &&
                    File.Exists(Path.Combine(current, "AGENTS.md")))
                {
                    return current;
                }

                current = Path.GetDirectoryName(current)!;
            }

            throw new DirectoryNotFoundException("Repository root not found from test work directory.");
        }

        private readonly record struct LoadedConfigs(
            MassNavigationConfig MassNavigation,
            NavMeshBakeConfig NavMesh,
            PathingConfig Pathing);
    }
}
