using System.Text.Json;
using Ludots.Core.Engine;
using Ludots.WebUI.DataPlane;
using ThreeKingdomsTacticsMod.Runtime;

namespace ThreeKingdomsTacticsMod;

internal sealed class ThreeKingdomsTacticsTopicProducer : IWebUiTopicProducer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly GameEngine _engine;
    private readonly ThreeKingdomsTacticsRuntime _runtime;

    public ThreeKingdomsTacticsTopicProducer(GameEngine engine, ThreeKingdomsTacticsRuntime runtime)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public string Topic => ThreeKingdomsTacticsIds.DataPlaneTopic;

    public bool TryCreateSnapshot(in WebUiTopicContext context, out WebUiOutboundPacket packet)
    {
        ThreeKingdomsTacticsSnapshot snapshot = _runtime.BuildSnapshot(_engine);
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);
        packet = new WebUiOutboundPacket(
            context.SessionId,
            Topic,
            context.RequestId == 0 ? WebUiPacketKind.Delta : WebUiPacketKind.Snapshot,
            WebUiDeliverySemantics.LatestWins,
            payload,
            "application/json",
            context.RequestId);
        return true;
    }

    public WebUiCommandResult ApplyCommand(WebUiCommandRequest request)
    {
        switch (request.Name)
        {
            case "selectNext":
                _runtime.SelectNext(_engine);
                return WebUiCommandResult.Ok();
            case "move":
                _runtime.MoveSelected(_engine, ReadInt(request.Payload, "dx", 0), ReadInt(request.Payload, "dy", 0));
                return WebUiCommandResult.Ok();
            case "attack":
                _runtime.AttackNearest(_engine);
                return WebUiCommandResult.Ok();
            case "skill":
                _runtime.CastSelectedSkill(_engine);
                return WebUiCommandResult.Ok();
            case "troop":
                _runtime.CycleTroopType(_engine);
                return WebUiCommandResult.Ok();
            case "endTurn":
                _runtime.EndTurn(_engine);
                return WebUiCommandResult.Ok();
            default:
                return WebUiCommandResult.Fail("unknown_command", $"Unsupported Three Kingdoms command '{request.Name}'.");
        }
    }

    private static int ReadInt(JsonElement payload, string key, int fallback)
    {
        return payload.ValueKind == JsonValueKind.Object &&
               payload.TryGetProperty(key, out JsonElement value) &&
               value.TryGetInt32(out int resolved)
            ? resolved
            : fallback;
    }
}

internal sealed class ThreeKingdomsTacticsCommandHandler : IWebUiCommandHandler
{
    private readonly ThreeKingdomsTacticsTopicProducer _producer;

    public ThreeKingdomsTacticsCommandHandler(ThreeKingdomsTacticsTopicProducer producer)
    {
        _producer = producer;
    }

    public ValueTask<WebUiCommandResult> HandleAsync(WebUiCommandRequest request, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(_producer.ApplyCommand(request));
    }
}

internal sealed class ThreeKingdomsGenerationResolver : IWebUiEntityGenerationResolver
{
    public bool IsCurrent(WebUiEntityRef entityRef)
    {
        return entityRef.StableId <= 0 && entityRef.Generation <= 0;
    }
}

internal sealed class ThreeKingdomsPermissionValidator : IWebUiCommandPermissionValidator
{
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "selectNext",
        "move",
        "attack",
        "skill",
        "troop",
        "endTurn"
    };

    public bool CanUse(WebUiCommandRequest request, out string error)
    {
        if (Allowed.Contains(request.Name))
        {
            error = string.Empty;
            return true;
        }

        error = $"Command '{request.Name}' is not allowed by ThreeKingdomsTacticsMod.";
        return false;
    }
}
