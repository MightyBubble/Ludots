using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Presentation;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;
using Ludots.Platform.Abstractions;

namespace CapabilityStandardPresenterCommandShowcaseMod;

public readonly record struct PresenterCommandShowcasePanelState(
    string PanelElementId,
    string Title,
    string LastEvent,
    string MetricALabel,
    string MetricAValue,
    string MetricBLabel,
    string MetricBValue,
    string[] ButtonIds,
    string[] ButtonLabels,
    string[] ProofLines)
{
    public static PresenterCommandShowcasePanelState Empty => new(
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        Array.Empty<string>(),
        Array.Empty<string>(),
        Array.Empty<string>());
}

public sealed class PresenterCommandShowcaseRuntime : IBenchmarkSceneController
{
    private readonly string _mapId;
    private readonly Dictionary<string, Action<PresenterCommandShowcaseRuntime, GameEngine>> _actions = new(StringComparer.Ordinal);
    private GameEngine? _activeEngine;
    private string _lastEvent = "Ready.";
    private string _metricALabel = "Loaded";
    private string _metricAValue = "pending";
    private string _metricBLabel = "Targets";
    private string _metricBValue = "0";
    private int _pulseStep;

    public PresenterCommandShowcaseRuntime(string mapId, string readyText)
    {
        _mapId = mapId ?? throw new ArgumentNullException(nameof(mapId));
        _lastEvent = string.IsNullOrWhiteSpace(readyText) ? "Ready." : readyText;
    }

    public event Action<PresenterCommandShowcaseRuntime, GameEngine>? Activated;
    public event Action? Deactivated;
    public event Action<PresenterCommandShowcaseRuntime, GameEngine>? Ticked;

    public bool IsActive => _activeEngine != null && IsShowcaseMap(_activeEngine.CurrentMapSession?.MapId.Value);
    public bool SupportsScatterControl => false;
    public bool IsCleanPerformanceScene => false;
    public bool SuppressHostDiagnosticUi => false;
    public bool SuppressHostDebugGuides => false;
    public int ScatterMin => 0;
    public int ScatterMax => 0;
    public int ScatterTarget => 0;
    public int ScatterAppliedTotal => 0;
    public int PulseStep => _pulseStep;
    public string LastEvent => _lastEvent;

    public void SetScatterTargetFromRatio(float ratio) => ThrowScatterUnsupported();
    public void ApplyScatterTarget() => ThrowScatterUnsupported();
    public void ApplyScatterLayout(int total) => ThrowScatterUnsupported();

