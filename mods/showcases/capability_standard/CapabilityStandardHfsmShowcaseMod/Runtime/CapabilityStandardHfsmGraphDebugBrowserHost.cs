using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Browser;
using Ludots.UI.Compose;
using Ludots.UI.Surface;
using Ludots.WebUI.Browser;
using Ludots.WebUI.DataPlane;

namespace CapabilityStandardHfsmShowcaseMod.Runtime;

internal static class CapabilityStandardHfsmGraphDebugIds
{
    public const string AssetIndexPath = "CapabilityStandardHfsmShowcaseMod:Assets/hfsm-graph-debug-app/index.html";
    public const string WebUiTopic = "ludots.showcase.capability_standard.hfsm.graph_debug";
    public const string WebUiSessionId = "capability-standard-hfsm-graph-debug";
    public const string SelectNodeCommand = "selectNode";
    public const string OpenGraphCommand = "openGraph";
    public const string KillHeroCommand = "killHero";
    public const string MakeThirstyCommand = "makeThirsty";
    public const string ResetStoryCommand = "resetStory";
}

internal sealed class CapabilityStandardHfsmGraphDebugBrowserHost : IDisposable
{
    private const int PanelWidth = 620;
    private const int PanelMargin = 16;
    private const int DefaultViewportWidth = 1600;
    private const int DefaultViewportHeight = 900;

    private readonly GameEngine _engine;
    private readonly CapabilityStandardHfsmShowcaseRuntime _runtime;
    private readonly IModContext _modContext;

    private IBrowserSurface? _surface;
    private BrowserSurfaceCanvasContent? _browserContent;
    private WebUiDataPlaneRuntime? _dataPlaneRuntime;
    private WebUiQueuedCommandDispatcher? _commandDispatcher;
    private CapabilityStandardHfsmGraphDebugPumpSystem? _pumpSystem;
    private IUiSurfaceHost? _surfaceHost;
    private UiSurfaceLeaseHandle _lease;
    private bool _loggedFirstNonBlankFrame;
    private bool _disposed;

