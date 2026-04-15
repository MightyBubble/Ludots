using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.IO;
using Arch.Core;
using EntityInfoPanelsMod;
using EntityInfoPanelsMod.Insight;
using Ludots.Core.Config;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Input.Selection;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using Ludots.Core.Presentation.Hud;
using NUnit.Framework;

namespace Ludots.Tests.Presentation;

[TestFixture]
public sealed class EntityInfoPanelServiceTests
{
    private const string SelectedEntityKey = "Tests.EntityInfo.Selected";
    private static readonly PresentationTextCatalog SharedEntityInfoTextCatalog = CreateEntityInfoTextCatalog();
    private static readonly PresentationTextLocaleSelection SharedEntityInfoLocaleSelection = new(SharedEntityInfoTextCatalog);

    [Test]
    public void Refresh_TracksMultipleInstances_AndBumpsUiRevisionForLayoutOnlyChanges()
    {
        using var world = World.Create();

        int healthId = AttributeRegistry.Register("Tests.EntityInfo.Health");
        int burningTagId = TagRegistry.Register("Tests.EntityInfo.Burning");

        var attributes = new AttributeBuffer();
        attributes.SetBase(healthId, 100f);
        attributes.SetCurrent(healthId, 75f);

        var tagCounts = new TagCountContainer();
        Assert.That(tagCounts.AddCount(burningTagId, 2), Is.True);

        var staticTags = new GameplayTagContainer();
        staticTags.AddTag(burningTagId);

        var effectiveTags = new GameplayTagEffectiveCache();
        effectiveTags.Set(burningTagId, true);

        Entity entity = world.Create(
            new Name { Value = "Arcweaver" },
            attributes,
            tagCounts,
            staticTags,
            effectiveTags);

        var service = CreateService();
        var globals = new Dictionary<string, object>
        {
            [SelectedEntityKey] = entity
        };

        EntityInfoPanelHandle componentHandle = service.Open(new EntityInfoPanelRequest(
            EntityInfoPanelKind.ComponentInspector,
            EntityInfoPanelSurface.Ui,
            EntityInfoPanelTarget.Fixed(entity),
            new EntityInfoPanelLayout(EntityInfoPanelAnchor.TopLeft, 16f, 20f, 420f, 320f),
            EntityInfoGasDetailFlags.None,
            true));

        EntityInfoPanelHandle gasHandle = service.Open(new EntityInfoPanelRequest(
            EntityInfoPanelKind.GasInspector,
            EntityInfoPanelSurface.Ui | EntityInfoPanelSurface.Overlay,
            EntityInfoPanelTarget.Global(SelectedEntityKey),
            new EntityInfoPanelLayout(EntityInfoPanelAnchor.BottomRight, 18f, 22f, 440f, 300f),
            EntityInfoGasDetailFlags.ShowModifierState,
            true));

        service.Refresh(world, globals);

        Assert.That(componentHandle.IsValid, Is.True);
        Assert.That(gasHandle.IsValid, Is.True);
        Assert.That(service.GetVisibleUiCount(), Is.EqualTo(2));
        Assert.That(service.UiRevision, Is.EqualTo(1));
        Assert.That(service.GetSubtitle(componentHandle.Slot), Does.Contain("Arcweaver"));

        bool sawNameSection = false;
        for (int i = 0; i < service.GetComponentSectionCount(componentHandle.Slot); i++)
        {
            if (service.GetComponentSectionName(componentHandle.Slot, i) == nameof(Name))
            {
                sawNameSection = true;
                break;
            }
        }

        Assert.That(sawNameSection, Is.True, "Component inspector should expose component sections for the target entity.");

        int revisionBeforeLayoutUpdate = service.UiRevision;
        Assert.That(
            service.UpdateLayout(
                componentHandle,
                new EntityInfoPanelLayout(EntityInfoPanelAnchor.TopRight, 32f, 28f, 512f, 360f)),
            Is.True);

        service.Refresh(world, globals);

        Assert.That(service.UiRevision, Is.EqualTo(revisionBeforeLayoutUpdate + 1));
        Assert.That(service.GetLayout(componentHandle.Slot).Width, Is.EqualTo(512f));

        Assert.That(service.SetVisible(componentHandle, false), Is.True);
        service.Refresh(world, globals);

        Assert.That(service.GetVisibleUiCount(), Is.EqualTo(1));
        Assert.That(service.Close(gasHandle), Is.True);
        service.Refresh(world, globals);
        Assert.That(service.GetVisibleUiCount(), Is.EqualTo(0));
    }

