using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Arch.Core;
using EntityInfoPanelsMod;
using EntityInfoPanelsMod.Insight;
using Ludots.Core.Components;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Presentation;

[TestFixture]
public sealed class EntityInfoPanelServiceTests
{
    private const string SelectedEntityKey = "Tests.EntityInfo.Selected";

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

        var service = new EntityInfoPanelService();
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

        var service = new EntityInfoPanelService();
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

        var service = new EntityInfoPanelService();
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

        var collectionRegistry = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: System.StringComparer.Ordinal);
        var collections = new EntityCollectionStore(collectionRegistry);
        ReplaceCommandSource(collections, viewer, new[] { first, second, third }, "Command source | 3 entities");

        var globals = new Dictionary<string, object>
        {
            [CoreServiceKeys.LocalPlayerEntity.Name] = viewer,
            [CoreServiceKeys.EntityCollectionStore.Name] = collections,
            [CoreServiceKeys.EntityCollectionKeyRegistry.Name] = collectionRegistry,
        };

        var service = new EntityInfoPanelService();
        EntityInfoPanelHandle handle = service.Open(new EntityInfoPanelRequest(
            EntityInfoPanelKind.EntityCollectionInspector,
            EntityInfoPanelSurface.Ui,
            EntityInfoPanelTarget.CurrentSelectionView(),
            new EntityInfoPanelLayout(EntityInfoPanelAnchor.BottomLeft, 16f, 16f, 480f, 280f),
            EntityInfoGasDetailFlags.None,
            true));

        service.Refresh(world, globals);

        Assert.That(service.GetEntityCollectionCount(handle.Slot), Is.EqualTo(3));
        Assert.That(service.GetEntityCollectionViewKey(handle.Slot), Is.EqualTo(EntityCollectionKeys.CommandSource));
        Assert.That(service.GetEntityCollectionSetKey(handle.Slot), Is.EqualTo(EntityCollectionKeys.CommandSource));
        Assert.That(service.GetSubtitle(handle.Slot), Does.Contain("3 entities"));
        Assert.That(service.TryGetEntityCollectionRow(handle.Slot, 0, out EntityCollectionPanelRow firstRow), Is.True);
        Assert.That(firstRow.EntityId, Is.EqualTo(first.Id));
        Assert.That(firstRow.Name, Is.EqualTo("Arcweaver 01"));
        Assert.That(firstRow.IsPrimary, Is.True);
        Assert.That(firstRow.AttributesSummary, Does.Contain("Selection.Health 75/100"));
        Assert.That(firstRow.AttributesSummary, Does.Contain("Selection.Mana 32/80"));
        Assert.That(service.TryGetEntityCollectionRow(handle.Slot, 1, out EntityCollectionPanelRow secondRow), Is.True);
        Assert.That(secondRow.EntityId, Is.EqualTo(second.Id));
        Assert.That(secondRow.Name, Is.EqualTo("Arcweaver 02"));
        Assert.That(secondRow.AttributesSummary, Is.EqualTo("(no attributes)"));
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
    public void Refresh_ExplicitEntityCollectionInspector_RendersWithoutMutatingViewedSelection()
    {
        using var world = World.Create();
        Entity viewer = world.Create();
        Entity selected = world.Create(new Name { Value = "Selected Captain" });
        Entity queryFirst = world.Create(new Name { Value = "Query Vanguard" });
        Entity querySecond = world.Create(new Name { Value = "Query Scout" });

        var collectionRegistry = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: System.StringComparer.Ordinal);
        var collections = new EntityCollectionStore(collectionRegistry);
        ReplaceCommandSource(collections, viewer, new[] { selected }, "Command source | 1 entity");
        const string queryKey = "tests.entityinfo.query";
        collections.Replace(
            viewer,
            EntityCollectionDescriptor.Create(
                queryKey,
                EntityCollectionSourceKind.RelationDerived,
                EntityCollectionRoleKind.Display,
                viewer,
                queryFirst,
                "Relation query",
                "Assigned units | 2 entities"),
            new[] { queryFirst, querySecond });

        var globals = new Dictionary<string, object>
        {
            [CoreServiceKeys.LocalPlayerEntity.Name] = viewer,
            [CoreServiceKeys.EntityCollectionStore.Name] = collections,
            [CoreServiceKeys.EntityCollectionKeyRegistry.Name] = collectionRegistry,
        };

        var service = new EntityInfoPanelService();
        EntityInfoPanelHandle handle = service.Open(new EntityInfoPanelRequest(
            EntityInfoPanelKind.EntityCollectionInspector,
            EntityInfoPanelSurface.Ui,
            EntityInfoPanelTarget.EntityCollection(viewer, queryKey),
            new EntityInfoPanelLayout(EntityInfoPanelAnchor.BottomLeft, 16f, 16f, 480f, 280f),
            EntityInfoGasDetailFlags.None,
            true));

        service.Refresh(world, globals);

        Assert.That(collections.TryGet(viewer, EntityCollectionKeys.CommandSource, out EntityCollectionHandle commandSource), Is.True);
        Assert.That(collections.TryGetEntityAt(commandSource, 0, out Entity stillSelected), Is.True);
        Assert.That(stillSelected, Is.EqualTo(selected));
        Assert.That(service.GetEntityCollectionCount(handle.Slot), Is.EqualTo(2));
        Assert.That(service.GetEntityCollectionSourceTitle(handle.Slot), Is.EqualTo("Relation query"));
        Assert.That(service.GetEntityCollectionSourceSummary(handle.Slot), Is.EqualTo("Assigned units | 2 entities"));
        Assert.That(service.GetEntityCollectionSetKey(handle.Slot), Is.EqualTo(queryKey));
        Assert.That(service.TryGetEntityCollectionRow(handle.Slot, 0, out EntityCollectionPanelRow firstRow), Is.True);
        Assert.That(firstRow.EntityId, Is.EqualTo(queryFirst.Id));
        Assert.That(firstRow.Name, Is.EqualTo("Query Vanguard"));
        Assert.That(firstRow.IsPrimary, Is.True);
        Assert.That(service.TryGetEntityCollectionRow(handle.Slot, 1, out EntityCollectionPanelRow secondRow), Is.True);
        Assert.That(secondRow.EntityId, Is.EqualTo(querySecond.Id));
        Assert.That(secondRow.Name, Is.EqualTo("Query Scout"));
        Assert.That(secondRow.IsPrimary, Is.False);
    }

    private static EntityCollectionHandle ReplaceCommandSource(
        EntityCollectionStore collections,
        Entity viewer,
        ReadOnlySpan<Entity> members,
        string summary)
    {
        var descriptor = EntityCollectionDescriptor.Create(
            EntityCollectionKeys.CommandSource,
            EntityCollectionSourceKind.Explicit,
            EntityCollectionRoleKind.CommandSource,
            viewer,
            members.Length > 0 ? members[0] : Entity.Null,
            "Command source",
            summary);
        return collections.Replace(viewer, descriptor, members, viewer);
    }

    [Test]
    public void Refresh_TemplateDrivenInsightAndCollectionRows_ReuseProfileAndTextPath()
    {
        using var world = World.Create();
        int templateKeyId = 501;
        int healthId = AttributeRegistry.Register("Tests.EntityInfo.Template.Health");
        int abilityId = AbilityIdRegistry.Register("Tests.EntityInfo.Template.Ability");

        var attributes = new AttributeBuffer();
        attributes.SetBase(healthId, 90f);
        attributes.SetCurrent(healthId, 45f);

        var abilities = new AbilityStateBuffer();
        abilities.AddAbility(abilityId);

        Entity owner = world.Create();
        Entity entity = world.Create(
            new Name { Value = "Templated Vanguard" },
            new EntityTemplateKeyRef { TemplateKeyId = templateKeyId },
            attributes,
            abilities);

        var collectionRegistry = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: System.StringComparer.Ordinal);
        var collections = new EntityCollectionStore(collectionRegistry);
        const string queryKey = "tests.entityinfo.template.collection";
        collections.Replace(
            owner,
            EntityCollectionDescriptor.Create(
                queryKey,
                EntityCollectionSourceKind.Explicit,
                EntityCollectionRoleKind.Display,
                owner,
                entity,
                "Templated collection",
                "1 templated entity"),
            new[] { entity });

        PresentationTextCatalog textCatalog = CreateTemplateTextCatalog();
        var localeSelection = new PresentationTextLocaleSelection(textCatalog);
        var profileCatalog = CreateTemplateProfileCatalog(templateKeyId, healthId, abilityId, textCatalog);
        var templates = new EntityInfoPanelTemplateCatalog();
        templates.Register(new EntityInfoPanelTemplateDescriptor
        {
            Id = "tests.entityinfo.template.compact",
            Sections = EntityInfoPanelTemplateSectionFlags.Title |
                       EntityInfoPanelTemplateSectionFlags.Subtitle |
                       EntityInfoPanelTemplateSectionFlags.Body |
                       EntityInfoPanelTemplateSectionFlags.Stats |
                       EntityInfoPanelTemplateSectionFlags.Actions,
            RequireInsightProfile = true
        });

        var service = new EntityInfoPanelService(profileCatalog, textCatalog, localeSelection, templates: templates);
        EntityInfoPanelHandle standalone = service.Open(new EntityInfoPanelRequest(
            EntityInfoPanelKind.InsightBrief,
            EntityInfoPanelSurface.Ui,
            EntityInfoPanelTarget.Fixed(entity),
            new EntityInfoPanelLayout(EntityInfoPanelAnchor.TopLeft, 0f, 0f, 360f, 240f),
            EntityInfoGasDetailFlags.None,
            true,
            "tests.entityinfo.template.compact"));
        EntityInfoPanelHandle collection = service.Open(new EntityInfoPanelRequest(
            EntityInfoPanelKind.EntityCollectionInspector,
            EntityInfoPanelSurface.Ui,
            EntityInfoPanelTarget.EntityCollection(owner, queryKey),
            new EntityInfoPanelLayout(EntityInfoPanelAnchor.TopLeft, 0f, 0f, 360f, 240f),
            EntityInfoGasDetailFlags.None,
            true,
            "tests.entityinfo.template.compact"));

        service.Refresh(
            world,
            new Dictionary<string, object>
            {
                [CoreServiceKeys.EntityCollectionStore.Name] = collections,
                [CoreServiceKeys.EntityCollectionKeyRegistry.Name] = collectionRegistry
            });

        Assert.That(service.GetTemplateId(standalone.Slot), Is.EqualTo("tests.entityinfo.template.compact"));
        Assert.That(service.GetTitle(standalone.Slot), Is.EqualTo("Templated Vanguard"));
        Assert.That(service.GetSubtitle(standalone.Slot), Is.EqualTo("Profile subtitle"));
        Assert.That(service.GetInsightStatCount(standalone.Slot), Is.EqualTo(1));
        Assert.That(service.GetInsightActionCount(standalone.Slot), Is.EqualTo(1));
        Assert.That(service.TryGetEntityCollectionRow(collection.Slot, 0, out EntityCollectionPanelRow row), Is.True);
        Assert.That(row.TemplateId, Is.EqualTo("tests.entityinfo.template.compact"));
        Assert.That(row.TemplateSubtitle, Is.EqualTo("Profile subtitle"));
        Assert.That(row.TemplateBody, Is.EqualTo("Profile body"));
        Assert.That(row.AccentColorHex, Is.EqualTo("#55AAEE"));
    }

    [Test]
    public void Open_MissingTemplate_FailsExplicitly()
    {
        var service = new EntityInfoPanelService(templates: new EntityInfoPanelTemplateCatalog());

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => service.Open(new EntityInfoPanelRequest(
            EntityInfoPanelKind.InsightBrief,
            EntityInfoPanelSurface.Ui,
            EntityInfoPanelTarget.Fixed(Entity.Null),
            new EntityInfoPanelLayout(EntityInfoPanelAnchor.TopLeft, 0f, 0f, 240f, 160f),
            EntityInfoGasDetailFlags.None,
            true,
            "tests.entityinfo.template.missing")))!;

        Assert.That(ex.Message, Does.Contain("tests.entityinfo.template.missing"));
    }

    [Test]
    public void Refresh_TemplateRequiresProfile_FailsWhenEntityHasNoInsightProfile()
    {
        using var world = World.Create();
        Entity entity = world.Create(new Name { Value = "Unprofiled" });
        var templates = new EntityInfoPanelTemplateCatalog();
        templates.Register(new EntityInfoPanelTemplateDescriptor
        {
            Id = "tests.entityinfo.template.requires-profile",
            Sections = EntityInfoPanelTemplateSectionFlags.Title | EntityInfoPanelTemplateSectionFlags.Subtitle,
            RequireInsightProfile = true
        });

        var service = new EntityInfoPanelService(templates: templates);
        service.Open(new EntityInfoPanelRequest(
            EntityInfoPanelKind.InsightBrief,
            EntityInfoPanelSurface.Ui,
            EntityInfoPanelTarget.Fixed(entity),
            new EntityInfoPanelLayout(EntityInfoPanelAnchor.TopLeft, 0f, 0f, 240f, 160f),
            EntityInfoGasDetailFlags.None,
            true,
            "tests.entityinfo.template.requires-profile"));

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => service.Refresh(world, new Dictionary<string, object>()))!;
        Assert.That(ex.Message, Does.Contain("requires an insight profile"));
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

    private static PresentationTextCatalog CreateTemplateTextCatalog()
    {
        var tokenIds = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: System.StringComparer.Ordinal);
        int subtitleTokenId = tokenIds.Register("tests.entityinfo.subtitle");
        int bodyTokenId = tokenIds.Register("tests.entityinfo.body");
        int genreTokenId = tokenIds.Register("tests.entityinfo.genre");
        int statTokenId = tokenIds.Register("tests.entityinfo.stat.health");
        int actionTitleTokenId = tokenIds.Register("tests.entityinfo.action.title");
        int actionBodyTokenId = tokenIds.Register("tests.entityinfo.action.body");

        var tokens = new PresentationTextTokenDefinition[tokenIds.Count + 1];
        foreach (string key in new[]
                 {
                     "tests.entityinfo.subtitle",
                     "tests.entityinfo.body",
                     "tests.entityinfo.genre",
                     "tests.entityinfo.stat.health",
                     "tests.entityinfo.action.title",
                     "tests.entityinfo.action.body"
                 })
        {
            int id = tokenIds.GetId(key);
            tokens[id] = new PresentationTextTokenDefinition { TokenId = id, Key = key, ArgCount = 0 };
        }

        var localeIds = new StringIntRegistry(capacity: 4, startId: 1, invalidId: 0, comparer: System.StringComparer.Ordinal);
        int localeId = localeIds.Register("en-US");
        var templates = new PresentationTextTemplate[tokenIds.Count + 1];
        templates[subtitleTokenId] = Literal("Profile subtitle");
        templates[bodyTokenId] = Literal("Profile body");
        templates[genreTokenId] = Literal("Profile genre");
        templates[statTokenId] = Literal("Health");
        templates[actionTitleTokenId] = Literal("Template action");
        templates[actionBodyTokenId] = Literal("Template action body");

        var locales = new PresentationTextLocaleTable[localeIds.Count + 1];
        locales[localeId] = new PresentationTextLocaleTable(localeId, "en-US", templates);
        return new PresentationTextCatalog(tokenIds, tokens, localeIds, locales, defaultLocaleId: localeId);
    }

    private static EntityInsightProfileCatalog CreateTemplateProfileCatalog(
        int templateKeyId,
        int healthId,
        int abilityId,
        PresentationTextCatalog textCatalog)
    {
        var profile = new EntityInsightProfile
        {
            Id = "tests.entityinfo.profile",
            TemplateKeyIds = new[] { templateKeyId },
            AccentColorHex = "#55AAEE",
            SurfaceColorHex = "#101820",
            GenreGlyph = "G",
            PortraitGlyph = "P",
            GenreLabelTokenId = textCatalog.GetTokenId("tests.entityinfo.genre"),
            SubtitleTokenId = textCatalog.GetTokenId("tests.entityinfo.subtitle"),
            BodyTokenId = textCatalog.GetTokenId("tests.entityinfo.body"),
            Badges = Array.Empty<EntityInsightBadgeProfile>(),
            Stats = new[]
            {
                new EntityInsightStatProfile
                {
                    Glyph = "H",
                    LabelTokenId = textCatalog.GetTokenId("tests.entityinfo.stat.health"),
                    SourceKind = EntityInsightStatSourceKind.Attribute,
                    DisplayMode = EntityInsightValueDisplayMode.CurrentOverBase,
                    AttributeId = healthId
                }
            },
            Tips = Array.Empty<EntityInsightTipProfile>(),
            Actions = new[]
            {
                new EntityInsightActionProfile
                {
                    AbilityId = abilityId,
                    Glyph = "A",
                    TitleTokenId = textCatalog.GetTokenId("tests.entityinfo.action.title"),
                    BodyTokenId = textCatalog.GetTokenId("tests.entityinfo.action.body")
                }
            }
        };

        return new EntityInsightProfileCatalog(
            new[] { profile },
            new Dictionary<int, int> { [templateKeyId] = 0 });
    }

    private static PresentationTextTemplate Literal(string text)
    {
        return new PresentationTextTemplate(
            text,
            new[] { new PresentationTextTemplatePart(PresentationTextTemplatePartKind.Literal, text, -1) });
    }
}
