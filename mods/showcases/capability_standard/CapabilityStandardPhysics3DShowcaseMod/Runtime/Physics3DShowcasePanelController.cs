using System;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
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
            "Choose a sample",
            Ui.Row(
                    SceneButton("Bodies", Physics3DShowcaseScene.Bodies, state.Scene),
                    SceneButton("Shapes", Physics3DShowcaseScene.Shapes, state.Scene),
                    SceneButton("Stacking", Physics3DShowcaseScene.Stacking, state.Scene))
                .Wrap()
                .Gap(7f),
            Ui.Row(
                    SceneButton("Continuous", Physics3DShowcaseScene.Continuous, state.Scene),
                    SceneButton("Queries", Physics3DShowcaseScene.Queries, state.Scene),
                    SceneButton("Contacts", Physics3DShowcaseScene.ContactEvents, state.Scene))
                .Wrap()
                .Gap(7f),
            Ui.Row(
                    SceneButton("Joints", Physics3DShowcaseScene.Joints, state.Scene),
                    SceneButton("Replay", Physics3DShowcaseScene.Determinism, state.Scene),
                    SceneButton("Benchmark", Physics3DShowcaseScene.Benchmark, state.Scene))
                .Wrap()
                .Gap(7f));
    }

    private UiElementBuilder BuildPlaybackControls(Physics3DShowcasePanelState state)
    {
        UiElementBuilder sceneAction;
        if (state.Scene == Physics3DShowcaseScene.Determinism)
        {
            sceneAction = ActionButton(
                "Restart Run",
                false,
                "#5B4424",
                "physics3d-replay-restart",
                _ => Enqueue(Physics3DShowcaseCommandKind.SelectScene, (int)Physics3DShowcaseScene.Determinism));
        }
        else
        {
            sceneAction = ActionButton(
                state.Scene == Physics3DShowcaseScene.Continuous ? "Fire" : "Impact",
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
        if (state.Scene == Physics3DShowcaseScene.Benchmark)
        {
            return BuildBenchmarkEvidence(state);
        }

        if (state.Scene == Physics3DShowcaseScene.Determinism)
        {
            return BuildReplayEvidence(state);
        }

        return Section(
            "What this sample proves",
            Metric("queries", state.QuerySummary),
            Metric("contacts", state.ContactSummary),
            Metric("replay", state.ReplaySummary));
    }

    private UiElementBuilder BuildBenchmarkEvidence(Physics3DShowcasePanelState state)
    {
        Physics3DShowcaseConfig config = _runtime.ActiveConfig;
        double budgetMilliseconds = 1_000d / state.FixedHz;
        string budgetResult = state.MaximumStepMilliseconds <= budgetMilliseconds ? "PASS" : "OVER";
        int bodiesPerLayer = checked(config.BenchmarkColumns * config.BenchmarkDepth);
        int layers = (state.BenchmarkBodies + bodiesPerLayer - 1) / bodiesPerLayer;
        return Section(
            "Live server benchmark",
            Metric("world", $"{state.BenchmarkBodies:N0} authoritative · {state.AwakeBodies:N0} awake"),
            Metric("screen", $"{state.VisibleBodies:N0} sampled · simulation count unchanged"),
            Metric("budget", $"{state.MaximumStepMilliseconds:0.###} / {budgetMilliseconds:0.###} ms · {budgetResult}"),
            Metric("motion", $"{layers} counter-flow layers · {config.BenchmarkSpeedCmPerSecond * 0.01f:0.#} m/s"));
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
                    _ => Enqueue(Physics3DShowcaseCommandKind.SelectScene, (int)Physics3DShowcaseScene.Determinism)));
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
                "Server scale",
                Ui.Text("Load the Physics3D Sample Lab map to enable benchmark presets.")
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
                state.Scene == Physics3DShowcaseScene.Benchmark && state.BenchmarkBodies == preset,
                "#285541",
                $"physics3d-benchmark-{preset}",
                _ => Enqueue(Physics3DShowcaseCommandKind.SetBenchmarkBodies, preset));
        }

        return Section(
            "Server scale",
            Ui.Row(buttons).Wrap().Gap(7f),
            Ui.Text("The physics world keeps every body active; rendering samples a bounded subset so the number stays honest.")
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
