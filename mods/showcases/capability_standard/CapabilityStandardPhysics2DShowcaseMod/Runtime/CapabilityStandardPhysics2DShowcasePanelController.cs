using System;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;

namespace CapabilityStandardPhysics2DShowcaseMod.Runtime;

internal sealed class CapabilityStandardPhysics2DShowcasePanelController
{
    private const float PanelWidth = 430f;
    private const float PanelHeight = 760f;

    private readonly CapabilityStandardPhysics2DShowcaseRuntime _runtime;
    private ReactivePage<CapabilityStandardPhysics2DShowcasePanelState>? _page;
    private CapabilityStandardPhysics2DShowcasePanelState _lastState = CapabilityStandardPhysics2DShowcasePanelState.Empty;
    private GameEngine? _engine;
    private UiSurfaceLeaseHandle _lease;

    public CapabilityStandardPhysics2DShowcasePanelController(CapabilityStandardPhysics2DShowcaseRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public bool MountOrSync(UIRoot root, GameEngine engine, in CapabilityStandardPhysics2DShowcasePanelState state)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(engine);
        if (engine.GetService(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost surfaceHost)
        {
            return false;
        }

        _engine = engine;
        ReactivePage<CapabilityStandardPhysics2DShowcasePanelState>? page = EnsurePage();
        if (page == null)
        {
            return false;
        }

        bool changed = !_lease.IsValid || !surfaceHost.Revalidate(_lease);
        if (!StateEquals(in _lastState, in state))
        {
            CapabilityStandardPhysics2DShowcasePanelState snapshot = state;
            page.SetState(_ => snapshot);
            _lastState = snapshot;
            changed = true;
        }

        surfaceHost.PublishReactivePage(
            ref _lease,
            new UiSurfaceLeaseRequest("Showcase.CapabilityStandardPhysics2D.Panel", UiSurfaceSegment.Overlay, priority: 40),
            page);
        return changed;
    }

    public void ClearIfOwned(UIRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);

        if (_lease.IsValid &&
            _engine?.GetService(CoreServiceKeys.UiSurfaceHost) is IUiSurfaceHost surfaceHost)
        {
            surfaceHost.ReleaseLease(ref _lease);
        }

        _engine = null;
        _lastState = CapabilityStandardPhysics2DShowcasePanelState.Empty;
        _page?.SetState(_ => CapabilityStandardPhysics2DShowcasePanelState.Empty);
    }

    private ReactivePage<CapabilityStandardPhysics2DShowcasePanelState>? EnsurePage()
    {
        if (_page != null)
        {
            return _page;
        }

        GameEngine engine = RequireEngine();
        if (engine.GetService(CoreServiceKeys.UiTextMeasurer) is not IUiTextMeasurer textMeasurer ||
            engine.GetService(CoreServiceKeys.UiImageSizeProvider) is not IUiImageSizeProvider imageSizeProvider)
        {
            return null;
        }

        _page = new ReactivePage<CapabilityStandardPhysics2DShowcasePanelState>(
            textMeasurer,
            imageSizeProvider,
            CapabilityStandardPhysics2DShowcasePanelState.Empty,
            BuildRoot);
        return _page;
    }

    private UiElementBuilder BuildRoot(ReactiveContext<CapabilityStandardPhysics2DShowcasePanelState> context)
    {
        CapabilityStandardPhysics2DShowcasePanelState state = context.State;
        return Ui.Panel(
                Ui.Panel(
                        BuildHeader(state),
                        Ui.ScrollView(
                                BuildMetrics(state),
                                BuildSceneControls(state),
                                BuildPolicyControls(state),
                                BuildMaterialControls(state),
                                BuildPolygonControls(state))
                            .Height(610f)
                            .Gap(10f))
                    .Id("capability-standard-physics2d-panel")
                    .Width(PanelWidth)
                    .Height(PanelHeight)
                    .Padding(14f)
                    .Gap(10f)
                    .Radius(8f)
                    .Background("#111822")
                    .Border(1f, ParseColor("#35465A"))
                    .Absolute(16f, 16f)
                    .ZIndex(42))
            .WidthPercent(100f)
            .HeightPercent(100f)
            .Absolute(0f, 0f)
            .ZIndex(42);
    }

    private UiElementBuilder BuildHeader(CapabilityStandardPhysics2DShowcasePanelState state)
    {
        return Ui.Column(
                Ui.Row(
                        Ui.Text(state.Title)
                            .FontSize(20f)
                            .Bold()
                            .Color("#F3C96B")
                            .FlexGrow(1f),
                        BuildPill($"{state.PhysicsHz} Hz", "#163044", "#CFE8FF"))
                    .Gap(8f),
                Ui.Text(state.LastAction)
                    .FontSize(11f)
                    .Color("#B7C4D4")
                    .WhiteSpace(UiWhiteSpace.Normal))
            .Gap(6f);
    }

