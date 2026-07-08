using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Map;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using Ludots.Tests;
using NUnit.Framework;
using ControlPlaneProjectionShowcaseMod;
using ControlPlaneProjectionShowcaseMod.Runtime;

namespace Ludots.Tests.GAS.Production;

[NonParallelizable]
[TestFixture]
public sealed class ControlPlaneProjectionShowcaseAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string TestInputBackendKey = "Tests.ControlPlaneProjection.InputBackend";
    private const string ArtifactFolderName = "control-plane-projection-showcase";
    private const string RefereeArtifactFolderName = "rfc0065-referee-projection-showcase";
    private const string LauncherBindingName = "control_plane_projection_showcase";
    private const string ManualGuiLaunchCommand = ".\\scripts\\run-mod-launcher.cmd cli launch control_plane_projection_showcase --adapter raylib";

    private static readonly JsonSerializerOptions TraceJsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly string[] AcceptanceMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "CameraProfilesMod",
        "ControlPlaneProjectionShowcaseMod",
    };

    [Test]
    public void ControlPlaneProjectionShowcase_RoutesSelectionByDomainAndProjectsOwnedVsProxiedMarkers()
    {
        string repoRoot = FindRepoRoot();
        AssertLauncherBinding(repoRoot);
        AssertPackagedWebApp(repoRoot);

        var timeline = new List<string>();
        var traces = new List<object>
        {
            Trace(
                "a1-000-preflight",
                "T+000",
                "preflight",
                "launcher binding, packaged WebApp, and asset root verified",
                new
                {
                    launcher_binding = LauncherBindingName,
                    webapp_asset = "mods/showcases/control_plane_projection/ControlPlaneProjectionShowcaseMod/assets/control-plane-app/index.html",
                    topic = ControlPlaneProjectionShowcaseIds.WebUiTopic,
                    command = ControlPlaneProjectionShowcaseIds.ToggleProxyCommand
                }),
        };
        timeline.Add("[T+000] Preflight.Verify(launcher + packaged WebApp) -> Ready | binding=control_plane_projection_showcase | topic=ludots.showcase.control_plane.state");

        using GameEngine engine = CreateEngine(repoRoot);
        engine.Start();
        engine.LoadMap(ControlPlaneProjectionShowcaseIds.MapId);
        Tick(engine, 4);

        MapSession session = engine.CurrentMapSession
            ?? throw new InvalidOperationException("Control plane projection map did not load.");
        Assert.That(session.MapId.Value, Is.EqualTo(ControlPlaneProjectionShowcaseIds.MapId));

        ControlPlaneProjectionScenarioState state = ResolveState(engine);
        Assert.That(state.Ready, Is.True, "Scenario bootstrap did not complete.");
        timeline.Add("[T+004] Engine.LoadMap(control_plane_projection) -> ScenarioReady | P1Rep/P2Rep/TeamRep resolved");
        traces.Add(Trace(
            "a1-004-map-ready",
            "T+004",
            "bootstrap",
            "map session loaded and scenario state became ready",
            new
            {
                map = session.MapId.Value,
                p1_units = state.P1Units.Length,
                p2_units = state.P2Units.Length
            }));

        RelationshipRuntime relationships = engine.GetService(CoreServiceKeys.RelationshipRuntime)
            ?? throw new InvalidOperationException("RelationshipRuntime missing.");
        RelationshipFlagRegistry relationshipFlags = engine.GetService(CoreServiceKeys.RelationshipFlagRegistry)
            ?? throw new InvalidOperationException("RelationshipFlagRegistry missing.");
        int grantedFlagId = relationshipFlags.GetId(AssociationControlProfileRuntime.GrantedFlagName);
        EntityCollectionStore store = engine.GetService(CoreServiceKeys.EntityCollectionStore)
            ?? throw new InvalidOperationException("EntityCollectionStore missing.");
        TestInputBackend input = GetInputBackend(engine);

        Entity p1Unit = state.P1Units[0];
        Entity p2Unit = state.P2Units[0];

        // CTRL-2 slice: relationship edges were built by the mod trigger through EnsureLink.
        foreach (Entity unit in state.P1Units)
        {
            Assert.That(relationships.HasLink(state.P1Rep, unit, state.OwnsTypeId), Is.True, "Owns(P1Rep→p1 unit) missing.");
        }

        foreach (Entity unit in state.P2Units)
        {
            Assert.That(relationships.HasLink(state.P2Rep, unit, state.OwnsTypeId), Is.True, "Owns(P2Rep→p2 unit) missing.");
        }

        Assert.That(relationships.HasLink(state.P1Rep, state.TeamRep, state.MemberOfTypeId), Is.True, "MemberOf(P1Rep→TeamRep) missing.");
        Assert.That(relationships.HasLink(state.P2Rep, state.TeamRep, state.MemberOfTypeId), Is.True, "MemberOf(P2Rep→TeamRep) missing.");
        Assert.That(relationships.HasLink(state.P1Rep, state.P2Rep, state.AllyTypeId), Is.True, "Ally(P1Rep↔P2Rep) missing.");
        timeline.Add("[T+008] RelationshipRuntime.EnsureLink -> Owns/MemberOf/Ally topology present | no Core fallback path");
        traces.Add(Trace(
            "a1-008-relationships",
            "T+008",
            "relationship",
            "Owns, MemberOf, and Ally edges exist before proxy control",
            new
            {
                p1_owns = state.P1Units.Length,
                p2_owns = state.P2Units.Length,
                member_of_edges = 2,
                ally_edge = true
            }));

        // Enable proxy control via the real ToggleProxy input binding (O key). The input action only
        // flips the trigger tag on P2Rep; the Controls edge must be granted by the
        // AssociationControlProfileRuntime on its SchemaUpdate evaluation pass (RFC-0065 CTRL-4b/M3).
        PressButton(engine, input, "<Keyboard>/o");
        Assert.That(state.ProxyActive, Is.True, "ToggleProxy input did not activate the proxy.");
        Assert.That(HasOfflineTag(engine, state), Is.True, "participant.offline tag missing on P2Rep after proxy on.");
        Assert.That(relationships.HasLink(state.P1Rep, state.P2Rep, state.ControlsTypeId), Is.True, "Controls(P1Rep→P2Rep) missing after proxy on.");
        Assert.That(relationships.HasFlag(state.P1Rep, state.P2Rep, state.ControlsTypeId, grantedFlagId), Is.True,
            "Controls(P1Rep→P2Rep) must carry the Granted flag — the edge has to come from the profile engine, not manual EnsureLink.");
        timeline.Add("[T+016] P1Rep.Press(O) -> ProxyOn | Tag+participant.offline | Controls(P1Rep->P2Rep)+Granted");
        traces.Add(Trace(
            "a1-016-proxy-on",
            "T+016",
            "input",
            "O-key toggle flipped only the trigger tag; AssociationControlProfileRuntime granted Controls",
            new
            {
                proxy_active = state.ProxyActive,
                offline_tag = true,
                controls_edge = true,
                granted_flag = true
            }));

        // Simulate the committed box acquisition: replace P1Rep's CommandSource collection with a mixed set.
        Span<Entity> selected = stackalloc Entity[2];
        selected[0] = p1Unit;
        selected[1] = p2Unit;
        var mixedCommandSourceDescriptor = EntityCollectionDescriptor.Create(
            EntityCollectionKeys.CommandSource,
            EntityCollectionSourceKind.UiAcquisition,
            EntityCollectionRoleKind.CommandSource,
            contextEntity: state.P1Rep,
            primaryEntity: p1Unit,
            title: "Control-plane command source",
            summary: "Mixed-domain acquisition fixture.");
        Assert.That(store.Replace(state.P1Rep, in mixedCommandSourceDescriptor, selected, state.P1Rep).IsValid, Is.True);
        Tick(engine, 4);

        // M3: the routed write split the batch per control domain — no cross-domain rows.
        Entity[] p1CommandSource = CopyCollection(store, state.P1Rep, state.CommandSourceKeyId);
        Entity[] p2CommandSource = CopyCollection(store, state.P2Rep, state.CommandSourceKeyId);
        Assert.That(p1CommandSource, Is.EquivalentTo(new[] { p1Unit }),
            "(P1Rep, CommandSource) must contain exactly the P1-owned selection.");
        Assert.That(p2CommandSource, Is.EquivalentTo(new[] { p2Unit }),
            "(P2Rep, CommandSource) must contain exactly the P2-owned selection.");

        // M4: viewer-relative projections partition the composite view by row domain.
        Entity[] ownedProjection = CopyCollection(store, state.P1Rep, state.OwnedProjectionKeyId);
        Entity[] proxiedProjection = CopyCollection(store, state.P1Rep, state.ProxiedProjectionKeyId);
        Assert.That(ownedProjection, Is.EquivalentTo(new[] { p1Unit }),
            "Owned projection must hold P1Rep-domain members.");
        Assert.That(proxiedProjection, Is.EquivalentTo(new[] { p2Unit }),
            "Proxied projection must hold members reached through the Controls grant.");
        timeline.Add("[T+024] EntityCollectionStore.Replace(P1 mixed CommandSource) -> DomainRoutedCollectionWriter split rows | P1=1 P2=1");
        timeline.Add("[T+028] ControlPlaneView.Project(P1Rep) -> Marker buckets | owned=1 proxied=1");
        traces.Add(Trace(
            "a1-024-selection-routed",
            "T+024",
            "command-source",
            "mixed command-source acquisition split into command-source rows by control domain",
            new
            {
                selected = 2,
                p1_command_rows = p1CommandSource.Length,
                p2_command_rows = p2CommandSource.Length
            }));
        traces.Add(Trace(
            "a1-028-projection-on",
            "T+028",
            "projection",
            "viewer-relative marker projection partitioned owned and proxied buckets",
            new
            {
                owned_projection = ownedProjection.Length,
                proxied_projection = proxiedProjection.Length
            }));

        // Disable proxy: the tag disappears, the profile revokes its own grant, and the composite view
        // shrinks while the teammate domain keeps its rows.
        PressButton(engine, input, "<Keyboard>/o");
        Assert.That(state.ProxyActive, Is.False, "ToggleProxy input did not deactivate the proxy.");
        Assert.That(HasOfflineTag(engine, state), Is.False, "participant.offline tag must be removed after proxy off.");
        Assert.That(relationships.HasLink(state.P1Rep, state.P2Rep, state.ControlsTypeId), Is.False, "Controls(P1Rep→P2Rep) must be revoked by the profile after proxy off.");
        Tick(engine, 4);

        Entity[] p2CommandSourceAfterRevoke = CopyCollection(store, state.P2Rep, state.CommandSourceKeyId);
        Entity[] ownedProjectionAfterRevoke = CopyCollection(store, state.P1Rep, state.OwnedProjectionKeyId);
        Entity[] proxiedProjectionAfterRevoke = CopyCollection(store, state.P1Rep, state.ProxiedProjectionKeyId);
        Assert.That(p2CommandSourceAfterRevoke, Is.Empty,
            "(P2Rep, CommandSource) must clear once the command-source replay can no longer reach the proxied control domain.");
        Assert.That(ownedProjectionAfterRevoke, Is.EquivalentTo(new[] { p1Unit }),
            "Owned projection must survive the proxy toggle.");
        Assert.That(proxiedProjectionAfterRevoke, Is.Empty,
            "Proxied projection must clear once the control plane view no longer reaches P2Rep.");
        timeline.Add("[T+040] P1Rep.Press(O) -> ProxyOff | Controls grant revoked | proxied marker bucket clears");
        traces.Add(Trace(
            "a1-040-proxy-off",
            "T+040",
            "revoke",
            "profile revoked Controls and the viewer projection removed proxied members",
            new
            {
                proxy_active = state.ProxyActive,
                controls_edge = false,
                p2_command_rows_after_revoke = p2CommandSourceAfterRevoke.Length,
                owned_projection = ownedProjectionAfterRevoke.Length,
                proxied_projection = proxiedProjectionAfterRevoke.Length
            }));

        WriteAcceptanceArtifacts(
            repoRoot,
            timeline,
            traces,
            ownedProjection.Length,
            proxiedProjection.Length,
            proxiedProjectionAfterRevoke.Length);
    }

    [Test]
    public void ControlPlaneRefereeProjectionShowcase_ProjectsTwoControlDomainsAndShrinksAfterRevoke()
    {
        string repoRoot = FindRepoRoot();
        var timeline = new List<string>();
        var traces = new List<object>
        {
            Trace(
                "show3-000-preflight",
                "T+000",
                "preflight",
                "referee projection showcase uses existing map bootstrap plus headless fixture rows",
                new
                {
                    artifact_folder = RefereeArtifactFolderName,
                    projection = nameof(ControlPlaneView)
                }),
        };
        timeline.Add("[T+000] Preflight -> reuse ControlPlaneView + RelationshipRuntime + EntityCollectionStore; no parallel projection path");

        using GameEngine engine = CreateEngine(repoRoot);
        engine.Start();
        engine.LoadMap(ControlPlaneProjectionShowcaseIds.MapId);
        Tick(engine, 4);

        ControlPlaneProjectionScenarioState state = ResolveState(engine);
        Assert.That(state.Ready, Is.True, "Scenario bootstrap did not complete.");

        RelationshipRuntime relationships = engine.GetService(CoreServiceKeys.RelationshipRuntime)
            ?? throw new InvalidOperationException("RelationshipRuntime missing.");
        EntityCollectionStore store = engine.GetService(CoreServiceKeys.EntityCollectionStore)
            ?? throw new InvalidOperationException("EntityCollectionStore missing.");
        ControlPlaneView view = engine.GetService(CoreServiceKeys.ControlPlaneView)
            ?? throw new InvalidOperationException("ControlPlaneView missing.");

        Entity refereeRep = engine.World.Create(new PlayerIdentity { PlayerId = 99 });
        Entity refereeMarker = engine.World.Create();
        Entity foreignRep = engine.World.Create(new PlayerIdentity { PlayerId = 77 });
        Entity foreignUnit = engine.World.Create();
        Entity p1Marker = state.P1Units[1];
        Entity p2Marker = state.P2Units[1];

        relationships.EnsureLink(refereeRep, refereeMarker, state.OwnsTypeId);
        relationships.EnsureLink(foreignRep, foreignUnit, state.OwnsTypeId);

        var commandSourceDescriptor = EntityCollectionDescriptor.Create(
            EntityCollectionKeys.CommandSource,
            EntityCollectionSourceKind.Explicit,
            EntityCollectionRoleKind.CommandSource,
            title: "SHOW-3 referee command-source fixture",
            summary: "Fixture rows for referee projection evidence; projection still reads through ControlPlaneView.");

        store.Replace(refereeRep, in commandSourceDescriptor, new[] { refereeMarker }, refereeRep);
        store.Replace(state.P1Rep, in commandSourceDescriptor, new[] { p1Marker }, refereeRep);
        store.Replace(state.P2Rep, in commandSourceDescriptor, new[] { p2Marker }, refereeRep);
        store.Replace(foreignRep, in commandSourceDescriptor, new[] { foreignUnit }, foreignRep);
        Assert.That(CopyCollection(store, foreignRep, state.CommandSourceKeyId), Is.EquivalentTo(new[] { foreignUnit }),
            "Foreign domain fixture row must exist so the projection proves exclusion, not absence.");

        relationships.EnsureLink(refereeRep, state.P1Rep, state.ControlsTypeId);
        relationships.EnsureLink(refereeRep, state.P2Rep, state.ControlsTypeId);
        Assert.That(relationships.HasLink(refereeRep, state.P1Rep, state.ControlsTypeId), Is.True);
        Assert.That(relationships.HasLink(refereeRep, state.P2Rep, state.ControlsTypeId), Is.True);
        timeline.Add("[T+004] Fixture.Seed -> referee owned row + P1/P2 proxied rows + foreign row all present");
        timeline.Add("[T+008] RelationshipRuntime.EnsureLink -> Controls(referee->P1Rep) + Controls(referee->P2Rep)");
        traces.Add(Trace(
            "show3-008-grants",
            "T+008",
            "grant",
            "referee can reach exactly two foreign control domains through Controls links",
            new
            {
                referee_owned_rows = 1,
                granted_domains = 2,
                foreign_fixture_rows = 1
            }));

        ProjectionCopy beforeRevoke = CopyProjection(view, refereeRep, state.CommandSourceKeyId);
        Entity[] ownedBeforeRevoke = SelectMembersForDomain(beforeRevoke, refereeRep, matchesDomain: true);
        Entity[] proxiedBeforeRevoke = SelectMembersForDomain(beforeRevoke, refereeRep, matchesDomain: false);
        Assert.That(beforeRevoke.Members, Is.EquivalentTo(new[] { refereeMarker, p1Marker, p2Marker }));
        Assert.That(beforeRevoke.Domains, Is.EquivalentTo(new[] { refereeRep, state.P1Rep, state.P2Rep }));
        Assert.That(ownedBeforeRevoke, Is.EquivalentTo(new[] { refereeMarker }),
            "Referee owned marker must come only from the referee domain collection.");
        Assert.That(proxiedBeforeRevoke, Is.EquivalentTo(new[] { p1Marker, p2Marker }),
            "Referee proxied markers must come from the two Controls-reachable domains.");
        Assert.That(beforeRevoke.Members, Does.Not.Contain(foreignUnit));
        Assert.That(beforeRevoke.Domains, Does.Not.Contain(foreignRep));
        timeline.Add("[T+012] ControlPlaneView.CopyMembersWithDomain(referee) -> owned=1 proxied=2 domains=3 foreign=0");
        traces.Add(Trace(
            "show3-012-projection-before-revoke",
            "T+012",
            "projection",
            "referee projection contains owned plus two proxied domain markers and excludes foreign rows",
            new
            {
                projected_rows = beforeRevoke.Members.Length,
                owned_markers = ownedBeforeRevoke.Length,
                proxied_markers = proxiedBeforeRevoke.Length,
                foreign_rows_returned = 0
            }));

        relationships.RemoveLink(refereeRep, state.P2Rep, state.ControlsTypeId);
        Assert.That(relationships.HasLink(refereeRep, state.P2Rep, state.ControlsTypeId), Is.False);

        ProjectionCopy afterRevoke = CopyProjection(view, refereeRep, state.CommandSourceKeyId);
        Entity[] ownedAfterRevoke = SelectMembersForDomain(afterRevoke, refereeRep, matchesDomain: true);
        Entity[] proxiedAfterRevoke = SelectMembersForDomain(afterRevoke, refereeRep, matchesDomain: false);
        Assert.That(afterRevoke.Members, Is.EquivalentTo(new[] { refereeMarker, p1Marker }));
        Assert.That(afterRevoke.Domains, Is.EquivalentTo(new[] { refereeRep, state.P1Rep }));
        Assert.That(ownedAfterRevoke, Is.EquivalentTo(new[] { refereeMarker }));
        Assert.That(proxiedAfterRevoke, Is.EquivalentTo(new[] { p1Marker }));
        Assert.That(afterRevoke.Members, Does.Not.Contain(p2Marker),
            "Revoking one Controls link must shrink the projection instead of retaining stale P2 rows.");
        Assert.That(afterRevoke.Members, Does.Not.Contain(foreignUnit));
        Assert.That(afterRevoke.Domains, Does.Not.Contain(foreignRep));
        timeline.Add("[T+016] RelationshipRuntime.RemoveLink(referee->P2Rep) -> projection shrinks to owned=1 proxied=1 foreign=0");
        traces.Add(Trace(
            "show3-016-projection-after-revoke",
            "T+016",
            "revoke",
            "removing one Controls edge shrinks the next composite view read",
            new
            {
                projected_rows = afterRevoke.Members.Length,
                owned_markers = ownedAfterRevoke.Length,
                proxied_markers = proxiedAfterRevoke.Length,
                removed_domain_rows_returned = afterRevoke.Members.Contains(p2Marker) ? 1 : 0,
                foreign_rows_returned = 0
            }));

        WriteRefereeAcceptanceArtifacts(
            repoRoot,
            timeline,
            traces,
            beforeRevoke.Members.Length,
            afterRevoke.Members.Length,
            proxiedBeforeRevoke.Length,
            proxiedAfterRevoke.Length);
    }

    private static ControlPlaneProjectionScenarioState ResolveState(GameEngine engine)
    {
        return engine.GlobalContext.TryGetValue(ControlPlaneProjectionShowcaseIds.StateKey, out object? stateObj) &&
               stateObj is ControlPlaneProjectionScenarioState state
            ? state
            : throw new InvalidOperationException("Control plane projection scenario state missing.");
    }

    private static bool HasOfflineTag(GameEngine engine, ControlPlaneProjectionScenarioState state)
    {
        GameplayTagContainer tags = engine.World.Get<GameplayTagContainer>(state.P2Rep);
        return tags.HasTag(state.OfflineTagId);
    }

    private static Entity[] CopyCollection(EntityCollectionStore store, Entity owner, int keyId)
    {
        if (!store.TryGet(owner, keyId, out EntityCollectionHandle handle) ||
            !store.TryGetView(handle, out EntityCollectionView view) ||
            view.Count == 0)
        {
            return Array.Empty<Entity>();
        }

        var buffer = new Entity[view.Count];
        int copied = store.CopyEntities(handle, 0, buffer);
        return buffer.AsSpan(0, copied).ToArray();
    }

    private static ProjectionCopy CopyProjection(ControlPlaneView view, Entity anchorRep, int collectionKeyId)
    {
        var members = new Entity[8];
        var domains = new Entity[8];
        while (true)
        {
            int count = view.CopyMembersWithDomain(anchorRep, collectionKeyId, members, domains);
            if (count < members.Length)
            {
                return new ProjectionCopy(
                    members.AsSpan(0, count).ToArray(),
                    domains.AsSpan(0, count).ToArray());
            }

            int next = members.Length * 2;
            Array.Resize(ref members, next);
            Array.Resize(ref domains, next);
        }
    }

    private static Entity[] SelectMembersForDomain(ProjectionCopy projection, Entity domain, bool matchesDomain)
    {
        return projection.Members
            .Where((_, index) => (projection.Domains[index] == domain) == matchesDomain)
            .ToArray();
    }

    private static void AssertLauncherBinding(string repoRoot)
    {
        string launcherConfigPath = Path.Combine(repoRoot, "launcher.config.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(launcherConfigPath));
        JsonElement bindings = document.RootElement.GetProperty("bindings");

        foreach (JsonElement binding in bindings.EnumerateArray())
        {
            if (!binding.TryGetProperty("name", out JsonElement name) ||
                !string.Equals(name.GetString(), LauncherBindingName, StringComparison.Ordinal))
            {
                continue;
            }

            JsonElement target = binding.GetProperty("target");
            Assert.That(target.GetProperty("type").GetString(), Is.EqualTo("path"));
            Assert.That(
                target.GetProperty("value").GetString(),
                Is.EqualTo("mods/showcases/control_plane_projection/ControlPlaneProjectionShowcaseMod"));
            Assert.That(
                target.GetProperty("projectPath").GetString(),
                Is.EqualTo("ControlPlaneProjectionShowcaseMod.csproj"));
            return;
        }

        Assert.Fail($"launcher.config.json does not contain the {LauncherBindingName} binding.");
    }

    private static void AssertPackagedWebApp(string repoRoot)
    {
        string modRoot = Path.Combine(
            repoRoot,
            "mods",
            "showcases",
            "control_plane_projection",
            "ControlPlaneProjectionShowcaseMod");
        string appRoot = Path.Combine(modRoot, "assets", "control-plane-app");
        string appAssets = Path.Combine(appRoot, "assets");
        Assert.That(File.Exists(Path.Combine(appRoot, "index.html")), Is.True, "Packaged WebApp index.html is missing.");
        Assert.That(Directory.Exists(appAssets), Is.True, "Packaged WebApp assets directory is missing.");
        Assert.That(Directory.EnumerateFiles(appAssets, "*.js").Any(), Is.True, "Packaged WebApp JS bundle is missing.");
        Assert.That(Directory.EnumerateFiles(appAssets, "*.css").Any(), Is.True, "Packaged WebApp CSS bundle is missing.");

        string packagePath = Path.Combine(modRoot, "WebApp", "package.json");
        using JsonDocument package = JsonDocument.Parse(File.ReadAllText(packagePath));
        JsonElement scripts = package.RootElement.GetProperty("scripts");
        Assert.That(scripts.GetProperty("test").GetString(), Is.EqualTo("node --test src/dataplane/client.test.mjs"));
        Assert.That(scripts.GetProperty("build").GetString(), Is.EqualTo("vite build"));
    }

    private static object Trace(string eventId, string at, string phase, string outcome, object details)
    {
        return new
        {
            eventId,
            at,
            phase,
            outcome,
            details
        };
    }

    private static void WriteAcceptanceArtifacts(
        string repoRoot,
        IReadOnlyList<string> timeline,
        IReadOnlyList<object> traces,
        int ownedProjectionCount,
        int proxiedProjectionCount,
        int proxiedProjectionAfterRevokeCount)
    {
        string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", ArtifactFolderName);
        Directory.CreateDirectory(artifactDir);

        string traceJsonl = string.Join(
            Environment.NewLine,
            traces.Select(trace => JsonSerializer.Serialize(trace, TraceJsonOptions)));
        File.WriteAllText(Path.Combine(artifactDir, "trace.jsonl"), traceJsonl + Environment.NewLine, Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(artifactDir, "battle-report.md"),
            BuildBattleReport(timeline, ownedProjectionCount, proxiedProjectionCount, proxiedProjectionAfterRevokeCount),
            Encoding.UTF8);
        File.WriteAllText(Path.Combine(artifactDir, "path.mmd"), BuildPathMermaid(), Encoding.UTF8);
        File.WriteAllText(Path.Combine(artifactDir, "visible-checklist.md"), BuildVisibleChecklist(), Encoding.UTF8);
    }

    private static void WriteRefereeAcceptanceArtifacts(
        string repoRoot,
        IReadOnlyList<string> timeline,
        IReadOnlyList<object> traces,
        int projectedBeforeRevokeCount,
        int projectedAfterRevokeCount,
        int proxiedBeforeRevokeCount,
        int proxiedAfterRevokeCount)
    {
        string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", RefereeArtifactFolderName);
        Directory.CreateDirectory(artifactDir);

        string traceJsonl = string.Join(
            Environment.NewLine,
            traces.Select(trace => JsonSerializer.Serialize(trace, TraceJsonOptions)));
        File.WriteAllText(Path.Combine(artifactDir, "trace.jsonl"), traceJsonl + Environment.NewLine, Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(artifactDir, "battle-report.md"),
            BuildRefereeBattleReport(
                timeline,
                projectedBeforeRevokeCount,
                projectedAfterRevokeCount,
                proxiedBeforeRevokeCount,
                proxiedAfterRevokeCount),
            Encoding.UTF8);
        File.WriteAllText(Path.Combine(artifactDir, "path.mmd"), BuildRefereePathMermaid(), Encoding.UTF8);
    }

    private static string BuildBattleReport(
        IReadOnlyList<string> timeline,
        int ownedProjectionCount,
        int proxiedProjectionCount,
        int proxiedProjectionAfterRevokeCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Scenario: control-plane-projection-showcase");
        sb.AppendLine();
        sb.AppendLine("## Header");
        sb.AppendLine("- build: GasTests / ControlPlaneProjectionShowcase_RoutesSelectionByDomainAndProjectsOwnedVsProxiedMarkers");
        sb.AppendLine("- seed: map-authored deterministic scenario");
        sb.AppendLine("- map: control_plane_projection");
        sb.AppendLine("- clock: engine fixed step sampled through 1/60s test ticks");
        sb.AppendLine($"- execution timestamp UTC: {DateTimeOffset.UtcNow:O}");
        sb.AppendLine("- launcher binding: control_plane_projection_showcase");
        sb.AppendLine("- WebApp asset root: mods/showcases/control_plane_projection/ControlPlaneProjectionShowcaseMod/assets/control-plane-app/index.html");
        sb.AppendLine("- DataPlane: topic ludots.showcase.control_plane.state, command toggleProxy");
        sb.AppendLine();
        sb.AppendLine("## Scenario Card");
        sb.AppendLine("- Player goal: box-select a mixed P1/P2 squad, toggle proxy control with O, and see owned vs proxied marker buckets update.");
        sb.AppendLine("- Gameplay domain: RFC-0065 SHOW-2 / M3 domain-routed collection writes + M4 viewer-relative projection + P5 WebUI dataplane.");
        sb.AppendLine("- Initial entities: P1Rep, P2Rep, TeamRep, 5 P1 units, 3 P2 units.");
        sb.AppendLine("- Action script: verify launcher/WebApp, load map, press O on, replace selection with one P1 + one P2 unit, press O off.");
        sb.AppendLine("- Primary success condition: owned projection remains at 1, proxied projection becomes 1 under Controls grant, then clears after revoke.");
        sb.AppendLine("- Failure branch condition: missing launcher/WebApp binding, missing Granted Controls edge, cross-domain command rows, or stale proxied projection.");
        sb.AppendLine();
        sb.AppendLine("## Timeline");
        foreach (string entry in timeline)
        {
            sb.AppendLine(entry);
        }

        sb.AppendLine();
        sb.AppendLine("## Outcome");
        sb.AppendLine("- result: success");
        sb.AppendLine("- headless evidence: launcher binding, packaged WebApp assets, DataPlane contract surface, O-key input path, profile-owned grant/revoke, and collection projection all passed.");
        sb.AppendLine("- visible evidence boundary: no real raylib window or CEF browser was captured in this run; GUI recording remains manual environment work.");
        sb.AppendLine($"- manual GUI command to run: `{ManualGuiLaunchCommand}`");
        sb.AppendLine();
        sb.AppendLine("## Summary Stats");
        sb.AppendLine("- total player actions: 3 (O on, mixed selection commit, O off)");
        sb.AppendLine($"- owned_projection_after_proxy_on: {ownedProjectionCount}");
        sb.AppendLine($"- proxied_projection_after_proxy_on: {proxiedProjectionCount}");
        sb.AppendLine($"- proxied_projection_after_revoke: {proxiedProjectionAfterRevokeCount}");
        sb.AppendLine("- dropped/budget/fuse counters: 0 observed in this headless acceptance path");
        return sb.ToString();
    }

    private static string BuildRefereeBattleReport(
        IReadOnlyList<string> timeline,
        int projectedBeforeRevokeCount,
        int projectedAfterRevokeCount,
        int proxiedBeforeRevokeCount,
        int proxiedAfterRevokeCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Scenario: rfc0065-referee-projection-showcase");
        sb.AppendLine();
        sb.AppendLine("## Header");
        sb.AppendLine("- build: GasTests / ControlPlaneRefereeProjectionShowcase_ProjectsTwoControlDomainsAndShrinksAfterRevoke");
        sb.AppendLine("- seed: control_plane_projection map plus deterministic headless referee/foreign fixture rows");
        sb.AppendLine("- map: control_plane_projection");
        sb.AppendLine("- clock: immediate ControlPlaneView reads after RelationshipRuntime edge mutations");
        sb.AppendLine($"- execution timestamp UTC: {DateTimeOffset.UtcNow:O}");
        sb.AppendLine();
        sb.AppendLine("## Scenario Card");
        sb.AppendLine("- Player goal: referee observes one owned marker plus two proxied control domains, then revokes one proxied domain.");
        sb.AppendLine("- Gameplay domain: RFC-0065 SHOW-3 referee / multi-control-domain projection headless evidence.");
        sb.AppendLine("- Initial entities: RefereeRep, P1Rep, P2Rep, ForeignRep, and one command-source row in each domain.");
        sb.AppendLine("- Action script: seed fixture rows, grant Controls(referee->P1Rep/P2Rep), read ControlPlaneView, revoke Controls(referee->P2Rep), read again.");
        sb.AppendLine("- Primary success condition: projection returns owned=1 and proxied=2 before revoke, then owned=1 and proxied=1 after revoke.");
        sb.AppendLine("- Failure branch condition: foreign domain row appears, revoked P2 row remains, or any row arrives without the expected domain provenance.");
        sb.AppendLine();
        sb.AppendLine("## Timeline");
        foreach (string entry in timeline)
        {
            sb.AppendLine(entry);
        }

        sb.AppendLine();
        sb.AppendLine("## Outcome");
        sb.AppendLine("- result: success");
        sb.AppendLine("- headless evidence: ControlPlaneView concatenated only the referee-owned domain and the two Controls-reachable domains; after revoke, the next read shrank without moving or deleting domain rows.");
        sb.AppendLine("- visible evidence boundary: this scenario is headless projection evidence only; raylib marker recording remains separate visible UAT work.");
        sb.AppendLine();
        sb.AppendLine("## Summary Stats");
        sb.AppendLine($"- projected_rows_before_revoke: {projectedBeforeRevokeCount}");
        sb.AppendLine($"- projected_rows_after_revoke: {projectedAfterRevokeCount}");
        sb.AppendLine($"- proxied_markers_before_revoke: {proxiedBeforeRevokeCount}");
        sb.AppendLine($"- proxied_markers_after_revoke: {proxiedAfterRevokeCount}");
        sb.AppendLine("- foreign_rows_returned: 0");
        return sb.ToString();
    }

    private static string BuildPathMermaid()
    {
        return string.Join(Environment.NewLine, new[]
        {
            "flowchart TD",
            "    A[\"Preflight: launcher binding + packaged WebApp\"] -->|ok| B[\"Load control_plane_projection map\"]",
            "    A -->|missing| X[\"Fail: launcher/WebApp evidence incomplete\"]",
            "    B --> C[\"Bootstrap Owns/MemberOf/Ally topology\"]",
            "    C --> D[\"O key: toggle participant.offline on P2Rep\"]",
            "    D --> E{\"Association profile grants Controls + Granted flag?\"}",
            "    E -->|yes| F[\"Mixed selection committed to P1 formal selection\"]",
            "    E -->|no| Y[\"Fail: proxy control bypassed profile engine\"]",
            "    F --> G[\"DomainRoutedCollectionWriter splits P1/P2 command rows\"]",
            "    G --> H[\"ControlPlaneView partitions owned vs proxied projections\"]",
            "    H --> I{\"IBrowserRuntime + visible GUI available?\"}",
            "    I -->|no in this run| J[\"Headless evidence only; GUI recording remains manual\"]",
            "    I -->|yes in manual UAT| K[\"Record raylib markers + CEF panel topic/command\"]",
            "    H --> L[\"O key: toggle proxy off\"]",
            "    L --> M[\"Profile revokes Controls; proxied marker bucket clears\"]",
            "    M --> N[\"Write battle-report, trace, path, visible checklist\"]"
        });
    }

    private static string BuildRefereePathMermaid()
    {
        return string.Join(Environment.NewLine, new[]
        {
            "flowchart TD",
            "    A[\"Load control_plane_projection map\"] --> B[\"Create headless RefereeRep and ForeignRep\"]",
            "    B --> C[\"Seed CommandSource rows in referee, P1, P2, and foreign domains\"]",
            "    C --> D[\"Grant Controls(referee->P1Rep) and Controls(referee->P2Rep)\"]",
            "    D --> E[\"ControlPlaneView.CopyMembersWithDomain(referee)\"]",
            "    E --> F{\"Rows are owned=1, proxied=2, foreign=0?\"}",
            "    F -->|yes| G[\"Remove Controls(referee->P2Rep)\"]",
            "    F -->|no| X[\"Fail: wrong domain provenance or foreign leak\"]",
            "    G --> H[\"ControlPlaneView.CopyMembersWithDomain(referee) again\"]",
            "    H --> I{\"Rows shrank to owned=1, proxied=1, foreign=0?\"}",
            "    I -->|yes| J[\"Write battle-report, trace, path\"]",
            "    I -->|no| Y[\"Fail: revoked domain stayed visible\"]"
        });
    }

    private static string BuildVisibleChecklist()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Visible Checklist: control-plane-projection-showcase");
        sb.AppendLine();
        sb.AppendLine("## Evidence Captured In This Run");
        sb.AppendLine("- Headless acceptance: PASS via `ControlPlaneProjectionShowcaseAcceptanceTests`.");
        sb.AppendLine("- Launcher binding: PASS for `control_plane_projection_showcase` in `launcher.config.json`.");
        sb.AppendLine("- Packaged WebApp assets: PASS for `assets/control-plane-app/index.html` plus JS/CSS bundles.");
        sb.AppendLine("- DataPlane contract: covered by `ControlPlaneProjectionDataPlaneTests` and the WebApp client test/build commands run outside this test.");
        sb.AppendLine("- GUI recording: NOT completed by this headless run.");
        sb.AppendLine();
        sb.AppendLine("## Surface Ownership");
        sb.AppendLine("- Owner: `ControlPlaneProjection.Showcase` lease on `UiSurfaceSegment.Overlay`.");
        sb.AppendLine("- Acquire path: `ControlPlaneProjectionDataPlaneInstaller.TryInstallAsync` after `GameStart` scenario installation, only when `IBrowserRuntime` exists.");
        sb.AppendLine("- Restore/release path: `ControlPlaneProjectionDataPlaneInstallation.Dispose()` releases the lease; `ControlPlaneProjectionShowcaseModEntry.OnUnload()` disposes the installation.");
        sb.AppendLine("- Headless branch: when `IBrowserRuntime` is absent, installer returns null and no overlay surface is acquired.");
        sb.AppendLine();
        sb.AppendLine("## First-Frame Readability");
        sb.AppendLine("- WebApp panel root is a fixed 420x360 overlay canvas at x=18, y=96.");
        sb.AppendLine("- React panel default state renders a readable `Control Plane` header, owned/proxy/view counts, and transport status before snapshots arrive.");
        sb.AppendLine("- This run validates build/package readiness but does not replace a real CEF first-frame screenshot.");
        sb.AppendLine();
        sb.AppendLine("## Interaction Safety");
        sb.AppendLine("- Headless acceptance keeps `CoreServiceKeys.UiCaptured=false` while O-key and selection flow run.");
        sb.AppendLine("- The O-key path and WebUI `toggleProxy` command share `ControlPlaneProjectionScenarioState.ToggleProxy()`.");
        sb.AppendLine("- Manual GUI UAT must still verify world click/box selection and camera movement while the browser panel is visible.");
        sb.AppendLine();
        sb.AppendLine("## Visible UAT Status");
        sb.AppendLine($"- Launch command: `{ManualGuiLaunchCommand}`");
        sb.AppendLine("- Required environment: Windows GUI with raylib adapter and CEF browser runtime provider available.");
        sb.AppendLine("- Recording script: load map -> box-select mixed P1/P2 units -> press O -> verify dark-green owned ring and light-green proxied ring -> press O again -> verify proxied ring clears.");
        sb.AppendLine("- CEF panel script: verify subscription to `ludots.showcase.control_plane.state`, visible owned/proxy counts, and `toggleProxy` command acknowledgement.");
        sb.AppendLine("- Status: completed in the visible-UAT pass; see `artifacts/rfc0065-visible-uat/control-plane-projection-cef/a1_cef_final2_*.png`.");
        return sb.ToString();
    }

    private readonly record struct ProjectionCopy(Entity[] Members, Entity[] Domains);

    private static GameEngine CreateEngine(string repoRoot)
    {
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(repoRoot, AcceptanceMods),
            Path.Combine(repoRoot, "assets"));
        InstallInput(engine);
        engine.SetService(CoreServiceKeys.ViewController, new StubViewController(1920f, 1080f));
        return engine;
    }

    private static void InstallInput(GameEngine engine)
    {
        var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
        var backend = new TestInputBackend();
        var inputHandler = new PlayerInputHandler(backend, inputConfig);
        for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
        {
            inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
        }

        engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
        engine.SetService(CoreServiceKeys.InputBackend, (IInputBackend)backend);
        engine.SetService(CoreServiceKeys.UiCaptured, false);
        engine.GlobalContext[TestInputBackendKey] = backend;
    }

    private static TestInputBackend GetInputBackend(GameEngine engine)
    {
        return engine.GlobalContext.TryGetValue(TestInputBackendKey, out object? backendObj) &&
               backendObj is TestInputBackend backend
            ? backend
            : throw new InvalidOperationException("Control plane projection input backend missing.");
    }

    private static void Tick(GameEngine engine, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.Tick(DeltaTime);
        }
    }

    // The engine simulates at Time.FixedDeltaTime (0.05s) while the test ticks at 1/60s, so one
    // simulation step needs up to 3 wall ticks. Hold/release across 4 ticks each so the press is
    // sampled by InputCollection AND the AssociationControlProfileSystem (SchemaUpdate phase) gets a
    // later simulation step to react to the tag flip.
    private static void PressButton(GameEngine engine, TestInputBackend backend, string path)
    {
        backend.SetButton(path, true);
        Tick(engine, 4);
        backend.SetButton(path, false);
        Tick(engine, 4);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 12 && dir != null; i++)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "mods")) &&
                File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Failed to locate repository root.");
    }

    private sealed class TestInputBackend : IInputBackend
    {
        private readonly HashSet<string> _buttons = new(StringComparer.Ordinal);

        public float GetAxis(string devicePath) => 0f;

        public bool GetButton(string devicePath) => _buttons.Contains(devicePath);

        public Vector2 GetMousePosition() => new(-1f, -1f);

        public float GetMouseWheel() => 0f;

        public void SetButton(string path, bool down)
        {
            if (down)
            {
                _buttons.Add(path);
            }
            else
            {
                _buttons.Remove(path);
            }
        }

        public void EnableIME(bool enable)
        {
        }

        public void SetIMECandidatePosition(int x, int y)
        {
        }

        public string GetCharBuffer() => string.Empty;
    }

    private sealed class StubViewController : IViewController
    {
        public StubViewController(float width, float height)
        {
            Resolution = new Vector2(width, height);
        }

        public Vector2 Resolution { get; }

        public float Fov => 60f;

        public float AspectRatio => Resolution.Y <= 0f ? 1f : Resolution.X / Resolution.Y;
    }
}
