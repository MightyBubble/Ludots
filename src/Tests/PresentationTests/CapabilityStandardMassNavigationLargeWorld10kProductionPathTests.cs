using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Arch.Core;
using CapabilityStandardMassNavigationLargeWorld10kMod;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Knowledge;
using Ludots.Core.Mathematics;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Minimap;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class CapabilityStandardMassNavigationLargeWorld10kProductionPathTests
    {
        private const int ExpectedAgentCount = 10_000;
        private const int ExpectedTeamCount = 4;
        private const float FixedDeltaSeconds = 1f / 60f;
        private const int MaxWarmupFrames = 240;
        private const int MovementObservationFrames = 60;
        private const int HealthStabilityObservationFrames = 75;
        private const int HudStabilityObservationFrames = 12;
        private const float CommandTargetOffsetWindowScale = 0.25f;
        private const float MovementEpsilonCm = 1f;
        private const string MouseLeftButtonPath = "<Mouse>/LeftButton";
        private const string MouseRightButtonPath = "<Mouse>/RightButton";
        private const string LightCommandMarkerPerformerId = "mass_navigation_agent_command_marker_light";
        private const string HeavyCommandMarkerPerformerId = "mass_navigation_agent_command_marker_heavy";

        private static readonly string[] ShowcaseMods =
        {
            "LudotsCoreMod",
            "CoreInputMod",
            "MassNavigationMod",
            "CapabilityStandardMassNavigationLargeWorld10kMod"
        };

        [SetUp]
        public void SetUp()
        {
            AttributeRegistry.Clear();
            TagRegistry.Clear();
            PerformerScopeTagRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            AttributeRegistry.Clear();
            TagRegistry.Clear();
            PerformerScopeTagRegistry.Clear();
        }

        [Test]
        public void Showcase_ProjectsFourTeamAgentsToMinimapAndScreenHud()
        {
            GC.KeepAlive(typeof(CapabilityStandardMassNavigationLargeWorld10kModEntry).Assembly);

            using var engine = CreateEngine();
            StartStartupMap(engine);

            var simulation = RequireService(engine, MassNavigationKeys.SimulationRuntime);
            int expectedAgents = checked(simulation.Config.Scenario.Teams.Length * simulation.Config.Scenario.AgentsPerTeam);
            Assert.That(expectedAgents, Is.EqualTo(ExpectedAgentCount));
            Assert.That(engine.MergedConfig.GasRuntimeCapacity.OrderQueueCapacity, Is.GreaterThanOrEqualTo(expectedAgents));
            Assert.That(engine.MergedConfig.GasRuntimeCapacity.OrderAdmissionResultCapacity, Is.GreaterThanOrEqualTo(expectedAgents * 2));
            Assert.That(engine.MergedConfig.GasRuntimeCapacity.OrderTerminalResultCapacity, Is.GreaterThanOrEqualTo(expectedAgents));
            Assert.That(simulation.Config.Scenario.Teams.Length, Is.EqualTo(ExpectedTeamCount));

            var hudProjection = CreateHudProjection(engine);
            ProjectionSample sample = WaitForProductionProjection(engine, hudProjection, simulation, expectedAgents);

            var minimapRuntime = RequireService(engine, CoreServiceKeys.MinimapRuntime);
            var minimapMarkers = RequireService(engine, CoreServiceKeys.MinimapMarkerBuffer);
            var minimapScreenMarkers = RequireService(engine, CoreServiceKeys.MinimapScreenMarkerBuffer);
            var screenHud = RequireService(engine, CoreServiceKeys.PresentationScreenHudBuffer);
            string diagnostics = sample.Diagnostics;

            Assert.That(minimapRuntime.Visible, Is.True, diagnostics);
            Assert.That(minimapRuntime.Preset, Is.EqualTo(MinimapPreset.RtsFullMap), diagnostics);
            Assert.That(sample.MinimapSnapshot.ZoomBand, Is.EqualTo(MinimapZoomBand.Strategic), diagnostics);
            Assert.That(minimapMarkers.Count, Is.GreaterThanOrEqualTo(expectedAgents), diagnostics);
            Assert.That(minimapScreenMarkers.Count, Is.GreaterThanOrEqualTo(expectedAgents), diagnostics);
            Assert.That(sample.MinimapSnapshot.VisibleMarkerCount, Is.GreaterThanOrEqualTo(expectedAgents), diagnostics);
            Assert.That(sample.WorldHudBars, Is.GreaterThanOrEqualTo(expectedAgents), diagnostics);
            Assert.That(sample.WorldHudText, Is.GreaterThanOrEqualTo(expectedAgents), diagnostics);
            Assert.That(screenHud.BarCount, Is.GreaterThanOrEqualTo(expectedAgents), diagnostics);
            Assert.That(screenHud.TextCount, Is.GreaterThanOrEqualTo(expectedAgents), diagnostics);
        }

        [Test]
        public void Showcase_CommandSourceCapacityCoversAuthoredAgentSet()
        {
            GC.KeepAlive(typeof(CapabilityStandardMassNavigationLargeWorld10kModEntry).Assembly);

            using var engine = CreateEngine();
            StartStartupMap(engine);

            var simulation = RequireService(engine, MassNavigationKeys.SimulationRuntime);
            int expectedAgents = checked(simulation.Config.Scenario.Teams.Length * simulation.Config.Scenario.AgentsPerTeam);
            Assert.That(expectedAgents, Is.EqualTo(ExpectedAgentCount));
            Assert.That(simulation.Config.ScenarioRuntime.InitialCommandActorScratchCapacity, Is.GreaterThanOrEqualTo(expectedAgents));
            Assert.That(simulation.Config.ScenarioRuntime.InitialCommandActorSnapshotCapacity, Is.GreaterThanOrEqualTo(expectedAgents));
            Assert.That(simulation.Config.ScenarioRuntime.RuntimeCapacity.CommandActorScratchCapacity, Is.GreaterThanOrEqualTo(expectedAgents));
            Assert.That(simulation.Config.ScenarioRuntime.RuntimeCapacity.GroupMemberCapacity, Is.GreaterThanOrEqualTo(expectedAgents));
            Assert.That(simulation.Config.ScenarioRuntime.RuntimeCapacity.OrderIngestionMemberCapacity, Is.GreaterThanOrEqualTo(expectedAgents));

            var hudProjection = CreateHudProjection(engine);
            _ = WaitForProductionProjection(engine, hudProjection, simulation, expectedAgents);

            Entity[] agents = CollectMassNavigationAgents(engine, expectedAgents);
            Entity localPlayer = RequireService(engine, CoreServiceKeys.LocalPlayerEntity);
            ReplaceCommandSource(engine, localPlayer, agents);

            Assert.DoesNotThrow(() => simulation.SetCommandActorSnapshot(agents, simulation.CommandActorSnapshotRevision + 1));
            Assert.That(simulation.CommandActorCount, Is.EqualTo(expectedAgents));
        }

        [Test]
        public void Showcase_AgentHealthHudInputsRemainStableAcrossHealthDriftPeriodWindow()
        {
            GC.KeepAlive(typeof(CapabilityStandardMassNavigationLargeWorld10kModEntry).Assembly);

            using var engine = CreateEngine();
            StartStartupMap(engine);

            var simulation = RequireService(engine, MassNavigationKeys.SimulationRuntime);
            int expectedAgents = checked(simulation.Config.Scenario.Teams.Length * simulation.Config.Scenario.AgentsPerTeam);
            Assert.That(expectedAgents, Is.EqualTo(ExpectedAgentCount));
            AssertScenarioAgentTemplatesDoNotDriveHealthPeriodically(engine, simulation);

            var hudProjection = CreateHudProjection(engine);
            _ = WaitForProductionProjection(engine, hudProjection, simulation, expectedAgents);
            AssertScreenHudIdentityStableAcrossProjectionFrames(engine, hudProjection, HudStabilityObservationFrames);

            Dictionary<int, AgentHealthSample> before = CaptureAgentHealth(engine, expectedAgents);
            TickProjectionFrames(engine, hudProjection, HealthStabilityObservationFrames);
            Dictionary<int, AgentHealthSample> after = CaptureAgentHealth(engine, expectedAgents);

            AssertAgentHealthStable(before, after);
        }

        [Test]
        public void Showcase_MouseBoxAcquisition_AcquiresVisibleMassNavigationAgents()
        {
            GC.KeepAlive(typeof(CapabilityStandardMassNavigationLargeWorld10kModEntry).Assembly);

            using var engine = CreateEngine();
            StartStartupMap(engine);

            var simulation = RequireService(engine, MassNavigationKeys.SimulationRuntime);
            int expectedAgents = checked(simulation.Config.Scenario.Teams.Length * simulation.Config.Scenario.AgentsPerTeam);
            Assert.That(expectedAgents, Is.EqualTo(ExpectedAgentCount));

            var hudProjection = CreateHudProjection(engine);
            _ = WaitForProductionProjection(engine, hudProjection, simulation, expectedAgents);
            AssertLocalScenarioAgentsAreCommandable(engine);

            var backend = RequireMutableInputBackend(engine);
            CommandSourceDragGesture gesture = ResolveVisibleAgentDragGesture(engine);
            CommandSourceDiagnostics before = CaptureCommandSourceDiagnostics(engine, gesture.Marquee);
            Assert.That(before.VisibleSelectable, Is.GreaterThan(0), before.ToString());
            Assert.That(before.ScreenIntersecting, Is.GreaterThan(0), before.ToString());
            Assert.That(before.EligibleIntersecting, Is.GreaterThan(0), before.ToString());

            DriveCommandSourceBoxAcquisition(engine, hudProjection, backend, gesture);
            TickProjectionFrames(engine, hudProjection, 2);

            Entity[] commandActors = SnapshotCommandSource(engine);
            CommandSourceDiagnostics after = CaptureCommandSourceDiagnostics(engine, gesture.Marquee);
            Assert.That(commandActors.Length, Is.GreaterThan(0), after.ToString());
            Assert.That(simulation.CommandActorCount, Is.GreaterThan(0), after.ToString());
            Assert.That(CountActiveCommandMarkers(engine), Is.EqualTo(commandActors.Length), after.ToString());
        }

        [Test]
        public void Showcase_MouseBoxAcquisition_RightClickIssuesOrdersForCommandableAgents()
        {
            GC.KeepAlive(typeof(CapabilityStandardMassNavigationLargeWorld10kModEntry).Assembly);

            using var engine = CreateEngine();
            StartStartupMap(engine);

            var simulation = RequireService(engine, MassNavigationKeys.SimulationRuntime);
            int expectedAgents = checked(simulation.Config.Scenario.Teams.Length * simulation.Config.Scenario.AgentsPerTeam);
            Assert.That(expectedAgents, Is.EqualTo(ExpectedAgentCount));

            var hudProjection = CreateHudProjection(engine);
            _ = WaitForProductionProjection(engine, hudProjection, simulation, expectedAgents);

            var backend = RequireMutableInputBackend(engine);
            CommandSourceDragGesture gesture = ResolveVisibleAgentDragGesture(engine);
            DriveCommandSourceBoxAcquisition(engine, hudProjection, backend, gesture);
            TickProjectionFrames(engine, hudProjection, 2);

            Entity[] commandActors = SnapshotCommandSource(engine);
            CommandSourceDiagnostics commandSourceDiagnostics = CaptureCommandSourceDiagnostics(engine, gesture.Marquee);
            Assert.That(commandActors.Length, Is.GreaterThan(0), commandSourceDiagnostics.ToString());
            Assert.That(simulation.CommandActorCount, Is.EqualTo(commandActors.Length), commandSourceDiagnostics.ToString());
            AssertCommandActorsAreCommandable(engine, commandActors);

            int activeOrdersBefore = CountActiveMoveOrders(engine, commandActors);
            int rejectsBefore = simulation.CommandRejectsTotal;
            Vector2[] positionsBefore = CaptureCommandActorWorldPositions(engine, simulation, commandActors);
            Vector2 commandScreenPoint = ResolveCommandTargetScreenPoint(engine, simulation, commandActors);

            DriveRightClickCommandFrame(engine, hudProjection, backend, commandScreenPoint);

            Assert.That(simulation.CommandRejectsTotal, Is.EqualTo(rejectsBefore), commandSourceDiagnostics.ToString());
            Assert.That(simulation.CommandCountFrame, Is.GreaterThan(0), commandSourceDiagnostics.ToString());
            Assert.That(simulation.LastCommandActorCount, Is.EqualTo(commandActors.Length), commandSourceDiagnostics.ToString());
            Assert.That(CountActiveMoveOrders(engine, commandActors), Is.GreaterThan(activeOrdersBefore), commandSourceDiagnostics.ToString());

            TickProjectionFrames(engine, hudProjection, MovementObservationFrames);
            Assert.That(
                CountMovedCommandActors(engine, simulation, commandActors, positionsBefore),
                Is.GreaterThan(0),
                commandSourceDiagnostics.ToString());

            backend.SetButton(MouseRightButtonPath, false);
            TickProjectionFrames(engine, hudProjection, 2);
        }

        private static void StartStartupMap(GameEngine engine)
        {
            Assert.That(engine.MergedConfig.StartupMapId, Is.Not.Empty);
            Assert.That(engine.MergedConfig.StartupLocalPlayerId, Is.GreaterThan(0));

            engine.Start();
            engine.LoadStartupMap();
            AssertStartupParticipantBindings(engine);
        }

        private static void AssertStartupParticipantBindings(GameEngine engine)
        {
            int playerId = engine.MergedConfig.StartupLocalPlayerId;
            Entity localPlayer = RequireService(engine, CoreServiceKeys.LocalPlayerEntity);
            Assert.That(localPlayer, Is.Not.EqualTo(Entity.Null));
            Assert.That(engine.World.IsAlive(localPlayer), Is.True);

            var players = RequireService(engine, CoreServiceKeys.PlayerEntityLookup);
            Assert.That(players.TryGet(playerId, out Entity playerEntity), Is.True);
            Assert.That(playerEntity, Is.EqualTo(localPlayer));

            var session = engine.CurrentMapSession
                ?? throw new InvalidOperationException("Startup map session is missing.");
            PlayerBindingData? playerBinding = null;
            for (int i = 0; i < session.MapConfig.Players.Count; i++)
            {
                PlayerBindingData binding = session.MapConfig.Players[i];
                if (binding.PlayerId == playerId)
                {
                    playerBinding = binding;
                    break;
                }
            }

            Assert.That(playerBinding, Is.Not.Null);

            var teams = RequireService(engine, CoreServiceKeys.TeamEntityLookup);
            Assert.That(teams.TryGet(playerBinding!.TeamId, out Entity teamEntity), Is.True);
            Assert.That(engine.World.IsAlive(teamEntity), Is.True);
            Assert.That(engine.World.TryGet(localPlayer, out PlayerIdentity identity), Is.True);
            Assert.That(identity.PlayerId, Is.EqualTo(playerId));
            Assert.That(engine.World.TryGet(localPlayer, out PlayerOwner owner), Is.True);
            Assert.That(owner.PlayerId, Is.EqualTo(playerId));
            Assert.That(engine.World.TryGet(localPlayer, out Team team), Is.True);
            Assert.That(team.Id, Is.EqualTo(playerBinding.TeamId));
        }

        private static GameEngine CreateEngine()
        {
            string repoRoot = FindRepoRoot();
            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(
                RepoModPaths.ResolveExplicit(repoRoot, ShowcaseMods),
                Path.Combine(repoRoot, "assets"));
            ApplyHostAssets(engine);
            InstallInput(engine);
            HeadlessPresentationTestHost.Install(engine);
            return engine;
        }

        private static void ApplyHostAssets(GameEngine engine)
        {
            var meshAssets = RequireService(engine, CoreServiceKeys.PresentationMeshAssetRegistry);
            var materialAssets = RequireService(engine, CoreServiceKeys.PresentationMaterialRegistry);
            new PresentationHostAssetConfigLoader(engine.ConfigPipeline, meshAssets, materialAssets)
                .Apply("raylib", engine.ConfigCatalog, engine.ConfigConflictReport);
        }

        private static void InstallInput(GameEngine engine)
        {
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var backend = new MutableInputBackend();
            var inputHandler = new PlayerInputHandler(backend, inputConfig);
            for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
            {
                inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
            }

            engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
            engine.SetService(CoreServiceKeys.InputBackend, (IInputBackend)backend);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
        }

        private static MutableInputBackend RequireMutableInputBackend(GameEngine engine)
        {
            return RequireService(engine, CoreServiceKeys.InputBackend) as MutableInputBackend
                ?? throw new InvalidOperationException("MassNavigation production path test requires the mutable input backend.");
        }

        private static WorldHudToScreenSystem CreateHudProjection(GameEngine engine)
        {
            return new WorldHudToScreenSystem(
                engine.World,
                RequireService(engine, CoreServiceKeys.PresentationWorldHudBuffer),
                engine.GetService(CoreServiceKeys.PresentationWorldHudStrings),
                RequireService(engine, CoreServiceKeys.ScreenProjector),
                RequireService(engine, CoreServiceKeys.ViewController),
                RequireService(engine, CoreServiceKeys.PresentationScreenHudBuffer),
                engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics),
                engine.GetService(CoreServiceKeys.CameraCullingDebugState));
        }

        private static ProjectionSample WaitForProductionProjection(
            GameEngine engine,
            WorldHudToScreenSystem hudProjection,
            MassNavigationSimulationRuntime simulation,
            int expectedAgents)
        {
            ProjectionSample lastSample = default;
            for (int frame = 0; frame < MaxWarmupFrames; frame++)
            {
                TickProjectionFrames(engine, hudProjection, 1);
                lastSample = CaptureProjectionSample(engine, simulation);
                if (simulation.AgentState.HasBoundAgents(expectedAgents) &&
                    lastSample.MinimapSnapshot.ZoomBand == MinimapZoomBand.Strategic &&
                    lastSample.MinimapScreenMarkers >= expectedAgents &&
                    lastSample.MinimapSnapshot.VisibleMarkerCount >= expectedAgents &&
                    lastSample.WorldHudBars >= expectedAgents &&
                    lastSample.WorldHudText >= expectedAgents &&
                    lastSample.ScreenHudBars >= expectedAgents &&
                    lastSample.ScreenHudText >= expectedAgents)
                {
                    return lastSample;
                }
            }

            Assert.Fail(
                $"MassNavigation showcase did not project {expectedAgents} agents to minimap and HUD within {MaxWarmupFrames} frames; {lastSample.Diagnostics}");
            return default;
        }

        private static ProjectionSample CaptureProjectionSample(GameEngine engine, MassNavigationSimulationRuntime simulation)
        {
            var minimapRuntime = RequireService(engine, CoreServiceKeys.MinimapRuntime);
            var minimapMarkers = RequireService(engine, CoreServiceKeys.MinimapMarkerBuffer);
            var minimapScreenMarkers = RequireService(engine, CoreServiceKeys.MinimapScreenMarkerBuffer);
            var worldHud = RequireService(engine, CoreServiceKeys.PresentationWorldHudBuffer);
            var screenHud = RequireService(engine, CoreServiceKeys.PresentationScreenHudBuffer);
            MinimapDebugSnapshot minimapSnapshot = minimapRuntime.CaptureDebugSnapshot();
            int worldHudBars = CountWorldHudItems(worldHud, WorldHudItemKind.Bar);
            int worldHudText = CountWorldHudItems(worldHud, WorldHudItemKind.Text);
            return new ProjectionSample(
                minimapSnapshot,
                minimapScreenMarkers.Count,
                worldHudBars,
                worldHudText,
                screenHud.BarCount,
                screenHud.TextCount,
                BuildDiagnostics(
                    simulation,
                    minimapRuntime,
                    minimapSnapshot,
                    minimapMarkers,
                    minimapScreenMarkers,
                    worldHud,
                    screenHud,
                    worldHudBars,
                    worldHudText));
        }

        private static void TickProjectionFrames(GameEngine engine, WorldHudToScreenSystem hudProjection, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                engine.SetService(CoreServiceKeys.UiCaptured, false);
                engine.Tick(FixedDeltaSeconds);
                HeadlessPresentationTestHost.UpdateCamera(engine);
                if (engine.GetService(CoreServiceKeys.MinimapRuntime) is MinimapRuntime minimapRuntime &&
                    engine.GetService(CoreServiceKeys.MinimapMarkerBuffer) is MinimapMarkerBuffer minimapMarkers &&
                    engine.GetService(CoreServiceKeys.MinimapScreenMarkerBuffer) is MinimapScreenMarkerBuffer minimapScreenMarkers)
                {
                    minimapRuntime.Refresh(engine, minimapMarkers, minimapScreenMarkers);
                }

                hudProjection.Update(FixedDeltaSeconds);
            }
        }

        private static void DriveCommandSourceBoxAcquisition(
            GameEngine engine,
            WorldHudToScreenSystem hudProjection,
            MutableInputBackend backend,
            in CommandSourceDragGesture gesture)
        {
            backend.SetMousePosition(gesture.Start);
            backend.SetButton(MouseLeftButtonPath, false);
            TickProjectionFrames(engine, hudProjection, 1);

            backend.SetButton(MouseLeftButtonPath, true);
            TickProjectionFrames(engine, hudProjection, 1);

            backend.SetMousePosition(gesture.End);
            TickProjectionFrames(engine, hudProjection, 1);

            backend.SetButton(MouseLeftButtonPath, false);
            TickProjectionFrames(engine, hudProjection, 1);
        }

        private static void DriveRightClickCommandFrame(
            GameEngine engine,
            WorldHudToScreenSystem hudProjection,
            MutableInputBackend backend,
            Vector2 position)
        {
            backend.SetMousePosition(position);
            backend.SetButton(MouseRightButtonPath, false);
            TickProjectionFrames(engine, hudProjection, 1);

            backend.SetButton(MouseRightButtonPath, true);
            TickProjectionFrames(engine, hudProjection, 1);

            backend.SetButton(MouseRightButtonPath, false);
            TickProjectionFrames(engine, hudProjection, 2);
        }

        private static Vector2 ResolveCommandTargetScreenPoint(
            GameEngine engine,
            MassNavigationSimulationRuntime simulation,
            ReadOnlySpan<Entity> commandActors)
        {
            Vector2 targetWorldCm = ResolveCommandTargetWorldCm(engine, simulation, commandActors);
            Vector2 candidate = WorldToScreen(engine, targetWorldCm);
            if (AuthoritativeGroundPointerHelper.TryResolveFromScreen(
                    engine.GlobalContext,
                    candidate,
                    out WorldCmInt2 worldCm) &&
                simulation.ContainsWorldPoint(worldCm.X, worldCm.Y))
            {
                return candidate;
            }

            throw new InvalidOperationException("MassNavigation production path test could not resolve the command target to commandable ground.");
        }

        private static Vector2 ResolveCommandTargetWorldCm(
            GameEngine engine,
            MassNavigationSimulationRuntime simulation,
            ReadOnlySpan<Entity> commandActors)
        {
            Vector2 center = ResolveCommandActorCenterWorldCm(engine, simulation, commandActors);
            float offsetCm = MathF.Min(simulation.SolverWindowWidthCm, simulation.SolverWindowHeightCm) * CommandTargetOffsetWindowScale;
            ReadOnlySpan<Vector2> directions = stackalloc Vector2[]
            {
                new(1f, 0f),
                new(0f, 1f),
                new(-1f, 0f),
                new(0f, -1f),
                Vector2.Normalize(new Vector2(1f, 1f)),
                Vector2.Normalize(new Vector2(1f, -1f)),
                Vector2.Normalize(new Vector2(-1f, 1f)),
                Vector2.Normalize(new Vector2(-1f, -1f))
            };

            for (int i = 0; i < directions.Length; i++)
            {
                Vector2 candidate = ClampWorldPoint(simulation.WorldBounds, center + (directions[i] * offsetCm));
                if (Vector2.DistanceSquared(candidate, center) < offsetCm * offsetCm * 0.25f)
                {
                    continue;
                }

                Vector2 screen = WorldToScreen(engine, candidate);
                if (!IsScreenPointInsideView(engine, screen) ||
                    IsScreenPointInsideMinimap(engine, screen))
                {
                    continue;
                }

                if (AuthoritativeGroundPointerHelper.TryResolveFromScreen(
                        engine.GlobalContext,
                        screen,
                        out WorldCmInt2 resolved) &&
                    simulation.ContainsWorldPoint(resolved.X, resolved.Y))
                {
                    return candidate;
                }
            }

            throw new InvalidOperationException("MassNavigation production path test could not find a visible off-center command target.");
        }

        private static Vector2 ResolveCommandActorCenterWorldCm(
            GameEngine engine,
            MassNavigationSimulationRuntime simulation,
            ReadOnlySpan<Entity> commandActors)
        {
            Vector2 sum = Vector2.Zero;
            int count = 0;
            for (int i = 0; i < commandActors.Length; i++)
            {
                if (simulation.TryGetAgentWorldPositionCm(engine.World, commandActors[i], out Vector2 worldCm))
                {
                    sum += worldCm;
                    count++;
                }
            }

            if (count <= 0)
            {
                throw new InvalidOperationException("MassNavigation production path test has no command actor positions.");
            }

            return sum / count;
        }

        private static Vector2 ClampWorldPoint(WorldAabbCm bounds, Vector2 point)
        {
            return new Vector2(
                Math.Clamp(point.X, bounds.Left, bounds.Right),
                Math.Clamp(point.Y, bounds.Top, bounds.Bottom));
        }

        private static Vector2 WorldToScreen(GameEngine engine, Vector2 worldCm)
        {
            var projector = RequireService(engine, CoreServiceKeys.ScreenProjector);
            return projector.WorldToScreen(new Vector3(worldCm.X / 100f, 0f, worldCm.Y / 100f));
        }

        private static bool IsScreenPointInsideView(GameEngine engine, Vector2 screen)
        {
            var view = RequireService(engine, CoreServiceKeys.ViewController);
            return float.IsFinite(screen.X) &&
                float.IsFinite(screen.Y) &&
                screen.X >= 0f &&
                screen.X <= view.Resolution.X &&
                screen.Y >= 0f &&
                screen.Y <= view.Resolution.Y;
        }

        private static bool IsScreenPointInsideMinimap(GameEngine engine, Vector2 screen)
        {
            return engine.GetService(CoreServiceKeys.MinimapRuntime) is MinimapRuntime minimapRuntime &&
                (minimapRuntime.ContainsField(screen) ||
                 minimapRuntime.ContainsZoomSlider(screen) ||
                 minimapRuntime.ContainsPresetToggle(screen) ||
                 minimapRuntime.ContainsRotateToggle(screen));
        }

        private static CommandSourceDragGesture ResolveVisibleAgentDragGesture(GameEngine engine)
        {
            var projector = RequireService(engine, CoreServiceKeys.ScreenProjector);
            var view = RequireService(engine, CoreServiceKeys.ViewController);
            var commandSourceConfig = RequireService(engine, CoreServiceKeys.CommandSourceAcquisitionConfig);
            Vector2 resolution = view.Resolution;
            float padding = commandSourceConfig.ClickPickRadiusPixels + commandSourceConfig.DragThresholdPixels;
            bool hasBounds = false;
            ScreenRect bounds = default;

            var query = new QueryDescription().WithAll<MassNavigationAgent, VisualTransform, CullState, CommandSourceSelectableTag>();
            engine.World.Query(in query, (Entity entity, ref MassNavigationAgent _, ref VisualTransform _, ref CullState cull, ref CommandSourceSelectableTag _) =>
            {
                if (!cull.IsVisible ||
                    !SpatialBoundsUtility.TryProjectScreenBounds(engine.World, entity, projector, out ScreenRect candidate))
                {
                    return;
                }

                if (!hasBounds)
                {
                    bounds = candidate;
                    hasBounds = true;
                    return;
                }

                bounds = new ScreenRect(
                    MathF.Min(bounds.MinX, candidate.MinX),
                    MathF.Min(bounds.MinY, candidate.MinY),
                    MathF.Max(bounds.MaxX, candidate.MaxX),
                    MathF.Max(bounds.MaxY, candidate.MaxY));
            });

            if (!hasBounds)
            {
                Assert.Fail("MassNavigation showcase has no projected selectable agents to drive a box selection gesture.");
            }

            Vector2 start = new(
                Clamp(bounds.MinX - padding, 0f, resolution.X),
                Clamp(bounds.MinY - padding, 0f, resolution.Y));
            Vector2 end = new(
                Clamp(bounds.MaxX + padding, 0f, resolution.X),
                Clamp(bounds.MaxY + padding, 0f, resolution.Y));

            float minExtent = commandSourceConfig.DragThresholdPixels + padding;
            if (MathF.Abs(end.X - start.X) <= commandSourceConfig.DragThresholdPixels)
            {
                end.X = Clamp(start.X + minExtent, 0f, resolution.X);
            }

            if (MathF.Abs(end.Y - start.Y) <= commandSourceConfig.DragThresholdPixels)
            {
                end.Y = Clamp(start.Y + minExtent, 0f, resolution.Y);
            }

            return new CommandSourceDragGesture(start, end, ScreenRect.FromPoints(start, end));
        }

        private static CommandSourceDiagnostics CaptureCommandSourceDiagnostics(GameEngine engine, in ScreenRect marquee)
        {
            var projector = RequireService(engine, CoreServiceKeys.ScreenProjector);
            var commandSourceConfig = RequireService(engine, CoreServiceKeys.CommandSourceAcquisitionConfig);
            Entity localPlayer = engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? localObj) &&
                localObj is Entity local &&
                engine.World.IsAlive(local)
                    ? local
                    : default;
            bool hasCurrentView = TryDescribeCommandSourceView(engine, out _);
            bool hasKnowledgeResolver = KnowledgeProjectionConsumer.HasResolver(engine.GlobalContext);
            bool localPlayerResolved = localPlayer != default && engine.World.IsAlive(localPlayer);
            Team localTeam = default;
            bool localHasTeam = localPlayerResolved && engine.World.TryGet(localPlayer, out localTeam);
            int localTeamId = localHasTeam ? localTeam.Id : 0;
            int selectedCount = SnapshotCommandSource(engine).Length;
            int visibleSelectable = 0;
            int projected = 0;
            int screenIntersecting = 0;
            int eligible = 0;
            int eligibleIntersecting = 0;
            int liveVisible = 0;
            ScreenRect dragRect = marquee;

            var query = new QueryDescription().WithAll<MassNavigationAgent, VisualTransform, CullState, CommandSourceSelectableTag>();
            engine.World.Query(in query, (Entity entity, ref MassNavigationAgent _, ref VisualTransform _, ref CullState cull, ref CommandSourceSelectableTag _) =>
            {
                if (!cull.IsVisible)
                {
                    return;
                }

                visibleSelectable++;
                bool hasProjectedBounds = SpatialBoundsUtility.TryProjectScreenBounds(engine.World, entity, projector, out ScreenRect bounds);
                if (hasProjectedBounds)
                {
                    projected++;
                }

                bool intersects = hasProjectedBounds && bounds.Intersects(in dragRect);
                if (intersects)
                {
                    screenIntersecting++;
                }

                if (hasKnowledgeResolver &&
                    localPlayerResolved &&
                    KnowledgeProjectionConsumer.CanReadPositionForViewer(
                        engine.World,
                        engine.GlobalContext,
                        localPlayer,
                        entity,
                        KnowledgePositionAccess.Live,
                        out KnowledgeProjection projection) &&
                    projection.Presence == KnowledgePresence.LiveVisible)
                {
                    liveVisible++;
                }

                bool canAcquire = localPlayerResolved &&
                    CommandSourceEligibility.CanAcquire(
                        engine.World,
                        engine.GlobalContext,
                        localPlayer,
                        entity,
                        (commandSourceConfig.TargetFilter ?? throw new InvalidOperationException("commandSource.targetFilter is missing.")).ParseRelationFilter());
                if (canAcquire)
                {
                    eligible++;
                }

                if (intersects && canAcquire)
                {
                    eligibleIntersecting++;
                }
            });

            return new CommandSourceDiagnostics(
                localPlayer,
                localTeamId,
                localHasTeam,
                hasCurrentView,
                hasKnowledgeResolver,
                visibleSelectable,
                projected,
                screenIntersecting,
                liveVisible,
                eligible,
                eligibleIntersecting,
                selectedCount);
        }

        private static float Clamp(float value, float min, float max)
        {
            return MathF.Min(MathF.Max(value, min), max);
        }

        private static int CountActiveCommandMarkers(GameEngine engine)
        {
            var performers = RequireService(engine, CoreServiceKeys.PerformerEntityRuntime);
            var definitions = RequireService(engine, CoreServiceKeys.PerformerDefinitionRegistry);
            int lightMarkerId = definitions.GetId(LightCommandMarkerPerformerId);
            int heavyMarkerId = definitions.GetId(HeavyCommandMarkerPerformerId);
            if (lightMarkerId <= 0 || heavyMarkerId <= 0)
            {
                throw new InvalidOperationException("MassNavigation command marker performer definitions are not registered.");
            }

            int count = 0;
            var query = new QueryDescription().WithAll<PerformerState>();
            engine.World.Query(in query, (ref PerformerState state) =>
            {
                if (state.DefId == lightMarkerId ||
                    state.DefId == heavyMarkerId)
                {
                    count++;
                }
            });

            return count;
        }

        private static Vector2[] CaptureCommandActorWorldPositions(
            GameEngine engine,
            MassNavigationSimulationRuntime simulation,
            ReadOnlySpan<Entity> commandActors)
        {
            var positions = new Vector2[commandActors.Length];
            for (int i = 0; i < commandActors.Length; i++)
            {
                if (!simulation.TryGetAgentWorldPositionCm(engine.World, commandActors[i], out positions[i]))
                {
                    throw new InvalidOperationException(DescribeEntityCommandState(engine, commandActors[i]));
                }
            }

            return positions;
        }

        private static int CountMovedCommandActors(
            GameEngine engine,
            MassNavigationSimulationRuntime simulation,
            ReadOnlySpan<Entity> commandActors,
            ReadOnlySpan<Vector2> positionsBefore)
        {
            if (positionsBefore.Length != commandActors.Length)
            {
                throw new InvalidOperationException("MassNavigation movement sample must match command actor count.");
            }

            int moved = 0;
            float epsilonSquared = MovementEpsilonCm * MovementEpsilonCm;
            for (int i = 0; i < commandActors.Length; i++)
            {
                if (simulation.TryGetAgentWorldPositionCm(engine.World, commandActors[i], out Vector2 current) &&
                    Vector2.DistanceSquared(current, positionsBefore[i]) > epsilonSquared)
                {
                    moved++;
                }
            }

            return moved;
        }

        private static void AssertCommandActorsAreCommandable(GameEngine engine, ReadOnlySpan<Entity> commandActors)
        {
            int playerId = engine.MergedConfig.StartupLocalPlayerId;
            for (int i = 0; i < commandActors.Length; i++)
            {
                Entity entity = commandActors[i];
                string diagnostics = DescribeEntityCommandState(engine, entity);
                Assert.That(engine.World.IsAlive(entity), Is.True, diagnostics);
                Assert.That(engine.World.TryGet(entity, out PlayerOwner owner), Is.True, diagnostics);
                Assert.That(owner.PlayerId, Is.EqualTo(playerId), diagnostics);
            }
        }

        private static void AssertLocalScenarioAgentsAreCommandable(GameEngine engine)
        {
            int playerId = engine.MergedConfig.StartupLocalPlayerId;
            Entity localPlayer = RequireService(engine, CoreServiceKeys.LocalPlayerEntity);
            Assert.That(engine.World.TryGet(localPlayer, out Team localTeam), Is.True, "Startup local player must have a Team.");

            int localTeamAgents = 0;
            int localOwnedAgents = 0;
            var query = new QueryDescription().WithAll<MassNavigationAgent, Team>();
            engine.World.Query(in query, (Entity entity, ref MassNavigationAgent _, ref Team team) =>
            {
                if (team.Id != localTeam.Id)
                {
                    return;
                }

                localTeamAgents++;
                if (engine.World.TryGet(entity, out PlayerOwner owner) &&
                    owner.PlayerId == playerId)
                {
                    localOwnedAgents++;
                }
            });

            Assert.That(localTeamAgents, Is.GreaterThan(0), $"MassNavigation scenario should spawn agents for local team {localTeam.Id}.");
            Assert.That(
                localOwnedAgents,
                Is.EqualTo(localTeamAgents),
                $"MassNavigation scenario local team {localTeam.Id} agents must be owned by startup player {playerId}.");
        }

        private static string DescribeEntityCommandState(GameEngine engine, Entity entity)
        {
            string alive = engine.World.IsAlive(entity) ? "alive" : "dead";
            string team = engine.World.TryGet(entity, out Team teamComponent)
                ? teamComponent.Id.ToString()
                : "none";
            string owner = engine.World.TryGet(entity, out PlayerOwner ownerComponent)
                ? ownerComponent.PlayerId.ToString()
                : "none";
            var commandSourceConfig = RequireService(engine, CoreServiceKeys.CommandSourceAcquisitionConfig);
            string relationFilter = commandSourceConfig.TargetFilter?.RelationFilter ?? string.Empty;
            return $"commandActor={entity.Id}, state={alive}, team={team}, playerOwner={owner}, targetRelationFilter={relationFilter}";
        }

        private static int CountActiveMoveOrders(GameEngine engine, ReadOnlySpan<Entity> commandActors)
        {
            int count = 0;
            for (int i = 0; i < commandActors.Length; i++)
            {
                Entity entity = commandActors[i];
                if (engine.World.IsAlive(entity) &&
                    engine.World.TryGet(entity, out OrderBuffer orders) &&
                    orders.HasActive)
                {
                    count++;
                }
            }

            return count;
        }

        private static Entity[] SnapshotCommandSource(GameEngine engine)
        {
            Entity owner = RequireService(engine, CoreServiceKeys.LocalPlayerEntity);
            return EntityCollectionContextRuntime.Snapshot(engine.GlobalContext, owner, EntityCollectionKeys.CommandSource);
        }

        private static bool TryDescribeCommandSourceView(GameEngine engine, out EntityCollectionView view)
        {
            view = default;
            Entity owner = RequireService(engine, CoreServiceKeys.LocalPlayerEntity);
            if (!engine.TryGetService(CoreServiceKeys.EntityCollectionStore, out EntityCollectionStore collections))
            {
                return false;
            }

            return EntityCollectionContextRuntime.TryDescribeView(collections, owner, EntityCollectionKeys.CommandSource, out view);
        }

        private static void ReplaceCommandSource(GameEngine engine, Entity owner, ReadOnlySpan<Entity> members)
        {
            EntityCollectionStore collections = RequireService(engine, CoreServiceKeys.EntityCollectionStore);
            var descriptor = EntityCollectionDescriptor.Create(
                EntityCollectionKeys.CommandSource,
                EntityCollectionSourceKind.Explicit,
                EntityCollectionRoleKind.CommandSource,
                owner,
                members.Length > 0 ? members[0] : Entity.Null,
                "Command source",
                $"{members.Length} entity(s)");
            collections.Replace(owner, descriptor, members, owner);
        }

        private static T RequireService<T>(GameEngine engine, ServiceKey<T> key)
        {
            T value = engine.GetService(key);
            return value ?? throw new InvalidOperationException($"{key.Name} service is missing.");
        }

        private static int CountWorldHudItems(WorldHudBatchBuffer worldHud, WorldHudItemKind kind)
        {
            int count = 0;
            ReadOnlySpan<WorldHudItem> span = worldHud.GetSpan();
            for (int i = 0; i < span.Length; i++)
            {
                if (span[i].Kind == kind)
                {
                    count++;
                }
            }

            return count;
        }

        private static Entity[] CollectMassNavigationAgents(GameEngine engine, int expectedAgents)
        {
            var agents = new Entity[expectedAgents];
            int count = 0;
            var query = new QueryDescription().WithAll<MassNavigationAgent, VisualTransform, CullState, CommandSourceSelectableTag>();
            engine.World.Query(in query, (Entity entity, ref MassNavigationAgent _, ref VisualTransform _, ref CullState cull, ref CommandSourceSelectableTag _) =>
            {
                if (!cull.IsVisible)
                {
                    return;
                }

                Assert.That(count, Is.LessThan(agents.Length), "MassNavigation showcase authored more selectable visible agents than its scenario count.");
                agents[count++] = entity;
            });

            Assert.That(count, Is.EqualTo(expectedAgents));
            return agents;
        }

        private static void AssertScenarioAgentTemplatesDoNotDriveHealthPeriodically(
            GameEngine engine,
            MassNavigationSimulationRuntime simulation)
        {
            var checkedTemplateIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < simulation.Config.Presentation.Teams.Length; i++)
            {
                MassNavigationTeamPresentationConfig team = simulation.Config.Presentation.Teams[i];
                AssertAgentTemplateDoesNotDriveHealthPeriodically(engine, team.LightTemplateId, checkedTemplateIds);
                AssertAgentTemplateDoesNotDriveHealthPeriodically(engine, team.HeavyTemplateId, checkedTemplateIds);
            }
        }

        private static void AssertAgentTemplateDoesNotDriveHealthPeriodically(
            GameEngine engine,
            string templateId,
            HashSet<string> checkedTemplateIds)
        {
            Assert.That(templateId, Is.Not.Empty);
            if (!checkedTemplateIds.Add(templateId))
            {
                return;
            }

            EntityTemplate template = engine.MapLoader.TemplateRegistry.Get(templateId)
                ?? throw new InvalidOperationException($"MassNavigation showcase agent template '{templateId}' is not registered.");
            Assert.That(
                template.OnSpawnEffect,
                Is.Null.Or.Empty,
                $"MassNavigation 10k showcase template '{templateId}' must not attach periodic health effects; HUD text/bar inputs must stay stable unless gameplay changes Health.");
        }

        private static Dictionary<int, AgentHealthSample> CaptureAgentHealth(GameEngine engine, int expectedAgents)
        {
            int healthAttributeId = AttributeRegistry.GetId("Health");
            Assert.That(healthAttributeId, Is.GreaterThanOrEqualTo(0), "MassNavigation HUD health stability requires the Health attribute id.");

            var samples = new Dictionary<int, AgentHealthSample>(expectedAgents);
            var query = new QueryDescription().WithAll<MassNavigationAgent, AttributeBuffer>();
            engine.World.Query(in query, (Entity entity, ref MassNavigationAgent _, ref AttributeBuffer attributes) =>
            {
                samples.Add(
                    entity.Id,
                    new AgentHealthSample(
                        attributes.GetCurrent(healthAttributeId),
                        attributes.GetBase(healthAttributeId)));
            });

            Assert.That(samples.Count, Is.EqualTo(expectedAgents));
            return samples;
        }

        private static void AssertAgentHealthStable(
            IReadOnlyDictionary<int, AgentHealthSample> before,
            IReadOnlyDictionary<int, AgentHealthSample> after)
        {
            Assert.That(after.Count, Is.EqualTo(before.Count));
            foreach (KeyValuePair<int, AgentHealthSample> pair in before)
            {
                Assert.That(after.TryGetValue(pair.Key, out AgentHealthSample actual), Is.True);
                Assert.That(actual.Current, Is.EqualTo(pair.Value.Current).Within(0.0001f), $"Agent {pair.Key} Health current changed.");
                Assert.That(actual.Base, Is.EqualTo(pair.Value.Base).Within(0.0001f), $"Agent {pair.Key} Health base changed.");
            }
        }

        private static void AssertScreenHudIdentityStableAcrossProjectionFrames(
            GameEngine engine,
            WorldHudToScreenSystem hudProjection,
            int frames)
        {
            var screenHud = RequireService(engine, CoreServiceKeys.PresentationScreenHudBuffer);
            ScreenHudIdentitySnapshot before = CaptureScreenHudIdentity(screenHud);
            for (int i = 0; i < frames; i++)
            {
                TickProjectionFrames(engine, hudProjection, 1);
                ScreenHudIdentitySnapshot after = CaptureScreenHudIdentity(screenHud);
                Assert.That(after.BarCount, Is.EqualTo(before.BarCount), $"Screen HUD bar count changed on frame {i + 1}.");
                Assert.That(after.TextCount, Is.EqualTo(before.TextCount), $"Screen HUD text count changed on frame {i + 1}.");
                Assert.That(after.BarIdentities, Is.EqualTo(before.BarIdentities), $"Screen HUD bar identities changed on frame {i + 1}.");
                Assert.That(after.TextIdentities, Is.EqualTo(before.TextIdentities), $"Screen HUD text identities changed on frame {i + 1}.");
            }
        }

        private static ScreenHudIdentitySnapshot CaptureScreenHudIdentity(ScreenHudBatchBuffer screenHud)
        {
            ReadOnlySpan<ScreenHudBarItem> bars = screenHud.GetBarSpan();
            ReadOnlySpan<ScreenHudTextItem> texts = screenHud.GetTextSpan();
            long[] barIdentities = new long[bars.Length];
            long[] textIdentities = new long[texts.Length];
            for (int i = 0; i < bars.Length; i++)
            {
                barIdentities[i] = ComposeHudIdentity(bars[i].StableId, bars[i].DirtySerial);
            }

            for (int i = 0; i < texts.Length; i++)
            {
                textIdentities[i] = ComposeHudIdentity(texts[i].StableId, texts[i].DirtySerial);
            }

            Array.Sort(barIdentities);
            Array.Sort(textIdentities);
            return new ScreenHudIdentitySnapshot(bars.Length, texts.Length, barIdentities, textIdentities);
        }

        private static long ComposeHudIdentity(int stableId, int dirtySerial)
        {
            return ((long)stableId << 32) ^ (uint)dirtySerial;
        }

        private static string BuildDiagnostics(
            MassNavigationSimulationRuntime simulation,
            MinimapRuntime minimapRuntime,
            MinimapDebugSnapshot minimapSnapshot,
            MinimapMarkerBuffer minimapMarkers,
            MinimapScreenMarkerBuffer minimapScreenMarkers,
            WorldHudBatchBuffer worldHud,
            ScreenHudBatchBuffer screenHud,
            int worldHudBars,
            int worldHudText)
        {
            return string.Join(
                ", ",
                $"agents={simulation.AgentState.TotalAgents}",
                $"minimapVisible={minimapRuntime.Visible}",
                $"minimapPreset={minimapRuntime.Preset}",
                $"minimapBand={minimapSnapshot.ZoomBand}",
                $"minimapHalfExtentCm={minimapSnapshot.HalfExtentCm:0.###}",
                $"minimapMarkers={minimapMarkers.Count}",
                $"minimapScreenMarkers={minimapScreenMarkers.Count}",
                $"minimapVisibleMarkers={minimapSnapshot.VisibleMarkerCount}",
                $"worldHud={worldHud.Count}",
                $"worldHudBars={worldHudBars}",
                $"worldHudText={worldHudText}",
                $"screenHudBars={screenHud.BarCount}",
                $"screenHudText={screenHud.TextCount}");
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

        private readonly record struct ProjectionSample(
            MinimapDebugSnapshot MinimapSnapshot,
            int MinimapScreenMarkers,
            int WorldHudBars,
            int WorldHudText,
            int ScreenHudBars,
            int ScreenHudText,
            string Diagnostics);

        private readonly record struct CommandSourceDragGesture(Vector2 Start, Vector2 End, ScreenRect Marquee);

        private readonly record struct AgentHealthSample(float Current, float Base);

        private readonly record struct ScreenHudIdentitySnapshot(
            int BarCount,
            int TextCount,
            long[] BarIdentities,
            long[] TextIdentities);

        private readonly record struct CommandSourceDiagnostics(
            Entity LocalPlayer,
            int LocalTeamId,
            bool LocalHasTeam,
            bool HasCurrentView,
            bool HasKnowledgeResolver,
            int VisibleSelectable,
            int Projected,
            int ScreenIntersecting,
            int LiveVisible,
            int Eligible,
            int EligibleIntersecting,
            int CurrentCommandSourceCount)
        {
            public override string ToString()
            {
                return string.Join(
                    ", ",
                    $"localPlayer={LocalPlayer}",
                    $"localHasTeam={LocalHasTeam}",
                    $"localTeamId={LocalTeamId}",
                    $"hasCurrentView={HasCurrentView}",
                    $"hasKnowledgeResolver={HasKnowledgeResolver}",
                    $"visibleSelectable={VisibleSelectable}",
                    $"projected={Projected}",
                    $"screenIntersecting={ScreenIntersecting}",
                    $"liveVisible={LiveVisible}",
                    $"eligible={Eligible}",
                    $"eligibleIntersecting={EligibleIntersecting}",
                    $"currentCommandSourceCount={CurrentCommandSourceCount}");
            }
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

            public void SetMousePosition(Vector2 mousePosition)
            {
                _mousePosition = mousePosition;
            }

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
    }
}
