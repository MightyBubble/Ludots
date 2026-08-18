using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Input.Orders;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Client;
using Ludots.Core.Scripting;
using Ludots.Core.UI.EntityCommandPanels;
using Ludots.WebUI.DataPlane;

namespace CityRallyWebUiShowcaseMod.Runtime;

internal sealed class CityRallyTopicProducer : IWebUiTopicProducer
{
    public const string TopicName = "ludots.showcase.rtsProduction.world";
    private const string GasAbilitySlotsSourceId = "gas.ability-slots";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly GameEngine _engine;
    private int _tick;
    private string _activeFactionId = "team-1";

    public CityRallyTopicProducer(GameEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
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
        return request.Name switch
        {
            "selectEntity" => SelectEntity(request),
            "activateAbilitySlot" => ActivateAbilitySlot(request),
            "switchParticipantView" => SwitchParticipantView(request),
            "cancelPlanting" => CancelPlanting(request),
            _ => WebUiCommandResult.Fail("unknown_command", $"Unsupported city rally command '{request.Name}'.")
        };
    }

    private WebUiOutboundPacket CreateJsonPacket(string sessionId, WebUiPacketKind kind, long requestId, string reason)
    {
        CityRallySnapshot snapshot = BuildSnapshot(reason);
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

    private CityRallySnapshot BuildSnapshot(string reason)
    {
        string mapId = _engine.CurrentMapSession?.MapConfig?.Id ?? string.Empty;
        Entity[] selected = SnapshotCommandSource();
        Entity primary = selected.Length > 0 ? selected[0] : Entity.Null;

        var entities = BuildEntities(selected);
        var factions = BuildFactions(entities);
        var commands = BuildCommandPanel(primary);
        var production = BuildProductionQueue(commands);
        var buildables = BuildBuildables(commands);
        var diagnostics = BuildDiagnostics(reason, mapId, entities, commands);

        return new CityRallySnapshot(
            _tick,
            mapId,
            "city-rally",
            _activeFactionId,
            BuildResourceChips(),
            factions,
            entities,
            BuildSelection(primary, selected),
            BuildGarrison(primary),
            commands,
            buildables,
            production,
            CityRallyTechTreeView.Empty,
            CityRallyDiplomacyView.Empty,
            diagnostics);
    }

    private CityRallyEntityView[] BuildEntities(Entity[] selected)
    {
        var selectedSet = selected
            .Where(entity => entity != Entity.Null)
            .Select(static entity => EntityKey(entity))
            .ToHashSet(StringComparer.Ordinal);
        var entities = new List<CityRallyEntityView>(64);
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
            string kind = ResolveEntityKind(name.Value);
            string key = EntityKey(entity);

            entities.Add(new CityRallyEntityView(
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
                0f,
                selectedSet.Contains(key),
                BuildEntityAbilityNames(entity)));
        });

