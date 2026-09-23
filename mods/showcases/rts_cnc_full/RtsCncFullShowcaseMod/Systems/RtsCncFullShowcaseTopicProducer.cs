using System.Text.Json;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Items;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Scripting;
using Ludots.WebUI.DataPlane;

namespace RtsCncFullShowcaseMod.Systems;

public sealed class RtsCncFullShowcaseTopicProducer : IWebUiTopicProducer
{
    public const string TopicName = "ludots.showcase.rtsCncFull.world";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly FactionDef[] Factions =
    [
        new("atlantic", "Atlantic Directorate", 1, "#3B82F6"),
        new("volkov", "Volkov Union", 2, "#EF4444"),
        new("nile", "Nile Compact", 3, "#F59E0B"),
        new("pacific", "Pacific Combine", 4, "#10B981"),
        new("andes", "Andes League", 5, "#A855F7"),
    ];

    private static readonly string[] ProducerCategories =
    [
        "infantry",
        "armor",
        "air",
        "naval",
        "support",
    ];

    private static readonly string[] SupplyItemIds =
    [
        "rts_cnc_full_ore_canister",
        "rts_cnc_full_power_core",
        "rts_cnc_full_vehicle_kit",
        "rts_cnc_full_airframe_crate",
        "rts_cnc_full_naval_parts",
    ];

    private static readonly string[] GraphIds =
    [
        "Graph.RtsCncFull.RosterTotals",
    ];

    private readonly GameEngine _engine;
    private readonly RtsCncFullMatchRuntime? _matchRuntime;
    private int _tick;
    private int _commandCount;
    private string _activeFactionId = "atlantic";
    private string _lastCommand = "snapshot";
    private string _lastCommandStatus = "idle";

    public RtsCncFullShowcaseTopicProducer(GameEngine engine)
        : this(engine, null)
    {
    }