    public CapabilityStandardHfsmGraphDebugBrowserHost(
        GameEngine engine,
        CapabilityStandardHfsmShowcaseRuntime runtime,
        IModContext modContext)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _modContext = modContext ?? throw new ArgumentNullException(nameof(modContext));
    }

    public async Task<bool> TryInstallAsync(ScriptContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(context);

        if (!TryGetBrowserRuntime(context, _engine, out IBrowserRuntime browserRuntime))
        {
            _modContext.Log("[CapabilityStandardHfsmShowcaseMod] No browser runtime capability; HFSM graph editor/debug view stays inactive.");
            return false;
        }

        IUiSurfaceHost surfaceHost = _engine.GetService(CoreServiceKeys.UiSurfaceHost) as IUiSurfaceHost
            ?? throw new InvalidOperationException("HFSM graph editor/debug view requires UiSurfaceHost.");
        _surfaceHost = surfaceHost;

        CapabilityStandardHfsmShowcaseConfig config = _runtime.EnsureConfigForGraphDebug(_engine);
        string assetRoot = ResolveAssetRoot(_engine);
        var resolver = new BrowserAppResourceResolver(assetRoot);
        (float viewportWidth, float viewportHeight) = ResolveVisibleViewport(_engine);
        int panelHeight = ResolvePanelHeight(viewportHeight);
        var viewport = new BrowserViewport(PanelWidth, panelHeight);
        _surface = await browserRuntime
            .CreateSurfaceAsync(viewport, resolver)
            .ConfigureAwait(false);

        SetupDataPlane(config, _surface);

        _browserContent = new BrowserSurfaceCanvasContent(
            _surface,
            hitTestOptions: BrowserSurfaceHitTestOptions.Alpha());
        BrowserSurfaceCanvasContent browserContent = _browserContent;
        _lease = surfaceHost.Acquire(new UiSurfaceLeaseRequest(
            "CapabilityStandardHfsm.GraphDebug",
            UiSurfaceSegment.Overlay,
            priority: 98));
        surfaceHost.Publish(
            _lease,
            UiSurfaceContribution.FromBuilder(() => BuildBrowserRoot(
                browserContent,
                viewportWidth,
                viewportHeight)));

        await _surface.NavigateAsync(new BrowserNavigationRequest(BrowserLocalAppUri.Root)).ConfigureAwait(false);
        _runtime.BrowserGraphDebugViewActive = true;
        _modContext.Log("[CapabilityStandardHfsmShowcaseMod] HFSM graph editor/debug view active: topic " + CapabilityStandardHfsmGraphDebugIds.WebUiTopic);
        return true;
    }

    private void SetupDataPlane(CapabilityStandardHfsmShowcaseConfig config, IBrowserSurface surface)
    {
        var producer = new CapabilityStandardHfsmGraphDebugTopicProducer(config, _runtime);
        var router = new WebUiCommandRouter(
            new CapabilityStandardHfsmGraphDebugGenerationResolver(),
            new CapabilityStandardHfsmGraphDebugPermissionValidator());
        var handler = new CapabilityStandardHfsmGraphDebugCommandHandler(producer);
        router.Register(CapabilityStandardHfsmGraphDebugIds.SelectNodeCommand, handler);
        router.Register(CapabilityStandardHfsmGraphDebugIds.OpenGraphCommand, handler);
        router.Register(CapabilityStandardHfsmGraphDebugIds.KillHeroCommand, handler);
        router.Register(CapabilityStandardHfsmGraphDebugIds.MakeThirstyCommand, handler);
        router.Register(CapabilityStandardHfsmGraphDebugIds.ResetStoryCommand, handler);

        _commandDispatcher = new WebUiQueuedCommandDispatcher(router);
        _dataPlaneRuntime = new WebUiDataPlaneRuntime(_commandDispatcher);
        _dataPlaneRuntime.RegisterTopic(producer);
        _dataPlaneRuntime.AttachSession(
            CapabilityStandardHfsmGraphDebugIds.WebUiSessionId,
            new BrowserMessageBridgeDataTransport(surface.Messages));
        var pump = new WebUiDataPlaneTickPump(_dataPlaneRuntime, _commandDispatcher);
        pump.TrackTopic(CapabilityStandardHfsmGraphDebugIds.WebUiTopic);
        _pumpSystem = new CapabilityStandardHfsmGraphDebugPumpSystem(pump, this);
        _engine.RegisterSystem(_pumpSystem, SystemGroup.InputCollection);
    }

    internal void TryLogFirstNonBlankBrowserFrame()
    {
        if (_loggedFirstNonBlankFrame || _browserContent?.LatestFrame is not BrowserFrame frame)
        {
            return;
        }

        if (!HasVisibleBrowserPixels(frame))
        {
            return;
        }

        _loggedFirstNonBlankFrame = true;
        _modContext.Log(
            "[CapabilityStandardHfsmShowcaseMod] HFSM graph editor/debug browser frame is rendering " +
            $"{frame.Viewport.Width}x{frame.Viewport.Height}, sequence {frame.Sequence}.");
    }

    private static UiElementBuilder BuildBrowserRoot(
        BrowserSurfaceCanvasContent browserContent,
        float viewportWidth,
        float viewportHeight)
    {
        int panelHeight = ResolvePanelHeight(viewportHeight);
        float left = MathF.Max(PanelMargin, viewportWidth - PanelWidth - PanelMargin);
        float top = PanelMargin;
        return Ui.Canvas(browserContent)
            .Id("capability-standard-hfsm-graph-debug-browser-surface")
            .Width(PanelWidth)
            .Height(panelHeight)
            .Absolute(left, top)
            .ZIndex(98);
    }

    private static int ResolvePanelHeight(float viewportHeight)
    {
        return Math.Max(560, (int)MathF.Round(MathF.Max(600f, viewportHeight) - (PanelMargin * 2)));
    }

    private static (float Width, float Height) ResolveVisibleViewport(GameEngine engine)
    {
        float viewportWidth = engine.MergedConfig.WindowWidth > 0 ? engine.MergedConfig.WindowWidth : DefaultViewportWidth;
        float viewportHeight = engine.MergedConfig.WindowHeight > 0 ? engine.MergedConfig.WindowHeight : DefaultViewportHeight;
        if (engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
        {
            viewportWidth = root.Width > 0f ? root.Width : viewportWidth;
            viewportHeight = root.Height > 0f ? root.Height : viewportHeight;
        }

        return (viewportWidth, viewportHeight);
    }

    private static bool HasVisibleBrowserPixels(BrowserFrame frame)
    {
        ReadOnlySpan<byte> pixels = frame.Pixels.Span;
        int stepX = Math.Max(1, frame.Viewport.Width / 12);
        int stepY = Math.Max(1, frame.Viewport.Height / 12);
        for (int y = 0; y < frame.Viewport.Height; y += stepY)
        {
            int rowOffset = y * frame.RowBytes;
            for (int x = 0; x < frame.Viewport.Width; x += stepX)
            {
                int offset = rowOffset + (x * BrowserFrameBuffer.BytesPerPixel);
                if (offset + 3 >= pixels.Length)
                {
                    continue;
                }

                byte blue = pixels[offset];
                byte green = pixels[offset + 1];
                byte red = pixels[offset + 2];
                byte alpha = pixels[offset + 3];
                if (alpha > 16 && (red > 12 || green > 12 || blue > 12))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryGetBrowserRuntime(
        ScriptContext context,
        GameEngine engine,
        out IBrowserRuntime runtime)
    {
        var key = new ServiceKey<IBrowserRuntime>(BrowserRuntimeServiceNames.BrowserRuntime);
        if (context.TryGet(key, out runtime))
        {
            return true;
        }

        if (engine.TryGetService(key, out runtime))
        {
            context.Set(key, runtime);
            return true;
        }

        runtime = null!;
        return false;
    }

    private static string ResolveAssetRoot(GameEngine engine)
    {
        if (engine.VFS != null &&
            engine.VFS.TryResolveFullPath(CapabilityStandardHfsmGraphDebugIds.AssetIndexPath, out string indexPath))
        {
            string? root = Path.GetDirectoryName(indexPath);
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
            {
                return root;
            }
        }

        throw new DirectoryNotFoundException(
            $"HFSM graph editor/debug browser app assets were not found: {CapabilityStandardHfsmGraphDebugIds.AssetIndexPath}");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _runtime.BrowserGraphDebugViewActive = false;
        _pumpSystem?.Dispose();
        _pumpSystem = null;
        if (_lease.IsValid && _surfaceHost != null)
        {
            _surfaceHost.ReleaseLease(ref _lease);
        }

        _surfaceHost = null;
        _browserContent?.Dispose();
        _browserContent = null;
        if (_dataPlaneRuntime != null)
        {
            _dataPlaneRuntime.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _dataPlaneRuntime = null;
        }

        _commandDispatcher?.Dispose();
        _commandDispatcher = null;
        _surface?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _surface = null;
    }
}

internal sealed class CapabilityStandardHfsmGraphDebugPumpSystem : ISystem<float>
{
    private const float TopicPublishIntervalSeconds = 0.08f;

    private readonly WebUiDataPlaneTickPump _pump;
    private readonly CapabilityStandardHfsmGraphDebugBrowserHost _host;
    private float _secondsSincePublish;
    private bool _disposed;

    public CapabilityStandardHfsmGraphDebugPumpSystem(
        WebUiDataPlaneTickPump pump,
        CapabilityStandardHfsmGraphDebugBrowserHost host)
    {
        _pump = pump ?? throw new ArgumentNullException(nameof(pump));
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }

    public void Update(in float dt)
    {
        if (_disposed)
        {
            return;
        }

        _host.TryLogFirstNonBlankBrowserFrame();
        _pump.FlushCommandsAsync().AsTask().GetAwaiter().GetResult();
        _secondsSincePublish += MathF.Max(0f, dt);
        if (_secondsSincePublish < TopicPublishIntervalSeconds)
        {
            return;
        }

        _secondsSincePublish = 0f;
        _pump.PublishTopicsAsync().AsTask().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _disposed = true;
    }
}

internal sealed class CapabilityStandardHfsmGraphDebugTopicProducer : IWebUiTopicProducer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly CapabilityStandardHfsmShowcaseConfig _config;
    private readonly CapabilityStandardHfsmShowcaseRuntime _runtime;
    private int _revision;
    private string _selectedNodeId = CapabilityStandardHfsmShowcaseRuntime.StateGoDrink;
    private string _activeGraphId;
    private string _lastCommand = "none";
    private string _lastCommandStatus = "idle";

    public CapabilityStandardHfsmGraphDebugTopicProducer(
        CapabilityStandardHfsmShowcaseConfig config,
        CapabilityStandardHfsmShowcaseRuntime runtime)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _activeGraphId = config.GraphDebug.RootGraphId;
    }

    public string Topic => CapabilityStandardHfsmGraphDebugIds.WebUiTopic;

    public bool TryCreateSnapshot(in WebUiTopicContext context, out WebUiOutboundPacket packet)
    {
        bool isSubscriptionSnapshot = context.RequestId != 0;
        if (!isSubscriptionSnapshot)
        {
            _revision++;
        }

        CapabilityStandardHfsmGraphDebugSnapshot snapshot = BuildSnapshot();
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);
        packet = new WebUiOutboundPacket(
            context.SessionId,
            Topic,
            isSubscriptionSnapshot ? WebUiPacketKind.Snapshot : WebUiPacketKind.Delta,
            WebUiDeliverySemantics.LatestWins,
            payload,
            "application/json",
            context.RequestId);
        return true;
    }

    public WebUiCommandResult ApplyCommand(WebUiCommandRequest request)
    {
        WebUiCommandResult result = request.Name switch
        {
            CapabilityStandardHfsmGraphDebugIds.SelectNodeCommand => SelectNode(request.Payload),
            CapabilityStandardHfsmGraphDebugIds.OpenGraphCommand => OpenGraph(request.Payload),
            CapabilityStandardHfsmGraphDebugIds.KillHeroCommand => ApplyRuntimeCommand(_runtime.ApplyFatalDamage, "killHero"),
            CapabilityStandardHfsmGraphDebugIds.MakeThirstyCommand => ApplyRuntimeCommand(_runtime.MakeThirsty, "makeThirsty"),
            CapabilityStandardHfsmGraphDebugIds.ResetStoryCommand => ApplyRuntimeCommand(_runtime.ResetStory, "resetStory"),
            _ => WebUiCommandResult.Fail("unknown_command", $"Unsupported HFSM graph debug command '{request.Name}'.")
        };

        _lastCommand = request.Name;
        _lastCommandStatus = result.Success ? "ack" : $"{result.ErrorCode}: {result.Message}";
        return result;
    }

    private CapabilityStandardHfsmGraphDebugSnapshot BuildSnapshot()
    {
        CapabilityStandardHfsmShowcaseSnapshot runtimeSnapshot = _runtime.Snapshot;
        string activeStateId = runtimeSnapshot.IsActive ? runtimeSnapshot.StateId : string.Empty;
        HfsmImplementationGraphConfig? activeImplementation = _config.GraphDebug.FindImplementationForState(activeStateId);
        string[] activeStatePathIds = BuildActiveStatePathIds(activeStateId);
        string[] activeOpNodeIds = BuildActiveOpNodeIds(activeImplementation);
        var rootGraph = new HfsmGraphDebugGraphView(
            _config.GraphDebug.RootGraphId,
            _config.GraphDebug.RootTitle,
            "hfsm",
            string.Empty,
            string.Empty,
            _config.GraphDebug.Nodes.ToArray(),
            _config.GraphDebug.Edges.ToArray());
        HfsmGraphDebugGraphView[] implementations = _config.GraphDebug.Implementations
            .Select(static graph => new HfsmGraphDebugGraphView(
                graph.Id,
                graph.Title,
                "implementation",
                graph.OwnerStateId,
                graph.Summary,
                graph.Nodes.ToArray(),
                graph.Edges.ToArray()))
            .ToArray();

        return new CapabilityStandardHfsmGraphDebugSnapshot(
            1,
            _revision,
            "hfsm-editor-debug",
            new HfsmGraphDebugSelectedEntityView(_config.HeroInstanceId, "HFSM Runner"),
            new HfsmGraphDebugRuntimeView(
                runtimeSnapshot.IsActive,
                activeStateId,
                runtimeSnapshot.StateLabel,
                runtimeSnapshot.StatePath,
                runtimeSnapshot.PlayerStory,
                runtimeSnapshot.LastEvent,
                runtimeSnapshot.Health,
                runtimeSnapshot.Water,
                runtimeSnapshot.LapCount,
                runtimeSnapshot.TransitionCount,
                runtimeSnapshot.HeroXCm,
                runtimeSnapshot.HeroYCm,
                runtimeSnapshot.Dead),
            rootGraph,
            implementations,
            _activeGraphId,
            _selectedNodeId,
            activeStateId,
            activeStatePathIds,
            activeImplementation?.Id ?? string.Empty,
            activeOpNodeIds,
            new HfsmGraphDebugCommandView(_lastCommand, _lastCommandStatus));
    }

    private string[] BuildActiveStatePathIds(string stateId)
    {
        if (string.IsNullOrWhiteSpace(stateId))
        {
            return Array.Empty<string>();
        }

        var states = new List<string>(4);
        string current = stateId;
        while (!string.IsNullOrWhiteSpace(current))
        {
            states.Insert(0, current);
            HfsmStateConfig state = _config.RequireState(current);
            current = state.Parent;
        }

        return states.ToArray();
    }

    private string[] BuildActiveOpNodeIds(HfsmImplementationGraphConfig? implementation)
    {
        if (implementation == null || implementation.Nodes.Count == 0 || !_runtime.Snapshot.IsActive)
        {
            return Array.Empty<string>();
        }

        int index = Math.Abs(_revision) % implementation.Nodes.Count;
        return new[] { implementation.Nodes[index].Id };
    }

    private WebUiCommandResult SelectNode(JsonElement payload)
    {
        if (!TryReadString(payload, "nodeId", out string nodeId))
        {
            return WebUiCommandResult.Fail("invalid_payload", "selectNode requires nodeId.");
        }

        if (!_config.GraphDebug.ContainsNode(nodeId))
        {
            return WebUiCommandResult.Fail("unknown_node", $"HFSM graph node '{nodeId}' does not exist.");
        }

        _selectedNodeId = nodeId;
        return WebUiCommandResult.Ok();
    }

    private WebUiCommandResult OpenGraph(JsonElement payload)
    {
        if (!TryReadString(payload, "graphId", out string graphId))
        {
            return WebUiCommandResult.Fail("invalid_payload", "openGraph requires graphId.");
        }

        if (!string.Equals(graphId, _config.GraphDebug.RootGraphId, StringComparison.Ordinal) &&
            _config.GraphDebug.FindImplementation(graphId) == null)
        {
            return WebUiCommandResult.Fail("unknown_graph", $"HFSM graph '{graphId}' does not exist.");
        }

        _activeGraphId = graphId;
        return WebUiCommandResult.Ok();
    }

    private WebUiCommandResult ApplyRuntimeCommand(Action command, string commandName)
    {
        if (!_runtime.IsActive)
        {
            return WebUiCommandResult.Fail("showcase_inactive", $"Cannot run {commandName}; HFSM showcase map is not active.");
        }

        command();
        return WebUiCommandResult.Ok();
    }

    private static bool TryReadString(JsonElement payload, string propertyName, out string value)
    {
        value = string.Empty;
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty(propertyName, out JsonElement element) ||
            element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        string? raw = element.GetString();
        if (string.IsNullOrWhiteSpace(raw) || raw.Trim().Length != raw.Length)
        {
            return false;
        }

        value = raw;
        return true;
    }
}

