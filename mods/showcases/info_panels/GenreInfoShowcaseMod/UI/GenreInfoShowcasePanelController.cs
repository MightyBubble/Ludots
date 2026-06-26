using System;
using System.Collections.Generic;
using Arch.Core;
using EntityInfoPanelsMod;
using EntityInfoPanelsMod.Insight;
using EntityInfoPanelsMod.UI;
using GenreInfoShowcaseMod.Runtime;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Input.Selection;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Actions;
using Ludots.UI.Surface;

namespace GenreInfoShowcaseMod.UI
{
    internal sealed class GenreInfoShowcasePanelController
    {
        private const float DockWidth = 560f;
        private const float UnitCardWidth = 166f;
        private const float UnitCardHeight = 104f;
        private const float UnitCardGap = 8f;
        private const float UnitCardRowHeight = UnitCardHeight + UnitCardGap;

        private readonly GenreInfoShowcaseRuntime _runtime;
        private ReactivePage<GenreInfoShowcasePanelState>? _page;
        private GameEngine? _engine;
        private UiSurfaceLeaseHandle _lease;

        public GenreInfoShowcasePanelController(GenreInfoShowcaseRuntime runtime)
        {
            _runtime = runtime;
        }

        public void MountOrRefresh(UIRoot root, GameEngine engine, string mapId)
        {
            if (engine.GetService(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost surfaceHost)
            {
                return;
            }

            _engine = engine;

            GenreInfoShowcasePanelState nextState = BuildState(engine, mapId);
            if (_page == null)
            {
                var textMeasurer = (IUiTextMeasurer)engine.GetService(CoreServiceKeys.UiTextMeasurer);
                var imageSizeProvider = (IUiImageSizeProvider)engine.GetService(CoreServiceKeys.UiImageSizeProvider);
                _page = new ReactivePage<GenreInfoShowcasePanelState>(textMeasurer, imageSizeProvider, nextState, BuildRoot);
            }
            else if (!_page.State.Equals(nextState))
            {
                _page.SetState(_ => nextState);
            }

            surfaceHost.PublishReactivePage(
                ref _lease,
                new UiSurfaceLeaseRequest("Showcase.GenreInfo.Panel", UiSurfaceSegment.Overlay, priority: 40),
                _page);
        }

        public void ClearIfOwned(UIRoot root)
        {
            if (_lease.IsValid &&
                _engine?.GetService(CoreServiceKeys.UiSurfaceHost) is IUiSurfaceHost surfaceHost)
            {
                surfaceHost.ReleaseLease(ref _lease);
            }

            _engine = null;
        }

        private UiElementBuilder BuildRoot(ReactiveContext<GenreInfoShowcasePanelState> context)
        {
            if (_engine == null)
            {
                return Ui.Column();
            }

            EntityInfoPanelService? infoService = _engine.GetService(EntityInfoPanelServiceKeys.Service);
            PresentationTextCatalog textCatalog = _engine.GetService(CoreServiceKeys.PresentationTextCatalog) ?? PresentationTextCatalog.Empty;
            PresentationTextLocaleSelection localeSelection = _engine.GetService(CoreServiceKeys.PresentationTextLocaleSelection)
                ?? new PresentationTextLocaleSelection(textCatalog);
            var textResolver = new EntityInsightTextResolver(textCatalog, localeSelection);
            SelectionDockSnapshot dock = ResolveSelectionDock(_engine, infoService, textResolver);

            return Ui.Column(
                    BuildMainDock(context, dock, localeSelection.ActiveLocaleKey, textResolver),
                    infoService == null ? Ui.Column() : EntityInfoPanelUiComposer.BuildLayer(infoService, context))
                .WidthPercent(100f)
                .HeightPercent(100f)
                .Absolute(0f, 0f)
                .ZIndex(36);
        }

        private UiElementBuilder BuildMainDock(
            ReactiveContext<GenreInfoShowcasePanelState> context,
            SelectionDockSnapshot dock,
            string activeLocaleKey,
            EntityInsightTextResolver textResolver)
        {
            return Ui.Column(
                    BuildHeaderCard(activeLocaleKey, textResolver),
                    BuildControlCard(dock, textResolver),
                    BuildSelectionDeck(context, dock, textResolver))
                .Width(DockWidth)
                .Gap(10f)
                .Padding(16f)
                .Radius(28f)
                .Background("#071019")
                .Absolute(16f, 16f)
                .ZIndex(36);
        }

        private UiElementBuilder BuildHeaderCard(string activeLocaleKey, EntityInsightTextResolver textResolver)
        {
            return Ui.Card(
                    Ui.Text(textResolver.ResolveRequiredTokenKey("genreinfo.header.title"))
                        .FontSize(26f)
                        .Bold()
                        .Color("#F7FBFF"),
                    Ui.Text(textResolver.ResolveRequiredTokenKey("genreinfo.header.body"))
                        .FontSize(12f)
                        .Color("#B7C9DA")
                        .WhiteSpace(UiWhiteSpace.Normal),
                    Ui.Row(
                            BuildLocaleButton("EN", "en-US", activeLocaleKey, GenreInfoShowcaseIds.LocaleEnglishButtonId),
                            BuildLocaleButton("中文", "zh-CN", activeLocaleKey, GenreInfoShowcaseIds.LocaleChineseButtonId))
                        .Gap(8f))
                .Gap(10f)
                .Padding(16f)
                .Radius(18f)
                .Background("#0D1824");
        }

        private UiElementBuilder BuildControlCard(SelectionDockSnapshot dock, EntityInsightTextResolver textResolver)
        {
            return Ui.Card(
                    Ui.Text(textResolver.ResolveRequiredTokenKey("genreinfo.controls.title"))
                        .FontSize(12f)
                        .Bold()
                        .Color("#F0C36B"),
                    Ui.Row(
                            BuildViewButton(textResolver.ResolveRequiredTokenKey("genreinfo.view.live"), SelectionViewMode.Live, dock.ViewMode, GenreInfoShowcaseIds.LiveViewButtonId),
                            BuildViewButton(textResolver.ResolveRequiredTokenKey("genreinfo.view.formation"), SelectionViewMode.Formation, dock.ViewMode, GenreInfoShowcaseIds.FormationViewButtonId))
                        .Gap(8f)
                        .Wrap(),
                    Ui.Text($"{textResolver.ResolveRequiredTokenKey("genreinfo.controls.current")}: {dock.ViewLabel}")
                        .FontSize(11.5f)
                        .Color("#D6E2EE")
                        .WhiteSpace(UiWhiteSpace.Normal),
                    Ui.Row(
                            BuildRecallGroupButton(1, dock.Group1, dock.ActiveControlGroup, textResolver),
                            BuildRecallGroupButton(2, dock.Group2, dock.ActiveControlGroup, textResolver),
                            BuildRecallGroupButton(3, dock.Group3, dock.ActiveControlGroup, textResolver),
                            BuildRecallGroupButton(4, dock.Group4, dock.ActiveControlGroup, textResolver))
                        .Gap(8f)
                        .Wrap(),
                    Ui.Row(
                            BuildSaveGroupButton(1, textResolver),
                            BuildSaveGroupButton(2, textResolver),
                            BuildSaveGroupButton(3, textResolver),
                            BuildSaveGroupButton(4, textResolver))
                        .Gap(8f)
                        .Wrap(),
                    Ui.Text(textResolver.ResolveRequiredTokenKey("genreinfo.controls.hint"))
                        .FontSize(11f)
                        .Color("#8EA2B6")
                        .WhiteSpace(UiWhiteSpace.Normal))
                .Gap(10f)
                .Padding(14f)
                .Radius(18f)
                .Background("#101B28");
        }

        private UiElementBuilder BuildSelectionDeck(
            ReactiveContext<GenreInfoShowcasePanelState> context,
            SelectionDockSnapshot dock,
            EntityInsightTextResolver textResolver)
        {
            UiElementBuilder primaryCard = dock.Primary == null
                ? Ui.Text(textResolver.ResolveRequiredTokenKey("genreinfo.selection.empty")).FontSize(11f).Color("#8EA2B6")
                : BuildPrimaryPortraitCard(dock.Primary, textResolver);

            return Ui.Card(
                    Ui.Text(textResolver.ResolveRequiredTokenKey("genreinfo.selection.title"))
                        .FontSize(12f)
                        .Bold()
                        .Color("#F0C36B"),
                    Ui.Text($"{dock.SelectionCount} {textResolver.ResolveRequiredTokenKey("genreinfo.selection.units")} | {dock.SelectionSummary}")
                        .FontSize(11.5f)
                        .Color("#D8E5F3")
                        .WhiteSpace(UiWhiteSpace.Normal),
                    BuildAvatarStrip(dock),
                    primaryCard,
                    BuildCategoryChips(dock),
                    BuildUnitCardGrid(context, dock, textResolver))
                .Gap(10f)
                .Padding(14f)
                .Radius(18f)
                .Background("#0E1720");
        }

        private UiElementBuilder BuildAvatarStrip(SelectionDockSnapshot dock)
        {
            if (dock.Cards.Count <= 0)
            {
                return Ui.Column();
            }

            int count = Math.Min(6, dock.Cards.Count);
            var avatars = new UiElementBuilder[count];
            for (int i = 0; i < count; i++)
            {
                SelectionCardData card = dock.Cards[i];
                avatars[i] = Ui.Image(card.PortraitUri)
                    .Width(42f)
                    .Height(42f)
                    .Radius(999f)
                    .Border(2f, card.IsPrimary ? ParseHexColor(card.AccentHex, new UiColor(0xF0, 0xC3, 0x6B)) : new UiColor(0x2B, 0x41, 0x58));
            }

            return Ui.Row(avatars)
                .Gap(8f)
                .Wrap();
        }

        private UiElementBuilder BuildPrimaryPortraitCard(SelectionCardData card, EntityInsightTextResolver textResolver)
        {
            return Ui.Card(
                    Ui.Row(
                            Ui.Image(card.PortraitUri).Width(108f).Height(108f).Radius(18f),
                            Ui.Column(
                                    Ui.Row(
                                            Ui.Image(card.GenreIconUri).Width(22f).Height(22f),
                                            Ui.Text(card.GenreLabel).FontSize(11f).Bold().Color("#F0C36B"))
                                        .Gap(8f)
                                        .Align(UiAlignItems.Center),
                                    Ui.Text(card.Name).FontSize(21f).Bold().Color("#F7FBFF"),
                                    Ui.Text(card.QuickStats).FontSize(11.5f).Color("#BCD0E2").WhiteSpace(UiWhiteSpace.Normal),
                                    Ui.Text(card.Body).FontSize(11f).Color("#DCE7F2").WhiteSpace(UiWhiteSpace.Normal),
                                    Ui.Text($"{textResolver.ResolveRequiredTokenKey("genreinfo.selection.primary")}: #{card.EntityId}")
                                        .FontSize(10.5f)
                                        .Color("#89A2B8"))
                                .Gap(6f)
                                .FlexGrow(1f))
                        .Gap(12f)
                        .Align(UiAlignItems.Start))
                .Padding(12f)
                .Radius(18f)
                .Border(1f, ParseHexColor(card.AccentHex, new UiColor(0x58, 0xB7, 0xFF)))
                .Background("#112131");
        }

        private UiElementBuilder BuildCategoryChips(SelectionDockSnapshot dock)
        {
            if (dock.Categories.Count <= 0)
            {
                return Ui.Column();
            }

            var chips = new UiElementBuilder[dock.Categories.Count];
            for (int i = 0; i < dock.Categories.Count; i++)
            {
                SelectionCategorySummary category = dock.Categories[i];
                chips[i] = Ui.Text(category.IsPrimaryCategory ? $"{category.Label} x{category.Count} *" : $"{category.Label} x{category.Count}")
                    .FontSize(10.5f)
                    .Bold()
                    .Color(category.IsPrimaryCategory ? "#08131D" : "#DDE8F2")
                    .Padding(8f, 5f)
                    .Radius(999f)
                    .Background(category.IsPrimaryCategory ? "#F0C36B" : "#162432");
            }

            return Ui.Row(chips)
                .Gap(6f)
                .Wrap();
        }

        private UiElementBuilder BuildUnitCardGrid(
            ReactiveContext<GenreInfoShowcasePanelState> context,
            SelectionDockSnapshot dock,
            EntityInsightTextResolver textResolver)
        {
            if (dock.Cards.Count <= 0)
            {
                return Ui.Text(textResolver.ResolveRequiredTokenKey("genreinfo.selection.empty"))
                    .FontSize(11f)
                    .Color("#7E96AA");
            }

            const int columns = 3;
            int rowCount = Math.Max(1, (dock.Cards.Count + columns - 1) / columns);
            float viewportHeight = 252f;
            UiVirtualWindow window = context.GetVerticalVirtualWindow(
                GenreInfoShowcaseIds.SelectionGridHostId,
                rowCount,
                UnitCardRowHeight,
                viewportHeight,
                overscan: 2);

            var rows = new List<UiElementBuilder>();
            if (window.LeadingSpacerExtent > 0.01f)
            {
                rows.Add(Ui.Spacer(window.LeadingSpacerExtent));
            }

            for (int rowIndex = window.StartIndex; rowIndex < window.EndIndexExclusive; rowIndex++)
            {
                rows.Add(BuildUnitCardRow(dock, rowIndex, columns, textResolver));
            }

            if (window.TrailingSpacerExtent > 0.01f)
            {
                rows.Add(Ui.Spacer(window.TrailingSpacerExtent));
            }

            return Ui.Column(
                    Ui.Text(textResolver.ResolveRequiredTokenKey("genreinfo.cards.title"))
                        .FontSize(12f)
                        .Bold()
                        .Color("#F0C36B"),
                    Ui.Text($"{textResolver.ResolveRequiredTokenKey("genreinfo.cards.window")}: {window.StartIndex + 1}-{window.EndIndexExclusive} / {window.TotalCount}")
                        .FontSize(10.5f)
                        .Color("#86A0B4"),
                    Ui.ScrollView(rows.ToArray())
                        .Id(GenreInfoShowcaseIds.SelectionGridHostId)
                        .Height(viewportHeight)
                        .Padding(6f)
                        .Gap(0f)
                        .Radius(14f)
                        .Background("#0A131C"))
                .Gap(8f);
        }

        private UiElementBuilder BuildUnitCardRow(SelectionDockSnapshot dock, int rowIndex, int columns, EntityInsightTextResolver textResolver)
        {
            var tiles = new UiElementBuilder[columns];
            int startIndex = rowIndex * columns;
            for (int column = 0; column < columns; column++)
            {
                int cardIndex = startIndex + column;
                tiles[column] = cardIndex < dock.Cards.Count
                    ? BuildUnitCard(dock.Cards[cardIndex], cardIndex, textResolver)
                    : Ui.Panel().Width(UnitCardWidth).Height(UnitCardHeight);
            }

            return Ui.Row(tiles)
                .Gap(UnitCardGap)
                .Height(UnitCardRowHeight);
        }

        private UiElementBuilder BuildUnitCard(SelectionCardData card, int index, EntityInsightTextResolver textResolver)
        {
            return Ui.Card(
                    Ui.Row(
                            Ui.Image(card.PortraitUri).Width(44f).Height(44f).Radius(12f),
                            Ui.Column(
                                    Ui.Text(card.Name).FontSize(12f).Bold().Color("#F7FBFF").WhiteSpace(UiWhiteSpace.Normal),
                                    Ui.Text(card.QuickStats).FontSize(10f).Color("#A7BDCF").WhiteSpace(UiWhiteSpace.Normal),
                                    Ui.Text(card.GenreLabel).FontSize(9.5f).Bold().Color("#F0C36B"))
                                .Gap(3f)
                                .FlexGrow(1f))
                        .Gap(8f),
                    Ui.Row(
                            Ui.Text($"{index + 1:00}").FontSize(10f).Color("#698197"),
                            Ui.Text($"#{card.EntityId}").FontSize(10f).Bold().Color("#D9E5F0"),
                            card.IsPrimary
                                ? Ui.Text(textResolver.ResolveRequiredTokenKey("genreinfo.cards.primary")).FontSize(8.5f).Bold().Color("#08131D").Padding(6f, 3f).Radius(999f).Background("#F0C36B")
                                : Ui.Panel())
                        .Gap(6f)
                        .Align(UiAlignItems.Center))
                .Width(UnitCardWidth)
                .Height(UnitCardHeight)
                .Padding(10f)
                .Radius(14f)
                .Background(card.IsPrimary ? "#173145" : "#121E29")
                .Border(1f, card.IsPrimary ? ParseHexColor(card.AccentHex, new UiColor(0x58, 0xB7, 0xFF)) : new UiColor(0x2B, 0x41, 0x58));
        }

        private UiElementBuilder BuildLocaleButton(string label, string localeKey, string activeLocaleKey, string elementId)
        {
            bool active = string.Equals(localeKey, activeLocaleKey, StringComparison.OrdinalIgnoreCase);
            return Ui.Button(label, _ =>
                {
                    if (_engine != null)
                    {
                        _runtime.SetLocale(_engine, localeKey);
                    }
                })
                .Id(elementId)
                .Padding(10f, 8f)
                .Radius(999f)
                .Background(active ? "#5E4518" : "#172432")
                .Color(active ? "#FFF4D8" : "#D9E5F0");
        }

        private UiElementBuilder BuildViewButton(string label, SelectionViewMode mode, SelectionViewMode activeMode, string elementId)
        {
            bool active = mode == activeMode;
            return Ui.Button(label, _ =>
                {
                    if (_engine == null)
                    {
                        return;
                    }

                    if (mode == SelectionViewMode.Live)
                    {
                        _runtime.ShowLiveSelection(_engine);
                    }
                    else
                    {
                        _runtime.ShowFormationSelection(_engine);
                    }
                })
                .Id(elementId)
                .Padding(10f, 8f)
                .Radius(10f)
                .Background(active ? "#284B67" : "#132232")
                .Color("#F5F7FA");
        }

        private UiElementBuilder BuildRecallGroupButton(int groupIndex, SelectionGroupSummary summary, int activeGroup, EntityInsightTextResolver textResolver)
        {
            string label = $"{textResolver.ResolveRequiredTokenKey($"genreinfo.group.{groupIndex}")} {summary.Count}";
            return Ui.Button(label, _ =>
                {
                    if (_engine != null)
                    {
                        _runtime.RecallControlGroup(_engine, groupIndex);
                    }
                })
                .Id($"{GenreInfoShowcaseIds.RecallGroupPrefix}{groupIndex}")
                .Padding(10f, 8f)
                .Radius(10f)
                .Background(activeGroup == groupIndex ? "#5E4518" : (summary.Count > 0 ? "#183247" : "#111A24"))
                .Color(summary.Count > 0 ? "#F5F7FA" : "#7E93A8");
        }

        private UiElementBuilder BuildSaveGroupButton(int groupIndex, EntityInsightTextResolver textResolver)
        {
            return Ui.Button($"{textResolver.ResolveRequiredTokenKey("genreinfo.controls.save_prefix")} G{groupIndex}", _ =>
                {
                    if (_engine != null)
                    {
                        _runtime.SaveControlGroup(_engine, groupIndex);
                    }
                })
                .Id($"{GenreInfoShowcaseIds.SaveGroupPrefix}{groupIndex}")
                .Padding(8f, 6f)
                .Radius(10f)
                .Background("#101A24")
                .Color("#8FA6BA");
        }

        private static GenreInfoShowcasePanelState BuildState(GameEngine engine, string mapId)
        {
            PresentationTextLocaleSelection localeSelection = engine.GetService(CoreServiceKeys.PresentationTextLocaleSelection)
                ?? new PresentationTextLocaleSelection(PresentationTextCatalog.Empty);
            string signature = $"{localeSelection.ActiveLocaleId}|{ResolveActiveControlGroup(engine)}|{engine.GetService(EntityInfoPanelServiceKeys.Service)?.UiRevision ?? 0}";

            if (SelectionContextRuntime.TryDescribeCurrentView(engine.World, engine.GlobalContext, out SelectionViewDescriptor descriptor))
            {
                signature = $"{signature}|{descriptor.ViewKey}|{descriptor.Container.SetKey}|{descriptor.Container.MemberCount}|{descriptor.Container.Revision}|{descriptor.Container.Primary.Id}";
            }

            return new GenreInfoShowcasePanelState(mapId, signature);
        }

        private static SelectionDockSnapshot ResolveSelectionDock(GameEngine engine, EntityInfoPanelService? service, EntityInsightTextResolver textResolver)
        {
            var cards = new List<SelectionCardData>();
            var categories = new List<SelectionCategorySummary>();
            SelectionViewMode viewMode = ResolveSelectionViewMode(engine);
            string viewLabel = ResolveSelectionViewLabel(engine, textResolver, viewMode);
            int activeControlGroup = ResolveActiveControlGroup(engine);
            SelectionGroupSummary group1 = ResolveControlGroupSummary(engine, 1);
            SelectionGroupSummary group2 = ResolveControlGroupSummary(engine, 2);
            SelectionGroupSummary group3 = ResolveControlGroupSummary(engine, 3);
            SelectionGroupSummary group4 = ResolveControlGroupSummary(engine, 4);

            if (service != null &&
                SelectionContextRuntime.TryGetCurrentContainer(engine.World, engine.GlobalContext, out Entity container) &&
                SelectionContextRuntime.TryGetRuntime(engine.GlobalContext, out SelectionRuntime selection))
            {
                int count = selection.GetSelectionCount(container);
                Entity primary = SelectionContextRuntime.TryGetCurrentPrimary(engine.World, engine.GlobalContext, out Entity resolvedPrimary)
                    ? resolvedPrimary
                    : Entity.Null;
                for (int index = 0; index < count; index++)
                {
                    if (selection.TryGetSelectionAt(container, index, out Entity entity) &&
                        entity != Entity.Null &&
                        engine.World.IsAlive(entity))
                    {
                        cards.Add(BuildSelectionCard(engine.World, service, textResolver, entity, entity == primary));
                    }
                }

                categories.AddRange(ResolveCategories(cards));
            }

            SelectionCardData? primaryCard = null;
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i].IsPrimary)
                {
                    primaryCard = cards[i];
                    break;
                }
            }

