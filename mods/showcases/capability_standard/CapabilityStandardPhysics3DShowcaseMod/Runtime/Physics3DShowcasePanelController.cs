using System;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.Core.Vehicle3D;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;

namespace CapabilityStandardPhysics3DShowcaseMod.Runtime;

internal sealed class Physics3DShowcasePanelController
{
    private const float PanelWidth = 470f;
    private const float PanelHeight = 850f;

    private readonly Physics3DShowcaseRuntime _runtime;
    private ReactivePage<Physics3DShowcasePanelState>? _page;
    private Physics3DShowcasePanelState _lastState = Physics3DShowcasePanelState.Empty;
    private GameEngine? _engine;
    private UiSurfaceLeaseHandle _lease;

    public Physics3DShowcasePanelController(Physics3DShowcaseRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public void MountOrSync(UIRoot root, GameEngine engine, in Physics3DShowcasePanelState state)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(engine);
        if (engine.GetService(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost surfaceHost)
        {
            throw new InvalidOperationException("Physics3D showcase requires IUiSurfaceHost.");
        }

        _engine = engine;
        ReactivePage<Physics3DShowcasePanelState> page = EnsurePage();
        if (!_lastState.Equals(state))
        {
            Physics3DShowcasePanelState snapshot = state;
            page.SetState(_ => snapshot);
            _lastState = snapshot;
        }

        surfaceHost.PublishReactivePage(
            ref _lease,
            new UiSurfaceLeaseRequest("Showcase.CapabilityStandardPhysics3D.Panel", UiSurfaceSegment.Overlay, priority: 40),
            page);
    }

    public void ClearIfOwned(UIRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (_lease.IsValid)
        {
            if (_engine?.GetService(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost surfaceHost)
            {
                throw new InvalidOperationException("Physics3D showcase lost IUiSurfaceHost before releasing its lease.");
            }
            surfaceHost.ReleaseLease(ref _lease);
        }

        _engine = null;
        _lastState = Physics3DShowcasePanelState.Empty;
    }

    private ReactivePage<Physics3DShowcasePanelState> EnsurePage()
    {
        if (_page != null)
        {
            return _page;
        }

        GameEngine engine = RequireEngine();
        if (engine.GetService(CoreServiceKeys.UiTextMeasurer) is not IUiTextMeasurer textMeasurer)
        {
            throw new InvalidOperationException("Physics3D showcase requires IUiTextMeasurer.");
        }

        if (engine.GetService(CoreServiceKeys.UiImageSizeProvider) is not IUiImageSizeProvider imageSizeProvider)
        {
            throw new InvalidOperationException("Physics3D showcase requires IUiImageSizeProvider.");
        }
        _page = new ReactivePage<Physics3DShowcasePanelState>(
            textMeasurer,
            imageSizeProvider,
            Physics3DShowcasePanelState.Empty,
            BuildRoot);
        return _page;
    }

    private UiElementBuilder BuildRoot(ReactiveContext<Physics3DShowcasePanelState> context)
    {
        Physics3DShowcasePanelState state = context.State;
        return Ui.Panel(
                Ui.Panel(
                        BuildHeader(state),
                        Ui.ScrollView(
                                BuildSceneSelector(state),
                                BuildStationGoal(state),
                                BuildPrimaryStationControls(state),
                                BuildSceneEvidence(state),
                                BuildPlaybackControls(state),
                                BuildMetrics(state))
                            .Height(700f)
                            .Gap(10f))
                    .Id("capability-standard-physics3d-panel")
                    .Width(PanelWidth)
                    .Height(PanelHeight)
                    .Padding(14f)
                    .Gap(10f)
                    .Radius(9f)
                    .Background("#0D1520")
                    .Border(1f, ParseColor("#35506B"))
                    .Absolute(16f, 16f)
                    .ZIndex(42))
            .WidthPercent(100f)
            .HeightPercent(100f)
            .Absolute(0f, 0f)
            .PointerEvents(UiPointerEvents.None)
            .ZIndex(42);
    }

    private UiElementBuilder BuildHeader(Physics3DShowcasePanelState state)
    {
        return Ui.Column(
                Ui.Row(
                        Ui.Text(state.Title)
                            .FontSize(20f)
                            .Bold()
                            .Color("#F4CC73")
                            .FlexGrow(1f),
                        BuildPill($"{state.FixedHz} Hz", "#173448", "#D7EEFF"),
                        BuildPill(state.Paused ? "PAUSED" : "RUNNING", state.Paused ? "#53303A" : "#214836", "#F5F8FB"))
                    .Gap(7f),
                Ui.Text(state.SceneTitle)
                    .FontSize(16f)
                    .Bold()
                    .Color("#E6EEF7"),
                Ui.Text(state.SceneDescription)
                    .FontSize(11f)
                    .Color("#AFC0D2")
                    .WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text(state.LastAction)
                    .FontSize(10f)
                    .Color("#7ED6B2")
                    .WhiteSpace(UiWhiteSpace.Normal))
            .Gap(5f);
    }

    private UiElementBuilder BuildSceneSelector(Physics3DShowcasePanelState state)
    {
        return Section(
            "Choose a station",
            Ui.Row(
                    SceneButton("Scanner Range", Physics3DShowcaseScene.ScannerRange, state.Scene),
                    SceneButton("Material Hill", Physics3DShowcaseScene.MaterialHill, state.Scene),
                    SceneButton("Platform Station", Physics3DShowcaseScene.PlatformStation, state.Scene))
                .Wrap()
                .Gap(7f),
            Ui.Row(
                    SceneButton("Wind Tunnel", Physics3DShowcaseScene.WindTunnel, state.Scene),
                    SceneButton("Traversal Course", Physics3DShowcaseScene.TraversalCourse, state.Scene),
                    SceneButton("Wheel Lab", Physics3DShowcaseScene.WheelLab, state.Scene))
                .Wrap()
                .Gap(7f),
            Ui.Row(
                    SceneButton("Ragdoll Lab", Physics3DShowcaseScene.RagdollLab, state.Scene),
                    SceneButton("Constraint Forge", Physics3DShowcaseScene.ConstraintForge, state.Scene))
                .Wrap()
                .Gap(7f),
            Ui.Row(
                    SceneButton("Deterministic Rebuild", Physics3DShowcaseScene.ReplayTheater, state.Scene),
                    SceneButton("Scale City", Physics3DShowcaseScene.ScaleCity, state.Scene))
                .Wrap()
                .Gap(7f));
    }

    private UiElementBuilder BuildPlaybackControls(Physics3DShowcasePanelState state)
    {
        if (state.Scene is Physics3DShowcaseScene.MaterialHill or
            Physics3DShowcaseScene.ReplayTheater or
            Physics3DShowcaseScene.ScaleCity)
        {
            return Section(
                "Retry / reset",
                Ui.Row(
                        ActionButton(state.Paused ? "Resume" : "Pause", state.Paused, "#4A3549", "physics3d-action-pause", _ => Enqueue(Physics3DShowcaseCommandKind.TogglePause)),
                        ActionButton("Single Step", state.Paused, "#2A526A", "physics3d-action-single-step", _ => Enqueue(Physics3DShowcaseCommandKind.SingleStep)),
                        ActionButton("Reset Station", false, "#50333B", "physics3d-action-reset", _ => Enqueue(Physics3DShowcaseCommandKind.Reset)))
                    .Wrap()
                    .Gap(7f));
        }

        if (state.Scene is Physics3DShowcaseScene.ScannerRange or
            Physics3DShowcaseScene.WindTunnel or
            Physics3DShowcaseScene.ConstraintForge)
        {
            return Section(
                "World controls",
                Ui.Row(
                        ActionButton(state.Paused ? "Resume" : "Pause", state.Paused, "#4A3549", "physics3d-action-pause", _ => Enqueue(Physics3DShowcaseCommandKind.TogglePause)),
                        ActionButton("Single Step", state.Paused, "#2A526A", "physics3d-action-single-step", _ => Enqueue(Physics3DShowcaseCommandKind.SingleStep)),
                        ActionButton("Reset Station", false, "#50333B", "physics3d-action-reset", _ => Enqueue(Physics3DShowcaseCommandKind.Reset)))
                    .Wrap()
                    .Gap(7f));
        }

        if (state.Scene is Physics3DShowcaseScene.PlatformStation or Physics3DShowcaseScene.TraversalCourse)
        {
            return Section(
                "Route actions",
                Ui.Row(
                        ActionButton(state.Paused ? "Resume" : "Pause", state.Paused, "#4A3549", "physics3d-action-pause", _ => Enqueue(Physics3DShowcaseCommandKind.TogglePause)),
                        ActionButton("Single Step", state.Paused, "#2A526A", "physics3d-action-single-step", _ => Enqueue(Physics3DShowcaseCommandKind.SingleStep)),
                        ActionButton("Restart Route", false, "#315944", "physics3d-route-restart", _ => Enqueue(Physics3DShowcaseCommandKind.Reset)))
                    .Wrap()
                    .Gap(7f));
        }

        UiElementBuilder sceneAction;
        if (state.Scene is Physics3DShowcaseScene.WheelLab or
                 Physics3DShowcaseScene.RagdollLab)
        {
            sceneAction = ActionButton(
                "Restart Route",
                false,
                "#315944",
                "physics3d-route-restart",
                _ => Enqueue(Physics3DShowcaseCommandKind.Reset));
        }
        else
        {
            sceneAction = ActionButton(
                "Impact",
                false,
                "#5B4424",
                "physics3d-action-impact",
                _ => Enqueue(Physics3DShowcaseCommandKind.Impact));
        }

        return Section(
            "Play with it",
            Ui.Row(
                    ActionButton(state.Paused ? "Resume" : "Pause", state.Paused, "#4A3549", "physics3d-action-pause", _ => Enqueue(Physics3DShowcaseCommandKind.TogglePause)),
                    ActionButton("Single Step", state.Paused, "#2A526A", "physics3d-action-single-step", _ => Enqueue(Physics3DShowcaseCommandKind.SingleStep)),
                    ActionButton("Reset", false, "#50333B", "physics3d-action-reset", _ => Enqueue(Physics3DShowcaseCommandKind.Reset)),
                    sceneAction)
                .Wrap()
                .Gap(7f),
            Ui.Text("Pause freezes the authoritative world. Single Step always advances exactly one 30Hz step.")
                .FontSize(10f)
                .Color("#91A5BA")
                .WhiteSpace(UiWhiteSpace.Normal));
    }

    private static UiElementBuilder BuildMetrics(Physics3DShowcasePanelState state)
    {
        return Section(
            "Live physics",
            Metric("step", $"{state.PhysicsUpdateMilliseconds:0.###} ms total · {state.MaximumStepMilliseconds:0.###} ms update max"),
            Metric("bodies", $"{state.Bodies} total · {state.DynamicBodies} dynamic · {state.KinematicBodies} kinematic · {state.StaticBodies} static"),
            Metric("awake", $"{state.AwakeBodies} awake · {state.VisibleBodies} drawn"),
            Metric("contacts", $"{state.ContactPairs} pairs · {state.ContactEvents} events"),
            Metric("joints", state.Constraints.ToString()),
            Metric("clock", $"step {state.PhysicsStepsLastUpdate} this tick · {state.TotalPhysicsSteps} total"));
    }

    private UiElementBuilder BuildSceneEvidence(Physics3DShowcasePanelState state)
    {
        if (state.Scene == Physics3DShowcaseScene.ScaleCity)
        {
            return BuildBenchmarkEvidence(state);
        }

        if (state.Scene == Physics3DShowcaseScene.ReplayTheater)
        {
            return BuildReplayEvidence(state);
        }

        if (state.Scene == Physics3DShowcaseScene.WheelLab)
        {
            return BuildWheelLabEvidence(state);
        }

        if (state.Scene == Physics3DShowcaseScene.MaterialHill)
        {
            Physics3DMaterialHillShowcaseState hill = state.MaterialHill;
            string progress = hill.Status == Physics3DShowcaseChallengeStatus.Ready
                ? "waiting for Push Crates"
                : $"stable {hill.StableTicks}/{hill.RequiredStableTicks} · {hill.TicksRemaining} fixed ticks left";
            return Section(
                "Run progress",
                Metric("status", Physics3DShowcaseRuntime.ChallengeStatusLabel(hill.Status)),
                Metric("stability", progress),
                Metric("ranking", state.MaterialSummary));
        }

        if (state.Scene == Physics3DShowcaseScene.WindTunnel)
        {
            return Section(
                "Wind response",
                Metric("zone", $"{Physics3DShowcaseRuntime.WindZoneLabel(state.WindZone)} | {Physics3DShowcaseRuntime.DriveDirectionLabel(state.WindDirection)}"),
                Metric("light", $"{state.WindLightTravelCm:0} cm from launch"),
                Metric("heavy", $"{state.WindHeavyTravelCm:0} cm from launch"));
        }

        if (state.Scene == Physics3DShowcaseScene.RagdollLab)
        {
            return Section("Mannequin state", Metric("body", state.RagdollSummary));
        }

        if (state.Scene == Physics3DShowcaseScene.ScannerRange)
        {
            return BuildScannerEvidence(state);
        }

        if (state.Scene == Physics3DShowcaseScene.ConstraintForge)
        {
            return Section(
                "Drive response",
                Metric("state", state.ConstraintDriveEnabled ? "RUNNING" : "PAUSED"),
                Metric("direction", Physics3DShowcaseRuntime.DriveDirectionLabel(state.ConstraintDriveDirection)),
                Metric("motion", state.ConstraintSummary));
        }

        if (state.Scene is Physics3DShowcaseScene.PlatformStation or Physics3DShowcaseScene.TraversalCourse)
        {
            return BuildCharacterRouteEvidence(state);
        }

        return Section(
            "Station evidence",
            Metric("constraints", state.Constraints.ToString()));
    }

    private UiElementBuilder BuildScannerEvidence(Physics3DShowcasePanelState state)
    {
        int queryIndex = (int)state.ScannerQueryKind - 1;
        int hitCount = _runtime.GetQueryHitCount(queryIndex);
        UiElementBuilder[] children = new UiElementBuilder[hitCount + 2];
        children[0] = Metric(
            "selection",
            $"{ScannerQueryLabel(state.ScannerQueryKind)} | {state.ScannerDistanceCm:0} cm | {state.ScannerLayerFilterName}");
        children[1] = Metric(
            "result",
            state.ScannerQueryFailed
                ? "FAILED | result capacity exceeded; nothing was truncated"
                : state.ScannerHasResult
                    ? $"{hitCount} hit(s), nearest first"
                    : "Ready | choose settings, then Run Scan");
        for (int hitIndex = 0; hitIndex < hitCount; hitIndex++)
        {
            if (!_runtime.TryGetQueryHitVisual(queryIndex, hitIndex, out Physics3DShowcaseQueryHitVisual hit))
            {
                throw new InvalidOperationException($"Scanner Range panel lost hit {hitIndex} for query {state.ScannerQueryKind}.");
            }

            string normal = hit.Normal.LengthSquared() <= 1e-8f
                ? "normal n/a"
                : $"normal ({hit.Normal.X:0.00}, {hit.Normal.Y:0.00}, {hit.Normal.Z:0.00})";
            children[hitIndex + 2] = Metric(
                $"hit {hitIndex + 1}",
                $"{hit.DistanceCm:0.0} cm | {normal} | start overlap {(hit.StartedOverlapping ? "YES" : "NO")}");
        }

        return Section("Scan results", children);
    }

    private UiElementBuilder BuildStationGoal(Physics3DShowcasePanelState state)
    {
        return state.Scene switch
        {
            Physics3DShowcaseScene.ScannerRange => Section(
                "Goal",
                Ui.Text("Choose one scan shape, distance, and target layer. Run it, then inspect every ordered hit in the lane.")
                    .FontSize(11f).Color("#D7E2ED").WhiteSpace(UiWhiteSpace.Normal)),
            Physics3DShowcaseScene.WindTunnel => Section(
                "Goal",
                Ui.Text("Compare the same light and heavy pair in steady wind, gusts, and a vortex. Reverse and relaunch for a fair rerun.")
                    .FontSize(11f).Color("#D7E2ED").WhiteSpace(UiWhiteSpace.Normal)),
            Physics3DShowcaseScene.ConstraintForge => Section(
                "Goal",
                Ui.Text("Start, pause, and reverse the door motor and moving servo targets while watching the real jointed bodies respond.")
                    .FontSize(11f).Color("#D7E2ED").WhiteSpace(UiWhiteSpace.Normal)),
            Physics3DShowcaseScene.PlatformStation => Section(
                "Goal",
                Ui.Text("Cross the moving lift, rotating platform, conveyor, and one-way finish in order before time expires.")
                    .FontSize(11f).Color("#D7E2ED").WhiteSpace(UiWhiteSpace.Normal)),
            Physics3DShowcaseScene.TraversalCourse => Section(
                "Goal",
                Ui.Text("Clear the slope, steps, moving platform, ladder mantle, and wall mantle, then stand on the upper deck.")
                    .FontSize(11f).Color("#D7E2ED").WhiteSpace(UiWhiteSpace.Normal)),
            Physics3DShowcaseScene.WheelLab => Section(
                "Goal",
                Ui.Text("Drive the same chassis over the test road with physical, box, and scanning wheels, then compare suspension, contact, and slip feedback.")
                    .FontSize(11f).Color("#D7E2ED").WhiteSpace(UiWhiteSpace.Normal)),
            Physics3DShowcaseScene.RagdollLab => Section(
                "Goal",
                Ui.Text("Swing the pendulum into the mannequin, release its active pose, then recover only after the landing space is clear.")
                    .FontSize(11f).Color("#D7E2ED").WhiteSpace(UiWhiteSpace.Normal)),
            Physics3DShowcaseScene.MaterialHill => Section(
                "Goal",
                Ui.Text("Launch three identical crates once, wait until all three settle, then compare the ranked stopping distances.")
                    .FontSize(11f).Color("#D7E2ED").WhiteSpace(UiWhiteSpace.Normal)),
            Physics3DShowcaseScene.ReplayTheater => Section(
                "Goal",
                Ui.Text("Choose a clean rebuild check or deliberately inject the authored difference, then see the first mismatching step and both hashes.")
                    .FontSize(11f).Color("#D7E2ED").WhiteSpace(UiWhiteSpace.Normal)),
            Physics3DShowcaseScene.ScaleCity => Section(
                "Goal",
                Ui.Text("Pulse the colliding foreground, watch its activity change, then switch city population without disturbing the separated background paths.")
                    .FontSize(11f).Color("#D7E2ED").WhiteSpace(UiWhiteSpace.Normal)),
            _ => Ui.Panel()
        };
    }

    private UiElementBuilder BuildPrimaryStationControls(Physics3DShowcasePanelState state)
    {
        return state.Scene switch
        {
            Physics3DShowcaseScene.ScannerRange => BuildScannerControls(state),
            Physics3DShowcaseScene.WindTunnel => BuildWindTunnelControls(state),
            Physics3DShowcaseScene.ConstraintForge => BuildConstraintForgeControls(state),
            Physics3DShowcaseScene.PlatformStation => BuildCharacterRouteControls(),
            Physics3DShowcaseScene.TraversalCourse => BuildCharacterRouteControls(),
            Physics3DShowcaseScene.WheelLab => BuildWheelLabControls(state),
            Physics3DShowcaseScene.RagdollLab => BuildRagdollLabControls(state),
            Physics3DShowcaseScene.MaterialHill => BuildMaterialHillControls(state),
            Physics3DShowcaseScene.ReplayTheater => BuildReplayControls(state),
            Physics3DShowcaseScene.ScaleCity => BuildBenchmarkControls(state),
            _ => Ui.Panel()
        };
    }

    private UiElementBuilder BuildMaterialHillControls(Physics3DShowcasePanelState state)
    {
        string label = state.MaterialHill.Status == Physics3DShowcaseChallengeStatus.Ready
            ? "Push Crates"
            : "Push Again";
        return Section(
            "Operation",
            ActionButton(label, false, "#5B4424", "physics3d-action-impact", _ => Enqueue(Physics3DShowcaseCommandKind.Impact)));
    }

    private UiElementBuilder BuildReplayControls(Physics3DShowcasePanelState state)
    {
        if (state.DeterminismComparisonStatus != Physics3DShowcaseReplayStatus.ReadyToReplay)
        {
            return Section(
                "Operation",
                Metric("available", "Wait for the baseline and rebuild, or Reset Station to start over."));
        }

        return Section(
            "Operation",
            Ui.Row(
                    ActionButton(
                        "Verify Clean Run",
                        true,
                        "#285541",
                        "physics3d-replay-start",
                        _ => Enqueue(Physics3DShowcaseCommandKind.StartReplayComparison)),
                    ActionButton(
                        "Inject Difference",
                        false,
                        "#5B4424",
                        "physics3d-replay-inject-difference",
                        _ => Enqueue(Physics3DShowcaseCommandKind.StartReplayDifferenceComparison)))
                .Wrap()
                .Gap(7f));
    }

    private static UiElementBuilder BuildCharacterRouteControls()
    {
        return Section(
            "Controls",
            Metric("move", "W / S forward and back · A / D across the lane or up and down while climbing"),
            Metric("jump", "Space"),
            Metric("grab / release", "E near a marked ladder or wall"));
    }

    private static UiElementBuilder BuildCharacterRouteEvidence(Physics3DShowcasePanelState state)
    {
        string status = state.CharacterRouteStatus switch
        {
            Physics3DShowcaseRouteStatus.InProgress => "RUNNING",
            Physics3DShowcaseRouteStatus.Completed => "COMPLETE",
            Physics3DShowcaseRouteStatus.Failed => "FAILED",
            _ => throw new InvalidOperationException($"Unknown character route status '{state.CharacterRouteStatus}'.")
        };
        return Section(
            "Route progress",
            Metric("status", status),
            Metric("checkpoints", $"{state.CharacterRouteCheckpointIndex}/{state.CharacterRouteCheckpointCount}"),
            Metric("next", state.CharacterRouteNextAction),
            Metric("time", $"{state.CharacterRouteTicksRemaining} fixed ticks remaining"));
    }

    private UiElementBuilder BuildScannerControls(Physics3DShowcasePanelState state)
    {
        Physics3DShowcaseQueryKind[] kinds = Enum.GetValues<Physics3DShowcaseQueryKind>();
        UiElementBuilder[] kindButtons = new UiElementBuilder[kinds.Length];
        for (int i = 0; i < kinds.Length; i++)
        {
            Physics3DShowcaseQueryKind kind = kinds[i];
            kindButtons[i] = ActionButton(
                ScannerQueryLabel(kind),
                state.ScannerQueryKind == kind,
                "#285541",
                $"physics3d-scanner-kind-{kind.ToString().ToLowerInvariant()}",
                _ => Enqueue(Physics3DShowcaseCommandKind.SetScannerQueryKind, (int)kind));
        }

        float[] distances = _runtime.ActiveConfig.ScannerRange.DistancePresetsCm;
        UiElementBuilder[] distanceButtons = new UiElementBuilder[distances.Length];
        for (int i = 0; i < distances.Length; i++)
        {
            int presetIndex = i;
            distanceButtons[i] = ActionButton(
                $"{distances[i]:0} cm",
                state.ScannerDistancePresetIndex == i,
                "#2A526A",
                $"physics3d-scanner-distance-{i}",
                _ => Enqueue(Physics3DShowcaseCommandKind.SetScannerDistancePreset, presetIndex));
        }

        Physics3DScannerLayerFilterShowcaseConfig[] filters = _runtime.ActiveConfig.ScannerRange.LayerFilters;
        UiElementBuilder[] filterButtons = new UiElementBuilder[filters.Length];
        for (int i = 0; i < filters.Length; i++)
        {
            int filterIndex = i;
            filterButtons[i] = ActionButton(
                filters[i].Name,
                state.ScannerLayerFilterIndex == i,
                "#5B4424",
                $"physics3d-scanner-layer-{i}",
                _ => Enqueue(Physics3DShowcaseCommandKind.SetScannerLayerFilter, filterIndex));
        }

        return Section(
            "Scanner controls",
            Ui.Row(kindButtons).Wrap().Gap(7f),
            Ui.Row(distanceButtons).Wrap().Gap(7f),
            Ui.Row(filterButtons).Wrap().Gap(7f),
            ActionButton("Run Scan", false, "#315944", "physics3d-scanner-run", _ => Enqueue(Physics3DShowcaseCommandKind.RunScannerQuery)));
    }

    private UiElementBuilder BuildWindTunnelControls(Physics3DShowcasePanelState state)
    {
        return Section(
            "Wind controls",
            Ui.Row(
                    WindZoneButton(Physics3DShowcaseWindZone.Steady, state.WindZone),
                    WindZoneButton(Physics3DShowcaseWindZone.Gust, state.WindZone),
                    WindZoneButton(Physics3DShowcaseWindZone.Vortex, state.WindZone))
                .Wrap().Gap(7f),
            Ui.Row(
                    ActionButton("Reverse Wind", false, "#5B4424", "physics3d-wind-reverse", _ => Enqueue(Physics3DShowcaseCommandKind.ReverseWindDirection)),
                    ActionButton("Relaunch Pair", false, "#315944", "physics3d-wind-relaunch", _ => Enqueue(Physics3DShowcaseCommandKind.RelaunchWindPair)))
                .Wrap().Gap(7f));
    }

    private UiElementBuilder WindZoneButton(Physics3DShowcaseWindZone zone, Physics3DShowcaseWindZone activeZone)
    {
        return ActionButton(
            Physics3DShowcaseRuntime.WindZoneLabel(zone),
            zone == activeZone,
            "#285541",
            $"physics3d-wind-zone-{zone.ToString().ToLowerInvariant()}",
            _ => Enqueue(Physics3DShowcaseCommandKind.SetWindZone, (int)zone));
    }

    private UiElementBuilder BuildConstraintForgeControls(Physics3DShowcasePanelState state)
    {
        return Section(
            "Forge controls",
            Ui.Row(
                    ActionButton(
                        state.ConstraintDriveEnabled ? "Pause Drives" : "Start Drives",
                        state.ConstraintDriveEnabled,
                        "#285541",
                        "physics3d-constraint-toggle",
                        _ => Enqueue(Physics3DShowcaseCommandKind.ToggleConstraintDrive)),
                    ActionButton(
                        "Reverse Drives",
                        false,
                        "#5B4424",
                        "physics3d-constraint-reverse",
                        _ => Enqueue(Physics3DShowcaseCommandKind.ReverseConstraintDrive)))
                .Wrap().Gap(7f));
    }

    private static string ScannerQueryLabel(Physics3DShowcaseQueryKind kind) => kind switch
    {
        Physics3DShowcaseQueryKind.Ray => "Ray",
        Physics3DShowcaseQueryKind.BoxCast => "Box Cast",
        Physics3DShowcaseQueryKind.SphereCast => "Sphere Cast",
        Physics3DShowcaseQueryKind.CapsuleCast => "Capsule Cast",
        Physics3DShowcaseQueryKind.BoxOverlap => "Box Overlap",
        Physics3DShowcaseQueryKind.SphereOverlap => "Sphere Overlap",
        Physics3DShowcaseQueryKind.CapsuleOverlap => "Capsule Overlap",
        _ => throw new InvalidOperationException($"Unknown Scanner Range query kind '{kind}'.")
    };

    private static UiElementBuilder BuildWheelLabEvidence(Physics3DShowcasePanelState state)
    {
        return Section(
            "Fair three-wheel route",
            Metric("live", state.WheelSummary),
            Metric("route", state.WheelRouteGuide),
            Metric("physical", state.WheelPhysicalResult).Id("physics3d-wheel-result-physical"),
            Metric("box", state.WheelBoxResult).Id("physics3d-wheel-result-box"),
            Metric("scanning", state.WheelScanningResult).Id("physics3d-wheel-result-scanning"),
            Metric("course", "YELLOW ramps · BROWN pothole · BLUE side slope · PURPLE platform · RED jump · GREEN brake"),
            Metric("debug", "Gold contact · green normal · cyan suspension · red slip"),
            Metric("keys", "W/S throttle · A/D steer · Space brake · Q next wheel · R retry"));
    }

    private UiElementBuilder BuildWheelLabControls(Physics3DShowcasePanelState state)
    {
        if (state.Scene != Physics3DShowcaseScene.WheelLab)
        {
            return Ui.Panel();
        }

        Vehicle3DWheelKind mode = _runtime.WheelLabMode;
        return Section(
            "Wheel type",
            Ui.Row(
                    WheelModeButton("Physical", Vehicle3DWheelKind.Physical, mode),
                    WheelModeButton("Box Wheel", Vehicle3DWheelKind.Box, mode),
                    WheelModeButton("Scanning", Vehicle3DWheelKind.Scanning, mode))
                .Wrap()
                .Gap(7f),
            Ui.Text("Every wheel type starts from the same chassis and moving-platform state. Changing wheels mid-run marks that run VOID.")
                .FontSize(10f)
                .Color("#91A5BA")
                .WhiteSpace(UiWhiteSpace.Normal));
    }

    private UiElementBuilder WheelModeButton(
        string label,
        Vehicle3DWheelKind mode,
        Vehicle3DWheelKind activeMode)
    {
        return ActionButton(
            label,
            mode == activeMode,
            "#285541",
            $"physics3d-wheel-{mode.ToString().ToLowerInvariant()}",
            _ => Enqueue(Physics3DShowcaseCommandKind.SetWheelMode, (int)mode));
    }

    private UiElementBuilder BuildRagdollLabControls(Physics3DShowcasePanelState state)
    {
        if (state.Scene != Physics3DShowcaseScene.RagdollLab)
        {
            return Ui.Panel();
        }

        return Section(
            "Ragdoll controls",
            Ui.Row(
                    ActionButton(
                        "Swing Pendulum",
                        false,
                        "#5B4424",
                        "physics3d-ragdoll-pendulum",
                        _ => Enqueue(Physics3DShowcaseCommandKind.LaunchRagdollPendulum)),
                    ActionButton(
                        "Toggle Active Pose",
                        false,
                        "#285541",
                        "physics3d-ragdoll-active-pose",
                        _ => Enqueue(Physics3DShowcaseCommandKind.ToggleRagdollActivePose)),
                    ActionButton(
                        "Recover",
                        false,
                        "#315944",
                        "physics3d-ragdoll-recover",
                        _ => Enqueue(Physics3DShowcaseCommandKind.RecoverRagdoll)))
                .Wrap()
                .Gap(7f));
    }

    private UiElementBuilder BuildBenchmarkEvidence(Physics3DShowcasePanelState state)
    {
        Physics3DScaleCityShowcaseState scaleCity = state.ScaleCity;
        string percentileEvidence = scaleCity.PerformanceSampleCount == 0
            ? "waiting for first fixed step"
            : $"P50 {scaleCity.StepP50Milliseconds:0.###} · P95 {scaleCity.StepP95Milliseconds:0.###} · " +
              $"P99 {scaleCity.StepP99Milliseconds:0.###} ms";
        return Section(
            "Scale City status",
            Metric("population", ScaleCityPopulationLabel(in scaleCity)),
            Metric("contacts", $"{scaleCity.ContactPairs:N0} active pairs"),
            Metric("wind", $"{ScaleCityWindDirectionLabel(scaleCity.WindAccelerationXCmPerSecondSquared)} · " +
                           $"{MathF.Abs(scaleCity.WindAccelerationXCmPerSecondSquared):0} cm/s²"),
            Metric("activity", ScaleCityActivityLabel(in scaleCity)),
            Metric("window", $"{ScaleCityPerformanceStatusLabel(scaleCity.PerformanceStatus)} · " +
                             $"{scaleCity.PerformanceSampleCount:N0}/{scaleCity.PerformanceWindowCapacity:N0} fixed steps"),
            Metric("latency", percentileEvidence),
            Metric("budget", $"P95 and P99 must both stay below {scaleCity.PerformanceBudgetMilliseconds:0.###} ms"));
    }

    internal static string ScaleCityPerformanceStatusLabel(Physics3DScaleCityPerformanceStatus status) => status switch
    {
        Physics3DScaleCityPerformanceStatus.Warming => "WARMING",
        Physics3DScaleCityPerformanceStatus.Pass => "PASS",
        Physics3DScaleCityPerformanceStatus.OverBudget => "OVER BUDGET",
        _ => throw new InvalidOperationException($"Unknown Scale City performance status '{status}'.")
    };

    internal static string ScaleCityPopulationLabel(in Physics3DScaleCityShowcaseState state)
        => $"{state.InteractiveBodies:N0} foreground · {state.SparseBodies:N0} background";

    internal static string ScaleCityActivityLabel(in Physics3DScaleCityShowcaseState state)
        => $"pulse {state.PulseCount:N0} hit {state.PulsedForegroundBodiesLastPulse:N0} foreground · " +
           $"{state.InteractiveRelaunchedBodiesLastStep:N0} foreground launched · " +
           $"{state.SparseRecycledBodiesLastStep:N0} background recycled";

    internal static string ScaleCityWindDirectionLabel(float accelerationXCmPerSecondSquared)
    {
        if (!float.IsFinite(accelerationXCmPerSecondSquared))
        {
            throw new InvalidOperationException("Scale City wind acceleration must be finite.");
        }

        return accelerationXCmPerSecondSquared > 0f
            ? "RIGHT"
            : accelerationXCmPerSecondSquared < 0f
                ? "LEFT"
                : "CALM";
    }

    private UiElementBuilder BuildReplayEvidence(Physics3DShowcasePanelState state)
    {
        UiElementBuilder phases = Ui.Row(
                ReplayPhasePill("1 BASELINE", state.DeterminismComparisonStatus == Physics3DShowcaseReplayStatus.Recording,
                    state.DeterminismComparisonStatus is Physics3DShowcaseReplayStatus.ReadyToReplay or Physics3DShowcaseReplayStatus.Replaying or Physics3DShowcaseReplayStatus.Passed),
                ReplayPhasePill("2 REBUILD", state.DeterminismComparisonStatus == Physics3DShowcaseReplayStatus.ReadyToReplay,
                    state.DeterminismComparisonStatus is Physics3DShowcaseReplayStatus.Replaying or Physics3DShowcaseReplayStatus.Passed),
                ReplayPhasePill("3 VERIFY", state.DeterminismComparisonStatus == Physics3DShowcaseReplayStatus.Replaying,
                    state.DeterminismComparisonStatus == Physics3DShowcaseReplayStatus.Passed))
            .Wrap()
            .Gap(6f);

        if (state.DeterminismComparisonStatus == Physics3DShowcaseReplayStatus.ReadyToReplay)
        {
            return Section(
                "Deterministic rebuild check",
                phases,
                Metric("progress", state.DeterminismComparisonSummary),
                Metric("lanes", "BLUE scripted baseline · GOLD rebuilt run"),
                Metric("scope", "Authored bodies only · no player-input replay · no world rollback"));
        }

        if (state.DeterminismComparisonStatus is Physics3DShowcaseReplayStatus.Passed or Physics3DShowcaseReplayStatus.Failed)
        {
            return Section(
                "Deterministic rebuild check",
                phases,
                Metric("result", state.DeterminismComparisonSummary),
                Metric("difference", state.DeterminismDifferenceInjected
                    ? $"INJECTED · body {_runtime.ActiveConfig.ReplayDifferenceBodyIndex + 1} · step {_runtime.ActiveConfig.ReplayDifferenceStep}"
                    : "CLEAN RUN · no difference injected"),
                Metric("lanes", "BLUE scripted baseline · GOLD rebuilt run"),
                Metric("scope", "Authored bodies only · no player-input replay · no world rollback"));
        }

        return Section(
            "Deterministic rebuild check",
            phases,
            Metric("progress", state.DeterminismComparisonSummary),
            Metric("lanes", state.DeterminismComparisonStatus == Physics3DShowcaseReplayStatus.Recording
                ? "BLUE scripted baseline · rebuilt run waiting"
                : "BLUE scripted baseline · GOLD rebuilt run"),
            Metric("scope", "Authored bodies only · no player-input replay · no world rollback"));
    }

    private static UiElementBuilder ReplayPhasePill(string text, bool active, bool complete)
    {
        return BuildPill(
            text,
            active ? "#254F68" : complete ? "#285541" : "#202F40",
            active || complete ? "#F4F8FC" : "#8196AC");
    }

    private UiElementBuilder BuildBenchmarkControls(Physics3DShowcasePanelState state)
    {
        if (!_runtime.IsActive)
        {
            return Section(
                "Scale City",
                Ui.Text("Load the Physics3D Playground map to enable city presets.")
                    .FontSize(10f)
                    .Color("#91A5BA"));
        }

        int[] presets = _runtime.ActiveConfig.BenchmarkPresets;
        UiElementBuilder[] buttons = new UiElementBuilder[presets.Length];
        for (int i = 0; i < presets.Length; i++)
        {
            int preset = presets[i];
            string label = preset >= 1000 && preset % 1000 == 0 ? $"{preset / 1000}K" : preset.ToString();
            buttons[i] = ActionButton(
                label,
                state.Scene == Physics3DShowcaseScene.ScaleCity && state.BenchmarkBodies == preset,
                "#285541",
                $"physics3d-benchmark-{preset}",
                _ => Enqueue(Physics3DShowcaseCommandKind.SetBenchmarkBodies, preset));
        }

        return Section(
            "Operation",
            Ui.Row(buttons).Wrap().Gap(7f),
            ActionButton("City Pulse", false, "#5B4424", "physics3d-action-impact", _ => Enqueue(Physics3DShowcaseCommandKind.Impact)),
            Ui.Text("The foreground city keeps colliding, taking wind, and relaunching. The sparse district preserves the full authoritative body count at 30 Hz.")
                .FontSize(10f)
                .Color("#91A5BA")
                .WhiteSpace(UiWhiteSpace.Normal));
    }

    private UiElementBuilder SceneButton(string label, Physics3DShowcaseScene scene, Physics3DShowcaseScene activeScene)
    {
        return ActionButton(
            label,
            scene == activeScene,
            "#254F68",
            $"physics3d-scene-{scene.ToString().ToLowerInvariant()}",
            _ => Enqueue(Physics3DShowcaseCommandKind.SelectScene, (int)scene));
    }

    private static UiElementBuilder Section(string title, params UiElementBuilder[] children)
    {
        UiElementBuilder[] sectionChildren = new UiElementBuilder[children.Length + 1];
        sectionChildren[0] = Ui.Text(title)
            .FontSize(12f)
            .Bold()
            .Color("#F4CC73");
        Array.Copy(children, 0, sectionChildren, 1, children.Length);
        return Ui.Panel(sectionChildren)
            .Gap(8f)
            .Padding(10f)
            .Radius(8f)
            .Background("#152131")
            .Border(1f, ParseColor("#263D53"));
    }

    private static UiElementBuilder Metric(string label, string value)
    {
        return Ui.Row(
                Ui.Text(label)
                    .FontSize(10f)
                    .Color("#8196AC")
                    .Width(58f)
                    .FlexShrink(0f),
                Ui.Text(value)
                    .FontSize(11f)
                    .Color("#E2ECF6")
                    .WhiteSpace(UiWhiteSpace.Normal)
                    .FlexBasis(0f)
                    .FlexGrow(1f)
                    .FlexShrink(1f))
            .Gap(8f)
            .Align(UiAlignItems.Start);
    }

    private static UiElementBuilder BuildPill(string text, string background, string color)
    {
        return Ui.Text(text)
            .FontSize(10f)
            .Bold()
            .Color(color)
            .Padding(8f, 4f)
            .Radius(8f)
            .Background(background);
    }

    private static UiElementBuilder ActionButton(
        string label,
        bool active,
        string activeBackground,
        string elementId,
        Action<Ludots.UI.Runtime.Actions.UiActionContext> onClick)
    {
        return Ui.Button(label, onClick)
            .Id(elementId)
            .Padding(9f, 7f)
            .Radius(8f)
            .Background(active ? activeBackground : "#202F40")
            .Border(1f, ParseColor(active ? "#7193AD" : "#31465B"))
            .Color("#F4F8FC")
            .FontSize(11f);
    }

    private void Enqueue(Physics3DShowcaseCommandKind kind, int value = 0)
    {
        _runtime.EnqueueCommand(new Physics3DShowcaseCommand(kind, value));
        GameEngine engine = RequireEngine();
        if (engine.GetService(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost surfaceHost)
        {
            throw new InvalidOperationException("Physics3D showcase lost IUiSurfaceHost while handling a panel command.");
        }
        if (_lease.IsValid)
        {
            surfaceHost.InvalidateLease(_lease);
        }
    }

    private GameEngine RequireEngine()
    {
        return _engine ?? throw new InvalidOperationException("Physics3D showcase panel requires an active engine.");
    }

    private static UiColor ParseColor(string hex)
    {
        if (!UiColor.TryParse(hex, out UiColor color))
        {
            throw new InvalidOperationException($"Unsupported color literal '{hex}'.");
        }

        return color;
    }
}