internal sealed class CapabilityStandardHfsmGraphDebugCommandHandler : IWebUiCommandHandler
{
    private readonly CapabilityStandardHfsmGraphDebugTopicProducer _producer;

    public CapabilityStandardHfsmGraphDebugCommandHandler(CapabilityStandardHfsmGraphDebugTopicProducer producer)
    {
        _producer = producer ?? throw new ArgumentNullException(nameof(producer));
    }

    public ValueTask<WebUiCommandResult> HandleAsync(
        WebUiCommandRequest request,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(_producer.ApplyCommand(request));
    }
}

internal sealed class CapabilityStandardHfsmGraphDebugGenerationResolver : IWebUiEntityGenerationResolver
{
    public bool IsCurrent(WebUiEntityRef entityRef)
    {
        return entityRef.StableId <= 0 && entityRef.Generation <= 0;
    }
}

internal sealed class CapabilityStandardHfsmGraphDebugPermissionValidator : IWebUiCommandPermissionValidator
{
    private static readonly HashSet<string> AllowedCommands = new(StringComparer.Ordinal)
    {
        CapabilityStandardHfsmGraphDebugIds.SelectNodeCommand,
        CapabilityStandardHfsmGraphDebugIds.OpenGraphCommand,
        CapabilityStandardHfsmGraphDebugIds.KillHeroCommand,
        CapabilityStandardHfsmGraphDebugIds.MakeThirstyCommand,
        CapabilityStandardHfsmGraphDebugIds.ResetStoryCommand
    };