    public void RegisterAction(string buttonId, Action<PresenterCommandShowcaseRuntime, GameEngine> action)
    {
        _actions[buttonId ?? throw new ArgumentNullException(nameof(buttonId))] = action ?? throw new ArgumentNullException(nameof(action));
    }

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
        _lastEvent = "Showcase ready.";
        Activated?.Invoke(this, engine);
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
        Ticked?.Invoke(this, engine);
    }

    public void InvokeAction(string buttonId, GameEngine engine)
    {
        if (!_actions.TryGetValue(buttonId, out Action<PresenterCommandShowcaseRuntime, GameEngine>? action))
        {
            throw new InvalidOperationException($"Presenter command showcase action '{buttonId}' is not registered.");
        }

        _pulseStep = 0;
        action(this, engine);
    }

    public void SetLastEvent(string value)
    {
        _lastEvent = string.IsNullOrWhiteSpace(value) ? "Showcase ready." : value;
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

    public PresenterCommandShowcasePanelState CapturePanelState(
        GameEngine engine,
        string panelElementId,
        string title,
        string[] buttonIds,
        string[] buttonLabels,
        string[] proofLines)
    {
        if (!IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
        {
            return PresenterCommandShowcasePanelState.Empty;
        }

        return new PresenterCommandShowcasePanelState(
            panelElementId,
            title,
            _lastEvent,
            _metricALabel,
            _metricAValue,
            _metricBLabel,
            _metricBValue,
            buttonIds,
            buttonLabels,
            proofLines);
    }

    private bool IsShowcaseMap(string? mapId)
    {
        return string.Equals(mapId, _mapId, StringComparison.Ordinal);
    }

    private void Disable()
    {
        _activeEngine = null;
        _pulseStep = 0;
        Deactivated?.Invoke();
    }

    private static void ThrowScatterUnsupported()
    {
        throw new NotSupportedException("Presenter command showcase does not support scatter control.");
    }
}

public sealed class PresenterCommandShowcasePanelController
{
    private const string AccentColor = "#7FC8A9";
    private const string PanelElementId = "capability-standard-presenter-command-panel";

    private readonly PresenterCommandShowcaseRuntime _runtime;
    private readonly string _title;
    private readonly string[] _buttonIds;
    private readonly string[] _buttonLabels;
    private readonly string[] _proofLines;
    private ReactivePage<PresenterCommandShowcasePanelState>? _page;
    private PresenterCommandShowcasePanelState _lastState = PresenterCommandShowcasePanelState.Empty;
    private GameEngine? _engine;
    private UiSurfaceLeaseHandle _lease;

    public PresenterCommandShowcasePanelController(
        PresenterCommandShowcaseRuntime runtime,
        string title,
        string[] buttonIds,
        string[] buttonLabels,
        string[] proofLines)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _title = title;
        _buttonIds = buttonIds;
        _buttonLabels = buttonLabels;
        _proofLines = proofLines;
    }

    public void MountOrSync(UIRoot root, GameEngine engine)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(engine);
        if (engine.GetService(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost surfaceHost)
        {
            throw new InvalidOperationException("Presenter command showcase requires UiSurfaceHost.");
        }

        _engine = engine;
        ReactivePage<PresenterCommandShowcasePanelState> page = EnsurePage();
        PresenterCommandShowcasePanelState state = _runtime.CapturePanelState(
            engine,
            PanelElementId,
            _title,
            _buttonIds,
            _buttonLabels,
            _proofLines);

        if (!EqualityComparer<PresenterCommandShowcasePanelState>.Default.Equals(_lastState, state))
        {
            PresenterCommandShowcasePanelState snapshot = state;
            page.SetState(_ => snapshot);
            _lastState = snapshot;
        }

        surfaceHost.PublishReactivePage(
            ref _lease,
            new UiSurfaceLeaseRequest("Showcase.CapabilityStandardPresenterCommand.Panel", UiSurfaceSegment.Overlay, priority: 45),
            page);
    }

    public void ClearIfOwned(UIRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (_lease.IsValid)
        {
            if (_engine?.GetService(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost surfaceHost)
            {
                throw new InvalidOperationException("Presenter command showcase cannot release UI lease without UiSurfaceHost.");
            }

            surfaceHost.ReleaseLease(ref _lease);
        }

        _engine = null;
        _lastState = PresenterCommandShowcasePanelState.Empty;
        _page?.SetState(_ => PresenterCommandShowcasePanelState.Empty);
    }

    private ReactivePage<PresenterCommandShowcasePanelState> EnsurePage()
    {
        if (_page != null)
        {
            return _page;
        }

        GameEngine engine = RequireEngine();
        if (engine.GetService(CoreServiceKeys.UiTextMeasurer) is not IUiTextMeasurer textMeasurer ||
            engine.GetService(CoreServiceKeys.UiImageSizeProvider) is not IUiImageSizeProvider imageSizeProvider)
        {
            throw new InvalidOperationException("Presenter command showcase requires UI text measurer and image size provider.");
        }

        _page = new ReactivePage<PresenterCommandShowcasePanelState>(
            textMeasurer,
            imageSizeProvider,
            PresenterCommandShowcasePanelState.Empty,
            BuildRoot);
        return _page;
    }

    private UiElementBuilder BuildRoot(ReactiveContext<PresenterCommandShowcasePanelState> context)
    {
        PresenterCommandShowcasePanelState state = context.State;
        return Ui.Panel(
                Ui.Panel(BuildButtonGrid(state)).Width(232f).Gap(6f),
                BuildCard(state))
            .Id(state.PanelElementId)
            .WidthPercent(100f)
            .HeightPercent(100f)
            .Absolute(0f, 0f)
            .ZIndex(45);
    }

    private UiElementBuilder[] BuildButtonGrid(PresenterCommandShowcasePanelState state)
    {
        var rows = new List<UiElementBuilder>();
        for (int i = 0; i < state.ButtonIds.Length; i += 2)
        {
            UiElementBuilder left = BuildButton(state, i);
            UiElementBuilder right = i + 1 < state.ButtonIds.Length
                ? BuildButton(state, i + 1)
                : Ui.Text(string.Empty).FontSize(1f);
            rows.Add(Ui.Row(left, right).Gap(6f));
        }

        return rows.ToArray();
    }

    private UiElementBuilder BuildButton(PresenterCommandShowcasePanelState state, int index)
    {
        string buttonId = state.ButtonIds[index];
        return Ui.Button(state.ButtonLabels[index], _ => _runtime.InvokeAction(buttonId, RequireEngine()))
            .Id(buttonId)
            .Padding(11f, 8f)
            .Radius(8f)
            .Background("#20423A")
            .Border(1f, ParseColor(AccentColor))
            .Color("#F3F7FB")
            .FontSize(12f)
            .Bold();
    }

    private UiElementBuilder BuildCard(PresenterCommandShowcasePanelState state)
    {
        var proof = new List<UiElementBuilder>();
        for (int i = 0; i < state.ProofLines.Length; i++)
        {
            proof.Add(Ui.Text(state.ProofLines[i])
                .FontSize(11f)
                .Color("#BAC8D8")
                .WhiteSpace(UiWhiteSpace.Normal));
        }

        return Ui.Panel(
                Ui.Row(
                        Ui.Text(state.Title)
                            .FontSize(18f)
                            .Bold()
                            .Color("#F4F8FF")
                            .FlexGrow(1f),
                        Pill("Presenter Commands", "#172A36", AccentColor))
                    .Align(UiAlignItems.Center)
                    .Gap(8f),
                Ui.Text(state.LastEvent)
                    .FontSize(12f)
                    .Color("#D6E2F0")
                    .WhiteSpace(UiWhiteSpace.Normal),
                Ui.Row(
                        Stat(state.MetricALabel, state.MetricAValue),
                        Stat(state.MetricBLabel, state.MetricBValue))
                    .Gap(8f),
                Ui.Panel(proof.ToArray())
                    .Gap(4f)
                    .Padding(9f)
                    .Radius(8f)
                    .Background("#111820")
                    .Border(1f, ParseColor("#2D3C4D")))
            .Width(438f)
            .Padding(14f)
            .Gap(9f)
            .Radius(8f)
            .Background("#101820")
            .Border(1f, ParseColor("#40566B"))
            .Absolute(286f, 24f)
            .ZIndex(64);
    }

    private static UiElementBuilder Stat(string title, string value)
    {
        return Ui.Panel(
                Ui.Text(title)
                    .FontSize(10f)
                    .Bold()
                    .Color("#8EA0B5"),
                Ui.Text(value)
                    .FontSize(13f)
                    .Bold()
                    .Color("#F4F8FF"))
            .Width(140f)
            .Padding(8f)
            .Gap(3f)
            .Radius(8f)
            .Background("#17202A")
            .Border(1f, ParseColor("#2D3C4D"));
    }

    private static UiElementBuilder Pill(string text, string background, string color)
    {
        return Ui.Text(text)
            .FontSize(10f)
            .Bold()
            .Color(color)
            .Padding(7f, 4f)
            .Radius(8f)
            .Background(background);
    }

    private GameEngine RequireEngine()
    {
        return _engine ?? throw new InvalidOperationException("Presenter command showcase panel requires an active engine.");
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

public sealed class PresenterCommandShowcasePresentationSystem : Arch.System.ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly PresenterCommandShowcaseRuntime _runtime;
    private readonly PresenterCommandShowcasePanelController _panel;
    private readonly DebugDrawCommandBuffer _debugDraw;
    private readonly Vector2[] _stationOrigins;

    public PresenterCommandShowcasePresentationSystem(
        GameEngine engine,
        PresenterCommandShowcaseRuntime runtime,
        PresenterCommandShowcasePanelController panel,
        DebugDrawCommandBuffer debugDraw,
        Vector2[] stationOrigins)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _panel = panel ?? throw new ArgumentNullException(nameof(panel));
        _debugDraw = debugDraw ?? throw new ArgumentNullException(nameof(debugDraw));
        _stationOrigins = stationOrigins ?? throw new ArgumentNullException(nameof(stationOrigins));
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
                throw new InvalidOperationException("Presenter command showcase requires UIRoot while its map is active.");
            }

            return;
        }

        if (!_runtime.IsActive)
        {
            _panel.ClearIfOwned(root);
            return;
        }

        _panel.MountOrSync(root, _engine);
        DrawStations();
    }

    private void DrawStations()
    {
        var accent = new DebugDrawColor(127, 200, 169);
        for (int i = 0; i < _stationOrigins.Length; i++)
        {
            _debugDraw.Circles.Add(new DebugDrawCircle2D
            {
                Center = _stationOrigins[i],
                Radius = 1.6f,
                Thickness = 2f,
                Color = accent
            });
        }
    }
}

public static class PresenterCommandShowcaseInstall
{
    public static void Install(
        IModContext context,
        PresenterCommandShowcaseRuntime runtime,
        PresenterCommandShowcasePanelController panel,
        Vector2[] stationOrigins,
        string logName)
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
                engine.RegisterPresentationSystem(new PresenterCommandShowcasePresentationSystem(
                    engine,
                    runtime,
                    panel,
                    debugDraw,
                    stationOrigins));
            }

            return Task.CompletedTask;
        });

        context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
    }
}
