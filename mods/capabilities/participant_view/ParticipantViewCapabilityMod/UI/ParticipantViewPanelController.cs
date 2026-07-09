using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Knowledge;
using Ludots.Core.Map;
using Ludots.Core.Mathematics;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;
using ParticipantViewCapabilityMod.Runtime;

namespace ParticipantViewCapabilityMod.UI;

internal sealed class ParticipantViewPanelController
{
    private const float PanelWidth = 520f;
    private const int RelationshipScratchCapacity = 32;

    private readonly ParticipantViewCapabilityRuntime _runtime;
    private ReactivePage<ParticipantViewPanelState>? _page;
    private ParticipantViewPanelState _lastState = ParticipantViewPanelState.Empty;
    private GameEngine? _engine;
    private UiSurfaceLeaseHandle _lease;

    public ParticipantViewPanelController(ParticipantViewCapabilityRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public bool MountOrSync(UIRoot root, GameEngine engine)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(engine);
        if (engine.GetService(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost surfaceHost)
        {
            return false;
        }

        _engine = engine;
        ReactivePage<ParticipantViewPanelState> page = EnsurePage();
        bool changed = false;

        if (ApplyStateSnapshot(engine))
        {
            changed = true;
        }

        surfaceHost.Publish(
            surfaceHost.EnsureLease(
                ref _lease,
                new UiSurfaceLeaseRequest("ParticipantView.Panel", UiSurfaceSegment.Overlay, priority: 40)),
            UiSurfaceContribution.FromReactivePage(page));
        return true;
    }

    public void SyncIfMounted(UIRoot root, GameEngine engine)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(engine);

        _engine = engine;
        if (_page == null ||
            !_lease.IsValid ||
            engine.GetService(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost surfaceHost ||
            !surfaceHost.Revalidate(_lease))
        {
            return;
        }

        ApplyStateSnapshot(engine);
    }

    public void ClearIfOwned(UIRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);

        if (_lease.IsValid &&
            _engine?.GetService(CoreServiceKeys.UiSurfaceHost) is IUiSurfaceHost surfaceHost)
        {
            surfaceHost.Release(_lease);
            _lease = default;
        }

        _engine = null;
        _lastState = ParticipantViewPanelState.Empty;
        _page?.SetState(_ => ParticipantViewPanelState.Empty);
    }

    private ReactivePage<ParticipantViewPanelState> EnsurePage()
    {
        if (_page != null)
        {
            return _page;
        }

        GameEngine engine = RequireEngine();
        IUiTextMeasurer textMeasurer = (IUiTextMeasurer)engine.GetService(CoreServiceKeys.UiTextMeasurer);
        IUiImageSizeProvider imageSizeProvider = (IUiImageSizeProvider)engine.GetService(CoreServiceKeys.UiImageSizeProvider);
        _page = new ReactivePage<ParticipantViewPanelState>(
            textMeasurer,
            imageSizeProvider,
            ParticipantViewPanelState.Empty,
            BuildRoot);
        return _page;
    }

    private UiElementBuilder BuildRoot(ReactiveContext<ParticipantViewPanelState> context)
    {
        ParticipantViewPanelState state = context.State;
        if (string.IsNullOrWhiteSpace(state.MapId))
        {
            return BuildPanel(
                Ui.Text("Participant Views").FontSize(20f).Bold().Color("#F7FAFF"),
                Ui.Text("No active participant view map.").FontSize(12f).Color("#A6B3C4"));
        }

        return BuildPanel(
            Ui.Text("Participant Views").FontSize(20f).Bold().Color("#F7FAFF"),
            Ui.Text(state.MapLabel).FontSize(12f).Color("#D8E1EE"),
            Ui.Text(state.Summary).FontSize(12f).Color("#9FB0C4").WhiteSpace(UiWhiteSpace.Normal),
            BuildStatRow(state),
            Ui.Row(
                    BuildModeButton("Players", ParticipantViewMode.Players, state.Mode == ParticipantViewMode.Players),
                    BuildModeButton("Teams", ParticipantViewMode.Teams, state.Mode == ParticipantViewMode.Teams))
                .Gap(8f),
            Ui.Row(
                    BuildParticipantList(state).FlexBasis(220f).FlexGrow(0f).FlexShrink(0f),
                    BuildSelectionDetail(state).FlexGrow(1f).FlexShrink(1f))
                .Gap(12f)
                .Align(UiAlignItems.Start));
    }

