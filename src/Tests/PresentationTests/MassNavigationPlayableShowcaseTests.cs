using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Input.Systems;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.Pathing;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Minimap;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;
using Ludots.Launcher.Backend;
using Ludots.UI;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Events;
using Ludots.UI.Skia;
using MassNavigationMod;
using MassNavigationMod.Runtime;
using MassNavigationMod.Systems;
using MassNavigationMod.UI;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class MassNavigationPlayableShowcaseTests
    {
        [Test]
        public void GuidedShowcaseRuntime_ExposesPlayerReadableStepsAndInteractiveProbes()
        {
            using GameEngine engine = CreateMassNavigationEngineWithUi();
            engine.LoadMap("mass_navigation");

            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            MassNavigationShowcaseGuideRuntime guide = RequireGuide(engine);

            Assert.That(guide.StepCount, Is.GreaterThanOrEqualTo(16));
            string[] titles = guide.Steps.ToArray().Select(step => step.Title).ToArray();
            foreach (string id in new[]
            {
                "U1", "U2", "U3", "U4", "U5", "U6", "U7", "U8",
                "U9", "U10", "U11", "U12", "U13", "U14", "U15", "U16"
            })
            {
                Assert.That(titles.Any(title => title.Contains(id, StringComparison.Ordinal)), Is.True, $"{id} must be a real runtime guide step.");
            }

            Assert.That(titles, Does.Contain("U4 Path-only point query"));
            Assert.That(titles, Does.Contain("U1/U16 NavMesh bake workbench"));
            Assert.That(titles, Does.Contain("U8 Large-selection target allocation"));
            Assert.That(titles, Does.Contain("U16 Runtime bake/query update"));
            Assert.That(guide.CurrentStep.Who, Is.Not.Empty);
            Assert.That(guide.CurrentStep.What, Is.Not.Empty);
            Assert.That(guide.CurrentStep.When, Is.Not.Empty);
            Assert.That(guide.CurrentStep.Where, Is.Not.Empty);
            Assert.That(guide.CurrentStep.Why, Is.Not.Empty);
            Assert.That(guide.CurrentStep.How, Is.Not.Empty);
            Assert.That(guide.CurrentStep.PlayerInput, Is.Not.Empty);
            Assert.That(guide.CurrentStep.PlayerExpected, Is.Not.Empty);
            Assert.That(guide.CurrentStep.ReadablePassSignal, Is.Not.Empty);
            Assert.That(guide.CurrentStep.DebugLegend, Is.Not.Empty);
            Assert.That(guide.PrimaryActionLabel, Is.EqualTo("Bake VHTM Window"));
            Assert.That(guide.OperationMode, Is.EqualTo("Editor tool"));
            Assert.That(guide.OperationContract, Does.Contain("Output:"));
            Assert.That(MassNavigationShowcaseGuideRuntime.ResolvePrimaryActionLabel(MassNavigationShowcaseStepId.PathOnly), Is.EqualTo("Pick Path Preview"));
            Assert.That(MassNavigationShowcaseGuideRuntime.ResolveOperationMode(MassNavigationShowcaseStepId.TargetAllocation), Is.EqualTo("Playable RTS"));
            Assert.That(MassNavigationShowcaseGuideRuntime.ResolveOperationMode(MassNavigationShowcaseStepId.BakeToolQuery), Is.EqualTo("Runtime NavData tool"));
            Assert.That(MassNavigationShowcaseGuideRuntime.ResolveOperationContract(MassNavigationShowcaseStepId.WorldHpa), Does.Contain("numbered macro chunks"));
            Assert.That(guide.NavMeshSample.Available, Is.True);
            Assert.That(guide.NavMeshSample.TriangleCount, Is.GreaterThan(0));
            Assert.That(guide.NavMeshSample.PortalCount, Is.GreaterThan(0));
            Assert.That(guide.NavMeshSample.MinPortalClearanceCm, Is.GreaterThan(0));
            Assert.That(guide.NavMeshSample.AgentRadiusCm, Is.GreaterThan(0));
            Assert.That(guide.NavMeshSample.LogicHeightmapSource, Does.EndWith(".lhtm"));
            Assert.That(guide.NavMeshSample.AreaLegend, Does.Contain("NoFlyZone"));
            Assert.That(guide.NavMeshSample.LayerLegend, Does.Contain("Ground"));
            AssertNavMeshCoverageMatchesBakeDiagnostics(simulation, guide);

            guide.RunPathPreview(simulation);
            Assert.That(guide.CurrentStepId, Is.EqualTo(MassNavigationShowcaseStepId.PathOnly));
            Assert.That(guide.LastActionOrderDelta, Is.EqualTo(0));
            Assert.That(simulation.AcceptanceDiagnostics.PathOnlyPathPoints.Length, Is.EqualTo(simulation.AcceptanceDiagnostics.PathOnlyQuery.PathPointCount));

            guide.SetStep(MassNavigationShowcaseStepId.OrderReuse);
            guide.RecordOrderReuseSelectionPrepared(64);
            Assert.That(guide.CurrentStepId, Is.EqualTo(MassNavigationShowcaseStepId.OrderReuse));
            Assert.That(guide.LastActionText, Does.Contain("Right-click one destination twice"));

            guide.RunTargetAllocationProbe(simulation, 10_000);
            Assert.That(guide.CurrentStepId, Is.EqualTo(MassNavigationShowcaseStepId.TargetAllocation));
            Assert.That(guide.LastActionText, Does.Contain("OrderBuffer"));
            Assert.That(simulation.AcceptanceDiagnostics.TargetAllocation.SelectedCount, Is.EqualTo(0),
                "Arming target allocation must not fabricate slots; allocation proof starts after a real selection/right-click order.");
        }

        [Test]
        public void PathOnlyFocusedShowcase_ConsumesPlayerPickedEndpointsWithoutSubmittingOrders()
        {
            using GameEngine engine = CreateMassNavigationFocusedEntryEngineWithUi("MassNavigationU04PathOnlyQueryShowcaseMod");
            engine.LoadMap("mass_navigation");
            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            MassNavigationShowcaseGuideRuntime guide = RequireGuide(engine);

            var input = new FrozenInputActionReader();
            var pointerButtons = new AuthoritativePointerButtonSnapshot();
            var bindings = new InteractionActionBindings();
            var pathStore = new PathStore(maxPaths: 8, maxPointsPerPath: 8);
            var pathService = new RecordingPathService(pathStore);
            var pathPreview = new MassNavigationPathPreviewInputSystem(engine, simulation, guide);
            Vector2 start = new(
                simulation.BakeDataDiagnostics!.WorldMinXCm + (simulation.BakeDataDiagnostics.MacroChunkSizeXCm * 10) + 1_000,
                simulation.BakeDataDiagnostics.WorldMinYCm + (simulation.BakeDataDiagnostics.MacroChunkSizeYCm * 12) + 1_000);
            Vector2 goal = new(
                simulation.BakeDataDiagnostics.WorldMinXCm + (simulation.BakeDataDiagnostics.MacroChunkSizeXCm * 18) + 2_000,
                simulation.BakeDataDiagnostics.WorldMinYCm + (simulation.BakeDataDiagnostics.MacroChunkSizeYCm * 15) + 2_000);

            engine.SetService(CoreServiceKeys.AuthoritativeInput, input);
            engine.SetService(CoreServiceKeys.AuthoritativePointerButtons, pointerButtons);
            engine.SetService(CoreServiceKeys.InteractionActionBindings, bindings);
            engine.SetService(CoreServiceKeys.PathStore, pathStore);
            engine.SetService(CoreServiceKeys.PathService, pathService);
            guide.RunPathPreview(simulation);

            SetPointerPick(input, pointerButtons, bindings.ConfirmActionId, start);
            pathPreview.Update(1f / 60f);

            Assert.That(pathService.Requests, Is.Empty);
            Assert.That(guide.LastActionText, Does.Contain("start picked"));
            Assert.That(guide.HasPathPreviewStart, Is.True);
            Assert.That(guide.PathPreviewStartWorldCm, Is.EqualTo(start));
            Assert.That(guide.HasPathPreviewGoal, Is.False);
            Assert.That(engine.GetService(CoreServiceKeys.PointerInputCaptured), Is.True);

            ClearPointerPick(input, pointerButtons, bindings.ConfirmActionId);
            engine.SetService(CoreServiceKeys.PointerInputCaptured, false);
            SetPointerPick(input, pointerButtons, bindings.CommandActionId, goal);
            pathPreview.Update(1f / 60f);

            Assert.That(pathService.Requests, Has.Count.EqualTo(1));
            Assert.That(pathService.Requests[0].Domain, Is.EqualTo(PathDomain.NavMesh));
            Assert.That(pathService.Requests[0].Start.Xcm, Is.EqualTo((int)MathF.Round(start.X)));
            Assert.That(pathService.Requests[0].Start.Ycm, Is.EqualTo((int)MathF.Round(start.Y)));
            Assert.That(pathService.Requests[0].Goal.Xcm, Is.EqualTo((int)MathF.Round(goal.X)));
            Assert.That(pathService.Requests[0].Goal.Ycm, Is.EqualTo((int)MathF.Round(goal.Y)));
            Assert.That(simulation.AcceptanceDiagnostics.PathOnlyQuery.Available, Is.True);
            Assert.That(simulation.AcceptanceDiagnostics.PathOnlyQuery.NoOrderSubmitted, Is.True);
            Assert.That(simulation.AcceptanceDiagnostics.PathOnlyQuery.QuerySource, Is.EqualTo(nameof(RecordingPathService)));
            Assert.That(simulation.AcceptanceDiagnostics.PathOnlyQuery.RouteProvenance, Is.EqualTo("RecordingPathService/NavMesh"));
            Assert.That(simulation.AcceptanceDiagnostics.PathOnlyQuery.StartWorldCm, Is.EqualTo(start));
            Assert.That(simulation.AcceptanceDiagnostics.PathOnlyQuery.GoalWorldCm, Is.EqualTo(goal));
            Assert.That(guide.HasPathPreviewStart, Is.True);
            Assert.That(guide.HasPathPreviewGoal, Is.True);
            Assert.That(guide.PathPreviewGoalWorldCm, Is.EqualTo(goal));
            Assert.That(simulation.AcceptanceDiagnostics.PathOnlyQuery.PathPointCount, Is.EqualTo(3));
            Assert.That(simulation.AcceptanceDiagnostics.PathOnlyQuery.StartMacroChunkX, Is.EqualTo(10));
            Assert.That(simulation.AcceptanceDiagnostics.PathOnlyQuery.StartMacroChunkY, Is.EqualTo(12));
            Assert.That(simulation.AcceptanceDiagnostics.PathOnlyQuery.GoalMacroChunkX, Is.EqualTo(18));
            Assert.That(simulation.AcceptanceDiagnostics.PathOnlyQuery.GoalMacroChunkY, Is.EqualTo(15));
            Assert.That(simulation.AcceptanceDiagnostics.HpaMacro.StartMacroChunkX, Is.EqualTo(10));
            Assert.That(simulation.AcceptanceDiagnostics.HpaMacro.GoalMacroChunkX, Is.EqualTo(18));
            Assert.That(guide.LastActionOrderDelta, Is.EqualTo(0));
            Assert.That(guide.LastActionText, Does.Contain("orderDelta=0"));
            Assert.That(simulation.CommandCountFrame + simulation.PendingCommandCount, Is.EqualTo(0));
            Assert.That(engine.GetService(CoreServiceKeys.PointerInputCaptured), Is.True);
        }

        [Test]
        public void MinimapInputConsumer_ProvidesConfirmAndCommandGroundOverridesForPathShowcase()
        {
            using GameEngine engine = CreateMassNavigationFocusedEntryEngineWithUi("MassNavigationU04PathOnlyQueryShowcaseMod");
            engine.LoadMap("mass_navigation");

            var runtime = new MinimapRuntime(new MinimapRuntimeConfig
            {
                MinZoomExtentMode = MinimapZoomExtentMode.ExplicitCm,
                MaxZoomExtentMode = MinimapZoomExtentMode.ExplicitCm,
                MinZoomExplicitHalfExtentCm = 10_000f,
                MaxZoomExplicitHalfExtentCm = 80_000f,
                MinFieldSizePx = 160,
                MaxFieldSizePx = 220,
            });
            runtime.Visible = true;
            runtime.SetViewport(120_000f, 220_000f, 60_000f);
            runtime.Refresh(engine, new MinimapMarkerBuffer(4), new MinimapScreenMarkerBuffer(4));

            var input = new PlayerInputHandler(new NullInputBackend(), CreateMinimapInputConfig());
            var accumulator = new AuthoritativeInputAccumulator();
            var pointerAccumulator = new AuthoritativePointerButtonAccumulator();
            var inputRuntime = new InputRuntimeSystem(engine.GlobalContext, accumulator, pointerAccumulator);
            var inputSnapshot = new FrozenInputActionReader();
            var pointerSnapshot = new AuthoritativePointerButtonSnapshot();
            var bindings = new InteractionActionBindings();
            var consumer = new MinimapInputConsumer(runtime);
            var frameConsumers = new List<IInputFrameConsumer> { consumer };

            engine.SetService(CoreServiceKeys.InputHandler, input);
            engine.SetService(CoreServiceKeys.AuthoritativeInput, inputSnapshot);
            engine.SetService(CoreServiceKeys.AuthoritativePointerButtons, pointerSnapshot);
            engine.SetService(CoreServiceKeys.InteractionActionBindings, bindings);
            engine.SetService(CoreServiceKeys.InputFrameConsumers, frameConsumers);
            engine.SetService(CoreServiceKeys.PointerInputCaptured, false);
            engine.SetService(CoreServiceKeys.ScreenRayProvider, new CountingScreenRayProvider());
            engine.SetService(CoreServiceKeys.VisualHeightmap, CreateFlatHeightmap(engine.WorldSizeSpec.Bounds));

            Vector2 fieldCenter = new(runtime.FieldX + (runtime.FieldSize * 0.5f), runtime.FieldY + (runtime.FieldSize * 0.5f));
            Assert.That(runtime.TryScreenToWorld(fieldCenter, out Vector2 expectedWorld), Is.True);

            input.InjectAction(bindings.PointerPositionActionId, new Vector3(fieldCenter.X, fieldCenter.Y, 0f));
            input.InjectAction(bindings.ConfirmActionId, Vector3.One);
            inputRuntime.Update(1f / 60f);
            accumulator.BuildTickSnapshot(inputSnapshot);
            pointerAccumulator.BuildTickSnapshot(pointerSnapshot);

            Assert.That(inputSnapshot.PressedThisFrame(bindings.ConfirmActionId), Is.True);
            Assert.That(AuthoritativeGroundPointerHelper.TryRead(inputSnapshot, out WorldCmInt2 confirmWorld), Is.True);
            Assert.That(confirmWorld.X, Is.EqualTo((int)MathF.Round(expectedWorld.X)).Within(1));
            Assert.That(confirmWorld.Y, Is.EqualTo((int)MathF.Round(expectedWorld.Y)).Within(1));
            Assert.That(engine.GetService(CoreServiceKeys.ScreenRayProvider) is CountingScreenRayProvider { CallCount: 0 }, Is.True,
                "Minimap confirm must feed the authoritative override instead of falling through to viewport ground raycast.");

            engine.SetService(CoreServiceKeys.PointerInputCaptured, false);
            accumulator.Clear();
            pointerSnapshot.Clear();
            inputSnapshot.Clear();
            inputRuntime.Update(1f / 60f);
            accumulator.BuildTickSnapshot(inputSnapshot);
            pointerAccumulator.BuildTickSnapshot(pointerSnapshot);
            accumulator.Clear();
            pointerSnapshot.Clear();
            inputSnapshot.Clear();

            var rayProvider = engine.GetService(CoreServiceKeys.ScreenRayProvider) as CountingScreenRayProvider
                ?? throw new InvalidOperationException("CountingScreenRayProvider missing.");
            rayProvider.Reset();

            input.InjectAction(bindings.PointerPositionActionId, new Vector3(fieldCenter.X, fieldCenter.Y, 0f));
            input.InjectAction(bindings.CommandActionId, Vector3.One);
            inputRuntime.Update(1f / 60f);
            accumulator.BuildTickSnapshot(inputSnapshot);
            pointerAccumulator.BuildTickSnapshot(pointerSnapshot);

            Assert.That(inputSnapshot.PressedThisFrame(bindings.CommandActionId), Is.True);
            Assert.That(AuthoritativeGroundPointerHelper.TryRead(inputSnapshot, out WorldCmInt2 commandWorld), Is.True);
            Assert.That(commandWorld.X, Is.EqualTo((int)MathF.Round(expectedWorld.X)).Within(1));
            Assert.That(commandWorld.Y, Is.EqualTo((int)MathF.Round(expectedWorld.Y)).Within(1));
            Assert.That(rayProvider.CallCount, Is.EqualTo(0),
                "Minimap command must feed the authoritative override instead of falling through to viewport ground raycast.");
        }

        [Test]
        public void GuidedShowcasePresentation_EmitsReadableScreenAndGroundDebugOverlays()
        {
            using GameEngine engine = CreateMassNavigationFocusedEntryEngineWithUi("MassNavigationU04PathOnlyQueryShowcaseMod");
            PerformerBlacksmithShowcaseTestHarness.LoadMap(engine, "mass_navigation", frames: 4);

            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            MassNavigationShowcaseGuideRuntime guide = RequireGuide(engine);
            var system = new MassNavigationShowcasePresentationSystem(engine, simulation, guide);
            var screen = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
                ?? throw new InvalidOperationException("ScreenOverlayBuffer missing.");
            var ground = engine.GetService(CoreServiceKeys.GroundOverlayBuffer)
                ?? throw new InvalidOperationException("GroundOverlayBuffer missing.");

            guide.RunPathPreview(simulation);
            MassNavigationRuntime.RequestCameraJump(engine, ResolvePathMidpoint(simulation), ResolvePathCameraDistanceCm(simulation));
            PerformerBlacksmithShowcaseTestHarness.Tick(engine, 2);
            system.Update(0.016f);
            string[] strings = GetOverlayStrings(screen);
            Assert.That(strings.Any(text => text.Contains("U4 Path Preview", StringComparison.Ordinal)), Is.True, DumpStrings(strings));
            Assert.That(strings.Any(text => text.Contains("pathpoints", StringComparison.Ordinal) && text.Contains("noOrder", StringComparison.Ordinal)), Is.True, DumpStrings(strings));
            Assert.That(strings.Any(text => text.StartsWith("Now:", StringComparison.Ordinal)), Is.False, DumpStrings(strings));
            Assert.That(strings.Any(text => text.StartsWith("Do:", StringComparison.Ordinal)), Is.False, DumpStrings(strings));
            Assert.That(strings.Any(text => text.StartsWith("Look:", StringComparison.Ordinal)), Is.False, DumpStrings(strings));
            Assert.That(strings.Any(text => text.StartsWith("Pass:", StringComparison.Ordinal)), Is.False, DumpStrings(strings));
            Assert.That(strings.Any(text => text.Contains("NoOrderSubmitted", StringComparison.Ordinal) ||
                text.Contains("noOrder", StringComparison.Ordinal)), Is.True, DumpStrings(strings));
            Assert.That(strings.Any(text => text.Contains("S picked start", StringComparison.Ordinal)), Is.True, DumpStrings(strings));
            Assert.That(strings.Any(text => text.Contains("left=start; right=goal; ground or minimap; no order", StringComparison.Ordinal)), Is.True, DumpStrings(strings));
            Assert.That(strings.Any(text => text.Contains("source=", StringComparison.Ordinal) && text.Contains("NavMesh", StringComparison.Ordinal)), Is.True, DumpStrings(strings));
            Assert.That(strings.Any(text => text.Contains("pathpoints: immutable query result", StringComparison.Ordinal)), Is.True);
            Assert.That(strings.Any(text => text.Contains("waypoints: editable order intent", StringComparison.Ordinal)), Is.False, DumpStrings(strings));
            Assert.That(screen.GetSpan().ToArray().Count(item => item.Kind == ScreenOverlayItemKind.Line), Is.GreaterThan(0),
                "U04 route must be readable as one projected route line, not as waypoint/corridor debug clutter.");
            Assert.That(ground.Count, Is.GreaterThan(0));
            Assert.That(ground.GetSpan().ToArray().Count(item => item.Shape == GroundOverlayShape.Line), Is.GreaterThan(0),
                "U04 must render the actual pathpoints as a visible ground route band; corridor and portal semantics belong to NavMesh/Bake/Waypoint showcases.");
            Assert.That(ground.GetSpan().ToArray().Count(item => item.Shape == GroundOverlayShape.Circle), Is.GreaterThan(0));
        }

        [Test]
        public void MassNavigationPanel_MountsGuidedShowcaseInsteadOfClearingIt()
        {
            using GameEngine engine = CreateMassNavigationEngineWithUi();
            engine.LoadMap("mass_navigation");

            UIRoot root = (UIRoot)(engine.GetService(CoreServiceKeys.UIRoot)
                ?? throw new InvalidOperationException("UIRoot missing."));
            Assert.That(root.Scene, Is.Not.Null);

            MassNavigationSimulationRuntime _ = RequireSimulation(engine);
            MassNavigationShowcaseGuideRuntime guide = RequireGuide(engine);
            Assert.That(guide.CurrentStep.Title, Is.EqualTo("U1 VisualHeightmap bake"));
            Assert.That(root.Scene, Is.Not.Null);
        }

        [Test]
        public void FocusedEntryMods_ResolveAsCleanRootsAndDriveSingleShowcasePreset()
        {
            string repoRoot = PerformerBlacksmithShowcaseTestHarness.FindRepoRoot();
            var service = new LauncherService(repoRoot);

            var result = service.Resolve(
                new[] { "mod:MassNavigationU05WorldHpaRouteShowcaseMod" },
                LauncherPlatformIds.Raylib,
                LauncherBuildMode.Never);
            var startupSetting = result.Plan.Diagnostics.Settings.Single(setting =>
                string.Equals(setting.Key, "startupMapId", StringComparison.OrdinalIgnoreCase));

            Assert.That(result.Plan.RootModIds, Is.EqualTo(new[] { "MassNavigationU05WorldHpaRouteShowcaseMod" }));
            Assert.That(result.Plan.OrderedModIds, Does.Contain("MassNavigationMod"));
            var orderedMods = result.Plan.OrderedModIds.ToList();
            Assert.That(orderedMods.IndexOf("MassNavigationMod"), Is.LessThan(orderedMods.IndexOf("MassNavigationU05WorldHpaRouteShowcaseMod")));
            Assert.That(startupSetting.EffectiveValue?.GetValue<string>(), Is.EqualTo("mass_navigation"));
            Assert.That(startupSetting.EffectiveSource, Does.Contain("MassNavigationU05WorldHpaRouteShowcaseMod").IgnoreCase);

            using GameEngine engine = CreateMassNavigationFocusedEntryEngineWithUi("MassNavigationU05WorldHpaRouteShowcaseMod");
            engine.LoadMap("mass_navigation");
            MassNavigationShowcaseGuideRuntime guide = RequireGuide(engine);
            Assert.That(guide.FocusedPanel, Is.True);
            Assert.That(guide.ShowcaseId, Is.EqualTo("mass_navigation_u05_world_hpa_route"));
            Assert.That(guide.StepCount, Is.EqualTo(1));
            Assert.That(guide.CurrentStepId, Is.EqualTo(MassNavigationShowcaseStepId.WorldHpa));
            Assert.That(guide.AllowsStep(MassNavigationShowcaseStepId.WorldHpa), Is.True);
            Assert.That(guide.AllowsStep(MassNavigationShowcaseStepId.PathOnly), Is.False);
            Assert.That(guide.PlayerPerspective, Does.Contain("numbered crossed chunks"));
            Assert.That(guide.ModAuthorPerspective, Does.Contain("macro route contract"));
            Assert.That(guide.PrimaryActionLabel, Is.EqualTo("Pick HPA Route"));
            Assert.That(guide.OperationMode, Is.EqualTo("Playable RTS"));
            Assert.That(guide.OperationContract, Does.Contain("active-window HPA graph route"));
        }

        [TestCase("MassNavigationU05WorldHpaRouteShowcaseMod", MassNavigationShowcaseStepId.WorldHpa, "Pick HPA Route")]
        [TestCase("MassNavigationU06StrategySwitchShowcaseMod", MassNavigationShowcaseStepId.StrategySwitch, "Pick Strategy Route")]
        [TestCase("MassNavigationU10WaypointAuthoringShowcaseMod", MassNavigationShowcaseStepId.WaypointAuthoring, "Edit Waypoint Plan")]
        public void PathDrivenFocusedShowcases_UsePlayerPickedEndpointsForTheirActualUseCase(
            string entryModId,
            MassNavigationShowcaseStepId expectedStep,
            string primaryAction)
        {
            using GameEngine engine = CreateMassNavigationFocusedEntryEngineWithUi(entryModId);
            engine.LoadMap("mass_navigation");
            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            MassNavigationShowcaseGuideRuntime guide = RequireGuide(engine);

            var input = new FrozenInputActionReader();
            var pointerButtons = new AuthoritativePointerButtonSnapshot();
            var bindings = new InteractionActionBindings();
            var pathStore = new PathStore(maxPaths: 8, maxPointsPerPath: 8);
            var pathService = new RecordingPathService(pathStore);
            var pathPreview = new MassNavigationPathPreviewInputSystem(engine, simulation, guide);
            Vector2 start = new(
                simulation.BakeDataDiagnostics!.WorldMinXCm + (simulation.BakeDataDiagnostics.MacroChunkSizeXCm * 11) + 1_200,
                simulation.BakeDataDiagnostics.WorldMinYCm + (simulation.BakeDataDiagnostics.MacroChunkSizeYCm * 13) + 1_600);
            Vector2 goal = new(
                simulation.BakeDataDiagnostics.WorldMinXCm + (simulation.BakeDataDiagnostics.MacroChunkSizeXCm * 20) + 2_100,
                simulation.BakeDataDiagnostics.WorldMinYCm + (simulation.BakeDataDiagnostics.MacroChunkSizeYCm * 17) + 2_400);

            engine.SetService(CoreServiceKeys.AuthoritativeInput, input);
            engine.SetService(CoreServiceKeys.AuthoritativePointerButtons, pointerButtons);
            engine.SetService(CoreServiceKeys.InteractionActionBindings, bindings);
            engine.SetService(CoreServiceKeys.PathStore, pathStore);
            engine.SetService(CoreServiceKeys.PathService, pathService);

            Assert.That(guide.CurrentStepId, Is.EqualTo(expectedStep));
            Assert.That(guide.PrimaryActionLabel, Is.EqualTo(primaryAction));
            guide.ArmPathDrivenOperation(expectedStep);

            SetPointerPick(input, pointerButtons, bindings.ConfirmActionId, start);
            pathPreview.Update(1f / 60f);
            ClearPointerPick(input, pointerButtons, bindings.ConfirmActionId);
            engine.SetService(CoreServiceKeys.PointerInputCaptured, false);
            SetPointerPick(input, pointerButtons, bindings.CommandActionId, goal);
            pathPreview.Update(1f / 60f);

            Assert.That(pathService.Requests, Has.Count.EqualTo(1), entryModId);
            Assert.That(guide.CurrentStepId, Is.EqualTo(expectedStep), entryModId);
            Assert.That(simulation.AcceptanceDiagnostics.PathOnlyQuery.Available, Is.True, entryModId);
            Assert.That(simulation.AcceptanceDiagnostics.PathOnlyQuery.NoOrderSubmitted, Is.True, entryModId);
            Assert.That(simulation.AcceptanceDiagnostics.PathOnlyQuery.StartWorldCm, Is.EqualTo(start), entryModId);
            Assert.That(simulation.AcceptanceDiagnostics.PathOnlyQuery.GoalWorldCm, Is.EqualTo(goal), entryModId);
            Assert.That(guide.LastActionOrderDelta, Is.EqualTo(0), entryModId);
            Assert.That(simulation.CommandCountFrame + simulation.PendingCommandCount, Is.EqualTo(0), entryModId);
        }

        [Test]
        public void RuntimeBakeFocusedShowcase_DrawsObstaclePolygonAndUpdatesRuntimeNavData()
        {
            using GameEngine engine = CreateMassNavigationFocusedEntryEngineWithUi("MassNavigationU16BakeToolQueryShowcaseMod");
            engine.LoadMap("mass_navigation");
            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            MassNavigationShowcaseGuideRuntime guide = RequireGuide(engine);
            MassNavigationBakeDataDiagnostics bake = simulation.BakeDataDiagnostics
                ?? throw new InvalidOperationException("Mass navigation bake diagnostics missing.");

            var input = new FrozenInputActionReader();
            var pointerButtons = new AuthoritativePointerButtonSnapshot();
            var bindings = new InteractionActionBindings();
            var pathStore = new PathStore(maxPaths: 16, maxPointsPerPath: 8);
            var pathService = new RecordingPathService(pathStore);
            var pathPreview = new MassNavigationPathPreviewInputSystem(engine, simulation, guide);
            var runtimeAuthoring = new MassNavigationRuntimeBakeAuthoringInputSystem(engine, simulation, guide);
            var presentation = new MassNavigationShowcasePresentationSystem(engine, simulation, guide);
            var screen = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
                ?? throw new InvalidOperationException("ScreenOverlayBuffer missing.");
            var ground = engine.GetService(CoreServiceKeys.GroundOverlayBuffer)
                ?? throw new InvalidOperationException("GroundOverlayBuffer missing.");

            int navChunkX = guide.NavMeshSample.Available ? guide.NavMeshSample.ChunkX : bake.MacroChunkColumns / 2;
            int navChunkY = guide.NavMeshSample.Available ? guide.NavMeshSample.ChunkY : bake.MacroChunkRows / 2;
            Vector2 start = ResolveWorldPointInChunk(bake, navChunkX, navChunkY, 1_000f, 1_000f);
            Vector2 goal = ResolveWorldPointInChunk(bake, navChunkX, navChunkY, 4_800f, 4_400f);
            NavQueryServiceRegistry navRegistry = engine.GetService(CoreServiceKeys.NavQueryServices)
                ?? throw new InvalidOperationException("NavQueryServiceRegistry missing.");
            NavMeshProfileRegistry navProfiles = engine.GetService(CoreServiceKeys.NavMeshProfiles)
                ?? throw new InvalidOperationException("NavMeshProfileRegistry missing.");
            Assert.That(navProfiles.TryGetIndex(guide.NavMeshSample.ProfileId, out int profileIndex), Is.True);
            Assert.That(navRegistry.TryGetStore(guide.NavMeshSample.Layer, profileIndex, out NavTileStore store), Is.True);
            NavTileId editedTileId = new(navChunkX, navChunkY, guide.NavMeshSample.Layer);
            NavTile beforeTile = store.GetOrLoad(editedTileId);
            NavTile rightWindowTile = store.GetOrLoad(new NavTileId(
                guide.NavMeshCoverage.ActiveWindowMaxChunkX,
                guide.NavMeshCoverage.ActiveWindowMinChunkY,
                guide.NavMeshSample.Layer));
            Vector2 obstacleA = ResolveWorldPointInNavTile(bake, beforeTile, -800f, -800f);
            Vector2 obstacleB = ResolveWorldPointInNavTile(bake, beforeTile, 7_200f, -600f);
            Vector2 obstacleC = ResolveWorldPointInNavTile(bake, beforeTile, 3_200f, 7_400f);

            engine.SetService(CoreServiceKeys.AuthoritativeInput, input);
            engine.SetService(CoreServiceKeys.AuthoritativePointerButtons, pointerButtons);
            engine.SetService(CoreServiceKeys.InteractionActionBindings, bindings);
            engine.SetService(CoreServiceKeys.PathStore, pathStore);
            engine.SetService(CoreServiceKeys.PathService, pathService);

            Assert.That(guide.CurrentStepId, Is.EqualTo(MassNavigationShowcaseStepId.BakeToolQuery));
            Assert.That(guide.PrimaryActionLabel, Is.EqualTo("Update NavData"));
            Assert.That(guide.OperationMode, Is.EqualTo("Runtime NavData tool"));
            Assert.That(guide.ActiveWindowNavMeshEdges.Length, Is.GreaterThan(0));
            Assert.That(guide.ActiveWindowNavMeshEdges.Length, Is.GreaterThan(216),
                "U16 must render a real active-window NavMesh wire, not the old 12-tile x 6-triangle sparse sample.");
            AssertNavMeshCoverageMatchesBakeDiagnostics(simulation, guide);
            AssertNavMeshSampleEdgesUseWorldCoordinates(bake, guide, beforeTile);
            Assert.That(rightWindowTile.TriangleCount, Is.GreaterThan(0),
                "The U16 fixture must include a non-empty right-side world tile so the showcase proves full-world navmesh coverage, not only the first tile.");
            AssertActiveWindowEdgesIncludeNavTile(bake, guide, rightWindowTile, "before runtime bake");

            guide.ArmPathDrivenOperation(MassNavigationShowcaseStepId.BakeToolQuery);
            SetPointerPick(input, pointerButtons, bindings.ConfirmActionId, start);
            pathPreview.Update(1f / 60f);
            ClearPointerPick(input, pointerButtons, bindings.ConfirmActionId);
            engine.SetService(CoreServiceKeys.PointerInputCaptured, false);
            SetPointerPick(input, pointerButtons, bindings.CommandActionId, goal);
            pathPreview.Update(1f / 60f);

            Assert.That(pathService.Requests, Has.Count.EqualTo(1));
            Assert.That(simulation.AcceptanceDiagnostics.PathOnlyQuery.Available, Is.True);
            Assert.That(simulation.AcceptanceDiagnostics.PathOnlyQuery.NoOrderSubmitted, Is.True);
            int requestCountBeforeAuthoring = pathService.Requests.Count;

            guide.ArmRuntimeObstacleAuthoring();
            simulation.AcceptanceDiagnostics.RecordRuntimeNavDataUpdate(guide.RuntimeBakeAuthoring.CreateSnapshot());
            SetPointerPick(input, pointerButtons, bindings.ConfirmActionId, obstacleA);
            runtimeAuthoring.Update(1f / 60f);
            pathPreview.Update(1f / 60f);
            ClearPointerPick(input, pointerButtons, bindings.ConfirmActionId);
            engine.SetService(CoreServiceKeys.PointerInputCaptured, false);
            SetPointerPick(input, pointerButtons, bindings.ConfirmActionId, obstacleB);
            runtimeAuthoring.Update(1f / 60f);
            pathPreview.Update(1f / 60f);
            ClearPointerPick(input, pointerButtons, bindings.ConfirmActionId);
            engine.SetService(CoreServiceKeys.PointerInputCaptured, false);
            SetPointerPick(input, pointerButtons, bindings.CommandActionId, obstacleC);
            runtimeAuthoring.Update(1f / 60f);
            pathPreview.Update(1f / 60f);

            Assert.That(guide.RuntimeBakeAuthoring.AuthoredPolygonCount, Is.EqualTo(1));
            Assert.That(guide.RuntimeBakeAuthoring.DraftPointCount, Is.EqualTo(0));
            Assert.That(guide.RuntimeBakeAuthoring.DirtyChunkCount, Is.GreaterThan(0));
            AssertRuntimeDirtyTilesUseNavTileWorldBounds(bake, guide.RuntimeBakeAuthoring.DirtyChunks, beforeTile);
            Assert.That(pathService.Requests, Has.Count.EqualTo(requestCountBeforeAuthoring),
                "Obstacle authoring must pause route picking instead of submitting extra path preview requests.");

            int activeWindowEdgeCountBeforeBake = guide.ActiveWindowNavMeshEdges.Length;

            MassNavigationRuntimeNavDataUpdateDiagnostics diagnostics = guide.RuntimeBakeAuthoring.RequestRuntimeNavDataUpdate(
                simulation,
                engine.GetService(CoreServiceKeys.NavMeshBakeConfig),
                navRegistry,
                navProfiles,
                pathService,
                pathStore);
            guide.RecordRuntimeNavDataUpdateResult(diagnostics);
            NavTile afterTile = store.GetOrLoad(editedTileId);

            Assert.That(diagnostics.Available, Is.True);
            Assert.That(diagnostics.AuthoredPolygonCount, Is.EqualTo(1));
            Assert.That(diagnostics.DirtyChunkCount, Is.GreaterThan(0));
            Assert.That(diagnostics.BakedTileCount, Is.GreaterThan(0));
            Assert.That(diagnostics.ChangedTileCount, Is.GreaterThan(0));
            Assert.That(diagnostics.BeforeTriangleCount, Is.GreaterThan(0));
            Assert.That(diagnostics.AfterTriangleCount, Is.GreaterThanOrEqualTo(0));
            Assert.That(diagnostics.BeforeChecksumXor, Is.Not.EqualTo(0UL));
            Assert.That(diagnostics.AfterChecksumXor, Is.Not.EqualTo(0UL));
            Assert.That(diagnostics.BeforeGeometryHashXor, Is.Not.EqualTo(diagnostics.AfterGeometryHashXor));
            Assert.That(afterTile.TileVersion, Is.GreaterThan(beforeTile.TileVersion));
            Assert.That(afterTile.Checksum, Is.Not.EqualTo(beforeTile.Checksum));
            Assert.That(afterTile.TriangleCount, Is.LessThan(beforeTile.TriangleCount));
            Assert.That(guide.NavMeshSample.TriangleCount, Is.EqualTo(afterTile.TriangleCount));
            Assert.That(guide.ActiveWindowNavMeshEdges.Length, Is.GreaterThan(0));
            Assert.That(guide.ActiveWindowNavMeshEdges.Length, Is.Not.EqualTo(activeWindowEdgeCountBeforeBake));
            AssertActiveWindowEdgesIncludeNavTile(bake, guide, afterTile, "after runtime bake");
            Assert.That(diagnostics.NavDataRevision, Is.EqualTo(1));
            Assert.That(diagnostics.QueryStatusAfterUpdate, Is.EqualTo("Ok"));
            Assert.That(diagnostics.QueryPathPointCount, Is.EqualTo(3));
            Assert.That(diagnostics.FlowObstacleRefreshQueued, Is.True);
            Assert.That(diagnostics.ProductionGap, Is.EqualTo("none_runtime_recast_incremental_bake_bound"));
            Assert.That(pathService.Requests, Has.Count.EqualTo(requestCountBeforeAuthoring + 1));
            Assert.That(simulation.CommandCountFrame + simulation.PendingCommandCount, Is.EqualTo(0));
            AssertRuntimeDirtyTilesUseNavTileWorldBounds(bake, guide.RuntimeBakeAuthoring.DirtyChunks, afterTile);

            presentation.Update(0.016f);
            string[] strings = GetOverlayStrings(screen);
            Assert.That(strings.Any(text => text.Contains("runtime obstacle polygons=1", StringComparison.Ordinal) ||
                text.Contains("Runtime bake update", StringComparison.Ordinal)), Is.True, DumpStrings(strings));
            Assert.That(strings.Any(text => text.Contains("dirtyChunks=", StringComparison.Ordinal) ||
                text.Contains("dirty chunks=", StringComparison.Ordinal)), Is.True, DumpStrings(strings));
            Assert.That(strings.Any(text => text.Contains("baked=", StringComparison.Ordinal) &&
                text.Contains("changed=", StringComparison.Ordinal)), Is.True, DumpStrings(strings));
            string expectedCoverageText = $"{guide.NavMeshCoverage.TargetChunkCount}/{guide.NavMeshCoverage.WorldChunkCount}";
            Assert.That(strings.Any(text => text.Contains(expectedCoverageText, StringComparison.Ordinal) &&
                text.Contains("full-world", StringComparison.Ordinal)), Is.True, DumpStrings(strings));
            Assert.That(strings.Any(text => text.Contains("source=", StringComparison.Ordinal) &&
                text.Contains("RecastNavTileBaker", StringComparison.Ordinal)), Is.True, DumpStrings(strings));
            Assert.That(ground.GetSpan().ToArray().Count(item => item.Shape == GroundOverlayShape.Line), Is.GreaterThan(0));
            Assert.That(ground.GetSpan().ToArray().Count(item => item.Shape == GroundOverlayShape.Circle), Is.GreaterThan(0));
        }

        [Test]
        public void RuntimeBakeFocusedShowcase_DirectUpdateUsesRuntimeWorldEndpointsWithoutWorldPathClick()
        {
            using GameEngine engine = CreateMassNavigationFocusedEntryEngineWithUi("MassNavigationU16BakeToolQueryShowcaseMod");
            engine.LoadMap("mass_navigation");
            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            MassNavigationShowcaseGuideRuntime guide = RequireGuide(engine);
            MassNavigationBakeDataDiagnostics bake = simulation.BakeDataDiagnostics
                ?? throw new InvalidOperationException("Mass navigation bake diagnostics missing.");

            var input = new FrozenInputActionReader();
            var pointerButtons = new AuthoritativePointerButtonSnapshot();
            var bindings = new InteractionActionBindings();
            var runtimeAuthoring = new MassNavigationRuntimeBakeAuthoringInputSystem(engine, simulation, guide);
            NavQueryServiceRegistry navRegistry = engine.GetService(CoreServiceKeys.NavQueryServices)
                ?? throw new InvalidOperationException("NavQueryServiceRegistry missing.");
            NavMeshProfileRegistry navProfiles = engine.GetService(CoreServiceKeys.NavMeshProfiles)
                ?? throw new InvalidOperationException("NavMeshProfileRegistry missing.");
            IPathService pathService = engine.GetService(CoreServiceKeys.PathService)
                ?? throw new InvalidOperationException("PathService missing.");
            PathStore pathStore = engine.GetService(CoreServiceKeys.PathStore)
                ?? throw new InvalidOperationException("PathStore missing.");

            Assert.That(pathService, Is.TypeOf<PathServiceRouter>());
            Assert.That(simulation.AcceptanceDiagnostics.HasReusablePathQueryEndpoints, Is.False,
                "Startup diagnostics may run a smoke query, but direct Update NavData must not reuse it as a user/World Path endpoint.");

            int navChunkX = guide.NavMeshSample.Available ? guide.NavMeshSample.ChunkX : bake.MacroChunkColumns / 2;
            int navChunkY = guide.NavMeshSample.Available ? guide.NavMeshSample.ChunkY : bake.MacroChunkRows / 2;
            Assert.That(navProfiles.TryGetIndex(guide.NavMeshSample.ProfileId, out int profileIndex), Is.True);
            Assert.That(navRegistry.TryGetStore(guide.NavMeshSample.Layer, profileIndex, out NavTileStore store), Is.True);
            NavTile editedTile = store.GetOrLoad(new NavTileId(navChunkX, navChunkY, guide.NavMeshSample.Layer));
            Vector2 obstacleA = ResolveWorldPointInNavTile(bake, editedTile, -800f, -800f);
            Vector2 obstacleB = ResolveWorldPointInNavTile(bake, editedTile, 7_200f, -600f);
            Vector2 obstacleC = ResolveWorldPointInNavTile(bake, editedTile, 3_200f, 7_400f);

            engine.SetService(CoreServiceKeys.AuthoritativeInput, input);
            engine.SetService(CoreServiceKeys.AuthoritativePointerButtons, pointerButtons);
            engine.SetService(CoreServiceKeys.InteractionActionBindings, bindings);

            Assert.That(guide.CurrentStepId, Is.EqualTo(MassNavigationShowcaseStepId.BakeToolQuery));
            Assert.That(guide.PrimaryActionLabel, Is.EqualTo("Update NavData"));
            guide.ArmRuntimeObstacleAuthoring();
            simulation.AcceptanceDiagnostics.RecordRuntimeNavDataUpdate(guide.RuntimeBakeAuthoring.CreateSnapshot());
            SetPointerPick(input, pointerButtons, bindings.ConfirmActionId, obstacleA);
            runtimeAuthoring.Update(1f / 60f);
            ClearPointerPick(input, pointerButtons, bindings.ConfirmActionId);
            engine.SetService(CoreServiceKeys.PointerInputCaptured, false);
            SetPointerPick(input, pointerButtons, bindings.ConfirmActionId, obstacleB);
            runtimeAuthoring.Update(1f / 60f);
            ClearPointerPick(input, pointerButtons, bindings.ConfirmActionId);
            engine.SetService(CoreServiceKeys.PointerInputCaptured, false);
            SetPointerPick(input, pointerButtons, bindings.CommandActionId, obstacleC);
            runtimeAuthoring.Update(1f / 60f);

            Assert.That(guide.RuntimeBakeAuthoring.AuthoredPolygonCount, Is.EqualTo(1));

            var router = (PathServiceRouter)pathService;
            PathQueryCacheDiagnostics before = router.CacheDiagnostics;
            MassNavigationRuntimeNavDataUpdateDiagnostics diagnostics = guide.RuntimeBakeAuthoring.RequestRuntimeNavDataUpdate(
                simulation,
                engine.GetService(CoreServiceKeys.NavMeshBakeConfig),
                navRegistry,
                navProfiles,
                pathService,
                pathStore);
            guide.RecordRuntimeNavDataUpdateResult(diagnostics);
            MassNavigationPathOnlyQueryDiagnostics query = simulation.AcceptanceDiagnostics.PathOnlyQuery;

            Assert.That(diagnostics.Available, Is.True);
            Assert.That(diagnostics.BakedTileCount, Is.GreaterThan(0));
            Assert.That(diagnostics.ChangedTileCount, Is.GreaterThan(0));
            Assert.That(diagnostics.QueryStatusAfterUpdate, Is.EqualTo("Ok"), DescribePathOnlyQuery(query));
            Assert.That(diagnostics.QueryPathPointCount, Is.GreaterThanOrEqualTo(2));
            Assert.That(simulation.AcceptanceDiagnostics.HasReusablePathQueryEndpoints, Is.False,
                "Runtime bake auto-query must not promote resolver endpoints into a reusable user/World Path seed.");
            Assert.That(query.Available, Is.True, DescribePathOnlyQuery(query));
            Assert.That(query.StartMacroChunkX, Is.GreaterThanOrEqualTo(0));
            Assert.That(query.StartMacroChunkY, Is.GreaterThanOrEqualTo(0));
            Assert.That(query.GoalMacroChunkX, Is.LessThan(bake.MacroChunkColumns));
            Assert.That(query.GoalMacroChunkY, Is.LessThan(bake.MacroChunkRows));
            Assert.That(MathF.Max(
                    MathF.Abs(query.GoalWorldCm.X - query.StartWorldCm.X),
                    MathF.Abs(query.GoalWorldCm.Y - query.StartWorldCm.Y)),
                Is.GreaterThan(bake.WorldWidthCm * 0.5f),
                "Direct Update NavData must resolve runtime-world endpoints from the live NavMesh component resolver; baked-scale coordinates collapse into the left/top world corner.");
            Assert.That(query.MacroRouteChunkCount, Is.GreaterThan(400));
            Assert.That(router.CacheDiagnostics.Misses, Is.GreaterThanOrEqualTo(before.Misses));

            MassNavigationRuntimeNavDataUpdateDiagnostics secondDiagnostics = guide.RuntimeBakeAuthoring.RequestRuntimeNavDataUpdate(
                simulation,
                engine.GetService(CoreServiceKeys.NavMeshBakeConfig),
                navRegistry,
                navProfiles,
                pathService,
                pathStore);
            MassNavigationPathOnlyQueryDiagnostics secondQuery = simulation.AcceptanceDiagnostics.PathOnlyQuery;

            Assert.That(secondDiagnostics.Available, Is.True);
            Assert.That(secondDiagnostics.QueryStatusAfterUpdate, Is.EqualTo("Ok"), DescribePathOnlyQuery(secondQuery));
            Assert.That(simulation.AcceptanceDiagnostics.HasReusablePathQueryEndpoints, Is.False,
                "A second direct Update NavData still must not reuse the previous runtime bake auto-query as an explicit endpoint seed.");
            Assert.That(secondQuery.MacroRouteChunkCount, Is.GreaterThan(400));
            Assert.That(MathF.Max(
                    MathF.Abs(secondQuery.GoalWorldCm.X - secondQuery.StartWorldCm.X),
                    MathF.Abs(secondQuery.GoalWorldCm.Y - secondQuery.StartWorldCm.Y)),
                Is.GreaterThan(bake.WorldWidthCm * 0.5f));
        }

        [Test]
        public void RuntimeBakeFocusedShowcase_RuntimeObstacleInvalidatesCacheAndRequeriesPathAroundPolygon()
        {
            using GameEngine engine = CreateMassNavigationFocusedEntryEngineWithUi("MassNavigationU16BakeToolQueryShowcaseMod");
            engine.LoadMap("mass_navigation");
            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            MassNavigationShowcaseGuideRuntime guide = RequireGuide(engine);
            MassNavigationBakeDataDiagnostics bake = simulation.BakeDataDiagnostics
                ?? throw new InvalidOperationException("Mass navigation bake diagnostics missing.");

            var input = new FrozenInputActionReader();
            var pointerButtons = new AuthoritativePointerButtonSnapshot();
            var bindings = new InteractionActionBindings();
            var runtimeAuthoring = new MassNavigationRuntimeBakeAuthoringInputSystem(engine, simulation, guide);
            NavQueryServiceRegistry navRegistry = engine.GetService(CoreServiceKeys.NavQueryServices)
                ?? throw new InvalidOperationException("NavQueryServiceRegistry missing.");
            NavMeshProfileRegistry navProfiles = engine.GetService(CoreServiceKeys.NavMeshProfiles)
                ?? throw new InvalidOperationException("NavMeshProfileRegistry missing.");
            IPathService pathService = engine.GetService(CoreServiceKeys.PathService)
                ?? throw new InvalidOperationException("PathService missing.");
            PathStore pathStore = engine.GetService(CoreServiceKeys.PathStore)
                ?? throw new InvalidOperationException("PathStore missing.");

            Assert.That(pathService, Is.TypeOf<PathServiceRouter>());
            var router = (PathServiceRouter)pathService;
            Assert.That(navProfiles.TryGetIndex(guide.NavMeshSample.ProfileId, out int profileIndex), Is.True);
            Assert.That(navRegistry.TryGetStore(guide.NavMeshSample.Layer, profileIndex, out NavTileStore store), Is.True);
            RuntimeObstaclePathFixture fixture = ResolveRuntimeObstaclePathFixture(
                bake,
                guide,
                store,
                guide.NavMeshSample.Layer);
            NavTile editedTile = fixture.Tile;
            int revisionBefore = store.Revision;
            Vector2 start = fixture.StartWorldCm;
            Vector2 goal = fixture.GoalWorldCm;
            Vector2[] authoredPolygon = fixture.PolygonWorldCm;

            simulation.AcceptanceDiagnostics.RecordPathOnlyPreviewQuery(
                pathService,
                pathStore,
                start,
                goal,
                PathDomain.NavMesh);
            MassNavigationPathOnlyQueryDiagnostics beforeQuery = simulation.AcceptanceDiagnostics.PathOnlyQuery;
            MassNavigationPathPointSample[] beforePath = simulation.AcceptanceDiagnostics.PathOnlyPathPoints.ToArray();
            Assert.That(beforeQuery.Available, Is.True, DescribePathOnlyQuery(beforeQuery));
            Assert.That(beforeQuery.Status, Is.EqualTo("Ok"), DescribePathOnlyQuery(beforeQuery));
            Assert.That(beforePath.Length, Is.EqualTo(beforeQuery.PathPointCount));
            AssertPathEntersPolygonInterior(beforePath, authoredPolygon, "before runtime obstacle bake");

            PathQueryCacheDiagnostics afterFirstQuery = router.CacheDiagnostics;
            simulation.AcceptanceDiagnostics.RecordPathOnlyPreviewQuery(
                pathService,
                pathStore,
                start,
                goal,
                PathDomain.NavMesh);
            PathQueryCacheDiagnostics afterWarmQuery = router.CacheDiagnostics;
            Assert.That(afterWarmQuery.Hits, Is.GreaterThan(afterFirstQuery.Hits),
                "Repeated same start/goal must hit the path cache before NavData changes.");
            Assert.That(afterWarmQuery.Misses, Is.EqualTo(afterFirstQuery.Misses));

            engine.SetService(CoreServiceKeys.AuthoritativeInput, input);
            engine.SetService(CoreServiceKeys.AuthoritativePointerButtons, pointerButtons);
            engine.SetService(CoreServiceKeys.InteractionActionBindings, bindings);
            guide.ArmRuntimeObstacleAuthoring();
            simulation.AcceptanceDiagnostics.RecordRuntimeNavDataUpdate(guide.RuntimeBakeAuthoring.CreateSnapshot());
            SetPointerPick(input, pointerButtons, bindings.ConfirmActionId, authoredPolygon[0]);
            runtimeAuthoring.Update(1f / 60f);
            ClearPointerPick(input, pointerButtons, bindings.ConfirmActionId);
            engine.SetService(CoreServiceKeys.PointerInputCaptured, false);
            SetPointerPick(input, pointerButtons, bindings.ConfirmActionId, authoredPolygon[1]);
            runtimeAuthoring.Update(1f / 60f);
            ClearPointerPick(input, pointerButtons, bindings.ConfirmActionId);
            engine.SetService(CoreServiceKeys.PointerInputCaptured, false);
            SetPointerPick(input, pointerButtons, bindings.ConfirmActionId, authoredPolygon[2]);
            runtimeAuthoring.Update(1f / 60f);
            ClearPointerPick(input, pointerButtons, bindings.ConfirmActionId);
            engine.SetService(CoreServiceKeys.PointerInputCaptured, false);
            SetPointerPick(input, pointerButtons, bindings.CommandActionId, authoredPolygon[3]);
            runtimeAuthoring.Update(1f / 60f);

            Assert.That(guide.RuntimeBakeAuthoring.AuthoredPolygonCount, Is.EqualTo(1));
            Assert.That(guide.RuntimeBakeAuthoring.DirtyChunkCount, Is.GreaterThan(0));

            MassNavigationRuntimeNavDataUpdateDiagnostics diagnostics = guide.RuntimeBakeAuthoring.RequestRuntimeNavDataUpdate(
                simulation,
                engine.GetService(CoreServiceKeys.NavMeshBakeConfig),
                navRegistry,
                navProfiles,
                pathService,
                pathStore);
            guide.RecordRuntimeNavDataUpdateResult(diagnostics);
            MassNavigationPathOnlyQueryDiagnostics afterQuery = simulation.AcceptanceDiagnostics.PathOnlyQuery;
            MassNavigationPathPointSample[] afterPath = simulation.AcceptanceDiagnostics.PathOnlyPathPoints.ToArray();
            PathQueryCacheDiagnostics afterRuntimeBakeQuery = router.CacheDiagnostics;

            Assert.That(diagnostics.Available, Is.True);
            Assert.That(diagnostics.BakedTileCount, Is.GreaterThan(0));
            Assert.That(diagnostics.ChangedTileCount, Is.GreaterThan(0));
            Assert.That(store.GetOrLoad(editedTile.TileId).TriangleCount, Is.GreaterThan(0),
                $"Runtime bake produced an empty tile for fixture {fixture.Source}.");
            Assert.That(diagnostics.QueryStatusAfterUpdate, Is.EqualTo("Ok"), DescribePathOnlyQuery(afterQuery));
            Assert.That(afterQuery.StartWorldCm, Is.EqualTo(start));
            Assert.That(afterQuery.GoalWorldCm, Is.EqualTo(goal));
            Assert.That(afterQuery.PathPointCount, Is.GreaterThanOrEqualTo(2), DescribePathOnlyQuery(afterQuery));
            Assert.That(afterPath.Length, Is.EqualTo(afterQuery.PathPointCount));
            AssertPathDoesNotEnterPolygonInterior(afterPath, authoredPolygon, "after runtime obstacle bake");
            Assert.That(store.Revision, Is.GreaterThan(revisionBefore));
            Assert.That(afterRuntimeBakeQuery.Misses, Is.GreaterThan(afterWarmQuery.Misses),
                "Runtime NavData update must invalidate warm cached paths and force a fresh NavMesh solve under the new data revision.");
        }

        [Test]
        public void RuntimeBakeFocusedShowcase_LiveNavMeshResolvesFullWorldComponentPath()
        {
            using GameEngine engine = CreateMassNavigationFocusedEntryEngineWithUi("MassNavigationU16BakeToolQueryShowcaseMod");
            engine.LoadMap("mass_navigation");
            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            MassNavigationShowcaseGuideRuntime guide = RequireGuide(engine);
            MassNavigationBakeDataDiagnostics bake = simulation.BakeDataDiagnostics
                ?? throw new InvalidOperationException("Mass navigation bake diagnostics missing.");
            NavQueryServiceRegistry navRegistry = engine.GetService(CoreServiceKeys.NavQueryServices)
                ?? throw new InvalidOperationException("NavQueryServiceRegistry missing.");
            NavMeshProfileRegistry navProfiles = engine.GetService(CoreServiceKeys.NavMeshProfiles)
                ?? throw new InvalidOperationException("NavMeshProfileRegistry missing.");
            string probeSummary = BuildLiveNavMeshProbeSummary(bake, navRegistry, navProfiles);
            Console.WriteLine(probeSummary);

            Assert.That(guide.TryResolveRuntimeBakeWorldPathEndpoints(
                    bake,
                    navRegistry,
                    navProfiles,
                    out MassNavigationRuntimeWorldPathEndpointResult endpoints),
                Is.True,
                "The U16 runtime bake showcase must resolve a live full-world NavMesh component endpoint pair; otherwise the UI keeps showing the old local smoke route.\n" + probeSummary);

            Assert.That(Math.Max(
                    Math.Abs(endpoints.GoalChunkX - endpoints.StartChunkX),
                    Math.Abs(endpoints.GoalChunkY - endpoints.StartChunkY)),
                Is.GreaterThanOrEqualTo(Math.Min(bake.MacroChunkColumns, bake.MacroChunkRows) - 24));
            Assert.That(endpoints.MacroRouteChunkCount, Is.GreaterThan(400), endpoints.Source);

            Assert.That(navProfiles.TryGetIndex(endpoints.Source.Contains("profile=GroundLight", StringComparison.Ordinal) ? "GroundLight" : guide.NavMeshSample.ProfileId, out int profileIndex), Is.True);
            Assert.That(navRegistry.TryGetStore(guide.NavMeshSample.Layer, profileIndex, out NavTileStore store), Is.True);
            var query = new NavQueryService(store, guide.NavMeshSample.Layer, NavAreaCostTable.CreateDefault());
            NavPathResult path = query.TryFindPath(
                (int)MathF.Round(endpoints.StartWorldCm.X),
                (int)MathF.Round(endpoints.StartWorldCm.Y),
                (int)MathF.Round(endpoints.GoalWorldCm.X),
                (int)MathF.Round(endpoints.GoalWorldCm.Y),
                maxPortals: 262_144);

            Assert.That(path.Status, Is.EqualTo(NavPathStatus.Ok), endpoints.Source);
            Assert.That(path.PathXcm.Length, Is.GreaterThanOrEqualTo(2));

            NavTile cachedStartTile = store.GetOrLoad(new NavTileId(endpoints.StartChunkX, endpoints.StartChunkY, guide.NavMeshSample.Layer));
            store.Replace(cachedStartTile);
            Assert.That(guide.TryResolveRuntimeBakeWorldPathEndpoints(
                    bake,
                    navRegistry,
                    navProfiles,
                    out MassNavigationRuntimeWorldPathEndpointResult revalidated),
                Is.True);
            Assert.That(revalidated.StartWorldCm, Is.EqualTo(endpoints.StartWorldCm));
            Assert.That(revalidated.GoalWorldCm, Is.EqualTo(endpoints.GoalWorldCm));
            Assert.That(revalidated.Source, Does.Contain("runtime_navmesh_cached_endpoint_revalidated"),
                "A NavData revision change with unchanged geometry should revalidate cached full-world endpoints instead of rebuilding the full portal component graph.");
        }

        [Test]
        public void RuntimeBakeFocusedShowcase_MeshViewCameraStaysInsideZoomProfileBounds()
        {
            const string meshViewCameraId = "Camera.Profile.MassNavigationMeshView";
            using GameEngine engine = CreateMassNavigationFocusedEntryEngineWithUi("MassNavigationU16BakeToolQueryShowcaseMod");
            engine.LoadMap("mass_navigation");
            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            MassNavigationShowcaseGuideRuntime guide = RequireGuide(engine);
            var controller = new MassNavigationPanelController();

            VirtualCameraRegistry registry = engine.GetService(CoreServiceKeys.VirtualCameraRegistry)
                ?? throw new InvalidOperationException("VirtualCameraRegistry missing.");
            VirtualCameraDefinition meshView = registry.Get(meshViewCameraId);
            Assert.That(meshView.MinDistanceCm, Is.EqualTo(8_000f));
            Assert.That(meshView.MaxDistanceCm, Is.EqualTo(68_000f));
            Assert.That(guide.CurrentStepId, Is.EqualTo(MassNavigationShowcaseStepId.BakeToolQuery));
            Assert.That(engine.GlobalContext.TryGetValue(CoreServiceKeys.VirtualCameraRequest.Name, out object? initialRequestObj), Is.True);
            Assert.That(((VirtualCameraRequest)initialRequestObj!).Id, Is.EqualTo(meshViewCameraId));
            CameraPoseRequest initialPose = engine.GetService(CoreServiceKeys.CameraPoseRequest)
                ?? throw new InvalidOperationException("Initial CameraPoseRequest missing.");
            Assert.That(initialPose.DistanceCm.HasValue, Is.True);
            Assert.That(initialPose.DistanceCm!.Value, Is.InRange(meshView.MinDistanceCm, meshView.MaxDistanceCm));

            Assert.That(controller.MountOrSync(engine, simulation), Is.True);
            UIRoot root = (UIRoot)engine.GetService(CoreServiceKeys.UIRoot)!;
            string text = ExtractUiSceneText(root);
            Assert.That(text, Does.Contain("Mesh View"));

            engine.GlobalContext.Remove(CoreServiceKeys.VirtualCameraRequest.Name);
            engine.GlobalContext.Remove(CoreServiceKeys.CameraPoseRequest.Name);
            InvokeUiButton(root, "Mesh View");

            Assert.That(engine.GlobalContext.TryGetValue(CoreServiceKeys.VirtualCameraRequest.Name, out object? requestObj), Is.True);
            var request = (VirtualCameraRequest)requestObj!;
            CameraPoseRequest pose = engine.GetService(CoreServiceKeys.CameraPoseRequest)
                ?? throw new InvalidOperationException("CameraPoseRequest missing.");
            Assert.That(request.Id, Is.EqualTo(meshViewCameraId));
            Assert.That(pose.VirtualCameraId, Is.EqualTo(meshViewCameraId));
            Assert.That(pose.DistanceCm.HasValue, Is.True);
            Assert.That(pose.DistanceCm!.Value, Is.InRange(meshView.MinDistanceCm, meshView.MaxDistanceCm));
        }

        [Test]
        public void RuntimeBakeFocusedShowcase_WorldPathButtonSetsFullWorldEndpointsAndUsesPathCache()
        {
            using GameEngine engine = CreateMassNavigationFocusedEntryEngineWithUi("MassNavigationU16BakeToolQueryShowcaseMod");
            engine.LoadMap("mass_navigation");
            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            MassNavigationBakeDataDiagnostics bake = simulation.BakeDataDiagnostics
                ?? throw new InvalidOperationException("Mass navigation bake diagnostics missing.");
            var controller = new MassNavigationPanelController();

            Assert.That(controller.MountOrSync(engine, simulation), Is.True);
            UIRoot root = (UIRoot)engine.GetService(CoreServiceKeys.UIRoot)!;
            string text = ExtractUiSceneText(root);
            Assert.That(text, Does.Contain("World Path"));

            IPathService pathService = engine.GetService(CoreServiceKeys.PathService)
                ?? throw new InvalidOperationException("PathService missing.");
            Assert.That(pathService, Is.TypeOf<PathServiceRouter>());
            var router = (PathServiceRouter)pathService;
            PathQueryCacheDiagnostics before = router.CacheDiagnostics;

            InvokeUiButton(root, "World Path");
            MassNavigationPathOnlyQueryDiagnostics first = simulation.AcceptanceDiagnostics.PathOnlyQuery;
            Assert.That(first.Available, Is.True, DescribePathOnlyQuery(first));
            Assert.That(first.Status, Is.EqualTo("Ok"));
            Assert.That(first.NoOrderSubmitted, Is.True);
            Assert.That(first.PathPointCount, Is.GreaterThanOrEqualTo(2));
            Assert.That(first.StartMacroChunkX, Is.GreaterThanOrEqualTo(0));
            Assert.That(first.StartMacroChunkY, Is.GreaterThanOrEqualTo(0));
            Assert.That(first.GoalMacroChunkX, Is.LessThan(bake.MacroChunkColumns));
            Assert.That(first.GoalMacroChunkY, Is.LessThan(bake.MacroChunkRows));
            int routeDeltaX = Math.Abs(first.GoalMacroChunkX - first.StartMacroChunkX);
            int routeDeltaY = Math.Abs(first.GoalMacroChunkY - first.StartMacroChunkY);
            Assert.That(Math.Max(routeDeltaX, routeDeltaY), Is.GreaterThanOrEqualTo(Math.Min(bake.MacroChunkColumns, bake.MacroChunkRows) - 24));
            Assert.That(first.MacroRouteChunkCount, Is.GreaterThan(400),
                "U16 World Path must exercise the largest live NavMesh component route scale, not a tiny active-window smoke path.");
            Assert.That(simulation.AcceptanceDiagnostics.PathOnlyPathPoints.Length, Is.EqualTo(first.PathPointCount));
            PathQueryCacheDiagnostics afterFirst = router.CacheDiagnostics;
            Assert.That(afterFirst.Misses, Is.GreaterThan(before.Misses));

            _ = controller.MountOrSync(engine, simulation);
            InvokeUiButton(root, "World Path");
            MassNavigationPathOnlyQueryDiagnostics second = simulation.AcceptanceDiagnostics.PathOnlyQuery;
            PathQueryCacheDiagnostics afterSecond = router.CacheDiagnostics;
            Assert.That(second.Available, Is.True, DescribePathOnlyQuery(second));
            Assert.That(second.StartWorldCm, Is.EqualTo(first.StartWorldCm));
            Assert.That(second.GoalWorldCm, Is.EqualTo(first.GoalWorldCm));
            Assert.That(second.PathPointCount, Is.EqualTo(first.PathPointCount));
            Assert.That(afterSecond.Hits, Is.GreaterThan(afterFirst.Hits));
            Assert.That(afterSecond.Misses, Is.EqualTo(afterFirst.Misses));
        }

        [Test]
        public void Benchmark_RuntimeBakeFocusedShowcase_FullWorldRepeatedPathCacheAndConcurrentQueries()
        {
            const int repeatedQueries = 128;
            const int concurrentQueries = 32;
            using GameEngine engine = CreateMassNavigationFocusedEntryEngineWithUi("MassNavigationU16BakeToolQueryShowcaseMod");
            engine.LoadMap("mass_navigation");
            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            MassNavigationShowcaseGuideRuntime guide = RequireGuide(engine);
            MassNavigationBakeDataDiagnostics bake = simulation.BakeDataDiagnostics
                ?? throw new InvalidOperationException("Mass navigation bake diagnostics missing.");
            NavQueryServiceRegistry navRegistry = engine.GetService(CoreServiceKeys.NavQueryServices)
                ?? throw new InvalidOperationException("NavQueryServiceRegistry missing.");
            NavMeshProfileRegistry navProfiles = engine.GetService(CoreServiceKeys.NavMeshProfiles)
                ?? throw new InvalidOperationException("NavMeshProfileRegistry missing.");
            IPathService pathService = engine.GetService(CoreServiceKeys.PathService)
                ?? throw new InvalidOperationException("PathService missing.");
            PathStore pathStore = engine.GetService(CoreServiceKeys.PathStore)
                ?? throw new InvalidOperationException("PathStore missing.");

            Assert.That(pathService, Is.TypeOf<PathServiceRouter>());
            var router = (PathServiceRouter)pathService;
            Assert.That(guide.TryResolveRuntimeBakeWorldPathEndpoints(
                    bake,
                    navRegistry,
                    navProfiles,
                    out MassNavigationRuntimeWorldPathEndpointResult endpoints),
                Is.True);
            Assert.That(endpoints.MacroRouteChunkCount, Is.GreaterThan(400), endpoints.Source);

            var request = new PathRequest(
                requestId: 1001,
                actor: default,
                domain: PathDomain.NavMesh,
                agentTypeId: "Infantry",
                start: PathEndpoint.FromWorldCm(
                    (int)MathF.Round(endpoints.StartWorldCm.X),
                    (int)MathF.Round(endpoints.StartWorldCm.Y)),
                goal: PathEndpoint.FromWorldCm(
                    (int)MathF.Round(endpoints.GoalWorldCm.X),
                    (int)MathF.Round(endpoints.GoalWorldCm.Y)),
                budget: new PathBudget(maxExpanded: 262_144, maxPoints: pathStore.MaxPointsPerPath));

            router.ClearCache();
            PathQueryCacheDiagnostics before = router.CacheDiagnostics;
            int warmPoints = SolveAndRelease(router, pathStore, request);
            PathQueryCacheDiagnostics afterWarm = router.CacheDiagnostics;
            Assert.That(warmPoints, Is.GreaterThanOrEqualTo(2));
            Assert.That(afterWarm.Misses, Is.EqualTo(before.Misses + 1));

            long startTicks = Stopwatch.GetTimestamp();
            for (int i = 0; i < repeatedQueries; i++)
            {
                int points = SolveAndRelease(router, pathStore, request);
                Assert.That(points, Is.EqualTo(warmPoints));
            }

            double repeatedElapsedMs = (Stopwatch.GetTimestamp() - startTicks) * 1000d / Stopwatch.Frequency;
            PathQueryCacheDiagnostics afterRepeated = router.CacheDiagnostics;
            Assert.That(afterRepeated.Hits, Is.GreaterThanOrEqualTo(afterWarm.Hits + repeatedQueries));
            Assert.That(afterRepeated.Misses, Is.EqualTo(afterWarm.Misses));

            using var gate = new ManualResetEventSlim(false);
            var tasks = new Task<int>[concurrentQueries];
            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = Task.Run(() =>
                {
                    gate.Wait();
                    return SolveAndRelease(router, pathStore, request);
                });
            }

            gate.Set();
            Task.WaitAll(tasks);
            Assert.That(tasks, Has.All.Matches<Task<int>>(task => task.Result == warmPoints));
            PathQueryCacheDiagnostics afterConcurrent = router.CacheDiagnostics;
            Assert.That(afterConcurrent.Hits, Is.GreaterThanOrEqualTo(afterRepeated.Hits + concurrentQueries));
            Assert.That(afterConcurrent.Misses, Is.EqualTo(afterRepeated.Misses));
            Console.WriteLine(
                $"[Benchmark] U16 full-world repeated/concurrent NavMesh cache: routeChunks={endpoints.MacroRouteChunkCount} " +
                $"points={warmPoints} repeated={repeatedQueries} avgRepeatedUs={repeatedElapsedMs * 1000d / repeatedQueries:F3} " +
                $"concurrent={concurrentQueries} hits={afterConcurrent.Hits} misses={afterConcurrent.Misses} source={endpoints.Source}");
        }

        [Test]
        public void WaypointAuthoringFocusedShowcase_EditsAuthoredWaypointAndRegeneratesPathpointsWithoutOrder()
        {
            using GameEngine engine = CreateMassNavigationFocusedEntryEngineWithUi("MassNavigationU10WaypointAuthoringShowcaseMod");
            engine.LoadMap("mass_navigation");
            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            MassNavigationShowcaseGuideRuntime guide = RequireGuide(engine);

            var input = new FrozenInputActionReader();
            var pointerButtons = new AuthoritativePointerButtonSnapshot();
            var bindings = new InteractionActionBindings();
            var pathStore = new PathStore(maxPaths: 16, maxPointsPerPath: 8);
            var pathService = new RecordingPathService(pathStore);
            var pathPreview = new MassNavigationPathPreviewInputSystem(engine, simulation, guide);
            Vector2 start = new(
                simulation.BakeDataDiagnostics!.WorldMinXCm + (simulation.BakeDataDiagnostics.MacroChunkSizeXCm * 12) + 1_100,
                simulation.BakeDataDiagnostics.WorldMinYCm + (simulation.BakeDataDiagnostics.MacroChunkSizeYCm * 12) + 1_900);
            Vector2 goal = new(
                simulation.BakeDataDiagnostics.WorldMinXCm + (simulation.BakeDataDiagnostics.MacroChunkSizeXCm * 18) + 1_600,
                simulation.BakeDataDiagnostics.WorldMinYCm + (simulation.BakeDataDiagnostics.MacroChunkSizeYCm * 16) + 2_300);
            Vector2 midpoint = (start + goal) * 0.5f + new Vector2(2_400f, -1_700f);

            engine.SetService(CoreServiceKeys.AuthoritativeInput, input);
            engine.SetService(CoreServiceKeys.AuthoritativePointerButtons, pointerButtons);
            engine.SetService(CoreServiceKeys.InteractionActionBindings, bindings);
            engine.SetService(CoreServiceKeys.PathStore, pathStore);
            engine.SetService(CoreServiceKeys.PathService, pathService);

            Assert.That(guide.CurrentStepId, Is.EqualTo(MassNavigationShowcaseStepId.WaypointAuthoring));
            guide.ArmPathDrivenOperation(MassNavigationShowcaseStepId.WaypointAuthoring);

            SetPointerPick(input, pointerButtons, bindings.ConfirmActionId, start);
            pathPreview.Update(1f / 60f);
            ClearPointerPick(input, pointerButtons, bindings.ConfirmActionId);
            engine.SetService(CoreServiceKeys.PointerInputCaptured, false);
            SetPointerPick(input, pointerButtons, bindings.CommandActionId, goal);
            pathPreview.Update(1f / 60f);

            Assert.That(pathService.Requests, Has.Count.EqualTo(1));
            Assert.That(simulation.AcceptanceDiagnostics.WaypointPath.HasAuthoredPlan, Is.False);
            int initialPathpoints = simulation.AcceptanceDiagnostics.PathOnlyQuery.PathPointCount;

            ClearPointerPick(input, pointerButtons, bindings.CommandActionId);
            engine.SetService(CoreServiceKeys.PointerInputCaptured, false);
            SetPointerPick(input, pointerButtons, bindings.ConfirmActionId, midpoint);
            pathPreview.Update(1f / 60f);

            Assert.That(pathService.Requests, Has.Count.EqualTo(3), "Waypoint edit must re-query the two authored legs through PathService.");
            MassNavigationWaypointPathDiagnostics waypoint = simulation.AcceptanceDiagnostics.WaypointPath;
            Assert.That(waypoint.HasAuthoredPlan, Is.True);
            Assert.That(waypoint.WaypointCount, Is.EqualTo(3));
            Assert.That(waypoint.PathPointCount, Is.GreaterThanOrEqualTo(3));
            Assert.That(waypoint.InvalidatedPathPointCount, Is.EqualTo(initialPathpoints));
            Assert.That(waypoint.AuthoredMidpointWorldCm, Is.EqualTo(midpoint));
            Assert.That(waypoint.EditState, Is.EqualTo("edited_from_user_world_click_pathpoints_regenerated"));
            Assert.That(simulation.AcceptanceDiagnostics.InvalidatedWaypointPathPoints.Length, Is.EqualTo(initialPathpoints));
            Assert.That(simulation.AcceptanceDiagnostics.PathOnlyQuery.WaypointCount, Is.EqualTo(3));
            Assert.That(simulation.AcceptanceDiagnostics.PathOnlyQuery.RoutePreviewState, Is.EqualTo("waypoint_plan_edited_pathpoints_regenerated"));
            Assert.That(guide.LastActionText, Does.Contain("Waypoint edited from user click"));
            Assert.That(guide.LastActionOrderDelta, Is.EqualTo(0));
            Assert.That(simulation.CommandCountFrame + simulation.PendingCommandCount, Is.EqualTo(0));
        }

        [Test]
        public void AllFocusedEntryMods_AreResourceOnlySingleUseCasePresets()
        {
            string repoRoot = PerformerBlacksmithShowcaseTestHarness.FindRepoRoot();
            var service = new LauncherService(repoRoot);

            foreach ((string entryModId, MassNavigationShowcaseStepId expectedStepId) in FocusedEntryMods())
            {
                var result = service.Resolve(
                    new[] { $"mod:{entryModId}" },
                    LauncherPlatformIds.Raylib,
                    LauncherBuildMode.Never);
                var orderedMods = result.Plan.OrderedModIds.ToList();
                var startupSetting = result.Plan.Diagnostics.Settings.Single(setting =>
                    string.Equals(setting.Key, "startupMapId", StringComparison.OrdinalIgnoreCase));

                Assert.That(result.Plan.RootModIds, Is.EqualTo(new[] { entryModId }), entryModId);
                Assert.That(orderedMods, Does.Contain("MassNavigationMod"), entryModId);
                Assert.That(orderedMods.IndexOf("MassNavigationMod"), Is.LessThan(orderedMods.IndexOf(entryModId)), entryModId);
                Assert.That(startupSetting.EffectiveValue?.GetValue<string>(), Is.EqualTo("mass_navigation"), entryModId);
                Assert.That(startupSetting.EffectiveSource, Does.Contain(entryModId).IgnoreCase, entryModId);

                using GameEngine engine = CreateMassNavigationFocusedEntryEngineWithUi(entryModId);
                engine.LoadMap("mass_navigation");
                MassNavigationShowcaseGuideRuntime guide = RequireGuide(engine);
                Assert.That(guide.FocusedPanel, Is.True, entryModId);
                Assert.That(guide.StepCount, Is.EqualTo(1), entryModId);
                Assert.That(guide.CurrentStepId, Is.EqualTo(expectedStepId), entryModId);
                Assert.That(guide.AllowsStep(expectedStepId), Is.True, entryModId);
                Assert.That(guide.PlayerPerspective, Is.Not.Empty, entryModId);
                Assert.That(guide.ModAuthorPerspective, Is.Not.Empty, entryModId);
                Assert.That(guide.PlayerPerspective, Does.Not.Contain("Play Showcase"), entryModId);
                Assert.That(guide.PrimaryActionLabel, Is.Not.EqualTo("Play Showcase"), entryModId);
                Assert.That(guide.PrimaryActionLabel, Is.Not.EqualTo("Play Current Step"), entryModId);
                Assert.That(guide.OperationContract, Does.Contain("Input:"), entryModId);
                Assert.That(guide.OperationContract, Does.Contain("Output:"), entryModId);
            }
        }

        [Test]
        public void UseCaseRunbook_SplitsEditorWorkbenchFromPlayableMods()
        {
            string repoRoot = PerformerBlacksmithShowcaseTestHarness.FindRepoRoot();
            string script = File.ReadAllText(Path.Combine(repoRoot, "scripts", "acceptance", "run-mass-navigation-usecase.ps1"));

            Assert.That(script, Does.Contain("$editorCases = @(\"U01\", \"U02\", \"U03\", \"U09\")"));
            Assert.That(script, Does.Not.Contain("$editorCases = @(\"U01\", \"U02\", \"U03\", \"U09\", \"U16\")"));
            Assert.That(script, Does.Contain("run-navmesh-bake-raylib-acceptance.ps1"));
            Assert.That(script, Does.Contain("EditorApplyPatch"));
            Assert.That(script, Does.Contain("EditorWorkbenchEvidenceOnly"));
            Assert.That(script, Does.Contain("InteractiveWorkbench"));
            Assert.That(script, Does.Contain("showcase_body=interactive_editor_workbench"));
            Assert.That(script, Does.Contain("showcase_body=$showcaseBody"));
            Assert.That(script, Does.Contain("runtime_navdata_authoring_update"));
            Assert.That(script, Does.Contain("interactive_playable_mod"));
            Assert.That(script, Does.Contain("CaptureEvidence"));
            Assert.That(script, Does.Contain("MassNavigationU04PathOnlyQueryShowcaseMod"));
            Assert.That(script, Does.Contain("MassNavigationU15DebugVisualBudgetShowcaseMod"));
            Assert.That(script, Does.Contain("mod:$entryMod"));
            Assert.That(script, Does.Contain("LUDOTS_TAKE_SCREENSHOT_PATH"));
            Assert.That(script, Does.Contain("LUDOTS_MASS_NAV_REPLAY_USECASE"));
            Assert.That(script, Does.Contain("LUDOTS_MASS_NAV_REPLAY_TRACE_PATH"));
            Assert.That(script, Does.Contain("operation_replay_then_capture"));
            Assert.That(script, Does.Contain("Operation trace for $UseCase did not include input/result/complete events"));
        }

        [Test]
        public void CaptureReplay_PathOnlyWritesOperationTraceThroughPathPreviewInput()
        {
            string tracePath = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"mass-nav-u04-replay-{Guid.NewGuid():N}.jsonl");
            try
            {
                Environment.SetEnvironmentVariable("LUDOTS_MASS_NAV_REPLAY_USECASE", "U04");
                Environment.SetEnvironmentVariable("LUDOTS_MASS_NAV_REPLAY_TRACE_PATH", tracePath);
                Environment.SetEnvironmentVariable("LUDOTS_MASS_NAV_REPLAY_FRAME_START", "1");

                using GameEngine engine = CreateMassNavigationFocusedEntryEngineWithUi("MassNavigationU04PathOnlyQueryShowcaseMod");
                PerformerBlacksmithShowcaseTestHarness.LoadMap(engine, "mass_navigation", frames: 48);
                MassNavigationSimulationRuntime simulation = RequireSimulation(engine);

                Assert.That(File.Exists(tracePath), Is.True);
                string trace = File.ReadAllText(tracePath);
                Assert.That(trace, Does.Contain("\"operation\":\"left_click_start\""));
                Assert.That(trace, Does.Contain("\"operation\":\"right_click_goal\""));
                Assert.That(trace, Does.Contain("\"kind\":\"result\""));
                Assert.That(trace, Does.Contain("\"kind\":\"complete\""));
                Assert.That(trace, Does.Contain(nameof(MassNavigationPathPreviewInputSystem)));
                Assert.That(simulation.AcceptanceDiagnostics.PathOnlyQuery.Available, Is.True);
                Assert.That(simulation.AcceptanceDiagnostics.PathOnlyQuery.NoOrderSubmitted, Is.True);
                Assert.That(simulation.CommandCountFrame + simulation.PendingCommandCount, Is.EqualTo(0));
            }
            finally
            {
                Environment.SetEnvironmentVariable("LUDOTS_MASS_NAV_REPLAY_USECASE", null);
                Environment.SetEnvironmentVariable("LUDOTS_MASS_NAV_REPLAY_TRACE_PATH", null);
                Environment.SetEnvironmentVariable("LUDOTS_MASS_NAV_REPLAY_FRAME_START", null);
                if (File.Exists(tracePath))
                {
                    File.Delete(tracePath);
                }
            }
        }

        [Test]
        public void CaptureReplay_RuntimeBakeWritesOperationTraceThroughRuntimeAuthoring()
        {
            string tracePath = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"mass-nav-u16-replay-{Guid.NewGuid():N}.jsonl");
            try
            {
                Environment.SetEnvironmentVariable("LUDOTS_MASS_NAV_REPLAY_USECASE", "U16");
                Environment.SetEnvironmentVariable("LUDOTS_MASS_NAV_REPLAY_TRACE_PATH", tracePath);
                Environment.SetEnvironmentVariable("LUDOTS_MASS_NAV_REPLAY_FRAME_START", "1");

                using GameEngine engine = CreateMassNavigationFocusedEntryEngineWithUi("MassNavigationU16BakeToolQueryShowcaseMod");
                PerformerBlacksmithShowcaseTestHarness.LoadMap(engine, "mass_navigation", frames: 64);
                for (int i = 0; i < 64; i++)
                {
                    if (File.Exists(tracePath) &&
                        File.ReadAllText(tracePath).Contains("\"kind\":\"complete\"", StringComparison.Ordinal))
                    {
                        break;
                    }

                    PerformerBlacksmithShowcaseTestHarness.Tick(engine, 1);
                }

                MassNavigationSimulationRuntime simulation = RequireSimulation(engine);

                Assert.That(File.Exists(tracePath), Is.True);
                string trace = File.ReadAllText(tracePath);
                Assert.That(trace, Does.Contain("\"operation\":\"draw_runtime_obstacle_polygon\""));
                Assert.That(trace, Does.Contain("\"operation\":\"update_navdata\""));
                Assert.That(trace, Does.Contain("\"operation\":\"runtime_navdata_authoring_update\""));
                Assert.That(trace, Does.Contain("\"BakedTileCount\":"));
                Assert.That(trace, Does.Contain("\"ChangedTileCount\":"));
                Assert.That(trace, Does.Contain(nameof(MassNavigationRuntimeBakeAuthoringInputSystem)));
                Assert.That(trace, Does.Contain("\"kind\":\"complete\""));
                Assert.That(simulation.AcceptanceDiagnostics.RuntimeNavDataUpdate.AuthoredPolygonCount, Is.EqualTo(1));
                Assert.That(simulation.AcceptanceDiagnostics.RuntimeNavDataUpdate.DirtyChunkCount, Is.GreaterThan(0));
                Assert.That(simulation.AcceptanceDiagnostics.RuntimeNavDataUpdate.BakedTileCount, Is.GreaterThan(0));
                Assert.That(simulation.AcceptanceDiagnostics.RuntimeNavDataUpdate.ChangedTileCount, Is.GreaterThan(0));
                Assert.That(simulation.AcceptanceDiagnostics.RuntimeNavDataUpdate.BeforeChecksumXor, Is.Not.EqualTo(simulation.AcceptanceDiagnostics.RuntimeNavDataUpdate.AfterChecksumXor));
                Assert.That(simulation.AcceptanceDiagnostics.RuntimeNavDataUpdate.BeforeGeometryHashXor, Is.Not.EqualTo(simulation.AcceptanceDiagnostics.RuntimeNavDataUpdate.AfterGeometryHashXor));
                Assert.That(simulation.AcceptanceDiagnostics.RuntimeNavDataUpdate.NavDataRevision, Is.EqualTo(1));
                Assert.That(simulation.AcceptanceDiagnostics.RuntimeNavDataUpdate.QueryPathPointCount, Is.GreaterThan(0));
            }
            finally
            {
                Environment.SetEnvironmentVariable("LUDOTS_MASS_NAV_REPLAY_USECASE", null);
                Environment.SetEnvironmentVariable("LUDOTS_MASS_NAV_REPLAY_TRACE_PATH", null);
                Environment.SetEnvironmentVariable("LUDOTS_MASS_NAV_REPLAY_FRAME_START", null);
                if (File.Exists(tracePath))
                {
                    File.Delete(tracePath);
                }
            }
        }

        [Test]
        public void CaptureReplay_TenKFlowWritesOperationTraceThroughSelectionOrderAndFlow()
        {
            string tracePath = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"mass-nav-u12-replay-{Guid.NewGuid():N}.jsonl");
            try
            {
                Environment.SetEnvironmentVariable("LUDOTS_MASS_NAV_REPLAY_USECASE", "U12");
                Environment.SetEnvironmentVariable("LUDOTS_MASS_NAV_REPLAY_TRACE_PATH", tracePath);
                Environment.SetEnvironmentVariable("LUDOTS_MASS_NAV_REPLAY_FRAME_START", "1");

                using GameEngine engine = CreateMassNavigationFocusedEntryEngineWithUi("MassNavigationU12TenKFlowShowcaseMod");
                PerformerBlacksmithShowcaseTestHarness.LoadMap(engine, "mass_navigation", frames: 64);
                for (int i = 0; i < 96; i++)
                {
                    if (File.Exists(tracePath) &&
                        File.ReadAllText(tracePath).Contains("\"kind\":\"complete\"", StringComparison.Ordinal))
                    {
                        break;
                    }

                    PerformerBlacksmithShowcaseTestHarness.Tick(engine, 1);
                }

                MassNavigationSimulationRuntime simulation = RequireSimulation(engine);

                Assert.That(File.Exists(tracePath), Is.True);
                string trace = File.ReadAllText(tracePath);
                Assert.That(trace, Does.Contain("\"operation\":\"select_10k_army\""));
                Assert.That(trace, Does.Contain("\"operation\":\"right_click_destination\""));
                Assert.That(trace, Does.Contain("\"operation\":\"order_bridge_after_large_selection_order\""));
                Assert.That(trace, Does.Contain("\"operation\":\"target_refresh_and_flow_smoke\""));
                Assert.That(trace, Does.Contain("\"kind\":\"result\""));
                Assert.That(trace, Does.Contain("\"kind\":\"complete\""));
                Assert.That(trace, Does.Contain("SelectionRuntime.LivePrimary"));
                Assert.That(trace, Does.Contain("\"flowEnabled\":true"));
                Assert.That(simulation.SelectedCount, Is.EqualTo(10_000));
                Assert.That(simulation.AcceptanceDiagnostics.TargetAllocation.SelectedCount, Is.EqualTo(10_000));
                Assert.That(simulation.MassFlow.CountUnitsWithTargets(), Is.GreaterThanOrEqualTo(10_000));
            }
            finally
            {
                Environment.SetEnvironmentVariable("LUDOTS_MASS_NAV_REPLAY_USECASE", null);
                Environment.SetEnvironmentVariable("LUDOTS_MASS_NAV_REPLAY_TRACE_PATH", null);
                Environment.SetEnvironmentVariable("LUDOTS_MASS_NAV_REPLAY_FRAME_START", null);
                if (File.Exists(tracePath))
                {
                    File.Delete(tracePath);
                }
            }
        }

        [Test]
        public void FocusedEntryPanel_UsesOperationControlsInsteadOfSlideshowLanguage()
        {
            using GameEngine engine = CreateMassNavigationFocusedEntryEngineWithUi("MassNavigationU08TargetAllocationShowcaseMod");
            engine.LoadMap("mass_navigation");
            UIRoot root = (UIRoot)(engine.GetService(CoreServiceKeys.UIRoot)
                ?? throw new InvalidOperationException("UIRoot missing."));
            root.Scene!.Layout(root.Width, root.Height);
            string text = ExtractUiSceneText(root);

            Assert.That(text, Does.Contain("Step 1/1 U8 Large-selection target allocation"));
            Assert.That(text, Does.Not.Contain("Goal:"));
            Assert.That(text, Does.Contain("Click Select 10k Army, then Right-click one destination."));
            Assert.That(text, Does.Contain("Live: selected=0; slots=0"));
            Assert.That(text, Does.Contain("Pass: Gate target selected=10000"));
            Assert.That(text, Does.Not.Contain("Input: selected 10k army plus one right-click destination."));
            Assert.That(text, Does.Contain("Select 10k Army"));
            Assert.That(text, Does.Contain("Right-click one destination"));
            Assert.That(text, Does.Contain("Field"));
            Assert.That(text, Does.Contain("Map"));
            Assert.That(text, Does.Not.Contain("Reset"));
            Assert.That(text, Does.Not.Contain("Focus View"));
            Assert.That(text, Does.Not.Contain("Play Showcase"));
            Assert.That(text, Does.Not.Contain("Play Current Step"));
            Assert.That(text, Does.Not.Contain("Now:"));
            Assert.That(text, Does.Not.Contain("Look:"));
            UiNode panel = ResolveFocusedPanelNode(root);
            Assert.That(panel.LayoutRect.Width, Is.LessThanOrEqualTo(244f), "Focused showcase controls must stay in a compact C&C-style command box, not a battlefield-width panel.");
            Assert.That(panel.LayoutRect.Height, Is.LessThanOrEqualTo(210f));
            Assert.That(panel.LayoutRect.X, Is.GreaterThanOrEqualTo(root.Width - 268f), "Focused showcase controls must live on the right edge instead of covering the battlefield center.");
            Assert.That(panel.LayoutRect.Y, Is.GreaterThanOrEqualTo(root.Height - 228f), "Focused showcase controls must stay near the bottom edge.");
        }

        [Test]
        public void MassNavigationMinimap_StaysCornerSizedForFocusedRtsShowcases()
        {
            using GameEngine engine = CreateMassNavigationFocusedEntryEngineWithUi("MassNavigationU12TenKFlowShowcaseMod");
            engine.LoadMap("mass_navigation");
            var minimap = engine.GetService(CoreServiceKeys.MinimapRuntime)
                ?? throw new InvalidOperationException("MinimapRuntime missing.");
            var markers = engine.GetService(CoreServiceKeys.MinimapMarkerBuffer)
                ?? throw new InvalidOperationException("MinimapMarkerBuffer missing.");
            var screenMarkers = engine.GetService(CoreServiceKeys.MinimapScreenMarkerBuffer)
                ?? throw new InvalidOperationException("MinimapScreenMarkerBuffer missing.");

            SetHeadlessViewport(engine, 1280, 720);
            minimap.Visible = true;
            minimap.UseRtsFullMapPreset();
            minimap.Refresh(engine, markers, screenMarkers);

            Assert.That(minimap.FieldSize, Is.InRange(120, 132), "MassNavigation minimap should be sized from the 1280x720 viewport ratio, not a fixed large diagnostics map.");
            Assert.That(minimap.PanelWidth, Is.LessThanOrEqualTo(168));
            Assert.That(minimap.PanelHeight, Is.LessThanOrEqualTo(256));
            Assert.That(minimap.ZoomSliderEnabled, Is.False);
            Assert.That(minimap.PresetToggleWidth, Is.EqualTo(68));
        }

        [Test]
        public void MassNavigationMinimap_TracksViewportRatioInsteadOfFixedPixels()
        {
            using GameEngine engine = CreateMassNavigationFocusedEntryEngineWithUi("MassNavigationU12TenKFlowShowcaseMod");
            engine.LoadMap("mass_navigation");
            var minimap = engine.GetService(CoreServiceKeys.MinimapRuntime)
                ?? throw new InvalidOperationException("MinimapRuntime missing.");
            var markers = engine.GetService(CoreServiceKeys.MinimapMarkerBuffer)
                ?? throw new InvalidOperationException("MinimapMarkerBuffer missing.");
            var screenMarkers = engine.GetService(CoreServiceKeys.MinimapScreenMarkerBuffer)
                ?? throw new InvalidOperationException("MinimapScreenMarkerBuffer missing.");

            minimap.Visible = true;
            minimap.UseRtsFullMapPreset();

            SetHeadlessViewport(engine, 960, 540);
            minimap.Refresh(engine, markers, screenMarkers);
            int compactFieldSize = minimap.FieldSize;
            int compactPanelWidth = minimap.PanelWidth;

            SetHeadlessViewport(engine, 1920, 1080);
            minimap.Refresh(engine, markers, screenMarkers);

            Assert.That(compactFieldSize, Is.EqualTo(120), "Small viewports use only the minimum safety floor.");
            Assert.That(minimap.FieldSize, Is.InRange(190, 196), "Large viewports must grow from the configured short-edge ratio instead of staying at the small-screen pixel cap.");
            Assert.That(minimap.FieldSize, Is.GreaterThan(compactFieldSize));
            Assert.That(minimap.PanelWidth, Is.GreaterThan(compactPanelWidth));
            Assert.That(minimap.PanelWidth, Is.LessThanOrEqualTo(1920 * 0.13f));
            Assert.That(minimap.PanelHeight, Is.LessThanOrEqualTo(1080 * 0.25f));
        }

        [Test]
        public void FocusedEntryOverlay_UsesSmallBattlefieldStatusCapsule()
        {
            using GameEngine engine = CreateMassNavigationFocusedEntryEngineWithUi("MassNavigationU12TenKFlowShowcaseMod");
            PerformerBlacksmithShowcaseTestHarness.LoadMap(engine, "mass_navigation", frames: 4);
            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            MassNavigationShowcaseGuideRuntime guide = RequireGuide(engine);
            var system = new MassNavigationShowcasePresentationSystem(engine, simulation, guide);
            var screen = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
                ?? throw new InvalidOperationException("ScreenOverlayBuffer missing.");

            system.Update(0.016f);

            ScreenOverlayItem[] capsules = screen.GetSpan()
                .ToArray()
                .Where(item => item.Kind == ScreenOverlayItemKind.Rect && item.StableId == 44900)
                .ToArray();
            string[] strings = GetOverlayStrings(screen);
            Assert.That(capsules, Is.Not.Empty);
            Assert.That(capsules.All(item => item.Width <= 280), Is.True);
            Assert.That(capsules.All(item => item.Height <= 48), Is.True);
            Assert.That(strings.Any(text => text.Contains("U12 Flow", StringComparison.Ordinal)), Is.True, DumpStrings(strings));
            Assert.That(strings.Any(text => text.Contains("10k ok", StringComparison.Ordinal)), Is.True, DumpStrings(strings));
            Assert.That(strings.Any(text => text.Contains("Do:", StringComparison.Ordinal)), Is.False, DumpStrings(strings));
            Assert.That(strings.Any(text => text.StartsWith("Live:", StringComparison.Ordinal)), Is.False, DumpStrings(strings));
            Assert.That(strings.Any(text => text.StartsWith("Pass:", StringComparison.Ordinal)), Is.False, DumpStrings(strings));
        }

        [TestCase(960, 540)]
        [TestCase(1280, 720)]
        [TestCase(1920, 1080)]
        public void FocusedEntryHudAndMinimap_ScaleFromViewportWithoutCoveringBattlefield(int width, int height)
        {
            using GameEngine engine = CreateMassNavigationFocusedEntryEngineWithUi("MassNavigationU12TenKFlowShowcaseMod");
            PerformerBlacksmithShowcaseTestHarness.LoadMap(engine, "mass_navigation", frames: 4);
            SetHeadlessViewport(engine, width, height);
            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            MassNavigationShowcaseGuideRuntime guide = RequireGuide(engine);
            var system = new MassNavigationShowcasePresentationSystem(engine, simulation, guide);
            var screen = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
                ?? throw new InvalidOperationException("ScreenOverlayBuffer missing.");
            UIRoot root = (UIRoot)(engine.GetService(CoreServiceKeys.UIRoot)
                ?? throw new InvalidOperationException("UIRoot missing."));
            var minimap = engine.GetService(CoreServiceKeys.MinimapRuntime)
                ?? throw new InvalidOperationException("MinimapRuntime missing.");
            var markers = engine.GetService(CoreServiceKeys.MinimapMarkerBuffer)
                ?? throw new InvalidOperationException("MinimapMarkerBuffer missing.");
            var screenMarkers = engine.GetService(CoreServiceKeys.MinimapScreenMarkerBuffer)
                ?? throw new InvalidOperationException("MinimapScreenMarkerBuffer missing.");

            root.Resize(width, height);
            PerformerBlacksmithShowcaseTestHarness.Tick(engine, 18);
            root.Scene!.Layout(root.Width, root.Height);
            screen.Clear();
            system.Update(0.016f);
            minimap.Visible = true;
            minimap.UseRtsFullMapPreset();
            minimap.Refresh(engine, markers, screenMarkers);

            UiNode panel = ResolveFocusedPanelNode(root);
            ScreenOverlayItem capsule = screen.GetSpan()
                .ToArray()
                .Single(item => item.Kind == ScreenOverlayItemKind.Rect && item.StableId == 44900);
            Assert.That(panel.LayoutRect.Width, Is.LessThanOrEqualTo(width * 0.25f));
            Assert.That(panel.LayoutRect.Height, Is.LessThanOrEqualTo(height * 0.31f));
            Assert.That(panel.LayoutRect.X, Is.GreaterThanOrEqualTo(width - panel.LayoutRect.Width - 24f));
            Assert.That(panel.LayoutRect.Y, Is.GreaterThanOrEqualTo(height - panel.LayoutRect.Height - 24f));
            Assert.That(capsule.Width, Is.LessThanOrEqualTo(width * 0.21f));
            Assert.That(capsule.Height, Is.LessThanOrEqualTo(40));
            Assert.That(minimap.PanelWidth, Is.LessThanOrEqualTo(width * 0.23f));
            Assert.That(minimap.PanelHeight, Is.LessThanOrEqualTo(height * 0.36f));
        }

        [Test]
        public void WorldHpaFocusedOverlay_SplitsGlobalRouteFromLoadedPortalSample()
        {
            string tracePath = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"mass-nav-u05-replay-{Guid.NewGuid():N}.jsonl");
            try
            {
                Environment.SetEnvironmentVariable("LUDOTS_MASS_NAV_REPLAY_USECASE", "U05");
                Environment.SetEnvironmentVariable("LUDOTS_MASS_NAV_REPLAY_TRACE_PATH", tracePath);
                Environment.SetEnvironmentVariable("LUDOTS_MASS_NAV_REPLAY_FRAME_START", "1");

                using GameEngine engine = CreateMassNavigationFocusedEntryEngineWithUi("MassNavigationU05WorldHpaRouteShowcaseMod");
                PerformerBlacksmithShowcaseTestHarness.LoadMap(engine, "mass_navigation", frames: 48);
                MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
                MassNavigationShowcaseGuideRuntime guide = RequireGuide(engine);
                var system = new MassNavigationShowcasePresentationSystem(engine, simulation, guide);
                var screen = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
                    ?? throw new InvalidOperationException("ScreenOverlayBuffer missing.");

                screen.Clear();
                system.Update(0.016f);

                string[] strings = GetOverlayStrings(screen);
                Assert.That(strings.Any(text => text.Contains("U5 HPA Route", StringComparison.Ordinal)), Is.True, DumpStrings(strings));
                Assert.That(strings.Any(text => text.Contains("global 8,9->248,247", StringComparison.Ordinal)), Is.True, DumpStrings(strings));
                Assert.That(strings.Any(text => text.Contains("Global HPA route:", StringComparison.Ordinal)), Is.True, DumpStrings(strings));
                Assert.That(strings.Any(text => text.Contains("sampled 256x256", StringComparison.Ordinal) || text.Contains("macro chunks", StringComparison.Ordinal)), Is.True, DumpStrings(strings));
                Assert.That(strings.Any(text => text.Contains("Global route 8,9->248,247", StringComparison.Ordinal)), Is.True, DumpStrings(strings));
                Assert.That(strings.Any(text => text.Contains("Loaded window", StringComparison.Ordinal)), Is.True, DumpStrings(strings));
                Assert.That(strings.Any(text => text.Contains("Portal sample: loaded window route", StringComparison.Ordinal)), Is.True, DumpStrings(strings));
                Assert.That(strings.Any(text => text.Contains("map label:", StringComparison.Ordinal)), Is.False, DumpStrings(strings));
                Assert.That(File.Exists(tracePath), Is.True);
            }
            finally
            {
                Environment.SetEnvironmentVariable("LUDOTS_MASS_NAV_REPLAY_USECASE", null);
                Environment.SetEnvironmentVariable("LUDOTS_MASS_NAV_REPLAY_TRACE_PATH", null);
                Environment.SetEnvironmentVariable("LUDOTS_MASS_NAV_REPLAY_FRAME_START", null);
                if (File.Exists(tracePath))
                {
                    File.Delete(tracePath);
                }
            }
        }

        [Test]
        public void TargetAllocationShowcase_PreparesRealSelectionBeforeRightClickOrder()
        {
            using GameEngine engine = CreateMassNavigationFocusedEntryEngineWithUi("MassNavigationU08TargetAllocationShowcaseMod");
            engine.LoadMap("mass_navigation");
            WaitForMassNavigationAgents(engine, expectedControllableCount: 10_000);
            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            MassNavigationShowcaseGuideRuntime guide = RequireGuide(engine);
            var controller = new MassNavigationPanelController();

            Assert.That(simulation.AgentState.ControllableCount, Is.GreaterThanOrEqualTo(10_000));
            Assert.That(controller.MountOrSync(engine, simulation), Is.True);
            string before = ExtractUiSceneText((UIRoot)engine.GetService(CoreServiceKeys.UIRoot)!);
            Assert.That(before, Does.Contain("Select 10k Army"));
            Assert.That(simulation.SelectedCount, Is.EqualTo(0));

            InvokeUiButton((UIRoot)engine.GetService(CoreServiceKeys.UIRoot)!, "Select 10k Army");

            SelectionRuntime selection = engine.GetService(CoreServiceKeys.SelectionRuntime)
                ?? throw new InvalidOperationException("SelectionRuntime missing.");
            Assert.That(SelectionContextRuntime.GetCurrentCount(engine.World, engine.GlobalContext), Is.EqualTo(10_000));
            Assert.That(simulation.SelectedCount, Is.EqualTo(10_000));
            Assert.That(guide.CurrentStepId, Is.EqualTo(MassNavigationShowcaseStepId.TargetAllocation));
            Assert.That(guide.LastActionText, Does.Contain("Right-click one destination"));

            Arch.Core.Entity[] selected = SelectionContextRuntime.SnapshotCurrentSelection(engine.World, engine.GlobalContext);
            Assert.That(selected, Has.Length.EqualTo(10_000));
            Assert.That(selection.GetViewCount(
                    (Arch.Core.Entity)engine.GetService(CoreServiceKeys.LocalPlayerEntity)!,
                    SelectionViewKeys.Primary),
                Is.EqualTo(10_000));

            var input = new FrozenInputActionReader();
            var bindings = new InteractionActionBindings();
            Vector2 destination = new(simulation.SolverWindowCenterXCm + 3_000f, simulation.SolverWindowCenterYCm + 1_500f);
            engine.SetService(CoreServiceKeys.AuthoritativeInput, input);
            engine.SetService(CoreServiceKeys.InteractionActionBindings, bindings);
            input.SetActionState(
                AuthoritativeGroundPointerHelper.ActionId,
                new Vector3(destination.X, 0f, destination.Y),
                isDown: true,
                pressedThisFrame: false,
                releasedThisFrame: false);
            input.SetActionState(
                bindings.CommandActionId,
                Vector3.One,
                isDown: false,
                pressedThisFrame: true,
                releasedThisFrame: false);

            var bridge = new MassNavigationCommandBridgeSystem(engine, simulation);
            bridge.Update(1f / 60f);

            Assert.That(simulation.AcceptanceDiagnostics.TargetAllocation.HasAllocation, Is.True);
            Assert.That(simulation.AcceptanceDiagnostics.TargetAllocation.SelectedCount, Is.EqualTo(10_000));
            Assert.That(simulation.AcceptanceDiagnostics.TargetAllocation.SlotCount, Is.GreaterThanOrEqualTo(10_000));
            Assert.That(simulation.AcceptanceDiagnostics.OrderReuse.HasOrder, Is.True);
            Assert.That(simulation.LastCommandSelectionCount, Is.EqualTo(10_000));
            Assert.That(simulation.AcceptanceDiagnostics.TargetAllocation.ActualTargetSampleCount, Is.EqualTo(0));
            Assert.That(simulation.AcceptanceDiagnostics.TargetAllocation.ActualTargetSampleSource, Is.EqualTo("mass_flow_targets_not_sampled_yet"));
        }

        [Test]
        public void OrderReuseShowcase_UsesFormalRightClickOrdersForSameAndNearBucketReuse()
        {
            using GameEngine engine = CreateMassNavigationFocusedEntryEngineWithUi("MassNavigationU07OrderReuseShowcaseMod");
            engine.LoadMap("mass_navigation");
            WaitForMassNavigationAgents(engine, expectedControllableCount: 64);
            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            MassNavigationShowcaseGuideRuntime guide = RequireGuide(engine);
            var controller = new MassNavigationPanelController();

            Assert.That(controller.MountOrSync(engine, simulation), Is.True);
            string text = ExtractUiSceneText((UIRoot)engine.GetService(CoreServiceKeys.UIRoot)!);
            Assert.That(text, Does.Contain("Select Reuse Squad"));
            Assert.That(CountUiButtons((UIRoot)engine.GetService(CoreServiceKeys.UIRoot)!, "Select Reuse Squad"), Is.EqualTo(1));
            Assert.That(CountUiButtons((UIRoot)engine.GetService(CoreServiceKeys.UIRoot)!, "Same Order"), Is.EqualTo(0));
            Assert.That(CountUiButtons((UIRoot)engine.GetService(CoreServiceKeys.UIRoot)!, "Near Order"), Is.EqualTo(0));

            InvokeUiButton((UIRoot)engine.GetService(CoreServiceKeys.UIRoot)!, "Select Reuse Squad");

            Assert.That(guide.CurrentStepId, Is.EqualTo(MassNavigationShowcaseStepId.OrderReuse));
            Assert.That(simulation.SelectedCount, Is.EqualTo(64));
            Assert.That(guide.LastActionText, Does.Contain("Right-click one destination twice"));

            Vector2 destination = new(simulation.SolverWindowCenterXCm + 2_800f, simulation.SolverWindowCenterYCm + 1_300f);
            SubmitRightClickDestination(engine, simulation, destination);
            Assert.That(simulation.AcceptanceDiagnostics.OrderReuse.CacheHit, Is.False);
            int routeId = simulation.AcceptanceDiagnostics.OrderReuse.ReusedRouteId;
            Assert.That(routeId, Is.GreaterThan(0));

            SubmitRightClickDestination(engine, simulation, destination);
            Assert.That(simulation.AcceptanceDiagnostics.OrderReuse.CacheHit, Is.True);
            Assert.That(simulation.AcceptanceDiagnostics.OrderReuse.ReusedRouteId, Is.EqualTo(routeId));
            Assert.That(simulation.AcceptanceDiagnostics.OrderReuse.ReuseScope, Is.EqualTo("same_point_order_bucket"));

            SubmitRightClickDestination(engine, simulation, destination + new Vector2(240f, 120f));
            Assert.That(simulation.AcceptanceDiagnostics.OrderReuse.CacheHit, Is.True);
            Assert.That(simulation.AcceptanceDiagnostics.OrderReuse.ReusedRouteId, Is.EqualTo(routeId));
            Assert.That(simulation.AcceptanceDiagnostics.OrderReuse.ReuseScope, Is.EqualTo("near_point_order_bucket"));
            Assert.That(simulation.AcceptanceDiagnostics.OrderReuse.FanoutCount, Is.EqualTo(64));
            Assert.That(simulation.AcceptanceDiagnostics.TargetAllocation.SelectedCount, Is.EqualTo(64));
        }

        [Test]
        public void TenKFlowShowcase_RoutesRealSelectionOrderIntoGasAndMassFlowTargets()
        {
            using GameEngine engine = CreateMassNavigationFocusedEntryEngineWithUi("MassNavigationU12TenKFlowShowcaseMod");
            engine.LoadMap("mass_navigation");
            WaitForMassNavigationAgents(engine, expectedControllableCount: 10_000);
            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            MassNavigationShowcaseGuideRuntime guide = RequireGuide(engine);
            var controller = new MassNavigationPanelController();

            Assert.That(controller.MountOrSync(engine, simulation), Is.True);
            Assert.That(guide.CurrentStepId, Is.EqualTo(MassNavigationShowcaseStepId.TenKFlow));
            Assert.That(simulation.FlowTuning.Enabled, Is.True);
            Assert.That(simulation.NavGroupRuntime.TargetRefreshBudget, Is.EqualTo(384));
            Assert.That(simulation.MassFlow.Semantics.Steering.MaxSeparationNeighborsPerUnit, Is.EqualTo(12));

            InvokeUiButton((UIRoot)engine.GetService(CoreServiceKeys.UIRoot)!, "Select 10k Army");

            Assert.That(guide.CurrentStepId, Is.EqualTo(MassNavigationShowcaseStepId.TenKFlow));
            Assert.That(simulation.SelectedCount, Is.EqualTo(10_000));
            Assert.That(SelectionContextRuntime.GetCurrentCount(engine.World, engine.GlobalContext), Is.EqualTo(10_000));

            Vector2 destination = new(simulation.SolverWindowCenterXCm + 4_500f, simulation.SolverWindowCenterYCm + 2_000f);
            SubmitRightClickDestination(engine, simulation, destination);

            Assert.That(simulation.AcceptanceDiagnostics.TargetAllocation.HasAllocation, Is.True);
            Assert.That(simulation.AcceptanceDiagnostics.TargetAllocation.SelectedCount, Is.EqualTo(10_000));
            Assert.That(simulation.AcceptanceDiagnostics.TargetAllocation.SlotCount, Is.GreaterThanOrEqualTo(10_000));
            Assert.That(simulation.LastCommandSelectionCount, Is.EqualTo(10_000));
            Assert.That(simulation.AcceptanceDiagnostics.OrderReuse.HasOrder, Is.True);

            Arch.Core.Entity[] selected = SelectionContextRuntime.SnapshotCurrentSelection(engine.World, engine.GlobalContext);
            Assert.That(selected, Has.Length.EqualTo(10_000));
            Assert.That(engine.World.Get<OrderBuffer>(selected[0]).HasActive, Is.True);

            var orderBridge = new MassNavigationOrderBridgeSystem(engine, simulation);
            for (int frame = 0;
                 frame < 32 &&
                 (simulation.NavGroupRuntime.ActiveOrderGroupCount <= 0 ||
                  simulation.AcceptanceDiagnostics.TargetAllocation.SlotCount < 10_000);
                 frame++)
            {
                orderBridge.Update(1f / 60f);
            }

            Assert.That(simulation.NavGroupRuntime.ActiveOrderGroupCount, Is.GreaterThan(0));
            DrainTargetRefresh(simulation, selected);
            Assert.That(simulation.NavGroupRuntime.PendingTargetRefreshCount, Is.EqualTo(0));
            Assert.That(simulation.MassFlow.CountUnitsWithTargets(), Is.GreaterThanOrEqualTo(10_000));
            simulation.AcceptanceDiagnostics.RecordTargetSamples(
                simulation.MassFlow,
                Enumerable.Range(0, 10_000).ToArray());
            Assert.That(simulation.AcceptanceDiagnostics.TargetAllocation.ActualTargetSampleCount, Is.GreaterThan(0));
            Assert.That(simulation.AcceptanceDiagnostics.TargetAllocation.ActualTargetSampleSource, Is.EqualTo("mass_flow_unit_targets_sample"));

            bool advancedFlow = simulation.MassFlow.AdvanceFlowPipeline(simulation.FlowTuning, simulation.FrameIndex);
            Assert.That(advancedFlow, Is.True);

            var refreshedController = new MassNavigationPanelController();
            Assert.That(refreshedController.MountOrSync(engine, simulation), Is.True);
            string text = ExtractUiSceneText((UIRoot)engine.GetService(CoreServiceKeys.UIRoot)!);
            Assert.That(text, Does.Contain("commanded=10000"));
            Assert.That(text, Does.Contain("moving/settled/stuck/waiting="));
            Assert.That(text, Does.Contain("accounted=10000"));
            Assert.That(text, Does.Contain("accounted=10000"));
            Assert.That(text, Does.Not.Contain("moving+settled+stuck explains all units"));
        }

        private static void WaitForMassNavigationAgents(GameEngine engine, int expectedControllableCount)
        {
            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            for (int frame = 0; frame < 30 && simulation.AgentState.ControllableCount < expectedControllableCount; frame++)
            {
                PerformerBlacksmithShowcaseTestHarness.Tick(engine, 1);
            }
        }

        private static void SubmitRightClickDestination(GameEngine engine, MassNavigationSimulationRuntime simulation, Vector2 destination)
        {
            var input = new FrozenInputActionReader();
            var bindings = new InteractionActionBindings();
            engine.SetService(CoreServiceKeys.AuthoritativeInput, input);
            engine.SetService(CoreServiceKeys.InteractionActionBindings, bindings);
            input.SetActionState(
                AuthoritativeGroundPointerHelper.ActionId,
                new Vector3(destination.X, 0f, destination.Y),
                isDown: true,
                pressedThisFrame: false,
                releasedThisFrame: false);
            input.SetActionState(
                bindings.CommandActionId,
                Vector3.One,
                isDown: false,
                pressedThisFrame: true,
                releasedThisFrame: false);

            var bridge = new MassNavigationCommandBridgeSystem(engine, simulation);
            bridge.Update(1f / 60f);
        }

        private static void SelectFirstControllableAgents(
            GameEngine engine,
            MassNavigationSimulationRuntime simulation,
            int requestedCount)
        {
            SelectionRuntime selection = engine.GetService(CoreServiceKeys.SelectionRuntime)
                ?? throw new InvalidOperationException("SelectionRuntime missing.");
            if (!engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? localObj) ||
                localObj is not Arch.Core.Entity owner ||
                !engine.World.IsAlive(owner))
            {
                throw new InvalidOperationException("Local player selection owner missing.");
            }

            int count = Math.Min(requestedCount, simulation.AgentState.ControllableCount);
            Assert.That(count, Is.GreaterThan(0), "MassNavigation controllable agents missing.");
            Arch.Core.Entity[] selected = simulation.AgentState.ControllableAgents.Take(count).ToArray();
            Assert.That(selection.ReplaceSelection(owner, SelectionSetKeys.LivePrimary, selected), Is.True);
            MassNavigationSelectionSync.SyncIfChanged(engine.World, engine.GlobalContext, selection, simulation);
            Assert.That(simulation.SelectedCount, Is.EqualTo(count));
        }

        private static void DrainTargetRefresh(MassNavigationSimulationRuntime simulation, Arch.Core.Entity[] selected)
        {
            int maxRefreshFrames = (int)Math.Ceiling(Math.Max(1, selected.Length) / (double)simulation.NavGroupRuntime.TargetRefreshBudget) * 4;
            for (int frame = 1; frame <= maxRefreshFrames && simulation.NavGroupRuntime.PendingTargetRefreshCount > 0; frame += 2)
            {
                simulation.NavGroupRuntime.UpdateTargets(
                    simulation.MassFlow,
                    simulation.AgentState,
                    selected,
                    frame);
            }
        }

        private static GameEngine CreateMassNavigationEngineWithUi()
        {
            GameEngine engine = PerformerBlacksmithShowcaseTestHarness.CreateEngine(
                "LudotsCoreMod",
                "CoreInputMod",
                "CameraProfilesMod",
                "PerformerBlacksmithShowcaseMod",
                "MassNavigationMod");
            engine.SetService(CoreServiceKeys.UIRoot, new UIRoot(new SkiaUiRenderer()));
            engine.SetService(CoreServiceKeys.UiTextMeasurer, new SkiaTextMeasurer());
            engine.SetService(CoreServiceKeys.UiImageSizeProvider, new SkiaImageSizeProvider());
            return engine;
        }

        private static GameEngine CreateMassNavigationFocusedEntryEngineWithUi(string entryModId)
        {
            GameEngine engine = PerformerBlacksmithShowcaseTestHarness.CreateEngine(
                "LudotsCoreMod",
                "CoreInputMod",
                "CameraProfilesMod",
                "PerformerBlacksmithShowcaseMod",
                "MassNavigationMod",
                entryModId);
            engine.SetService(CoreServiceKeys.UIRoot, new UIRoot(new SkiaUiRenderer()));
            engine.SetService(CoreServiceKeys.UiTextMeasurer, new SkiaTextMeasurer());
            engine.SetService(CoreServiceKeys.UiImageSizeProvider, new SkiaImageSizeProvider());
            return engine;
        }

        private static void SetHeadlessViewport(GameEngine engine, int width, int height)
        {
            object view = engine.GetService(CoreServiceKeys.ViewController)
                ?? throw new InvalidOperationException("ViewController missing.");
            var resolutionProperty = view.GetType().GetProperty(nameof(Ludots.Core.Presentation.Camera.IViewController.Resolution))
                ?? throw new InvalidOperationException("Headless ViewController must expose Resolution.");
            resolutionProperty.SetValue(view, new Vector2(width, height));
        }

        private static (string EntryModId, MassNavigationShowcaseStepId StepId)[] FocusedEntryMods()
        {
            return new[]
            {
                ("MassNavigationU01VisualHeightmapBakeShowcaseMod", MassNavigationShowcaseStepId.VisualHeightmapBake),
                ("MassNavigationU02LogicHeightmapBakeShowcaseMod", MassNavigationShowcaseStepId.LogicHeightmapBake),
                ("MassNavigationU03LayerAreaEditorShowcaseMod", MassNavigationShowcaseStepId.LayerAreaEditor),
                ("MassNavigationU04PathOnlyQueryShowcaseMod", MassNavigationShowcaseStepId.PathOnly),
                ("MassNavigationU05WorldHpaRouteShowcaseMod", MassNavigationShowcaseStepId.WorldHpa),
                ("MassNavigationU06StrategySwitchShowcaseMod", MassNavigationShowcaseStepId.StrategySwitch),
                ("MassNavigationU07OrderReuseShowcaseMod", MassNavigationShowcaseStepId.OrderReuse),
                ("MassNavigationU08TargetAllocationShowcaseMod", MassNavigationShowcaseStepId.TargetAllocation),
                ("MassNavigationU09LayerCostsShowcaseMod", MassNavigationShowcaseStepId.LayerCosts),
                ("MassNavigationU10WaypointAuthoringShowcaseMod", MassNavigationShowcaseStepId.WaypointAuthoring),
                ("MassNavigationU11LargeWorldStreamingShowcaseMod", MassNavigationShowcaseStepId.LargeWorldStreaming),
                ("MassNavigationU12TenKFlowShowcaseMod", MassNavigationShowcaseStepId.TenKFlow),
                ("MassNavigationU13StaticObstacleWorldShowcaseMod", MassNavigationShowcaseStepId.StaticObstacleWorld),
                ("MassNavigationU14PerformanceDebugShowcaseMod", MassNavigationShowcaseStepId.PerformanceDebug),
                ("MassNavigationU15DebugVisualBudgetShowcaseMod", MassNavigationShowcaseStepId.DebugVisualBudget),
                ("MassNavigationU16BakeToolQueryShowcaseMod", MassNavigationShowcaseStepId.BakeToolQuery)
            };
        }

        private static MassNavigationSimulationRuntime RequireSimulation(GameEngine engine)
        {
            return engine.GetService(MassNavigationKeys.SimulationRuntime)
                ?? throw new InvalidOperationException("MassNavigation runtime missing.");
        }

        private static MassNavigationShowcaseGuideRuntime RequireGuide(GameEngine engine)
        {
            return engine.GetService(MassNavigationKeys.ShowcaseGuideRuntime)
                ?? throw new InvalidOperationException("MassNavigation showcase guide missing.");
        }

        private static string[] GetOverlayStrings(ScreenOverlayBuffer overlay)
        {
            return overlay.GetSpan()
                .ToArray()
                .Where(item => item.Kind == ScreenOverlayItemKind.Text)
                .Select(item => overlay.GetString(item.StringId) ?? string.Empty)
                .Where(text => !string.IsNullOrEmpty(text))
                .ToArray();
        }

        private static string ExtractUiSceneText(UIRoot root)
        {
            if (root.Scene == null || root.Scene.Root == null)
            {
                return string.Empty;
            }

            root.Scene.Layout(root.Width, root.Height);
            var builder = new StringBuilder();
            AppendUiNodeText(root.Scene.Root, builder);
            return builder.ToString();
        }

        private static void AppendUiNodeText(UiNode node, StringBuilder builder)
        {
            if (!string.IsNullOrWhiteSpace(node.TextContent))
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.Append(node.TextContent);
            }

            for (int i = 0; i < node.Children.Count; i++)
            {
                AppendUiNodeText(node.Children[i], builder);
            }
        }

        private static void InvokeUiButton(UIRoot root, string label)
        {
            if (root.Scene?.Root == null)
            {
                throw new InvalidOperationException("UI scene is not mounted.");
            }

            if (!TryFindUiButton(root.Scene.Root, label, out UiNode? button))
            {
                throw new InvalidOperationException($"UI button '{label}' not found.");
            }

            UiNode resolvedButton = button ?? throw new InvalidOperationException($"UI button '{label}' resolved to null.");
            root.Scene.Layout(root.Width, root.Height);
            UiEventResult result = root.Scene.Dispatch(new UiPointerEvent(
                UiPointerEventType.Click,
                0,
                resolvedButton.LayoutRect.X + 2f,
                resolvedButton.LayoutRect.Y + 2f,
                resolvedButton.Id));
            if (!result.Handled)
            {
                throw new InvalidOperationException($"UI button '{label}' did not handle click.");
            }
        }

        private static int CountUiButtons(UIRoot root, string label)
        {
            if (root.Scene?.Root == null)
            {
                return 0;
            }

            return CountUiButtons(root.Scene.Root, label);
        }

        private static int CountUiButtons(UiNode node, string label)
        {
            int count = string.Equals(node.TextContent, label, StringComparison.Ordinal) &&
                node.Kind == UiNodeKind.Button
                    ? 1
                    : 0;
            for (int i = 0; i < node.Children.Count; i++)
            {
                count += CountUiButtons(node.Children[i], label);
            }

            return count;
        }

        private static UiNode ResolveFocusedPanelNode(UIRoot root)
        {
            if (root.Scene?.Root == null)
            {
                throw new InvalidOperationException("UI scene is not mounted.");
            }

            UiNode? best = null;
            FindFocusedPanelNode(root.Scene.Root, ref best);
            return best ?? throw new InvalidOperationException("Focused showcase panel node not found.");
        }

        private static void FindFocusedPanelNode(UiNode node, ref UiNode? best)
        {
            if (string.Equals(node.ElementId, "mass-navigation-focused-hud", StringComparison.Ordinal))
            {
                best = node;
                return;
            }

            for (int i = 0; i < node.Children.Count; i++)
            {
                FindFocusedPanelNode(node.Children[i], ref best);
            }
        }

        private static bool TryFindUiButton(UiNode node, string label, out UiNode? button)
        {
            if (string.Equals(node.TextContent, label, StringComparison.Ordinal) &&
                node.Kind == UiNodeKind.Button)
            {
                button = node;
                return true;
            }

            for (int i = 0; i < node.Children.Count; i++)
            {
                if (TryFindUiButton(node.Children[i], label, out button))
                {
                    return true;
                }
            }

            button = null;
            return false;
        }

        private static Vector2 ResolveNavMeshSampleCenter(MassNavigationSimulationRuntime simulation, MassNavigationShowcaseGuideRuntime guide)
        {
            MassNavigationNavMeshGuideSample sample = guide.NavMeshSample;
            MassNavigationBakeDataDiagnostics? bake = simulation.BakeDataDiagnostics;
            if (!sample.Available || bake == null)
            {
                return new Vector2(simulation.SolverWindowCenterXCm, simulation.SolverWindowCenterYCm);
            }

            return new Vector2(
                bake.WorldMinXCm + (sample.ChunkX * bake.MacroChunkSizeXCm) + (bake.MacroChunkSizeXCm * 0.5f),
                bake.WorldMinYCm + (sample.ChunkY * bake.MacroChunkSizeYCm) + (bake.MacroChunkSizeYCm * 0.5f));
        }

        private static Vector2 ResolvePathMidpoint(MassNavigationSimulationRuntime simulation)
        {
            ReadOnlySpan<MassNavigationPathPointSample> points = simulation.AcceptanceDiagnostics.PathOnlyPathPoints;
            if (points.Length > 0)
            {
                MassNavigationPathPointSample sample = points[points.Length / 2];
                return new Vector2(sample.Xcm, sample.Ycm);
            }

            MassNavigationPathOnlyQueryDiagnostics query = simulation.AcceptanceDiagnostics.PathOnlyQuery;
            if (query.StartWorldCm != Vector2.Zero && query.GoalWorldCm != Vector2.Zero)
            {
                return (query.StartWorldCm + query.GoalWorldCm) * 0.5f;
            }

            return new Vector2(simulation.SolverWindowCenterXCm, simulation.SolverWindowCenterYCm);
        }

        private static string DescribePathOnlyQuery(MassNavigationPathOnlyQueryDiagnostics query)
        {
            return $"status={query.Status}; error={query.ErrorCode}; routeState={query.RoutePreviewState}; " +
                $"source={query.QuerySource}; provenance={query.RouteProvenance}; " +
                $"startChunk={query.StartMacroChunkX},{query.StartMacroChunkY}; goalChunk={query.GoalMacroChunkX},{query.GoalMacroChunkY}; " +
                $"start=({query.StartWorldCm.X},{query.StartWorldCm.Y}); goal=({query.GoalWorldCm.X},{query.GoalWorldCm.Y}); " +
                $"points={query.PathPointCount}; macroRoute={query.MacroRouteChunkCount}; expanded={query.ExpandedNodeCount}; touched={query.TouchedTileCount}.";
        }

        private static string BuildLiveNavMeshProbeSummary(
            MassNavigationBakeDataDiagnostics bake,
            NavQueryServiceRegistry navRegistry,
            NavMeshProfileRegistry navProfiles)
        {
            var builder = new StringBuilder();
            MassNavigationBakeDataProfileSummary profile = bake.Profiles.Length > 0
                ? bake.Profiles[0]
                : new MassNavigationBakeDataProfileSummary("Infantry", "GroundLight", 0, "AutoCheapest", 0, 0, 0, 1f, string.Empty, string.Empty, string.Empty, string.Empty);

            if (!navProfiles.TryGetIndex(profile.NavProfileId, out int profileIndex) ||
                !navRegistry.TryGetStore(profile.Layer, profileIndex, out NavTileStore store))
            {
                return $"profile/store missing: layer={profile.Layer} profile={profile.NavProfileId}";
            }

            var query = new NavQueryService(store, profile.Layer, NavAreaCostTable.CreateDefault());
            AppendTileProbe(builder, "min", bake, store, profile.Layer, 0, 0);
            AppendTileProbe(builder, "east-neighbor", bake, store, profile.Layer, Math.Min(1, bake.MacroChunkColumns - 1), 0);
            AppendTileProbe(builder, "south-neighbor", bake, store, profile.Layer, 0, Math.Min(1, bake.MacroChunkRows - 1));
            AppendTileProbe(builder, "center", bake, store, profile.Layer, bake.MacroChunkColumns / 2, bake.MacroChunkRows / 2);
            AppendTileProbe(builder, "max", bake, store, profile.Layer, bake.MacroChunkColumns - 1, bake.MacroChunkRows - 1);

            if (TryGetTile(store, new NavTileId(0, 0, profile.Layer), out NavTile minTile) &&
                TryGetTile(store, new NavTileId(Math.Min(1, bake.MacroChunkColumns - 1), 0, profile.Layer), out NavTile eastTile))
            {
                builder.Append("; min-eastPortalMatches=").Append(CountMatchingPortalOverlaps(minTile, eastTile, NavPortalSide.East));
            }

            if (TryGetTile(store, new NavTileId(0, 0, profile.Layer), out minTile) &&
                TryGetTile(store, new NavTileId(0, Math.Min(1, bake.MacroChunkRows - 1), profile.Layer), out NavTile southTile))
            {
                builder.Append("; min-southPortalMatches=").Append(CountMatchingPortalOverlaps(minTile, southTile, NavPortalSide.South));
            }

            if (TryResolveTileCentroidWorldCm(bake, store, new NavTileId(0, 0, profile.Layer), out Vector2 minWorld) &&
                TryResolveTileCentroidWorldCm(bake, store, new NavTileId(Math.Min(1, bake.MacroChunkColumns - 1), 0, profile.Layer), out Vector2 eastWorld))
            {
                AppendQueryProbe(builder, "min->east", query, minWorld, eastWorld, 16_384);
            }

            if (TryGetTile(store, new NavTileId(0, 0, profile.Layer), out minTile) &&
                TryGetTile(store, new NavTileId(Math.Min(1, bake.MacroChunkColumns - 1), 0, profile.Layer), out eastTile) &&
                TryResolvePortalEndpointWorldCm(bake, minTile, NavPortalSide.East, out Vector2 minEastPortalWorld) &&
                TryResolvePortalEndpointWorldCm(bake, eastTile, NavPortalSide.West, out Vector2 eastWestPortalWorld))
            {
                builder
                    .Append("; min-eastPortalSample=(")
                    .Append(minEastPortalWorld.X)
                    .Append(',')
                    .Append(minEastPortalWorld.Y)
                    .Append(")->(")
                    .Append(eastWestPortalWorld.X)
                    .Append(',')
                    .Append(eastWestPortalWorld.Y)
                    .Append("); nearestLocalD=")
                    .Append(ComputeNearestTriangleDistanceCm(minTile, minEastPortalWorld, bake))
                    .Append(',')
                    .Append(ComputeNearestTriangleDistanceCm(eastTile, eastWestPortalWorld, bake));
                AppendQueryProbe(builder, "minPortal->eastPortal", query, minEastPortalWorld, eastWestPortalWorld, 16_384);
            }

            if (TryResolveTileCentroidWorldCm(bake, store, new NavTileId(0, 0, profile.Layer), out minWorld) &&
                TryResolveTileCentroidWorldCm(bake, store, new NavTileId(bake.MacroChunkColumns - 1, bake.MacroChunkRows - 1, profile.Layer), out Vector2 maxWorld))
            {
                AppendQueryProbe(builder, "min->max", query, minWorld, maxWorld, 262_144);
            }

            AppendSpanProbe(builder, bake, store, query, profile.Layer, 0, 0, 1, 0);
            AppendSpanProbe(builder, bake, store, query, profile.Layer, 0, 0, 2, 0);
            AppendSpanProbe(builder, bake, store, query, profile.Layer, 0, 0, 4, 0);
            AppendSpanProbe(builder, bake, store, query, profile.Layer, 0, 0, 8, 0);
            AppendSpanProbe(builder, bake, store, query, profile.Layer, 0, 0, 16, 0);
            AppendSpanProbe(builder, bake, store, query, profile.Layer, 0, 0, 32, 0);
            AppendSpanProbe(builder, bake, store, query, profile.Layer, 0, 0, 64, 0);
            AppendSpanProbe(builder, bake, store, query, profile.Layer, 0, 0, 128, 0);
            AppendSpanProbe(builder, bake, store, query, profile.Layer, 0, 0, 255, 0);
            AppendSpanProbe(builder, bake, store, query, profile.Layer, 0, 0, 255, 255);
            AppendPortalSpanProbe(builder, bake, store, query, profile.Layer, 0, 0, 2, 0, NavPortalSide.East, NavPortalSide.West);
            AppendPortalSpanProbe(builder, bake, store, query, profile.Layer, 0, 0, 8, 0, NavPortalSide.East, NavPortalSide.West);
            AppendPortalSpanProbe(builder, bake, store, query, profile.Layer, 0, 0, 255, 0, NavPortalSide.East, NavPortalSide.West);
            AppendPortalSpanProbe(builder, bake, store, query, profile.Layer, 0, 0, 0, 8, NavPortalSide.South, NavPortalSide.North);
            AppendPortalSpanProbe(builder, bake, store, query, profile.Layer, 0, 0, 0, 255, NavPortalSide.South, NavPortalSide.North);
            AppendPortalSpanProbe(builder, bake, store, query, profile.Layer, 0, 0, 255, 255, NavPortalSide.East, NavPortalSide.West);
            AppendPortalSpanProbe(builder, bake, store, query, profile.Layer, 0, 0, 255, 255, NavPortalSide.South, NavPortalSide.North);

            if (TryResolveTileCentroidWorldCm(bake, store, new NavTileId(Math.Max(0, bake.MacroChunkColumns / 2 - 2), Math.Max(0, bake.MacroChunkRows / 2 - 2), profile.Layer), out Vector2 localStart) &&
                TryResolveTileCentroidWorldCm(bake, store, new NavTileId(Math.Min(bake.MacroChunkColumns - 1, bake.MacroChunkColumns / 2 + 2), Math.Min(bake.MacroChunkRows - 1, bake.MacroChunkRows / 2 + 2), profile.Layer), out Vector2 localGoal))
            {
                AppendQueryProbe(builder, "center-smoke", query, localStart, localGoal, 16_384);
            }

            return builder.ToString();
        }

        private static void AppendTileProbe(
            StringBuilder builder,
            string label,
            MassNavigationBakeDataDiagnostics bake,
            NavTileStore store,
            int layer,
            int chunkX,
            int chunkY)
        {
            if (!TryGetTile(store, new NavTileId(chunkX, chunkY, layer), out NavTile tile))
            {
                builder.Append(label).Append("=missing;");
                return;
            }

            builder
                .Append(label)
                .Append('=')
                .Append(tile.TileId)
                .Append(":tri")
                .Append(tile.TriangleCount)
                .Append("/portal")
                .Append(tile.Portals.Length)
                .Append("/origin")
                .Append(tile.OriginXcm)
                .Append(',')
                .Append(tile.OriginZcm)
                .Append(';');
        }

        private static void AppendQueryProbe(
            StringBuilder builder,
            string label,
            NavQueryService query,
            Vector2 start,
            Vector2 goal,
            int maxPortals)
        {
            NavPathResult path = query.TryFindPath(
                (int)MathF.Round(start.X),
                (int)MathF.Round(start.Y),
                (int)MathF.Round(goal.X),
                (int)MathF.Round(goal.Y),
                maxPortals);
            builder
                .Append("; query ")
                .Append(label)
                .Append('=')
                .Append(path.Status)
                .Append("/points")
                .Append(path.PathXcm.Length);
        }

        private static void AppendSpanProbe(
            StringBuilder builder,
            MassNavigationBakeDataDiagnostics bake,
            NavTileStore store,
            NavQueryService query,
            int layer,
            int startX,
            int startY,
            int goalX,
            int goalY)
        {
            if (!TryResolveTileCentroidWorldCm(bake, store, new NavTileId(startX, startY, layer), out Vector2 start) ||
                !TryResolveTileCentroidWorldCm(bake, store, new NavTileId(goalX, goalY, layer), out Vector2 goal))
            {
                builder.Append("; span ").Append(startX).Append(',').Append(startY).Append("->").Append(goalX).Append(',').Append(goalY).Append("=sample_missing");
                return;
            }

            AppendQueryProbe(builder, $"span {startX},{startY}->{goalX},{goalY}", query, start, goal, 262_144);
        }

        private static void AppendPortalSpanProbe(
            StringBuilder builder,
            MassNavigationBakeDataDiagnostics bake,
            NavTileStore store,
            NavQueryService query,
            int layer,
            int startX,
            int startY,
            int goalX,
            int goalY,
            NavPortalSide startSide,
            NavPortalSide goalSide)
        {
            if (!TryGetTile(store, new NavTileId(startX, startY, layer), out NavTile startTile) ||
                !TryGetTile(store, new NavTileId(goalX, goalY, layer), out NavTile goalTile) ||
                !TryResolvePortalEndpointWorldCm(bake, startTile, startSide, out Vector2 start) ||
                !TryResolvePortalEndpointWorldCm(bake, goalTile, goalSide, out Vector2 goal))
            {
                builder.Append("; portalSpan ").Append(startX).Append(',').Append(startY).Append("->").Append(goalX).Append(',').Append(goalY).Append("=sample_missing");
                return;
            }

            AppendQueryProbe(builder, $"portalSpan {startX},{startY}->{goalX},{goalY}/{startSide}->{goalSide}", query, start, goal, 262_144);
        }

        private static bool TryResolveTileCentroidWorldCm(
            MassNavigationBakeDataDiagnostics bake,
            NavTileStore store,
            NavTileId tileId,
            out Vector2 worldCm)
        {
            worldCm = default;
            if (!TryGetTile(store, tileId, out NavTile tile) || tile.TriangleCount <= 0)
            {
                return false;
            }

            int bestTri = ResolveLargestTriangle(tile);
            if (bestTri < 0)
            {
                return false;
            }

            int a = tile.TriA[bestTri];
            int b = tile.TriB[bestTri];
            int c = tile.TriC[bestTri];
            MassNavigationNavMeshRuntimeCoordinateMapper mapper = MassNavigationNavMeshRuntimeCoordinateMapper.CreateFromNavTile(bake, tile);
            worldCm = mapper.BakedTileLocalToWorldCm(
                tile,
                (tile.VertexXcm[a] + tile.VertexXcm[b] + tile.VertexXcm[c]) / 3,
                (tile.VertexZcm[a] + tile.VertexZcm[b] + tile.VertexZcm[c]) / 3);
            return float.IsFinite(worldCm.X) && float.IsFinite(worldCm.Y);
        }

        private static bool TryResolvePortalEndpointWorldCm(
            MassNavigationBakeDataDiagnostics bake,
            NavTile tile,
            NavPortalSide side,
            out Vector2 worldCm)
        {
            worldCm = default;
            for (int i = 0; i < tile.Portals.Length; i++)
            {
                NavBorderPortal portal = tile.Portals[i];
                if (portal.Side != side)
                {
                    continue;
                }

                int localX = (portal.LeftXcm + portal.RightXcm) / 2;
                int localZ = (portal.LeftZcm + portal.RightZcm) / 2;
                int inset = Math.Clamp(Math.Max(1, Math.Max(localX, localZ) / 32), 32, 512);
                switch (side)
                {
                    case NavPortalSide.West:
                        localX += inset;
                        break;
                    case NavPortalSide.East:
                        localX -= inset;
                        break;
                    case NavPortalSide.North:
                        localZ += inset;
                        break;
                    case NavPortalSide.South:
                        localZ -= inset;
                        break;
                }

                MassNavigationNavMeshRuntimeCoordinateMapper mapper = MassNavigationNavMeshRuntimeCoordinateMapper.CreateFromNavTile(bake, tile);
                worldCm = mapper.BakedTileLocalToWorldCm(tile, localX, localZ);
                return float.IsFinite(worldCm.X) && float.IsFinite(worldCm.Y);
            }

            return false;
        }

        private static long ComputeNearestTriangleDistanceCm(
            NavTile tile,
            Vector2 worldCm,
            MassNavigationBakeDataDiagnostics bake)
        {
            MassNavigationNavMeshRuntimeCoordinateMapper mapper = MassNavigationNavMeshRuntimeCoordinateMapper.CreateFromNavTile(bake, tile);
            int bakedAbsX = mapper.WorldToBakedAbsoluteXcm(worldCm.X);
            int bakedAbsZ = mapper.WorldToBakedAbsoluteYcm(worldCm.Y);
            int localX = bakedAbsX - tile.OriginXcm;
            int localZ = bakedAbsZ - tile.OriginZcm;
            long bestD2 = long.MaxValue;
            for (int i = 0; i < tile.TriangleCount; i++)
            {
                int a = tile.TriA[i];
                int b = tile.TriB[i];
                int c = tile.TriC[i];
                long d2 = DistanceSquaredToTriangle(
                    localX,
                    localZ,
                    tile.VertexXcm[a],
                    tile.VertexZcm[a],
                    tile.VertexXcm[b],
                    tile.VertexZcm[b],
                    tile.VertexXcm[c],
                    tile.VertexZcm[c]);
                bestD2 = Math.Min(bestD2, d2);
            }

            return bestD2 == long.MaxValue ? -1 : DeterministicLongSqrt(bestD2);
        }

        private static long DistanceSquaredToTriangle(
            int px,
            int pz,
            int ax,
            int az,
            int bx,
            int bz,
            int cx,
            int cz)
        {
            if (PointInTriangle(px, pz, ax, az, bx, bz, cx, cz))
            {
                return 0;
            }

            return Math.Min(
                DistanceSquaredToSegment(px, pz, ax, az, bx, bz),
                Math.Min(
                    DistanceSquaredToSegment(px, pz, bx, bz, cx, cz),
                    DistanceSquaredToSegment(px, pz, cx, cz, ax, az)));
        }

        private static bool PointInTriangle(
            int px,
            int pz,
            int ax,
            int az,
            int bx,
            int bz,
            int cx,
            int cz)
        {
            long area = Orient2D(ax, az, bx, bz, cx, cz);
            if (area == 0)
            {
                return false;
            }

            long ab = Orient2D(ax, az, bx, bz, px, pz);
            long bc = Orient2D(bx, bz, cx, cz, px, pz);
            long ca = Orient2D(cx, cz, ax, az, px, pz);
            return area > 0
                ? ab >= 0 && bc >= 0 && ca >= 0
                : ab <= 0 && bc <= 0 && ca <= 0;
        }

        private static long DistanceSquaredToSegment(int px, int pz, int ax, int az, int bx, int bz)
        {
            long dx = bx - ax;
            long dz = bz - az;
            long len2 = (dx * dx) + (dz * dz);
            if (len2 <= 0)
            {
                return DistanceSquared(px, pz, ax, az);
            }

            long pax = px - ax;
            long paz = pz - az;
            long dot = (pax * dx) + (paz * dz);
            if (dot <= 0)
            {
                return DistanceSquared(px, pz, ax, az);
            }

            if (dot >= len2)
            {
                return DistanceSquared(px, pz, bx, bz);
            }

            long cross = (pax * dz) - (paz * dx);
            return DivRound(cross * cross, len2);
        }

        private static long DistanceSquared(int ax, int az, int bx, int bz)
        {
            long dx = (long)bx - ax;
            long dz = (long)bz - az;
            return (dx * dx) + (dz * dz);
        }

        private static long DivRound(long numerator, long denominator)
        {
            if (denominator <= 0)
            {
                return 0;
            }

            return numerator >= 0
                ? (numerator + (denominator / 2)) / denominator
                : (numerator - (denominator / 2)) / denominator;
        }

        private static long DeterministicLongSqrt(long n)
        {
            if (n <= 0)
            {
                return 0;
            }

            long x = n;
            long y = (x + 1) >> 1;
            while (y < x)
            {
                x = y;
                y = (x + n / x) >> 1;
            }

            return x;
        }

        private static int ResolveLargestTriangle(NavTile tile)
        {
            int bestTri = -1;
            long bestArea2 = 0;
            for (int i = 0; i < tile.TriangleCount; i++)
            {
                int a = tile.TriA[i];
                int b = tile.TriB[i];
                int c = tile.TriC[i];
                long area2 = Math.Abs(Orient2D(
                    tile.VertexXcm[a],
                    tile.VertexZcm[a],
                    tile.VertexXcm[b],
                    tile.VertexZcm[b],
                    tile.VertexXcm[c],
                    tile.VertexZcm[c]));
                if (area2 > bestArea2)
                {
                    bestArea2 = area2;
                    bestTri = i;
                }
            }

            return bestTri;
        }

        private static long Orient2D(int ax, int az, int bx, int bz, int cx, int cz)
        {
            return ((long)bx - ax) * ((long)cz - az) - (((long)bz - az) * ((long)cx - ax));
        }

        private static bool TryGetTile(NavTileStore store, NavTileId id, out NavTile tile)
        {
            try
            {
                tile = store.GetOrLoad(id);
                return true;
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is InvalidOperationException)
            {
                tile = null!;
                return false;
            }
        }

        private static int CountMatchingPortalOverlaps(NavTile tile, NavTile neighbor, NavPortalSide side)
        {
            int count = 0;
            NavPortalSide opposite = GetOppositePortalSide(side);
            for (int i = 0; i < tile.Portals.Length; i++)
            {
                NavBorderPortal portal = tile.Portals[i];
                if (portal.Side != side)
                {
                    continue;
                }

                GetPortalInterval(portal, out int start, out int end);
                for (int j = 0; j < neighbor.Portals.Length; j++)
                {
                    NavBorderPortal candidate = neighbor.Portals[j];
                    if (candidate.Side != opposite)
                    {
                        continue;
                    }

                    GetPortalInterval(candidate, out int candidateStart, out int candidateEnd);
                    if (Math.Max(start, candidateStart) < Math.Min(end, candidateEnd))
                    {
                        count++;
                        break;
                    }
                }
            }

            return count;
        }

        private static NavPortalSide GetOppositePortalSide(NavPortalSide side)
        {
            return side switch
            {
                NavPortalSide.West => NavPortalSide.East,
                NavPortalSide.East => NavPortalSide.West,
                NavPortalSide.North => NavPortalSide.South,
                NavPortalSide.South => NavPortalSide.North,
                _ => side
            };
        }

        private static void GetPortalInterval(NavBorderPortal portal, out int start, out int end)
        {
            if (portal.Side == NavPortalSide.West || portal.Side == NavPortalSide.East)
            {
                start = Math.Min(portal.V0, portal.V1);
                end = Math.Max(portal.V0, portal.V1);
                return;
            }

            start = Math.Min(portal.U0, portal.U1);
            end = Math.Max(portal.U0, portal.U1);
        }

        private static Vector2 ResolveWorldPointInChunk(
            MassNavigationBakeDataDiagnostics bake,
            int chunkX,
            int chunkY,
            float offsetXCm,
            float offsetYCm)
        {
            int x = Math.Clamp(chunkX, 0, Math.Max(0, bake.MacroChunkColumns - 1));
            int y = Math.Clamp(chunkY, 0, Math.Max(0, bake.MacroChunkRows - 1));
            float localX = Math.Clamp(offsetXCm, 500f, Math.Max(500f, bake.MacroChunkSizeXCm - 500f));
            float localY = Math.Clamp(offsetYCm, 500f, Math.Max(500f, bake.MacroChunkSizeYCm - 500f));
            return new Vector2(
                bake.WorldMinXCm + (x * bake.MacroChunkSizeXCm) + localX,
                bake.WorldMinYCm + (y * bake.MacroChunkSizeYCm) + localY);
        }

        private static Vector2 ResolveWorldPointInNavTile(
            MassNavigationBakeDataDiagnostics bake,
            NavTile tile,
            float localXCm,
            float localYCm)
        {
            MassNavigationNavMeshRuntimeCoordinateMapper mapper = MassNavigationNavMeshRuntimeCoordinateMapper.CreateFromNavTile(bake, tile);
            return mapper.BakedTileLocalToWorldCm(
                tile,
                (int)MathF.Round(localXCm),
                (int)MathF.Round(localYCm));
        }

        private static RuntimeObstaclePathFixture ResolveRuntimeObstaclePathFixture(
            MassNavigationBakeDataDiagnostics bake,
            MassNavigationShowcaseGuideRuntime guide,
            NavTileStore store,
            int layer)
        {
            var query = new NavQueryService(store, layer, NavAreaCostTable.CreateDefault());
            foreach (NavTileId id in BuildRuntimeObstacleCandidateTileIds(bake, guide, layer))
            {
                if (!TryGetTile(store, id, out NavTile tile) ||
                    tile.TriangleCount <= 0 ||
                    !TryBuildRuntimeObstaclePathFixture(bake, tile, query, out RuntimeObstaclePathFixture fixture))
                {
                    continue;
                }

                return fixture;
            }

            throw new InvalidOperationException("No stable runtime obstacle path fixture could be resolved from the live NavMesh.");
        }

        private static IEnumerable<NavTileId> BuildRuntimeObstacleCandidateTileIds(
            MassNavigationBakeDataDiagnostics bake,
            MassNavigationShowcaseGuideRuntime guide,
            int layer)
        {
            var emitted = new HashSet<long>();
            void Add(List<NavTileId> ids, int x, int y)
            {
                if (x < 0 || y < 0 || x >= bake.MacroChunkColumns || y >= bake.MacroChunkRows)
                {
                    return;
                }

                long key = (((long)x) << 32) ^ (uint)y;
                if (emitted.Add(key))
                {
                    ids.Add(new NavTileId(x, y, layer));
                }
            }

            var result = new List<NavTileId>(96);
            if (guide.NavMeshSample.Available)
            {
                Add(result, guide.NavMeshSample.ChunkX, guide.NavMeshSample.ChunkY);
            }

            int centerX = bake.MacroChunkColumns / 2;
            int centerY = bake.MacroChunkRows / 2;
            Add(result, centerX, centerY);
            if (guide.NavMeshCoverage.Available)
            {
                Add(result, guide.NavMeshCoverage.ActiveWindowMinChunkX, guide.NavMeshCoverage.ActiveWindowMinChunkY);
                Add(result, guide.NavMeshCoverage.ActiveWindowMaxChunkX, guide.NavMeshCoverage.ActiveWindowMinChunkY);
                Add(result, guide.NavMeshCoverage.ActiveWindowMinChunkX, guide.NavMeshCoverage.ActiveWindowMaxChunkY);
                Add(result, guide.NavMeshCoverage.ActiveWindowMaxChunkX, guide.NavMeshCoverage.ActiveWindowMaxChunkY);
                Add(
                    result,
                    (guide.NavMeshCoverage.ActiveWindowMinChunkX + guide.NavMeshCoverage.ActiveWindowMaxChunkX) / 2,
                    (guide.NavMeshCoverage.ActiveWindowMinChunkY + guide.NavMeshCoverage.ActiveWindowMaxChunkY) / 2);
            }

            for (int radius = 1; radius <= 8; radius++)
            {
                Add(result, centerX + radius, centerY);
                Add(result, centerX - radius, centerY);
                Add(result, centerX, centerY + radius);
                Add(result, centerX, centerY - radius);
                Add(result, centerX + radius, centerY + radius);
                Add(result, centerX - radius, centerY - radius);
            }

            return result;
        }

        private static bool TryBuildRuntimeObstaclePathFixture(
            MassNavigationBakeDataDiagnostics bake,
            NavTile tile,
            NavQueryService query,
            out RuntimeObstaclePathFixture fixture)
        {
            fixture = default;
            if (tile.VertexCount == 0 || tile.TriangleCount == 0)
            {
                return false;
            }

            int minX = tile.VertexXcm.Min();
            int maxX = tile.VertexXcm.Max();
            int minZ = tile.VertexZcm.Min();
            int maxZ = tile.VertexZcm.Max();
            int width = maxX - minX;
            int height = maxZ - minZ;
            if (width < 2_400 || height < 2_400)
            {
                return false;
            }

            int midZ = minZ + (height / 2);
            Vector2 start = ResolveWorldPointInNavTile(bake, tile, minX + (width * 30f / 100f), midZ);
            Vector2 goal = ResolveWorldPointInNavTile(bake, tile, minX + (width * 70f / 100f), midZ);
            Vector2[] polygon =
            {
                ResolveWorldPointInNavTile(bake, tile, minX + (width * 46f / 100f), minZ + (height * 42f / 100f)),
                ResolveWorldPointInNavTile(bake, tile, minX + (width * 54f / 100f), minZ + (height * 42f / 100f)),
                ResolveWorldPointInNavTile(bake, tile, minX + (width * 54f / 100f), minZ + (height * 58f / 100f)),
                ResolveWorldPointInNavTile(bake, tile, minX + (width * 46f / 100f), minZ + (height * 58f / 100f)),
            };

            NavPathResult path = query.TryFindPath(
                (int)MathF.Round(start.X),
                (int)MathF.Round(start.Y),
                (int)MathF.Round(goal.X),
                (int)MathF.Round(goal.Y),
                maxPortals: 16_384);
            if (path.Status != NavPathStatus.Ok || path.PathXcm.Length < 2)
            {
                return false;
            }

            MassNavigationPathPointSample[] points = new MassNavigationPathPointSample[path.PathXcm.Length];
            for (int i = 0; i < points.Length; i++)
            {
                points[i] = new MassNavigationPathPointSample(path.PathXcm[i], path.PathZcm[i]);
            }

            if (!PathEntersPolygonInterior(points, polygon))
            {
                return false;
            }

            fixture = new RuntimeObstaclePathFixture(
                tile,
                start,
                goal,
                polygon,
                $"tile={tile.TileId};localBounds={minX},{minZ}->{maxX},{maxZ};beforePoints={path.PathXcm.Length}");
            return true;
        }

        private static void AssertNavMeshSampleEdgesUseWorldCoordinates(
            MassNavigationBakeDataDiagnostics bake,
            MassNavigationShowcaseGuideRuntime guide,
            NavTile tile)
        {
            Assert.That(guide.NavMeshSample.TriangleEdges.Length, Is.GreaterThan(0));
            Assert.That(tile.VertexCount, Is.GreaterThan(0));

            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;
            foreach (MassNavigationGuideSegment edge in guide.NavMeshSample.TriangleEdges)
            {
                minX = MathF.Min(minX, MathF.Min(edge.Axcm, edge.Bxcm));
                minY = MathF.Min(minY, MathF.Min(edge.Aycm, edge.Bycm));
                maxX = MathF.Max(maxX, MathF.Max(edge.Axcm, edge.Bxcm));
                maxY = MathF.Max(maxY, MathF.Max(edge.Aycm, edge.Bycm));
            }

            int localMinX = tile.VertexXcm.Min();
            int localMinY = tile.VertexZcm.Min();
            int localMaxX = tile.VertexXcm.Max();
            int localMaxY = tile.VertexZcm.Max();
            MassNavigationNavMeshRuntimeCoordinateMapper mapper = MassNavigationNavMeshRuntimeCoordinateMapper.CreateFromNavTile(bake, tile);
            Vector2 expectedMin = mapper.BakedTileLocalToWorldCm(tile, localMinX, localMinY);
            Vector2 expectedMax = mapper.BakedTileLocalToWorldCm(tile, localMaxX, localMaxY);
            float expectedMinX = MathF.Min(expectedMin.X, expectedMax.X);
            float expectedMinY = MathF.Min(expectedMin.Y, expectedMax.Y);
            float expectedMaxX = MathF.Max(expectedMin.X, expectedMax.X);
            float expectedMaxY = MathF.Max(expectedMin.Y, expectedMax.Y);
            const float toleranceCm = 8f;

            Assert.That(minX, Is.GreaterThanOrEqualTo(expectedMinX - toleranceCm));
            Assert.That(minY, Is.GreaterThanOrEqualTo(expectedMinY - toleranceCm));
            Assert.That(maxX, Is.LessThanOrEqualTo(expectedMaxX + toleranceCm));
            Assert.That(maxY, Is.LessThanOrEqualTo(expectedMaxY + toleranceCm));
        }

        private static void AssertNavMeshCoverageMatchesBakeDiagnostics(
            MassNavigationSimulationRuntime simulation,
            MassNavigationShowcaseGuideRuntime guide)
        {
            MassNavigationBakeDataDiagnostics bake = simulation.BakeDataDiagnostics
                ?? throw new InvalidOperationException("Mass navigation bake diagnostics missing.");
            MassNavigationNavMeshCoverageGuide coverage = guide.NavMeshCoverage;
            Assert.That(coverage.Available, Is.True);
            Assert.That(coverage.WorldChunkCount, Is.EqualTo(bake.MacroChunkCount));
            Assert.That(coverage.TargetChunkCount, Is.EqualTo(coverage.ActiveWindowChunkCount));
            Assert.That(coverage.IsPartialCoverage, Is.EqualTo(coverage.TargetChunkCount < coverage.WorldChunkCount));
            Assert.That(coverage.TotalExpectedTileBakes, Is.EqualTo(coverage.TargetChunkCount * coverage.LayerCount * coverage.ProfileCount));
            Assert.That(coverage.TotalBakedTiles, Is.EqualTo(coverage.TotalExpectedTileBakes));
        }

        private static void AssertActiveWindowEdgesIncludeNavTile(
            MassNavigationBakeDataDiagnostics bake,
            MassNavigationShowcaseGuideRuntime guide,
            NavTile tile,
            string stage)
        {
            Assert.That(tile.TriangleCount, Is.GreaterThan(0), stage);
            Assert.That(tile.VertexCount, Is.GreaterThan(0), stage);

            const float toleranceCm = 8f;
            MassNavigationNavMeshRuntimeCoordinateMapper mapper = MassNavigationNavMeshRuntimeCoordinateMapper.CreateFromNavTile(bake, tile);
            Vector2 minWorld = mapper.BakedTileLocalToWorldCm(tile, tile.VertexXcm.Min(), tile.VertexZcm.Min());
            Vector2 maxWorld = mapper.BakedTileLocalToWorldCm(tile, tile.VertexXcm.Max(), tile.VertexZcm.Max());
            float minX = MathF.Min(minWorld.X, maxWorld.X) - toleranceCm;
            float minY = MathF.Min(minWorld.Y, maxWorld.Y) - toleranceCm;
            float maxX = MathF.Max(minWorld.X, maxWorld.X) + toleranceCm;
            float maxY = MathF.Max(minWorld.Y, maxWorld.Y) + toleranceCm;
            int matchedEdges = 0;

            foreach (MassNavigationGuideSegment edge in guide.ActiveWindowNavMeshEdges)
            {
                if (PointInside(edge.Axcm, edge.Aycm, minX, minY, maxX, maxY) &&
                    PointInside(edge.Bxcm, edge.Bycm, minX, minY, maxX, maxY))
                {
                    matchedEdges++;
                }
            }

            Assert.That(matchedEdges, Is.GreaterThan(0),
                $"Active-window NavMesh edges must include tile {tile.TileId} {stage}.");
        }

        private static bool PointInside(
            float x,
            float y,
            float minX,
            float minY,
            float maxX,
            float maxY)
        {
            return x >= minX &&
                x <= maxX &&
                y >= minY &&
                y <= maxY;
        }

        private static void AssertPathEntersPolygonInterior(
            IReadOnlyList<MassNavigationPathPointSample> path,
            IReadOnlyList<Vector2> polygon,
            string stage)
        {
            Assert.That(PathEntersPolygonInterior(path, polygon), Is.True,
                $"Path must cross the authored obstacle polygon {stage}; otherwise this test is not proving runtime obstacle invalidation.");
        }

        private static void AssertPathDoesNotEnterPolygonInterior(
            IReadOnlyList<MassNavigationPathPointSample> path,
            IReadOnlyList<Vector2> polygon,
            string stage)
        {
            Assert.That(path, Has.Count.GreaterThanOrEqualTo(2), stage);
            Assert.That(PathEntersPolygonInterior(path, polygon), Is.False,
                $"Path still enters the authored obstacle polygon {stage}.");
        }

        private static bool PathEntersPolygonInterior(
            IReadOnlyList<MassNavigationPathPointSample> path,
            IReadOnlyList<Vector2> polygon)
        {
            if (path.Count < 2 || polygon.Count < 3)
            {
                return false;
            }

            for (int i = 1; i < path.Count; i++)
            {
                int ax = path[i - 1].Xcm;
                int ay = path[i - 1].Ycm;
                int bx = path[i].Xcm;
                int by = path[i].Ycm;
                for (int sample = 1; sample < 64; sample++)
                {
                    float t = sample / 64f;
                    float x = ax + ((bx - ax) * t);
                    float y = ay + ((by - ay) * t);
                    if (PointInPolygonInterior(x, y, polygon))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool PointInPolygonInterior(float xcm, float ycm, IReadOnlyList<Vector2> polygon)
        {
            bool inside = false;
            int j = polygon.Count - 1;
            for (int i = 0; i < polygon.Count; j = i++)
            {
                float xi = polygon[i].X;
                float yi = polygon[i].Y;
                float xj = polygon[j].X;
                float yj = polygon[j].Y;
                if ((yi > ycm) == (yj > ycm))
                {
                    continue;
                }

                double xIntersection = (double)(xj - xi) * (ycm - yi) / (yj - yi) + xi;
                if (xcm < xIntersection)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private readonly record struct RuntimeObstaclePathFixture(
            NavTile Tile,
            Vector2 StartWorldCm,
            Vector2 GoalWorldCm,
            Vector2[] PolygonWorldCm,
            string Source);

        private static void AssertRuntimeDirtyTilesUseNavTileWorldBounds(
            MassNavigationBakeDataDiagnostics bake,
            IReadOnlyList<MassNavigationRuntimeDirtyChunk> dirtyTiles,
            NavTile editedTile)
        {
            Assert.That(dirtyTiles, Is.Not.Empty);
            foreach (MassNavigationRuntimeDirtyChunk dirtyTile in dirtyTiles)
            {
                Assert.That(dirtyTile.Grid, Is.EqualTo(MassNavigationRuntimeDirtyChunkGrid.NavTile));
                Assert.That(dirtyTile.HasWorldBounds, Is.True);
                Assert.That(dirtyTile.MinWorldXCm, Is.GreaterThanOrEqualTo(bake.WorldMinXCm));
                Assert.That(dirtyTile.MinWorldYCm, Is.GreaterThanOrEqualTo(bake.WorldMinYCm));
                Assert.That(dirtyTile.SizeXCm, Is.GreaterThan(0));
                Assert.That(dirtyTile.SizeYCm, Is.GreaterThan(0));
            }

            MassNavigationNavMeshRuntimeCoordinateMapper mapper = MassNavigationNavMeshRuntimeCoordinateMapper.CreateFromNavTile(bake, editedTile);
            int expectedMinX = mapper.TileMinWorldXcm(editedTile.TileId.ChunkX);
            int expectedMinY = mapper.TileMinWorldYcm(editedTile.TileId.ChunkY);
            Assert.That(dirtyTiles.Any(dirtyTile =>
                    dirtyTile.X == editedTile.TileId.ChunkX &&
                    dirtyTile.Y == editedTile.TileId.ChunkY &&
                    dirtyTile.MinWorldXCm == expectedMinX &&
                    dirtyTile.MinWorldYCm == expectedMinY),
                Is.True,
                "Runtime dirty tile markers must use the same WorldMin + NavTile origin coordinate system as the mesh they invalidate.");
        }

        private static float ResolvePathCameraDistanceCm(MassNavigationSimulationRuntime simulation)
        {
            MassNavigationPathOnlyQueryDiagnostics query = simulation.AcceptanceDiagnostics.PathOnlyQuery;
            if (query.StartWorldCm == Vector2.Zero || query.GoalWorldCm == Vector2.Zero)
            {
                return 18_000f;
            }

            float span = Vector2.Distance(query.StartWorldCm, query.GoalWorldCm);
            return Math.Clamp(span * 0.85f, 18_000f, 60_000f);
        }

        private static string DumpStrings(string[] strings)
        {
            return string.Join(Environment.NewLine, strings);
        }

        private static int SolveAndRelease(
            IPathService pathService,
            PathStore pathStore,
            PathRequest request)
        {
            Assert.That(pathService.TrySolve(in request, out PathResult result), Is.True);
            Assert.That(result.Status, Is.EqualTo(PathStatus.Found));
            Assert.That(result.Handle.IsValid, Is.True);
            try
            {
                int[] xs = new int[pathStore.MaxPointsPerPath];
                int[] ys = new int[pathStore.MaxPointsPerPath];
                Assert.That(pathService.TryCopyPath(in result.Handle, xs, ys, out int count), Is.True);
                return count;
            }
            finally
            {
                if (pathStore.IsAlive(result.Handle))
                {
                    pathStore.Release(result.Handle);
                }
            }
        }

        private static InputConfigRoot CreateMinimapInputConfig()
        {
            var bindings = new InteractionActionBindings();
            return new InputConfigRoot
            {
                Actions =
                {
                    new InputActionDef { Id = bindings.PointerPositionActionId, Name = "Pointer", Type = InputActionType.Axis2D },
                    new InputActionDef { Id = bindings.ConfirmActionId, Name = "Confirm", Type = InputActionType.Button },
                    new InputActionDef { Id = bindings.CommandActionId, Name = "Command", Type = InputActionType.Button },
                    new InputActionDef { Id = bindings.CancelActionId, Name = "Cancel", Type = InputActionType.Button },
                    new InputActionDef { Id = MinimapInputActions.Zoom, Name = "Minimap Zoom", Type = InputActionType.Axis1D },
                    new InputActionDef { Id = MinimapInputActions.Toggle, Name = "Minimap Toggle", Type = InputActionType.Button },
                    new InputActionDef { Id = MinimapInputActions.TogglePreset, Name = "Minimap Preset", Type = InputActionType.Button },
                    new InputActionDef { Id = MinimapInputActions.ToggleRotateWithCamera, Name = "Minimap Rotate", Type = InputActionType.Button },
                    new InputActionDef { Id = MinimapInputActions.ZoomIn, Name = "Minimap Zoom In", Type = InputActionType.Button },
                    new InputActionDef { Id = MinimapInputActions.ZoomOut, Name = "Minimap Zoom Out", Type = InputActionType.Button },
                    new InputActionDef { Id = MinimapInputActions.Pan, Name = "Minimap Pan", Type = InputActionType.Axis2D },
                    new InputActionDef { Id = MinimapInputActions.CenterOnSelection, Name = "Minimap Center", Type = InputActionType.Button },
                },
                Contexts =
                {
                    new InputContextDef { Id = "test", Name = "test", Priority = 1 }
                }
            };
        }

        private static void SetPointerPick(
            FrozenInputActionReader input,
            AuthoritativePointerButtonSnapshot pointerButtons,
            string actionId,
            Vector2 worldCm)
        {
            input.Clear();
            input.SetActionState(
                AuthoritativeGroundPointerHelper.ActionId,
                new Vector3(worldCm.X, 0f, worldCm.Y),
                isDown: true,
                pressedThisFrame: false,
                releasedThisFrame: false);
            pointerButtons.Clear();
            pointerButtons.SetState(
                InteractionActionBindings.DefaultConfirmActionId,
                new PointerButtonState(
                    pointer: Vector2.Zero,
                    pressPointer: Vector2.Zero,
                    releasePointer: Vector2.Zero,
                    lastDownPointer: Vector2.Zero,
                    isDown: false,
                    pressedThisFrame: string.Equals(actionId, InteractionActionBindings.DefaultConfirmActionId, StringComparison.Ordinal),
                    releasedThisFrame: false,
                    hasPressPointer: string.Equals(actionId, InteractionActionBindings.DefaultConfirmActionId, StringComparison.Ordinal),
                    hasReleasePointer: false,
                    hasLastDownPointer: false));
            if (string.Equals(actionId, InteractionActionBindings.DefaultConfirmActionId, StringComparison.Ordinal))
            {
                return;
            }

            pointerButtons.SetState(
                actionId,
                new PointerButtonState(
                    pointer: Vector2.Zero,
                    pressPointer: Vector2.Zero,
                    releasePointer: Vector2.Zero,
                    lastDownPointer: Vector2.Zero,
                    isDown: false,
                    pressedThisFrame: true,
                    releasedThisFrame: false,
                    hasPressPointer: true,
                    hasReleasePointer: false,
                    hasLastDownPointer: false));
        }

        private static void ClearPointerPick(
            FrozenInputActionReader input,
            AuthoritativePointerButtonSnapshot pointerButtons,
            string actionId)
        {
            input.Clear();
            pointerButtons.Clear();
            pointerButtons.SetState(
                actionId,
                new PointerButtonState(
                    pointer: Vector2.Zero,
                    pressPointer: Vector2.Zero,
                    releasePointer: Vector2.Zero,
                    lastDownPointer: Vector2.Zero,
                    isDown: false,
                    pressedThisFrame: false,
                    releasedThisFrame: false,
                    hasPressPointer: false,
                    hasReleasePointer: false,
                    hasLastDownPointer: false));
        }

        private sealed class RecordingPathService : IPathService
        {
            private readonly PathStore _store;

            public RecordingPathService(PathStore store)
            {
                _store = store;
            }

            public List<PathRequest> Requests { get; } = new();

            public bool TrySolve(in PathRequest request, out PathResult result)
            {
                Requests.Add(request);
                if (!_store.TryAllocate(3, out PathHandle handle))
                {
                    result = new PathResult(request.RequestId, request.Actor, PathStatus.BudgetExceeded, default, expanded: 0, errorCode: 1);
                    return false;
                }

                Span<int> xs = stackalloc[]
                {
                    request.Start.Xcm,
                    (request.Start.Xcm + request.Goal.Xcm) / 2,
                    request.Goal.Xcm
                };
                Span<int> ys = stackalloc[]
                {
                    request.Start.Ycm,
                    (request.Start.Ycm + request.Goal.Ycm) / 2,
                    request.Goal.Ycm
                };
                Assert.That(_store.TryWrite(in handle, xs, ys, 3), Is.True);
                result = new PathResult(request.RequestId, request.Actor, PathStatus.Found, handle, expanded: 3, errorCode: 0);
                return true;
            }

            public bool TryCopyPath(in PathHandle handle, Span<int> xcmOut, Span<int> ycmOut, out int count)
            {
                return _store.TryCopy(in handle, xcmOut, ycmOut, out count);
            }
        }

        private sealed class NullInputBackend : IInputBackend
        {
            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => false;
            public Vector2 GetMousePosition() => Vector2.Zero;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }

        private sealed class CountingScreenRayProvider : IScreenRayProvider
        {
            public int CallCount { get; private set; }

            public void Reset() => CallCount = 0;

            public ScreenRay GetRay(Vector2 screenPosition)
            {
                CallCount++;
                return new ScreenRay(
                    new Vector3(screenPosition.X, 10f, screenPosition.Y),
                    new Vector3(0f, -1f, 0f));
            }
        }

        private static IVisualHeightmap CreateFlatHeightmap(WorldAabbCm bounds)
        {
            return new VisualHeightmapRuntime(
                VisualHeightmapAsset.CreateSingleLayer(
                    bounds,
                    sampleColumns: 2,
                    sampleRows: 2,
                    new short[]
                    {
                        0, 0,
                        0, 0,
                    }));
        }
    }
}