            if (primaryCard == null && cards.Count > 0)
            {
                primaryCard = cards[0];
            }

            string selectionSummary = cards.Count <= 0
                ? textResolver.ResolveRequiredTokenKey("genreinfo.selection.empty")
                : string.Join(" | ", cards.GetRange(0, Math.Min(4, cards.Count)).ConvertAll(card => card.Name));

            return new SelectionDockSnapshot(
                viewMode,
                viewLabel,
                activeControlGroup,
                group1,
                group2,
                group3,
                group4,
                cards.Count,
                selectionSummary,
                cards,
                categories,
                primaryCard);
        }

        private static SelectionCardData BuildSelectionCard(World world, EntityInfoPanelService service, EntityInsightTextResolver textResolver, Entity entity, bool isPrimary)
        {
            string name = world.TryGet(entity, out Name entityName) && !string.IsNullOrWhiteSpace(entityName.Value)
                ? entityName.Value
                : $"Entity #{entity.Id}";

            if (service.TryGetEntityInsightProfile(world, entity, out EntityInsightProfile profile))
            {
                return new SelectionCardData(
                    entity.Id,
                    name,
                    ResolveCategoryLabel(name),
                    profile.AccentColorHex,
                    service.BuildInsightPortraitIconUri(profile),
                    service.BuildInsightGenreIconUri(profile),
                    service.ResolveTextTokenId(profile.GenreLabelTokenId),
                    TrimText(service.ResolveTextTokenId(profile.BodyTokenId), 92),
                    BuildQuickStats(world, entity, profile, service),
                    isPrimary);
            }

            string glyph = string.IsNullOrWhiteSpace(name) ? "?" : name[..1];
            return new SelectionCardData(
                entity.Id,
                name,
                ResolveCategoryLabel(name),
                "#5C7FA0",
                service.BuildInsightGlyphIconUri(glyph, "#5C7FA0", "#12202C", emphatic: isPrimary),
                service.BuildInsightGlyphIconUri("?", "#5C7FA0", "#12202C"),
                textResolver.ResolveRequiredTokenKey("genreinfo.selection.unknown"),
                textResolver.ResolveRequiredTokenKey("genreinfo.selection.no_profile"),
                textResolver.ResolveRequiredTokenKey("genreinfo.selection.no_stats"),
                isPrimary);
        }

        private static string BuildQuickStats(World world, Entity entity, EntityInsightProfile profile, EntityInfoPanelService service)
        {
            if (!world.TryGet(entity, out AttributeBuffer attributes))
            {
                return service.ResolveTextTokenKey("genreinfo.selection.no_stats");
            }

            var parts = new List<string>(2);
            int limit = Math.Min(2, profile.Stats.Length);
            for (int i = 0; i < limit; i++)
            {
                EntityInsightStatProfile stat = profile.Stats[i];
                string label = service.ResolveTextTokenId(stat.LabelTokenId);
                float current = stat.SourceKind == EntityInsightStatSourceKind.Attribute ? attributes.GetCurrent(stat.AttributeId) : stat.ConstantValue;
                float baseValue = stat.SourceKind == EntityInsightStatSourceKind.Attribute ? attributes.GetBase(stat.AttributeId) : stat.ConstantValue;
                string value = stat.DisplayMode == EntityInsightValueDisplayMode.CurrentOverBase
                    ? $"{FormatNumber(current)}/{FormatNumber(baseValue)}"
                    : FormatNumber(current);
                parts.Add($"{label} {value}");
            }

            return parts.Count == 0
                ? service.ResolveTextTokenKey("genreinfo.selection.no_stats")
                : string.Join(" | ", parts);
        }

        private static List<SelectionCategorySummary> ResolveCategories(List<SelectionCardData> cards)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var primary = new Dictionary<string, bool>(StringComparer.Ordinal);
            var order = new List<string>();

            for (int i = 0; i < cards.Count; i++)
            {
                SelectionCardData card = cards[i];
                if (counts.TryGetValue(card.CategoryLabel, out int count))
                {
                    counts[card.CategoryLabel] = count + 1;
                    primary[card.CategoryLabel] = primary[card.CategoryLabel] || card.IsPrimary;
                }
                else
                {
                    counts[card.CategoryLabel] = 1;
                    primary[card.CategoryLabel] = card.IsPrimary;
                    order.Add(card.CategoryLabel);
                }
            }

            order.Sort((left, right) =>
            {
                int compare = counts[right].CompareTo(counts[left]);
                return compare != 0 ? compare : string.CompareOrdinal(left, right);
            });

            var categories = new List<SelectionCategorySummary>(order.Count);
            for (int i = 0; i < order.Count; i++)
            {
                string label = order[i];
                categories.Add(new SelectionCategorySummary(label, counts[label], primary[label]));
            }

            return categories;
        }

        private static SelectionViewMode ResolveSelectionViewMode(GameEngine engine)
        {
            return SelectionContextRuntime.TryDescribeCurrentView(engine.World, engine.GlobalContext, out SelectionViewDescriptor descriptor) &&
                   string.Equals(descriptor.ViewKey, SelectionViewKeys.Formation, StringComparison.Ordinal)
                ? SelectionViewMode.Formation
                : SelectionViewMode.Live;
        }

        private static string ResolveSelectionViewLabel(GameEngine engine, EntityInsightTextResolver textResolver, SelectionViewMode mode)
        {
            if (!SelectionContextRuntime.TryDescribeCurrentView(engine.World, engine.GlobalContext, out SelectionViewDescriptor descriptor))
            {
                return mode == SelectionViewMode.Formation
                    ? textResolver.ResolveRequiredTokenKey("genreinfo.view.formation")
                    : textResolver.ResolveRequiredTokenKey("genreinfo.view.live");
            }

            string prefix = mode == SelectionViewMode.Formation
                ? textResolver.ResolveRequiredTokenKey("genreinfo.view.formation")
                : textResolver.ResolveRequiredTokenKey("genreinfo.view.live");
            return $"{prefix} -> {descriptor.Container.SetKey}";
        }

        private static SelectionGroupSummary ResolveControlGroupSummary(GameEngine engine, int groupIndex)
        {
            if (!engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? viewerObj) ||
                viewerObj is not Entity viewer ||
                !engine.World.IsAlive(viewer) ||
                engine.GetService(CoreServiceKeys.SelectionRuntime) is not SelectionRuntime selection ||
                !SelectionControlGroupRuntime.TryDescribeControlGroup(engine.World, selection, viewer, groupIndex, out SelectionContainerDescriptor descriptor))
            {
                return SelectionGroupSummary.Empty;
            }

            string primaryLabel = descriptor.Primary != Entity.Null && engine.World.IsAlive(descriptor.Primary) && engine.World.TryGet(descriptor.Primary, out Name name)
                ? name.Value
                : string.Empty;
            return new SelectionGroupSummary(descriptor.MemberCount, primaryLabel);
        }

        private static int ResolveActiveControlGroup(GameEngine engine)
        {
            return engine.GlobalContext.TryGetValue(GenreInfoShowcaseIds.ActiveControlGroupKey, out object? groupObj) &&
                   groupObj is int groupIndex
                ? groupIndex
                : 0;
        }

        private static string ResolveCategoryLabel(string name)
        {
            string trimmed = name.Trim();
            int end = trimmed.Length;
            while (end > 0 && char.IsDigit(trimmed[end - 1]))
            {
                end--;
            }

            while (end > 0 && (trimmed[end - 1] == ' ' || trimmed[end - 1] == '-' || trimmed[end - 1] == '_'))
            {
                end--;
            }

            return end <= 0 ? trimmed : trimmed[..end];
        }

        private static string TrimText(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
            {
                return value;
            }

            return value[..Math.Max(0, maxLength - 1)] + "…";
        }

        private static string FormatNumber(float value)
        {
            return MathF.Abs(value - MathF.Round(value)) < 0.001f
                ? MathF.Round(value).ToString("0")
                : value.ToString("0.#");
        }

        private static UiColor ParseHexColor(string? value, UiColor fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            string hex = value.Trim().TrimStart('#');
            if (hex.Length != 6)
            {
                return fallback;
            }

            try
            {
                byte r = Convert.ToByte(hex[..2], 16);
                byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                return new UiColor(r, g, b);
            }
            catch
            {
                return fallback;
            }
        }

        private sealed record GenreInfoShowcasePanelState(string MapId, string Signature);

        private sealed record SelectionDockSnapshot(
            SelectionViewMode ViewMode,
            string ViewLabel,
            int ActiveControlGroup,
            SelectionGroupSummary Group1,
            SelectionGroupSummary Group2,
            SelectionGroupSummary Group3,
            SelectionGroupSummary Group4,
            int SelectionCount,
            string SelectionSummary,
            List<SelectionCardData> Cards,
            List<SelectionCategorySummary> Categories,
            SelectionCardData? Primary);

        private sealed record SelectionCardData(
            int EntityId,
            string Name,
            string CategoryLabel,
            string AccentHex,
            string PortraitUri,
            string GenreIconUri,
            string GenreLabel,
            string Body,
            string QuickStats,
            bool IsPrimary);

        private sealed record SelectionCategorySummary(string Label, int Count, bool IsPrimaryCategory);

        private readonly record struct SelectionGroupSummary(int Count, string PrimaryLabel)
        {
            public static SelectionGroupSummary Empty => new(0, string.Empty);
        }

        private enum SelectionViewMode : byte
        {
            Live = 0,
            Formation = 1
        }
    }
}
