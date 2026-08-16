using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Actions;
using Ludots.UI.Surface;

namespace CapabilityStandardModExtensibleRuntimeShowcaseShared;

public sealed class ExtensibleRuntimeShowcaseScenario
{
    public string MapId { get; init; } = string.Empty;
    public string PanelElementId { get; init; } = string.Empty;
    public string PrimaryButtonElementId { get; init; } = string.Empty;
    public string SurfaceOwnerId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string FeatureLabel { get; init; } = string.Empty;
    public string PrimaryButtonLabel { get; init; } = string.Empty;
    public string AccentColor { get; init; } = "#6FAF92";
    public string ReadyText { get; init; } = "Ready.";
    public string[] ProofLines { get; init; } = Array.Empty<string>();
    public Action<ExtensibleRuntimeShowcaseRuntime, GameEngine>? OnActivated { get; init; }
    public Action<ExtensibleRuntimeShowcaseRuntime, GameEngine>? OnUpdate { get; init; }
    public Action<ExtensibleRuntimeShowcaseRuntime, GameEngine>? OnPrimaryAction { get; init; }
}

public readonly record struct ExtensibleRuntimeShowcasePanelState(
    string PanelElementId,
    string PrimaryButtonElementId,
    string Title,
    string FeatureLabel,
    string PrimaryButtonLabel,
    string AccentColor,
    string LastEvent,
    string MetricALabel,
    string MetricAValue,
    string MetricBLabel,
    string MetricBValue,
    int PrimaryActionCount,
    int PulseStep,
    bool HighlightRight,
    string[] ProofLines)
{
    public static ExtensibleRuntimeShowcasePanelState Empty => new(
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        "#6FAF92",
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        0,
        0,
        false,
        Array.Empty<string>());
}

public sealed class ExtensibleRuntimeShowcaseRuntime : IBenchmarkSceneController
{
    private readonly ExtensibleRuntimeShowcaseScenario _scenario;
    private GameEngine? _activeEngine;
    private string _lastEvent;
    private string _metricALabel = "Loaded";
    private string _metricAValue = "pending";
    private string _metricBLabel = "Visible";
    private string _metricBValue = "pending";
    private int _primaryActionCount;
    private int _pulseStep;
    private bool _highlightRight;

    public ExtensibleRuntimeShowcaseRuntime(ExtensibleRuntimeShowcaseScenario scenario)
    {
        _scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
        _lastEvent = string.IsNullOrWhiteSpace(scenario.ReadyText) ? "Showcase ready." : scenario.ReadyText;
    }

    public ExtensibleRuntimeShowcaseScenario Scenario => _scenario;
    public bool IsActive => _activeEngine != null && IsShowcaseMap(_activeEngine.CurrentMapSession?.MapId.Value);
    public bool SupportsScatterControl => false;
    public bool IsCleanPerformanceScene => false;
    public bool SuppressHostDiagnosticUi => false;
    public bool SuppressHostDebugGuides => false;
    public int ScatterMin => 0;
    public int ScatterMax => 0;
    public int ScatterTarget => 0;
    public int ScatterAppliedTotal => 0;
    public int PrimaryActionCount => _primaryActionCount;
    public int PulseStep => _pulseStep;
    public bool HighlightRight => _highlightRight;
    public string LastEvent => _lastEvent;

    public void SetScatterTargetFromRatio(float ratio) => ThrowScatterUnsupported();
    public void ApplyScatterTarget() => ThrowScatterUnsupported();
    public void ApplyScatterLayout(int total) => ThrowScatterUnsupported();

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        GameEngine? engine = context.Get(CoreServiceKeys.Engine);
        if (engine == null)
        {
            return Task.CompletedTask;
        }

        if (!IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
        {
            Disable();
            return Task.CompletedTask;
        }

        _activeEngine = engine;
        _lastEvent = _scenario.ReadyText;
        _scenario.OnActivated?.Invoke(this, engine);
        return Task.CompletedTask;
    }

    public Task HandleMapUnloadedAsync(ScriptContext context)
    {
        GameEngine? engine = context.Get(CoreServiceKeys.Engine);
        string? mapId = context.TryGet(CoreServiceKeys.MapId, out var contextMapId)
            ? contextMapId.Value
            : engine?.CurrentMapSession?.MapId.Value;
        if (IsShowcaseMap(mapId))
        {
            Disable();
        }

        return Task.CompletedTask;
    }

