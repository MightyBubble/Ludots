using System;
using System.Collections.Generic;
using EntityCommandPanelMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
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
        private readonly Dictionary<int, string> _abilityLabelCache = new();
        private readonly ReactivePage<HostState> _page;
        private uint _lastRevision;

        public EntityCommandPanelController(GameEngine engine, EntityCommandPanelRuntime runtime)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
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
            if (!_runtime.HasVisiblePanels)
            {
                ClearIfOwned(root);
                return;
            }

            if (_lastRevision != _runtime.Revision)
            {
                _lastRevision = _runtime.Revision;
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
            if (count == 0)
            {
                return Ui.Panel();
            }

            var children = new UiElementBuilder[count];
            float viewportWidth = ResolveViewportWidth();
            float viewportHeight = ResolveViewportHeight();
            for (int i = 0; i < count; i++)
            {
                children[i] = BuildPanel(visibleSlots[i], viewportWidth, viewportHeight);
            }

            return Ui.Panel(children)
                .Width(0f)
                .Height(0f);
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

            Span<EntityCommandPanelSlotView> slots = stackalloc EntityCommandPanelSlotView[AbilityStateBuffer.CAPACITY];
            int slotCount = source == null ? 0 : source.CopySlots(state.TargetEntity, state.GroupIndex, slots);
            float slotSectionHeight = ResolveSlotSectionHeight(state.Size.HeightPx, slotCount);

            ResolvePanelPosition(state.Anchor, state.Size, viewportWidth, viewportHeight, out float left, out float top);

            return Ui.Card(
                    BuildHeader(state, group, groupCount),
                    BuildToolbar(state),
                    BuildSlotSection(slotCount, slots, slotSectionHeight))
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

        private UiElementBuilder BuildSlotSection(int slotCount, Span<EntityCommandPanelSlotView> slots, float sectionHeight)
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
                rows[i] = BuildSlotRow(in slots[i]);
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

        private UiElementBuilder BuildSlotRow(in EntityCommandPanelSlotView slot)
        {
            string abilityLabel = ResolveAbilityLabel(in slot);
            string detailLabel = ResolveDetailLabel(in slot);

            var flags = new List<UiElementBuilder>(4);
            AppendFlag(flags, slot.StateFlags.HasFlag(EntityCommandSlotStateFlags.Base), "BASE", "#1C3345", "#D6E6F4");
            AppendFlag(flags, slot.StateFlags.HasFlag(EntityCommandSlotStateFlags.FormOverride), "FORM", "#3A3017", "#F7D38F");
            AppendFlag(flags, slot.StateFlags.HasFlag(EntityCommandSlotStateFlags.GrantedOverride), "GRANT", "#193521", "#B4F0C2");
            AppendFlag(flags, slot.StateFlags.HasFlag(EntityCommandSlotStateFlags.TemplateBacked), "TPL", "#2A2040", "#D7C5FF");
            AppendFlag(flags, slot.StateFlags.HasFlag(EntityCommandSlotStateFlags.Empty), "EMPTY", "#2C3640", "#A7B4C0");

            UiElementBuilder flagRow = flags.Count == 0
                ? Ui.Text(string.Empty)
                : Ui.Row(flags.ToArray()).Gap(6f).Wrap();

            return Ui.Card(
                    Ui.Row(
                            Ui.Text($"{slot.SlotIndex + 1:00}")
                                .FontSize(11f)
                                .Bold()
                                .Color("#0B1520")
                                .Padding(8f, 6f)
                                .Radius(999f)
                                .Background("#F2C36B"),
                            Ui.Column(
                                    Ui.Text(abilityLabel)
                                        .FontSize(13f)
                                        .Bold()
                                        .Color("#F5F7FA"),
                                    Ui.Text(detailLabel)
                                        .FontSize(11f)
                                        .Color("#8FA6BD"))
                                .Gap(4f))
                        .Gap(10f),
                    flagRow)
                .Padding(10f)
                .Gap(8f)
                .Radius(12f)
                .Background("#10202F");
        }

        private string ResolveAbilityLabel(in EntityCommandPanelSlotView slot)
        {
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

        private static string ShortenName(string value)
        {
            int lastDot = value.LastIndexOf('.');
            return lastDot >= 0 && lastDot + 1 < value.Length ? value[(lastDot + 1)..] : value;
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
