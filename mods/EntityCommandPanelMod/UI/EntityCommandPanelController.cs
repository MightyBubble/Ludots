using System;
using System.Collections.Generic;
using Arch.Core;
using EntityCommandPanelMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Orders;
using Ludots.Core.Scripting;
using Ludots.Core.UI.EntityCommandPanels;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;

namespace EntityCommandPanelMod.UI
{
    internal sealed class EntityCommandPanelController
    {
        private readonly GameEngine _engine;
        private readonly EntityCommandPanelRuntime _runtime;
        private readonly AbilityDefinitionRegistry? _abilityDefinitions;
        private readonly AbilityPresentationIconFactory _iconFactory = new();
        private readonly EntityCommandPanelShowcaseArtFactory _showcaseArtFactory = new();
        private readonly Dictionary<int, string> _abilityLabelCache = new();
        private readonly ReactivePage<HostState> _page;
        private uint _lastRevision;
        private uint _lastToolbarRevision;
        private bool _lastToolbarVisible;
        private UiSurfaceLeaseHandle _lease;

        public EntityCommandPanelController(GameEngine engine, EntityCommandPanelRuntime runtime)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _abilityDefinitions = engine.GetService(CoreServiceKeys.AbilityDefinitionRegistry);
            var textMeasurer = engine.GetService(CoreServiceKeys.UiTextMeasurer) as IUiTextMeasurer
                ?? throw new InvalidOperationException("UiTextMeasurer service not registered.");
            var imageSizeProvider = engine.GetService(CoreServiceKeys.UiImageSizeProvider) as IUiImageSizeProvider
                ?? throw new InvalidOperationException("UiImageSizeProvider service not registered.");
            _page = new ReactivePage<HostState>(
                textMeasurer,
                imageSizeProvider,
                new HostState(0),
                BuildRoot);
        }