    public void Update(GameEngine engine, float deltaTime)
    {
        if (!IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
        {
            return;
        }

        _pulseStep++;
        _scenario.OnUpdate?.Invoke(this, engine);
    }

    public void TriggerPrimaryAction(GameEngine engine)
    {
        _primaryActionCount++;
        _pulseStep = 0;
        _scenario.OnPrimaryAction?.Invoke(this, engine);
    }

    public void SetLastEvent(string value)
    {
        _lastEvent = string.IsNullOrWhiteSpace(value) ? _scenario.ReadyText : value;
    }

    public void SetMetricA(string label, string value)
    {
        _metricALabel = label;
        _metricAValue = value;
    }

    public void SetMetricB(string label, string value)
    {
        _metricBLabel = label;
        _metricBValue = value;
    }

    public void SetHighlightRight(bool value)
    {
        _highlightRight = value;
    }

    public ExtensibleRuntimeShowcasePanelState CapturePanelState(GameEngine engine)
    {
        if (!IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
        {
            return ExtensibleRuntimeShowcasePanelState.Empty;
        }

        return new ExtensibleRuntimeShowcasePanelState(
            _scenario.PanelElementId,
            _scenario.PrimaryButtonElementId,
            _scenario.Title,
            _scenario.FeatureLabel,
            _scenario.PrimaryButtonLabel,
            _scenario.AccentColor,
            _lastEvent,
            _metricALabel,
            _metricAValue,
            _metricBLabel,
            _metricBValue,
            _primaryActionCount,
            _pulseStep,
            _highlightRight,
            _scenario.ProofLines);
    }

    private bool IsShowcaseMap(string? mapId)
    {
        return string.Equals(mapId, _scenario.MapId, StringComparison.Ordinal);
    }

    private void Disable()
    {
        _activeEngine = null;
        _primaryActionCount = 0;
        _pulseStep = 0;
        _highlightRight = false;
    }

    private static void ThrowScatterUnsupported()
    {
        throw new NotSupportedException("Extensible runtime showcases do not support scatter control.");
    }
}

public sealed class ExtensibleRuntimeShowcasePresentationSystem : Arch.System.ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly ExtensibleRuntimeShowcaseRuntime _runtime;
    private readonly DebugDrawCommandBuffer _debugDraw;
    private readonly ExtensibleRuntimeShowcasePanelController _panel;

    public ExtensibleRuntimeShowcasePresentationSystem(
        GameEngine engine,
        ExtensibleRuntimeShowcaseRuntime runtime,
        DebugDrawCommandBuffer debugDraw)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _debugDraw = debugDraw ?? throw new ArgumentNullException(nameof(debugDraw));
        _panel = new ExtensibleRuntimeShowcasePanelController(runtime);
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        _runtime.Update(_engine, dt);
        if (_engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
        {
            if (_runtime.IsActive)
            {
                throw new InvalidOperationException("Extensible runtime showcase requires UIRoot while its map is active.");
            }

            return;
        }

        if (!_runtime.IsActive)
        {
            _panel.ClearIfOwned(root);
            return;
        }

        ExtensibleRuntimeShowcasePanelState state = _runtime.CapturePanelState(_engine);
        _panel.MountOrSync(root, _engine, in state);
        DrawScene(in state);
    }

    private void DrawScene(in ExtensibleRuntimeShowcasePanelState state)
    {
        var left = new Vector2(7.4f, 5.2f);
        var right = new Vector2(11.4f, 5.2f);
        var focus = state.HighlightRight ? right : left;
        var origin = new Vector2(5.2f, 7.4f);
        float pulse = MathF.Min(state.PulseStep / 60f, 1f);
        float radius = 0.45f + (pulse * 1.65f);
        var accent = ParseDebugColor(state.AccentColor);

        _debugDraw.Lines.Add(new DebugDrawLine2D
        {
            A = origin,
            B = focus,
            Thickness = 3f,
            Color = accent
        });
        _debugDraw.Circles.Add(new DebugDrawCircle2D
        {
            Center = left,
            Radius = state.HighlightRight ? 0.42f : 0.72f,
            Thickness = 3f,
            Color = state.HighlightRight ? DebugDrawColor.Gray : accent
        });
        _debugDraw.Circles.Add(new DebugDrawCircle2D
        {
            Center = right,
            Radius = state.HighlightRight ? 0.72f : 0.42f,
            Thickness = 3f,
            Color = state.HighlightRight ? accent : DebugDrawColor.Gray
        });
        _debugDraw.Circles.Add(new DebugDrawCircle2D
        {
            Center = focus,
            Radius = radius,
            Thickness = 2f,
            Color = accent
        });
        _debugDraw.Circles.Add(new DebugDrawCircle2D
        {
            Center = origin,
            Radius = 0.35f,
            Thickness = 2f,
            Color = new DebugDrawColor(255, 226, 138)
        });
    }

    private static DebugDrawColor ParseDebugColor(string hex)
    {
        if (!UiColor.TryParse(hex, out UiColor color))
        {
            throw new InvalidOperationException($"Unsupported debug draw color literal '{hex}'.");
        }

        return new DebugDrawColor(color.R, color.G, color.B, color.A);
    }
}

public sealed class ExtensibleRuntimeShowcasePanelController
{
    private readonly ExtensibleRuntimeShowcaseRuntime _runtime;
    private ReactivePage<ExtensibleRuntimeShowcasePanelState>? _page;
    private ExtensibleRuntimeShowcasePanelState _lastState = ExtensibleRuntimeShowcasePanelState.Empty;
    private GameEngine? _engine;
    private UiSurfaceLeaseHandle _lease;

