using System.Text.Json;
using Ludots.Core.Engine;
using Ludots.WebUI.DataPlane;
using ThreeKingdomsTacticsMod.Runtime;

namespace ThreeKingdomsTacticsMod;

public sealed class ThreeKingdomsTacticsTopicProducer : IWebUiTopicProducer
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
        if (!_runtime.TryBuildSnapshot(_engine, out ThreeKingdomsTacticsSnapshot snapshot))
        {
            packet = default!;
            return false;
        }

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
                if (!TryReadInt(request.Payload, "dx", out int dx) ||
                    !TryReadInt(request.Payload, "dy", out int dy))
                {
                    return WebUiCommandResult.Fail("invalid_payload", "Move command requires integer dx and dy fields.");
                }

                _runtime.MoveSelected(_engine, dx, dy);
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
            case "develop":
                _runtime.DevelopSelected(_engine);
                return WebUiCommandResult.Ok();
            case "advance":
                _runtime.AdvanceSelectedTowardEnemy(_engine);
                return WebUiCommandResult.Ok();
            case "battle":
                _runtime.ResolveCampaignBattle(_engine);
                return WebUiCommandResult.Ok();
            case "endTurn":
                _runtime.EndTurn(_engine);
                return WebUiCommandResult.Ok();
            default:
                return WebUiCommandResult.Fail("unknown_command", $"Unsupported Three Kingdoms command '{request.Name}'.");
        }
    }

    private static bool TryReadInt(JsonElement payload, string key, out int resolved)
    {
        resolved = 0;
        return payload.ValueKind == JsonValueKind.Object &&
               payload.TryGetProperty(key, out JsonElement value) &&
               value.TryGetInt32(out resolved);
    }
}

public sealed class ThreeKingdomsTacticsCommandHandler : IWebUiCommandHandler
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

public sealed class ThreeKingdomsGenerationResolver : IWebUiEntityGenerationResolver
{
    public bool IsCurrent(WebUiEntityRef entityRef)
    {
        return entityRef.StableId <= 0 && entityRef.Generation <= 0;
    }
}

public sealed class ThreeKingdomsPermissionValidator : IWebUiCommandPermissionValidator
{
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "selectNext",
        "move",
        "attack",
        "skill",
        "troop",
        "develop",
        "advance",
        "battle",
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