    public bool CanUse(WebUiCommandRequest request, out string error)
    {
        if (AllowedCommands.Contains(request.Name))
        {
            error = string.Empty;
            return true;
        }

        error = $"Command '{request.Name}' is not allowed in CapabilityStandardHfsmShowcaseMod.";
        return false;
    }
}

internal sealed record CapabilityStandardHfsmGraphDebugSnapshot(
    int SchemaVersion,
    int Revision,
    string Mode,
    HfsmGraphDebugSelectedEntityView SelectedEntity,
    HfsmGraphDebugRuntimeView Runtime,
    HfsmGraphDebugGraphView RootGraph,
    HfsmGraphDebugGraphView[] Implementations,
    string ActiveGraphId,
    string SelectedNodeId,
    string ActiveStateId,
    string[] ActiveStatePathIds,
    string ActiveImplementationGraphId,
    string[] ActiveOpNodeIds,
    HfsmGraphDebugCommandView Command);

internal sealed record HfsmGraphDebugSelectedEntityView(string InstanceId, string Name);

internal sealed record HfsmGraphDebugRuntimeView(
    bool IsActive,
    string StateId,
    string StateLabel,
    string StatePath,
    string PlayerStory,
    string LastEvent,
    int Health,
    int Water,
    int LapCount,
    int TransitionCount,
    int HeroXCm,
    int HeroYCm,
    bool Dead);

internal sealed record HfsmGraphDebugGraphView(
    string Id,
    string Title,
    string Kind,
    string OwnerStateId,
    string Summary,
    HfsmGraphNodeConfig[] Nodes,
    HfsmGraphEdgeConfig[] Edges);

internal sealed record HfsmGraphDebugCommandView(string LastCommand, string LastStatus);
