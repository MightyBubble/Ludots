using System.Collections.Concurrent;
using System.Numerics;
using System.Text.Json;
using Ludots.Core.Engine;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using Ludots.WebUI.DataPlane;
using ThreeKingdomsTacticsMod;
using ThreeKingdomsTacticsMod.Runtime;

var port = ResolvePort(args);
var repoRoot = FindRepoRoot(Directory.GetCurrentDirectory());
var appIndexPath = Path.Combine(repoRoot, "mods", "ThreeKingdomsTacticsMod", "assets", "tactics-app", "index.html");
var recordingHost = new RecordingGameHost(repoRoot);
recordingHost.Start();

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
var app = builder.Build();

app.MapGet("/", () => Results.File(appIndexPath, "text/html"));
app.MapGet("/health", () => Results.Json(recordingHost.Health()));
app.MapPost("/dataplane/control", async (HttpRequest request) =>
{
    WebUiControlEnvelope? envelope = await JsonSerializer.DeserializeAsync<WebUiControlEnvelope>(
        request.Body,
        RecordingGameHost.JsonOptions);
    if (envelope == null)
    {
        return Results.BadRequest(new { error = "Control envelope is required." });
    }

    recordingHost.Receive(envelope);
    await recordingHost.StepAsync();
    return Results.Json(new { ok = true });
});
app.MapGet("/dataplane/poll", async () =>
{
    await recordingHost.StepAsync();
    return Results.Json(recordingHost.DrainPackets());
});

app.Lifetime.ApplicationStopping.Register(recordingHost.Dispose);

Console.WriteLine($"Three Kingdoms recording host: http://127.0.0.1:{port}/");
await app.RunAsync();

static int ResolvePort(string[] args)
{
    for (int i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], "--port", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(args[i + 1], out int port))
        {
            return port;
        }
    }

    return 5311;
}

static string FindRepoRoot(string startDirectory)
{
    string? dir = Path.GetFullPath(startDirectory);
    while (!string.IsNullOrWhiteSpace(dir))
    {
        if (File.Exists(Path.Combine(dir, "gitbook", "README.md")) ||
            Directory.Exists(Path.Combine(dir, ".git")) ||
            File.Exists(Path.Combine(dir, ".git")))
        {
            return dir;
        }

        dir = Path.GetDirectoryName(dir);
    }

    throw new InvalidOperationException("Could not locate Ludots repository root.");
}