        return entities
            .OrderBy(entity => entity.TeamId)
            .ThenBy(entity => entity.Name, StringComparer.Ordinal)
            .Take(80)
            .ToArray();
    }

    private CityRallyFactionView[] BuildFactions(CityRallyEntityView[] entities)
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
            .Select(teamId => new CityRallyFactionView(
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

    private CityRallyGarrisonView[] BuildGarrison(Entity primary)
    {
        if (primary == Entity.Null || !_engine.World.IsAlive(primary) ||
            !_engine.World.Has<ChildrenBuffer>(primary))
        {
            return Array.Empty<CityRallyGarrisonView>();
        }

        ref var children = ref _engine.World.Get<ChildrenBuffer>(primary);
        var result = new List<CityRallyGarrisonView>(Math.Max(0, children.Count));
        for (int i = 0; i < children.Count; i++)
        {
            Entity child = children.Get(i);
            if (!_engine.World.IsAlive(child) || !_engine.World.TryGet(child, out Name childName))
            {
                continue;
            }

            string childKey = EntityKey(child);
            bool isGovernor = HasRoleTag(child, "Role.CityRally.Governor");
            bool isPlanting = HasRoleTag(child, "Status.CityRally.Planting");
            int progress = ResolvePlantingProgress(child);
            result.Add(new CityRallyGarrisonView(childKey, childName.Value, isGovernor, isPlanting, progress));
        }

        return result.ToArray();
    }

    private bool HasRoleTag(Entity entity, string tagName)
    {
        if (!_engine.World.Has<GameplayTagContainer>(entity))
        {
            return false;
        }

        int tagId = TagRegistry.GetId(tagName);
        if (tagId <= 0)
        {
            return false;
        }

        ref var tags = ref _engine.World.Get<GameplayTagContainer>(entity);
        var tagOps = _engine.GetService(CoreServiceKeys.TagOps) as TagOps;
        return tagOps != null && tagOps.HasTag(ref tags, tagId, TagSense.Effective);
    }

    private int ResolvePlantingProgress(Entity entity)
    {
        // 插旗进度由前端读 AbilityExecInstance 状态；此处保留占位（后续从能力引导时长换算）。
        return 0;
    }

    private CityRallySelectionView BuildSelection(Entity primary, Entity[] selected)
    {
        if (primary == Entity.Null || !_engine.World.IsAlive(primary))
        {
            return new CityRallySelectionView(
                string.Empty,
                "No entity selected",
                string.Empty,
                0,
                0,
                0,
                Array.Empty<CityRallySelectedEntityView>());
        }

        string name = _engine.World.TryGet(primary, out Name resolvedName) ? resolvedName.Value : $"Entity {primary.Id}";
        int teamId = _engine.World.TryGet(primary, out Team team) ? team.Id : 0;
        AttributeBuffer attributes = _engine.World.TryGet(primary, out AttributeBuffer resolvedAttributes)
            ? resolvedAttributes
            : default;

        return new CityRallySelectionView(
            EntityKey(primary),
            name,
            ResolveEntityKind(name),
            teamId,
            MathF.Round(ReadAttribute(in attributes, "Health"), 1),
            0f,
            selected
                .Where(entity => _engine.World.IsAlive(entity))
                .Select(entity =>
                {
                    string itemName = _engine.World.TryGet(entity, out Name itemResolvedName)
                        ? itemResolvedName.Value
                        : $"Entity {entity.Id}";
                    int itemTeam = _engine.World.TryGet(entity, out Team itemTeamValue) ? itemTeamValue.Id : 0;
                    return new CityRallySelectedEntityView(EntityKey(entity), itemName, itemTeam);
                })
                .ToArray());
    }

    private CityRallyCommandPanelView BuildCommandPanel(Entity target)
    {
        if (target == Entity.Null || !_engine.World.IsAlive(target))
        {
            return CityRallyCommandPanelView.Empty("No selected entity.");
        }

        if (!CanSolePossessedCommand(target))
        {
            return CityRallyCommandPanelView.Empty("Selected entity is not controlled by the local player.");
        }

        var registry = _engine.GetService(CoreServiceKeys.EntityCommandPanelSourceRegistry);
        if (registry == null || !registry.TryGet(GasAbilitySlotsSourceId, out IEntityCommandPanelSource source))
        {
            return CityRallyCommandPanelView.Empty("EntityCommandPanel source 'gas.ability-slots' is not registered.");
        }

        var context = new EntityCommandPanelSourceContext(target, GasAbilitySlotsSourceId, "city-rally-webui");
        EntityCommandPanelSourceDispatch.TryGetRevision(source, in context, out uint revision);
        int groupCount = EntityCommandPanelSourceDispatch.GetGroupCount(source, in context);
        var groups = new List<CityRallyCommandGroupView>(Math.Max(groupCount, 0));
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
            var slotViews = new CityRallyCommandSlotView[slotCount];
            for (int slotIndex = 0; slotIndex < slotCount; slotIndex++)
            {
                EntityCommandPanelSlotView slot = slots[slotIndex];
                slotViews[slotIndex] = new CityRallyCommandSlotView(
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

            groups.Add(new CityRallyCommandGroupView(groupIndex, group.GroupLabel, slotViews));
        }

        int statusCount = EntityCommandPanelSourceDispatch.CopyStatuses(source, in context, statuses);
        int queueCount = EntityCommandPanelSourceDispatch.CopyQueueItems(source, in context, queueItems);
        return new CityRallyCommandPanelView(
            EntityKey(target),
            revision,
            EntityCommandPanelSourceDispatch.CanActivate(source),
            groups.ToArray(),
            statuses.Take(statusCount)
                .Select(static status => new CityRallyStatusView(status.Label, status.Detail, status.ProgressPermille, status.AccentColorHex))
                .ToArray(),
            queueItems.Take(queueCount)
                .Select(static item => new CityRallyQueueItemView(item.Label, item.Detail, item.Stage.ToString(), 0, item.AccentColorHex))
                .ToArray(),
            string.Empty);
    }

    private static CityRallyResourceChipView[] BuildResourceChips()
    {
        return new[]
        {
            new CityRallyResourceChipView("Credits", 0f, ""),
            new CityRallyResourceChipView("Health", 0f, "")
        };
    }

    private static CityRallyBuildableView[] BuildBuildables(CityRallyCommandPanelView commands)
    {
        return commands.Groups
            .SelectMany(group => group.Slots)
            .Where(slot => slot.Enabled)
            .Select(slot => new CityRallyBuildableView(slot.Label, slot.AbilityId, slot.AbilityKey, string.Empty))
            .ToArray();
    }

    private CityRallyQueueItemView[] BuildProductionQueue(CityRallyCommandPanelView commands)
    {
        return commands.Queue;
    }

    private CityRallyDiagnosticsView BuildDiagnostics(string reason, string mapId, CityRallyEntityView[] entities, CityRallyCommandPanelView commands)
    {
        return new CityRallyDiagnosticsView(
            mapId,
            entities.Length,
            commands.Groups.Sum(group => group.Slots.Length),
            commands.Statuses.Length,
            _tick,
            reason);
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

        EntityCollectionStore? collections = _engine.GetService(CoreServiceKeys.EntityCollectionStore);
        Entity owner = ClientLocalSeatAccess.RequireSolePossessedRep(_engine);
        if (collections == null || !_engine.World.IsAlive(owner))
        {
            return WebUiCommandResult.Fail("command_source_missing", "EntityCollectionStore or sole ClientLocalSeat possession is missing.");
        }

        Span<Entity> next = stackalloc Entity[1];
        next[0] = target;
        var descriptor = EntityCollectionDescriptor.Create(
            EntityCollectionKeys.CommandSource,
            EntityCollectionSourceKind.Explicit,
            EntityCollectionRoleKind.CommandSource,
            owner,
            target,
            "City rally command source",
            "Selected through the browser data plane.");
        collections.Replace(owner, descriptor, next, owner);
        FocusCameraOnEntity(target);
        return WebUiCommandResult.Ok();
    }

    private void FocusCameraOnEntity(Entity target)
    {
        if (!_engine.World.IsAlive(target) ||
            !_engine.World.TryGet(target, out WorldPositionCm worldPosition))
        {
            return;
        }

        MapConfig? mapConfig = _engine.CurrentMapSession?.MapConfig;
        if (mapConfig == null)
        {
            return;
        }

        CameraConfig? cam = mapConfig.DefaultCamera;
        string virtualCameraId = string.IsNullOrWhiteSpace(cam?.VirtualCameraId)
            ? "Default"
            : cam.VirtualCameraId;

        _engine.GlobalContext[CoreServiceKeys.VirtualCameraRequest.Name] = new VirtualCameraRequest
        {
            Id = virtualCameraId,
            BlendDurationSeconds = 0f,
            SnapToFollowTargetWhenAvailable = true,
            ResetRuntimeState = true
        };

        _engine.GlobalContext[CoreServiceKeys.CameraPoseRequest.Name] = new CameraPoseRequest
        {
            VirtualCameraId = virtualCameraId,
            TargetCm = worldPosition.Value.ToVector2(),
            Yaw = cam?.Yaw,
            Pitch = cam?.Pitch,
            DistanceCm = ResolveFocusDistance(cam?.DistanceCm),
            FovYDeg = cam?.FovYDeg
        };
    }

    private static float? ResolveFocusDistance(float? distanceCm)
    {
        if (!distanceCm.HasValue || distanceCm.Value <= 0f)
        {
            return distanceCm;
        }

        return MathF.Max(7000f, distanceCm.Value * 0.72f);
    }

    private WebUiCommandResult ActivateAbilitySlot(WebUiCommandRequest request)
    {
        Entity target = ResolveCommandTarget(request);
        if (target == Entity.Null || !_engine.World.IsAlive(target))
        {
            return WebUiCommandResult.Fail("target_missing", "activateAbilitySlot needs a selected entity or payload.entityKey.");
        }

        if (!CanSolePossessedCommand(target))
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

        var context = new EntityCommandPanelSourceContext(target, GasAbilitySlotsSourceId, "city-rally-webui");
        if (!TryGetActionableSlot(source, in context, groupIndex, slotIndex, out _))
        {
            return WebUiCommandResult.Fail("ability_not_actionable", "This command slot has no gameplay execution exposed by GAS.");
        }

        if (!TryBindActiveInputMapping(target))
        {
            return WebUiCommandResult.Fail("input_mapping_missing", "ActiveInputOrderMapping is not ready for the selected command target.");
        }

        InputOrderActivationResult activation = EntityCommandPanelSourceDispatch.ActivateSlot(
            source,
            in context,
            groupIndex,
            slotIndex);
        if (activation.State == InputOrderActivationState.Rejected)
        {
            return WebUiCommandResult.Fail(
                MapActivationRejectionCode(activation.Rejection),
                MapActivationRejectionMessage(activation.Rejection));
        }

        return WebUiCommandResult.Ok();
    }

    private static string MapActivationRejectionCode(OrderSubmitResult rejection)
    {
        return rejection switch
        {
            OrderSubmitResult.RejectedQueueFull => "ability_queue_full",
            OrderSubmitResult.RejectedInvalidActor => "ability_invalid_actor",
            OrderSubmitResult.RejectedInvalidOrderType => "ability_invalid_order_type",
            OrderSubmitResult.RejectedByRule => "ability_rejected_by_rule",
            OrderSubmitResult.RejectedValidation => "ability_validation_failed",
            _ => "ability_activation_failed",
        };
    }

    private static string MapActivationRejectionMessage(OrderSubmitResult rejection)
    {
        return rejection switch
        {
            OrderSubmitResult.RejectedQueueFull => "The order queue is full; this ability activation was rejected.",
            OrderSubmitResult.RejectedInvalidActor => "The selected actor is not authorized for this ability activation.",
            OrderSubmitResult.RejectedInvalidOrderType => "No mapped action is available for this command-panel slot.",
            OrderSubmitResult.RejectedByRule => "A gameplay rule rejected this ability activation.",
            OrderSubmitResult.RejectedValidation => "Ability activation failed validation before submission.",
            _ => "The shared GAS command-panel source rejected this ability activation.",
        };
    }

    private WebUiCommandResult CancelPlanting(WebUiCommandRequest request)
    {
        if (!request.Payload.TryGetProperty("entityKey", out JsonElement keyElement) ||
            keyElement.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(keyElement.GetString()) ||
            !TryResolveEntityKey(keyElement.GetString() ?? string.Empty, out Entity target))
        {
            return WebUiCommandResult.Fail("entity_not_found", "cancelPlanting requires a live entityKey.");
        }

        if (!_engine.World.Has<GameplayTagContainer>(target))
        {
            return WebUiCommandResult.Ok();
        }

        int plantingTagId = TagRegistry.GetId("Status.CityRally.Planting");
        if (plantingTagId > 0)
        {
            var tagOps = _engine.GetService(CoreServiceKeys.TagOps) as TagOps;
            if (tagOps != null)
            {
                tagOps.RemoveTag(_engine.World, target, plantingTagId);
            }
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
            return WebUiCommandResult.Fail("invalid_participant", "This slice exposes team participant ids as team-N.");
        }

        _activeFactionId = participantId;
        return WebUiCommandResult.Ok();
    }

    private bool TryBindActiveInputMapping(Entity target)
    {
        InputOrderMappingSystem? mapping = _engine.GetService(CoreServiceKeys.ActiveInputOrderMapping);
        if (mapping == null || !TryGetSolePossessedPlayerId(out int playerId) || !CanSolePossessedCommand(target))
        {
            return false;
        }

        mapping.SetSolePossessedActor(target, playerId);
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
        Entity local = ClientLocalSeatAccess.RequireSolePossessedRep(_engine);
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

    private bool CanSolePossessedCommand(Entity entity)
    {
        if (entity == Entity.Null || !_engine.World.IsAlive(entity) || !TryGetSolePossessedPlayerId(out int localPlayerId))
        {
            return false;
        }

        if (!_engine.World.TryGet(entity, out PlayerOwner owner))
        {
            return false;
        }

        return owner.PlayerId == localPlayerId;
    }

    private bool TryGetSolePossessedPlayerId(out int playerId)
    {
        playerId = 0;
        ClientLocalSeatRegistry seats = ClientLocalSeatAccess.RequireRegistry(_engine.GlobalContext);
        if (!seats.TryGetSoleSeat(out ClientLocalSeat seat) || !seat.HasPossession)
        {
            return false;
        }

        playerId = seat.PossessedPlayerId;
        return playerId > 0;
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
        if (!CanSolePossessedCommand(entity))
        {
            return Array.Empty<string>();
        }

        var registry = _engine.GetService(CoreServiceKeys.EntityCommandPanelSourceRegistry);
        if (registry == null || !registry.TryGet(GasAbilitySlotsSourceId, out IEntityCommandPanelSource source))
        {
            return Array.Empty<string>();
        }

        var context = new EntityCommandPanelSourceContext(entity, GasAbilitySlotsSourceId, "city-rally-webui-entity-list");
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

    private bool TryGetActionableSlot(
        IEntityCommandPanelSource source,
        in EntityCommandPanelSourceContext context,
        int groupIndex,
        int slotIndex,
        out EntityCommandPanelSlotView slot)
    {
        var slots = new EntityCommandPanelSlotView[AbilityStateBuffer.CAPACITY];
        int copied = EntityCommandPanelSourceDispatch.CopySlots(source, in context, groupIndex, slots);
        for (int i = 0; i < copied; i++)
        {
            if (slots[i].SlotIndex != slotIndex)
            {
                continue;
            }

            slot = slots[i];
            return IsActionableCommandSlot(slot);
        }

        slot = default;
        return false;
    }

    private static bool IsActionableCommandSlot(EntityCommandPanelSlotView slot)
    {
        return (slot.StateFlags & EntityCommandSlotStateFlags.Empty) == 0 &&
               !string.IsNullOrWhiteSpace(slot.DisplayLabel);
    }

    private static string ResolveEntityKind(string name)
    {
        string lower = name.ToLowerInvariant();
        if (lower.Contains("city") || lower.Contains("城池") || lower.Contains("yard") ||
            lower.Contains("factory") || lower.Contains("barracks") || lower.Contains("center"))
        {
            return "structure";
        }

        return "unit";
    }

    private static float ReadAttribute(in AttributeBuffer attributes, string name)
    {
        int id = AttributeRegistry.GetId(name);
        return id == AttributeRegistry.InvalidId || !attributes.HasAttribute(id)
            ? 0f
            : attributes.GetCurrent(id);
    }

    private static int ReadInt(JsonElement payload, string name, int fallback)
    {
        return payload.TryGetProperty(name, out JsonElement element) && element.ValueKind == JsonValueKind.Number
            ? element.GetInt32()
            : fallback;
    }

    private static string EntityKey(Entity entity) => $"{entity.Id}:{entity.WorldId}:{entity.Version}";

    private static string TeamName(int teamId) => $"Team {teamId}";

    private static string TeamColor(int teamId)
    {
        return teamId switch
        {
            1 => "#62C8F3",
            2 => "#FF8FA3",
            _ => "#B889FF"
        };
    }
}

internal sealed record CityRallySnapshot(
    int Tick,
    string MapId,
    string Flavor,
    string ActiveFactionId,
    CityRallyResourceChipView[] Resources,
    CityRallyFactionView[] Factions,
    CityRallyEntityView[] Entities,
    CityRallySelectionView Selection,
    CityRallyGarrisonView[] Garrison,
    CityRallyCommandPanelView Commands,
    CityRallyBuildableView[] Buildables,
    CityRallyQueueItemView[] ProductionQueue,
    CityRallyTechTreeView TechTree,
    CityRallyDiplomacyView Diplomacy,
    CityRallyDiagnosticsView Diagnostics);

internal sealed record CityRallyResourceChipView(string Name, float Amount, string Rate);

internal sealed record CityRallyFactionView(
    string Id,
    string Name,
    int TeamId,
    string Color,
    string Controller,
    bool Active,
    int EntityCount,
    string Relationship);

internal sealed record CityRallyEntityView(
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

internal sealed record CityRallySelectionView(
    string EntityKey,
    string Name,
    string Kind,
    int TeamId,
    float Health,
    float Shield,
    CityRallySelectedEntityView[] Members);

internal sealed record CityRallySelectedEntityView(string EntityKey, string Name, int TeamId);

internal sealed record CityRallyGarrisonView(
    string EntityKey,
    string Name,
    bool IsGovernor,
    bool IsPlanting,
    int ProgressPermille);

internal sealed record CityRallyCommandPanelView(
    string TargetEntityKey,
    uint Revision,
    bool CanActivate,
    CityRallyCommandGroupView[] Groups,
    CityRallyStatusView[] Statuses,
    CityRallyQueueItemView[] Queue,
    string Message)
{
    public static CityRallyCommandPanelView Empty(string message)
    {
        return new CityRallyCommandPanelView(
            string.Empty,
            0,
            false,
            Array.Empty<CityRallyCommandGroupView>(),
            Array.Empty<CityRallyStatusView>(),
            Array.Empty<CityRallyQueueItemView>(),
            message);
    }
}

internal sealed record CityRallyCommandGroupView(
    int GroupIndex,
    string Label,
    CityRallyCommandSlotView[] Slots);

internal sealed record CityRallyCommandSlotView(
    int SlotIndex,
    int AbilityId,
    string AbilityKey,
    string Label,
    string Detail,
    string ActionId,
    short CooldownPermille,
    string StateFlags,
    bool Enabled);

internal sealed record CityRallyStatusView(string Label, string Detail, short ProgressPermille, string AccentColorHex);

internal sealed record CityRallyQueueItemView(string Label, string Detail, string Stage, short ProgressPermille, string AccentColorHex);

internal sealed record CityRallyBuildableView(string Label, int AbilityId, string AbilityKey, string Detail);

internal sealed record CityRallyTechTreeView(CityRallyTechNodeView[] Nodes, CityRallyTechEdgeView[] Edges)
{
    public static CityRallyTechTreeView Empty => new(Array.Empty<CityRallyTechNodeView>(), Array.Empty<CityRallyTechEdgeView>());
}

internal sealed record CityRallyTechNodeView(string Id, string Label, string State, string AccentColorHex);

internal sealed record CityRallyTechEdgeView(string From, string To);

internal sealed record CityRallyDiplomacyView(CityRallyDiplomacyRowView[] Rows, CityRallyDiplomacyProposalView[] Proposals)
{
    public static CityRallyDiplomacyView Empty => new(Array.Empty<CityRallyDiplomacyRowView>(), Array.Empty<CityRallyDiplomacyProposalView>());
}

internal sealed record CityRallyDiplomacyRowView(string Party, string Relationship, string Stance);

internal sealed record CityRallyDiplomacyProposalView(string Id, string Title, string Status);

internal sealed record CityRallyDiagnosticsView(
    string MapId,
    int EntityCount,
    int SlotCount,
    int StatusCount,
    int Tick,
    string Reason);