    private UiElementBuilder BuildPanel(params UiElementBuilder[] children)
    {
        return Ui.Card(children)
            .Width(PanelWidth)
            .Padding(14f)
            .Gap(10f)
            .Radius(8f)
            .Background("#111827")
            .Border(1f, new UiColor(36, 48, 65))
            .Absolute(16f, 16f)
            .ZIndex(30);
    }

    private static UiElementBuilder BuildStatRow(ParticipantViewPanelState state)
    {
        return Ui.Row(
                BuildStatCard("Mode", state.Mode == ParticipantViewMode.Players ? "Player" : "Team", "#2A4A63"),
                BuildStatCard("Bindings", state.ParticipantCount.ToString(), "#284A3B"),
                BuildStatCard("Selected", state.CurrentMemberCount.ToString(), "#5A4219"))
            .Gap(8f);
    }

    private static UiElementBuilder BuildStatCard(string label, string value, string background)
    {
        return Ui.Card(
                Ui.Text(label).FontSize(10f).Color("#AEBCCD"),
                Ui.Text(value).FontSize(16f).Bold().Color("#F7FAFF"))
            .Padding(10f)
            .Gap(3f)
            .Radius(8f)
            .Background(background)
            .Border(1f, new UiColor(61, 79, 101))
            .FlexGrow(1f);
    }

    private UiElementBuilder BuildModeButton(string label, ParticipantViewMode mode, bool active)
    {
        return Ui.Button(label, _ => _runtime.SelectMode(RequireEngine(), mode))
            .Padding(12f, 8f)
            .Radius(8f)
            .Background(active ? "#305D8C" : "#1B2532")
            .Color(active ? "#F8FAFC" : "#CBD5E1");
    }

    private UiElementBuilder BuildParticipantList(ParticipantViewPanelState state)
    {
        if (state.Participants.Length == 0)
        {
            return Ui.Card(
                    Ui.Text("Bindings").FontSize(12f).Bold().Color("#F4C77D"),
                    Ui.Text("No authored participants for the active mode.")
                        .FontSize(12f)
                        .Color("#92A0B1")
                        .WhiteSpace(UiWhiteSpace.Normal))
                .Padding(12f)
                .Gap(8f)
                .Radius(8f)
                .Background("#0F1722")
                .Border(1f, new UiColor(36, 48, 65));
        }

        var rows = new UiElementBuilder[state.Participants.Length];
        for (int i = 0; i < state.Participants.Length; i++)
        {
            ParticipantOption option = state.Participants[i];
            rows[i] = Ui.Button(
                    $"{option.Label}\nrep {option.RepresentativeLabel}\nmembers {option.MemberCount}",
                    _ => OnParticipantSelected(option))
                .Padding(11f, 9f)
                .Radius(8f)
                .Background(option.IsSelected ? "#4B3A18" : "#17212D")
                .Color(option.IsSelected ? "#F7E5B8" : "#E2E8F0")
                .WhiteSpace(UiWhiteSpace.Normal)
                .TextAlign(UiTextAlign.Left);
        }

        return Ui.Card(
                Ui.Text(state.Mode == ParticipantViewMode.Players ? "Player Bindings" : "Team Bindings")
                    .FontSize(12f)
                    .Bold()
                    .Color("#F4C77D"),
                Ui.Text("Map-owned participant bindings resolve representative entities, then project formal selection views.")
                    .FontSize(11f)
                    .Color("#8EA2BD")
                    .WhiteSpace(UiWhiteSpace.Normal),
                Ui.ScrollView(rows)
                    .Gap(8f)
                    .Height(260f))
            .Padding(12f)
            .Gap(8f)
            .Radius(8f)
            .Background("#0F1722")
            .Border(1f, new UiColor(36, 48, 65));
    }