    [Test]
    public void RenderOverlay_EmitsRetainedMetadata_AndGasDetailsRespectFlags()
    {
        using var world = World.Create();

        int healthId = AttributeRegistry.Register("Tests.EntityInfo.GasHealth");
        int hasteTagId = TagRegistry.Register("Tests.EntityInfo.Haste");
        int templateId = EffectTemplateIdRegistry.Register("Tests.EntityInfo.HasteAura");

        Entity source = world.Create(new Name { Value = "Commander" });

        var attributes = new AttributeBuffer();
        attributes.SetBase(healthId, 100f);
        attributes.SetCurrent(healthId, 135f);

        var tagCounts = new TagCountContainer();
        Assert.That(tagCounts.AddCount(hasteTagId, 1), Is.True);

        var staticTags = new GameplayTagContainer();
        staticTags.AddTag(hasteTagId);

        var effectiveTags = new GameplayTagEffectiveCache();
        effectiveTags.Set(hasteTagId, true);

        Entity target = world.Create(
            new Name { Value = "Vanguard" },
            attributes,
            tagCounts,
            staticTags,
            effectiveTags,
            new ActiveEffectContainer());

        var modifiers = new EffectModifiers();
        Assert.That(modifiers.Add(healthId, ModifierOp.Add, 35f), Is.True);

        Entity effect = world.Create(
            modifiers,
            new GameplayEffect
            {
                RemainingTicks = 24,
                State = EffectState.Committed
            },
            new EffectTemplateRef { TemplateId = templateId },
            new EffectContext
            {
                Source = source,
                Target = target
            },
            new EffectStack
            {
                Count = 2,
                Limit = 4
            });

        var activeEffects = new ActiveEffectContainer();
        Assert.That(activeEffects.Add(effect), Is.True);
        world.Set(target, activeEffects);

        var service = CreateService();
        var globals = new Dictionary<string, object>();
        EntityInfoPanelHandle handle = service.Open(new EntityInfoPanelRequest(
            EntityInfoPanelKind.GasInspector,
            EntityInfoPanelSurface.Overlay,
            EntityInfoPanelTarget.Fixed(target),
            new EntityInfoPanelLayout(EntityInfoPanelAnchor.TopLeft, 24f, 18f, 360f, 220f),
            EntityInfoGasDetailFlags.ShowAttributeAggregateSources | EntityInfoGasDetailFlags.ShowModifierState,
            true));

        service.Refresh(world, globals);

        var overlay = new ScreenOverlayBuffer();
        service.RenderOverlay(overlay, new Vector2(1920f, 1080f));

        ReadOnlySpan<ScreenOverlayItem> items = overlay.GetSpan();
        Assert.That(items.Length, Is.GreaterThan(3));
        Assert.That(items[0].Kind, Is.EqualTo(ScreenOverlayItemKind.Rect));
        Assert.That(items[0].StableId, Is.GreaterThan(0));
        Assert.That(items[0].DirtySerial, Is.GreaterThan(0));

        string[] linesWithDetails = GetOverlayStrings(overlay, items);
        Assert.That(linesWithDetails, Has.Some.Contains("Tests.EntityInfo.Haste"));
        Assert.That(linesWithDetails, Has.Some.Contains("<- Tests.EntityInfo.HasteAura"));
        Assert.That(linesWithDetails, Has.Some.Contains("state=Committed"));

        Assert.That(service.UpdateGasDetailFlags(handle, EntityInfoGasDetailFlags.None), Is.True);
        service.Refresh(world, globals);

        overlay.Clear();
        service.RenderOverlay(overlay, new Vector2(1920f, 1080f));

        string[] compactLines = GetOverlayStrings(overlay, overlay.GetSpan());
        Assert.That(compactLines.Any(line => line.Contains("<-", System.StringComparison.Ordinal)), Is.False);
        Assert.That(compactLines.Any(line => line.Contains("state=Committed", System.StringComparison.Ordinal)), Is.False);
    }

    [Test]
    public void Close_ThenReopen_ResetsComponentToggleStateForReusedSlots()
    {
        using var world = World.Create();
        Entity entity = world.Create(new Name { Value = "Commander" });

        var service = CreateService();
        var globals = new Dictionary<string, object>();

        EntityInfoPanelHandle firstHandle = service.Open(new EntityInfoPanelRequest(
            EntityInfoPanelKind.ComponentInspector,
            EntityInfoPanelSurface.Ui,
            EntityInfoPanelTarget.Fixed(entity),
            new EntityInfoPanelLayout(EntityInfoPanelAnchor.TopLeft, 0f, 0f, 320f, 240f),
            EntityInfoGasDetailFlags.None,
            true));

        service.Refresh(world, globals);
        Assert.That(service.SetAllComponentsEnabled(firstHandle, false), Is.True);
        service.Refresh(world, globals);
        Assert.That(service.GetComponentSectionLineCount(firstHandle.Slot, 0), Is.EqualTo(0));

        Assert.That(service.Close(firstHandle), Is.True);

        EntityInfoPanelHandle secondHandle = service.Open(new EntityInfoPanelRequest(
            EntityInfoPanelKind.ComponentInspector,
            EntityInfoPanelSurface.Ui,
            EntityInfoPanelTarget.Fixed(entity),
            new EntityInfoPanelLayout(EntityInfoPanelAnchor.TopLeft, 0f, 0f, 320f, 240f),
            EntityInfoGasDetailFlags.None,
            true));

        service.Refresh(world, globals);

        Assert.That(secondHandle.Slot, Is.EqualTo(firstHandle.Slot));
        Assert.That(service.GetComponentSectionLineCount(secondHandle.Slot, 0), Is.GreaterThan(0));
    }

