using System.Text.Json;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.EntityCollections;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Input.Orders;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Scripting;
using Ludots.Core.UI.EntityCommandPanels;
using Ludots.WebUI.DataPlane;

namespace CncTriNationFullGameMod;

internal sealed class CncTriNationTopicProducer : IWebUiTopicProducer
{
    public const string TopicName = "ludots.cnc.triNation.world";
    private const string GasAbilitySlotsSourceId = "gas.ability-slots";
    private const string RosterCatalogVfsPath = "CncTriNationFullGameMod:assets/roster_catalog.json";
    private const string GraphSummaryKeyPrefix = "cnc.summary.";
    private const string CommandPanelSurfaceId = "cnc-tri-nation";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly GameEngine _engine;
    private readonly CncTriNationRosterEntryView[] _rosterCatalog;
    private readonly CncTriNationItemView[] _itemCatalog;
    private int _tick;
    private int _commandCount;
    private string _activeFactionId = "team-1";
    private string _lastCommand = "snapshot";
    private string _lastCommandStatus = "idle";

    public CncTriNationTopicProducer(GameEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _rosterCatalog = LoadRosterCatalog(engine);
        _itemCatalog = BuildItemCatalog();
    }

    public string Topic => TopicName;

    public bool TryCreateSnapshot(in WebUiTopicContext context, out WebUiOutboundPacket packet)
    {
        bool isSubscriptionSnapshot = context.RequestId != 0;
        if (!isSubscriptionSnapshot)
        {
            _tick++;
        }

        packet = CreateJsonPacket(
            context.SessionId,
            isSubscriptionSnapshot ? WebUiPacketKind.Snapshot : WebUiPacketKind.Delta,
            context.RequestId,
            isSubscriptionSnapshot ? "snapshot" : "world");
        return true;
    }

    public WebUiCommandResult ApplyCommand(WebUiCommandRequest request)
    {
        _commandCount++;
        _lastCommand = request.Name;

        WebUiCommandResult result = request.Name switch
        {
            "selectEntity" => SelectEntity(request),
            "activateAbilitySlot" => ActivateAbilitySlot(request),
            "switchParticipantView" => SwitchParticipantView(request),
            _ => WebUiCommandResult.Fail("unknown_command", $"Unsupported C&C tri-nation command '{request.Name}'.")
        };

        _lastCommandStatus = result.Success ? "ack" : $"{result.ErrorCode}: {result.Message}";
        return result;
    }