    private static UiElementBuilder BuildSelectionDetail(ParticipantViewPanelState state)
    {
        if (state.Selection == null)
        {
            return Ui.Card(
                    Ui.Text("Selection Projection").FontSize(12f).Bold().Color("#F4C77D"),
                    Ui.Text("No active participant selection.")
                        .FontSize(12f)
                        .Color("#92A0B1"))
                .Padding(12f)
                .Gap(8f)
                .Radius(8f)
                .Background("#0F1722")
                .Border(1f, new UiColor(36, 48, 65));
        }

        ParticipantSelectionDetail detail = state.Selection;
        UiElementBuilder[] memberRows = BuildMemberRows(detail.Members);
        return Ui.Card(
                Ui.Text(detail.Title).FontSize(12f).Bold().Color("#F4C77D"),
                Ui.Text(detail.Summary).FontSize(11f).Color("#8EA2BD").WhiteSpace(UiWhiteSpace.Normal),
                BuildInfoBlock("Representative", detail.RepresentativeHeader, detail.RepresentativeLines),
                BuildInfoBlock("Projection", detail.ProjectionHeader, detail.ProjectionLines),
                BuildInfoBlock("Knowledge", detail.KnowledgeHeader, detail.KnowledgeLines),
                BuildInfoBlock("Resources", detail.ResourceHeader, detail.ResourceLines),
                BuildInfoBlock("Members", detail.MemberHeader, detail.MemberLines),
                Ui.ScrollView(memberRows)
                    .Gap(6f)
                    .Height(132f),
                Ui.ScrollView(BuildMemberRows(detail.KnowledgeRows))
                    .Gap(6f)
                    .Height(132f))
            .Padding(12f)
            .Gap(8f)
            .Radius(8f)
            .Background("#0F1722")
            .Border(1f, new UiColor(36, 48, 65));
    }

    private static UiElementBuilder BuildInfoBlock(string title, string header, string[] lines)
    {
        var children = new List<UiElementBuilder>
        {
            Ui.Text(title).FontSize(11f).Bold().Color("#E8EEF7"),
            Ui.Text(header).FontSize(11f).Color("#F7FAFF").WhiteSpace(UiWhiteSpace.Normal)
        };

        for (int i = 0; i < lines.Length; i++)
        {
            children.Add(Ui.Text(lines[i]).FontSize(11f).Color("#9FB0C4").WhiteSpace(UiWhiteSpace.Normal));
        }

        return Ui.Card(children.ToArray())
            .Padding(10f)
            .Gap(4f)
            .Radius(8f)
            .Background("#162232")
            .Border(1f, new UiColor(44, 57, 74));
    }

    private static UiElementBuilder[] BuildMemberRows(MemberSnapshot[] members)
    {
        if (members.Length == 0)
        {
            return new[]
            {
                Ui.Card(
                        Ui.Text("No members projected into the command source.")
                            .FontSize(11f)
                            .Color("#92A0B1")
                            .WhiteSpace(UiWhiteSpace.Normal))
                    .Padding(10f)
                    .Radius(8f)
                    .Background("#121B29")
                    .Border(1f, new UiColor(38, 48, 63))
            };
        }

        var rows = new UiElementBuilder[members.Length];
        for (int i = 0; i < members.Length; i++)
        {
            MemberSnapshot member = members[i];
            rows[i] = Ui.Card(
                    Ui.Text(member.Label).FontSize(11f).Bold().Color("#F7FAFF"),
                    Ui.Text(member.Detail).FontSize(11f).Color("#9FB0C4").WhiteSpace(UiWhiteSpace.Normal))
                .Padding(10f)
                .Gap(3f)
                .Radius(8f)
                .Background("#121B29")
                .Border(1f, new UiColor(38, 48, 63));
        }

        return rows;
    }

    private void OnParticipantSelected(ParticipantOption option)
    {
        GameEngine engine = RequireEngine();
        if (_runtime.Mode == ParticipantViewMode.Players)
        {
            _runtime.SelectPlayer(engine, option.Id);
        }
        else
        {
            _runtime.SelectTeam(engine, option.Id);
        }
    }

