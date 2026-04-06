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
                root.IsDirty = true;
            }

            if (!ReferenceEquals(root.Scene, _page.Scene))
            {
                root.MountScene(_page.Scene);
                root.IsDirty = true;
            }
        }

        public void ClearIfOwned(UIRoot root)
        {
            if (ReferenceEquals(root.Scene, _page.Scene))
            {
                root.ClearScene();
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
            var buttons = new EntityCommandPanelToolbarButtonView[12];
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

            float width = Math.Max(320f, Math.Min(560f, viewportWidth - 48f));
            return Ui.Card(
                    Ui.Column(
                            Ui.Text(string.IsNullOrWhiteSpace(provider.Title) ? "Cast Mode" : provider.Title)
                                .FontSize(14f)
                                .Bold()
                                .Color("#F5F7FA"),
                            Ui.Text(string.IsNullOrWhiteSpace(provider.Subtitle) ? "Global interaction profile" : provider.Subtitle)
                                .FontSize(11f)
                                .Color("#8FA6BD"))
                        .Gap(4f),
                    Ui.Row(buttonElements)
                        .Gap(8f)
                        .Wrap())
                .Id("entity-command-panel-toolbar")
                .Width(width)
                .Padding(14f)
                .Gap(10f)
                .Radius(18f)
                .Background("#09131C")
                .Border(1f, new UiColor(0x2B, 0x45, 0x59))
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
            int groupCount = source?.GetGroupCount(state.TargetEntity) ?? 0;
            EntityCommandPanelGroupView group = default;
            if (groupCount > 0)
            {
                source!.TryGetGroup(state.TargetEntity, state.GroupIndex, out group);
            }

            var slots = new EntityCommandPanelSlotView[AbilityStateBuffer.CAPACITY];
            int slotCount = source == null ? 0 : source.CopySlots(state.TargetEntity, state.GroupIndex, slots);
            float slotSectionHeight = ResolveSlotSectionHeight(state.Size.HeightPx, slotCount);

            ResolvePanelPosition(state.Anchor, state.Size, viewportWidth, viewportHeight, out float left, out float top);
            string showcaseThemeId = ResolveShowcaseThemeId();
            if (!string.Equals(showcaseThemeId, EntityCommandPanelShowcaseTheme.ClassicId, StringComparison.Ordinal))
            {
                var slotSnapshot = new EntityCommandPanelSlotView[slotCount];
                for (int i = 0; i < slotCount; i++)
                {
                    slotSnapshot[i] = slots[i];
                }

                return BuildShowcasePanel(showcaseThemeId, state, group, groupCount, source, slotSnapshot, left, top);
            }

            return Ui.Card(
                    BuildHeader(state, group, groupCount),
                    BuildToolbar(state),
                    BuildSlotSection(state.TargetEntity, state.GroupIndex, source, slotCount, slots, slotSectionHeight))
                .Id($"entity-command-panel-{slot}")
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
            EntityCommandPanelGroupView group,
            int groupCount,
            IEntityCommandPanelSource? source,
            EntityCommandPanelSlotView[] slots,
            float left,
            float top)
        {
            return themeId switch
            {
                var id when string.Equals(id, EntityCommandPanelShowcaseTheme.Dota2Id, StringComparison.Ordinal) => BuildDota2ShowcasePanel(state, group, groupCount, source, slots, left, top),
                var id when string.Equals(id, EntityCommandPanelShowcaseTheme.Sc2Id, StringComparison.Ordinal) => BuildSc2ShowcasePanel(state, group, groupCount, source, slots, left, top),
                _ => BuildLolShowcasePanel(state, group, groupCount, source, slots, left, top)
            };
        }

        private UiElementBuilder BuildLolShowcasePanel(
            EntityCommandPanelInstanceState state,
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
                    ? BuildShowcaseSlotCard(themeId, state.TargetEntity, state.GroupIndex, source, in slots[i], interactionModeKey, 124f, 12f, false)
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
                    ? BuildShowcaseSlotCard(themeId, state.TargetEntity, state.GroupIndex, source, in slots[i], interactionModeKey, 116f, 11f, false)
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
                    cells[i] = BuildShowcaseSlotCard(themeId, state.TargetEntity, state.GroupIndex, source, in slots[i], interactionModeKey, 82f, 9f, false);
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
            Entity target,
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

            if (source is IEntityCommandPanelActionSource actions &&
                !slot.StateFlags.HasFlag(EntityCommandSlotStateFlags.Empty))
            {
                int slotIndex = slot.SlotIndex;
                card.OnClick(_ => { actions.ActivateSlot(target, groupIndex, slotIndex); });
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

        private UiElementBuilder BuildToolbar(EntityCommandPanelInstanceState state)
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
                    BuildMetaPill(state.InstanceKey),
                    BuildMetaPill(state.SourceId))
                .Gap(8f)
                .Wrap();
        }

        private UiElementBuilder BuildSlotSection(
            Entity target,
            int groupIndex,
            IEntityCommandPanelSource? source,
            int slotCount,
            Span<EntityCommandPanelSlotView> slots,
            float sectionHeight)
        {
            if (slotCount <= 0)
            {
                return Ui.Card(
                        Ui.Text("No slot data available for this source.")
                            .FontSize(12f)
                            .Color("#8FA6BD"))
                    .Padding(12f)
                    .Radius(14f)
                    .Background("#0E1E2D");
            }

            var rows = new UiElementBuilder[slotCount];
            for (int i = 0; i < slotCount; i++)
            {
                rows[i] = BuildSlotRow(target, groupIndex, source, in slots[i]);
            }

            return Ui.ScrollView(rows)
                .Height(sectionHeight)
                .Padding(6f)
                .Gap(6f)
                .Radius(14f)
                .Background("#08111A");
        }

        private static float ResolveSlotSectionHeight(float panelHeight, int slotCount)
        {
            if (slotCount <= 0)
            {
                return 96f;
            }

            const float reservedHeightPx = 148f;
            return Math.Max(96f, panelHeight - reservedHeightPx);
        }

        private UiElementBuilder BuildSlotRow(
            Entity target,
            int groupIndex,
            IEntityCommandPanelSource? source,
            in EntityCommandPanelSlotView slot)
        {
            string interactionModeKey = ResolveInteractionModeKey();
            string abilityLabel = ResolveAbilityLabel(in slot);
            string detailLabel = ResolveDetailLabel(in slot);
            string actionKey = ResolveActionKeyLabel(slot.ActionId);

            var flags = new List<UiElementBuilder>(4);
            AppendFlag(flags, slot.StateFlags.HasFlag(EntityCommandSlotStateFlags.Base), "BASE", "#1C3345", "#D6E6F4");
            AppendFlag(flags, slot.StateFlags.HasFlag(EntityCommandSlotStateFlags.FormOverride), "FORM", "#3A3017", "#F7D38F");
            AppendFlag(flags, slot.StateFlags.HasFlag(EntityCommandSlotStateFlags.GrantedOverride), "GRANT", "#193521", "#B4F0C2");
            AppendFlag(flags, slot.StateFlags.HasFlag(EntityCommandSlotStateFlags.TemplateBacked), "TPL", "#2A2040", "#D7C5FF");
            AppendFlag(flags, slot.StateFlags.HasFlag(EntityCommandSlotStateFlags.Blocked), "BLOCK", "#4A1D21", "#FFB8B8");
            AppendFlag(flags, slot.StateFlags.HasFlag(EntityCommandSlotStateFlags.Active), "ACTIVE", "#173B2D", "#B8FFD8");
            AppendFlag(flags, slot.StateFlags.HasFlag(EntityCommandSlotStateFlags.Empty), "EMPTY", "#2C3640", "#A7B4C0");

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
                            BuildActionPill(slot.SlotIndex, actionKey))
                        .Gap(10f),
                    flagRow)
                .Padding(10f)
                .Gap(8f)
                .Radius(12f)
                .Background("#10202F");

            if (source is IEntityCommandPanelActionSource actions &&
                !slot.StateFlags.HasFlag(EntityCommandSlotStateFlags.Empty))
            {
                int slotIndex = slot.SlotIndex;
                row.OnClick(_ => { actions.ActivateSlot(target, groupIndex, slotIndex); });
            }

            return row;
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

        private static UiElementBuilder BuildActionPill(int slotIndex, string actionKey)
        {
            string text = string.IsNullOrWhiteSpace(actionKey) ? $"{slotIndex + 1:00}" : actionKey;
            return Ui.Text(text)
                .FontSize(11f)
                .Bold()
                .Color("#0B1520")
                .Padding(8f, 6f)
                .Radius(999f)
                .Background("#F2C36B");
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
    }
}