    [Test]
    public void Refresh_SelectionViewInspector_UsesViewedSelectionDescriptor_AndFormatsEntityRows()
    {
        using var world = World.Create();
        int healthId = AttributeRegistry.Register("Tests.EntityInfo.Selection.Health");
        int manaId = AttributeRegistry.Register("Tests.EntityInfo.Selection.Mana");
        var firstAttributes = new AttributeBuffer();
        firstAttributes.SetBase(healthId, 100f);
        firstAttributes.SetCurrent(healthId, 75f);
        firstAttributes.SetBase(manaId, 80f);
        firstAttributes.SetCurrent(manaId, 32f);
        Entity viewer = world.Create();
        Entity first = world.Create(new Name { Value = "Arcweaver 01" }, firstAttributes);
        Entity second = world.Create(new Name { Value = "Arcweaver 02" });
        Entity third = world.Create(new Name { Value = "Vanguard 01" });

        var selectionRegistry = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: System.StringComparer.Ordinal);
        var selection = new SelectionRuntime(world, new SelectionRuntimeConfig(), selectionRegistry);
        Assert.That(selection.ReplaceSelection(viewer, SelectionSetKeys.LivePrimary, new[] { first, second, third }), Is.True);
        Assert.That(selection.TryBindView(viewer, SelectionViewKeys.Primary, viewer, SelectionSetKeys.LivePrimary), Is.True);

        var globals = new Dictionary<string, object>
        {
            [CoreServiceKeys.SelectionRuntime.Name] = selection,
            [CoreServiceKeys.SelectionViewViewerEntity.Name] = viewer,
            [CoreServiceKeys.SelectionViewKey.Name] = SelectionViewKeys.Primary,
        };

        var service = CreateService(CreateSelectionSemanticCatalog(healthId, manaId));
        EntityInfoPanelHandle handle = service.Open(new EntityInfoPanelRequest(
            EntityInfoPanelKind.EntityCollectionInspector,
            EntityInfoPanelSurface.Ui,
            EntityInfoPanelTarget.CurrentSelectionView(),
            new EntityInfoPanelLayout(EntityInfoPanelAnchor.BottomLeft, 16f, 16f, 480f, 280f),
            EntityInfoGasDetailFlags.None,
            true));

        service.Refresh(world, globals);