    private bool ApplyStateSnapshot(GameEngine engine)
    {
        ParticipantViewPanelState next = CaptureState(engine);
        if (StateEquals(_lastState, next))
        {
            return false;
        }

        _lastState = next;
        EnsurePage().SetState(_ => next);
        return true;
    }

    private ParticipantViewPanelState CaptureState(GameEngine engine)
    {
        MapSession? session = engine.CurrentMapSession;
        if (session == null || !ParticipantViewCapabilityIds.IsParticipantViewMap(session.MapConfig))
        {
            return ParticipantViewPanelState.Empty;
        }

        ParticipantOption[] options = _runtime.Mode == ParticipantViewMode.Players
            ? BuildPlayerOptions(engine, session)
            : BuildTeamOptions(engine, session);

        ParticipantSelectionDetail? selection = _runtime.Mode == ParticipantViewMode.Players
            ? BuildPlayerSelectionDetail(engine, session, _runtime.SelectedPlayerId)
            : BuildTeamSelectionDetail(engine, session, _runtime.SelectedTeamId);

        int commandSourceMemberCount = TryResolveLocalCommandSourceOwner(engine, out Entity commandSourceOwner)
            ? EntityCollectionContextRuntime.GetCount(
                engine.GlobalContext,
                commandSourceOwner,
                EntityCollectionKeys.CommandSource)
            : 0;
        return new ParticipantViewPanelState(
            MapId: session.MapId.Value,
            MapLabel: $"Map: {session.MapId.Value}",
            Summary: "View mode swaps the command source between map-owned player/team representative projections.",
            Mode: _runtime.Mode,
            ParticipantCount: options.Length,
            CurrentMemberCount: commandSourceMemberCount,
            Participants: options,
            Selection: selection);
    }

    private static bool TryResolveLocalCommandSourceOwner(GameEngine engine, out Entity owner)
    {
        owner = Entity.Null;
        Entity local = engine.GetService(CoreServiceKeys.LocalPlayerEntity);
        if (local == Entity.Null || !engine.World.IsAlive(local))
        {
            return false;
        }

        owner = local;
        return true;
    }

    private ParticipantOption[] BuildPlayerOptions(GameEngine engine, MapSession session)
    {
        var options = new List<ParticipantOption>();
        foreach (KeyValuePair<int, Entity> entry in session.PlayerEntityLookup.Entries)
        {
            if (entry.Key <= 0 || !engine.World.IsAlive(entry.Value))
            {
                continue;
            }

            Entity[] members = ParticipantViewProjection.ResolvePlayerMembers(engine.World, session, entry.Key);
            options.Add(new ParticipantOption(
                Id: entry.Key,
                Label: $"P{entry.Key}",
                RepresentativeLabel: ResolveEntityLabel(engine.World, entry.Value, $"Player {entry.Key}"),
                MemberCount: members.Length,
                IsSelected: entry.Key == _runtime.SelectedPlayerId));
        }

        options.Sort(static (left, right) => left.Id.CompareTo(right.Id));
        return options.ToArray();
    }

    private ParticipantOption[] BuildTeamOptions(GameEngine engine, MapSession session)
    {
        var options = new List<ParticipantOption>();
        foreach (KeyValuePair<int, Entity> entry in session.TeamEntityLookup.Entries)
        {
            if (entry.Key <= 0 || !engine.World.IsAlive(entry.Value))
            {
                continue;
            }

            Entity[] members = ParticipantViewProjection.ResolveTeamMembers(engine.World, session, entry.Key);
            options.Add(new ParticipantOption(
                Id: entry.Key,
                Label: $"T{entry.Key}",
                RepresentativeLabel: ResolveEntityLabel(engine.World, entry.Value, $"Team {entry.Key}"),
                MemberCount: members.Length,
                IsSelected: entry.Key == _runtime.SelectedTeamId));
        }

        options.Sort(static (left, right) => left.Id.CompareTo(right.Id));
        return options.ToArray();
    }

