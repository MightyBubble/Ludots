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
            var statuses = new EntityCommandPanelStatusView[6];
            var queueItems = new EntityCommandPanelQueueItemView[8];
            int statusCount = 0;
            int queueItemCount = 0;
            if (source is IEntityCommandPanelSupplementalSource supplementalSource)
            {
                statusCount = supplementalSource.CopyStatuses(state.TargetEntity, statuses);
                queueItemCount = supplementalSource.CopyQueueItems(state.TargetEntity, queueItems);
            }

            float slotSectionHeight = ResolveSlotSectionHeight(state.Size.HeightPx, slotCount, statusCount, queueItemCount);

            ResolvePanelPosition(state.Anchor, state.Size, viewportWidth, viewportHeight, out float left, out float top);

            return Ui.Card(
                    BuildHeader(state, group, groupCount),
                    BuildToolbar(state),
                    BuildSlotSection(state.TargetEntity, state.GroupIndex, source, slotCount, slots, statusCount, statuses, queueItemCount, queueItems, slotSectionHeight))
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
            int statusCount,
            Span<EntityCommandPanelStatusView> statuses,
            int queueItemCount,
            Span<EntityCommandPanelQueueItemView> queueItems,
            float sectionHeight)
        {
            int totalItems = slotCount + statusCount + queueItemCount + (statusCount > 0 ? 1 : 0) + (queueItemCount > 0 ? 1 : 0);
            if (totalItems <= 0)
            {
                return Ui.Card(
                        Ui.Text("No slot or runtime state data available for this source.")
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
                rows[rowIndex++] = BuildSlotRow(target, groupIndex, source, in slots[i]);
            }

            if (statusCount > 0)
            {
                rows[rowIndex++] = BuildSupplementalHeader("Runtime Status", $"{statusCount} live");
                for (int i = 0; i < statusCount; i++)
                {
                    rows[rowIndex++] = BuildStatusRow(in statuses[i]);
                }
            }

            if (queueItemCount > 0)
            {
                rows[rowIndex++] = BuildSupplementalHeader("Order Queue", $"{queueItemCount} tracked");
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
            string progress = $"{Math.Clamp(status.ProgressPermille, (short)0, (short)1000) / 10f:0.#}%";
            string kindLabel = status.Kind == EntityCommandPanelStatusKind.ActiveAbility ? "ABILITY" : "EFFECT";

            return Ui.Card(
                    Ui.Row(
                            Ui.Column(
                                    Ui.Text(string.IsNullOrWhiteSpace(status.Label) ? "(unnamed status)" : status.Label)
                                        .FontSize(12f)
                                        .Bold()
                                        .Color("#F5F7FA"),
                                    Ui.Text(string.IsNullOrWhiteSpace(status.Detail) ? "Running" : status.Detail)
                                        .FontSize(11f)
                                        .Color("#8FA6BD"))
                                .Gap(4f)
                                .FlexGrow(1f)
                                .FlexBasis(0f),
                            Ui.Column(
                                    Ui.Text(kindLabel)
                                        .FontSize(9f)
                                        .Bold()
                                        .Color("#0B1520")
                                        .Padding(7f, 4f)
                                        .Radius(999f)
                                        .Background(accent),
                                    Ui.Text(progress)
                                        .FontSize(10f)
                                        .Bold()
                                        .Color("#DCE7F3"))
                                .Gap(6f)
                                .Align(UiAlignItems.End))
                        .Gap(10f))
                .Padding(10f)
                .Radius(12f)
                .Background("#0E1E2D");
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
                                        .Color("#F5F7FA"),
                                    Ui.Text(string.IsNullOrWhiteSpace(queueItem.Detail) ? "Queued" : queueItem.Detail)
                                        .FontSize(11f)
                                        .Color("#8FA6BD"))
                                .Gap(4f)
                                .FlexGrow(1f)
                                .FlexBasis(0f),
                            Ui.Text(ResolveQueueStageLabel(queueItem.Stage))
                                .FontSize(9f)
                                .Bold()
                                .Color("#0B1520")
                                .Padding(7f, 4f)
                                .Radius(999f)
                                .Background(accent))
                        .Gap(10f)
                        .Align(UiAlignItems.Center))
                .Padding(10f)
                .Radius(12f)
                .Background("#0E1E2D");
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

        private static string ResolveQueueStageLabel(EntityCommandPanelQueueStage stage)
        {
            return stage switch
            {
                EntityCommandPanelQueueStage.Active => "ACTIVE",
                EntityCommandPanelQueueStage.Queued => "QUEUED",
                EntityCommandPanelQueueStage.Pending => "PENDING",
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