        Assert.That(service.GetEntityCollectionCount(handle.Slot), Is.EqualTo(3));
        Assert.That(service.GetEntityCollectionViewKey(handle.Slot), Is.EqualTo(SelectionViewKeys.Primary));
        Assert.That(service.GetEntityCollectionAliasKey(handle.Slot), Is.EqualTo(SelectionSetKeys.LivePrimary));
        Assert.That(service.GetSubtitle(handle.Slot), Does.Contain("3 entities"));
        Assert.That(service.TryGetEntityCollectionRow(handle.Slot, 0, out EntityCollectionPanelRow firstRow), Is.True);
        Assert.That(firstRow.EntityId, Is.EqualTo(first.Id));
        Assert.That(firstRow.Name, Is.EqualTo("Arcweaver 01"));
        Assert.That(firstRow.IsPrimary, Is.True);
        Assert.That(firstRow.AttributesSummary, Does.Contain("Selection Health 75/100"));
        Assert.That(firstRow.AttributesSummary, Does.Contain("Selection Mana 32/80"));
        Assert.That(service.TryGetEntityCollectionRow(handle.Slot, 1, out EntityCollectionPanelRow secondRow), Is.True);
        Assert.That(secondRow.EntityId, Is.EqualTo(second.Id));
        Assert.That(secondRow.Name, Is.EqualTo("Arcweaver 02"));
        Assert.That(secondRow.AttributesSummary, Is.EqualTo("No semantic attributes"));
        Assert.That(service.GetEntityCollectionCategoryCount(handle.Slot), Is.EqualTo(2));
        Assert.That(service.TryGetEntityCollectionCategory(handle.Slot, 0, out EntityCollectionCategorySummary firstCategory), Is.True);
        Assert.That(firstCategory.Label, Is.EqualTo("Arcweaver"));
        Assert.That(firstCategory.Count, Is.EqualTo(2));
        Assert.That(firstCategory.ContainsPrimary, Is.True);
        Assert.That(service.TryGetEntityCollectionCategory(handle.Slot, 1, out EntityCollectionCategorySummary secondCategory), Is.True);
        Assert.That(secondCategory.Label, Is.EqualTo("Vanguard"));
        Assert.That(secondCategory.Count, Is.EqualTo(1));
    }

    [Test]
    public void Refresh_InsightBrief_UsesSemanticContractsAndPortraitImageAssets()
    {
        string root = Path.Combine(Path.GetTempPath(), "Ludots_EntityInfoInsight", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        TeamManager.Clear();

        try
        {
            WriteConfigFile("Core", "config_catalog.json",
                @"[
  { ""Path"": ""Presentation/text_tokens.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" },
  { ""Path"": ""Presentation/text_locales.json"", ""Policy"": ""DeepObject"" }
]", root);
            WriteConfigFile("Core", "Presentation/text_tokens.json",
                @"[
  { ""id"": ""entityinfo.genre.commander"", ""argCount"": 0 },
  { ""id"": ""entityinfo.subtitle.commander"", ""argCount"": 0 },
  { ""id"": ""entityinfo.body.commander"", ""argCount"": 0 },
  { ""id"": ""semantic.health.label"", ""argCount"": 0 },
  { ""id"": ""semantic.health.current"", ""argCount"": 1 },
  { ""id"": ""semantic.health.current_over_base"", ""argCount"": 2 },
  { ""id"": ""semantic.health.constant"", ""argCount"": 1 },
  { ""id"": ""semantic.unit.hp"", ""argCount"": 0 },
  { ""id"": ""semantic.relationship.label"", ""argCount"": 0 },
  { ""id"": ""semantic.relationship.friendly"", ""argCount"": 0 },
  { ""id"": ""semantic.relationship.hostile"", ""argCount"": 0 },
  { ""id"": ""semantic.relationship.neutral"", ""argCount"": 0 }
]", root);
            WriteConfigFile("Core", "Presentation/text_locales.json",
                @"{
  ""defaultLocale"": ""en-US"",
  ""locales"": {
    ""en-US"": {
      ""entityinfo.genre.commander"": ""Commander Class"",
      ""entityinfo.subtitle.commander"": ""Elite commander"",
      ""entityinfo.body.commander"": ""Leads the frontline."",
      ""semantic.health.label"": ""Health"",
      ""semantic.health.current"": ""{0}"",
      ""semantic.health.current_over_base"": ""{0}/{1}"",
      ""semantic.health.constant"": ""{0}"",
      ""semantic.unit.hp"": ""HP"",
      ""semantic.relationship.label"": ""Relationship"",
      ""semantic.relationship.friendly"": ""Friendly"",
      ""semantic.relationship.hostile"": ""Hostile"",
      ""semantic.relationship.neutral"": ""Neutral""
    }
  }
}", root);

            var (vfs, _, pipeline, catalog) = BuildPipeline(root);
            var textLoader = new PresentationTextCatalogLoader(pipeline);
            PresentationTextCatalog textCatalog = textLoader.Load(catalog);
            var localeSelection = new PresentationTextLocaleSelection(textCatalog);

            int healthId = AttributeRegistry.Register("Tests.EntityInfo.Insight.Health");
            int templateKeyId = new EntityTemplateKeyRegistry().Register("tests.hero.commander");

            var semanticCatalog = new PresentationSemanticCatalog(
                new Dictionary<string, PresentationSemanticAttributeDefinition>(StringComparer.Ordinal)
                {
                    ["unit.health"] = new PresentationSemanticAttributeDefinition
                    {
                        SemanticKey = "unit.health",
                        AttributeId = healthId,
                        AttributeKey = "Tests.EntityInfo.Insight.Health",
                        LabelTokenId = textCatalog.GetTokenId("semantic.health.label"),
                        CurrentFormatTokenId = textCatalog.GetTokenId("semantic.health.current"),
                        CurrentOverBaseFormatTokenId = textCatalog.GetTokenId("semantic.health.current_over_base"),
                        ConstantFormatTokenId = textCatalog.GetTokenId("semantic.health.constant"),
                        UnitTokenId = textCatalog.GetTokenId("semantic.unit.hp"),
                    }
                },
                new Dictionary<int, PresentationSemanticAttributeDefinition>
                {
                    [healthId] = new PresentationSemanticAttributeDefinition
                    {
                        SemanticKey = "unit.health",
                        AttributeId = healthId,
                        AttributeKey = "Tests.EntityInfo.Insight.Health",
                        LabelTokenId = textCatalog.GetTokenId("semantic.health.label"),
                        CurrentFormatTokenId = textCatalog.GetTokenId("semantic.health.current"),
                        CurrentOverBaseFormatTokenId = textCatalog.GetTokenId("semantic.health.current_over_base"),
                        ConstantFormatTokenId = textCatalog.GetTokenId("semantic.health.constant"),
                        UnitTokenId = textCatalog.GetTokenId("semantic.unit.hp"),
                    }
                },
                new Dictionary<string, PresentationSemanticValueMappingDefinition>(StringComparer.Ordinal)
                {
                    [WellKnownPresentationSemanticMappingKeys.TeamRelationship] = new PresentationSemanticValueMappingDefinition(
                        WellKnownPresentationSemanticMappingKeys.TeamRelationship,
                        textCatalog.GetTokenId("semantic.relationship.label"),
                        new Dictionary<string, int>(StringComparer.Ordinal)
                        {
                            [WellKnownPresentationSemanticMappingKeys.TeamRelationshipFriendly] = textCatalog.GetTokenId("semantic.relationship.friendly"),
                            [WellKnownPresentationSemanticMappingKeys.TeamRelationshipHostile] = textCatalog.GetTokenId("semantic.relationship.hostile"),
                            [WellKnownPresentationSemanticMappingKeys.TeamRelationshipNeutral] = textCatalog.GetTokenId("semantic.relationship.neutral"),
                        })
                });

            string portraitDir = Path.Combine(root, "Core", "assets", "Presentation", "portraits");
            Directory.CreateDirectory(portraitDir);
            string portraitPath = Path.Combine(portraitDir, "commander.svg");
            File.WriteAllText(portraitPath, "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 8 8\"><circle cx=\"4\" cy=\"4\" r=\"4\" fill=\"#AABBCC\"/></svg>");

            var imageRegistry = new PresentationImageRegistry();
            int portraitImageAssetId = imageRegistry.Register(
                "portrait.commander",
                new PresentationImageDefinition
                {
                    AssetKind = PresentationImageAssetKind.Portrait2D,
                    Locators = new[]
                    {
                        new PresentationImageLocatorDefinition("raylib", "Core:assets/Presentation/portraits/commander.svg")
                    }
                });
            var imageResolver = new PresentationImageSourceResolver(imageRegistry, vfs, "raylib");

            var profile = new EntityInsightProfile
            {
                Id = "tests.commander",
                TemplateKeyIds = new[] { templateKeyId },
                AccentColorHex = "#58B7FF",
                SurfaceColorHex = "#0F1721",
                GenreGlyph = "C",
                PortraitImageAssetId = portraitImageAssetId,
                GenreLabelTokenId = textCatalog.GetTokenId("entityinfo.genre.commander"),
                SubtitleTokenId = textCatalog.GetTokenId("entityinfo.subtitle.commander"),
                BodyTokenId = textCatalog.GetTokenId("entityinfo.body.commander"),
                Badges = Array.Empty<EntityInsightBadgeProfile>(),
                Stats = new[]
                {
                    new EntityInsightStatProfile
                    {
                        SemanticKey = "unit.health",
                        Glyph = "H",
                        AttributeId = healthId,
                        SourceKind = EntityInsightStatSourceKind.Attribute,
                        DisplayMode = EntityInsightValueDisplayMode.CurrentOverBase,
                    }
                },
                SemanticFields = new[]
                {
                    new EntityInsightSemanticFieldProfile
                    {
                        Glyph = "R",
                        MappingId = WellKnownPresentationSemanticMappingKeys.TeamRelationship,
                        SemanticValueSource = EntityInsightSemanticValueSourceKind.TeamRelationshipSelf,
                    }
                },
                Tips = Array.Empty<EntityInsightTipProfile>(),
                Actions = Array.Empty<EntityInsightActionProfile>(),
            };

            var insightCatalog = new EntityInsightProfileCatalog(
                new[] { profile },
                new Dictionary<int, int> { [templateKeyId] = 0 });

            using var world = World.Create();
            var attributes = new AttributeBuffer();
            attributes.SetBase(healthId, 100f);
            attributes.SetCurrent(healthId, 75f);

            Entity entity = world.Create(
                new Name { Value = "Arcweaver Commander" },
                new Team { Id = 7 },
                new EntityTemplateKeyCm { TemplateKeyId = templateKeyId },
                attributes);

            var service = new EntityInfoPanelService(
                insightCatalog,
                textCatalog,
                localeSelection,
                semanticCatalog,
                imageResolver);

            EntityInfoPanelHandle handle = service.Open(new EntityInfoPanelRequest(
                EntityInfoPanelKind.InsightBrief,
                EntityInfoPanelSurface.Ui,
                EntityInfoPanelTarget.Fixed(entity),
                new EntityInfoPanelLayout(EntityInfoPanelAnchor.TopLeft, 12f, 16f, 420f, 300f),
                EntityInfoGasDetailFlags.None,
                true));

            service.Refresh(world, new Dictionary<string, object>());

            Assert.That(service.GetTitle(handle.Slot), Is.EqualTo("Arcweaver Commander"));
            Assert.That(service.GetSubtitle(handle.Slot), Is.EqualTo("Elite commander"));
            Assert.That(service.GetInsightGenreLabel(handle.Slot), Is.EqualTo("Commander Class"));
            Assert.That(service.GetInsightBody(handle.Slot), Is.EqualTo("Leads the frontline."));
            Assert.That(service.GetInsightPortraitIconUri(handle.Slot), Is.EqualTo(Path.GetFullPath(portraitPath)));
            Assert.That(service.GetInsightPortraitIconUri(handle.Slot), Does.Not.StartWith("data:"));
            Assert.That(service.GetInsightStatCount(handle.Slot), Is.EqualTo(1));
            Assert.That(service.GetInsightStatLabel(handle.Slot, 0), Is.EqualTo("Health"));
            Assert.That(service.GetInsightStatLabelForProfile(profile.Stats[0]), Is.EqualTo("Health"));
            Assert.That(service.GetInsightStatValueText(handle.Slot, 0), Is.EqualTo("75/100 HP"));
            Assert.That(service.GetInsightSemanticFieldCount(handle.Slot), Is.EqualTo(1));
            Assert.That(service.GetInsightSemanticFieldLabel(handle.Slot, 0), Is.EqualTo("Relationship"));
            Assert.That(service.GetInsightSemanticFieldValueText(handle.Slot, 0), Is.EqualTo("Friendly"));
        }
        finally
        {
            TeamManager.Clear();
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Test]
    public void Refresh_InsightBrief_FailsFast_WhenPortraitAssetCannotResolveCurrentBackend()
    {
        string root = Path.Combine(Path.GetTempPath(), "Ludots_EntityInfo_PortraitContract", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", Path.Combine(root, "Core"));

            int templateKeyId = new EntityTemplateKeyRegistry().Register("tests.hero.assetfallback");
            var imageRegistry = new PresentationImageRegistry();
            int portraitImageAssetId = imageRegistry.Register(
                "portrait.assetfallback",
                new PresentationImageDefinition
                {
                    AssetKind = PresentationImageAssetKind.Portrait2D,
                    Locators = new[]
                    {
                        new PresentationImageLocatorDefinition("web", "https://example.invalid/portrait.svg"),
                    }
                });

            var profile = new EntityInsightProfile
            {
                Id = "tests.assetfallback",
                TemplateKeyIds = new[] { templateKeyId },
                AccentColorHex = "#FF0000",
                SurfaceColorHex = "#000000",
                GenreGlyph = "C",
                PortraitImageAssetId = portraitImageAssetId,
                GenreLabelTokenId = SharedEntityInfoTextCatalog.GetTokenId("entityinfo.section.actions"),
                SubtitleTokenId = SharedEntityInfoTextCatalog.GetTokenId("entityinfo.section.tips"),
                BodyTokenId = SharedEntityInfoTextCatalog.GetTokenId("entityinfo.collection.title"),
                Badges = Array.Empty<EntityInsightBadgeProfile>(),
                Stats = new[]
                {
                    new EntityInsightStatProfile
                    {
                        SemanticKey = "migration.constant",
                        Glyph = "M",
                        AttributeId = AttributeRegistry.InvalidId,
                        SourceKind = EntityInsightStatSourceKind.Constant,
                        DisplayMode = EntityInsightValueDisplayMode.Constant,
                        ConstantValue = 1f,
                    }
                },
                SemanticFields = Array.Empty<EntityInsightSemanticFieldProfile>(),
                Tips = Array.Empty<EntityInsightTipProfile>(),
                Actions = Array.Empty<EntityInsightActionProfile>(),
            };

            var semanticCatalog = CreateMigrationSemanticCatalog();
            var service = new EntityInfoPanelService(
                new EntityInsightProfileCatalog(new[] { profile }, new Dictionary<int, int> { [templateKeyId] = 0 }),
                SharedEntityInfoTextCatalog,
                SharedEntityInfoLocaleSelection,
                semanticCatalog,
                new PresentationImageSourceResolver(imageRegistry, vfs, "raylib"));

            using var world = World.Create();
            Entity entity = world.Create(
                new Name { Value = "Asset Fallback Unit" },
                new EntityTemplateKeyCm { TemplateKeyId = templateKeyId });

            EntityInfoPanelHandle handle = service.Open(new EntityInfoPanelRequest(
                EntityInfoPanelKind.InsightBrief,
                EntityInfoPanelSurface.Ui,
                EntityInfoPanelTarget.Fixed(entity),
                new EntityInfoPanelLayout(EntityInfoPanelAnchor.TopLeft, 0f, 0f, 320f, 240f),
                EntityInfoGasDetailFlags.None,
                true));

            service.Refresh(world, new Dictionary<string, object>());

            var ex = Assert.Throws<InvalidOperationException>(() => service.GetInsightPortraitIconUri(handle.Slot));
            Assert.That(ex!.Message, Does.Contain("does not define a locator for backend 'raylib'"));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Test]
    public void BuildInsightPortraitIconUri_FailsFast_WhenImageResolverIsMissing()
    {
        int templateKeyId = new EntityTemplateKeyRegistry().Register("tests.hero.migration");
        var profile = new EntityInsightProfile
        {
            Id = "tests.migration",
            TemplateKeyIds = new[] { templateKeyId },
            AccentColorHex = "#58B7FF",
            SurfaceColorHex = "#0F1721",
            GenreGlyph = "C",
            PortraitImageAssetId = 42,
            GenreLabelTokenId = SharedEntityInfoTextCatalog.GetTokenId("entityinfo.section.actions"),
            SubtitleTokenId = SharedEntityInfoTextCatalog.GetTokenId("entityinfo.section.tips"),
            BodyTokenId = SharedEntityInfoTextCatalog.GetTokenId("entityinfo.collection.title"),
            Badges = Array.Empty<EntityInsightBadgeProfile>(),
            Stats = new[]
            {
                new EntityInsightStatProfile
                {
                    SemanticKey = "migration.constant",
                    Glyph = "M",
                    AttributeId = AttributeRegistry.InvalidId,
                    SourceKind = EntityInsightStatSourceKind.Constant,
                    DisplayMode = EntityInsightValueDisplayMode.Constant,
                    ConstantValue = 1f,
                }
            },
            SemanticFields = Array.Empty<EntityInsightSemanticFieldProfile>(),
            Tips = Array.Empty<EntityInsightTipProfile>(),
            Actions = Array.Empty<EntityInsightActionProfile>(),
        };

        var semanticCatalog = CreateMigrationSemanticCatalog();

        var service = new EntityInfoPanelService(
            new EntityInsightProfileCatalog(new[] { profile }, new Dictionary<int, int> { [templateKeyId] = 0 }),
            SharedEntityInfoTextCatalog,
            SharedEntityInfoLocaleSelection,
            semanticCatalog,
            imageSourceResolver: null);
        var ex = Assert.Throws<InvalidOperationException>(() => service.BuildInsightPortraitIconUri(profile));
        Assert.That(ex!.Message, Does.Contain("requires a configured presentation image source resolver"));
    }

    private static PresentationSemanticCatalog CreateMigrationSemanticCatalog()
    {
        return new PresentationSemanticCatalog(
            new Dictionary<string, PresentationSemanticAttributeDefinition>(StringComparer.Ordinal)
            {
                ["migration.constant"] = new PresentationSemanticAttributeDefinition
                {
                    SemanticKey = "migration.constant",
                    AttributeId = AttributeRegistry.InvalidId,
                    AttributeKey = string.Empty,
                    LabelTokenId = SharedEntityInfoTextCatalog.GetTokenId("entityinfo.collection.primary"),
                    CurrentFormatTokenId = SharedEntityInfoTextCatalog.GetTokenId("entityinfo.gas.sources_on"),
                    CurrentOverBaseFormatTokenId = SharedEntityInfoTextCatalog.GetTokenId("entityinfo.collection.rows"),
                    ConstantFormatTokenId = SharedEntityInfoTextCatalog.GetTokenId("entityinfo.gas.sources_off"),
                    UnitTokenId = 0,
                }
            },
            new Dictionary<int, PresentationSemanticAttributeDefinition>(),
            new Dictionary<string, PresentationSemanticValueMappingDefinition>(StringComparer.Ordinal));
    }

    private static string[] GetOverlayStrings(ScreenOverlayBuffer overlay, ReadOnlySpan<ScreenOverlayItem> items)
    {
        var lines = new List<string>(items.Length);
        for (int i = 0; i < items.Length; i++)
        {
            ref readonly ScreenOverlayItem item = ref items[i];
            if (item.Kind != ScreenOverlayItemKind.Text)
            {
                continue;
            }

            string? text = overlay.GetString(item.StringId);
            if (!string.IsNullOrWhiteSpace(text))
            {
                lines.Add(text);
            }
        }

        return lines.ToArray();
    }

    private static (VirtualFileSystem vfs, ModLoader modLoader, ConfigPipeline pipeline, ConfigCatalog catalog) BuildPipeline(string root)
    {
        var vfs = new VirtualFileSystem();
        vfs.Mount("Core", Path.Combine(root, "Core"));
        var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
        var pipeline = new ConfigPipeline(vfs, modLoader);
        var catalog = ConfigCatalogLoader.Load(pipeline);
        return (vfs, modLoader, pipeline, catalog);
    }

    private static void WriteConfigFile(string modId, string relativePath, string content, string root)
    {
        string dir = Path.Combine(root, modId, "Configs", Path.GetDirectoryName(relativePath) ?? string.Empty);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, Path.GetFileName(relativePath)), content);
    }

    private static EntityInfoPanelService CreateService()
    {
        return new EntityInfoPanelService(
            presentationTextCatalog: SharedEntityInfoTextCatalog,
            localeSelection: SharedEntityInfoLocaleSelection);
    }

    private static EntityInfoPanelService CreateService(PresentationSemanticCatalog semanticCatalog)
    {
        return new EntityInfoPanelService(
            presentationTextCatalog: SharedEntityInfoTextCatalog,
            localeSelection: SharedEntityInfoLocaleSelection,
            semanticCatalog: semanticCatalog);
    }

    private static PresentationSemanticCatalog CreateSelectionSemanticCatalog(int healthId, int manaId)
    {
        var attributesByKey = new Dictionary<string, PresentationSemanticAttributeDefinition>(StringComparer.Ordinal);
        var attributesById = new Dictionary<int, PresentationSemanticAttributeDefinition>();
        AddSelectionSemanticAttribute(attributesByKey, attributesById, "selection.health", healthId, "entityinfo.test.selection.health");
        AddSelectionSemanticAttribute(attributesByKey, attributesById, "selection.mana", manaId, "entityinfo.test.selection.mana");
        return new PresentationSemanticCatalog(
            attributesByKey,
            attributesById,
            new Dictionary<string, PresentationSemanticValueMappingDefinition>(StringComparer.Ordinal));
    }

    private static void AddSelectionSemanticAttribute(
        Dictionary<string, PresentationSemanticAttributeDefinition> attributesByKey,
        Dictionary<int, PresentationSemanticAttributeDefinition> attributesById,
        string semanticKey,
        int attributeId,
        string labelTokenKey)
    {
        var definition = new PresentationSemanticAttributeDefinition
        {
            SemanticKey = semanticKey,
            AttributeId = attributeId,
            AttributeKey = AttributeRegistry.GetName(attributeId),
            LabelTokenId = SharedEntityInfoTextCatalog.GetTokenId(labelTokenKey),
            CurrentFormatTokenId = SharedEntityInfoTextCatalog.GetTokenId("entityinfo.test.attribute.current"),
            CurrentOverBaseFormatTokenId = SharedEntityInfoTextCatalog.GetTokenId("entityinfo.test.attribute.current_over_base"),
            ConstantFormatTokenId = SharedEntityInfoTextCatalog.GetTokenId("entityinfo.test.attribute.constant"),
            UnitTokenId = 0,
        };
        attributesByKey.Add(semanticKey, definition);
        attributesById.Add(attributeId, definition);
    }

    private static PresentationTextCatalog CreateEntityInfoTextCatalog()
    {
        string root = Path.Combine(Path.GetTempPath(), "Ludots_EntityInfoPanelService_Text", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            WriteConfigFile("Core", "config_catalog.json",
                @"[
  { ""Path"": ""Presentation/text_tokens.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" },
  { ""Path"": ""Presentation/text_locales.json"", ""Policy"": ""DeepObject"" }
]", root);
            WriteConfigFile("Core", "Presentation/text_tokens.json",
                @"[
  { ""id"": ""entityinfo.action.close"", ""argCount"": 0 },
  { ""id"": ""entityinfo.component.expand_all"", ""argCount"": 0 },
  { ""id"": ""entityinfo.component.collapse_all"", ""argCount"": 0 },
  { ""id"": ""entityinfo.component.show_prefix"", ""argCount"": 0 },
  { ""id"": ""entityinfo.component.hide_prefix"", ""argCount"": 0 },
  { ""id"": ""entityinfo.gas.sources_on"", ""argCount"": 0 },
  { ""id"": ""entityinfo.gas.sources_off"", ""argCount"": 0 },
  { ""id"": ""entityinfo.gas.modifiers_on"", ""argCount"": 0 },
  { ""id"": ""entityinfo.gas.modifiers_off"", ""argCount"": 0 },
  { ""id"": ""entityinfo.collection.title"", ""argCount"": 0 },
  { ""id"": ""entityinfo.collection.empty_title"", ""argCount"": 0 },
  { ""id"": ""entityinfo.collection.waiting_body"", ""argCount"": 0 },
  { ""id"": ""entityinfo.collection.no_categories"", ""argCount"": 0 },
  { ""id"": ""entityinfo.collection.primary"", ""argCount"": 0 },
  { ""id"": ""entityinfo.collection.entities"", ""argCount"": 0 },
  { ""id"": ""entityinfo.collection.categories"", ""argCount"": 0 },
  { ""id"": ""entityinfo.collection.rows"", ""argCount"": 2 },
  { ""id"": ""entityinfo.collection.no_attributes"", ""argCount"": 0 },
  { ""id"": ""entityinfo.collection.more_attributes"", ""argCount"": 1 },
  { ""id"": ""entityinfo.panel.component.title"", ""argCount"": 0 },
  { ""id"": ""entityinfo.panel.gas.title"", ""argCount"": 0 },
  { ""id"": ""entityinfo.panel.collection.title_text"", ""argCount"": 0 },
  { ""id"": ""entityinfo.target.fixed_unavailable"", ""argCount"": 0 },
  { ""id"": ""entityinfo.target.global_waiting"", ""argCount"": 0 },
  { ""id"": ""entityinfo.target.unavailable"", ""argCount"": 0 },
  { ""id"": ""entityinfo.section.actions"", ""argCount"": 0 },
  { ""id"": ""entityinfo.section.tips"", ""argCount"": 0 },
  { ""id"": ""entityinfo.actionstate.ready"", ""argCount"": 0 },
  { ""id"": ""entityinfo.actionstate.blocked"", ""argCount"": 0 },
  { ""id"": ""entityinfo.actionstate.active"", ""argCount"": 0 },
  { ""id"": ""entityinfo.actionstate.unavailable"", ""argCount"": 0 },
  { ""id"": ""entityinfo.test.selection.health"", ""argCount"": 0 },
  { ""id"": ""entityinfo.test.selection.mana"", ""argCount"": 0 },
  { ""id"": ""entityinfo.test.attribute.current"", ""argCount"": 1 },
  { ""id"": ""entityinfo.test.attribute.current_over_base"", ""argCount"": 2 },
  { ""id"": ""entityinfo.test.attribute.constant"", ""argCount"": 1 }
]", root);
            WriteConfigFile("Core", "Presentation/text_locales.json",
                @"{
  ""defaultLocale"": ""en-US"",
  ""locales"": {
    ""en-US"": {
      ""entityinfo.action.close"": ""Close"",
      ""entityinfo.component.expand_all"": ""Expand All"",
      ""entityinfo.component.collapse_all"": ""Collapse All"",
      ""entityinfo.component.show_prefix"": ""Show"",
      ""entityinfo.component.hide_prefix"": ""Hide"",
      ""entityinfo.gas.sources_on"": ""Sources ON"",
      ""entityinfo.gas.sources_off"": ""Sources OFF"",
      ""entityinfo.gas.modifiers_on"": ""Modifiers ON"",
      ""entityinfo.gas.modifiers_off"": ""Modifiers OFF"",
      ""entityinfo.collection.title"": ""Current viewed selection"",
      ""entityinfo.collection.empty_title"": ""Current viewed selection"",
      ""entityinfo.collection.waiting_body"": ""Waiting for active selection view."",
      ""entityinfo.collection.no_categories"": ""No category buckets yet."",
      ""entityinfo.collection.primary"": ""PRIMARY"",
      ""entityinfo.collection.entities"": ""entities"",
      ""entityinfo.collection.categories"": ""categories"",
      ""entityinfo.collection.rows"": ""rows {0}-{1}"",
      ""entityinfo.collection.no_attributes"": ""No semantic attributes"",
      ""entityinfo.collection.more_attributes"": ""+{0} semantic attributes"",
      ""entityinfo.panel.component.title"": ""Entity Component Inspector"",
      ""entityinfo.panel.gas.title"": ""Entity GAS Inspector"",
      ""entityinfo.panel.collection.title_text"": ""Entity Collection Inspector"",
      ""entityinfo.target.fixed_unavailable"": ""Fixed target unavailable."",
      ""entityinfo.target.global_waiting"": ""Waiting for configured target key."",
      ""entityinfo.target.unavailable"": ""Target unavailable."",
      ""entityinfo.section.actions"": ""Action lens"",
      ""entityinfo.section.tips"": ""Designer tips"",
      ""entityinfo.actionstate.ready"": ""Ready"",
      ""entityinfo.actionstate.blocked"": ""Blocked"",
      ""entityinfo.actionstate.active"": ""Active"",
      ""entityinfo.actionstate.unavailable"": ""Unavailable"",
      ""entityinfo.test.selection.health"": ""Selection Health"",
      ""entityinfo.test.selection.mana"": ""Selection Mana"",
      ""entityinfo.test.attribute.current"": ""{0}"",
      ""entityinfo.test.attribute.current_over_base"": ""{0}/{1}"",
      ""entityinfo.test.attribute.constant"": ""{0}""
    }
  }
}", root);

            var (_, _, pipeline, catalog) = BuildPipeline(root);
            return new PresentationTextCatalogLoader(pipeline).Load(catalog);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