    private ParticipantSelectionDetail? BuildPlayerSelectionDetail(GameEngine engine, MapSession session, int playerId)
    {
        if (playerId <= 0 || !session.PlayerEntityLookup.TryGet(playerId, out Entity representative) || !engine.World.IsAlive(representative))
        {
            return null;
        }

        Entity[] members = ParticipantViewProjection.ResolvePlayerMembers(engine.World, session, playerId);
        return BuildSelectionDetail(
            engine,
            session,
            representative,
            title: $"Player View  P{playerId}",
            summary: "Map player binding selects one representative entity. The runtime projects member entities owned by that player into LivePrimary.",
            projectionHeader: $"PlayerId {playerId} -> representative -> member entities",
            members);
    }

    private ParticipantSelectionDetail? BuildTeamSelectionDetail(GameEngine engine, MapSession session, int teamId)
    {
        if (teamId <= 0 || !session.TeamEntityLookup.TryGet(teamId, out Entity representative) || !engine.World.IsAlive(representative))
        {
            return null;
        }

        Entity[] members = ParticipantViewProjection.ResolveTeamMembers(engine.World, session, teamId);
        return BuildSelectionDetail(
            engine,
            session,
            representative,
            title: $"Team View  T{teamId}",
            summary: "Map team binding selects one representative entity. The runtime projects member entities carrying Team.Id into LivePrimary.",
            projectionHeader: $"TeamId {teamId} -> representative -> member entities",
            members);
    }

    private ParticipantSelectionDetail BuildSelectionDetail(
        GameEngine engine,
        MapSession session,
        Entity representative,
        string title,
        string summary,
        string projectionHeader,
        Entity[] members)
    {
        string representativeLabel = ResolveEntityLabel(engine.World, representative, $"Entity {representative.Id}");
        string representativeHeader = $"{representativeLabel}  #{representative.Id}";
        string[] representativeLines =
        {
            DescribeIdentity(engine.World, representative),
            DescribeResourceSummary(engine.World, representative),
            DescribeRelationshipSummary(engine, representative)
        };

        string[] projectionLines =
        {
            $"Map session: {session.MapId.Value}",
            $"Representative excluded from projection: yes",
            $"Command source count: {members.Length}"
        };

        KnowledgeRowSnapshot[] knowledgeRows = BuildKnowledgeSnapshots(engine, session, representative);
        string[] knowledgeLines = BuildKnowledgeSummaryLines(knowledgeRows);
        string[] resourceLines = BuildResourceLines(engine.World, representative);
        string memberHeader = members.Length == 0
            ? "No runtime members were projected."
            : $"{members.Length} entity(s) currently projected into the command source.";
        string[] memberLines =
        {
            "Each row below is a live ECS entity, not an authored string list."
        };

        return new ParticipantSelectionDetail(
            Title: title,
            Summary: summary,
            RepresentativeHeader: representativeHeader,
            RepresentativeLines: representativeLines,
            ProjectionHeader: projectionHeader,
            ProjectionLines: projectionLines,
            KnowledgeHeader: knowledgeRows.Length == 0
                ? "No map member entities available for knowledge projection."
                : $"{knowledgeRows.Length} map member entity(s) evaluated through KnowledgeProjectionResolver.",
            KnowledgeLines: knowledgeLines,
            ResourceHeader: resourceLines.Length == 0
                ? "Representative has no authored resources or tags."
                : "Representative resources live directly on the ECS entity.",
            ResourceLines: resourceLines.Length == 0 ? new[] { "AttributeBuffer / GameplayTagContainer / TagCountContainer are empty." } : resourceLines,
            MemberHeader: memberHeader,
            MemberLines: memberLines,
            Members: BuildMemberSnapshots(engine.World, members),
            KnowledgeRows: ToMemberSnapshots(knowledgeRows));
    }

    private static MemberSnapshot[] BuildMemberSnapshots(World world, Entity[] members)
    {
        var snapshots = new MemberSnapshot[members.Length];
        for (int i = 0; i < members.Length; i++)
        {
            Entity entity = members[i];
            string label = ResolveEntityLabel(world, entity, $"Entity {entity.Id}");
            snapshots[i] = new MemberSnapshot(
                Label: label,
                Detail: $"{DescribeIdentity(world, entity)}  |  {DescribeWorldPosition(world, entity)}");
        }

        return snapshots;
    }

