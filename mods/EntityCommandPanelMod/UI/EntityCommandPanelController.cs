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
            int groupCount = source?.GetGroupCount(state.TargetEntity) ?? 0;
            EntityCommandPanelGroupView group = default;
            if (groupCount > 0)
            {
                source!.TryGetGroup(state.TargetEntity, state.GroupIndex, out group);
            }

            var slots = new EntityCommandPanelSlotView[AbilityStateBuffer.CAPACITY];
            int slotCount = source == null ? 0 : source.CopySlots(state.TargetEntity, state.GroupIndex, slots);
            var statuses = new EntityCommandPanelStatusView[6];
            var queueItems = new EntityCommandPanelQueueItemView[8];
            int statusCount = 0;
            int queueItemCount = 0;
            if (source is IEntityCommandPanelSupplementalSource supplementalSource)
            {
                statusCount = supplementalSource.CopyStatuses(state.TargetEntity, statuses);
                queueItemCount = supplementalSource.CopyQueueItems(state.TargetEntity, queueItems);
            }

            ResolvePanelPosition(state.Anchor, state.Size, viewportWidth, viewportHeight, out float left, out float top);
            RtsHudTheme theme = ResolveHudTheme(state.TargetEntity);

            return state.LayoutPreset switch
            {
                EntityCommandPanelLayoutPreset.CommandDeck => BuildCommandDeckPanel(
                    state,
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
                    BuildSlotSection(state.TargetEntity, state.GroupIndex, source, slotCount, slots, statusCount, statuses, queueItemCount, queueItems, slotSectionHeight))
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

        private UiElementBuilder BuildCommandDeckPanel(
            EntityCommandPanelInstanceState state,
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
                    BuildCommandDeckGrid(state.TargetEntity, state.GroupIndex, source, slotCount, slots, theme),
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
            Entity target,
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

                cards.Add(BuildCommandDeckCard(target, groupIndex, source, in slot, theme));
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
            Entity target,
            int groupIndex,
            IEntityCommandPanelSource? source,
            in EntityCommandPanelSlotView slot,
            in RtsHudTheme theme)
        {
            bool blocked = slot.StateFlags.HasFlag(EntityCommandSlotStateFlags.Blocked);
            bool active = slot.StateFlags.HasFlag(EntityCommandSlotStateFlags.Active);
            bool interactive = !blocked && source is IEntityCommandPanelActionSource;
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
                                        blocked ? "LOCKED" : active ? "LIVE" : "READY",
                                        blocked ? "#D9777F" : active ? theme.AccentColorHex : "#7E8EA4",
                                        blocked ? "#2B0F14" : active ? "#08111A" : "#132232"))
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
                card.OnClick(_ => { ((IEntityCommandPanelActionSource)source!).ActivateSlot(target, groupIndex, slotIndex); });
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
            Entity target,
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
                rows[rowIndex++] = BuildSlotRow(target, groupIndex, source, isInteractiveGroup, in slots[i]);
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
            Entity target,
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
                source is IEntityCommandPanelActionSource actions &&
                !slot.StateFlags.HasFlag(EntityCommandSlotStateFlags.Empty))
            {
                int slotIndex = slot.SlotIndex;
                row.OnClick(_ => { actions.ActivateSlot(target, groupIndex, slotIndex); });
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
