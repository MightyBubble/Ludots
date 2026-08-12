using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Arch.Core;
using EntityCommandPanelShowcaseMod;
using EntityCommandPanelShowcaseMod.DataPlane;
using EntityCommandPanelMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Client;
using Ludots.Core.Scripting;
using Ludots.Core.UI.EntityCommandPanels;
using Ludots.UI.Surface;
using Ludots.WebUI.DataPlane;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production
{
    [NonParallelizable]
    [TestFixture]
    [Category("acceptance")]
    public sealed class EntityCommandPanelShowcaseAcceptanceTests
    {
        private const float DeltaTime = 1f / 60f;
        private const string HubMapId = "interaction_showcase_hub";
        private const string ArtifactFolderName = "entity-command-panel-showcase";
        private const string LauncherBindingName = "entity_command_panel_showcase";
        private const string LauncherTargetPath = "mods/showcases/entity_command_panel/EntityCommandPanelShowcaseMod";
        private const string ManualGuiLaunchCommand = ".\\scripts\\run-mod-launcher.cmd cli launch entity_command_panel_showcase --adapter raylib";
        private const string ByTemplateProfileId = "aggregation.by_template";
        private const string ByFamilyProfileId = "aggregation.by_family";
        private const string ByAbilityIdProfileId = "aggregation.by_ability_id";
        private const string AutoProfileTimelineEnvKey = "LUDOTS_ENTITY_COMMAND_PANEL_AUTO_PROFILE_TIMELINE";

        private static readonly JsonSerializerOptions TraceJsonOptions = new()
        {
            WriteIndented = false
        };

        private static readonly string[] ExpectedFamilyLabels =
        {
            "Projectile",
            "Mobility",
            "Defense",
            "Area",
            "Dash",
            "Toggle",
            "Context",
            "Advanced"
        };

        private static readonly string[] ProfileProjectionCollectionKeys =
        {
            EntityCommandPanelShowcaseIds.TemplateProjectionCollectionKey,
            EntityCommandPanelShowcaseIds.FamilyProjectionCollectionKey,
            EntityCommandPanelShowcaseIds.AbilityProjectionCollectionKey
        };

        private static readonly string[] AcceptanceMods =
        {
            "LudotsCoreMod",
            "CoreInputMod",
            "CameraProfilesMod",
            "EntityInfoPanelsMod",
            "InteractionShowcaseMod",
            "EntityCommandPanelMod",
            "EntityCommandPanelShowcaseMod"
        };

        [Test]
        public void EntityCommandPanelShowcase_SwitchesM6AggregationProfilesAtRuntime()
        {
            string repoRoot = FindRepoRoot();
            AssertLauncherBinding(repoRoot);

            using var engine = CreateEngine(repoRoot);
            engine.LoadMap(HubMapId);
            Tick(engine, 4);

            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));

            var toolbar = engine.GetService(CoreServiceKeys.EntityCommandPanelToolbarProvider)
                ?? throw new InvalidOperationException("EntityCommandPanelToolbarProvider service is missing.");
            Assert.That(toolbar.IsVisible, Is.False, "WebUI owns the player-visible profile controls; the old Compose toolbar must not draw.");
            Assert.That(toolbar.Title, Is.EqualTo("Aggregation Profile"));
            Assert.That(toolbar.Subtitle, Does.Contain("Family"));
            EntityCommandPanelToolbarButtonView[] initialButtons = CopyToolbarButtons(toolbar);
            AssertToolbarButtons(initialButtons, "Family");
            AssertWebUiRuntimeDeclared(engine);
            AssertPackagedWebApp(repoRoot);
            AssertShowcaseCameraLocked(engine);
            AssertProfileProjectionPerformerConfig(repoRoot);
            AssertOldComposePanelClosed(engine);

            var aggregationProfiles = engine.GetService(CoreServiceKeys.AbilityAggregationProfileRegistry)
                ?? throw new InvalidOperationException("AbilityAggregationProfileRegistry service is missing.");
            ProfileRegistryEvidence[] installedProfiles =
            {
                AssertProfileInstalled(aggregationProfiles, ByTemplateProfileId),
                AssertProfileInstalled(aggregationProfiles, ByFamilyProfileId),
                AssertProfileInstalled(aggregationProfiles, ByAbilityIdProfileId)
            };
            ProfileFragmentEvidence byFamilyFragment = AssertByFamilyFragment(repoRoot);

            var registry = engine.GetService(CoreServiceKeys.EntityCommandPanelSourceRegistry)
                ?? throw new InvalidOperationException("EntityCommandPanelSourceRegistry service is missing.");
            Assert.That(registry.TryGet(CollectionGasEntityCommandPanelSource.SourceId, out IEntityCommandPanelSource source), Is.True);
            Assert.That(source, Is.TypeOf<CollectionGasEntityCommandPanelSource>());

            Entity owner = ClientLocalSeatAccess.RequireSolePossessedRep(engine);
            Assert.That(owner, Is.Not.EqualTo(Entity.Null), "interaction showcase should publish a local player command owner.");

            var context = new EntityCommandPanelSourceContext(
                owner,
                CollectionGasEntityCommandPanelSource.SourceId,
                EntityCollectionKeys.CommandSource);
            CollectionEvidence collection = AssertAggregationCollection(engine, owner);
            var slots = new EntityCommandPanelSlotView[32];

            ProfileSnapshot family = CaptureProfile(toolbar, source, in context, slots, "Family", ByFamilyProfileId);
            Tick(engine, 1);
            AssertActiveProfileProjection(engine, owner, EntityCommandPanelShowcaseIds.FamilyProjectionCollectionKey);

            ProfileSnapshot template = CaptureProfile(toolbar, source, in context, slots, "Template", ByTemplateProfileId);
            Tick(engine, 1);
            AssertActiveProfileProjection(engine, owner, EntityCommandPanelShowcaseIds.TemplateProjectionCollectionKey);

            ProfileSnapshot ability = CaptureProfile(toolbar, source, in context, slots, "Ability", ByAbilityIdProfileId);
            Tick(engine, 1);
            AssertActiveProfileProjection(engine, owner, EntityCommandPanelShowcaseIds.AbilityProjectionCollectionKey);

            Assert.That(family.SlotCount, Is.EqualTo(8),
                "by_family should group the three showcase heroes into the eight M6 command families.");
            Assert.That(template.SlotCount, Is.EqualTo(24),
                "by_template should preserve each unit template command sheet: 3 heroes x 8 slots.");
            Assert.That(ability.SlotCount, Is.EqualTo(21),
                "by_ability_id should merge shared ability definitions while keeping distinct ability definitions separate.");
            Assert.That(template.Revision, Is.Not.EqualTo(family.Revision));
            Assert.That(ability.Revision, Is.Not.EqualTo(template.Revision));
            Assert.That(family.Labels, Does.Contain("Projectile"));
            Assert.That(family.Labels, Is.EquivalentTo(ExpectedFamilyLabels));
            Assert.That(template.Labels.Count(label => string.Equals(label, "Fireball", StringComparison.Ordinal)), Is.EqualTo(2),
                "by_template should show the shared Fireball in each owning unit template row.");
            Assert.That(template.Labels.Count(label => string.Equals(label, "Stone Throw", StringComparison.Ordinal)), Is.EqualTo(1));
            Assert.That(ability.Slots.Select(slot => slot.AbilityId).Distinct().Count(), Is.EqualTo(21),
                "by_ability_id should expose one cell per distinct ability definition.");
            Assert.That(ability.Labels.Count(label => string.Equals(label, "Fireball", StringComparison.Ordinal)), Is.EqualTo(1),
                "Ability view should collapse Arcweaver and Vanguard's shared Fireball into one cell.");
            Assert.That(ability.Labels.Count(label => string.Equals(label, "Stone Throw", StringComparison.Ordinal)), Is.EqualTo(1));
            AssertWebUiDataPlaneSnapshot(engine, ability);

            WriteAcceptanceArtifacts(
                repoRoot,
                installedProfiles,
                byFamilyFragment,
                collection,
                initialButtons,
                family,
                template,
                ability);
        }

        [Test]
        public void EntityCommandPanelShowcase_VisibleUatTimelineCyclesProfiles()
        {
            string repoRoot = FindRepoRoot();
            string? previousTimeline = Environment.GetEnvironmentVariable(AutoProfileTimelineEnvKey);
            try
            {
                Environment.SetEnvironmentVariable(AutoProfileTimelineEnvKey, "1");
                using var engine = CreateEngine(repoRoot);
                engine.LoadMap(HubMapId);

                Tick(engine, 2);
                var toolbar = engine.GetService(CoreServiceKeys.EntityCommandPanelToolbarProvider)
                    ?? throw new InvalidOperationException("EntityCommandPanelToolbarProvider service is missing.");
                AssertToolbarActive(toolbar, "Template");

                Tick(engine, 90);
                AssertToolbarActive(toolbar, "Family");

                Tick(engine, 90);
                AssertToolbarActive(toolbar, "Ability");
            }
            finally
            {
                Environment.SetEnvironmentVariable(AutoProfileTimelineEnvKey, previousTimeline);
            }
        }

        private static ProfileSnapshot CaptureProfile(
            IEntityCommandPanelToolbarProvider toolbar,
            IEntityCommandPanelSource source,
            in EntityCommandPanelSourceContext context,
            EntityCommandPanelSlotView[] slots,
            string label,
            string profileId)
        {
            ActivateToolbarButton(toolbar, label);
            Assert.That(EntityCommandPanelSourceDispatch.TryGetRevision(source, in context, out uint revision), Is.True);
            Array.Clear(slots, 0, slots.Length);
            int copied = EntityCommandPanelSourceDispatch.CopySlots(source, in context, 0, slots);
            Assert.That(copied, Is.GreaterThan(0), $"{label} profile should produce aggregation slots.");
            AssertToolbarActive(toolbar, label);

            return new ProfileSnapshot(
                label,
                profileId,
                copied,
                revision,
                slots.Take(copied).Select(slot => slot.DisplayLabel).ToArray(),
                slots.Take(copied).Select(slot => new SlotSnapshot(
                    slot.SlotIndex,
                    slot.AbilityId,
                    slot.TemplateEntityId,
                    slot.DisplayLabel,
                    slot.DetailLabel,
                    slot.ActionId,
                    FormatSlotFlags(slot.StateFlags))).ToArray());
        }

        private static void ActivateToolbarButton(IEntityCommandPanelToolbarProvider toolbar, string label)
        {
            var buttons = new EntityCommandPanelToolbarButtonView[8];
            int count = toolbar.CopyButtons(buttons);
            for (int i = 0; i < count; i++)
            {
                if (!string.Equals(buttons[i].Label, label, StringComparison.Ordinal))
                {
                    continue;
                }

                toolbar.Activate(buttons[i].ButtonId);
                return;
            }

            Assert.Fail($"Toolbar did not expose profile button '{label}'.");
        }

        private static EntityCommandPanelToolbarButtonView[] CopyToolbarButtons(IEntityCommandPanelToolbarProvider toolbar)
        {
            var buttons = new EntityCommandPanelToolbarButtonView[8];
            int count = toolbar.CopyButtons(buttons);
            Assert.That(count, Is.EqualTo(3), "showcase toolbar should expose the three P3 aggregation choices.");
            return buttons.Take(count).ToArray();
        }

        private static void AssertToolbarButtons(EntityCommandPanelToolbarButtonView[] buttons, string activeLabel)
        {
            Assert.That(buttons.Select(button => button.Label), Is.EquivalentTo(new[] { "Template", "Family", "Ability" }));
            Assert.That(buttons.Count(button => button.Active), Is.EqualTo(1));
            Assert.That(buttons.Single(button => string.Equals(button.Label, activeLabel, StringComparison.Ordinal)).Active, Is.True);
            Assert.That(buttons.Single(button => string.Equals(button.Label, "Template", StringComparison.Ordinal)).ButtonId, Is.EqualTo("profile.by_template"));
            Assert.That(buttons.Single(button => string.Equals(button.Label, "Family", StringComparison.Ordinal)).ButtonId, Is.EqualTo("profile.by_family"));
            Assert.That(buttons.Single(button => string.Equals(button.Label, "Ability", StringComparison.Ordinal)).ButtonId, Is.EqualTo("profile.by_ability_id"));
        }

        private static void AssertToolbarActive(IEntityCommandPanelToolbarProvider toolbar, string label)
        {
            var buttons = new EntityCommandPanelToolbarButtonView[8];
            int count = toolbar.CopyButtons(buttons);
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                if (!string.Equals(buttons[i].Label, label, StringComparison.Ordinal))
                {
                    continue;
                }

                found = true;
                Assert.That(buttons[i].Active, Is.True, $"{label} profile button should be active after activation.");
            }

            Assert.That(found, Is.True, $"Toolbar did not expose profile button '{label}'.");
        }

        private static ProfileRegistryEvidence AssertProfileInstalled(
            AbilityAggregationProfileRegistry registry,
            string profileId)
        {
            Assert.That(registry.ProfileIdRegistry.TryGetId(profileId, out int id), Is.True,
                $"aggregation profile '{profileId}' should be registered.");
            Assert.That(registry.IsInstalled(id), Is.True,
                $"aggregation profile '{profileId}' should be compiled and installed.");
            return new ProfileRegistryEvidence(profileId, id, registry.GetOverflow(id));
        }

        private static ProfileFragmentEvidence AssertByFamilyFragment(string repoRoot)
        {
            string fragmentPath = Path.Combine(
                repoRoot,
                "mods",
                "EntityCommandPanelMod",
                "assets",
                "Configs",
                "UI",
                "ability_aggregation_profiles.json");
            Assert.That(File.Exists(fragmentPath), Is.True, "EntityCommandPanelMod should provide the by-family profile fragment.");

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(fragmentPath, Encoding.UTF8));
            foreach (JsonElement profile in document.RootElement.EnumerateArray())
            {
                string id = profile.GetProperty("id").GetString() ?? string.Empty;
                if (!string.Equals(id, ByFamilyProfileId, StringComparison.Ordinal))
                {
                    continue;
                }

                string groupBy = profile.GetProperty("groupBy").GetString() ?? string.Empty;
                string overflow = profile.TryGetProperty("overflow", out JsonElement overflowElement)
                    ? overflowElement.GetString() ?? string.Empty
                    : string.Empty;
                Assert.That(groupBy, Is.EqualTo("catalog.castFamily"));
                Assert.That(overflow, Is.EqualTo("nextPanelSlot"));
                return new ProfileFragmentEvidence(
                    ToRepoRelativePath(repoRoot, fragmentPath),
                    id,
                    groupBy,
                    overflow);
            }

            Assert.Fail($"EntityCommandPanelMod profile fragment did not declare '{ByFamilyProfileId}'.");
            return default;
        }

        private static CollectionEvidence AssertAggregationCollection(GameEngine engine, Entity owner)
        {
            var collections = engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("EntityCollectionStore service is missing.");
            Assert.That(collections.TryGetView(owner, EntityCollectionKeys.CommandSource, out EntityCollectionView view), Is.True,
                "showcase host mod should publish a command-source collection for the local player.");
            Assert.That(view.Count, Is.EqualTo(3), "M6 aggregation showcase should contain Arcweaver, Vanguard, and Commander.");
            Assert.That(view.SourceKind, Is.EqualTo(EntityCollectionSourceKind.Explicit));
            Assert.That(view.Role, Is.EqualTo(EntityCollectionRoleKind.CommandSource));

            var members = new Entity[4];
            int copied = collections.CopyEntities(owner, EntityCollectionKeys.CommandSource, members);
            Assert.That(copied, Is.EqualTo(3));

            return new CollectionEvidence(
                EntityCollectionKeys.CommandSource,
                view.Title,
                view.Summary,
                view.Revision,
                view.Count,
                copied,
                owner.Id,
                owner.Version);
        }

        private static void AssertActiveProfileProjection(GameEngine engine, Entity owner, string activeCollectionKey)
        {
            var collections = engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("EntityCollectionStore service is missing.");

            for (int i = 0; i < ProfileProjectionCollectionKeys.Length; i++)
            {
                string key = ProfileProjectionCollectionKeys[i];
                int expectedCount = string.Equals(key, activeCollectionKey, StringComparison.Ordinal)
                    ? EntityCommandPanelShowcaseIds.ExpectedSourceActorCount
                    : 0;
                AssertProjectionCollection(collections, owner, key, expectedCount);
            }
        }

        private static void AssertProjectionCollection(
            EntityCollectionStore collections,
            Entity owner,
            string collectionKey,
            int expectedCount)
        {
            Assert.That(collections.TryGetView(owner, collectionKey, out EntityCollectionView view), Is.True,
                $"A2 should publish the world-space projection collection '{collectionKey}'.");
            Assert.That(view.SourceKind, Is.EqualTo(EntityCollectionSourceKind.Explicit));
            Assert.That(view.Role, Is.EqualTo(EntityCollectionRoleKind.Display));
            Assert.That(view.Count, Is.EqualTo(expectedCount),
                $"A2 projection collection '{collectionKey}' should contain only the active profile's command owners.");
            Assert.That(view.Title, Is.EqualTo("A2 Aggregation Profile Projection"));

            var members = new Entity[EntityCommandPanelShowcaseIds.ExpectedSourceActorCount];
            int copied = collections.CopyEntities(owner, collectionKey, members);
            Assert.That(copied, Is.EqualTo(expectedCount));
        }

        private static void AssertWebUiRuntimeDeclared(GameEngine engine)
        {
            Assert.That(engine.MergedConfig.BrowserRuntime.Enabled, Is.True,
                "EntityCommandPanelShowcaseMod game.json should require the host BrowserRuntime path.");
            Assert.That(engine.MergedConfig.BrowserRuntime.Required, Is.True);
            Assert.That(engine.MergedConfig.BrowserRuntime.Provider, Is.EqualTo("cef"));
        }

        private static void AssertShowcaseCameraLocked(GameEngine engine)
        {
            var registry = engine.GetService(CoreServiceKeys.VirtualCameraRegistry)
                ?? throw new InvalidOperationException("VirtualCameraRegistry service is missing.");
            VirtualCameraDefinition tactical = registry.Get("Camera.Profile.Tactical");
            Assert.That(tactical.DisplayName, Is.EqualTo("Entity Command Panel Showcase"));
            Assert.That(tactical.PanMode, Is.EqualTo(CameraPanMode.None));
            Assert.That(tactical.EnableGrabDrag, Is.False);
            Assert.That(tactical.EnableZoom, Is.False);
            Assert.That(tactical.AllowUserInput, Is.False);
        }

        private static void AssertPackagedWebApp(string repoRoot)
        {
            string appRoot = Path.Combine(
                repoRoot,
                "mods",
                "showcases",
                "entity_command_panel",
                "EntityCommandPanelShowcaseMod",
                "assets",
                "entity-command-panel-app");
            Assert.That(File.Exists(Path.Combine(appRoot, "index.html")), Is.True,
                "WebApp build output must be packaged into mod assets.");
            Assert.That(Directory.GetFiles(Path.Combine(appRoot, "assets"), "*.js").Length, Is.GreaterThan(0));
            Assert.That(Directory.GetFiles(Path.Combine(appRoot, "assets"), "*.css").Length, Is.GreaterThan(0));
        }

        private static void AssertProfileProjectionPerformerConfig(string repoRoot)
        {
            string performerPath = Path.Combine(
                repoRoot,
                "mods",
                "showcases",
                "entity_command_panel",
                "EntityCommandPanelShowcaseMod",
                "assets",
                "Presentation",
                "performers.json");
            Assert.That(File.Exists(performerPath), Is.True,
                "A2 visible UAT should package performer rules for world-space profile projection markers.");

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(performerPath, Encoding.UTF8));
            string[] markerIds =
            {
                EntityCommandPanelShowcaseIds.TemplateProjectionMarkerDefId,
                EntityCommandPanelShowcaseIds.FamilyProjectionMarkerDefId,
                EntityCommandPanelShowcaseIds.AbilityProjectionMarkerDefId
            };

            foreach (string markerId in markerIds)
            {
                Assert.That(
                    document.RootElement.EnumerateArray().Any(entry =>
                        entry.TryGetProperty("id", out JsonElement id) &&
                        string.Equals(id.GetString(), markerId, StringComparison.Ordinal)),
                    Is.True,
                    $"performers.json should declare marker '{markerId}'.");
            }

            foreach (string collectionKey in ProfileProjectionCollectionKeys)
            {
                Assert.That(
                    document.RootElement.EnumerateArray().Any(entry =>
                        entry.TryGetProperty("rules", out JsonElement rules) &&
                        rules.EnumerateArray().Any(rule =>
                            rule.TryGetProperty("event", out JsonElement evt) &&
                            evt.TryGetProperty("key", out JsonElement key) &&
                            string.Equals(key.GetString(), collectionKey, StringComparison.Ordinal))),
                    Is.True,
                    $"performers.json should bind world markers to collection '{collectionKey}'.");
            }
        }

        private static void AssertOldComposePanelClosed(GameEngine engine)
        {
            var handles = engine.GetService(CoreServiceKeys.EntityCommandPanelHandleStore)
                ?? throw new InvalidOperationException("EntityCommandPanelHandleStore service is missing.");
            Assert.That(
                handles.TryGet(EntityCommandPanelShowcaseIds.AggregationAlias, out _),
                Is.False,
                "EntityCommandPanelShowcaseMod must not open the old Compose aggregation panel when WebUI owns A2.");

            if (engine.GetService(CoreServiceKeys.UiSurfaceHost) is IUiSurfaceHost surfaceHost &&
                surfaceHost.Scene != null)
            {
                Assert.That(
                    surfaceHost.Scene.FindByElementId("ui-surface-Showcase-EntityCommandPanel-Status"),
                    Is.Null,
                    "EntityCommandPanelShowcaseMod must not publish the old Compose status overlay when WebUI owns A2.");
            }
        }

        private static void AssertWebUiDataPlaneSnapshot(GameEngine engine, ProfileSnapshot activeProfile)
        {
            var dataPlane = new EntityCommandPanelShowcaseDataPlane(engine);
            var context = new WebUiTopicContext(
                EntityCommandPanelShowcaseIds.WebUiSessionId,
                EntityCommandPanelShowcaseIds.WebUiTopic,
                1,
                default);
            Assert.That(dataPlane.TryCreateSnapshot(in context, out WebUiOutboundPacket packet), Is.True);
            Assert.That(packet.Topic, Is.EqualTo(EntityCommandPanelShowcaseIds.WebUiTopic));
            string json = Encoding.UTF8.GetString(packet.Payload.Span);
            EntityCommandPanelShowcaseSnapshot snapshot = JsonSerializer.Deserialize<EntityCommandPanelShowcaseSnapshot>(
                    json,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidOperationException("Entity command panel WebUI snapshot failed to deserialize.");

            Assert.That(snapshot.Ready, Is.True, snapshot.Error);
            Assert.That(snapshot.ActiveProfile, Is.EqualTo(activeProfile.ProfileLabel));
            Assert.That(snapshot.ActiveProfileId, Is.EqualTo(activeProfile.ProfileId));
            Assert.That(snapshot.SourceActorCount, Is.EqualTo(3));
            Assert.That(snapshot.TileCount, Is.EqualTo(activeProfile.SlotCount));
            Assert.That(snapshot.Tiles.Length, Is.EqualTo(activeProfile.SlotCount));
            Assert.That(snapshot.ExpectedTileCount, Is.EqualTo(activeProfile.SlotCount));
            Assert.That(snapshot.Profiles.Select(profile => profile.Label), Is.EquivalentTo(new[] { "Template", "Family", "Ability" }));
            EntityCommandPanelShowcaseCommandTileView fireball =
                snapshot.Tiles.Single(tile => string.Equals(tile.Label, "Fireball", StringComparison.Ordinal));
            Assert.That(fireball.ContributorNames, Is.EquivalentTo(new[] { "Arcweaver", "Vanguard" }));
            Assert.That(fireball.OwnerCount, Is.EqualTo(2));
            EntityCommandPanelShowcaseCommandTileView stoneThrow =
                snapshot.Tiles.Single(tile => string.Equals(tile.Label, "Stone Throw", StringComparison.Ordinal));
            Assert.That(stoneThrow.ContributorNames, Is.EquivalentTo(new[] { "Commander" }));
            Assert.That(snapshot.Then, Does.Contain("Fireball appears once"));
            Assert.That(snapshot.VisibleResult, Does.Contain("shared abilities show contributor labels"));
        }

        private static GameEngine CreateEngine(string repoRoot)
        {
            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(
                RepoModPaths.ResolveExplicit(repoRoot, AcceptanceMods),
                Path.Combine(repoRoot, "assets"));
            InstallInput(engine);
            AcceptanceUiHostInstaller.Install(engine);
            engine.Start();
            return engine;
        }

        private static void InstallInput(GameEngine engine)
        {
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var inputHandler = new PlayerInputHandler(new NullInputBackend(), inputConfig);
            for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
            {
                inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
            }

            engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
        }

        private static void Tick(GameEngine engine, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                engine.SetService(CoreServiceKeys.UiCaptured, false);
                engine.Tick(DeltaTime);
            }
        }

        private static void WriteAcceptanceArtifacts(
            string repoRoot,
            ProfileRegistryEvidence[] installedProfiles,
            ProfileFragmentEvidence byFamilyFragment,
            CollectionEvidence collection,
            EntityCommandPanelToolbarButtonView[] initialButtons,
            params ProfileSnapshot[] snapshots)
        {
            string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", ArtifactFolderName);
            Directory.CreateDirectory(artifactDir);
            File.WriteAllText(Path.Combine(artifactDir, "aggregation-profile-report.md"), BuildReport(snapshots), Encoding.UTF8);
            File.WriteAllText(
                Path.Combine(artifactDir, "trace.jsonl"),
                BuildTraceJsonl(installedProfiles, byFamilyFragment, collection, initialButtons, snapshots),
                Encoding.UTF8);
            File.WriteAllText(
                Path.Combine(artifactDir, "battle-report.md"),
                BuildBattleReport(installedProfiles, byFamilyFragment, collection, snapshots),
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(artifactDir, "path.mmd"), BuildPathMermaid(), Encoding.UTF8);
        }

        private static string BuildReport(params ProfileSnapshot[] snapshots)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Entity Command Panel Showcase Acceptance");
            builder.AppendLine();
            builder.AppendLine("## Scenario");
            builder.AppendLine("- Showcase: `EntityCommandPanelShowcaseMod` over `interaction_showcase_hub`.");
            builder.AppendLine($"- Launcher binding: `{LauncherBindingName}` (`{ManualGuiLaunchCommand}`).");
            builder.AppendLine("- Registry: `EntityCommandPanelSourceRegistry` resolves `gas.collection-ability-slots` to `CollectionGasEntityCommandPanelSource`.");
            builder.AppendLine("- Profile registry: Core `aggregation.by_template`/`aggregation.by_ability_id` plus EntityCommandPanelMod `aggregation.by_family` fragment are installed.");
            builder.AppendLine("- Runtime path: `IEntityCommandPanelToolbarProvider.Activate` -> `CollectionGasEntityCommandPanelSource.SetAggregationProfile` -> `EntityCommandPanelSourceDispatch.CopySlots`.");
            builder.AppendLine("- WebUI path: `EntityCommandPanelShowcaseDataPlane` publishes `ludots.showcase.entity_command_panel.state`; CEF app assets are packaged under `assets/entity-command-panel-app`.");
            builder.AppendLine("- Collection owner: local player entity, `collection.command.source` containing Arcweaver, Vanguard, and Commander.");
            builder.AppendLine();
            builder.AppendLine("## Results");
            builder.AppendLine("| Profile | Profile id | Slot count | Revision | Labels |");
            builder.AppendLine("|---------|------------|------------|----------|--------|");
            for (int i = 0; i < snapshots.Length; i++)
            {
                ProfileSnapshot snapshot = snapshots[i];
                builder.Append("| ");
                builder.Append(snapshot.ProfileLabel);
                builder.Append(" | ");
                builder.Append(snapshot.ProfileId);
                builder.Append(" | ");
                builder.Append(snapshot.SlotCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
                builder.Append(" | ");
                builder.Append(snapshot.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture));
                builder.Append(" | ");
                builder.Append(string.Join(", ", snapshot.Labels));
                builder.AppendLine(" |");
            }

            builder.AppendLine();
            builder.AppendLine("## Slot Detail");
            foreach (ProfileSnapshot snapshot in snapshots)
            {
                builder.AppendLine($"### {snapshot.ProfileLabel}");
                builder.AppendLine("| Slot | Label | Detail | Ability id | Template id | Flags | Action |");
                builder.AppendLine("|------|-------|--------|------------|-------------|-------|--------|");
                for (int i = 0; i < snapshot.Slots.Length; i++)
                {
                    SlotSnapshot slot = snapshot.Slots[i];
                    builder.Append("| ");
                    builder.Append(slot.SlotIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    builder.Append(" | ");
                    builder.Append(slot.DisplayLabel);
                    builder.Append(" | ");
                    builder.Append(slot.DetailLabel);
                    builder.Append(" | ");
                    builder.Append(slot.AbilityId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    builder.Append(" | ");
                    builder.Append(slot.TemplateEntityId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    builder.Append(" | ");
                    builder.Append(slot.StateFlags);
                    builder.Append(" | ");
                    builder.Append(slot.ActionId);
                    builder.AppendLine(" |");
                }

                builder.AppendLine();
            }

            builder.AppendLine();
            builder.AppendLine("## Verdict");
            builder.AppendLine("- success: yes");
            builder.AppendLine("- evidence: the showcase exposes all three profile buttons, uses the registered collection source, and regroups the live M6 collection through installed aggregation profiles without rebuilding Core pipeline infrastructure.");
            return builder.ToString();
        }

        private static string BuildTraceJsonl(
            ProfileRegistryEvidence[] installedProfiles,
            ProfileFragmentEvidence byFamilyFragment,
            CollectionEvidence collection,
            EntityCommandPanelToolbarButtonView[] initialButtons,
            ProfileSnapshot[] snapshots)
        {
            object[] traces =
            {
                new
                {
                    at = "preflight.registry",
                    phase = "registry/source",
                    status = "pass",
                    sourceId = CollectionGasEntityCommandPanelSource.SourceId,
                    sourceType = nameof(CollectionGasEntityCommandPanelSource),
                    installedProfiles,
                    byFamilyFragment
                },
                new
                {
                    at = "showcase.host",
                    phase = "host mod collection",
                    status = "pass",
                    map = HubMapId,
                    hostMod = "EntityCommandPanelShowcaseMod",
                    collection
                },
                new
                {
                    at = "toolbar.initial",
                    phase = "runtime profile selector",
                    status = "pass",
                    activeLabel = "Family",
                    buttons = initialButtons.Select(button => new
                    {
                        button.ButtonId,
                        button.Label,
                        button.Active,
                        button.AccentColorHex
                    }).ToArray()
                }
            };

            string header = string.Join(
                Environment.NewLine,
                traces.Select(trace => JsonSerializer.Serialize(trace, TraceJsonOptions)));
            string profiles = string.Join(
                Environment.NewLine,
                snapshots.Select(snapshot => JsonSerializer.Serialize(new
                {
                    at = $"profile.{snapshot.ProfileLabel.ToLowerInvariant()}",
                    phase = "profile aggregation runtime",
                    status = "pass",
                    runtimePath = "toolbar.Activate -> CollectionGasEntityCommandPanelSource.SetAggregationProfile -> EntityCommandPanelSourceDispatch.CopySlots",
                    snapshot.ProfileLabel,
                    snapshot.ProfileId,
                    snapshot.SlotCount,
                    snapshot.Revision,
                    snapshot.Labels,
                    snapshot.Slots
                }, TraceJsonOptions)));
            return header + Environment.NewLine + profiles + Environment.NewLine;
        }

        private static string BuildBattleReport(
            ProfileRegistryEvidence[] installedProfiles,
            ProfileFragmentEvidence byFamilyFragment,
            CollectionEvidence collection,
            ProfileSnapshot[] snapshots)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Scenario: entity-command-panel-showcase");
            sb.AppendLine();
            sb.AppendLine("## Header");
            sb.AppendLine("- build: GasTests / EntityCommandPanelShowcase_SwitchesM6AggregationProfilesAtRuntime");
            sb.AppendLine("- seed: map-authored deterministic scenario");
            sb.AppendLine($"- map: {HubMapId}");
            sb.AppendLine("- clock: engine fixed step sampled through 1/60s test ticks");
            sb.AppendLine($"- execution timestamp UTC: {DateTimeOffset.UtcNow:O}");
            sb.AppendLine("- host mod: EntityCommandPanelShowcaseMod");
            sb.AppendLine($"- launcher binding: `{LauncherBindingName}`");
            sb.AppendLine($"- manual GUI command: `{ManualGuiLaunchCommand}`");
            sb.AppendLine("- panel source: gas.collection-ability-slots / CollectionGasEntityCommandPanelSource");
            sb.AppendLine("- visible panel: WebUI/CEF bottom command panel; old Compose command panel stays closed.");
            sb.AppendLine();
            sb.AppendLine("## Scenario Card");
            sb.AppendLine("- Player goal: switch the command panel between Family, Template, and Ability aggregation profiles and verify the live M6 collection regroups immediately.");
            sb.AppendLine("- Gameplay domain: RFC-0065 SHOW-4 / P3 runtime aggregation preference over the M6 command-source collection.");
            sb.AppendLine("- Initial entities: local player command owner plus Arcweaver, Vanguard, and Commander showcase command providers.");
            sb.AppendLine("- Action script: load `interaction_showcase_hub`, verify toolbar/source registries, activate Family, Template, and Ability buttons, then copy slots from the registered collection source.");
            sb.AppendLine("- Primary success condition: by-family collapses the three heroes into eight command families, by-template shows 24 unit-template slots, and by-ability shows 21 distinct ability definitions.");
            sb.AppendLine("- Failure branch condition: missing source registry entry, missing by-family profile fragment, toolbar not bound to source, stale revision, or copied slots bypassing aggregation.");
            sb.AppendLine();
            sb.AppendLine("## Runtime Evidence");
            sb.AppendLine($"- collection key: `{collection.CollectionKey}`; rows: {collection.RowCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}; title: {collection.Title}");
            sb.AppendLine($"- by-family fragment: `{byFamilyFragment.RelativePath}` declares `{byFamilyFragment.Id}` groupBy `{byFamilyFragment.GroupBy}` overflow `{byFamilyFragment.Overflow}`.");
            sb.Append("- installed profile registry ids:");
            for (int i = 0; i < installedProfiles.Length; i++)
            {
                ProfileRegistryEvidence profile = installedProfiles[i];
                sb.Append(i == 0 ? " " : ", ");
                sb.Append(profile.ProfileId);
                sb.Append("=");
                sb.Append(profile.RegistryId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("## Timeline");
            sb.AppendLine($"- t+000: verified launcher binding `{LauncherBindingName}` -> `{LauncherTargetPath}` and loaded `interaction_showcase_hub`.");
            sb.AppendLine("- t+004: showcase host published `collection.command.source` for Arcweaver, Vanguard, and Commander.");
            sb.AppendLine("- t+005: toolbar provider exposed Template, Family, and Ability profile buttons with Family active.");
            sb.AppendLine("- t+006: WebUI DataPlane snapshot reported ready=true, sourceActorCount=3, and active profile tile counts.");
            foreach (ProfileSnapshot snapshot in snapshots)
            {
                sb.AppendLine(
                    $"- t+profile: activated {snapshot.ProfileLabel} (`{snapshot.ProfileId}`), revision {snapshot.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture)}, copied {snapshot.SlotCount.ToString(System.Globalization.CultureInfo.InvariantCulture)} slots.");
            }

            sb.AppendLine();
            sb.AppendLine("## Outcome");
            sb.AppendLine("- result: success");
            sb.AppendLine("- headless evidence: registry/source lookup, by-family config fragment, host-mod command collection, toolbar activation, revision changes, and aggregation slot counts all passed.");
            sb.AppendLine("- visible evidence boundary: this artifact set is produced by the filtered GasTests acceptance run; it does not claim a captured raylib/CEF video.");
            sb.AppendLine();
            sb.AppendLine("## Artifacts");
            sb.AppendLine("- `artifacts/acceptance/entity-command-panel-showcase/aggregation-profile-report.md`");
            sb.AppendLine("- `artifacts/acceptance/entity-command-panel-showcase/battle-report.md`");
            sb.AppendLine("- `artifacts/acceptance/entity-command-panel-showcase/trace.jsonl`");
            sb.AppendLine("- `artifacts/acceptance/entity-command-panel-showcase/path.mmd`");
            return sb.ToString();
        }

        private static string BuildPathMermaid()
        {
            return string.Join(Environment.NewLine, new[]
            {
                "flowchart TD",
                "    A[\"Load EntityCommandPanelShowcaseMod on interaction_showcase_hub\"] --> B[\"EntityCommandPanelMod registers gas.collection-ability-slots source\"]",
                "    B --> C[\"ConfigPipeline installs Core template/ability profiles\"]",
                "    C --> D[\"ArrayById fragment installs aggregation.by_family catalog.castFamily\"]",
                "    D --> E[\"Showcase host publishes collection.command.source for 3 heroes\"]",
                "    E --> F[\"Aggregation toolbar binds CollectionGasEntityCommandPanelSource\"]",
                "    F --> G{\"Runtime profile button\"}",
                "    G -->|Family| H[\"SetAggregationProfile aggregation.by_family\"]",
                "    G -->|Template| I[\"SetAggregationProfile aggregation.by_template\"]",
                "    G -->|Ability| J[\"SetAggregationProfile aggregation.by_ability_id\"]",
                "    H --> K[\"EntityCommandPanelSourceDispatch.CopySlots\"]",
                "    I --> K",
                "    J --> K",
                "    K --> L[\"Assert 8 family slots, 24 template slots, or 21 ability slots\"]",
                "    L --> M[\"Write battle-report, trace.jsonl, path.mmd\"]"
            });
        }

        private static string FormatSlotFlags(EntityCommandSlotStateFlags flags)
        {
            if (flags == EntityCommandSlotStateFlags.None)
            {
                return nameof(EntityCommandSlotStateFlags.None);
            }

            var parts = new[]
            {
                flags.HasFlag(EntityCommandSlotStateFlags.Empty) ? nameof(EntityCommandSlotStateFlags.Empty) : null,
                flags.HasFlag(EntityCommandSlotStateFlags.Base) ? nameof(EntityCommandSlotStateFlags.Base) : null,
                flags.HasFlag(EntityCommandSlotStateFlags.FormOverride) ? nameof(EntityCommandSlotStateFlags.FormOverride) : null,
                flags.HasFlag(EntityCommandSlotStateFlags.GrantedOverride) ? nameof(EntityCommandSlotStateFlags.GrantedOverride) : null,
                flags.HasFlag(EntityCommandSlotStateFlags.TemplateBacked) ? nameof(EntityCommandSlotStateFlags.TemplateBacked) : null,
                flags.HasFlag(EntityCommandSlotStateFlags.Blocked) ? nameof(EntityCommandSlotStateFlags.Blocked) : null,
                flags.HasFlag(EntityCommandSlotStateFlags.Active) ? nameof(EntityCommandSlotStateFlags.Active) : null,
                flags.HasFlag(EntityCommandSlotStateFlags.PendingTarget) ? nameof(EntityCommandSlotStateFlags.PendingTarget) : null
            };
            return string.Join("|", parts.Where(part => part != null));
        }

        private static string ToRepoRelativePath(string repoRoot, string path)
        {
            string relative = Path.GetRelativePath(repoRoot, path);
            return relative.Replace(Path.DirectorySeparatorChar, '/');
        }

        private static void AssertLauncherBinding(string repoRoot)
        {
            string launcherConfigPath = Path.Combine(repoRoot, "launcher.config.json");
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(launcherConfigPath, Encoding.UTF8));
            foreach (JsonElement binding in document.RootElement.GetProperty("bindings").EnumerateArray())
            {
                if (!binding.TryGetProperty("name", out JsonElement name) ||
                    !string.Equals(name.GetString(), LauncherBindingName, StringComparison.Ordinal))
                {
                    continue;
                }

                JsonElement target = binding.GetProperty("target");
                Assert.That(target.GetProperty("type").GetString(), Is.EqualTo("path"));
                Assert.That(target.GetProperty("value").GetString(), Is.EqualTo(LauncherTargetPath));
                Assert.That(target.GetProperty("projectPath").GetString(), Is.EqualTo("EntityCommandPanelShowcaseMod.csproj"));
                return;
            }

            Assert.Fail($"launcher.config.json does not contain the {LauncherBindingName} binding.");
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir.FullName, "src", "Core", "Ludots.Core.csproj")) &&
                    Directory.Exists(Path.Combine(dir.FullName, "mods")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Failed to locate repository root from test output directory.");
        }

        private readonly record struct ProfileSnapshot(
            string ProfileLabel,
            string ProfileId,
            int SlotCount,
            uint Revision,
            string[] Labels,
            SlotSnapshot[] Slots);

        private readonly record struct SlotSnapshot(
            int SlotIndex,
            int AbilityId,
            int TemplateEntityId,
            string DisplayLabel,
            string DetailLabel,
            string ActionId,
            string StateFlags);

        private readonly record struct ProfileRegistryEvidence(
            string ProfileId,
            int RegistryId,
            string Overflow);

        private readonly record struct ProfileFragmentEvidence(
            string RelativePath,
            string Id,
            string GroupBy,
            string Overflow);

        private readonly record struct CollectionEvidence(
            string CollectionKey,
            string Title,
            string Summary,
            uint Revision,
            int RowCount,
            int CopiedMemberCount,
            int OwnerEntityId,
            int OwnerEntityVersion);

        private sealed class NullInputBackend : IInputBackend
        {
            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => false;
            public Vector2 GetMousePosition() => Vector2.Zero;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }
    }
}
