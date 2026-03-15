using System;
using Ludots.UI.Compose;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Actions;

namespace EntityInfoPanelsMod.UI;

public static class EntityInfoPanelUiComposer
{
    public static UiElementBuilder BuildLayer(EntityInfoPanelService service)
    {
        if (service == null)
        {
            throw new ArgumentNullException(nameof(service));
        }

        int visibleCount = service.GetVisibleUiCount();
        if (visibleCount <= 0)
        {
            return Ui.Column();
        }

        UiElementBuilder[] wrappers = new UiElementBuilder[visibleCount];
        for (int i = 0; i < visibleCount; i++)
        {
            int slot = service.GetVisibleUiSlot(i);
            wrappers[i] = WrapAnchoredPanel(service, slot, i);
        }

        return Ui.Column(wrappers)
            .WidthPercent(100f)
            .HeightPercent(100f)
            .Absolute(0f, 0f)
            .ZIndex(48);
    }

    private static UiElementBuilder WrapAnchoredPanel(EntityInfoPanelService service, int slot, int zIndex)
    {
        EntityInfoPanelLayout layout = service.GetLayout(slot);
        UiElementBuilder card = BuildPanelCard(service, slot)
            .Width(layout.Width)
            .Height(layout.Height);

        UiElementBuilder wrapper = Ui.Column(card)
            .WidthPercent(100f)
            .HeightPercent(100f)
            .Absolute(0f, 0f)
            .Padding(Math.Max(0f, layout.OffsetX), Math.Max(0f, layout.OffsetY))
            .ZIndex(48 + zIndex);

        return layout.Anchor switch
        {
            EntityInfoPanelAnchor.TopLeft => wrapper.Align(UiAlignItems.Start).Justify(UiJustifyContent.Start),
            EntityInfoPanelAnchor.TopRight => wrapper.Align(UiAlignItems.End).Justify(UiJustifyContent.Start),
            EntityInfoPanelAnchor.BottomLeft => wrapper.Align(UiAlignItems.Start).Justify(UiJustifyContent.End),
            EntityInfoPanelAnchor.BottomRight => wrapper.Align(UiAlignItems.End).Justify(UiJustifyContent.End),
            EntityInfoPanelAnchor.TopCenter => wrapper.Align(UiAlignItems.Center).Justify(UiJustifyContent.Start),
            EntityInfoPanelAnchor.BottomCenter => wrapper.Align(UiAlignItems.Center).Justify(UiJustifyContent.End),
            _ => wrapper.Align(UiAlignItems.Center).Justify(UiJustifyContent.Center),
        };
    }

    private static UiElementBuilder BuildPanelCard(EntityInfoPanelService service, int slot)
    {
        EntityInfoPanelHandle handle = service.GetHandle(slot);
        UiElementBuilder header = Ui.Row(
                Ui.Column(
                        Ui.Text(service.GetTitle(slot)).FontSize(15f).Bold().Color("#F6E2AF"),
                        Ui.Text(service.GetSubtitle(slot)).FontSize(11f).Color("#9FB4C9").WhiteSpace(UiWhiteSpace.Normal))
                    .Gap(4f)
                    .FlexGrow(1f),
                Ui.Button("Close", _ => service.Close(handle))
                    .Padding(8f, 6f)
                    .Radius(10f)
                    .Background("#1C2836")
                    .Color("#D8E5F3"))
            .Align(UiAlignItems.Center)
            .Gap(10f);

        UiElementBuilder body = service.GetKind(slot) == EntityInfoPanelKind.ComponentInspector
            ? BuildComponentInspector(service, slot, handle)
            : BuildGasInspector(service, slot, handle);

        return Ui.Card(header, body)
            .Gap(10f)
            .Padding(14f)
            .Radius(18f)
            .Border(1f, new UiColor(0x2B, 0x41, 0x58))
            .Background("#09131D")
            .BackdropBlur(4f)
            .BoxShadow(0f, 10f, 24f, new UiColor(0x00, 0x00, 0x00, 0x55));
    }

