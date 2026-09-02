using System.Text.Json;
using Ludots.Core.Modding;
using Ludots.WebUI.DataPlane;

namespace SangoWebUiSpikeMod;

public sealed class SangoMapStatsTopicProducer : IWebUiTopicProducer
{
    public const string TopicName = "sango.map.stats";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IModContext _modContext;
    private readonly SangoCitySample[] _cities =
    [
        new("city-luoyang", "洛阳", "曹操", 215000),
        new("city-yecheng", "邺城", "袁绍", 178000),
        new("city-xuchang", "许昌", "曹操", 142000),
        new("city-puyang", "濮阳", "曹操", 96000),
        new("city-chenliu", "陈留", "张绣", 88000),
        new("city-wan", "宛城", "张绣", 104000),
        new("city-xiangyang", "襄阳", "刘表", 156000),
        new("city-chengdu", "成都", "刘璋", 189000)
    ];

    private readonly Dictionary<string, int> _terrainHistogram = new()
    {
        ["平原"] = 3840,
        ["山地"] = 1260,
        ["森林"] = 1520,
        ["河泽"] = 640,
        ["沙漠"] = 300,
        ["关隘"] = 40
    };

    private int _tick;
    private int _commandCount;
    private string? _selectedCityId;
    private string _lastCommand = "snapshot";
    private string _lastCommandStatus = "idle";

    public SangoMapStatsTopicProducer(IModContext modContext)
    {
        _modContext = modContext ?? throw new ArgumentNullException(nameof(modContext));
    }

    public string Topic => TopicName;

    public bool TryCreateSnapshot(in WebUiTopicContext context, out WebUiOutboundPacket packet)
    {
        bool isSubscriptionSnapshot = context.RequestId != 0;
        if (!isSubscriptionSnapshot)
        {
            _tick++;
        }

        SangoMapStatsSnapshot snapshot = BuildSnapshot();
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);
        packet = new WebUiOutboundPacket(
            context.SessionId,
            TopicName,
            isSubscriptionSnapshot ? WebUiPacketKind.Snapshot : WebUiPacketKind.Delta,
            WebUiDeliverySemantics.LatestWins,
            payload,
            "application/json",
            context.RequestId);
        return true;
    }

    public WebUiCommandResult ApplyCommand(WebUiCommandRequest request)
    {
        _commandCount++;
        _lastCommand = request.Name;

        WebUiCommandResult result = request.Name switch
        {
            "sango.selectCity" => SelectCity(request),
            _ => WebUiCommandResult.Fail("unknown_command", $"Unsupported Sango spike command '{request.Name}'.")
        };

        _lastCommandStatus = result.Success ? "ack" : $"{result.ErrorCode}: {result.Message}";
        return result;
    }

    private WebUiCommandResult SelectCity(WebUiCommandRequest request)
    {
        if (!request.Payload.TryGetProperty("cityId", out JsonElement cityElement) ||
            cityElement.ValueKind != JsonValueKind.String)
        {
            return WebUiCommandResult.Fail("invalid_payload", "sango.selectCity requires payload.cityId.");
        }

        string cityId = cityElement.GetString() ?? string.Empty;
        if (_cities.All(city => city.Id != cityId))
        {
            return WebUiCommandResult.Fail("city_not_found", $"Unknown Sango sample city '{cityId}'.");
        }

        _modContext.Log($"[SangoWebUiSpikeMod] sango.selectCity received: {cityId}");
        _selectedCityId = cityId;
        return WebUiCommandResult.Ok();
    }

    private SangoMapStatsSnapshot BuildSnapshot()
    {
        var cities = _cities
            .Select(city => new SangoCityView(city.Id, city.Name, city.Faction, city.Population, city.Id == _selectedCityId))
            .ToArray();

        return new SangoMapStatsSnapshot(
            _tick,
            "群雄割据·中原",
            120,
            90,
            _terrainHistogram,
            cities,
            new SangoSpikeDiagnosticsView(_lastCommand, _lastCommandStatus, _commandCount, _selectedCityId ?? string.Empty));
    }

    private readonly record struct SangoCitySample(string Id, string Name, string Faction, int Population);
}

public sealed class SangoWebUiSpikeCommandHandler : IWebUiCommandHandler
{
    private readonly SangoMapStatsTopicProducer _producer;

    public SangoWebUiSpikeCommandHandler(SangoMapStatsTopicProducer producer)
    {
        _producer = producer ?? throw new ArgumentNullException(nameof(producer));
    }

    public ValueTask<WebUiCommandResult> HandleAsync(WebUiCommandRequest request, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(_producer.ApplyCommand(request));
    }
}

public sealed class SangoWebUiSpikeGenerationResolver : IWebUiEntityGenerationResolver
{
    public bool IsCurrent(WebUiEntityRef entityRef)
    {
        return entityRef.StableId <= 0 && entityRef.Generation <= 0;
    }
}

public sealed class SangoWebUiSpikePermissionValidator : IWebUiCommandPermissionValidator
{
    private static readonly HashSet<string> AllowedCommands = new(StringComparer.Ordinal)
    {
        "sango.selectCity"
    };

    public bool CanUse(WebUiCommandRequest request, out string error)
    {
        if (AllowedCommands.Contains(request.Name))
        {
            error = string.Empty;
            return true;
        }

        error = $"Command '{request.Name}' is not allowed in SangoWebUiSpikeMod.";
        return false;
    }
}

internal sealed record SangoMapStatsSnapshot(
    int Tick,
    string MapName,
    int Width,
    int Height,
    Dictionary<string, int> TerrainHistogram,
    SangoCityView[] Cities,
    SangoSpikeDiagnosticsView Diagnostics);

internal sealed record SangoCityView(
    string Id,
    string Name,
    string Faction,
    int Population,
    bool Selected);

internal sealed record SangoSpikeDiagnosticsView(
    string LastCommand,
    string LastCommandStatus,
    int CommandCount,
    string SelectedCityId);