        public void Sync(UIRoot root)
        {
            if (_engine.GetService(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost surfaceHost)
            {
                return;
            }

            IEntityCommandPanelToolbarProvider? toolbar = ResolveToolbarProvider();
            bool toolbarVisible = toolbar?.IsVisible == true;
            uint toolbarRevision = toolbarVisible ? toolbar!.Revision : 0u;

            if (!_runtime.HasVisiblePanels && !toolbarVisible)
            {
                ClearIfOwned(root);
                return;
            }

            if (_lastRevision != _runtime.Revision ||
                _lastToolbarRevision != toolbarRevision ||
                _lastToolbarVisible != toolbarVisible)
            {
                _lastRevision = _runtime.Revision;
                _lastToolbarRevision = toolbarRevision;
                _lastToolbarVisible = toolbarVisible;
                _page.SetState(_ => new HostState(_lastRevision));
            }

            surfaceHost.Publish(
                surfaceHost.EnsureLease(
                    ref _lease,
                    new UiSurfaceLeaseRequest("EntityCommandPanel.Host", UiSurfaceSegment.Overlay, priority: 80)),
                UiSurfaceContribution.FromReactivePage(_page));
        }

        public void ClearIfOwned(UIRoot root)
        {
            if (_lease.IsValid &&
                _engine.GetService(CoreServiceKeys.UiSurfaceHost) is IUiSurfaceHost surfaceHost)
            {
                surfaceHost.Release(_lease);
                _lease = default;
            }
        }

        private UiElementBuilder BuildRoot(ReactiveContext<HostState> context)
        {
            Span<int> visibleSlots = stackalloc int[EntityCommandPanelRuntime.MaxInstances];
            int count = _runtime.CopyVisibleSlotIndices(visibleSlots);
            IEntityCommandPanelToolbarProvider? toolbar = ResolveToolbarProvider();
            bool hasToolbar = toolbar?.IsVisible == true;
            if (count == 0 && !hasToolbar)
            {
                return Ui.Panel();
            }

            float viewportWidth = ResolveViewportWidth();
            float viewportHeight = ResolveViewportHeight();
            var children = new UiElementBuilder[count + (hasToolbar ? 1 : 0)];
            int childIndex = 0;
            if (hasToolbar)
            {
                children[childIndex++] = BuildGlobalToolbar(toolbar!, viewportWidth);
            }

            for (int i = 0; i < count; i++)
            {
                children[childIndex++] = BuildPanel(visibleSlots[i], viewportWidth, viewportHeight);
            }

            return Ui.Panel(children)
                .Width(0f)
                .Height(0f);
        }

        private UiElementBuilder BuildGlobalToolbar(IEntityCommandPanelToolbarProvider provider, float viewportWidth)
        {
            var buttons = new EntityCommandPanelToolbarButtonView[16];
            int buttonCount = provider.CopyButtons(buttons);
            var buttonElements = new UiElementBuilder[buttonCount];
            for (int i = 0; i < buttonCount; i++)
            {
                var button = buttons[i];
                string accent = NormalizeColor(button.AccentColorHex, button.Active ? "#F6D37A" : "#4FA9E6");
                buttonElements[i] = Ui.Button(button.Label, _ => { provider.Activate(button.ButtonId); })
                    .Padding(10f, 7f)
                    .Radius(999f)
                    .Background(button.Active ? accent : "#132232")
                    .Color(button.Active ? "#0B1520" : "#E6EEF5");
            }

            float width = Math.Max(420f, Math.Min(980f, viewportWidth - 48f));
            string title = string.IsNullOrWhiteSpace(provider.Title) ? "Cast Mode" : provider.Title;
            string subtitle = string.IsNullOrWhiteSpace(provider.Subtitle) ? "Global interaction profile" : provider.Subtitle;
            string headerAccent = ResolveToolbarAccent(title);

            return Ui.Card(
                    Ui.Row(
                            Ui.Column(
                                    Ui.Text(title)
                                        .FontSize(18f)
                                        .Bold()
                                        .Color("#F5F7FA"),
                                    Ui.Text(subtitle)
                                        .FontSize(11f)
                                        .Color("#B7C6D6")
                                        .WhiteSpace(UiWhiteSpace.Normal))
                                .Gap(6f)
                                .FlexGrow(1f)
                                .FlexBasis(0f),
                            Ui.Column(
                                    BuildTagPill("LIVE", headerAccent, "#0B1520"),
                                    BuildMetaPill("Focus one rule set per map."))
                                .Gap(8f)
                                .Align(UiAlignItems.End))
                        .Gap(12f)
                        .Align(UiAlignItems.Start),
                    Ui.Row(buttonElements)
                        .Gap(8f)
                        .Wrap())
                .Id("entity-command-panel-toolbar")
                .Width(width)
                .Padding(16f)
                .Gap(12f)
                .Radius(22f)
                .Background("#09131C")
                .Border(1f, ParseUiColor("#2F5068"))
                .BackdropBlur(8f)
                .Absolute(Math.Max(24f, (viewportWidth - width) * 0.5f), 22f)
                .ZIndex(48);
        }

        private UiElementBuilder BuildPanel(int slot, float viewportWidth, float viewportHeight)
        {
            if (!_runtime.TryGetStateBySlot(slot, out EntityCommandPanelInstanceState state))
            {
                return Ui.Panel();
            }

            _runtime.TryGetSourceBySlot(slot, out IEntityCommandPanelSource source);
            var sourceContext = new EntityCommandPanelSourceContext(state.TargetEntity, state.SourceId, state.InstanceKey);
            int groupCount = source == null ? 0 : EntityCommandPanelSourceDispatch.GetGroupCount(source, in sourceContext);
            EntityCommandPanelGroupView group = default;
            if (groupCount > 0)
            {
                EntityCommandPanelSourceDispatch.TryGetGroup(source!, in sourceContext, state.GroupIndex, out group);
            }

            var slots = new EntityCommandPanelSlotView[AbilityStateBuffer.CAPACITY];
            int slotCount = source == null
                ? 0
                : EntityCommandPanelSourceDispatch.CopySlots(source, in sourceContext, state.GroupIndex, slots);
            var statuses = new EntityCommandPanelStatusView[6];
            var queueItems = new EntityCommandPanelQueueItemView[8];
            int statusCount = source == null ? 0 : EntityCommandPanelSourceDispatch.CopyStatuses(source, in sourceContext, statuses);
            int queueItemCount = source == null ? 0 : EntityCommandPanelSourceDispatch.CopyQueueItems(source, in sourceContext, queueItems);

            ResolvePanelPosition(state.Anchor, state.Size, viewportWidth, viewportHeight, out float left, out float top);
            string showcaseThemeId = ResolveShowcaseThemeId();
            if (!string.Equals(showcaseThemeId, EntityCommandPanelShowcaseTheme.ClassicId, StringComparison.Ordinal))
            {
                var slotSnapshot = new EntityCommandPanelSlotView[slotCount];
                for (int i = 0; i < slotCount; i++)
                {
                    slotSnapshot[i] = slots[i];
                }

                return BuildShowcasePanel(showcaseThemeId, state, sourceContext, group, groupCount, source, slotSnapshot, left, top);
            }

            RtsHudTheme theme = ResolveHudTheme(state.TargetEntity);
            return state.LayoutPreset switch
            {
                EntityCommandPanelLayoutPreset.CommandDeck => BuildCommandDeckPanel(
                    state,
                    sourceContext,
                    source,
                    slotCount,
                    slots,
                    statusCount,
                    statuses,
                    queueItemCount,
                    queueItems,
                    left,
                    top,
                    theme),
                EntityCommandPanelLayoutPreset.OrderMonitor => BuildOrderMonitorPanel(
                    state,
                    slotCount,
                    slots,
                    statusCount,
                    statuses,
                    queueItemCount,
                    queueItems,
                    left,
                    top,
                    theme),
                _ => BuildStandardPanel(
                    state,
                    sourceContext,
                    group,
                    groupCount,
                    source,
                    slotCount,
                    slots,
                    statusCount,
                    statuses,
                    queueItemCount,
                    queueItems,
                    left,
                    top)
            };
        }

        private UiElementBuilder BuildStandardPanel(
            EntityCommandPanelInstanceState state,
            EntityCommandPanelSourceContext sourceContext,
            EntityCommandPanelGroupView group,
            int groupCount,
            IEntityCommandPanelSource? source,
            int slotCount,
            Span<EntityCommandPanelSlotView> slots,
            int statusCount,
            Span<EntityCommandPanelStatusView> statuses,
            int queueItemCount,
            Span<EntityCommandPanelQueueItemView> queueItems,
            float left,
            float top)
        {
            float slotSectionHeight = ResolveSlotSectionHeight(state.Size.HeightPx, slotCount, statusCount, queueItemCount);
            return Ui.Card(
                    BuildHeader(state, group, groupCount),
                    BuildToolbar(state, state.GroupIndex == 0),
                    BuildSlotSection(sourceContext, state.GroupIndex, source, slotCount, slots, statusCount, statuses, queueItemCount, queueItems, slotSectionHeight))
                .Id($"entity-command-panel-{state.Handle.Slot}")
                .Width(Math.Max(220f, state.Size.WidthPx))
                .Height(Math.Max(180f, state.Size.HeightPx))
                .Padding(14f)
                .Gap(10f)
                .Radius(18f)
                .Background("#0A1621")
                .Border(1f, new UiColor(0x36, 0x51, 0x6A))
                .Overflow(UiOverflow.Hidden)
                .Absolute(left, top)
                .ZIndex(40);
        }

        private UiElementBuilder BuildShowcasePanel(
            string themeId,
            EntityCommandPanelInstanceState state,
            EntityCommandPanelSourceContext sourceContext,
            EntityCommandPanelGroupView group,
            int groupCount,
            IEntityCommandPanelSource? source,
            EntityCommandPanelSlotView[] slots,
            float left,
            float top)
        {
            return themeId switch
            {
                var id when string.Equals(id, EntityCommandPanelShowcaseTheme.Dota2Id, StringComparison.Ordinal) => BuildDota2ShowcasePanel(state, sourceContext, group, groupCount, source, slots, left, top),
                var id when string.Equals(id, EntityCommandPanelShowcaseTheme.Sc2Id, StringComparison.Ordinal) => BuildSc2ShowcasePanel(state, sourceContext, group, groupCount, source, slots, left, top),
                _ => BuildLolShowcasePanel(state, sourceContext, group, groupCount, source, slots, left, top)
            };
        }

        private UiElementBuilder BuildLolShowcasePanel(
            EntityCommandPanelInstanceState state,
            EntityCommandPanelSourceContext sourceContext,
            EntityCommandPanelGroupView group,
            int groupCount,
            IEntityCommandPanelSource? source,
            EntityCommandPanelSlotView[] slots,
            float left,
            float top)
        {
            string themeId = EntityCommandPanelShowcaseTheme.LolId;
            string title = _runtime.ResolveEntityTitle(state.TargetEntity);
            string accent = ResolvePrimaryAccent(slots);
            string interactionModeKey = ResolveInteractionModeKey();
            var primarySlots = new UiElementBuilder[4];
            for (int i = 0; i < primarySlots.Length; i++)
            {
                primarySlots[i] = i < slots.Length
                    ? BuildShowcaseSlotCard(themeId, sourceContext, state.GroupIndex, source, in slots[i], interactionModeKey, 124f, 12f, false)
                    : BuildShowcasePlaceholderCard(themeId, "-", string.Empty, "Unbound", "#415060", 124f, 12f, false);
            }

            var utilitySlots = new[]
            {
                BuildShowcasePlaceholderCard(themeId, "D", "D", "Flash", "#F1C561", 88f, 10f, false),
                BuildShowcasePlaceholderCard(themeId, "F", "F", "Heal", "#62C6FF", 88f, 10f, false),
                BuildShowcasePlaceholderCard(themeId, "1", "1", "Active", "#8B6DF3", 88f, 10f, false),
                BuildShowcasePlaceholderCard(themeId, "4", "4", "Ward", "#60CDB5", 88f, 10f, false)
            };

            return Ui.Card(
                    Ui.Row(
                            Ui.Image(_showcaseArtFactory.BuildPortraitArt(themeId, title, "Summoner Panel", accent))
                                .Width(184f)
                                .Height(160f)
                                .FlexShrink(0f),
                            Ui.Column(
                                    Ui.Row(
                                            Ui.Column(
                                                    Ui.Text(title)
                                                        .FontFamily("Segoe UI Semibold")
                                                        .FontSize(24f)
                                                        .Color("#F3E3A4")
                                                        .TextShadow(0f, 1f, 2f, new UiColor(0x06, 0x09, 0x0E)),
                                                    Ui.Text($"Theme LoL | {ResolveModeBadge(interactionModeKey)} | {ResolveGroupLine(group, groupCount, state.GroupIndex)}")
                                                        .FontFamily("Segoe UI")
                                                        .FontSize(11f)
                                                        .Color("#C6D3DE"))
                                                .Gap(4f),
                                            Ui.Row(
                                                    BuildShowcaseInfoPill("CAST", ResolveModeBadge(interactionModeKey), "#1A2430", "#ECD28B"),
                                                    BuildShowcaseInfoPill("HUD", "LoL", "#1A2430", "#D9E6F2"))
                                                .Gap(8f))
                                        .Justify(UiJustifyContent.SpaceBetween)
                                        .Align(UiAlignItems.Center),
                                    BuildShowcaseBar("Resource", "Mana Ready", 0.84f, "#2C8CE4", "#102035", 564f),
                                    BuildShowcaseBar("Cooldown Sync", ResolveModeBadge(interactionModeKey), ResolveModeProgress(interactionModeKey), "#D2A755", "#241C0D", 564f),
                                    Ui.Row(primarySlots).Gap(8f),
                                    Ui.Row(utilitySlots).Gap(8f))
                                .Gap(10f)
                                .FlexGrow(1f))
                        .Gap(16f))
                .Id($"entity-command-panel-showcase-lol-{state.Handle.Slot}")
                .Width(Math.Max(948f, state.Size.WidthPx))
                .Height(Math.Max(248f, state.Size.HeightPx))
                .Padding(16f)
                .Gap(12f)
                .Radius(22f)
                .BackgroundGradient(135f, new UiColor(0x0A, 0x0F, 0x16), new UiColor(0x12, 0x18, 0x21), new UiColor(0x08, 0x0C, 0x12))
                .Border(2f, new UiColor(0x7A, 0x62, 0x33))
                .BoxShadow(0f, 10f, 22f, new UiColor(0x02, 0x03, 0x06, 180))
                .Absolute(left, top)
                .ZIndex(44);
        }

        private UiElementBuilder BuildDota2ShowcasePanel(
            EntityCommandPanelInstanceState state,
            EntityCommandPanelSourceContext sourceContext,
            EntityCommandPanelGroupView group,
            int groupCount,
            IEntityCommandPanelSource? source,
            EntityCommandPanelSlotView[] slots,
            float left,
            float top)
        {
            string themeId = EntityCommandPanelShowcaseTheme.Dota2Id;
            string title = _runtime.ResolveEntityTitle(state.TargetEntity);
            string accent = ResolvePrimaryAccent(slots);
            string interactionModeKey = ResolveInteractionModeKey();
            var abilitySlots = new UiElementBuilder[6];
            for (int i = 0; i < abilitySlots.Length; i++)
            {
                abilitySlots[i] = i < slots.Length
                    ? BuildShowcaseSlotCard(themeId, sourceContext, state.GroupIndex, source, in slots[i], interactionModeKey, 116f, 11f, false)
                    : BuildShowcasePlaceholderCard(themeId, "-", string.Empty, "Unbound", "#6A5647", 116f, 11f, false);
            }

            var inventorySlots = new[]
            {
                BuildShowcasePlaceholderCard(themeId, "T", "T", "Town", "#8C6A4E", 116f, 11f, false),
                BuildShowcasePlaceholderCard(themeId, "1", "1", "Bottle", "#6C8396", 116f, 11f, false),
                BuildShowcasePlaceholderCard(themeId, "2", "2", "Blade", "#5E8F78", 116f, 11f, false),
                BuildShowcasePlaceholderCard(themeId, "3", "3", "Dust", "#8F7C57", 116f, 11f, false),
                BuildShowcasePlaceholderCard(themeId, "4", "4", "Blink", "#6A69A4", 116f, 11f, false),
                BuildShowcasePlaceholderCard(themeId, "5", "5", "Ward", "#4F8F79", 116f, 11f, false)
            };

            return Ui.Card(
                    Ui.Row(
                            Ui.Column(
                                    Ui.Text(title)
                                        .FontFamily("Georgia")
                                        .FontSize(24f)
                                        .Color("#F0D8AE"),
                                    Ui.Text($"Theme Dota2 | {ResolveGroupLine(group, groupCount, state.GroupIndex)}")
                                        .FontFamily("Georgia")
                                        .FontSize(12f)
                                        .Color("#CFB79B"),
                                    Ui.Image(_showcaseArtFactory.BuildPortraitArt(themeId, title, "Ability Console", accent))
                                        .Width(220f)
                                        .Height(170f)
                                        .FlexShrink(0f),
                                    BuildShowcaseBar("Routing", ResolveModeBadge(interactionModeKey), ResolveModeProgress(interactionModeKey), "#A76B44", "#21140E", 220f))
                                .Gap(10f)
                                .FlexShrink(0f),
                            Ui.Column(
                                    Ui.Row(
                                            BuildShowcaseInfoPill("CAST", ResolveModeBadge(interactionModeKey), "#2A1D16", "#F0D8AE"),
                                            BuildShowcaseInfoPill("GROUP", groupCount <= 0 ? "0/0" : $"{state.GroupIndex + 1}/{groupCount}", "#2A1D16", "#D8C5B1"),
                                            BuildShowcaseInfoPill("HUD", "Dota2", "#2A1D16", "#D6905B"))
                                        .Gap(8f),
                                    BuildShowcaseBar("Ability Ready", "Six-Slot Console", 0.92f, "#B76A44", "#21140E", 742f),
                                    Ui.Row(abilitySlots).Gap(10f),
                                    Ui.Row(inventorySlots).Gap(10f))
                                .Gap(12f)
                                .FlexGrow(1f))
                        .Gap(18f))
                .Id($"entity-command-panel-showcase-dota2-{state.Handle.Slot}")
                .Width(Math.Max(1236f, state.Size.WidthPx))
                .Height(Math.Max(332f, state.Size.HeightPx))
                .Padding(18f)
                .Radius(20f)
                .BackgroundGradient(145f, new UiColor(0x12, 0x0D, 0x0A), new UiColor(0x21, 0x16, 0x11), new UiColor(0x0C, 0x08, 0x06))
                .Border(2f, new UiColor(0x7A, 0x59, 0x3C))
                .BoxShadow(0f, 12f, 28f, new UiColor(0x02, 0x01, 0x00, 190))
                .Absolute(left, top)
                .ZIndex(44);
        }

        private UiElementBuilder BuildSc2ShowcasePanel(
            EntityCommandPanelInstanceState state,
            EntityCommandPanelSourceContext sourceContext,
            EntityCommandPanelGroupView group,
            int groupCount,
            IEntityCommandPanelSource? source,
            EntityCommandPanelSlotView[] slots,
            float left,
            float top)
        {
            string themeId = EntityCommandPanelShowcaseTheme.Sc2Id;
            string title = _runtime.ResolveEntityTitle(state.TargetEntity);
            string accent = ResolvePrimaryAccent(slots);
            string interactionModeKey = ResolveInteractionModeKey();
            var cells = new UiElementBuilder[15];
            for (int i = 0; i < cells.Length; i++)
            {
                if (i < slots.Length)
                {
                    cells[i] = BuildShowcaseSlotCard(themeId, sourceContext, state.GroupIndex, source, in slots[i], interactionModeKey, 82f, 9f, false);
                    continue;
                }

                cells[i] = i switch
                {
                    4 => BuildShowcasePlaceholderCard(themeId, "A", "A", "Attack", "#E88A61", 82f, 9f, false),
                    5 => BuildShowcasePlaceholderCard(themeId, "M", "M", "Move", "#69B8E8", 82f, 9f, false),
                    6 => BuildShowcasePlaceholderCard(themeId, "S", "S", "Stop", "#E57957", 82f, 9f, false),
                    7 => BuildShowcasePlaceholderCard(themeId, "P", "P", "Patrol", "#85D0BE", 82f, 9f, false),
                    8 => BuildShowcasePlaceholderCard(themeId, "H", "H", "Hold", "#D7C56E", 82f, 9f, false),
                    9 => BuildShowcasePlaceholderCard(themeId, "C", "C", "Cloak", "#9177F2", 82f, 9f, false),
                    10 => BuildShowcasePlaceholderCard(themeId, "L", "L", "Lift", "#69D8F8", 82f, 9f, false),
                    11 => BuildShowcasePlaceholderCard(themeId, "R", "R", "Rally", "#E8D06A", 82f, 9f, false),
                    12 => BuildShowcasePlaceholderCard(themeId, "B", "B", "Build", "#6BCFB5", 82f, 9f, false),
                    13 => BuildShowcasePlaceholderCard(themeId, "T", "T", "Tech", "#62A7FF", 82f, 9f, false),
                    _ => BuildShowcasePlaceholderCard(themeId, "X", "X", "Cancel", "#9EB4C4", 82f, 9f, false)
                };
            }

            return Ui.Card(
                    Ui.Row(
                            Ui.Column(
                                    Ui.Image(_showcaseArtFactory.BuildPortraitArt(themeId, title, "Command Card", accent))
                                        .Width(176f)
                                        .Height(176f)
                                        .FlexShrink(0f),
                                    BuildShowcaseInfoPill("VIEW", "SC2", "#0A1824", "#92DEFF"),
                                    BuildShowcaseInfoPill("GROUP", groupCount <= 0 ? "0/0" : $"{state.GroupIndex + 1}/{groupCount}", "#0A1824", "#D8EFF9"))
                                .Gap(8f)
                                .FlexShrink(0f),
                            Ui.Column(
                                    Ui.Row(
                                            Ui.Column(
                                                    Ui.Text(title)
                                                        .FontFamily("Segoe UI Semibold")
                                                        .FontSize(22f)
                                                        .Color("#D7F6FF"),
                                                    Ui.Text($"Theme SC2 | {ResolveGroupLine(group, groupCount, state.GroupIndex)} | {ResolveModeBadge(interactionModeKey)}")
                                                        .FontFamily("Segoe UI")
                                                        .FontSize(11f)
                                                        .Color("#93BED7"))
                                                .Gap(4f),
                                            BuildShowcaseBar("Command Sync", ResolveModeBadge(interactionModeKey), ResolveModeProgress(interactionModeKey), "#4CB6E9", "#0D2131", 254f))
                                        .Justify(UiJustifyContent.SpaceBetween)
                                        .Align(UiAlignItems.Center),
                                    Ui.Row(cells[0], cells[1], cells[2], cells[3], cells[4]).Gap(8f),
                                    Ui.Row(cells[5], cells[6], cells[7], cells[8], cells[9]).Gap(8f),
                                    Ui.Row(cells[10], cells[11], cells[12], cells[13], cells[14]).Gap(8f))
                                .Gap(8f)
                                .FlexGrow(1f))
                        .Gap(16f))
                .Id($"entity-command-panel-showcase-sc2-{state.Handle.Slot}")
                .Width(Math.Max(744f, state.Size.WidthPx))
                .Height(Math.Max(430f, state.Size.HeightPx))
                .Padding(16f)
                .Radius(14f)
                .BackgroundGradient(135f, new UiColor(0x04, 0x0D, 0x15), new UiColor(0x0D, 0x1B, 0x2A), new UiColor(0x07, 0x10, 0x19))
                .Border(2f, new UiColor(0x2A, 0x53, 0x70))
                .BoxShadow(0f, 12f, 26f, new UiColor(0x00, 0x04, 0x09, 190))
                .Absolute(left, top)
                .ZIndex(44);
        }

        private UiElementBuilder BuildShowcaseSlotCard(
            string themeId,
            EntityCommandPanelSourceContext sourceContext,
            int groupIndex,
            IEntityCommandPanelSource? source,
            in EntityCommandPanelSlotView slot,
            string interactionModeKey,
            float width,
            float labelFontSize,
            bool showDetail)
        {
            string accent = ResolveAbilityAccent(in slot);
            string glyph = ResolveAbilityGlyph(in slot, interactionModeKey);
            string hotkey = ResolveActionKeyLabel(slot.ActionId);
            string art = _showcaseArtFactory.BuildSlotArt(
                themeId,
                glyph,
                hotkey,
                accent,
                slot.CooldownPermille,
                slot.StateFlags.HasFlag(EntityCommandSlotStateFlags.Blocked),
                slot.StateFlags.HasFlag(EntityCommandSlotStateFlags.Active),
                slot.StateFlags.HasFlag(EntityCommandSlotStateFlags.Empty));
            float artWidth = ResolveShowcaseArtWidth(themeId, width);
            float artHeight = ResolveShowcaseArtHeight(themeId, artWidth);

            UiElementBuilder card = Ui.Card(
                    Ui.Image(art)
                        .Width(artWidth)
                        .Height(artHeight)
                        .FlexShrink(0f),
                    Ui.Text(ResolveAbilityLabel(in slot))
                        .FontFamily(string.Equals(themeId, EntityCommandPanelShowcaseTheme.Dota2Id, StringComparison.Ordinal) ? "Georgia" : "Segoe UI")
                        .FontSize(labelFontSize)
                        .Bold()
                        .Color(ResolveThemeTextColor(themeId))
                        .Width(width - 4f),
                    showDetail
                        ? Ui.Text(ResolveDetailLabel(in slot))
                            .FontSize(Math.Max(9f, labelFontSize - 1f))
                            .Color(ResolveThemeSubTextColor(themeId))
                            .Width(width - 4f)
                        : Ui.Text(slot.CooldownPermille > 0 ? $"Cooldown {slot.CooldownPermille / 10f:0}%" : ResolveFlagSummary(slot.StateFlags))
                            .FontSize(Math.Max(9f, labelFontSize - 1f))
                            .Color(ResolveThemeSubTextColor(themeId))
                            .Width(width - 4f))
                .Width(width)
                .Gap(4f)
                .Padding(0f)
                .Background(UiColor.Transparent);

            if (EntityCommandPanelSourceDispatch.CanActivate(source) &&
                !slot.StateFlags.HasFlag(EntityCommandSlotStateFlags.Empty))
            {
                int slotIndex = slot.SlotIndex;
                card.OnClick(_ => { EntityCommandPanelSourceDispatch.ActivateSlot(source!, in sourceContext, groupIndex, slotIndex); });
            }

            return card;
        }

        private UiElementBuilder BuildShowcasePlaceholderCard(
            string themeId,
            string glyph,
            string hotkey,
            string label,
            string accentColorHex,
            float width,
            float labelFontSize,
            bool showDetail)
        {
            string art = _showcaseArtFactory.BuildSlotArt(themeId, glyph, hotkey, accentColorHex, 0, blocked: false, active: false, empty: false);
            float artWidth = ResolveShowcaseArtWidth(themeId, width);
            float artHeight = ResolveShowcaseArtHeight(themeId, artWidth);
            return Ui.Card(
                    Ui.Image(art)
                        .Width(artWidth)
                        .Height(artHeight)
                        .FlexShrink(0f),
                    Ui.Text(label)
                        .FontFamily(string.Equals(themeId, EntityCommandPanelShowcaseTheme.Dota2Id, StringComparison.Ordinal) ? "Georgia" : "Segoe UI")
                        .FontSize(labelFontSize)
                        .Bold()
                        .Color(ResolveThemeTextColor(themeId))
                        .Width(width - 4f),
                    showDetail
                        ? Ui.Text("Command")
                            .FontSize(Math.Max(9f, labelFontSize - 1f))
                            .Color(ResolveThemeSubTextColor(themeId))
                            .Width(width - 4f)
                        : Ui.Text("Showcase")
                            .FontSize(Math.Max(9f, labelFontSize - 1f))
                            .Color(ResolveThemeSubTextColor(themeId))
                            .Width(width - 4f))
                .Width(width)
                .Gap(4f)
                .Padding(0f)
                .Background(UiColor.Transparent);
        }

        private static UiElementBuilder BuildShowcaseInfoPill(string label, string value, string background, string color)
        {
            return Ui.Column(
                    Ui.Text(label)
                        .FontSize(10f)
                        .Bold()
                        .Color("#7F95A8"),
                    Ui.Text(value)
                        .FontSize(11f)
                        .Bold()
                        .Color(color))
                .Gap(3f)
                .Padding(8f, 6f)
                .Radius(10f)
                .Background(background);
        }

        private static UiElementBuilder BuildShowcaseBar(string label, string value, float progress, string fillColor, string trackColor, float width)
        {
            float clampedWidth = Math.Max(120f, width);
            float fillWidth = Math.Max(18f, clampedWidth * Math.Clamp(progress, 0.08f, 1f));
            return Ui.Column(
                    Ui.Row(
                            Ui.Text(label)
                                .FontSize(10f)
                                .Bold()
                                .Color("#8AA2B6"),
                            Ui.Text(value)
                                .FontSize(10f)
                                .Bold()
                                .Color("#E7F4FF"))
                        .Justify(UiJustifyContent.SpaceBetween),
                    Ui.Panel(
                            Ui.Panel()
                                .Width(fillWidth)
                                .Height(10f)
                                .Radius(999f)
                                .Background(fillColor))
                        .Width(clampedWidth)
                        .Height(10f)
                        .Radius(999f)
                        .Background(trackColor)
                        .Overflow(UiOverflow.Hidden))
                .Gap(4f);
        }

        private static string ResolveThemeTextColor(string themeId)
        {
            if (string.Equals(themeId, EntityCommandPanelShowcaseTheme.Dota2Id, StringComparison.Ordinal))
            {
                return "#F2DDC0";
            }

            if (string.Equals(themeId, EntityCommandPanelShowcaseTheme.Sc2Id, StringComparison.Ordinal))
            {
                return "#D7F6FF";
            }

            return "#F5E8B1";
        }

        private static string ResolveThemeSubTextColor(string themeId)
        {
            if (string.Equals(themeId, EntityCommandPanelShowcaseTheme.Dota2Id, StringComparison.Ordinal))
            {
                return "#CBB69E";
            }

            if (string.Equals(themeId, EntityCommandPanelShowcaseTheme.Sc2Id, StringComparison.Ordinal))
            {
                return "#8FB9D1";
            }

            return "#C5D3DE";
        }

        private static string ResolveFlagSummary(EntityCommandSlotStateFlags flags)
        {
            if (flags.HasFlag(EntityCommandSlotStateFlags.Active))
            {
                return "Active";
            }

            if (flags.HasFlag(EntityCommandSlotStateFlags.Blocked))
            {
                return "Blocked";
            }

            if (flags.HasFlag(EntityCommandSlotStateFlags.FormOverride))
            {
                return "Form Override";
            }

            if (flags.HasFlag(EntityCommandSlotStateFlags.GrantedOverride))
            {
                return "Granted";
            }

            if (flags.HasFlag(EntityCommandSlotStateFlags.Empty))
            {
                return "Empty";
            }

            return "Ready";
        }

        private static string ResolveGroupLine(EntityCommandPanelGroupView group, int groupCount, int groupIndex)
        {
            string label = string.IsNullOrWhiteSpace(group.GroupLabel) ? "Unavailable" : group.GroupLabel;
            string counter = groupCount <= 0 ? "0/0" : $"{groupIndex + 1}/{groupCount}";
            return $"{label} | {counter}";
        }

        private static float ResolveShowcaseArtWidth(string themeId, float cardWidth)
        {
            if (string.Equals(themeId, EntityCommandPanelShowcaseTheme.Sc2Id, StringComparison.Ordinal))
            {
                return Math.Max(70f, Math.Min(cardWidth, 84f));
            }

            if (string.Equals(themeId, EntityCommandPanelShowcaseTheme.LolId, StringComparison.Ordinal))
            {
                return Math.Max(86f, Math.Min(cardWidth, 116f));
            }

            return Math.Max(88f, Math.Min(cardWidth, 112f));
        }

        private static float ResolveShowcaseArtHeight(string themeId, float artWidth)
        {
            return string.Equals(themeId, EntityCommandPanelShowcaseTheme.Sc2Id, StringComparison.Ordinal)
                ? artWidth * 1.08f
                : artWidth * 1.15625f;
        }

        private static float ResolveModeProgress(string interactionModeKey)
        {
            return interactionModeKey switch
            {
                nameof(InteractionModeType.SmartCast) => 0.96f,
                nameof(InteractionModeType.SmartCastWithIndicator) => 0.72f,
                nameof(InteractionModeType.PressReleaseAimCast) => 0.58f,
                nameof(InteractionModeType.AimCast) => 0.51f,
                _ => 0.42f
            };
        }

        private string ResolvePrimaryAccent(IReadOnlyList<EntityCommandPanelSlotView> slots)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                EntityCommandPanelSlotView slot = slots[i];
                if (slot.StateFlags.HasFlag(EntityCommandSlotStateFlags.Empty))
                {
                    continue;
                }

                return ResolveAbilityAccent(in slot);
            }