    private UiElementBuilder BuildMetrics(CapabilityStandardPhysics2DShowcasePanelState state)
    {
        return Section(
            "Diagnostics",
            BuildMetricRow("step", $"{state.PhysicsUpdateMs:0.###} ms"),
            BuildMetricRow("pairs", $"{state.PotentialPairs} potential / {state.ContactPairs} contact"),
            BuildMetricRow("bodies", $"{state.DynamicBodies} dynamic / {state.StaticBodies} static"),
            BuildMetricRow("static", $"{state.DirtyStaticBodies} dirty"),
            BuildMetricRow("broad", $"{state.BroadphaseStrategy} / {state.BroadphaseCellSizeCm} cm"),
            BuildMetricRow("scale", state.ScaleSummary),
            BuildMetricRow("material", state.MaterialSummary));
    }

    private UiElementBuilder BuildSceneControls(CapabilityStandardPhysics2DShowcasePanelState state)
    {
        return Section(
            "Scene",
            Ui.Row(
                    ActionButton("Reset", false, "#523039", _ => Execute(runtime => runtime.ResetScene())),
                    ActionButton("Spawn D", false, "#24435B", _ => Execute(runtime => runtime.SpawnDynamicBatch())),
                    ActionButton("Spawn S", false, "#254B38", _ => Execute(runtime => runtime.SpawnStaticObstacleBatch())))
                .Wrap()
                .Gap(8f),
            BuildStepper($"D batch {state.SpawnBatchDynamic}", "-D", "+D",
                _ => Execute(runtime => runtime.AdjustDynamicBatch(-RequireConfig().DynamicSpawnStep)),
                _ => Execute(runtime => runtime.AdjustDynamicBatch(RequireConfig().DynamicSpawnStep))),
            BuildStepper($"S batch {state.SpawnBatchStatic}", "-S", "+S",
                _ => Execute(runtime => runtime.AdjustStaticBatch(-RequireConfig().StaticObstacleSpawnStep)),
                _ => Execute(runtime => runtime.AdjustStaticBatch(RequireConfig().StaticObstacleSpawnStep))),
            Ui.Row(
                    ActionButton("1K", state.SpawnBatchDynamic == 1000, "#2D5948", _ => Execute(runtime => runtime.ApplyScatterLayout(1000))),
                    ActionButton("10K", state.SpawnBatchDynamic == 10000, "#2D5948", _ => Execute(runtime => runtime.ApplyScatterLayout(10000))),
                    ActionButton("30K", state.SpawnBatchDynamic == 30000, "#2D5948", _ => Execute(runtime => runtime.ApplyScatterLayout(30000))))
                .Wrap()
                .Gap(8f));
    }

    private UiElementBuilder BuildPolicyControls(CapabilityStandardPhysics2DShowcasePanelState state)
    {
        return Section(
            "Policy",
            BuildStepper($"Hz {state.PhysicsHz}", "-Hz", "+Hz",
                _ => Execute(runtime => runtime.AdjustPhysicsHz(-RequireConfig().PhysicsHzStep)),
                _ => Execute(runtime => runtime.AdjustPhysicsHz(RequireConfig().PhysicsHzStep))),
            BuildStepper($"Steps {state.PhysicsMaxSteps}", "-Step", "+Step",
                _ => Execute(runtime => runtime.AdjustMaxSteps(-1)),
                _ => Execute(runtime => runtime.AdjustMaxSteps(1))),
            Ui.Row(
                    ActionButton(state.BroadphaseStrategy, true, "#4B3F22", _ => Execute(runtime => runtime.ToggleBroadphase())),
                    ActionButton("-Cell", false, "#263C52", _ => Execute(runtime => runtime.AdjustBroadphaseCellSize(-RequireConfig().BroadphaseCellSizeStepCm))),
                    ActionButton("+Cell", false, "#263C52", _ => Execute(runtime => runtime.AdjustBroadphaseCellSize(RequireConfig().BroadphaseCellSizeStepCm))))
                .Wrap()
                .Gap(8f));
    }

    private UiElementBuilder BuildMaterialControls(CapabilityStandardPhysics2DShowcasePanelState state)
    {
        return Section(
            "Material",
            BuildStepper("Friction", "-F", "+F",
                _ => Execute(runtime => runtime.AdjustFriction(-RequireConfig().FrictionStep)),
                _ => Execute(runtime => runtime.AdjustFriction(RequireConfig().FrictionStep))),
            BuildStepper("Restitution", "-R", "+R",
                _ => Execute(runtime => runtime.AdjustRestitution(-RequireConfig().RestitutionStep)),
                _ => Execute(runtime => runtime.AdjustRestitution(RequireConfig().RestitutionStep))),
            BuildStepper("Damping", "-Damp", "+Damp",
                _ => Execute(runtime => runtime.AdjustDamping(-RequireConfig().DampingStep)),
                _ => Execute(runtime => runtime.AdjustDamping(RequireConfig().DampingStep))));
    }

