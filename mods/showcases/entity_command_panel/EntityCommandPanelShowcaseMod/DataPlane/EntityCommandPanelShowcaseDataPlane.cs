using System;
using System.Collections.Generic;
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
using Ludots.Core.Gameplay.Items;
using Ludots.Core.Client;
using Ludots.Core.Registry;
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
        private AbilityAggregationResult _aggregationResult = new();
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
            EntityCommandPanelShowcaseCommandTileView[] tiles = BuildKernelTiles(activeLabel, profileId, sourceActorCount);

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
                tiles.Length > 0 ? 1 : groupCount,
                group.GroupLabel,
                tiles,
                "Given Arcweaver, Vanguard, and Commander are active.",
                $"When the {activeLabel} command view is active.",
                thenText,
                VisibleResult(activeLabel, tiles.Length, expectedTiles, sourceActorCount),
                _lastCommand,
                string.Empty);
        }

        private EntityCommandPanelShowcaseCommandTileView[] BuildKernelTiles(
            string activeLabel,
            string profileId,
            int sourceActorCount)
        {
            if (sourceActorCount <= 0)
            {
                return Array.Empty<EntityCommandPanelShowcaseCommandTileView>();
            }

            var aggregationProfiles = _engine.GetService(CoreServiceKeys.AbilityAggregationProfileRegistry)
                ?? throw new InvalidOperationException("AbilityAggregationProfileRegistry is missing.");
            if (!aggregationProfiles.ProfileIdRegistry.TryGetId(profileId, out int profileRegistryId) ||
                !aggregationProfiles.IsInstalled(profileRegistryId))
            {
                throw new InvalidOperationException($"Aggregation profile '{profileId}' is not installed.");
            }

            var abilities = _engine.GetService(CoreServiceKeys.AbilityDefinitionRegistry)
                ?? throw new InvalidOperationException("AbilityDefinitionRegistry is missing.");

            int groupCount = aggregationProfiles.BuildGroups(
                profileRegistryId,
                _ownerScratch.AsSpan(0, sourceActorCount),
                _engine.World,
                abilities,
                ref _aggregationResult);
            var tiles = new EntityCommandPanelShowcaseCommandTileView[groupCount];
            for (int groupIndex = 0; groupIndex < groupCount; groupIndex++)
            {
                long groupKey = _aggregationResult.GetGroupKey(groupIndex);
                ReadOnlySpan<Entity> members = _aggregationResult.GroupEntities(groupIndex);
                ReadOnlySpan<int> slots = _aggregationResult.GroupSlotIndices(groupIndex);
                var contributorNames = new List<string>(members.Length);
                var abilityLabels = new List<string>(members.Length);
                int representativeAbilityId = 0;
                int representativeTemplateEntityId = 0;
                string representativeAbilityKey = string.Empty;

                for (int memberIndex = 0; memberIndex < members.Length; memberIndex++)
                {
                    Entity member = members[memberIndex];
                    AddDistinct(contributorNames, ResolveEntityName(member));
                    AbilitySlotState slot = ResolveAbilitySlot(member, slots[memberIndex]);
                    if (representativeAbilityId == 0)
                    {
                        representativeAbilityId = slot.AbilityId;
                        representativeTemplateEntityId = slot.TemplateEntityId;
                        representativeAbilityKey = slot.AbilityId > 0 ? AbilityIdRegistry.GetName(slot.AbilityId) : string.Empty;
                    }

                    AddDistinct(abilityLabels, ResolveAbilityLabel(slot));
                }

                string[] contributors = contributorNames.ToArray();
                string owner = contributors.Length == 1 ? contributors[0] : string.Empty;
                string label = ResolveGroupLabel(groupKey, representativeAbilityId, abilityLabels);
                string family = ResolveGroupFamily(groupKey, representativeAbilityId);
                string detail = ResolveGroupDetail(activeLabel, abilityLabels, contributorNames);
                tiles[groupIndex] = new EntityCommandPanelShowcaseCommandTileView(
                    groupIndex,
                    representativeAbilityId,
                    representativeTemplateEntityId,
                    representativeAbilityKey,
                    label,
                    detail,
                    string.Empty,
                    ResolveHotkey(groupIndex),
                    owner,
                    contributors.Length,
                    family,
                    ResolveAccent(contributors.Length > 0 ? contributors[0] : string.Empty, activeLabel, groupIndex),
                    nameof(EntityCommandSlotStateFlags.Base),
                    contributors);
            }

            return tiles;
        }

        private AbilitySlotState ResolveAbilitySlot(Entity owner, int slotIndex)
        {
            if (!_engine.World.IsAlive(owner) || !_engine.World.TryGet(owner, out AbilityStateBuffer abilities))
            {
                return default;
            }

            return AbilitySlotResolver.TryResolve(_engine.World, owner, slotIndex, out AbilitySlotState slot)
                ? slot
                : default;
        }

        private string ResolveAbilityLabel(AbilitySlotState slot)
        {
            if (slot.AbilityId <= 0)
            {
                return slot.TemplateEntityId > 0 ? $"Template {slot.TemplateEntityId}" : "Command";
            }

            string fallback = ResolveLeafName(AbilityIdRegistry.GetName(slot.AbilityId));
            var definitions = _engine.GetService(CoreServiceKeys.AbilityDefinitionRegistry);
            return definitions != null &&
                   definitions.TryGet(slot.AbilityId, out AbilityDefinition definition) &&
                   definition.HasPresentation &&
                   definition.Presentation != null
                ? definition.Presentation.ResolveDisplayName(fallback)
                : fallback;
        }

        private static string ResolveGroupLabel(long groupKey, int representativeAbilityId, List<string> abilityLabels)
        {
            int kind = (int)(groupKey >> 32);
            if (kind == AbilityAggregationKeyKinds.CatalogTag)
            {
                return FormatTagLeaf(AbilityCategoryRegistry.GetName((int)(uint)groupKey));
            }

            return abilityLabels.Count > 0
                ? abilityLabels[0]
                : representativeAbilityId > 0
                    ? ResolveLeafName(AbilityIdRegistry.GetName(representativeAbilityId))
                    : "Command";
        }

        private string ResolveGroupFamily(long groupKey, int representativeAbilityId)
        {
            int kind = (int)(groupKey >> 32);
            if (kind == AbilityAggregationKeyKinds.CatalogTag)
            {
                return FormatTagLeaf(AbilityCategoryRegistry.GetName((int)(uint)groupKey));
            }

            if (representativeAbilityId <= 0)
            {
                return string.Empty;
            }

            var definitions = _engine.GetService(CoreServiceKeys.AbilityDefinitionRegistry);
            if (definitions == null)
            {
                return string.Empty;
            }

            RegistryMapping[] mappings = AbilityCategoryRegistry.SnapshotMappings();
            for (int i = 0; i < mappings.Length; i++)
            {
                string name = mappings[i].Name;
                int categoryId = mappings[i].Id;
                if (categoryId > 0 &&
                    name.StartsWith("castFamily.", StringComparison.Ordinal) &&
                    definitions.HasCategory(representativeAbilityId, categoryId))
                {
                    return FormatTagLeaf(name);
                }
            }

            return string.Empty;
        }

        private static string ResolveGroupDetail(string activeLabel, List<string> abilityLabels, List<string> contributorNames)
        {
            string contributors = string.Join(", ", contributorNames);
            if (string.Equals(activeLabel, EntityCommandPanelShowcaseIds.FamilyLabel, StringComparison.Ordinal))
            {
                return string.Concat(string.Join(", ", abilityLabels), " | ", contributors);
            }

            return contributors;
        }

        private string ResolveEntityName(Entity entity)
        {
            return _engine.World.IsAlive(entity) && _engine.World.TryGet(entity, out Name name)
                ? name.Value
                : $"Entity {entity.Id}";
        }

        private static void AddDistinct(List<string> values, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], value, StringComparison.Ordinal))
                {
                    return;
                }
            }

            values.Add(value);
        }

        private static string ResolveLeafName(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return string.Empty;
            }

            int dot = id.LastIndexOf('.');
            return dot >= 0 && dot + 1 < id.Length ? id[(dot + 1)..] : id;
        }

        private static string FormatTagLeaf(string tag)
        {
            string leaf = ResolveLeafName(tag).Replace('_', ' ');
            if (leaf.Length == 0)
            {
                return string.Empty;
            }

            return string.Concat(char.ToUpperInvariant(leaf[0]), leaf.Length == 1 ? string.Empty : leaf[1..]);
        }

        private bool TryResolveCommandSource(
            out IEntityCommandPanelSource source,
            out Entity owner,
            out string error)
        {
            source = null!;
            owner = Entity.Null;
            error = string.Empty;

            if (!ClientLocalSeatAccess.TryGetSolePossessedRep(_engine, out Entity localPlayer) ||
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
            if (string.Equals(activeLabel, EntityCommandPanelShowcaseIds.TemplateLabel, StringComparison.Ordinal))
            {
                return EntityCommandPanelShowcaseIds.ExpectedTemplateTileCount;
            }

            return string.Equals(activeLabel, EntityCommandPanelShowcaseIds.AbilityLabel, StringComparison.Ordinal)
                ? EntityCommandPanelShowcaseIds.ExpectedAbilityTileCount
                : EntityCommandPanelShowcaseIds.ExpectedFamilyTileCount;
        }

        private static string ThenTextForProfile(string activeLabel)
        {
            if (string.Equals(activeLabel, EntityCommandPanelShowcaseIds.TemplateLabel, StringComparison.Ordinal))
            {
                return "Then the bottom command deck shows 3 hero sheets x 8 commands = 24 tiles.";
            }

            if (string.Equals(activeLabel, EntityCommandPanelShowcaseIds.AbilityLabel, StringComparison.Ordinal))
            {
                return "Then Fireball appears once for Arcweaver and Vanguard, while Stone Throw appears once for Commander.";
            }

            return "Then Fireball and Stone Throw collapse into one Projectile family tile marked by all three heroes.";
        }

        private static string VisibleResult(string activeLabel, int actualTiles, int expectedTiles, int sourceActorCount)
        {
            if (string.Equals(activeLabel, EntityCommandPanelShowcaseIds.TemplateLabel, StringComparison.Ordinal))
            {
                return $"{sourceActorCount} heroes x 8 commands = {actualTiles}/{expectedTiles} template tiles";
            }

            if (string.Equals(activeLabel, EntityCommandPanelShowcaseIds.AbilityLabel, StringComparison.Ordinal))
            {
                return $"Ability view = {actualTiles}/{expectedTiles}; shared abilities show contributor labels";
            }

            return $"Family view = {actualTiles}/{expectedTiles}; Projectile shows x{sourceActorCount} heroes";
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
                activeLabel == EntityCommandPanelShowcaseIds.TemplateLabel
                    ? EntityCommandPanelShowcaseIds.ExpectedTemplateTileCount
                    : activeLabel == EntityCommandPanelShowcaseIds.AbilityLabel
                        ? EntityCommandPanelShowcaseIds.ExpectedAbilityTileCount
                        : EntityCommandPanelShowcaseIds.ExpectedFamilyTileCount,
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
        string StateFlags,
        string[] ContributorNames);

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