    private static KnowledgeRowSnapshot[] BuildKnowledgeSnapshots(GameEngine engine, MapSession session, Entity viewer)
    {
        Entity[] targets = ParticipantViewProjection.ResolveMapMembers(engine.World, session);
        var snapshots = new KnowledgeRowSnapshot[targets.Length];
        KnowledgeProjectionResolver? resolver = engine.GetService(CoreServiceKeys.KnowledgeProjectionResolver);
        int currentTick = KnowledgeProjectionConsumer.ResolveCurrentTick(engine.GlobalContext);
        for (int i = 0; i < targets.Length; i++)
        {
            Entity target = targets[i];
            ParticipantKnowledgeSnapshot knowledge = ParticipantViewProjection.ResolveKnowledgeSnapshot(
                engine.World,
                resolver,
                viewer,
                target,
                currentTick);
            string label = ResolveEntityLabel(engine.World, target, $"Entity {target.Id}");
            snapshots[i] = new KnowledgeRowSnapshot(
                Row: new MemberSnapshot(
                    Label: $"{label}  |  {DescribeKnowledgeState(knowledge)}",
                    Detail: $"{DescribeIdentity(engine.World, target)}  |  {DescribeWorldPosition(engine.World, target)}  |  {DescribeFiniteDisclosure(engine, knowledge)}"),
                Knowledge: knowledge);
        }

        return snapshots;
    }

    private static string[] BuildKnowledgeSummaryLines(KnowledgeRowSnapshot[] rows)
    {
        int unknown = 0;
        int known = 0;
        int live = 0;
        int lastKnown = 0;
        int disclosed = 0;
        int finiteAttr = 0;
        int finiteRel = 0;
        int finiteTag = 0;
        for (int i = 0; i < rows.Length; i++)
        {
            ParticipantKnowledgeSnapshot knowledge = rows[i].Knowledge;
            if (!knowledge.IsKnown)
            {
                unknown++;
                continue;
            }

            known++;
            if (knowledge.IsLiveVisible)
            {
                live++;
            }

            if (knowledge.IsLastKnown)
            {
                lastKnown++;
            }

            if (knowledge.IsDisclosed)
            {
                disclosed++;
            }

            if (knowledge.HasFiniteAttributes)
            {
                finiteAttr++;
            }

            if (knowledge.HasFiniteRelationships)
            {
                finiteRel++;
            }

            if (knowledge.HasFiniteTags)
            {
                finiteTag++;
            }
        }

        return new[]
        {
            $"states unknown {unknown} | known {known} | live-visible {live} | last-known {lastKnown} | disclosed {disclosed}",
            $"finite aspects attr {finiteAttr} | relationship {finiteRel} | tag {finiteTag}"
        };
    }

    private static string DescribeKnowledgeState(ParticipantKnowledgeSnapshot knowledge)
    {
        if (!knowledge.IsKnown)
        {
            return "unknown";
        }

        if (knowledge.IsDisclosed)
        {
            return knowledge.IsLiveVisible ? "disclosed live-visible" : "disclosed last-known";
        }

        if (knowledge.IsLiveVisible)
        {
            return "live-visible";
        }

        if (knowledge.IsLastKnown)
        {
            return "last-known";
        }

        return "known";
    }

    private static MemberSnapshot[] ToMemberSnapshots(KnowledgeRowSnapshot[] rows)
    {
        var snapshots = new MemberSnapshot[rows.Length];
        for (int i = 0; i < rows.Length; i++)
        {
            snapshots[i] = rows[i].Row;
        }

        return snapshots;
    }

