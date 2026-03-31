using System;
using System.Collections.Generic;
using Ludots.UI.Reactive;
using Ludots.UI.Compose;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Actions;

namespace EntityInfoPanelsMod.UI;

public static class EntityInfoPanelUiComposer
{
    private const float EntityCollectionTileWidth = 148f;
    private const float EntityCollectionTileHeight = 84f;
    private const float EntityCollectionGridGap = 8f;
    private const float EntityCollectionGridRowHeight = EntityCollectionTileHeight + EntityCollectionGridGap;

    public static UiElementBuilder BuildLayer<TState>(EntityInfoPanelService service, ReactiveContext<TState> context)
    {
        if (service == null)
        {
            throw new ArgumentNullException(nameof(service));
        }

        ArgumentNullException.ThrowIfNull(context);

        int visibleCount = service.GetVisibleUiCount();
        if (visibleCount <= 0)
        {
            return Ui.Column();
        }

        UiElementBuilder[] wrappers = new UiElementBuilder[visibleCount];
        for (int i = 0; i < visibleCount; i++)
        {
            int slot = service.GetVisibleUiSlot(i);
            wrappers[i] = WrapAnchoredPanel(service, context, slot, i);
        }

        return Ui.Column(wrappers)
            .WidthPercent(100f)
            .HeightPercent(100f)
            .Absolute(0f, 0f)
            .ZIndex(48);
    }