    public ExtensibleRuntimeShowcasePanelController(ExtensibleRuntimeShowcaseRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public bool MountOrSync(UIRoot root, GameEngine engine, in ExtensibleRuntimeShowcasePanelState state)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(engine);
        if (engine.GetService(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost surfaceHost)
        {
            throw new InvalidOperationException("Extensible runtime showcase requires UiSurfaceHost.");
        }

        _engine = engine;
        ReactivePage<ExtensibleRuntimeShowcasePanelState> page = EnsurePage();

        bool changed = !_lease.IsValid || !surfaceHost.Revalidate(_lease);
        if (!EqualityComparer<ExtensibleRuntimeShowcasePanelState>.Default.Equals(_lastState, state))
        {
            ExtensibleRuntimeShowcasePanelState snapshot = state;
            page.SetState(_ => snapshot);
            _lastState = snapshot;
            changed = true;
        }

        surfaceHost.PublishReactivePage(
            ref _lease,
            new UiSurfaceLeaseRequest(_runtime.Scenario.SurfaceOwnerId, UiSurfaceSegment.Overlay, priority: 45),
            page);
        return changed;
    }

    public void ClearIfOwned(UIRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (_lease.IsValid)
        {
            if (_engine?.GetService(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost surfaceHost)
            {
                throw new InvalidOperationException("Extensible runtime showcase cannot release UI lease without UiSurfaceHost.");
            }

            surfaceHost.ReleaseLease(ref _lease);
        }

        _engine = null;
        _lastState = ExtensibleRuntimeShowcasePanelState.Empty;
        _page?.SetState(_ => ExtensibleRuntimeShowcasePanelState.Empty);
    }

    private ReactivePage<ExtensibleRuntimeShowcasePanelState> EnsurePage()
    {
        if (_page != null)
        {
            return _page;
        }

        GameEngine engine = RequireEngine();
        if (engine.GetService(CoreServiceKeys.UiTextMeasurer) is not IUiTextMeasurer textMeasurer ||
            engine.GetService(CoreServiceKeys.UiImageSizeProvider) is not IUiImageSizeProvider imageSizeProvider)
        {
            throw new InvalidOperationException("Extensible runtime showcase requires UI text measurer and image size provider.");
        }

        _page = new ReactivePage<ExtensibleRuntimeShowcasePanelState>(
            textMeasurer,
            imageSizeProvider,
            ExtensibleRuntimeShowcasePanelState.Empty,
            BuildRoot);
        return _page;
    }

    private UiElementBuilder BuildRoot(ReactiveContext<ExtensibleRuntimeShowcasePanelState> context)
    {
        ExtensibleRuntimeShowcasePanelState state = context.State;
        return Ui.Panel(
                BuildWorldLabels(state),
                BuildCard(state))
            .Id(state.PanelElementId)
            .WidthPercent(100f)
            .HeightPercent(100f)
            .Absolute(0f, 0f)
            .ZIndex(45);
    }

    private UiElementBuilder BuildWorldLabels(ExtensibleRuntimeShowcasePanelState state)
    {
        return Ui.Panel(
                WorldLabel("Source", "player action", 440f, 640f, "#2D2A18", "#FFE28A"),
                WorldLabel(state.HighlightRight ? "Standby target" : "Active target", state.MetricAValue, 690f, 455f, "#142C2D", state.AccentColor),
                WorldLabel(state.HighlightRight ? "Active target" : "Standby target", state.MetricBValue, 1060f, 455f, "#202739", state.AccentColor))
            .WidthPercent(100f)
            .HeightPercent(100f)
            .Absolute(0f, 0f)
            .ZIndex(46);
    }

    private UiElementBuilder BuildCard(ExtensibleRuntimeShowcasePanelState state)
    {
        var proof = new List<UiElementBuilder>();
        for (int i = 0; i < state.ProofLines.Length; i++)
        {
            proof.Add(Ui.Text(state.ProofLines[i])
                .FontSize(12f)
                .Color("#BAC8D8")
                .WhiteSpace(UiWhiteSpace.Normal));
        }

        return Ui.Panel(
                Ui.Row(
                        Ui.Text(state.Title)
                            .FontSize(20f)
                            .Bold()
                            .Color("#F4F8FF")
                            .FlexGrow(1f),
                        Pill(state.FeatureLabel, "#172A36", state.AccentColor))
                    .Align(UiAlignItems.Center)
                    .Gap(8f),
                Ui.Text(state.LastEvent)
                    .FontSize(13f)
                    .Color("#D6E2F0")
                    .WhiteSpace(UiWhiteSpace.Normal),
                Ui.Row(
                        Stat(state.MetricALabel, state.MetricAValue),
                        Stat(state.MetricBLabel, state.MetricBValue),
                        Stat("Actions", state.PrimaryActionCount.ToString()))
                    .Gap(8f),
                Ui.Panel(proof.ToArray())
                    .Gap(5f)
                    .Padding(10f)
                    .Radius(8f)
                    .Background("#111820")
                    .Border(1f, ParseColor("#2D3C4D")),
                Ui.Button(state.PrimaryButtonLabel, _ => Execute(runtime => runtime.TriggerPrimaryAction(RequireEngine())))
                    .Id(state.PrimaryButtonElementId)
                    .Padding(13f, 9f)
                    .Radius(8f)
                    .Background("#2D5948")
                    .Border(1f, ParseColor("#6FAF92"))
                    .Color("#F3F7FB")
                    .FontSize(13f)
                    .Bold())
            .Width(430f)
            .Padding(16f)
            .Gap(10f)
            .Radius(8f)
            .Background("#101820")
            .Border(1f, ParseColor("#40566B"))
            .Absolute(24f, 24f)
            .ZIndex(64);
    }

    private static UiElementBuilder WorldLabel(string title, string value, float x, float y, string background, string accent)
    {
        return Ui.Panel(
                Ui.Text(title)
                    .FontSize(10f)
                    .Bold()
                    .Color(accent),
                Ui.Text(value)
                    .FontSize(12f)
                    .Color("#F3F7FB")
                    .WhiteSpace(UiWhiteSpace.Normal))
            .Width(190f)
            .Padding(10f)
            .Gap(3f)
            .Radius(8f)
            .Background(background)
            .Border(1f, ParseColor(accent))
            .Absolute(x, y)
            .ZIndex(52);
    }

    private static UiElementBuilder Stat(string title, string value)
    {
        return Ui.Panel(
                Ui.Text(title)
                    .FontSize(10f)
                    .Bold()
                    .Color("#8EA0B5"),
                Ui.Text(value)
                    .FontSize(14f)
                    .Bold()
                    .Color("#F4F8FF"))
            .Width(120f)
            .Padding(9f)
            .Gap(3f)
            .Radius(8f)
            .Background("#17202A")
            .Border(1f, ParseColor("#2D3C4D"));
    }

    private static UiElementBuilder Pill(string text, string background, string color)
    {
        return Ui.Text(text)
            .FontSize(11f)
            .Bold()
            .Color(color)
            .Padding(8f, 4f)
            .Radius(8f)
            .Background(background);
    }

    private void Execute(Action<ExtensibleRuntimeShowcaseRuntime> action)
    {
        GameEngine engine = RequireEngine();
        action(_runtime);
        if (_lease.IsValid &&
            engine.GetService(CoreServiceKeys.UiSurfaceHost) is IUiSurfaceHost surfaceHost)
        {
            surfaceHost.InvalidateLease(_lease);
        }
    }

    private GameEngine RequireEngine()
    {
        return _engine ?? throw new InvalidOperationException("Extensible runtime showcase panel requires an active engine.");
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

public static class ExtensibleRuntimeShowcaseBootstrap
{
    public static void Install(IModContext context, ExtensibleRuntimeShowcaseRuntime runtime, string logName)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(runtime);

        context.Log($"[{logName}] Loaded");
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine? engine = ctx.Get(CoreServiceKeys.Engine);
            if (engine != null)
            {
                engine.SetService(CoreServiceKeys.BenchmarkSceneController, runtime);
                var debugDraw = new DebugDrawCommandBuffer();
                engine.SetService(CoreServiceKeys.DebugDrawCommandBuffer, debugDraw);
                engine.RegisterPresentationSystem(new ExtensibleRuntimeShowcasePresentationSystem(engine, runtime, debugDraw));
            }

            return Task.CompletedTask;
        });

        context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
    }
}