    private static UiElementBuilder BuildComponentInspector(EntityInfoPanelService service, int slot, EntityInfoPanelHandle handle)
    {
        int sectionCount = service.GetComponentSectionCount(slot);
        UiElementBuilder[] rows = new UiElementBuilder[Math.Max(1, sectionCount + 1)];
        rows[0] = Ui.Row(
                Ui.Button("Expand All", _ => service.SetAllComponentsEnabled(handle, true))
                    .Padding(8f, 6f)
                    .Radius(10f)
                    .Background("#213248")
                    .Color("#F5F7FA"),
                Ui.Button("Collapse All", _ => service.SetAllComponentsEnabled(handle, false))
                    .Padding(8f, 6f)
                    .Radius(10f)
                    .Background("#182332")
                    .Color("#CFD9E3"))
            .Gap(8f)
            .Wrap();

        for (int i = 0; i < sectionCount; i++)
        {
            int componentTypeId = service.GetComponentSectionTypeId(slot, i);
            bool expanded = service.IsComponentExpanded(slot, i);
            int lineCount = service.GetComponentSectionLineCount(slot, i);
            UiElementBuilder[] children = new UiElementBuilder[Math.Max(1, lineCount + 1)];
            children[0] = Ui.Button(
                    $"{(expanded ? "Hide" : "Show")} {service.GetComponentSectionName(slot, i)}",
                    _ => service.SetComponentEnabled(handle, componentTypeId, !expanded))
                .Padding(8f, 6f)
                .Radius(10f)
                .Background(expanded ? "#25435B" : "#172433")
                .Color(expanded ? "#F6E2AF" : "#C8D5E2");

            for (int lineIndex = 0; lineIndex < lineCount; lineIndex++)
            {
                children[lineIndex + 1] = Ui.Text(service.GetComponentSectionLine(slot, i, lineIndex))
                    .FontSize(11f)
                    .Color("#CCD7E2")
                    .WhiteSpace(UiWhiteSpace.Normal);
            }

            rows[i + 1] = Ui.Card(children)
                .Gap(6f)
                .Padding(10f)
                .Radius(12f)
                .Background("#111C28");
        }

        return Ui.ScrollView(rows)
            .Gap(8f)
            .FlexGrow(1f);
    }

    private static UiElementBuilder BuildGasInspector(EntityInfoPanelService service, int slot, EntityInfoPanelHandle handle)
    {
        EntityInfoGasDetailFlags flags = service.GetGasDetailFlags(slot);
        bool showSources = (flags & EntityInfoGasDetailFlags.ShowAttributeAggregateSources) != 0;
        bool showModifiers = (flags & EntityInfoGasDetailFlags.ShowModifierState) != 0;
        UiElementBuilder toggles = Ui.Row(
                BuildToggleButton(
                    $"Sources {(showSources ? "ON" : "OFF")}",
                    showSources,
                    _ => service.UpdateGasDetailFlags(
                        handle,
                        showSources
                            ? flags & ~EntityInfoGasDetailFlags.ShowAttributeAggregateSources
                            : flags | EntityInfoGasDetailFlags.ShowAttributeAggregateSources)),
                BuildToggleButton(
                    $"Modifiers {(showModifiers ? "ON" : "OFF")}",
                    showModifiers,
                    _ => service.UpdateGasDetailFlags(
                        handle,
                        showModifiers
                            ? flags & ~EntityInfoGasDetailFlags.ShowModifierState
                            : flags | EntityInfoGasDetailFlags.ShowModifierState)))
            .Gap(8f)
            .Wrap();

        int lineCount = service.GetGasLineCount(slot);
        UiElementBuilder[] rows = new UiElementBuilder[lineCount + 1];
        rows[0] = toggles;
        for (int i = 0; i < lineCount; i++)
        {
            rows[i + 1] = Ui.Text(service.GetGasLine(slot, i))
                .FontSize(11f)
                .Color("#CCD7E2")
                .WhiteSpace(UiWhiteSpace.Normal);
        }

        return Ui.ScrollView(rows)
            .Gap(6f)
            .FlexGrow(1f);
    }

    private static UiElementBuilder BuildToggleButton(string label, bool active, Action<UiActionContext> onClick)
    {
        return Ui.Button(label, onClick)
            .Padding(8f, 6f)
            .Radius(10f)
            .Background(active ? "#335872" : "#172433")
            .Color(active ? "#F6E2AF" : "#C8D5E2");
    }
}