            return "#59B7FF";
        }

        private UiElementBuilder BuildCommandDeckPanel(
            EntityCommandPanelInstanceState state,
            EntityCommandPanelSourceContext sourceContext,
            IEntityCommandPanelSource? source,
            int slotCount,
            Span<EntityCommandPanelSlotView> slots,
            int statusCount,
            Span<EntityCommandPanelStatusView> statuses,
            int queueItemCount,
            Span<EntityCommandPanelQueueItemView> queueItems,
            float left,
            float top,
            in RtsHudTheme theme)
        {
            int actionableCount = CountActionableSlots(slotCount, slots);
            string entityTitle = _runtime.ResolveEntityTitle(state.TargetEntity);
            string statusLabel = statusCount > 0
                ? ResolveFriendlyStatusLabel(in statuses[0])
                : actionableCount > 0 ? "Ready for orders" : "No command cards";
            string queueLabel = queueItemCount > 0 ? $"Queue {queueItemCount}" : "Queue empty";

            return Ui.Card(
                    Ui.Row(
                            Ui.Column(
                                    Ui.Text(entityTitle)
                                        .FontSize(22f)
                                        .Bold()
                                        .Color("#F8FBFF"),
                                    Ui.Text(theme.CommandSubtitle)
                                        .FontSize(11f)
                                        .Color("#B3C4D6")
                                        .WhiteSpace(UiWhiteSpace.Normal))
                                .Gap(6f)
                                .FlexGrow(1f)
                                .FlexBasis(0f),
                            BuildTagPill(theme.ScenarioLabel, theme.AccentColorHex, "#08111A"))
                        .Gap(10f)
                        .Align(UiAlignItems.Start),
                    Ui.Row(
                            BuildMetaPill(statusLabel),
                            BuildMetaPill(queueLabel),
                            BuildMetaPill(theme.RuleLabel))
                        .Gap(8f)
                        .Wrap(),
                    BuildCommandDeckGrid(sourceContext, state.GroupIndex, source, slotCount, slots, theme),
                    BuildDeckFooter(statusCount, statuses, queueItemCount, queueItems, theme))
                .Id($"entity-command-panel-{state.Handle.Slot}")
                .Width(Math.Max(360f, state.Size.WidthPx))
                .Height(Math.Max(180f, state.Size.HeightPx))
                .Padding(16f)
                .Gap(12f)
                .Radius(22f)
                .Background("#08131D")
                .Border(1f, ParseUiColor(theme.BorderColorHex))
                .BackdropBlur(8f)
                .Absolute(left, top)
                .ZIndex(44);
        }

        private UiElementBuilder BuildOrderMonitorPanel(
            EntityCommandPanelInstanceState state,
            int slotCount,
            Span<EntityCommandPanelSlotView> slots,
            int statusCount,
            Span<EntityCommandPanelStatusView> statuses,
            int queueItemCount,
            Span<EntityCommandPanelQueueItemView> queueItems,
            float left,
            float top,
            in RtsHudTheme theme)
        {
            string entityTitle = _runtime.ResolveEntityTitle(state.TargetEntity);
            string nextActionHint = ResolvePrimaryActionHint(slotCount, slots);

            return Ui.Card(
                    Ui.Row(
                            Ui.Column(
                                    Ui.Text(theme.MonitorTitle)
                                        .FontSize(15f)
                                        .Bold()
                                        .Color("#F7FAFD"),
                                    Ui.Text(entityTitle)
                                        .FontSize(21f)
                                        .Bold()
                                        .Color("#F7FAFD"),
                                    Ui.Text(theme.MonitorSubtitle)
                                        .FontSize(11f)
                                        .Color("#ACC0D4")
                                        .WhiteSpace(UiWhiteSpace.Normal))
                                .Gap(4f)
                                .FlexGrow(1f)
                                .FlexBasis(0f),
                            BuildTagPill(theme.RuleLabel, theme.AccentColorHex, "#08111A"))
                        .Gap(10f)
                        .Align(UiAlignItems.Start),
                    BuildMonitorHero(statusCount, statuses, nextActionHint, theme),
                    BuildMonitorQueue(queueItemCount, queueItems, theme))
                .Id($"entity-command-panel-{state.Handle.Slot}")
                .Width(Math.Max(300f, state.Size.WidthPx))
                .Height(Math.Max(220f, state.Size.HeightPx))
                .Padding(16f)
                .Gap(12f)
                .Radius(22f)
                .Background("#0A1622")
                .Border(1f, ParseUiColor(theme.BorderColorHex))
                .BackdropBlur(8f)
                .Absolute(left, top)
                .ZIndex(44);
        }

        private UiElementBuilder BuildCommandDeckGrid(
            EntityCommandPanelSourceContext sourceContext,
            int groupIndex,
            IEntityCommandPanelSource? source,
            int slotCount,
            Span<EntityCommandPanelSlotView> slots,
            in RtsHudTheme theme)
        {
            var cards = new List<UiElementBuilder>(slotCount);
            for (int i = 0; i < slotCount; i++)
            {
                EntityCommandPanelSlotView slot = slots[i];
                if (slot.StateFlags.HasFlag(EntityCommandSlotStateFlags.Empty))
                {
                    continue;
                }

                cards.Add(BuildCommandDeckCard(sourceContext, groupIndex, source, in slot, theme));
            }

            if (cards.Count == 0)
            {
                return Ui.Card(
                        Ui.Text("No command cards are available for this selection.")
                            .FontSize(12f)
                            .Color("#9DB2C8"))
                    .Padding(12f)
                    .Radius(16f)
                    .Background("#0D1C29");
            }

            return Ui.Row(cards.ToArray())
                .Gap(10f)
                .Wrap();
        }

        private UiElementBuilder BuildCommandDeckCard(
            EntityCommandPanelSourceContext sourceContext,
            int groupIndex,
            IEntityCommandPanelSource? source,
            in EntityCommandPanelSlotView slot,
            in RtsHudTheme theme)
        {
            bool blocked = slot.StateFlags.HasFlag(EntityCommandSlotStateFlags.Blocked);
            bool pendingTarget = slot.StateFlags.HasFlag(EntityCommandSlotStateFlags.PendingTarget);
            bool active = slot.StateFlags.HasFlag(EntityCommandSlotStateFlags.Active);
            bool interactive = !blocked && EntityCommandPanelSourceDispatch.CanActivate(source);
            string interactionModeKey = ResolveInteractionModeKey();
            string detailLabel = ResolveDetailLabel(in slot);
            string actionKey = ResolveActionKeyLabel(slot.ActionId);

            UiElementBuilder card = Ui.Card(
                    Ui.Row(
                            BuildAbilityIcon(in slot, interactionModeKey),
                            Ui.Column(
                                    Ui.Text(ResolveAbilityLabel(in slot))
                                        .FontSize(14f)
                                        .Bold()
                                        .Color("#F7FAFD")
                                        .WhiteSpace(UiWhiteSpace.Normal),
                                    Ui.Text(detailLabel)
                                        .FontSize(11f)
                                        .Color("#ABC1D5")
                                        .WhiteSpace(UiWhiteSpace.Normal))
                                .Gap(4f)
                                .FlexGrow(1f)
                                .FlexBasis(0f),
                            Ui.Column(
                                    BuildActionPill(slot.SlotIndex, actionKey, isInteractiveGroup: true),
                                    BuildTagPill(
                                        blocked ? "LOCKED" : pendingTarget ? "TARGET" : active ? "LIVE" : "READY",
                                        blocked ? "#D9777F" : pendingTarget ? "#8FB8FF" : active ? theme.AccentColorHex : "#7E8EA4",
                                        blocked ? "#2B0F14" : pendingTarget ? "#0E1F35" : active ? "#08111A" : "#132232"))
                                .Gap(8f)
                                .Align(UiAlignItems.End))
                        .Gap(10f)
                        .Align(UiAlignItems.Start))
                .Width(216f)
                .Height(92f)
                .Padding(12f)
                .Radius(16f)
                .Background(active ? "#122534" : "#0D1A26")
                .Border(1f, ParseUiColor(active ? theme.BorderColorHex : "#244156"));

            if (interactive)
            {
                int slotIndex = slot.SlotIndex;
                card.OnClick(_ => { EntityCommandPanelSourceDispatch.ActivateSlot(source!, in sourceContext, groupIndex, slotIndex); });
            }

            return card;
        }

        private UiElementBuilder BuildDeckFooter(
            int statusCount,
            Span<EntityCommandPanelStatusView> statuses,
            int queueItemCount,
            Span<EntityCommandPanelQueueItemView> queueItems,
            in RtsHudTheme theme)
        {
            string footerText;
            if (statusCount > 0)
            {
                footerText = $"{ResolveFriendlyStatusLabel(in statuses[0])} · {statuses[0].ProgressPermille / 10f:0.#}%";
            }
            else if (queueItemCount > 0)
            {
                footerText = $"Next up: {queueItems[0].Label}";
            }
            else
            {
                footerText = theme.FooterText;
            }

            return Ui.Row(
                    Ui.Text(footerText)
                        .FontSize(11f)
                        .Color("#C8D6E5")
                        .WhiteSpace(UiWhiteSpace.Normal),
                    Ui.Text(theme.FooterHint)
                        .FontSize(10f)
                        .Color("#7FA6C6"))
                .Justify(UiJustifyContent.SpaceBetween)
                .Align(UiAlignItems.Center)
                .Padding(8f, 6f)
                .Background("#0C1823")
                .Radius(14f);
        }

        private UiElementBuilder BuildMonitorHero(
            int statusCount,
            Span<EntityCommandPanelStatusView> statuses,
            string nextActionHint,
            in RtsHudTheme theme)
        {
            if (statusCount <= 0)
            {
                return Ui.Card(
                        Ui.Text("Idle")
                            .FontSize(16f)
                            .Bold()
                            .Color("#F8FBFF"),
                        Ui.Text(string.IsNullOrWhiteSpace(nextActionHint) ? "Queue a production order to start the showcase loop." : nextActionHint)
                            .FontSize(11f)
                            .Color("#AFC1D2")
                            .WhiteSpace(UiWhiteSpace.Normal),
                        BuildProgressBar(0f, "#2A3C4E"))
                    .Padding(14f)
                    .Gap(10f)
                    .Radius(18f)
                    .Background("#0E1D2B");
            }

            EntityCommandPanelStatusView status = statuses[0];
            float progress = Math.Clamp(status.ProgressPermille / 1000f, 0f, 1f);
            string accent = NormalizeColor(status.AccentColorHex, theme.AccentColorHex);

            return Ui.Card(
                    Ui.Row(
                            Ui.Column(
                                    Ui.Text(ResolveFriendlyStatusLabel(in status))
                                        .FontSize(16f)
                                        .Bold()
                                        .Color("#F8FBFF"),
                                    Ui.Text(ResolveFriendlyStatusDetail(in status))
                                        .FontSize(11f)
                                        .Color("#AFC1D2")
                                        .WhiteSpace(UiWhiteSpace.Normal))
                                .Gap(4f)
                                .FlexGrow(1f)
                                .FlexBasis(0f),
                            Ui.Text($"{progress * 100f:0.#}%")
                                .FontSize(12f)
                                .Bold()
                                .Color("#F8FBFF"))
                        .Gap(10f),
                    BuildProgressBar(progress, accent))
                .Padding(14f)
                .Gap(10f)
                .Radius(18f)
                .Background("#0E1D2B");
        }

        private UiElementBuilder BuildMonitorQueue(
            int queueItemCount,
            Span<EntityCommandPanelQueueItemView> queueItems,
            in RtsHudTheme theme)
        {
            var rows = new List<UiElementBuilder>(queueItemCount + 1)
            {
                Ui.Row(
                        Ui.Text("Production Queue")
                            .FontSize(12f)
                            .Bold()
                            .Color("#F7FAFD"),
                        Ui.Text(queueItemCount > 0 ? $"{queueItemCount} tracked" : "waiting")
                            .FontSize(10f)
                            .Color("#86A2BC"))
                    .Justify(UiJustifyContent.SpaceBetween)
            };

            if (queueItemCount <= 0)
            {
                rows.Add(
                    Ui.Card(
                            Ui.Text("No queued orders yet. The next order you issue will appear here.")
                                .FontSize(11f)
                                .Color("#ABC1D5")
                                .WhiteSpace(UiWhiteSpace.Normal))
                        .Padding(12f)
                        .Radius(14f)
                        .Background("#0D1A26"));
            }
            else
            {
                for (int i = 0; i < queueItemCount; i++)
                {
                    rows.Add(BuildQueueRow(in queueItems[i]));
                }
            }

            rows.Add(
                Ui.Row(
                        BuildTagPill(theme.ScenarioLabel, theme.AccentColorHex, "#08111A"),
                        BuildMetaPill(theme.FooterHint))
                    .Gap(8f)
                    .Wrap());

            return Ui.Column(rows.ToArray())
                .Gap(8f);
        }

        private UiElementBuilder BuildHeader(
            EntityCommandPanelInstanceState state,
            EntityCommandPanelGroupView group,
            int groupCount)
        {
            string title = _runtime.ResolveEntityTitle(state.TargetEntity);
            string groupLabel = string.IsNullOrWhiteSpace(group.GroupLabel) ? "Unavailable" : group.GroupLabel;
            string groupCounter = groupCount <= 0 ? "0/0" : $"{state.GroupIndex + 1}/{groupCount}";
            EntityCommandPanelHandle handle = state.Handle;

            return Ui.Row(
                    Ui.Column(
                            Ui.Text(title).FontSize(18f).Bold().Color("#F5F7FA"),
                            Ui.Text($"{groupLabel} · {groupCounter}")
                                .FontSize(11f)
                                .Color("#90A5BA"))
                        .Gap(4f),
                    Ui.Button("x", _ => { _runtime.Close(handle); })
                        .Padding(8f, 6f)
                        .Radius(999f)
                        .Background("#203042")
                        .Color("#F5F7FA"))
                .Align(UiAlignItems.Center)
                .Justify(UiJustifyContent.SpaceBetween);
        }

        private UiElementBuilder BuildToolbar(EntityCommandPanelInstanceState state, bool isInteractiveGroup)
        {
            EntityCommandPanelHandle handle = state.Handle;
            return Ui.Row(
                    Ui.Button("<", _ => { _runtime.CycleGroup(handle, -1); })
                        .Padding(10f, 6f)
                        .Radius(10f)
                        .Background("#162637")
                        .Color("#D9E3ED"),
                    Ui.Button(">", _ => { _runtime.CycleGroup(handle, 1); })
                        .Padding(10f, 6f)
                        .Radius(10f)
                        .Background("#162637")
                        .Color("#D9E3ED"),
                    BuildMetaPill(
                        isInteractiveGroup
                            ? "Playable now: click a card or use Q/W/E/R."
                            : "Preview only: browse here, then return to Live Loadout to cast."))
                .Gap(8f)
                .Wrap();
        }

        private UiElementBuilder BuildSlotSection(
            EntityCommandPanelSourceContext sourceContext,
            int groupIndex,
            IEntityCommandPanelSource? source,
            int slotCount,
            Span<EntityCommandPanelSlotView> slots,
            int statusCount,
            Span<EntityCommandPanelStatusView> statuses,
            int queueItemCount,
            Span<EntityCommandPanelQueueItemView> queueItems,
            float sectionHeight)
        {
            bool isInteractiveGroup = groupIndex == 0;
            int totalItems = slotCount + statusCount + queueItemCount + (statusCount > 0 ? 1 : 0) + (queueItemCount > 0 ? 1 : 0);
            if (totalItems <= 0)
            {
                return Ui.Card(
                        Ui.Text("No abilities or live state to show for this page.")
                            .FontSize(12f)
                            .Color("#8FA6BD"))
                    .Padding(12f)
                    .Radius(14f)
                    .Background("#0E1E2D");
            }

            var rows = new UiElementBuilder[totalItems];
            int rowIndex = 0;
            for (int i = 0; i < slotCount; i++)
            {
                rows[rowIndex++] = BuildSlotRow(sourceContext, groupIndex, source, isInteractiveGroup, in slots[i]);
            }

            if (statusCount > 0)
            {
                rows[rowIndex++] = BuildSupplementalHeader("Now Running", $"{statusCount} tracked");
                for (int i = 0; i < statusCount; i++)
                {
                    rows[rowIndex++] = BuildStatusRow(in statuses[i]);
                }
            }

            if (queueItemCount > 0)
            {
                rows[rowIndex++] = BuildSupplementalHeader("Queue", $"{queueItemCount} tracked");
                for (int i = 0; i < queueItemCount; i++)
                {
                    rows[rowIndex++] = BuildQueueRow(in queueItems[i]);
                }
            }

            return Ui.ScrollView(rows)
                .Height(sectionHeight)
                .Padding(6f)
                .Gap(6f)
                .Radius(14f)
                .Background("#08111A");
        }

        private static float ResolveSlotSectionHeight(float panelHeight, int slotCount, int statusCount, int queueItemCount)
        {
            if (slotCount + statusCount + queueItemCount <= 0)
            {
                return 96f;
            }

            const float reservedHeightPx = 148f;
            return Math.Max(120f, panelHeight - reservedHeightPx);
        }

        private UiElementBuilder BuildSlotRow(
            EntityCommandPanelSourceContext sourceContext,
            int groupIndex,
            IEntityCommandPanelSource? source,
            bool isInteractiveGroup,
            in EntityCommandPanelSlotView slot)
        {
            string interactionModeKey = ResolveInteractionModeKey();
            string abilityLabel = ResolveAbilityLabel(in slot);
            string detailLabel = ResolveDetailLabel(in slot);
            if (!isInteractiveGroup && !slot.StateFlags.HasFlag(EntityCommandSlotStateFlags.Empty))
            {
                detailLabel = $"Preview only · {detailLabel}";
            }

            string actionKey = isInteractiveGroup ? ResolveActionKeyLabel(slot.ActionId) : "VIEW";

            var flags = new List<UiElementBuilder>(4);
            AppendFlag(flags, !isInteractiveGroup, "PREVIEW", "#1D3142", "#D5E8F8");
            AppendFlag(flags, slot.StateFlags.HasFlag(EntityCommandSlotStateFlags.FormOverride), "VARIANT", "#3A3017", "#F7D38F");
            AppendFlag(flags, slot.StateFlags.HasFlag(EntityCommandSlotStateFlags.GrantedOverride), "UNLOCK", "#193521", "#B4F0C2");
            AppendFlag(flags, slot.StateFlags.HasFlag(EntityCommandSlotStateFlags.TemplateBacked), "SPAWNS", "#2A2040", "#D7C5FF");
            AppendFlag(flags, slot.StateFlags.HasFlag(EntityCommandSlotStateFlags.Blocked), "LOCKED", "#4A1D21", "#FFB8B8");
            AppendFlag(flags, slot.StateFlags.HasFlag(EntityCommandSlotStateFlags.PendingTarget), "TARGET", "#172F4D", "#C8DCFF");
            AppendFlag(flags, slot.StateFlags.HasFlag(EntityCommandSlotStateFlags.Active), "ACTIVE", "#173B2D", "#B8FFD8");

            UiElementBuilder flagRow = flags.Count == 0
                ? Ui.Text(string.Empty)
                : Ui.Row(flags.ToArray()).Gap(6f).Wrap();

            UiElementBuilder row = Ui.Card(
                    Ui.Row(
                            BuildAbilityIcon(in slot, interactionModeKey),
                            Ui.Column(
                                    Ui.Text(abilityLabel)
                                        .FontSize(13f)
                                        .Bold()
                                        .Color("#F5F7FA"),
                                    Ui.Text(detailLabel)
                                        .FontSize(11f)
                                        .Color("#8FA6BD"))
                                .Gap(4f)
                                .FlexGrow(1f)
                                .FlexBasis(0f),
                            BuildActionPill(slot.SlotIndex, actionKey, isInteractiveGroup))
                        .Gap(10f),
                    flagRow)
                .Padding(10f)
                .Gap(8f)
                .Radius(12f)
                .Background(isInteractiveGroup ? "#10202F" : "#0C1721");

            if (isInteractiveGroup &&
                EntityCommandPanelSourceDispatch.CanActivate(source) &&
                !slot.StateFlags.HasFlag(EntityCommandSlotStateFlags.Empty))
            {
                int slotIndex = slot.SlotIndex;
                row.OnClick(_ => { EntityCommandPanelSourceDispatch.ActivateSlot(source!, in sourceContext, groupIndex, slotIndex); });
            }

            return row;
        }

        private static UiElementBuilder BuildSupplementalHeader(string title, string detail)
        {
            return Ui.Row(
                    Ui.Text(title)
                        .FontSize(11f)
                        .Bold()
                        .Color("#F2C36B"),
                    Ui.Text(detail)
                        .FontSize(10f)
                        .Color("#7E93A8"))
                .Justify(UiJustifyContent.SpaceBetween)
                .Align(UiAlignItems.Center)
                .Padding(6f, 4f);
        }

        private static UiElementBuilder BuildStatusRow(in EntityCommandPanelStatusView status)
        {
            string accent = NormalizeColor(status.AccentColorHex, status.Kind == EntityCommandPanelStatusKind.ActiveAbility ? "#58B7FF" : "#34D399");
            float progressRatio = Math.Clamp(status.ProgressPermille / 1000f, 0f, 1f);

            return Ui.Card(
                    Ui.Row(
                            Ui.Column(
                                    Ui.Text(ResolveFriendlyStatusLabel(in status))
                                        .FontSize(12f)
                                        .Bold()
                                        .Color("#F5F7FA"),
                                    Ui.Text(ResolveFriendlyStatusDetail(in status))
                                        .FontSize(11f)
                                        .Color("#8FA6BD")
                                        .WhiteSpace(UiWhiteSpace.Normal))
                                .Gap(4f)
                                .FlexGrow(1f)
                                .FlexBasis(0f),
                            Ui.Column(
                                    BuildTagPill(status.Kind == EntityCommandPanelStatusKind.ActiveAbility ? "LIVE" : "EFFECT", accent, "#0B1520"),
                                    Ui.Text($"{progressRatio * 100f:0.#}%")
                                        .FontSize(10f)
                                        .Bold()
                                        .Color("#DCE7F3"))
                                .Gap(6f)
                                .Align(UiAlignItems.End))
                        .Gap(10f))
                .Gap(8f)
                .Padding(10f)
                .Radius(12f)
                .Background("#0E1E2D")
                .Border(1f, ParseUiColor("#20384A"));
        }

        private static UiElementBuilder BuildQueueRow(in EntityCommandPanelQueueItemView queueItem)
        {
            string accent = NormalizeColor(queueItem.AccentColorHex, ResolveQueueStageAccent(queueItem.Stage));
            return Ui.Card(
                    Ui.Row(
                            Ui.Column(
                                    Ui.Text(string.IsNullOrWhiteSpace(queueItem.Label) ? "(unnamed order)" : queueItem.Label)
                                        .FontSize(12f)
                                        .Bold()
                                        .Color("#F5F7FA")
                                        .WhiteSpace(UiWhiteSpace.Normal),
                                    Ui.Text(ResolveFriendlyQueueDetail(queueItem.Detail))
                                        .FontSize(11f)
                                        .Color("#8FA6BD")
                                        .WhiteSpace(UiWhiteSpace.Normal))
                                .Gap(4f)
                                .FlexGrow(1f)
                                .FlexBasis(0f),
                            BuildTagPill(ResolveQueueStageLabel(queueItem.Stage), accent, "#08111A"))
                        .Gap(10f)
                        .Align(UiAlignItems.Center))
                .Padding(10f)
                .Radius(12f)
                .Background("#0E1E2D")
                .Border(1f, ParseUiColor("#20384A"));
        }

        private UiElementBuilder BuildAbilityIcon(in EntityCommandPanelSlotView slot, string interactionModeKey)
        {
            string glyph = ResolveAbilityGlyph(in slot, interactionModeKey);
            string accent = ResolveAbilityAccent(in slot);
            string iconUri = _iconFactory.Build(
                glyph,
                accent,
                ResolveModeBadge(interactionModeKey),
                slot.StateFlags.HasFlag(EntityCommandSlotStateFlags.Blocked),
                slot.StateFlags.HasFlag(EntityCommandSlotStateFlags.Active),
                slot.StateFlags.HasFlag(EntityCommandSlotStateFlags.Empty));

            return Ui.Image(iconUri)
                .Width(46f)
                .Height(46f)
                .FlexShrink(0f);
        }

        private static UiElementBuilder BuildActionPill(int slotIndex, string actionKey, bool isInteractiveGroup)
        {
            string text = string.IsNullOrWhiteSpace(actionKey) ? $"{slotIndex + 1:00}" : actionKey;
            return Ui.Text(text)
                .FontSize(11f)
                .Bold()
                .Color(isInteractiveGroup ? "#0B1520" : "#D8E3EE")
                .Padding(8f, 6f)
                .Radius(999f)
                .Background(isInteractiveGroup ? "#F2C36B" : "#243544");
        }

        private string ResolveAbilityLabel(in EntityCommandPanelSlotView slot)
        {
            if (!string.IsNullOrWhiteSpace(slot.DisplayLabel))
            {
                return slot.DisplayLabel;
            }

            if (slot.StateFlags.HasFlag(EntityCommandSlotStateFlags.Empty))
            {
                return "(empty)";
            }

            if (slot.AbilityId != 0)
            {
                if (_abilityLabelCache.TryGetValue(slot.AbilityId, out string? cached))
                {
                    return cached;
                }

                string raw = AbilityIdRegistry.GetName(slot.AbilityId);
                string label = string.IsNullOrWhiteSpace(raw) ? $"Ability#{slot.AbilityId}" : ShortenName(raw);
                _abilityLabelCache[slot.AbilityId] = label;
                return label;
            }

            if (slot.TemplateEntityId != 0)
            {
                return $"Template#{slot.TemplateEntityId}";
            }

            return "Unknown";
        }

        private static string ResolveDetailLabel(in EntityCommandPanelSlotView slot)
        {
            if (!string.IsNullOrWhiteSpace(slot.DetailLabel))
            {
                return slot.DetailLabel;
            }
            if (slot.CooldownPermille > 0 || slot.ChargesMax > 0)
            {
                return $"CD {slot.CooldownPermille / 10f:0.#}% · Charges {slot.ChargesCurrent}/{slot.ChargesMax}";
            }

            if (slot.TemplateEntityId != 0)
            {
                return $"Template entity {slot.TemplateEntityId}";
            }

            if (slot.AbilityId != 0)
            {
                return $"AbilityId {slot.AbilityId}";
            }

            return "No command assigned";
        }

        private static string ResolveQueueStageLabel(EntityCommandPanelQueueStage stage)
        {
            return stage switch
            {
                EntityCommandPanelQueueStage.Active => "NOW",
                EntityCommandPanelQueueStage.Queued => "NEXT",
                EntityCommandPanelQueueStage.Pending => "READY",
                _ => "ORDER"
            };
        }

        private static string ResolveQueueStageAccent(EntityCommandPanelQueueStage stage)
        {
            return stage switch
            {
                EntityCommandPanelQueueStage.Active => "#58B7FF",
                EntityCommandPanelQueueStage.Queued => "#F2C36B",
                EntityCommandPanelQueueStage.Pending => "#F59E0B",
                _ => "#8FA6BD"
            };
        }

        private static void AppendFlag(List<UiElementBuilder> flags, bool enabled, string label, string background, string color)
        {
            if (!enabled)
            {
                return;
            }

            flags.Add(
                Ui.Text(label)
                    .FontSize(10f)
                    .Bold()
                    .Color(color)
                    .Padding(7f, 4f)
                    .Radius(999f)
                    .Background(background));
        }

        private static UiElementBuilder BuildMetaPill(string value)
        {
            string text = string.IsNullOrWhiteSpace(value) ? "(none)" : value;
            return Ui.Text(text)
                .FontSize(10f)
                .Color("#8FA6BD")
                .Padding(7f, 4f)
                .Radius(999f)
                .Background("#13202C");
        }

        private static UiElementBuilder BuildTagPill(string value, string background, string color)
        {
            string text = string.IsNullOrWhiteSpace(value) ? "(tag)" : value;
            return Ui.Text(text)
                .FontSize(10f)
                .Bold()
                .Color(color)
                .Padding(8f, 4f)
                .Radius(999f)
                .Background(NormalizeColor(background, "#58B7FF"));
        }

        private static UiElementBuilder BuildProgressBar(float ratio, string accentHex)
        {
            float fill = Math.Clamp(ratio, 0f, 1f);
            float fillWidth = 260f * fill;
            float emptyWidth = Math.Max(0f, 260f - fillWidth);
            return Ui.Row(
                    Ui.Panel()
                        .Width(fillWidth)
                        .Height(8f)
                        .Radius(999f)
                        .Background(NormalizeColor(accentHex, "#58B7FF")),
                    Ui.Panel()
                        .Width(emptyWidth)
                        .Height(8f)
                        .Radius(999f)
                        .Background("#20313F"))
                .Gap(0f);
        }

        private string ResolveAbilityGlyph(in EntityCommandPanelSlotView slot, string interactionModeKey)
        {
            if (slot.AbilityId > 0 &&
                _abilityDefinitions != null &&
                _abilityDefinitions.TryGet(slot.AbilityId, out var definition) &&
                definition.HasPresentation &&
                definition.Presentation != null)
            {
                return definition.Presentation.ResolveIconGlyph(interactionModeKey, ResolveFallbackGlyph(slot));
            }

            return ResolveFallbackGlyph(slot);
        }

        private string ResolveAbilityAccent(in EntityCommandPanelSlotView slot)
        {
            if (slot.AbilityId > 0 &&
                _abilityDefinitions != null &&
                _abilityDefinitions.TryGet(slot.AbilityId, out var definition) &&
                definition.HasPresentation &&
                definition.Presentation != null &&
                !string.IsNullOrWhiteSpace(definition.Presentation.AccentColorHex))
            {
                return NormalizeColor(definition.Presentation.AccentColorHex, "#58B7FF");
            }

            if (slot.StateFlags.HasFlag(EntityCommandSlotStateFlags.GrantedOverride))
            {
                return "#61D99D";
            }

            if (slot.StateFlags.HasFlag(EntityCommandSlotStateFlags.FormOverride))
            {
                return "#F1C96D";
            }

            if (slot.StateFlags.HasFlag(EntityCommandSlotStateFlags.TemplateBacked))
            {
                return "#C2A5FF";
            }

            return "#58B7FF";
        }

        private string ResolveFallbackGlyph(in EntityCommandPanelSlotView slot)
        {
            string actionKey = ResolveActionKeyLabel(slot.ActionId);
            if (!string.IsNullOrWhiteSpace(actionKey))
            {
                return actionKey;
            }

            if (slot.StateFlags.HasFlag(EntityCommandSlotStateFlags.Empty))
            {
                return "-";
            }

            string label = ResolveAbilityLabel(in slot);
            return !string.IsNullOrWhiteSpace(label) ? label[..1].ToUpperInvariant() : "?";
        }

        private static string ResolveActionKeyLabel(string actionId)
        {
            if (string.IsNullOrWhiteSpace(actionId))
            {
                return string.Empty;
            }

            if (actionId.StartsWith("Skill", StringComparison.OrdinalIgnoreCase) &&
                actionId.Length > "Skill".Length)
            {
                return actionId["Skill".Length..].ToUpperInvariant();
            }

            return actionId;
        }

        private static string ResolveModeBadge(string interactionModeKey)
        {
            return interactionModeKey switch
            {
                nameof(InteractionModeType.SmartCast) => "SC",
                nameof(InteractionModeType.SmartCastWithIndicator) => "RC",
                nameof(InteractionModeType.AimCast) => "RTS",
                nameof(InteractionModeType.PressReleaseAimCast) => "PR",
                nameof(InteractionModeType.ContextScored) => "CTX",
                _ => "TF"
            };
        }

        private static string ShortenName(string value)
        {
            int lastDot = value.LastIndexOf('.');
            return lastDot >= 0 && lastDot + 1 < value.Length ? value[(lastDot + 1)..] : value;
        }

        private IEntityCommandPanelToolbarProvider? ResolveToolbarProvider()
        {
            return _engine.GetService(CoreServiceKeys.EntityCommandPanelToolbarProvider);
        }

        private string ResolveInteractionModeKey()
        {
            if (_engine.GetService(CoreServiceKeys.ActiveInputOrderMapping) is InputOrderMappingSystem mapping)
            {
                return mapping.InteractionMode.ToString();
            }

            return nameof(InteractionModeType.TargetFirst);
        }

        private string ResolveShowcaseThemeId()
        {
            if (_engine.GlobalContext.TryGetValue(EntityCommandPanelShowcaseTheme.ContextKey, out object? themeObj) &&
                themeObj is string themeId)
            {
                return EntityCommandPanelShowcaseTheme.Normalize(themeId, EntityCommandPanelShowcaseTheme.ClassicId);
            }

            return EntityCommandPanelShowcaseTheme.ClassicId;
        }

        private static string NormalizeColor(string? value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            string trimmed = value.Trim();
            if (!trimmed.StartsWith('#'))
            {
                trimmed = "#" + trimmed;
            }

            return trimmed.Length >= 7 ? trimmed[..7] : fallback;
        }

        private static UiColor ParseUiColor(string value)
        {
            return UiColor.TryParse(value, out UiColor color)
                ? color
                : new UiColor(0x58, 0xB7, 0xFF);
        }

        private static string ResolveFriendlyStatusLabel(in EntityCommandPanelStatusView status)
        {
            if (!string.IsNullOrWhiteSpace(status.Label))
            {
                return status.Label;
            }

            return status.Kind == EntityCommandPanelStatusKind.ActiveAbility
                ? "Production running"
                : "Effect running";
        }

        private static string ResolveFriendlyStatusDetail(in EntityCommandPanelStatusView status)
        {
            if (string.IsNullOrWhiteSpace(status.Detail))
            {
                return "Working";
            }

            return status.Detail
                .Replace("Executing", "In progress", StringComparison.OrdinalIgnoreCase)
                .Replace("Waiting", "Waiting", StringComparison.OrdinalIgnoreCase)
                .Replace("Committed", "Committed", StringComparison.OrdinalIgnoreCase)
                .Replace("steps left", "ticks remaining", StringComparison.OrdinalIgnoreCase)
                .Replace("ticks left", "ticks remaining", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveFriendlyQueueDetail(string? detail)
        {
            if (string.IsNullOrWhiteSpace(detail))
            {
                return "Queued";
            }

            return detail
                .Replace("slot 0", "card Q", StringComparison.OrdinalIgnoreCase)
                .Replace("slot 1", "card W", StringComparison.OrdinalIgnoreCase)
                .Replace("slot 2", "card E", StringComparison.OrdinalIgnoreCase)
                .Replace("slot 3", "card R", StringComparison.OrdinalIgnoreCase)
                .Replace("Active", "Running now", StringComparison.OrdinalIgnoreCase)
                .Replace("Queued", "Queued next", StringComparison.OrdinalIgnoreCase)
                .Replace("Pending", "Ready", StringComparison.OrdinalIgnoreCase);
        }

        private static int CountActionableSlots(int slotCount, Span<EntityCommandPanelSlotView> slots)
        {
            int count = 0;
            for (int i = 0; i < slotCount; i++)
            {
                if (!slots[i].StateFlags.HasFlag(EntityCommandSlotStateFlags.Empty))
                {
                    count++;
                }
            }

            return count;
        }

        private string ResolvePrimaryActionHint(int slotCount, Span<EntityCommandPanelSlotView> slots)
        {
            for (int i = 0; i < slotCount; i++)
            {
                EntityCommandPanelSlotView slot = slots[i];
                if (slot.StateFlags.HasFlag(EntityCommandSlotStateFlags.Empty))
                {
                    continue;
                }

                string actionKey = ResolveActionKeyLabel(slot.ActionId);
                string label = ResolveAbilityLabel(in slot);
                return string.IsNullOrWhiteSpace(actionKey)
                    ? $"Queue {label} to start production."
                    : $"Press {actionKey} or click {label} to start production.";
            }

            return string.Empty;
        }

        private RtsHudTheme ResolveHudTheme(Entity target)
        {
            var tags = _engine.CurrentMapSession?.MapConfig?.Tags;
            bool war3 = false;
            bool cnc = false;
            bool sc2 = false;
            if (tags != null)
            {
                for (int i = 0; i < tags.Count; i++)
                {
                    string tag = tags[i];
                    if (string.Equals(tag, "war3", StringComparison.OrdinalIgnoreCase))
                    {
                        war3 = true;
                    }
                    else if (string.Equals(tag, "cnc", StringComparison.OrdinalIgnoreCase))
                    {
                        cnc = true;
                    }
                    else if (string.Equals(tag, "sc2", StringComparison.OrdinalIgnoreCase))
                    {
                        sc2 = true;
                    }
                }
            }

            string entityTitle = _runtime.ResolveEntityTitle(target);
            if (war3 || entityTitle.Contains("Barracks", StringComparison.OrdinalIgnoreCase))
            {
                return new RtsHudTheme(
                    "WAR3",
                    "Upfront cost",
                    "Command deck for classic Barracks training.",
                    "Watch the barracks run, then look for the Footman stepping out.",
                    "Queue a Footman to see the full train -> exit loop.",
                    "Hotkeys stay pinned to the cards below.",
                    "#7BC96F",
                    "#365D42");
            }

            if (cnc || entityTitle.Contains("Factory", StringComparison.OrdinalIgnoreCase))
            {
                return new RtsHudTheme(
                    "C&C",
                    "Pay over time",
                    "Command deck for staged-funding factory production.",
                    "Watch credits drain in pulses while the War Factory keeps working.",
                    "Queue a Rhino to see pulse payments and rollout timing.",
                    "The right monitor tracks each pulse-driven order.",
                    "#F28F45",
                    "#6A4427");
            }

            if (sc2 || entityTitle.Contains("Gateway", StringComparison.OrdinalIgnoreCase))
            {
                return new RtsHudTheme(
                    "SC2",
                    "Gateway training",
                    "Command deck for Protoss gateway production.",
                    "Watch the gateway charge up, then the Zealot materialize on completion.",
                    "Queue a Zealot to see the upfront-cost training flow.",
                    "The right monitor stays focused on the active training loop.",
                    "#3AC7F5",
                    "#235A70");
            }

            return new RtsHudTheme(
                "RTS",
                "Production rule",
                "Command deck",
                "Production monitor",
                "Queue one order to start the showcase loop.",
                "Use the command cards to issue a focused order.",
                "#58B7FF",
                "#34516A");
        }

        private static string ResolveToolbarAccent(string title)
        {
            if (title.Contains("War3", StringComparison.OrdinalIgnoreCase))
            {
                return "#7BC96F";
            }

            if (title.Contains("C&C", StringComparison.OrdinalIgnoreCase))
            {
                return "#F28F45";
            }

            if (title.Contains("SC2", StringComparison.OrdinalIgnoreCase))
            {
                return "#3AC7F5";
            }

            return "#58B7FF";
        }

        private static void ResolvePanelPosition(
            in EntityCommandPanelAnchor anchor,
            in EntityCommandPanelSize size,
            float viewportWidth,
            float viewportHeight,
            out float left,
            out float top)
        {
            float width = Math.Max(220f, size.WidthPx);
            float height = Math.Max(180f, size.HeightPx);
            float centeredLeft = Math.Max(0f, (viewportWidth - width) * 0.5f + anchor.OffsetX);
            float centeredTop = Math.Max(0f, (viewportHeight - height) * 0.5f + anchor.OffsetY);

            switch (anchor.Preset)
            {
                case EntityCommandPanelAnchorPreset.TopLeft:
                    left = Math.Max(0f, anchor.OffsetX);
                    top = Math.Max(0f, anchor.OffsetY);
                    break;
                case EntityCommandPanelAnchorPreset.TopRight:
                    left = Math.Max(0f, viewportWidth - width - anchor.OffsetX);
                    top = Math.Max(0f, anchor.OffsetY);
                    break;
                case EntityCommandPanelAnchorPreset.BottomLeft:
                    left = Math.Max(0f, anchor.OffsetX);
                    top = Math.Max(0f, viewportHeight - height - anchor.OffsetY);
                    break;
                case EntityCommandPanelAnchorPreset.BottomRight:
                    left = Math.Max(0f, viewportWidth - width - anchor.OffsetX);
                    top = Math.Max(0f, viewportHeight - height - anchor.OffsetY);
                    break;
                case EntityCommandPanelAnchorPreset.BottomCenter:
                    left = centeredLeft;
                    top = Math.Max(0f, viewportHeight - height - anchor.OffsetY);
                    break;
                case EntityCommandPanelAnchorPreset.Center:
                default:
                    left = centeredLeft;
                    top = centeredTop;
                    break;
            }
        }

        private float ResolveViewportWidth()
        {
            if (_engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root && root.Width > 0f)
            {
                return root.Width;
            }

            return 1920f;
        }

        private float ResolveViewportHeight()
        {
            if (_engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root && root.Height > 0f)
            {
                return root.Height;
            }

            return 1080f;
        }

        private readonly record struct HostState(uint Revision);

        private readonly record struct RtsHudTheme(
            string ScenarioLabel,
            string RuleLabel,
            string CommandSubtitle,
            string MonitorSubtitle,
            string FooterText,
            string FooterHint,
            string AccentColorHex,
            string BorderColorHex)
        {
            public string MonitorTitle => "Production Track";
        }
    }
}