    private UiElementBuilder BuildPolygonControls(CapabilityStandardPhysics2DShowcasePanelState state)
    {
        return Section(
            "Polygon",
            BuildMetricRow("draw", state.PolygonDrawMode ? "on" : "off"),
            BuildMetricRow("vertices", state.DrawnPolygonVertices.ToString()),
            Ui.Row(
                    ActionButton(state.PolygonDrawMode ? "Draw On" : "Draw Off", state.PolygonDrawMode, "#394A62", _ => Execute(runtime => runtime.TogglePolygonDrawMode())),
                    ActionButton("Complete", state.DrawnPolygonVertices >= 3, "#2E5744", _ => Execute(runtime => runtime.CompletePolygonObstacle())),
                    ActionButton("Clear", false, "#4D3440", _ => Execute(runtime => runtime.ClearPolygonDraft())))
                .Wrap()
                .Gap(8f));
    }

    private UiElementBuilder BuildStepper(
        string label,
        string minus,
        string plus,
        Action<Ludots.UI.Runtime.Actions.UiActionContext> onMinus,
        Action<Ludots.UI.Runtime.Actions.UiActionContext> onPlus)
    {
        return Ui.Row(
                Ui.Text(label)
                    .FontSize(11f)
                    .Color("#D8E3EF")
                    .FlexGrow(1f),
                ActionButton(minus, false, "#24384F", onMinus),
                ActionButton(plus, false, "#24384F", onPlus))
            .Gap(8f)
            .Align(UiAlignItems.Center);
    }

    private static UiElementBuilder Section(string title, params UiElementBuilder[] children)
    {
        UiElementBuilder[] sectionChildren = new UiElementBuilder[children.Length + 1];
        sectionChildren[0] = Ui.Text(title)
            .FontSize(12f)
            .Bold()
            .Color("#F3C96B");
        Array.Copy(children, 0, sectionChildren, 1, children.Length);
        return Ui.Panel(sectionChildren)
            .Gap(8f)
            .Padding(10f)
            .Radius(8f)
            .Background("#172231")
            .Border(1f, ParseColor("#26384C"));
    }

    private static UiElementBuilder BuildMetricRow(string label, string value)
    {
        return Ui.Row(
                Ui.Text(label)
                    .FontSize(10f)
                    .Color("#8EA0B5")
                    .Width(58f),
                Ui.Text(value)
                    .FontSize(11f)
                    .Color("#E3EDF7")
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
        Action<Ludots.UI.Runtime.Actions.UiActionContext> onClick)
    {
        return Ui.Button(label, onClick)
            .Padding(9f, 7f)
            .Radius(8f)
            .Background(active ? activeBackground : "#202D3D")
            .Border(1f, ParseColor(active ? "#6D8EA6" : "#314155"))
            .Color("#F3F7FB")
            .FontSize(11f);
    }

    private void Execute(Action<CapabilityStandardPhysics2DShowcaseRuntime> action)
    {
        GameEngine engine = RequireEngine();
        action(_runtime);
        if (_lease.IsValid &&
            engine.GetService(CoreServiceKeys.UiSurfaceHost) is IUiSurfaceHost surfaceHost)
        {
            surfaceHost.InvalidateLease(_lease);
        }
    }

    private CapabilityStandardPhysics2DShowcaseConfig RequireConfig()
    {
        return _runtime.ActiveConfig;
    }

    private GameEngine RequireEngine()
    {
        return _engine ?? throw new InvalidOperationException("Physics2D showcase panel requires an active engine.");
    }

    private static UiColor ParseColor(string hex)
    {
        if (!UiColor.TryParse(hex, out UiColor color))
        {
            throw new InvalidOperationException($"Unsupported color literal '{hex}'.");
        }

        return color;
    }

    private static bool StateEquals(
        in CapabilityStandardPhysics2DShowcasePanelState left,
        in CapabilityStandardPhysics2DShowcasePanelState right)
    {
        return string.Equals(left.Title, right.Title, StringComparison.Ordinal) &&
               string.Equals(left.LastAction, right.LastAction, StringComparison.Ordinal) &&
               left.PhysicsHz == right.PhysicsHz &&
               left.PhysicsMaxSteps == right.PhysicsMaxSteps &&
               string.Equals(left.BroadphaseStrategy, right.BroadphaseStrategy, StringComparison.Ordinal) &&
               left.BroadphaseCellSizeCm == right.BroadphaseCellSizeCm &&
               Math.Abs(left.PhysicsUpdateMs - right.PhysicsUpdateMs) < 0.0001d &&
               left.PotentialPairs == right.PotentialPairs &&
               left.ContactPairs == right.ContactPairs &&
               left.DynamicBodies == right.DynamicBodies &&
               left.StaticBodies == right.StaticBodies &&
               left.DirtyStaticBodies == right.DirtyStaticBodies &&
               left.SpawnBatchDynamic == right.SpawnBatchDynamic &&
               left.SpawnBatchStatic == right.SpawnBatchStatic &&
               left.PolygonDrawMode == right.PolygonDrawMode &&
               left.DrawnPolygonVertices == right.DrawnPolygonVertices &&
               string.Equals(left.MaterialSummary, right.MaterialSummary, StringComparison.Ordinal) &&
               string.Equals(left.ScaleSummary, right.ScaleSummary, StringComparison.Ordinal);
    }
}
