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
        _page?.SetState(_ => Physics3DShowcasePanelState.Empty);
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
                                BuildPlaybackControls(state),
                                BuildMetrics(state),
                                BuildSceneEvidence(state),
                                BuildWheelLabControls(state),
                                BuildRagdollLabControls(state),
                                BuildBenchmarkControls(state))
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
                    SceneButton("Replay Theater", Physics3DShowcaseScene.ReplayTheater, state.Scene),
                    SceneButton("Scale City", Physics3DShowcaseScene.ScaleCity, state.Scene))
                .Wrap()
                .Gap(7f));
    }

    private UiElementBuilder BuildPlaybackControls(Physics3DShowcasePanelState state)
    {
        UiElementBuilder sceneAction;
        if (state.Scene == Physics3DShowcaseScene.ReplayTheater)
        {
            sceneAction = ActionButton(
                "Restart Run",
                false,
                "#5B4424",
                "physics3d-replay-restart",
                _ => Enqueue(Physics3DShowcaseCommandKind.SelectScene, (int)Physics3DShowcaseScene.ReplayTheater));
        }
        else if (state.Scene is Physics3DShowcaseScene.PlatformStation or
                 Physics3DShowcaseScene.TraversalCourse or
                 Physics3DShowcaseScene.WheelLab or
                 Physics3DShowcaseScene.RagdollLab or
                 Physics3DShowcaseScene.WindTunnel)
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
                state.Scene == Physics3DShowcaseScene.MaterialHill ? "Push Crates" :
                state.Scene == Physics3DShowcaseScene.ScaleCity ? "City Pulse" : "Impact",
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
            Metric("step", $"{state.PhysicsUpdateMilliseconds:0.###} ms total · {state.MaximumStepMilliseconds:0.###} ms max"),
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
            return Section("Surface comparison", Metric("distance", state.MaterialSummary));
        }

        if (state.Scene == Physics3DShowcaseScene.WindTunnel)
        {
            return Section("Wind response", Metric("motion", state.WindSummary));
        }

        if (state.Scene == Physics3DShowcaseScene.RagdollLab)
        {
            return Section("Mannequin state", Metric("body", state.RagdollSummary));
        }

        if (state.Scene == Physics3DShowcaseScene.ScannerRange)
        {
            return Section("Scan results", Metric("hits", state.QuerySummary));
        }

        return Section(
            "Station evidence",
            Metric("contacts", state.ContactSummary),
            Metric("constraints", state.Constraints.ToString()));
    }

    private static UiElementBuilder BuildWheelLabEvidence(Physics3DShowcasePanelState state)
    {
        return Section(
            "Driver telemetry",
            Metric("vehicle", state.WheelSummary),
            Metric("course", "YELLOW bumps · BROWN pothole · BLUE side slope · PURPLE platform · RED jump · GREEN brake"),
            Metric("debug", "Gold contact · green normal · cyan suspension · red slip"),
            Metric("keys", "W/S throttle · A/D steer · Space brake · Q wheel type · R reset"));
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
            Ui.Text("The chassis keeps its position and velocity while the complete wheel assembly changes.")
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
        double budgetMilliseconds = _runtime.ActiveConfig.BenchmarkRealTimeBudgetMilliseconds;
        string budgetResult = state.MaximumStepMilliseconds <= budgetMilliseconds
            ? "REALTIME"
            : "OVER 30 HZ BUDGET";
        double visiblePercentage = state.BenchmarkBodies == 0
            ? 0d
            : (100d * state.VisibleBodies) / state.BenchmarkBodies;
        return Section(
            "Scale City status",
            Metric("world", $"{state.BenchmarkBodies:N0} authoritative / {state.AwakeBodies:N0} moving"),
            Metric("view", $"{state.VisibleBodies:N0} sampled / {visiblePercentage:0.0}% of world"),
            Metric("frame", $"{state.MaximumStepMilliseconds:0.###} / {budgetMilliseconds:0.###} ms / {budgetResult}"),
            Metric("flow", $"{state.BenchmarkPathCount:N0} lanes / {state.BenchmarkWaveCount:N0} launch waves"),
            Metric("loop", $"{state.BenchmarkRecycledBodiesLastStep:N0} bodies relaunched this step"));
    }

    private UiElementBuilder BuildReplayEvidence(Physics3DShowcasePanelState state)
    {
        UiElementBuilder phases = Ui.Row(
                ReplayPhasePill("1 RECORD", state.ReplayStatus == Physics3DShowcaseReplayStatus.Recording,
                    state.ReplayStatus is Physics3DShowcaseReplayStatus.ReadyToReplay or Physics3DShowcaseReplayStatus.Replaying or Physics3DShowcaseReplayStatus.Passed),
                ReplayPhasePill("2 REBUILD", state.ReplayStatus == Physics3DShowcaseReplayStatus.ReadyToReplay,
                    state.ReplayStatus is Physics3DShowcaseReplayStatus.Replaying or Physics3DShowcaseReplayStatus.Passed),
                ReplayPhasePill("3 COMPARE", state.ReplayStatus == Physics3DShowcaseReplayStatus.Replaying,
                    state.ReplayStatus == Physics3DShowcaseReplayStatus.Passed))
            .Wrap()
            .Gap(6f);

        if (state.ReplayStatus == Physics3DShowcaseReplayStatus.ReadyToReplay)
        {
            return Section(
                "Replay comparison",
                phases,
                Metric("progress", state.ReplaySummary),
                Metric("lanes", "BLUE recorded · GOLD live replay"),
                ActionButton(
                    "Start Comparison",
                    true,
                    "#285541",
                    "physics3d-replay-start",
                    _ => Enqueue(Physics3DShowcaseCommandKind.StartReplayComparison)));
        }

        if (state.ReplayStatus is Physics3DShowcaseReplayStatus.Passed or Physics3DShowcaseReplayStatus.Failed)
        {
            return Section(
                "Replay comparison",
                phases,
                Metric("result", state.ReplaySummary),
                Metric("lanes", "BLUE recorded · GOLD live replay"),
                ActionButton(
                    "Run Again",
                    false,
                    "#5B4424",
                    "physics3d-replay-run-again",
                    _ => Enqueue(Physics3DShowcaseCommandKind.SelectScene, (int)Physics3DShowcaseScene.ReplayTheater)));
        }

        return Section(
            "Replay comparison",
            phases,
            Metric("progress", state.ReplaySummary),
            Metric("lanes", state.ReplayStatus == Physics3DShowcaseReplayStatus.Recording
                ? "BLUE recording · replay lane waiting"
                : "BLUE recorded · GOLD live replay"));
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
            "Scale City",
            Ui.Row(buttons).Wrap().Gap(7f),
            Ui.Text("World count is authoritative. The camera samples a fixed number of bodies while every lane keeps moving at 30 Hz.")
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
                    .Width(58f),
                Ui.Text(value)
                    .FontSize(11f)
                    .Color("#E2ECF6")
                    .WhiteSpace(UiWhiteSpace.Normal)
                    .FlexGrow(1f))
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
