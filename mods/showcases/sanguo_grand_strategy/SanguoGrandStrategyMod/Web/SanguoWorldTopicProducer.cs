using System.Text.Json;
using Ludots.Core.Engine;
using SanguoGrandStrategyMod.Runtime;
using Ludots.WebUI.DataPlane;

namespace SanguoGrandStrategyMod.Web;

internal sealed class SanguoWorldTopicProducer : IWebUiTopicProducer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly GameEngine _engine;
    private readonly SanguoGrandStrategyRuntime _runtime;
    private int _tick;

    public SanguoWorldTopicProducer(GameEngine engine, SanguoGrandStrategyRuntime runtime)
    {
        _engine = engine;
        _runtime = runtime;
    }

    public string Topic => SanguoGrandStrategyIds.TopicName;

    public bool TryCreateSnapshot(in WebUiTopicContext context, out WebUiOutboundPacket packet)
    {
        _tick++;
        SanguoGrandStrategySnapshot snapshot = _runtime.BuildDataPlaneSnapshot();
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);
        packet = new WebUiOutboundPacket(
            context.SessionId,
            Topic,
            context.RequestId == 0 ? WebUiPacketKind.Delta : WebUiPacketKind.Snapshot,
            WebUiDeliverySemantics.LatestWins,
            payload,
            "application/json",
            context.RequestId,
            _tick);
        return true;
    }

    public WebUiCommandResult ApplyCommand(WebUiCommandRequest request)
    {
        if (!SanguoGrandStrategyIds.IsShowcaseMap(_engine.CurrentMapSession?.MapId.Value))
        {
            return WebUiCommandResult.Fail("map_not_loaded", "Sanguo map is not loaded.");
        }

        switch (request.Name)
        {
            case "selectCity":
                if (!request.Payload.TryGetProperty("cityId", out JsonElement cityIdElement) ||
                    cityIdElement.ValueKind != JsonValueKind.String ||
                    !_runtime.SelectCityById(_engine, cityIdElement.GetString() ?? string.Empty))
                {
                    return WebUiCommandResult.Fail("city_not_found", "selectCity requires payload.cityId for a loaded Sanguo city.");
                }
                return WebUiCommandResult.Ok();
            case "nextCity":
                _runtime.SelectNextCity(_engine);
                return WebUiCommandResult.Ok();
            case "nextUnit":
                _runtime.SelectNextUnitType(_engine);
                return WebUiCommandResult.Ok();
            case "conscript":
                _runtime.Conscript(_engine);
                return WebUiCommandResult.Ok();
            case "trainElite":
                _runtime.TrainElite(_engine);
                return WebUiCommandResult.Ok();
            case "develop":
                _runtime.Develop(_engine);
                return WebUiCommandResult.Ok();
            case "tax":
                _runtime.Tax(_engine);
                return WebUiCommandResult.Ok();
            case "harvest":
                _runtime.Harvest(_engine);
                return WebUiCommandResult.Ok();
            case "march":
                _runtime.March(_engine);
                return WebUiCommandResult.Ok();
            case "resolveBattle":
                _runtime.ResolveBattle(_engine);
                return WebUiCommandResult.Ok();
            case "research":
                _runtime.Research(_engine);
                return WebUiCommandResult.Ok();
            case "diplomacy":
                _runtime.Diplomacy(_engine);
                return WebUiCommandResult.Ok();
            default:
                return WebUiCommandResult.Fail("unknown_command", $"Unsupported Sanguo command '{request.Name}'.");
        }
    }
}

internal sealed class SanguoWorldCommandHandler : IWebUiCommandHandler
{
    private readonly SanguoWorldTopicProducer _producer;

    public SanguoWorldCommandHandler(SanguoWorldTopicProducer producer)
    {
        _producer = producer;
    }

    public ValueTask<WebUiCommandResult> HandleAsync(WebUiCommandRequest request, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(_producer.ApplyCommand(request));
    }
}

internal sealed class SanguoWorldGenerationResolver : IWebUiEntityGenerationResolver
{
    public bool IsCurrent(WebUiEntityRef entityRef) => true;
}

internal sealed class SanguoWorldPermissionValidator : IWebUiCommandPermissionValidator
{
    private static readonly HashSet<string> AllowedCommands = new(StringComparer.Ordinal)
    {
        "selectCity",
        "nextCity",
        "nextUnit",
        "conscript",
        "trainElite",
        "develop",
        "tax",
        "harvest",
        "march",
        "resolveBattle",
        "research",
        "diplomacy"
    };

    public bool CanUse(WebUiCommandRequest request, out string error)
    {
        if (AllowedCommands.Contains(request.Name))
        {
            error = string.Empty;
            return true;
        }

        error = $"Command '{request.Name}' is not allowed in SanguoGrandStrategyMod.";
        return false;
    }
}