    private static UiElementBuilder WrapAnchoredPanel<TState>(EntityInfoPanelService service, ReactiveContext<TState> context, int slot, int zIndex)
    {
        EntityInfoPanelLayout layout = service.GetLayout(slot);
        UiElementBuilder card = BuildPanelCard(service, context, slot)
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

    private static UiElementBuilder BuildPanelCard<TState>(EntityInfoPanelService service, ReactiveContext<TState> context, int slot)
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

        UiElementBuilder body = service.GetKind(slot) switch
        {
            EntityInfoPanelKind.ComponentInspector => BuildComponentInspector(service, slot, handle),
            EntityInfoPanelKind.GasInspector => BuildGasInspector(service, slot, handle),
            EntityInfoPanelKind.EntityCollectionInspector => BuildEntityCollectionInspector(service, context, slot),
            _ => Ui.Column(),
        };

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

    private static UiElementBuilder BuildEntityCollectionInspector<TState>(EntityInfoPanelService service, ReactiveContext<TState> context, int slot)
    {
        int count = service.GetEntityCollectionCount(slot);
        EntityInfoPanelLayout layout = service.GetLayout(slot);
        string hostId = GetEntityCollectionGridHostId(slot);

        if (count <= 0)
        {
            return Ui.Column(
                    Ui.Text("Current viewed selection").FontSize(12f).Bold().Color("#F6E2AF"),
                    Ui.Text("Waiting for active selection view.")
                        .FontSize(11f)
                        .Color("#9FB4C9")
                        .WhiteSpace(UiWhiteSpace.Normal))
                .Gap(8f)
                .FlexGrow(1f);
        }

        float usableWidth = Math.Max(200f, layout.Width - 28f);
        int columns = Math.Max(1, (int)MathF.Floor((usableWidth + EntityCollectionGridGap) / (EntityCollectionTileWidth + EntityCollectionGridGap)));
        int rowCount = Math.Max(1, (count + columns - 1) / columns);
        float viewportHeight = Math.Max(120f, layout.Height - 168f);
        UiVirtualWindow window = context.GetVerticalVirtualWindow(
            hostId,
            rowCount,
            EntityCollectionGridRowHeight,
            viewportHeight,
            overscan: 2);

        var rows = new List<UiElementBuilder>();
        if (window.LeadingSpacerExtent > 0.01f)
        {
            rows.Add(Ui.Spacer(window.LeadingSpacerExtent));
        }

        for (int rowIndex = window.StartIndex; rowIndex < window.EndIndexExclusive; rowIndex++)
        {
            rows.Add(BuildEntityCollectionGridRow(service, slot, rowIndex, columns));
        }

        if (window.TrailingSpacerExtent > 0.01f)
        {
            rows.Add(Ui.Spacer(window.TrailingSpacerExtent));
        }

        return Ui.Column(
                Ui.Text("Current viewed selection").FontSize(12f).Bold().Color("#F6E2AF"),
                Ui.Text(
                        $"{service.GetEntityCollectionViewKey(slot)} -> {service.GetEntityCollectionAliasKey(slot)} | {count} entities | {service.GetEntityCollectionCategoryCount(slot)} categories | rows {FormatVisibleRange(window)}")
                    .Id($"{hostId}-summary")
                    .FontSize(11f)
                    .Color("#9FB4C9")
                    .WhiteSpace(UiWhiteSpace.Normal),
                BuildEntityCollectionCategories(service, slot),
                Ui.ScrollView(rows.ToArray())
                    .Id(hostId)
                    .Height(viewportHeight)
                    .Padding(6f)
                    .Gap(0f)
                    .Radius(12f)
                    .Background("#111C28"))
            .Gap(8f)
            .FlexGrow(1f);
    }

    private static UiElementBuilder BuildEntityCollectionCategories(EntityInfoPanelService service, int slot)
    {
        int categoryCount = service.GetEntityCollectionCategoryCount(slot);
        if (categoryCount <= 0)
        {
            return Ui.Text("No category buckets yet.")
                .FontSize(10f)
                .Color("#6F869B");
        }

        var categories = new UiElementBuilder[Math.Min(categoryCount, 8)];
        for (int i = 0; i < categories.Length; i++)
        {
            service.TryGetEntityCollectionCategory(slot, i, out EntityCollectionCategorySummary category);
            categories[i] = Ui.Text(category.ContainsPrimary ? $"{category.Label} x{category.Count} *" : $"{category.Label} x{category.Count}")
                .FontSize(10f)
                .Bold()
                .Color(category.ContainsPrimary ? "#08131D" : "#D5E0EA")
                .Padding(8f, 5f)
                .Radius(999f)
                .Background(category.ContainsPrimary ? "#F0C36B" : "#162533");
        }

        return Ui.Row(categories)
            .Gap(6f)
            .Wrap();
    }

    private static UiElementBuilder BuildEntityCollectionGridRow(EntityInfoPanelService service, int slot, int rowIndex, int columns)
    {
        var tiles = new UiElementBuilder[columns];
        int startIndex = rowIndex * columns;
        for (int column = 0; column < columns; column++)
        {
            int entityIndex = startIndex + column;
            tiles[column] = service.TryGetEntityCollectionRow(slot, entityIndex, out EntityCollectionPanelRow row)
                ? BuildEntityCollectionTile(slot, row)
                : Ui.Panel().Width(EntityCollectionTileWidth).Height(EntityCollectionTileHeight);
        }

        return Ui.Row(tiles)
            .Gap(EntityCollectionGridGap)
            .Height(EntityCollectionGridRowHeight);
    }

    private static UiElementBuilder BuildEntityCollectionTile(int slot, EntityCollectionPanelRow row)
    {
        return Ui.Card(
                Ui.Row(
                        Ui.Text($"{row.Index + 1:000}").FontSize(10f).Color("#60758A"),
                        Ui.Text($"#{row.EntityId}").FontSize(11f).Bold().Color("#F6E2AF"),
                        row.IsPrimary
                            ? Ui.Text("PRIMARY")
                                .FontSize(9f)
                                .Bold()
                                .Color("#08131D")
                                .Padding(6f, 3f)
                                .Radius(999f)
                                .Background("#F6E2AF")
                            : Ui.Panel())
                    .Gap(6f)
                    .Align(UiAlignItems.Center),
                Ui.Text(row.Name)
                    .Id($"{GetEntityCollectionGridHostId(slot)}-tile-{row.Index:0000}-name")
                    .FontSize(12f)
                    .Bold()
                    .Color("#F5F7FA")
                    .WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text(row.AttributesSummary)
                    .Id($"{GetEntityCollectionGridHostId(slot)}-tile-{row.Index:0000}-attrs")
                    .FontSize(10f)
                    .Color("#9FB4C9")
                    .WhiteSpace(UiWhiteSpace.Normal))
            .Width(EntityCollectionTileWidth)
            .Height(EntityCollectionTileHeight)
            .Padding(10f)
            .Radius(12f)
            .Background(row.IsPrimary ? "#183247" : "#162230")
            .Border(1f, row.IsPrimary ? new UiColor(0x73, 0x95, 0xB8) : new UiColor(0x2B, 0x41, 0x58));
    }

    private static UiElementBuilder BuildToggleButton(string label, bool active, Action<UiActionContext> onClick)
    {
        return Ui.Button(label, onClick)
            .Padding(8f, 6f)
            .Radius(10f)
            .Background(active ? "#335872" : "#172433")
            .Color(active ? "#F6E2AF" : "#C8D5E2");
    }

    private static string FormatVisibleRange(UiVirtualWindow window)
    {
        if (window.TotalCount <= 0 || window.VisibleCount <= 0)
        {
            return "0-0";
        }

        return $"{window.StartIndex + 1}-{window.EndIndexExclusive}";
    }

    private static string GetEntityCollectionGridHostId(int slot) => $"entity-info-collection-grid-{slot:00}";
}