    private WebUiOutboundPacket CreateJsonPacket(string sessionId, WebUiPacketKind kind, long requestId, string reason)
    {
        CncTriNationSnapshot snapshot = BuildSnapshot(reason);
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);
        return new WebUiOutboundPacket(
            sessionId,
            TopicName,
            kind,
            WebUiDeliverySemantics.LatestWins,
            payload,
            "application/json",
            requestId);
    }

    private CncTriNationSnapshot BuildSnapshot(string reason)
    {
        string mapId = _engine.CurrentMapSession?.MapConfig?.Id ?? string.Empty;
        string flavor = ResolveFlavor(mapId);
        Entity[] selected = SnapshotCommandSource();
        Entity primary = selected.Length > 0 ? selected[0] : Entity.Null;

        var entities = BuildEntities(selected);
        var factions = BuildFactions(entities);
        var commands = BuildCommandPanel(primary);
        var resources = BuildResourceChips();
        var production = BuildProductionQueue(commands);
        var buildables = BuildBuildables(commands);
        var diagnostics = BuildDiagnostics(reason, mapId, entities, commands);

        return new CncTriNationSnapshot(
            _tick,
            mapId,
            flavor,
            _activeFactionId,
            resources,
            factions,
            entities,
            BuildSelection(primary, selected),
            commands,
            buildables,
            production,
            CncTriNationTechTreeView.Empty,
            CncTriNationDiplomacyView.Empty,
            diagnostics,
            _rosterCatalog,
            _itemCatalog,
            BuildGraphSummaries());
    }

    private CncTriNationEntityView[] BuildEntities(Entity[] selected)
    {
        var selectedSet = selected
            .Where(entity => entity != Entity.Null)
            .Select(static entity => EntityKey(entity))
            .ToHashSet(StringComparer.Ordinal);
        var entities = new List<CncTriNationEntityView>(64);
        var query = new QueryDescription().WithAll<Name>();
        _engine.World.Query(in query, (Entity entity, ref Name name) =>
        {
            if (!_engine.World.IsAlive(entity) || string.IsNullOrWhiteSpace(name.Value))
            {
                return;
            }

            int teamId = _engine.World.TryGet(entity, out Team team) ? team.Id : 0;
            WorldPositionCm position = _engine.World.TryGet(entity, out WorldPositionCm resolvedPosition)
                ? resolvedPosition
                : default;
            AttributeBuffer attributes = _engine.World.TryGet(entity, out AttributeBuffer resolvedAttributes)
                ? resolvedAttributes
                : default;
            float health = ReadAttribute(in attributes, "Health");
            float shield = ReadAttribute(in attributes, "Shield");
            string kind = ResolveEntityKind(name.Value);
            string key = EntityKey(entity);

            entities.Add(new CncTriNationEntityView(
                key,
                entity.Id,
                entity.Version,
                name.Value,
                kind,
                teamId,
                TeamName(teamId),
                TeamColor(teamId),
                MathF.Round(position.Value.X.ToFloat() / 100f, 2),
                MathF.Round(position.Value.Y.ToFloat() / 100f, 2),
                MathF.Round(health, 1),
                MathF.Round(shield, 1),
                selectedSet.Contains(key),
                BuildEntityAbilityNames(entity)));
        });

        return entities
            .OrderBy(entity => entity.TeamId)
            .ThenBy(entity => entity.Name, StringComparer.Ordinal)
            .Take(80)
            .ToArray();
    }

    private CncTriNationFactionView[] BuildFactions(CncTriNationEntityView[] entities)
    {
        var teamIds = entities
            .Select(static entity => entity.TeamId)
            .Where(static teamId => teamId > 0)
            .Distinct()
            .OrderBy(static teamId => teamId)
            .ToArray();
        if (teamIds.Length == 0)
        {
            teamIds = [1];
        }

        if (!teamIds.Any(teamId => string.Equals($"team-{teamId}", _activeFactionId, StringComparison.Ordinal)))
        {
            _activeFactionId = $"team-{teamIds[0]}";
        }

        return teamIds
            .Select(teamId => new CncTriNationFactionView(
                $"team-{teamId}",
                TeamName(teamId),
                teamId,
                TeamColor(teamId),
                teamId == 1 ? "player" : "ai",
                string.Equals($"team-{teamId}", _activeFactionId, StringComparison.Ordinal),
                entities.Count(entity => entity.TeamId == teamId),
                teamId == ResolveActiveTeamId() ? "Friendly" : "Neutral"))
            .ToArray();
    }

    private CncTriNationSelectionView BuildSelection(Entity primary, Entity[] selected)
    {
        if (primary == Entity.Null || !_engine.World.IsAlive(primary))
        {
            return new CncTriNationSelectionView(
                string.Empty,
                "No entity selected",
                string.Empty,
                0,
                0,
                0,
                Array.Empty<CncTriNationSelectedEntityView>());
        }

        string name = _engine.World.TryGet(primary, out Name resolvedName) ? resolvedName.Value : $"Entity {primary.Id}";
        int teamId = _engine.World.TryGet(primary, out Team team) ? team.Id : 0;
        AttributeBuffer attributes = _engine.World.TryGet(primary, out AttributeBuffer resolvedAttributes)
            ? resolvedAttributes
            : default;

        return new CncTriNationSelectionView(
            EntityKey(primary),
            name,
            ResolveEntityKind(name),
            teamId,
            MathF.Round(ReadAttribute(in attributes, "Health"), 1),
            MathF.Round(ReadAttribute(in attributes, "Shield"), 1),
            selected
                .Where(entity => _engine.World.IsAlive(entity))
                .Select(entity =>
                {
                    string itemName = _engine.World.TryGet(entity, out Name itemResolvedName)
                        ? itemResolvedName.Value
                        : $"Entity {entity.Id}";
                    int itemTeam = _engine.World.TryGet(entity, out Team itemTeamValue) ? itemTeamValue.Id : 0;
                    return new CncTriNationSelectedEntityView(EntityKey(entity), itemName, itemTeam);
                })
                .ToArray());
    }

    private CncTriNationCommandPanelView BuildCommandPanel(Entity target)
    {
        if (target == Entity.Null || !_engine.World.IsAlive(target))
        {
            return CncTriNationCommandPanelView.Empty("No selected entity.");
        }

        if (!CanLocalPlayerCommand(target))
        {
            return CncTriNationCommandPanelView.Empty("Selected entity is not controlled by the local player.");
        }

        var registry = _engine.GetService(CoreServiceKeys.EntityCommandPanelSourceRegistry);
        if (registry == null || !registry.TryGet(GasAbilitySlotsSourceId, out IEntityCommandPanelSource source))
        {
            return CncTriNationCommandPanelView.Empty("EntityCommandPanel source 'gas.ability-slots' is not registered.");
        }

        var context = new EntityCommandPanelSourceContext(target, GasAbilitySlotsSourceId, CommandPanelSurfaceId);
        EntityCommandPanelSourceDispatch.TryGetRevision(source, in context, out uint revision);
        int groupCount = EntityCommandPanelSourceDispatch.GetGroupCount(source, in context);
        var groups = new List<CncTriNationCommandGroupView>(Math.Max(groupCount, 0));
        var slots = new EntityCommandPanelSlotView[AbilityStateBuffer.CAPACITY];
        var statuses = new EntityCommandPanelStatusView[8];
        var queueItems = new EntityCommandPanelQueueItemView[8];

        for (int groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            if (!EntityCommandPanelSourceDispatch.TryGetGroup(source, in context, groupIndex, out EntityCommandPanelGroupView group))
            {
                continue;
            }

            int slotCount = EntityCommandPanelSourceDispatch.CopySlots(source, in context, groupIndex, slots);
            var slotViews = new CncTriNationCommandSlotView[slotCount];
            for (int slotIndex = 0; slotIndex < slotCount; slotIndex++)
            {
                EntityCommandPanelSlotView slot = slots[slotIndex];
                slotViews[slotIndex] = new CncTriNationCommandSlotView(
                    slot.SlotIndex,
                    slot.AbilityId,
                    AbilityIdRegistry.GetName(slot.AbilityId),
                    string.IsNullOrWhiteSpace(slot.DisplayLabel) ? $"Slot {slot.SlotIndex + 1}" : slot.DisplayLabel,
                    slot.DetailLabel,
                    slot.ActionId,
                    slot.CooldownPermille,
                    slot.StateFlags.ToString(),
                    IsActionableCommandSlot(slot));
            }

            groups.Add(new CncTriNationCommandGroupView(groupIndex, group.GroupLabel, slotViews));
        }

        int statusCount = EntityCommandPanelSourceDispatch.CopyStatuses(source, in context, statuses);
        int queueCount = EntityCommandPanelSourceDispatch.CopyQueueItems(source, in context, queueItems);
        return new CncTriNationCommandPanelView(
            EntityKey(target),
            revision,
            EntityCommandPanelSourceDispatch.CanActivate(source),
            groups.ToArray(),
            statuses.Take(statusCount)
                .Select(static status => new CncTriNationStatusView(status.Label, status.Detail, status.ProgressPermille, status.AccentColorHex))
                .ToArray(),
            queueItems.Take(queueCount)
                .Select(static item => new CncTriNationQueueItemView(item.Label, item.Detail, item.Stage.ToString(), 0, item.AccentColorHex))
                .ToArray(),
            string.Empty);
    }

    private CncTriNationResourceChipView[] BuildResourceChips()
    {
        string flavor = ResolveFlavor(_engine.CurrentMapSession?.MapConfig?.Id ?? string.Empty);
        string[] names = flavor switch
        {
            "cnc-tri-nation" => ["Credits", "Power", "Ore"],
            _ => ["Credits", "Power", "Ore"]
        };

        return names
            .Select(name =>
            {
                float current = ReadTeamAttributeTotal(ResolveActiveTeamId(), name, out bool found);
                return new CncTriNationResourceChipView(name, MathF.Round(current, 1), found ? "+0.0" : "");
            })
            .ToArray();
    }

    private static CncTriNationBuildableView[] BuildBuildables(CncTriNationCommandPanelView commands)
    {
        return commands.Groups
            .Where(static group => group.GroupIndex == 0)
            .SelectMany(static group => group.Slots)
            .Where(static slot => slot.Enabled)
            .Select(static slot => new CncTriNationBuildableView(
                slot.Label,
                slot.AbilityId,
                true,
                slot.Detail,
                0,
                0))
            .ToArray();
    }

    private CncTriNationQueueItemView[] BuildProductionQueue(CncTriNationCommandPanelView commands)
    {
        if (commands.Queue.Length == 0 && commands.Statuses.Length == 0)
        {
            return Array.Empty<CncTriNationQueueItemView>();
        }

        var rows = new List<CncTriNationQueueItemView>(commands.Statuses.Length + commands.Queue.Length);
        rows.AddRange(commands.Statuses
            .Select(static status => new CncTriNationQueueItemView(status.Label, status.Detail, "Active", status.ProgressPermille, status.AccentColorHex)));
        rows.AddRange(commands.Queue
            .Where(static item => !string.Equals(item.Stage, "Active", StringComparison.OrdinalIgnoreCase)));
        return rows.ToArray();
    }

    private CncTriNationDiagnosticsView BuildDiagnostics(
        string reason,
        string mapId,
        CncTriNationEntityView[] entities,
        CncTriNationCommandPanelView commands)
    {
        var messages = new List<string>();
        if (commands.Message.Length > 0)
        {
            messages.Add(commands.Message);
        }

        if (TryBuildAimingMessage(commands, out string aimingMessage))
        {
            messages.Add(aimingMessage);
        }

        return new CncTriNationDiagnosticsView(
            reason,
            _lastCommand,
            _lastCommandStatus,
            _commandCount,
            mapId,
            entities.Length,
            0,
            messages.ToArray());
    }

    private CncTriNationGraphSummaryView[] BuildGraphSummaries()
    {
        var summaries = new List<CncTriNationGraphSummaryView>();
        foreach (KeyValuePair<string, object> entry in _engine.GlobalContext)
        {
            if (!entry.Key.StartsWith(GraphSummaryKeyPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            summaries.Add(new CncTriNationGraphSummaryView(
                entry.Key,
                entry.Key[GraphSummaryKeyPrefix.Length..],
                FormatSummaryValue(entry.Value)));
        }

        return summaries
            .OrderBy(summary => summary.Key, StringComparer.Ordinal)
            .ToArray();
    }

    private static string FormatSummaryValue(object? value)
    {
        return value switch
        {
            int intValue => intValue.ToString(),
            float floatValue => MathF.Round(floatValue, 1).ToString(),
            double doubleValue => MathF.Round((float)doubleValue, 1).ToString(),
            bool boolValue => boolValue ? "true" : "false",
            null => string.Empty,
            _ => value.ToString() ?? string.Empty
        };
    }

    private static CncTriNationRosterEntryView[] LoadRosterCatalog(GameEngine engine)
    {
        if (engine.VFS == null ||
            !engine.VFS.TryResolveFullPath(RosterCatalogVfsPath, out string fullPath) ||
            !File.Exists(fullPath))
        {
            return Array.Empty<CncTriNationRosterEntryView>();
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(fullPath));
        if (!document.RootElement.TryGetProperty("roster", out JsonElement rosterElement) ||
            rosterElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<CncTriNationRosterEntryView>();
        }

        var entries = new List<CncTriNationRosterEntryView>(rosterElement.GetArrayLength());
        foreach (JsonElement item in rosterElement.EnumerateArray())
        {
            entries.Add(new CncTriNationRosterEntryView(
                ReadStringProperty(item, "nation"),
                ReadStringProperty(item, "id"),
                ReadStringProperty(item, "name"),
                ReadStringProperty(item, "category")));
        }

        return entries.ToArray();
    }

    private static CncTriNationItemView[] BuildItemCatalog()
    {
        return
        [
            new("itm_allied_upgrade_0", "Allied Armor Plating", "allied"),
            new("itm_allied_upgrade_1", "Allied Reactor Boost", "allied"),
            new("itm_allied_upgrade_2", "Allied Targeting AI", "allied"),
            new("itm_soviet_upgrade_0", "Soviet Armor Plating", "soviet"),
            new("itm_soviet_upgrade_1", "Soviet Reactor Boost", "soviet"),
            new("itm_soviet_upgrade_2", "Soviet Targeting AI", "soviet"),
            new("itm_yuri_upgrade_0", "Yuri Armor Plating", "yuri"),
            new("itm_yuri_upgrade_1", "Yuri Reactor Boost", "yuri"),
            new("itm_yuri_upgrade_2", "Yuri Targeting AI", "yuri"),
        ];
    }

    private static string ReadStringProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement property) &&
               property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private bool TryBuildAimingMessage(CncTriNationCommandPanelView commands, out string message)
    {
        message = string.Empty;
        InputOrderMappingSystem? mapping = _engine.GetService(CoreServiceKeys.ActiveInputOrderMapping);
        if (mapping is not { IsAiming: true })
        {
            return false;
        }

        string label = ResolveAimingCommandLabel(commands, mapping.AimingActionId);
        message = label.Length > 0
            ? $"Placement armed: {label}. Terrain confirmation pending."
            : "Placement armed. Terrain confirmation pending.";
        return true;
    }

    private static string ResolveAimingCommandLabel(CncTriNationCommandPanelView commands, string actionId)
    {
        if (string.IsNullOrWhiteSpace(actionId))
        {
            return string.Empty;
        }

        foreach (CncTriNationCommandGroupView group in commands.Groups)
        {
            foreach (CncTriNationCommandSlotView slot in group.Slots)
            {
                if (string.Equals(slot.ActionId, actionId, StringComparison.Ordinal))
                {
                    return slot.Label;
                }
            }
        }

        return actionId;
    }

    private WebUiCommandResult SelectEntity(WebUiCommandRequest request)
    {
        if (!request.Payload.TryGetProperty("entityKey", out JsonElement keyElement) ||
            keyElement.ValueKind != JsonValueKind.String)
        {
            return WebUiCommandResult.Fail("invalid_payload", "selectEntity requires payload.entityKey.");
        }

        string? key = keyElement.GetString();
        if (string.IsNullOrWhiteSpace(key) || !TryResolveEntityKey(key, out Entity target))
        {
            return WebUiCommandResult.Fail("entity_not_found", $"Entity '{key}' is not alive.");
        }

        if (!TrySelectCommandTarget(target))
        {
            return WebUiCommandResult.Fail("command_source_missing", "EntityCollectionStore or LocalPlayerEntity is missing.");
        }

        return WebUiCommandResult.Ok();
    }

    private WebUiCommandResult ActivateAbilitySlot(WebUiCommandRequest request)
    {
        Entity target = ResolveCommandTarget(request);
        if (target == Entity.Null || !_engine.World.IsAlive(target))
        {
            return WebUiCommandResult.Fail("target_missing", "activateAbilitySlot needs a selected entity or payload.entityKey.");
        }

        if (!CanLocalPlayerCommand(target))
        {
            return WebUiCommandResult.Fail("target_not_controllable", "The selected entity is not controlled by the local player.");
        }

        int groupIndex = ReadInt(request.Payload, "groupIndex", 0);
        int slotIndex = ReadInt(request.Payload, "slotIndex", -1);
        if (slotIndex < 0)
        {
            return WebUiCommandResult.Fail("invalid_payload", "activateAbilitySlot requires payload.slotIndex.");
        }

        var registry = _engine.GetService(CoreServiceKeys.EntityCommandPanelSourceRegistry);
        if (registry == null || !registry.TryGet(GasAbilitySlotsSourceId, out IEntityCommandPanelSource source))
        {
            return WebUiCommandResult.Fail("command_panel_missing", "EntityCommandPanel source 'gas.ability-slots' is not registered.");
        }

        var context = new EntityCommandPanelSourceContext(target, GasAbilitySlotsSourceId, CommandPanelSurfaceId);
        if (!TryGetActionableSlot(source, in context, groupIndex, slotIndex, out _))
        {
            return WebUiCommandResult.Fail("ability_not_actionable", "This command slot has no gameplay execution exposed by GAS.");
        }

        if (!TryBindActiveInputMapping(target))
        {
            return WebUiCommandResult.Fail("input_mapping_missing", "ActiveInputOrderMapping is not ready for the selected command target.");
        }

        if (!EntityCommandPanelSourceDispatch.ActivateSlot(source, in context, groupIndex, slotIndex))
        {
            return WebUiCommandResult.Fail("ability_activation_failed", "The shared GAS command-panel source rejected this ability activation.");
        }

        return WebUiCommandResult.Ok();
    }

    private WebUiCommandResult SwitchParticipantView(WebUiCommandRequest request)
    {
        if (!request.Payload.TryGetProperty("participantId", out JsonElement participantElement) ||
            participantElement.ValueKind != JsonValueKind.String)
        {
            return WebUiCommandResult.Fail("invalid_payload", "switchParticipantView requires payload.participantId.");
        }

        string participantId = participantElement.GetString() ?? string.Empty;
        if (!participantId.StartsWith("team-", StringComparison.Ordinal))
        {
            return WebUiCommandResult.Fail("invalid_participant", "This vertical slice exposes team participant ids as team-N.");
        }

        _activeFactionId = participantId;
        return WebUiCommandResult.Ok();
    }

    private Entity ResolveSnapshotCommandTarget(string flavor, Entity primary)
    {
        if (IsCommandTarget(primary))
        {
            return primary;
        }

        foreach (string token in PreferredCommandTargetTokens(flavor))
        {
            Entity candidate = FindFirstCommandTargetByName(token);
            if (candidate != Entity.Null)
            {
                return candidate;
            }
        }

        return FindFirstCommandTarget();
    }

    private bool IsCommandTarget(Entity entity)
    {
        if (entity == Entity.Null || !_engine.World.IsAlive(entity))
        {
            return false;
        }

        if (!CanLocalPlayerCommand(entity))
        {
            return false;
        }

        var registry = _engine.GetService(CoreServiceKeys.EntityCommandPanelSourceRegistry);
        if (registry == null || !registry.TryGet(GasAbilitySlotsSourceId, out IEntityCommandPanelSource source))
        {
            return false;
        }

        var context = new EntityCommandPanelSourceContext(entity, GasAbilitySlotsSourceId, $"{CommandPanelSurfaceId}-target-probe");
        int groupCount = EntityCommandPanelSourceDispatch.GetGroupCount(source, in context);
        if (groupCount <= 0)
        {
            return false;
        }

        var slots = new EntityCommandPanelSlotView[AbilityStateBuffer.CAPACITY];
        for (int groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            int copied = EntityCommandPanelSourceDispatch.CopySlots(source, in context, groupIndex, slots);
            for (int slotIndex = 0; slotIndex < copied; slotIndex++)
            {
                EntityCommandPanelSlotView slot = slots[slotIndex];
                if (IsActionableCommandSlot(slot))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private Entity FindFirstCommandTargetByName(string token)
    {
        Entity result = Entity.Null;
        var query = new QueryDescription().WithAll<Name>();
        _engine.World.Query(in query, (Entity entity, ref Name name) =>
        {
            if (result != Entity.Null ||
                string.IsNullOrWhiteSpace(name.Value) ||
                name.Value.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0 ||
                !IsCommandTarget(entity))
            {
                return;
            }

            result = entity;
        });

        return result;
    }

    private Entity FindFirstCommandTarget()
    {
        Entity result = Entity.Null;
        var query = new QueryDescription().WithAll<Name>();
        _engine.World.Query(in query, (Entity entity, ref Name _) =>
        {
            if (result == Entity.Null && IsCommandTarget(entity))
            {
                result = entity;
            }
        });

        return result;
    }

    private bool TryBindActiveInputMapping(Entity target)
    {
        InputOrderMappingSystem? mapping = _engine.GetService(CoreServiceKeys.ActiveInputOrderMapping);
        if (mapping == null || !TryGetLocalPlayerId(out int playerId) || !CanLocalPlayerCommand(target))
        {
            return false;
        }

        mapping.SetLocalPlayer(target, playerId);
        return true;
    }

    private Entity ResolveCommandTarget(WebUiCommandRequest request)
    {
        if (request.Payload.TryGetProperty("entityKey", out JsonElement keyElement) &&
            keyElement.ValueKind == JsonValueKind.String &&
            TryResolveEntityKey(keyElement.GetString() ?? string.Empty, out Entity target))
        {
            return target;
        }

        return TryGetCommandSourcePrimary(out Entity commandSourcePrimary)
            ? commandSourcePrimary
            : Entity.Null;
    }

    private bool TrySelectCommandTarget(Entity target)
    {
        EntityCollectionStore? collections = _engine.GetService(CoreServiceKeys.EntityCollectionStore);
        Entity owner = _engine.GetService(CoreServiceKeys.LocalPlayerEntity);
        if (collections == null || !_engine.World.IsAlive(owner) || !_engine.World.IsAlive(target))
        {
            return false;
        }

        Span<Entity> next = stackalloc Entity[1];
        next[0] = target;
        var descriptor = EntityCollectionDescriptor.Create(
            EntityCollectionKeys.CommandSource,
            EntityCollectionSourceKind.Explicit,
            EntityCollectionRoleKind.CommandSource,
            owner,
            target,
            "C&C command source",
            "Selected through the browser data plane.");
        collections.Replace(owner, descriptor, next, owner);
        return true;
    }

    private Entity[] SnapshotCommandSource()
    {
        return TryResolveLocalCommandSourceOwner(out Entity owner)
            ? EntityCollectionContextRuntime.Snapshot(_engine.GlobalContext, owner, EntityCollectionKeys.CommandSource)
            : Array.Empty<Entity>();
    }

    private bool TryGetCommandSourcePrimary(out Entity entity)
    {
        entity = Entity.Null;
        return TryResolveLocalCommandSourceOwner(out Entity owner) &&
               EntityCollectionContextRuntime.TryGetPrimary(
                   _engine.World,
                   _engine.GlobalContext,
                   owner,
                   EntityCollectionKeys.CommandSource,
                   out entity);
    }

    private bool TryResolveLocalCommandSourceOwner(out Entity owner)
    {
        owner = Entity.Null;
        Entity local = _engine.GetService(CoreServiceKeys.LocalPlayerEntity);
        if (local == Entity.Null || !_engine.World.IsAlive(local))
        {
            return false;
        }

        owner = local;
        return true;
    }

    private bool TryResolveEntityKey(string key, out Entity entity)
    {
        Entity resolved = Entity.Null;
        var query = new QueryDescription().WithAll<Name>();
        _engine.World.Query(in query, (Entity candidate, ref Name _) =>
        {
            if (resolved == Entity.Null && EntityKey(candidate) == key)
            {
                resolved = candidate;
            }
        });

        entity = resolved;
        return entity != Entity.Null && _engine.World.IsAlive(entity);
    }

    private int ResolveActiveTeamId()
    {
        return _activeFactionId.StartsWith("team-", StringComparison.Ordinal) &&
            int.TryParse(_activeFactionId["team-".Length..], out int teamId)
                ? teamId
                : 1;
    }

    private string[] BuildEntityAbilityNames(Entity entity)
    {
        if (!CanLocalPlayerCommand(entity))
        {
            return Array.Empty<string>();
        }

        var registry = _engine.GetService(CoreServiceKeys.EntityCommandPanelSourceRegistry);
        if (registry == null || !registry.TryGet(GasAbilitySlotsSourceId, out IEntityCommandPanelSource source))
        {
            return Array.Empty<string>();
        }

        var context = new EntityCommandPanelSourceContext(entity, GasAbilitySlotsSourceId, $"{CommandPanelSurfaceId}-entity-list");
        int groupCount = EntityCommandPanelSourceDispatch.GetGroupCount(source, in context);
        if (groupCount <= 0)
        {
            return Array.Empty<string>();
        }

        var names = new List<string>(AbilityStateBuffer.CAPACITY);
        var slots = new EntityCommandPanelSlotView[AbilityStateBuffer.CAPACITY];
        for (int groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            int copied = EntityCommandPanelSourceDispatch.CopySlots(source, in context, groupIndex, slots);
            for (int slotIndex = 0; slotIndex < copied; slotIndex++)
            {
                EntityCommandPanelSlotView slot = slots[slotIndex];
                if ((slot.StateFlags & EntityCommandSlotStateFlags.Empty) != 0 ||
                    string.IsNullOrWhiteSpace(slot.DisplayLabel) ||
                    !IsActionableCommandSlot(slot))
                {
                    continue;
                }

                names.Add(slot.DisplayLabel);
            }
        }

        return names
            .Distinct(StringComparer.Ordinal)
            .Take(AbilityStateBuffer.CAPACITY)
            .ToArray();
    }

    private bool CanLocalPlayerCommand(Entity entity)
    {
        if (entity == Entity.Null || !_engine.World.IsAlive(entity) || !TryGetLocalPlayerId(out int localPlayerId))
        {
            return false;
        }

        return _engine.World.TryGet(entity, out PlayerOwner owner) &&
               owner.PlayerId == localPlayerId;
    }

    private bool TryGetLocalPlayerId(out int playerId)
    {
        playerId = 0;
        return _engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerId.Name, out object? value) &&
               value is int candidate &&
               candidate > 0 &&
               (playerId = candidate) > 0;
    }

    private static string[] PreferredCommandTargetTokens(string flavor)
    {
        return flavor switch
        {
            "cnc-tri-nation" => ["Construction Yard", "Barracks", "War Factory"],
            _ => ["Construction Yard", "Barracks", "War Factory"]
        };
    }

    private static string EntityKey(Entity entity)
    {
        return $"{entity.Id}:{entity.WorldId}:{entity.Version}";
    }

    private bool TryGetActionableSlot(
        IEntityCommandPanelSource source,
        in EntityCommandPanelSourceContext context,
        int groupIndex,
        int slotIndex,
        out EntityCommandPanelSlotView matched)
    {
        matched = default;
        if (groupIndex < 0 || slotIndex < 0)
        {
            return false;
        }

        var slots = new EntityCommandPanelSlotView[AbilityStateBuffer.CAPACITY];
        int copied = EntityCommandPanelSourceDispatch.CopySlots(source, in context, groupIndex, slots);
        for (int i = 0; i < copied; i++)
        {
            EntityCommandPanelSlotView slot = slots[i];
            if (slot.SlotIndex != slotIndex)
            {
                continue;
            }

            matched = slot;
            return IsActionableCommandSlot(slot);
        }

        return false;
    }

    private bool IsActionableCommandSlot(EntityCommandPanelSlotView slot)
    {
        return !string.IsNullOrWhiteSpace(slot.ActionId) &&
               (slot.StateFlags & EntityCommandSlotStateFlags.Blocked) == 0 &&
               (slot.StateFlags & EntityCommandSlotStateFlags.Empty) == 0 &&
               AbilityHasGameplayExecution(slot.AbilityId);
    }

    private bool AbilityHasGameplayExecution(int abilityId)
    {
        if (abilityId <= 0)
        {
            return false;
        }

        var abilities = _engine.GetService(CoreServiceKeys.AbilityDefinitionRegistry);
        if (abilities == null || !abilities.TryGet(abilityId, out AbilityDefinition definition))
        {
            return false;
        }

        if (definition.HasOnActivateEffects || definition.HasToggleSpec)
        {
            return true;
        }

        for (int itemIndex = 0; itemIndex < definition.ExecSpec.ItemCount; itemIndex++)
        {
            ExecItemKind kind = definition.ExecSpec.GetKind(itemIndex);
            if (kind != ExecItemKind.None && kind != ExecItemKind.End)
            {
                return true;
            }
        }

        return false;
    }

    private static string ResolveFlavor(string mapId)
    {
        return mapId switch
        {
            "cnc_tri_nation_war" => "cnc-tri-nation",
            _ => "shared"
        };
    }

    private static string ResolveEntityKind(string name)
    {
        string lower = name.ToLowerInvariant();
        if (lower.Contains("yard") || lower.Contains("factory") || lower.Contains("barracks") ||
            lower.Contains("gateway") || lower.Contains("pool") || lower.Contains("mill") ||
            lower.Contains("center") || lower.Contains("city") || lower.Contains("plant") ||
            lower.Contains("refinery") || lower.Contains("reactor"))
        {
            return "structure";
        }

        if (lower.Contains("drone") || lower.Contains("probe") || lower.Contains("peasant") ||
            lower.Contains("villager") || lower.Contains("settler") || lower.Contains("engineer") ||
            lower.Contains("harvester"))
        {
            return "worker";
        }

        if (lower.Contains("tank") || lower.Contains("rhino") || lower.Contains("mcv") ||
            lower.Contains("vehicle") || lower.Contains("ifv"))
        {
            return "vehicle";
        }

        return "unit";
    }

    private static string TeamName(int teamId)
    {
        return teamId switch
        {
            1 => "Allied",
            2 => "Soviet",
            3 => "Yuri",
            _ => $"Team {teamId}"
        };
    }

    private static string TeamColor(int teamId)
    {
        return teamId switch
        {
            1 => "#59A7FF",
            2 => "#F06449",
            3 => "#B692FF",
            _ => "#8DB596"
        };
    }

    private static float ReadAttribute(in AttributeBuffer attributes, string name)
    {
        int id = AttributeRegistry.GetId(name);
        return id == AttributeRegistry.InvalidId || !attributes.HasAttribute(id)
            ? 0f
            : attributes.GetCurrent(id);
    }

    private float ReadTeamAttributeTotal(int teamId, string name, out bool found)
    {
        found = false;
        int id = AttributeRegistry.GetId(name);
        if (id == AttributeRegistry.InvalidId)
        {
            return 0f;
        }

        float total = 0f;
        bool hasValue = false;
        var query = new QueryDescription().WithAll<Team, AttributeBuffer>();
        _engine.World.Query(in query, (ref Team team, ref AttributeBuffer attributes) =>
        {
            if (team.Id != teamId || !attributes.HasAttribute(id))
            {
                return;
            }

            hasValue = true;
            total += attributes.GetCurrent(id);
        });

        found = hasValue;
        return total;
    }

    private static int ReadInt(JsonElement payload, string name, int defaultValue)
    {
        return payload.TryGetProperty(name, out JsonElement element) && element.TryGetInt32(out int value)
            ? value
            : defaultValue;
    }
}

internal sealed class CncTriNationCommandHandler : IWebUiCommandHandler
{
    private readonly CncTriNationTopicProducer _producer;

    public CncTriNationCommandHandler(CncTriNationTopicProducer producer)
    {
        _producer = producer;
    }

    public ValueTask<WebUiCommandResult> HandleAsync(WebUiCommandRequest request, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(_producer.ApplyCommand(request));
    }
}

internal sealed class CncTriNationGenerationResolver : IWebUiEntityGenerationResolver
{
    public CncTriNationGenerationResolver(GameEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
    }

    public bool IsCurrent(WebUiEntityRef entityRef)
    {
        return entityRef.StableId <= 0 && entityRef.Generation <= 0;
    }
}

internal sealed class CncTriNationPermissionValidator : IWebUiCommandPermissionValidator
{
    private static readonly HashSet<string> AllowedCommands = new(StringComparer.Ordinal)
    {
        "selectEntity",
        "activateAbilitySlot",
        "switchParticipantView",
    };

    public bool CanUse(WebUiCommandRequest request, out string error)
    {
        if (AllowedCommands.Contains(request.Name))
        {
            error = string.Empty;
            return true;
        }

        error = $"Command '{request.Name}' is not allowed in CncTriNationFullGameMod.";
        return false;
    }
}

internal sealed record CncTriNationSnapshot(
    int Tick,
    string MapId,
    string Flavor,
    string ActiveFactionId,
    CncTriNationResourceChipView[] Resources,
    CncTriNationFactionView[] Factions,
    CncTriNationEntityView[] Entities,
    CncTriNationSelectionView Selection,
    CncTriNationCommandPanelView Commands,
    CncTriNationBuildableView[] Buildables,
    CncTriNationQueueItemView[] ProductionQueue,
    CncTriNationTechTreeView TechTree,
    CncTriNationDiplomacyView Diplomacy,
    CncTriNationDiagnosticsView Diagnostics,
    CncTriNationRosterEntryView[] RosterCatalog,
    CncTriNationItemView[] ItemCatalog,
    CncTriNationGraphSummaryView[] GraphSummaries);

internal sealed record CncTriNationResourceChipView(string Name, float Amount, string Rate);

internal sealed record CncTriNationFactionView(
    string Id,
    string Name,
    int TeamId,
    string Color,
    string Controller,
    bool Active,
    int EntityCount,
    string Relationship);

internal sealed record CncTriNationEntityView(
    string Key,
    int Id,
    int Generation,
    string Name,
    string Kind,
    int TeamId,
    string TeamName,
    string TeamColor,
    float X,
    float Y,
    float Health,
    float Shield,
    bool Selected,
    string[] AbilityNames);

internal sealed record CncTriNationSelectionView(
    string EntityKey,
    string Name,
    string Kind,
    int TeamId,
    float Health,
    float Shield,
    CncTriNationSelectedEntityView[] Members);

internal sealed record CncTriNationSelectedEntityView(string EntityKey, string Name, int TeamId);

internal sealed record CncTriNationCommandPanelView(
    string TargetEntityKey,
    uint Revision,
    bool CanActivate,
    CncTriNationCommandGroupView[] Groups,
    CncTriNationStatusView[] Statuses,
    CncTriNationQueueItemView[] Queue,
    string Message)
{
    public static CncTriNationCommandPanelView Empty(string message)
    {
        return new CncTriNationCommandPanelView(
            string.Empty,
            0,
            false,
            Array.Empty<CncTriNationCommandGroupView>(),
            Array.Empty<CncTriNationStatusView>(),
            Array.Empty<CncTriNationQueueItemView>(),
            message);
    }
}

internal sealed record CncTriNationCommandGroupView(
    int GroupIndex,
    string Label,
    CncTriNationCommandSlotView[] Slots);

internal sealed record CncTriNationCommandSlotView(
    int SlotIndex,
    int AbilityId,
    string AbilityKey,
    string Label,
    string Detail,
    string ActionId,
    short CooldownPermille,
    string StateFlags,
    bool Enabled);

internal sealed record CncTriNationStatusView(string Label, string Detail, short ProgressPermille, string AccentColorHex);

internal sealed record CncTriNationQueueItemView(string Label, string Detail, string Stage, short ProgressPermille, string AccentColorHex);

internal sealed record CncTriNationBuildableView(
    string Label,
    int AbilityId,
    bool Available,
    string Requirement,
    int Cost,
    int BuildSeconds);

internal sealed record CncTriNationTechTreeView(
    CncTriNationTechNodeView[] Nodes,
    CncTriNationTechEdgeView[] Edges)
{
    public static CncTriNationTechTreeView Empty { get; } = new(
        Array.Empty<CncTriNationTechNodeView>(),
        Array.Empty<CncTriNationTechEdgeView>());
}

internal sealed record CncTriNationTechNodeView(
    string Id,
    string Label,
    string State,
    int Column,
    int Row,
    string Detail);

internal sealed record CncTriNationTechEdgeView(string From, string To);

internal sealed record CncTriNationDiplomacyView(
    CncTriNationDiplomacyRowView[] Rows,
    CncTriNationProposalView[] Proposals)
{
    public static CncTriNationDiplomacyView Empty { get; } = new(
        Array.Empty<CncTriNationDiplomacyRowView>(),
        Array.Empty<CncTriNationProposalView>());
}

internal sealed record CncTriNationDiplomacyRowView(
    string FactionId,
    string FactionName,
    string Relationship,
    int Trust,
    string[] Pacts,
    string Gate);

internal sealed record CncTriNationProposalView(string Id, string From, string Summary);

internal sealed record CncTriNationDiagnosticsView(
    string Reason,
    string LastCommand,
    string LastCommandStatus,
    int CommandCount,
    string MapId,
    int EntityCount,
    int TeamRelationshipRevision,
    string[] Messages);

internal sealed record CncTriNationRosterEntryView(
    string Nation,
    string Id,
    string Name,
    string Category);

internal sealed record CncTriNationItemView(
    string Id,
    string DisplayName,
    string Nation);

internal sealed record CncTriNationGraphSummaryView(
    string Key,
    string Metric,
    string Value);