    private static string DescribeFiniteDisclosure(GameEngine engine, ParticipantKnowledgeSnapshot knowledge)
    {
        string attr = DescribeMask(knowledge.AttributeMask, AttributeRegistry.GetName, prefix: "attr");
        string relation = DescribeMask(
            knowledge.RelationshipTypeMask,
            id => ResolveRelationshipTypeName(engine, id),
            prefix: "relation");
        string tag = DescribeMask(knowledge.TagMask, TagRegistry.GetName, prefix: "tag");
        string source = knowledge.IsDisclosed && knowledge.Source != Entity.Null
            ? $"source #{knowledge.Source.Id}"
            : "source self";
        return $"{attr}  |  {relation}  |  {tag}  |  {source}  |  confidence {knowledge.ConfidencePermille}";
    }

    private static string DescribeMask(KnowledgeIdMask256 mask, Func<int, string> resolveName, string prefix)
    {
        var names = new string[8];
        int count = 0;
        for (int id = 0; id < 256 && count < names.Length; id++)
        {
            if (!mask.ContainsId(id))
            {
                continue;
            }

            string name = resolveName(id);
            names[count++] = string.IsNullOrWhiteSpace(name) ? id.ToString() : name;
        }

        if (count == 0)
        {
            return $"{prefix} -";
        }

        return $"{prefix} {string.Join(",", names, 0, count)}";
    }

    private static string ResolveRelationshipTypeName(GameEngine engine, int relationshipTypeId)
    {
        if (engine.GetService(CoreServiceKeys.RelationshipTypeRegistry) is RelationshipTypeRegistry registry &&
            relationshipTypeId >= 0 &&
            relationshipTypeId < registry.Count)
        {
            return registry.Get(relationshipTypeId).Name;
        }

        return relationshipTypeId.ToString();
    }

    private static string DescribeIdentity(World world, Entity entity)
    {
        string team = world.TryGet(entity, out Team teamValue) ? $"team {teamValue.Id}" : "team -";
        string owner = world.TryGet(entity, out PlayerOwner ownerValue) ? $"owner {ownerValue.PlayerId}" : "owner -";
        string rep =
            world.TryGet(entity, out TeamIdentity teamIdentity) ? $"team-rep {teamIdentity.TeamId}" :
            world.TryGet(entity, out PlayerIdentity playerIdentity) ? $"player-rep {playerIdentity.PlayerId}" :
            "member";
        return $"{rep}  |  {team}  |  {owner}";
    }

    private static string DescribeWorldPosition(World world, Entity entity)
    {
        if (!world.TryGet(entity, out WorldPositionCm position))
        {
            return "logical entity (no world position)";
        }

        WorldCmInt2 cm = position.ToWorldCmInt2();
        return $"world ({cm.X}, {cm.Y}) cm";
    }

    private static string DescribeResourceSummary(World world, Entity entity)
    {
        int attributeCount = CountAttributes(world, entity);
        int gameplayTagCount = CountGameplayTags(world, entity);
        return $"attrs {attributeCount}  |  tags {gameplayTagCount}";
    }

    private static string DescribeRelationshipSummary(GameEngine engine, Entity entity)
    {
        if (engine.GetService(CoreServiceKeys.RelationshipRuntime) is not RelationshipRuntime relationships)
        {
            return "relationships unavailable";
        }

        var outgoing = new Entity[RelationshipScratchCapacity];
        var incoming = new Entity[RelationshipScratchCapacity];
        int outgoingCount = relationships.CollectOutgoing(entity, outgoing);
        int incomingCount = relationships.CollectIncoming(entity, incoming);
        return $"relationship links out {outgoingCount}  |  in {incomingCount}";
    }

    private static string[] BuildResourceLines(World world, Entity representative)
    {
        var lines = new List<string>();
        if (world.TryGet(representative, out AttributeBuffer attributes))
        {
            for (int attributeId = 0; attributeId < AttributeBuffer.MAX_ATTRS; attributeId++)
            {
                if (!attributes.HasAttribute(attributeId))
                {
                    continue;
                }

                string name = AttributeRegistry.GetName(attributeId);
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = $"Attribute {attributeId}";
                }

                lines.Add($"{name}: base {attributes.GetBase(attributeId):0.##}  current {attributes.GetCurrent(attributeId):0.##}");
            }
        }