    public RtsCncFullShowcaseTopicProducer(GameEngine engine, RtsCncFullMatchRuntime? matchRuntime)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _matchRuntime = matchRuntime;
    }

    public string Topic => TopicName;

    public bool TryCreateSnapshot(in WebUiTopicContext context, out WebUiOutboundPacket packet)
    {
        bool isSubscriptionSnapshot = context.RequestId != 0;
        if (!isSubscriptionSnapshot)
        {
            _tick++;
        }

        RtsCncFullSnapshot snapshot = BuildSnapshot(isSubscriptionSnapshot ? "snapshot" : "tick");
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);
        packet = new WebUiOutboundPacket(
            context.SessionId,
            TopicName,
            isSubscriptionSnapshot ? WebUiPacketKind.Snapshot : WebUiPacketKind.Delta,
            WebUiDeliverySemantics.LatestWins,
            payload,
            "application/json",
            context.RequestId,
            _tick);
        return true;
    }

    public WebUiCommandResult ApplyCommand(WebUiCommandRequest request)
    {
        _commandCount++;
        _lastCommand = request.Name;

        WebUiCommandResult result = request.Name switch
        {
            "selectFaction" => SelectFaction(request),
            "startHarvest" => QueueMatchAction(RtsCncFullMatchAction.StartHarvest, "dataplane"),
            "trainArmy" => QueueMatchAction(RtsCncFullMatchAction.TrainArmy, "dataplane"),
            "attackEnemy" => QueueMatchAction(RtsCncFullMatchAction.AttackEnemy, "dataplane"),
            "resetMatch" => QueueMatchAction(RtsCncFullMatchAction.Reset, "dataplane"),
            _ => WebUiCommandResult.Fail("unknown_command", $"Unsupported C&C full showcase command '{request.Name}'.")
        };

        _lastCommandStatus = result.Success ? "ack" : $"{result.ErrorCode}: {result.Message}";
        return result;
    }

    private RtsCncFullSnapshot BuildSnapshot(string reason)
    {
        var units = BuildUnits();
        var producers = BuildProducers();
        var factions = BuildFactions(units, producers);
        var items = BuildSupplyItems();
        var graphs = BuildGraphViews();
        string mapId = _engine.CurrentMapSession?.MapConfig?.Id ?? string.Empty;

        return new RtsCncFullSnapshot(
            _tick,
            mapId,
            _activeFactionId,
            new RtsCncFullRosterSummary(
                factions.Length,
                units.Select(static unit => unit.Name).Distinct(StringComparer.Ordinal).Count(),
                producers.Select(static producer => producer.Name).Distinct(StringComparer.Ordinal).Count(),
                ProducerCategories.Length,
                items.Length,
                graphs.Length),
            factions,
            units,
            producers,
            items,
            graphs,
            _matchRuntime?.ToView(),
            new RtsCncFullDiagnostics(reason, _lastCommand, _lastCommandStatus, _commandCount));
    }

    private RtsCncFullFactionView[] BuildFactions(
        IReadOnlyList<RtsCncFullUnitView> units,
        IReadOnlyList<RtsCncFullProducerView> producers)
    {
        return Factions.Select(faction =>
            new RtsCncFullFactionView(
                faction.Id,
                faction.Name,
                faction.TeamId,
                faction.Color,
                faction.Id == _activeFactionId,
                units.Count(unit => unit.TeamId == faction.TeamId),
                producers.Count(producer => producer.TeamId == faction.TeamId)))
            .ToArray();
    }

    private RtsCncFullUnitView[] BuildUnits()
    {
        EntityTemplateKeyRegistry? templateKeys = _engine.GetService(CoreServiceKeys.EntityTemplateKeyRegistry);
        var units = new List<RtsCncFullUnitView>(128);
        var query = new QueryDescription().WithAll<Name, EntityTemplateKeyRef>();
        _engine.World.Query(in query, (Entity entity, ref Name name, ref EntityTemplateKeyRef templateKey) =>
        {
            string templateId = templateKeys?.GetName(templateKey.TemplateKeyId) ?? string.Empty;
            if (!templateId.StartsWith("rts_cnc_full_", StringComparison.Ordinal) ||
                IsProducerTemplate(templateId) ||
                IsAnchorTemplate(templateId))
            {
                return;
            }

            int teamId = _engine.World.TryGet(entity, out Team team) ? team.Id : 0;
            units.Add(new RtsCncFullUnitView(
                name.Value,
                ResolveFactionId(teamId),
                teamId,
                ResolveCategory(templateId),
                templateId,
                ReadHealth(entity),
                ReadAttribute(entity, "Damage"),
                ReadAttribute(entity, "Range")));
        });

        return units
            .OrderBy(static unit => unit.TeamId)
            .ThenBy(static unit => unit.Category, StringComparer.Ordinal)
            .ThenBy(static unit => unit.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private RtsCncFullProducerView[] BuildProducers()
    {
        EntityTemplateKeyRegistry? templateKeys = _engine.GetService(CoreServiceKeys.EntityTemplateKeyRegistry);
        var producers = new List<RtsCncFullProducerView>(32);
        var query = new QueryDescription().WithAll<Name, EntityTemplateKeyRef>();
        _engine.World.Query(in query, (Entity entity, ref Name name, ref EntityTemplateKeyRef templateKey) =>
        {
            string templateId = templateKeys?.GetName(templateKey.TemplateKeyId) ?? string.Empty;
            if (!IsProducerTemplate(templateId))
            {
                return;
            }

            int teamId = _engine.World.TryGet(entity, out Team team) ? team.Id : 0;
            producers.Add(new RtsCncFullProducerView(
                name.Value,
                ResolveFactionId(teamId),
                teamId,
                ResolveCategory(templateId),
                templateId,
                CountAbilitySlots(entity)));
        });

        return producers
            .OrderBy(static producer => producer.TeamId)
            .ThenBy(static producer => producer.Category, StringComparer.Ordinal)
            .ToArray();
    }

    private RtsCncFullSupplyItemView[] BuildSupplyItems()
    {
        ItemDefinitionRegistry? definitions = _engine.GetService(CoreServiceKeys.ItemDefinitionRegistry);
        if (definitions == null)
        {
            return Array.Empty<RtsCncFullSupplyItemView>();
        }

        var items = new List<RtsCncFullSupplyItemView>(SupplyItemIds.Length);
        for (int i = 0; i < SupplyItemIds.Length; i++)
        {
            int id = definitions.GetId(SupplyItemIds[i]);
            if (id <= 0 || !definitions.TryGet(id, out ItemDefinition definition))
            {
                continue;
            }

            items.Add(new RtsCncFullSupplyItemView(definition.Id, definition.DisplayName, definition.MaxStack));
        }

        return items.ToArray();
    }

    private static RtsCncFullGraphView[] BuildGraphViews()
    {
        return GraphIds
            .Select(id => new RtsCncFullGraphView(id, GraphIdRegistry.GetId(id) > 0))
            .ToArray();
    }

    private WebUiCommandResult SelectFaction(WebUiCommandRequest request)
    {
        if (!request.Payload.TryGetProperty("factionId", out JsonElement value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return WebUiCommandResult.Fail("invalid_payload", "selectFaction requires payload.factionId.");
        }

        string? factionId = value.GetString();
        if (string.IsNullOrWhiteSpace(factionId) ||
            !Factions.Any(faction => string.Equals(faction.Id, factionId, StringComparison.Ordinal)))
        {
            return WebUiCommandResult.Fail("unknown_faction", $"Unknown faction '{factionId}'.");
        }

        _activeFactionId = factionId.Trim();
        return WebUiCommandResult.Ok();
    }

    private WebUiCommandResult QueueMatchAction(RtsCncFullMatchAction action, string source)
    {
        if (_matchRuntime == null)
        {
            return WebUiCommandResult.Fail("match_runtime_missing", "RtsCncFull match runtime is not installed.");
        }

        return _matchRuntime.TryQueueAction(action, source, out string message)
            ? WebUiCommandResult.Ok()
            : WebUiCommandResult.Fail("action_rejected", message);
    }

    private float ReadHealth(Entity entity) => ReadAttribute(entity, "Health");

    private float ReadAttribute(Entity entity, string attributeName)
    {
        int attributeId = AttributeRegistry.GetId(attributeName);
        return attributeId > 0 &&
               _engine.World.TryGet(entity, out Ludots.Core.Gameplay.GAS.Components.AttributeBuffer attributes) &&
               attributes.HasAttribute(attributeId)
            ? MathF.Round(attributes.GetCurrent(attributeId), 1)
            : 0f;
    }

    private int CountAbilitySlots(Entity entity)
    {
        if (!_engine.World.TryGet(entity, out Ludots.Core.Gameplay.GAS.Components.AbilityStateBuffer abilities))
        {
            return 0;
        }

        return abilities.Count;
    }

    private static string ResolveFactionId(int teamId)
    {
        for (int i = 0; i < Factions.Length; i++)
        {
            if (Factions[i].TeamId == teamId)
            {
                return Factions[i].Id;
            }
        }

        return $"team-{teamId}";
    }

    private static string ResolveCategory(string templateId)
    {
        string[] parts = templateId.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 5 ? parts[4] : "unit";
    }

    private static bool IsProducerTemplate(string templateId)
    {
        return templateId.EndsWith("_producer", StringComparison.Ordinal);
    }

    private static bool IsAnchorTemplate(string templateId)
    {
        return templateId.EndsWith("_anchor", StringComparison.Ordinal);
    }

    private readonly record struct FactionDef(string Id, string Name, int TeamId, string Color);
}

internal sealed class RtsCncFullCommandHandler : IWebUiCommandHandler
{
    private readonly RtsCncFullShowcaseTopicProducer _topic;

    public RtsCncFullCommandHandler(RtsCncFullShowcaseTopicProducer topic)
    {
        _topic = topic ?? throw new ArgumentNullException(nameof(topic));
    }

    public ValueTask<WebUiCommandResult> HandleAsync(WebUiCommandRequest request, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(_topic.ApplyCommand(request));
    }
}

internal sealed class RtsCncFullGenerationResolver : IWebUiEntityGenerationResolver
{
    public bool IsCurrent(WebUiEntityRef entityRef) => entityRef.StableId <= 0 && entityRef.Generation <= 0;
}

internal sealed class RtsCncFullPermissionValidator : IWebUiCommandPermissionValidator
{
    public bool CanUse(WebUiCommandRequest request, out string error)
    {
        if (string.Equals(request.Name, "selectFaction", StringComparison.Ordinal) ||
            string.Equals(request.Name, "startHarvest", StringComparison.Ordinal) ||
            string.Equals(request.Name, "trainArmy", StringComparison.Ordinal) ||
            string.Equals(request.Name, "attackEnemy", StringComparison.Ordinal) ||
            string.Equals(request.Name, "resetMatch", StringComparison.Ordinal))
        {
            error = string.Empty;
            return true;
        }

        error = $"Command '{request.Name}' is not allowed in RtsCncFullShowcaseMod.";
        return false;
    }
}

internal sealed record RtsCncFullSnapshot(
    int Tick,
    string MapId,
    string ActiveFactionId,
    RtsCncFullRosterSummary Summary,
    RtsCncFullFactionView[] Factions,
    RtsCncFullUnitView[] Units,
    RtsCncFullProducerView[] Producers,
    RtsCncFullSupplyItemView[] SupplyItems,
    RtsCncFullGraphView[] Graphs,
    RtsCncFullMatchView? Match,
    RtsCncFullDiagnostics Diagnostics);

internal sealed record RtsCncFullRosterSummary(
    int Factions,
    int UnitTypes,
    int Producers,
    int ProducerCategories,
    int SupplyItemTypes,
    int Graphs);

internal sealed record RtsCncFullFactionView(
    string Id,
    string Name,
    int TeamId,
    string Color,
    bool Active,
    int UnitTypes,
    int Producers);

internal sealed record RtsCncFullUnitView(
    string Name,
    string FactionId,
    int TeamId,
    string Category,
    string TemplateId,
    float Health,
    float Damage,
    float Range);

internal sealed record RtsCncFullProducerView(
    string Name,
    string FactionId,
    int TeamId,
    string Category,
    string TemplateId,
    int AbilitySlots);

internal sealed record RtsCncFullSupplyItemView(string Id, string DisplayName, int MaxStack);

internal sealed record RtsCncFullGraphView(string Id, bool Registered);

internal sealed record RtsCncFullDiagnostics(
    string Reason,
    string LastCommand,
    string LastCommandStatus,
    int CommandCount);
