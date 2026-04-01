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
        string closeText = service.ResolveTextTokenKey("entityinfo.action.close");
        if (service.GetKind(slot) == EntityInfoPanelKind.InsightBrief)
        {
            return BuildInsightBriefCard(service, slot);
        }

        EntityInfoPanelHandle handle = service.GetHandle(slot);
        UiElementBuilder header = Ui.Row(
                Ui.Column(
                        Ui.Text(service.GetTitle(slot)).FontSize(15f).Bold().Color("#F6E2AF"),
                        Ui.Text(service.GetSubtitle(slot)).FontSize(11f).Color("#9FB4C9").WhiteSpace(UiWhiteSpace.Normal))
                    .Gap(4f)
                    .FlexGrow(1f),
                Ui.Button(closeText, _ => service.Close(handle))
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

    private static UiElementBuilder BuildInsightBriefCard(EntityInfoPanelService service, int slot)
    {
        EntityInfoPanelHandle handle = service.GetHandle(slot);
        string accentHex = service.GetInsightAccentColor(slot);
        UiColor accent = ParseHexColor(accentHex, new UiColor(0x58, 0xB7, 0xFF));
        UiColor surface = ParseHexColor(service.GetInsightSurfaceColor(slot), new UiColor(0x0F, 0x17, 0x21));
        string closeText = service.ResolveTextTokenKey("entityinfo.action.close");

        var badges = new UiElementBuilder[service.GetInsightBadgeCount(slot)];
        for (int i = 0; i < badges.Length; i++)
        {
            badges[i] = Ui.Row(
                    Ui.Image(service.GetInsightBadgeIconUri(slot, i)).Width(18f).Height(18f).FlexShrink(0f),
                    Ui.Text(service.GetInsightBadgeText(slot, i)).FontSize(10.5f).Bold().Color("#F5F7FA"))
                .Gap(6f)
                .Padding(8f, 6f)
                .Radius(999f)
                .Background("#142230");
        }

        var stats = new UiElementBuilder[service.GetInsightStatCount(slot)];
        for (int i = 0; i < stats.Length; i++)
        {
            stats[i] = Ui.Card(
                    Ui.Row(
                            Ui.Image(service.GetInsightStatIconUri(slot, i)).Width(28f).Height(28f).FlexShrink(0f),
                            Ui.Column(
                                    Ui.Text(service.GetInsightStatLabel(slot, i)).FontSize(10.5f).Color("#9CB2C6"),
                                    Ui.Text(service.GetInsightStatValueText(slot, i)).FontSize(14f).Bold().Color("#F7FBFF"))
                                .Gap(3f))
                        .Gap(8f))
                .Padding(10f)
                .Radius(14f)
                .Background("#101B26");
        }

        var tips = new UiElementBuilder[service.GetInsightTipCount(slot) + 1];
        tips[0] = Ui.Text(service.ResolveTextTokenKey("entityinfo.section.tips"))
            .FontSize(12f)
            .Bold()
            .Color("#F0C36B");
        for (int i = 0; i < service.GetInsightTipCount(slot); i++)
        {
            tips[i + 1] = Ui.Row(
                    Ui.Image(service.GetInsightTipIconUri(slot, i)).Width(22f).Height(22f).FlexShrink(0f),
                    Ui.Text(service.GetInsightTipText(slot, i)).FontSize(11.5f).Color("#D9E5F0").WhiteSpace(UiWhiteSpace.Normal))
                .Gap(8f);
        }

        var actions = new UiElementBuilder[service.GetInsightActionCount(slot) + 1];
        actions[0] = Ui.Text(service.ResolveTextTokenKey("entityinfo.section.actions"))
            .FontSize(12f)
            .Bold()
            .Color("#F0C36B");
        for (int i = 0; i < service.GetInsightActionCount(slot); i++)
        {
            string statusText = service.GetInsightActionStateText(slot, i);
            actions[i + 1] = Ui.Card(
                    Ui.Row(
                            Ui.Image(service.GetInsightActionIconUri(slot, i)).Width(34f).Height(34f).FlexShrink(0f),
                            Ui.Column(
                                    Ui.Text(service.GetInsightActionTitle(slot, i)).FontSize(12f).Bold().Color("#F7FBFF"),
                                    Ui.Text(service.GetInsightActionBody(slot, i)).FontSize(11f).Color("#A9C0D4").WhiteSpace(UiWhiteSpace.Normal))
                                .Gap(4f)
                                .FlexGrow(1f),
                            Ui.Text(statusText)
                                .FontSize(10.5f)
                                .Bold()
                                .Color("#0B1520")
                                .Padding(7f, 5f)
                                .Radius(999f)
                                .Background(service.IsInsightActionBlocked(slot, i)
                                    ? "#F08A8A"
                                    : service.IsInsightActionActive(slot, i)
                                        ? "#F0C36B"
                                        : service.IsInsightActionPresent(slot, i)
                                            ? "#7DE2A7"
                                            : "#7D8EA3"))
                        .Gap(10f))
                .Padding(10f)
                .Radius(14f)
                .Background("#101B26");
        }

        UiElementBuilder heroHeader = Ui.Row(
                Ui.Image(service.GetInsightPortraitIconUri(slot)).Width(74f).Height(74f).FlexShrink(0f),
                Ui.Column(
                        Ui.Row(
                                Ui.Image(service.GetInsightGenreIconUri(slot)).Width(28f).Height(28f).FlexShrink(0f),
                                Ui.Text(service.GetInsightGenreLabel(slot)).FontSize(11f).Bold().Color("#F0C36B"))
                            .Gap(8f)
                            .Align(UiAlignItems.Center),
                        Ui.Text(service.GetTitle(slot)).FontSize(22f).Bold().Color("#F7FBFF"),
                        Ui.Text(service.GetSubtitle(slot)).FontSize(11.5f).Color("#9CB2C6").WhiteSpace(UiWhiteSpace.Normal),
                        Ui.Text(service.GetInsightBody(slot)).FontSize(11.5f).Color("#D9E5F0").WhiteSpace(UiWhiteSpace.Normal),
                        badges.Length == 0 ? Ui.Column() : Ui.Row(badges).Wrap().Gap(8f))
                    .Gap(6f)
                    .FlexGrow(1f),
                Ui.Button(closeText, _ => service.Close(handle))
                    .Id($"entity-info-close-{slot}")
                    .Padding(8f, 6f)
                    .Radius(10f)
                    .Background("#12202C")
                    .Color("#D8E5F3"))
            .Gap(12f)
            .Align(UiAlignItems.Start);

        return Ui.Card(
                heroHeader,
                Ui.Row(stats).Wrap().Gap(10f),
                Ui.Card(tips).Gap(8f).Padding(12f).Radius(16f).Background("#0D1822"),
                Ui.ScrollView(actions).Gap(8f).FlexGrow(1f))
            .Id($"entity-info-insight-{slot}")
            .Gap(12f)
            .Padding(16f)
            .Radius(22f)
            .BackgroundGradient(135f, surface, new UiColor(0x08, 0x11, 0x1A))
            .Border(1f, accent)
            .BackdropBlur(4f)
            .BoxShadow(0f, 12f, 28f, new UiColor(0x00, 0x00, 0x00, 0x55));
    }

    private static UiElementBuilder BuildComponentInspector(EntityInfoPanelService service, int slot, EntityInfoPanelHandle handle)
    {
        int sectionCount = service.GetComponentSectionCount(slot);
        string expandAllText = service.ResolveTextTokenKey("entityinfo.component.expand_all");
        string collapseAllText = service.ResolveTextTokenKey("entityinfo.component.collapse_all");
        string showPrefix = service.ResolveTextTokenKey("entityinfo.component.show_prefix");
        string hidePrefix = service.ResolveTextTokenKey("entityinfo.component.hide_prefix");
        UiElementBuilder[] rows = new UiElementBuilder[Math.Max(1, sectionCount + 1)];
        rows[0] = Ui.Row(
                Ui.Button(expandAllText, _ => service.SetAllComponentsEnabled(handle, true))
                    .Padding(8f, 6f)
                    .Radius(10f)
                    .Background("#213248")
                    .Color("#F5F7FA"),
                Ui.Button(collapseAllText, _ => service.SetAllComponentsEnabled(handle, false))
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
                    $"{(expanded ? hidePrefix : showPrefix)} {service.GetComponentSectionName(slot, i)}",
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
        string sourcesText = service.ResolveTextTokenKey(showSources ? "entityinfo.gas.sources_on" : "entityinfo.gas.sources_off");
        string modifiersText = service.ResolveTextTokenKey(showModifiers ? "entityinfo.gas.modifiers_on" : "entityinfo.gas.modifiers_off");
        UiElementBuilder toggles = Ui.Row(
                BuildToggleButton(
                    sourcesText,
                    showSources,
                    _ => service.UpdateGasDetailFlags(
                        handle,
                        showSources
                            ? flags & ~EntityInfoGasDetailFlags.ShowAttributeAggregateSources
                            : flags | EntityInfoGasDetailFlags.ShowAttributeAggregateSources)),
                BuildToggleButton(
                    modifiersText,
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

    private static UiColor ParseHexColor(string value, UiColor fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        string trimmed = value.Trim();
        if (trimmed.StartsWith('#'))
        {
            trimmed = trimmed[1..];
        }

        if (trimmed.Length == 3)
        {
            trimmed = string.Concat(
                trimmed[0], trimmed[0],
                trimmed[1], trimmed[1],
                trimmed[2], trimmed[2]);
        }

        if (trimmed.Length < 6 ||
            !byte.TryParse(trimmed[..2], System.Globalization.NumberStyles.HexNumber, null, out byte r) ||
            !byte.TryParse(trimmed.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out byte g) ||
            !byte.TryParse(trimmed.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out byte b))
        {
            return fallback;
        }

        return new UiColor(r, g, b);
    }
}