        if (world.TryGet(representative, out GameplayTagContainer tags))
        {
            for (int tagId = 1; tagId <= GameplayTagContainer.MAX_TAG_ID; tagId++)
            {
                if (!tags.HasTag(tagId))
                {
                    continue;
                }

                string name = TagRegistry.GetName(tagId);
                lines.Add(string.IsNullOrWhiteSpace(name) ? $"Tag {tagId}" : name);
            }
        }

        return lines.ToArray();
    }

    private static int CountAttributes(World world, Entity entity)
    {
        if (!world.TryGet(entity, out AttributeBuffer attributes))
        {
            return 0;
        }

        int count = 0;
        for (int attributeId = 0; attributeId < AttributeBuffer.MAX_ATTRS; attributeId++)
        {
            if (attributes.HasAttribute(attributeId))
            {
                count++;
            }
        }

        return count;
    }

    private static int CountGameplayTags(World world, Entity entity)
    {
        if (!world.TryGet(entity, out GameplayTagContainer tags))
        {
            return 0;
        }

        int count = 0;
        for (int tagId = 1; tagId <= GameplayTagContainer.MAX_TAG_ID; tagId++)
        {
            if (tags.HasTag(tagId))
            {
                count++;
            }
        }

        return count;
    }

    private static string ResolveEntityLabel(World world, Entity entity, string fallback)
    {
        if (world.IsAlive(entity) &&
            world.TryGet(entity, out Name name) &&
            !string.IsNullOrWhiteSpace(name.Value))
        {
            return name.Value;
        }

        return fallback;
    }

    private GameEngine RequireEngine()
    {
        return _engine ?? throw new InvalidOperationException("ParticipantViewPanelController is not bound to an engine.");
    }

    private static bool StateEquals(ParticipantViewPanelState left, ParticipantViewPanelState right)
    {
        if (!string.Equals(left.MapId, right.MapId, StringComparison.Ordinal) ||
            !string.Equals(left.MapLabel, right.MapLabel, StringComparison.Ordinal) ||
            !string.Equals(left.Summary, right.Summary, StringComparison.Ordinal) ||
            left.Mode != right.Mode ||
            left.ParticipantCount != right.ParticipantCount ||
            left.CurrentMemberCount != right.CurrentMemberCount ||
            left.Participants.Length != right.Participants.Length)
        {
            return false;
        }

        for (int i = 0; i < left.Participants.Length; i++)
        {
            if (!Equals(left.Participants[i], right.Participants[i]))
            {
                return false;
            }
        }

        if (left.Selection is null || right.Selection is null)
        {
            return left.Selection is null && right.Selection is null;
        }

        return Equals(left.Selection, right.Selection);
    }

    private sealed record ParticipantViewPanelState(
        string MapId,
        string MapLabel,
        string Summary,
        ParticipantViewMode Mode,
        int ParticipantCount,
        int CurrentMemberCount,
        ParticipantOption[] Participants,
        ParticipantSelectionDetail? Selection)
    {
        public static ParticipantViewPanelState Empty { get; } = new(
            string.Empty,
            string.Empty,
            string.Empty,
            ParticipantViewMode.Players,
            0,
            0,
            Array.Empty<ParticipantOption>(),
            null);
    }

    private sealed record ParticipantSelectionDetail(
        string Title,
        string Summary,
        string RepresentativeHeader,
        string[] RepresentativeLines,
        string ProjectionHeader,
        string[] ProjectionLines,
        string KnowledgeHeader,
        string[] KnowledgeLines,
        string ResourceHeader,
        string[] ResourceLines,
        string MemberHeader,
        string[] MemberLines,
        MemberSnapshot[] Members,
        MemberSnapshot[] KnowledgeRows);

    private readonly record struct ParticipantOption(
        int Id,
        string Label,
        string RepresentativeLabel,
        int MemberCount,
        bool IsSelected);

    private readonly record struct MemberSnapshot(
        string Label,
        string Detail);

    private readonly record struct KnowledgeRowSnapshot(
        MemberSnapshot Row,
        ParticipantKnowledgeSnapshot Knowledge);
}