internal sealed class RecordingGameHost : IDisposable
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const float DeltaTime = 1f / 30f;
    private static readonly string[] ModIds =
    [
        "LudotsCoreMod",
        "CoreInputMod",
        "ThreeKingdomsTacticsMod"
    ];

    private readonly string _repoRoot;
    private readonly SemaphoreSlim _stepLock = new(1, 1);
    private readonly PollingDataTransport _transport = new();
    private GameEngine? _engine;
    private ThreeKingdomsTacticsRuntime? _runtime;
    private WebUiQueuedCommandDispatcher? _dispatcher;
    private WebUiDataPlaneRuntime? _dataPlaneRuntime;
    private WebUiDataPlaneTickPump? _pump;
    private int _frame;

    public RecordingGameHost(string repoRoot)
    {
        _repoRoot = repoRoot;
    }

    public void Start()
    {
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(ResolveModPaths(), Path.Combine(_repoRoot, "assets"));
        InstallDummyInput(engine);
        engine.Start();
        engine.LoadMap(ThreeKingdomsTacticsIds.MapId);
        _engine = engine;
        for (int i = 0; i < 12; i++)
        {
            engine.Tick(DeltaTime);
        }

        if (!engine.GlobalContext.TryGetValue(ThreeKingdomsTacticsIds.RuntimeKey, out object? runtimeObj) ||
            runtimeObj is not ThreeKingdomsTacticsRuntime tacticsRuntime)
        {
            throw new InvalidOperationException("Three Kingdoms tactics runtime was not installed.");
        }

        _runtime = tacticsRuntime;
        SetupDataPlane(engine, tacticsRuntime);
    }

    public object Health()
    {
        ThreeKingdomsTacticsSnapshot? snapshot = _runtime?.Snapshot;
        return new
        {
            status = snapshot == null ? "starting" : "ok",
            frame = _frame,
            topic = ThreeKingdomsTacticsIds.DataPlaneTopic,
            outcome = snapshot?.Outcome,
            playerUnits = snapshot?.PlayerUnitsAlive,
            enemyUnits = snapshot?.EnemyUnitsAlive
        };
    }

    public void Receive(WebUiControlEnvelope envelope)
    {
        byte[] payload = WebUiDataPlaneProtocol.SerializeControlEnvelope(envelope);
        var packet = new WebUiInboundPacket(
            envelope.SessionId,
            envelope.Topic,
            WebUiPacketKind.Control,
            WebUiDeliverySemantics.ReliableOrdered,
            payload,
            WebUiDataPlaneProtocol.ControlContentType,
            envelope.RequestId);
        _transport.Receive(packet);
    }

    public async Task StepAsync()
    {
        if (_engine == null || _pump == null)
        {
            return;
        }

        await _stepLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _engine.Tick(DeltaTime);
            _frame++;
            await _pump.FlushCommandsAsync().ConfigureAwait(false);
            await _pump.PublishTopicsAsync().ConfigureAwait(false);
        }
        finally
        {
            _stepLock.Release();
        }
    }

    public object[] DrainPackets()
    {
        return _transport.Drain().Select(ToBrowserPacket).ToArray();
    }

    public void Dispose()
    {
        _dataPlaneRuntime?.Dispose();
        _dispatcher?.Dispose();
        try
        {
            _engine?.Dispose();
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("JobScheduler", StringComparison.Ordinal))
        {
        }
        _stepLock.Dispose();
    }

    private void SetupDataPlane(GameEngine engine, ThreeKingdomsTacticsRuntime runtime)
    {
        var topic = new ThreeKingdomsTacticsTopicProducer(engine, runtime);
        var router = new WebUiCommandRouter(new ThreeKingdomsGenerationResolver(), new ThreeKingdomsPermissionValidator());
        var handler = new ThreeKingdomsTacticsCommandHandler(topic);
        router.Register("selectNext", handler);
        router.Register("move", handler);
        router.Register("attack", handler);
        router.Register("skill", handler);
        router.Register("troop", handler);
        router.Register("develop", handler);
        router.Register("advance", handler);
        router.Register("battle", handler);
        router.Register("endTurn", handler);

        _dispatcher = new WebUiQueuedCommandDispatcher(router);
        _dataPlaneRuntime = new WebUiDataPlaneRuntime(_dispatcher);
        _dataPlaneRuntime.RegisterTopic(topic);
        _dataPlaneRuntime.AttachSession("three-kingdoms-recording", _transport);
        _pump = new WebUiDataPlaneTickPump(_dataPlaneRuntime, _dispatcher);
        _pump.TrackTopic(ThreeKingdomsTacticsIds.DataPlaneTopic);
    }

    private List<string> ResolveModPaths()
    {
        return ModIds
            .Select(modId => Path.Combine(_repoRoot, "mods", modId))
            .ToList();
    }

    private static object ToBrowserPacket(WebUiOutboundPacket packet)
    {
        string kind = packet.Kind.ToString();
        JsonElement payload = default;
        if (string.Equals(packet.ContentType, WebUiDataPlaneProtocol.ControlContentType, StringComparison.Ordinal) &&
            WebUiDataPlaneProtocol.TryParseControlEnvelope(packet.Payload.Span, out WebUiControlEnvelope envelope, out _))
        {
            kind = envelope.Kind;
            payload = envelope.Payload.Clone();
        }
        else if (packet.Payload.Length > 0)
        {
            payload = JsonSerializer.Deserialize<JsonElement>(packet.Payload.Span, JsonOptions).Clone();
        }

        return new
        {
            kind,
            topic = packet.Topic,
            requestId = packet.RequestId,
            clientSeq = packet.ClientSeq,
            payload
        };
    }

    private static void InstallDummyInput(GameEngine engine)
    {
        var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
        var inputHandler = new PlayerInputHandler(new NullInputBackend(), inputConfig);
        engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
        engine.SetService(CoreServiceKeys.UiCaptured, false);
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
}

internal sealed class PollingDataTransport : IWebUiDataTransport
{
    private readonly ConcurrentQueue<WebUiOutboundPacket> _outbound = new();

    public WebUiTransportCapabilities Capabilities { get; } = WebUiTransportCapabilities.MessageBridge();

    public event EventHandler<WebUiInboundPacket>? PacketReceived;

    public void Receive(WebUiInboundPacket packet)
    {
        PacketReceived?.Invoke(this, packet);
    }

    public WebUiOutboundPacket[] Drain()
    {
        var packets = new List<WebUiOutboundPacket>();
        while (_outbound.TryDequeue(out WebUiOutboundPacket packet))
        {
            packets.Add(packet);
        }

        return packets.ToArray();
    }

    public ValueTask SendAsync(WebUiOutboundPacket packet, CancellationToken cancellationToken = default)
    {
        _outbound.Enqueue(packet);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
