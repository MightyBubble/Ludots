using System;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Arch.Core;
using EntityCommandPanelMod.Runtime;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Scripting;
using Ludots.Core.UI.EntityCommandPanels;
using Ludots.WebUI.DataPlane;

namespace EntityCommandPanelShowcaseMod.DataPlane
{
    public sealed class EntityCommandPanelShowcaseDataPlane : IWebUiTopicProducer
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private readonly GameEngine _engine;
        private readonly Entity[] _ownerScratch = new Entity[EntityCommandPanelShowcaseIds.ExpectedSourceActorCount];
        private readonly EntityCommandPanelSlotView[] _slotScratch =
            new EntityCommandPanelSlotView[AbilityStateBuffer.CAPACITY * EntityCommandPanelShowcaseIds.ExpectedSourceActorCount];
        private string _lastCommand = "snapshot";

        public EntityCommandPanelShowcaseDataPlane(GameEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        }

        public string Topic => EntityCommandPanelShowcaseIds.WebUiTopic;

        public bool TryCreateSnapshot(in WebUiTopicContext context, out WebUiOutboundPacket packet)
        {
            EntityCommandPanelShowcaseSnapshot snapshot = BuildSnapshot();
            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);
            packet = new WebUiOutboundPacket(
                context.SessionId,
                Topic,
                WebUiPacketKind.Snapshot,
                WebUiDeliverySemantics.LatestWins,
                payload,
                "application/json",
                context.RequestId);
            return true;
        }

        public WebUiCommandResult ApplyCommand(WebUiCommandRequest request)
        {
            if (!string.Equals(request.Name, EntityCommandPanelShowcaseIds.SetProfileCommand, StringComparison.Ordinal))
            {
                return WebUiCommandResult.Fail(
                    "unknown_command",
                    $"Unsupported entity command panel showcase command '{request.Name}'.");
            }

            if (!TryResolveToolbar(out IEntityCommandPanelToolbarProvider toolbar, out string error))
            {
                return WebUiCommandResult.Fail("toolbar_missing", error);
            }

            string requested = ReadString(request.Payload, "buttonId");
            if (string.IsNullOrWhiteSpace(requested))
            {
                requested = ButtonIdForLabel(ReadString(request.Payload, "profile"));
            }

            if (string.IsNullOrWhiteSpace(requested))
            {
                return WebUiCommandResult.Fail(
                    "invalid_payload",
                    "setProfile requires payload.buttonId or payload.profile.");
            }

            EntityCommandPanelToolbarButtonView[] profiles = CopyProfiles(toolbar);
            if (!ContainsProfileButton(profiles, requested))
            {
                return WebUiCommandResult.Fail(
                    "profile_not_found",
                    $"Profile button '{requested}' is not available in EntityCommandPanelShowcaseMod.");
            }

            toolbar.Activate(requested);
            _lastCommand = $"setProfile:{LabelForButtonId(requested)}:ack";
            return WebUiCommandResult.Ok();
        }

        private EntityCommandPanelShowcaseSnapshot BuildSnapshot()
        {
            EntityCommandPanelToolbarButtonView[] profiles = ResolveProfiles(out string activeLabel, out uint toolbarRevision);
            string profileId = ProfileIdForLabel(activeLabel);
            string thenText = ThenTextForProfile(activeLabel);

            if (!TryResolveCommandSource(out IEntityCommandPanelSource source, out Entity owner, out string sourceError))
            {
                return EntityCommandPanelShowcaseSnapshot.NotReady(
                    profiles,
                    activeLabel,
                    profileId,
                    _lastCommand,
                    sourceError,
                    thenText);
            }

            var context = new EntityCommandPanelSourceContext(
                owner,
                CollectionGasEntityCommandPanelSource.SourceId,
                EntityCollectionKeys.CommandSource);
            EntityCommandPanelSourceDispatch.TryGetRevision(source, in context, out uint sourceRevision);
            int groupCount = EntityCommandPanelSourceDispatch.GetGroupCount(source, in context);
            EntityCommandPanelGroupView group = default;
            if (groupCount > 0)
            {
                EntityCommandPanelSourceDispatch.TryGetGroup(source, in context, 0, out group);
            }

            int sourceActorCount = CopyOwners(owner, out EntityCommandPanelShowcaseOwnerView[] owners);
            int slotCount = groupCount <= 0
                ? 0
                : EntityCommandPanelSourceDispatch.CopySlots(source, in context, 0, _slotScratch);

            var tiles = new EntityCommandPanelShowcaseCommandTileView[slotCount];
            for (int i = 0; i < slotCount; i++)
            {
                EntityCommandPanelSlotView slot = _slotScratch[i];
                string abilityKey = slot.AbilityId > 0 ? AbilityIdRegistry.GetName(slot.AbilityId) : string.Empty;
                string tileOwner = ResolveOwnerName(slot.AbilityId, sourceActorCount);
                int ownerCount = string.Equals(activeLabel, EntityCommandPanelShowcaseIds.FamilyLabel, StringComparison.Ordinal)
                    ? Math.Max(ParseOwnerCount(slot.DetailLabel), sourceActorCount)
                    : 1;
                tiles[i] = new EntityCommandPanelShowcaseCommandTileView(
                    slot.SlotIndex,
                    slot.AbilityId,
                    slot.TemplateEntityId,
                    abilityKey,
                    string.IsNullOrWhiteSpace(slot.DisplayLabel) ? $"Command {slot.SlotIndex + 1}" : slot.DisplayLabel,
                    slot.DetailLabel,
                    slot.ActionId,
                    ResolveHotkey(slot.SlotIndex),
                    tileOwner,
                    ownerCount,
                    ResolveFamilyLabel(slot.DisplayLabel, abilityKey),
                    ResolveAccent(tileOwner, activeLabel, slot.SlotIndex),
                    slot.StateFlags.ToString());
            }

            int expectedTiles = ExpectedTileCount(activeLabel);
            uint revision = sourceRevision == 0 ? toolbarRevision : sourceRevision;
            return new EntityCommandPanelShowcaseSnapshot(
                true,
                EntityCommandPanelShowcaseIds.MapId,
                activeLabel,
                profileId,
                revision,
                sourceActorCount,
                tiles.Length,
                expectedTiles,
                CreateProfileViews(profiles, activeLabel, profileId),
                owners,
                groupCount,
                group.GroupLabel,
                tiles,
                "Given Arcweaver, Vanguard, and Commander are active.",
                $"When the {activeLabel} command view is active.",
                thenText,
                VisibleResult(activeLabel, tiles.Length, expectedTiles, sourceActorCount),
                _lastCommand,
                string.Empty);
        }

        private bool TryResolveCommandSource(
            out IEntityCommandPanelSource source,
            out Entity owner,
            out string error)
        {
            source = null!;
            owner = Entity.Null;
            error = string.Empty;

            if (!_engine.TryGetService(CoreServiceKeys.LocalPlayerEntity, out Entity localPlayer) ||
                localPlayer == Entity.Null ||
                !_engine.World.IsAlive(localPlayer))
            {
                error = "Local player command owner is not ready.";
                return false;
            }

            var registry = _engine.GetService(CoreServiceKeys.EntityCommandPanelSourceRegistry);
            if (registry == null ||
                !registry.TryGet(CollectionGasEntityCommandPanelSource.SourceId, out source))
            {
                error = "Command panel data source is not ready.";
                return false;
            }

            var collections = _engine.GetService(CoreServiceKeys.EntityCollectionStore);
            if (collections == null ||
                !collections.TryGetView(localPlayer, EntityCollectionKeys.CommandSource, out EntityCollectionView view) ||
                view.Count <= 0)
            {
                error = "Hero command roster is not published yet.";
                return false;
            }

            owner = localPlayer;
            return true;
        }

        private int CopyOwners(Entity owner, out EntityCommandPanelShowcaseOwnerView[] owners)
        {
            owners = Array.Empty<EntityCommandPanelShowcaseOwnerView>();
            var collections = _engine.GetService(CoreServiceKeys.EntityCollectionStore);
            if (collections == null ||
                !collections.TryGet(owner, EntityCollectionKeys.CommandSource, out EntityCollectionHandle handle))
            {
                return 0;
            }

            int copied = collections.CopyEntities(handle, 0, _ownerScratch);
            owners = new EntityCommandPanelShowcaseOwnerView[copied];
            for (int i = 0; i < copied; i++)
            {
                Entity entity = _ownerScratch[i];
                string name = _engine.World.IsAlive(entity) && _engine.World.TryGet(entity, out Name resolved)
                    ? resolved.Value
                    : $"Entity {entity.Id}";
                owners[i] = new EntityCommandPanelShowcaseOwnerView(
                    EntityKey(entity),
                    name,
                    ResolveOwnerRole(name),
                    ResolveAccent(name, string.Empty, i),
                    i);
            }

            return copied;
        }

        private EntityCommandPanelToolbarButtonView[] ResolveProfiles(out string activeLabel, out uint revision)
        {
            activeLabel = EntityCommandPanelShowcaseIds.FamilyLabel;
            revision = 0;
            if (!TryResolveToolbar(out IEntityCommandPanelToolbarProvider toolbar, out _))
            {
                return DefaultProfiles(activeLabel);
            }

            revision = toolbar.Revision;
            EntityCommandPanelToolbarButtonView[] profiles = CopyProfiles(toolbar);
            for (int i = 0; i < profiles.Length; i++)
            {
                if (profiles[i].Active)
                {
                    activeLabel = profiles[i].Label;
                    break;
                }
            }

            return profiles.Length == 0 ? DefaultProfiles(activeLabel) : profiles;
        }

        private bool TryResolveToolbar(out IEntityCommandPanelToolbarProvider toolbar, out string error)
        {
            toolbar = _engine.GetService(CoreServiceKeys.EntityCommandPanelToolbarProvider)!;
            if (toolbar != null)
            {
                error = string.Empty;
                return true;
            }

            error = "EntityCommandPanelToolbarProvider is not installed.";
            return false;
        }

        private static EntityCommandPanelToolbarButtonView[] CopyProfiles(IEntityCommandPanelToolbarProvider toolbar)
        {
            var profiles = new EntityCommandPanelToolbarButtonView[EntityCommandPanelShowcaseIds.ProfileButtonCapacity];
            int count = toolbar.CopyButtons(profiles);
            var result = new EntityCommandPanelToolbarButtonView[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = profiles[i];
            }

            return result;
        }

        private static EntityCommandPanelToolbarButtonView[] DefaultProfiles(string activeLabel)
        {
            return new[]
            {
                new EntityCommandPanelToolbarButtonView(
                    EntityCommandPanelShowcaseIds.TemplateButtonId,
                    EntityCommandPanelShowcaseIds.TemplateLabel,
                    string.Equals(activeLabel, EntityCommandPanelShowcaseIds.TemplateLabel, StringComparison.Ordinal),
                    "#6EC6FF"),
                new EntityCommandPanelToolbarButtonView(
                    EntityCommandPanelShowcaseIds.FamilyButtonId,
                    EntityCommandPanelShowcaseIds.FamilyLabel,
                    string.Equals(activeLabel, EntityCommandPanelShowcaseIds.FamilyLabel, StringComparison.Ordinal),
                    "#F6D37A"),
                new EntityCommandPanelToolbarButtonView(
                    EntityCommandPanelShowcaseIds.AbilityButtonId,
                    EntityCommandPanelShowcaseIds.AbilityLabel,
                    string.Equals(activeLabel, EntityCommandPanelShowcaseIds.AbilityLabel, StringComparison.Ordinal),
                    "#9EE493")
            };
        }

        private static string ReadString(JsonElement payload, string propertyName)
        {
            return payload.TryGetProperty(propertyName, out JsonElement element) &&
                   element.ValueKind == JsonValueKind.String
                ? element.GetString() ?? string.Empty
                : string.Empty;
        }

        private static string ButtonIdForLabel(string label)
        {
            return label switch
            {
                EntityCommandPanelShowcaseIds.TemplateLabel => EntityCommandPanelShowcaseIds.TemplateButtonId,
                EntityCommandPanelShowcaseIds.FamilyLabel => EntityCommandPanelShowcaseIds.FamilyButtonId,
                EntityCommandPanelShowcaseIds.AbilityLabel => EntityCommandPanelShowcaseIds.AbilityButtonId,
                _ => string.Empty
            };
        }

        private static string LabelForButtonId(string buttonId)
        {
            return buttonId switch
            {
                EntityCommandPanelShowcaseIds.TemplateButtonId => EntityCommandPanelShowcaseIds.TemplateLabel,
                EntityCommandPanelShowcaseIds.FamilyButtonId => EntityCommandPanelShowcaseIds.FamilyLabel,
                EntityCommandPanelShowcaseIds.AbilityButtonId => EntityCommandPanelShowcaseIds.AbilityLabel,
                _ => buttonId
            };
        }

        private static string ProfileIdForLabel(string label)
        {
            return label switch
            {
                EntityCommandPanelShowcaseIds.TemplateLabel => EntityCommandPanelShowcaseIds.ByTemplateProfileId,
                EntityCommandPanelShowcaseIds.FamilyLabel => EntityCommandPanelShowcaseIds.ByFamilyProfileId,
                EntityCommandPanelShowcaseIds.AbilityLabel => EntityCommandPanelShowcaseIds.ByAbilityIdProfileId,
                _ => string.Empty
            };
        }

        private static int ExpectedTileCount(string activeLabel)
        {
            return string.Equals(activeLabel, EntityCommandPanelShowcaseIds.FamilyLabel, StringComparison.Ordinal)
                ? EntityCommandPanelShowcaseIds.ExpectedFamilyTileCount
                : EntityCommandPanelShowcaseIds.ExpectedIdentityTileCount;
        }

        private static string ThenTextForProfile(string activeLabel)
        {
            if (string.Equals(activeLabel, EntityCommandPanelShowcaseIds.TemplateLabel, StringComparison.Ordinal))
            {
                return "Then the bottom command deck shows 3 hero sheets x 8 commands = 24 tiles.";
            }

            if (string.Equals(activeLabel, EntityCommandPanelShowcaseIds.AbilityLabel, StringComparison.Ordinal))
            {
                return "Then ActionContext stays split across Arcweaver, Vanguard, and Commander.";
            }

            return "Then 24 commands collapse into 8 shared family tiles, each marked x3 heroes.";
        }

        private static string VisibleResult(string activeLabel, int actualTiles, int expectedTiles, int sourceActorCount)
        {
            if (string.Equals(activeLabel, EntityCommandPanelShowcaseIds.TemplateLabel, StringComparison.Ordinal))
            {
                return $"{sourceActorCount} heroes x 8 commands = {actualTiles}/{expectedTiles} template tiles";
            }

            if (string.Equals(activeLabel, EntityCommandPanelShowcaseIds.AbilityLabel, StringComparison.Ordinal))
            {
                return $"Ability view = {actualTiles}/{expectedTiles}; repeated names stay split by hero";
            }

            return $"Family view = {actualTiles}/{expectedTiles}; each shared tile shows x{sourceActorCount} heroes";
        }

        private string ResolveOwnerName(int abilityId, int sourceActorCount)
        {
            if (abilityId <= 0 || sourceActorCount <= 0)
            {
                return string.Empty;
            }

            for (int ownerIndex = 0; ownerIndex < sourceActorCount; ownerIndex++)
            {
                Entity owner = _ownerScratch[ownerIndex];
                if (!_engine.World.IsAlive(owner) ||
                    !_engine.World.TryGet(owner, out AbilityStateBuffer abilities))
                {
                    continue;
                }

                bool hasForm = _engine.World.Has<AbilityFormSlotBuffer>(owner);
                AbilityFormSlotBuffer formSlots = hasForm ? _engine.World.Get<AbilityFormSlotBuffer>(owner) : default;
                bool hasGranted = _engine.World.Has<GrantedSlotBuffer>(owner);
                GrantedSlotBuffer grantedSlots = hasGranted ? _engine.World.Get<GrantedSlotBuffer>(owner) : default;
                for (int slotIndex = 0; slotIndex < abilities.Count; slotIndex++)
                {
                    AbilitySlotState slot = AbilitySlotResolver.Resolve(
                        in abilities,
                        in formSlots,
                        hasForm,
                        in grantedSlots,
                        hasGranted,
                        slotIndex);
                    if (slot.AbilityId != abilityId)
                    {
                        continue;
                    }

                    return _engine.World.TryGet(owner, out Name name) ? name.Value : $"Entity {owner.Id}";
                }
            }

            return string.Empty;
        }

        private static string ResolveOwnerRole(string name)
        {
            if (name.Contains(EntityCommandPanelShowcaseIds.ArcweaverName, StringComparison.OrdinalIgnoreCase))
            {
                return "caster";
            }

            if (name.Contains(EntityCommandPanelShowcaseIds.VanguardName, StringComparison.OrdinalIgnoreCase))
            {
                return "frontline";
            }

            if (name.Contains(EntityCommandPanelShowcaseIds.CommanderName, StringComparison.OrdinalIgnoreCase))
            {
                return "support";
            }

            return "source";
        }

        private static string ResolveFamilyLabel(string displayLabel, string abilityKey)
        {
            if (!string.IsNullOrWhiteSpace(displayLabel))
            {
                return displayLabel;
            }

            int lastDot = abilityKey.LastIndexOf('.');
            return lastDot >= 0 && lastDot + 1 < abilityKey.Length ? abilityKey[(lastDot + 1)..] : abilityKey;
        }

        private static int ParseOwnerCount(string detail)
        {
            if (string.IsNullOrWhiteSpace(detail))
            {
                return 1;
            }

            int marker = detail.IndexOf(" owners", StringComparison.OrdinalIgnoreCase);
            if (marker <= 0)
            {
                return 1;
            }

            string prefix = detail[..marker].Trim();
            return int.TryParse(prefix, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? Math.Max(1, parsed)
                : 1;
        }

        private static string ResolveHotkey(int slotIndex)
        {
            int localSlotIndex = Math.Abs(slotIndex % AbilityStateBuffer.CAPACITY);
            return localSlotIndex switch
            {
                0 => "Q",
                1 => "W",
                2 => "E",
                3 => "R",
                4 => "Z",
                5 => "X",
                6 => "C",
                7 => "V",
                _ => (localSlotIndex + 1).ToString(CultureInfo.InvariantCulture)
            };
        }

        private static string ResolveAccent(string owner, string activeLabel, int slotIndex)
        {
            if (owner.Contains(EntityCommandPanelShowcaseIds.ArcweaverName, StringComparison.OrdinalIgnoreCase))
            {
                return "#6EC6FF";
            }

            if (owner.Contains(EntityCommandPanelShowcaseIds.VanguardName, StringComparison.OrdinalIgnoreCase))
            {
                return "#F6D37A";
            }

            if (owner.Contains(EntityCommandPanelShowcaseIds.CommanderName, StringComparison.OrdinalIgnoreCase))
            {
                return "#9EE493";
            }

            if (string.Equals(activeLabel, EntityCommandPanelShowcaseIds.FamilyLabel, StringComparison.Ordinal))
            {
                return "#D8B15E";
            }

            string[] palette = { "#6EC6FF", "#F6D37A", "#9EE493", "#D59DFF" };
            return palette[Math.Abs(slotIndex) % palette.Length];
        }

        private static string NormalizeAccent(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "#D8B15E" : value;
        }

        private static bool ContainsProfileButton(
            ReadOnlySpan<EntityCommandPanelToolbarButtonView> profiles,
            string buttonId)
        {
            for (int i = 0; i < profiles.Length; i++)
            {
                if (string.Equals(profiles[i].ButtonId, buttonId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        internal static EntityCommandPanelShowcaseProfileView[] CreateProfileViews(
            ReadOnlySpan<EntityCommandPanelToolbarButtonView> profiles,
            string activeLabel,
            string activeProfileId)
        {
            var result = new EntityCommandPanelShowcaseProfileView[profiles.Length];
            for (int i = 0; i < profiles.Length; i++)
            {
                EntityCommandPanelToolbarButtonView profile = profiles[i];
                result[i] = new EntityCommandPanelShowcaseProfileView(
                    profile.ButtonId,
                    profile.Label,
                    string.Equals(profile.Label, activeLabel, StringComparison.Ordinal)
                        ? activeProfileId
                        : ProfileIdForLabel(profile.Label),
                    profile.Active,
                    NormalizeAccent(profile.AccentColorHex));
            }

            return result;
        }

        private static string EntityKey(Entity entity)
        {
            return $"{entity.Id}:{entity.WorldId}:{entity.Version}";
        }
    }

    public sealed record EntityCommandPanelShowcaseSnapshot(
        bool Ready,
        string MapId,
        string ActiveProfile,
        string ActiveProfileId,
        uint Revision,
        int SourceActorCount,
        int TileCount,
        int ExpectedTileCount,
        EntityCommandPanelShowcaseProfileView[] Profiles,
        EntityCommandPanelShowcaseOwnerView[] Owners,
        int GroupCount,
        string GroupLabel,
        EntityCommandPanelShowcaseCommandTileView[] Tiles,
        string Given,
        string When,
        string Then,
        string VisibleResult,
        string LastCommand,
        string Error)
    {
        public static EntityCommandPanelShowcaseSnapshot NotReady(
            EntityCommandPanelToolbarButtonView[] profiles,
            string activeLabel,
            string activeProfileId,
            string lastCommand,
            string error,
            string thenText)
        {
            return new EntityCommandPanelShowcaseSnapshot(
                false,
                EntityCommandPanelShowcaseIds.MapId,
                activeLabel,
                activeProfileId,
                0,
                0,
                0,
                activeLabel == EntityCommandPanelShowcaseIds.FamilyLabel
                    ? EntityCommandPanelShowcaseIds.ExpectedFamilyTileCount
                    : EntityCommandPanelShowcaseIds.ExpectedIdentityTileCount,
                EntityCommandPanelShowcaseDataPlane.CreateProfileViews(profiles, activeLabel, activeProfileId),
                Array.Empty<EntityCommandPanelShowcaseOwnerView>(),
                0,
                string.Empty,
                Array.Empty<EntityCommandPanelShowcaseCommandTileView>(),
                "Given Arcweaver, Vanguard, and Commander are active.",
                $"When the {activeLabel} command view is active.",
                thenText,
                "Waiting for the live hero command roster.",
                lastCommand,
                error);
        }
    }

    public sealed record EntityCommandPanelShowcaseProfileView(
        string ButtonId,
        string Label,
        string ProfileId,
        bool Active,
        string AccentColorHex);

    public sealed record EntityCommandPanelShowcaseOwnerView(
        string EntityKey,
        string Name,
        string Role,
        string AccentColorHex,
        int Order);

    public sealed record EntityCommandPanelShowcaseCommandTileView(
        int SlotIndex,
        int AbilityId,
        int TemplateEntityId,
        string AbilityKey,
        string Label,
        string Detail,
        string ActionId,
        string Hotkey,
        string Owner,
        int OwnerCount,
        string Family,
        string AccentColorHex,
        string StateFlags);

    public sealed class EntityCommandPanelShowcaseCommandHandler : IWebUiCommandHandler
    {
        private readonly EntityCommandPanelShowcaseDataPlane _producer;

        public EntityCommandPanelShowcaseCommandHandler(EntityCommandPanelShowcaseDataPlane producer)
        {
            _producer = producer ?? throw new ArgumentNullException(nameof(producer));
        }

        public ValueTask<WebUiCommandResult> HandleAsync(WebUiCommandRequest request, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(_producer.ApplyCommand(request));
        }
    }

    public sealed class EntityCommandPanelShowcaseGenerationResolver : IWebUiEntityGenerationResolver
    {
        public bool IsCurrent(WebUiEntityRef entityRef)
        {
            return entityRef.StableId <= 0 && entityRef.Generation <= 0;
        }
    }

    public sealed class EntityCommandPanelShowcasePermissionValidator : IWebUiCommandPermissionValidator
    {
        public bool CanUse(WebUiCommandRequest request, out string error)
        {
            if (string.Equals(request.Name, EntityCommandPanelShowcaseIds.SetProfileCommand, StringComparison.Ordinal))
            {
                error = string.Empty;
                return true;
            }

            error = $"Command '{request.Name}' is not allowed in EntityCommandPanelShowcaseMod.";
            return false;
        }
    }
}
