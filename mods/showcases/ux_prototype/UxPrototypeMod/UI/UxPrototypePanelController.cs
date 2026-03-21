using System;
using System.Collections.Generic;
using CoreInputMod.ViewMode;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Actions;
using UxPrototypeMod.Runtime;

namespace UxPrototypeMod.UI;

internal sealed class UxPrototypePanelController
{
    private readonly UxPrototypeScenarioState _state;
    private ReactivePage<UxPrototypePanelState>? _page;
    private GameEngine? _engine;
    private ViewModeManager? _viewModeManager;

    public UxPrototypePanelController(UxPrototypeScenarioState state)
    {
        _state = state;
    }

    public void MountOrRefresh(UIRoot root, GameEngine engine, ViewModeManager? viewModeManager)
    {
        _engine = engine;
        _viewModeManager = viewModeManager;
        UxPrototypePanelState nextState = _state.BuildSnapshot(engine, viewModeManager);
        if (_page == null)
        {
            var textMeasurer = (IUiTextMeasurer)engine.GetService(CoreServiceKeys.UiTextMeasurer);
            var imageSizeProvider = (IUiImageSizeProvider)engine.GetService(CoreServiceKeys.UiImageSizeProvider);
            _page = new ReactivePage<UxPrototypePanelState>(textMeasurer, imageSizeProvider, nextState, BuildRoot);
        }
        else
        {
            _page.SetState(_ => nextState);
        }

        if (!ReferenceEquals(root.Scene, _page.Scene))
        {
            root.MountScene(_page.Scene);
        }

        root.IsDirty = true;
    }

    public void ClearIfOwned(UIRoot root)
    {
        if (_page != null && ReferenceEquals(root.Scene, _page.Scene))
        {
            root.ClearScene();
        }
    }

    private UiElementBuilder BuildRoot(ReactiveContext<UxPrototypePanelState> context)
    {
        UxPrototypePanelState state = context.State;
        return Ui.Column(
                Ui.Text(" ")
                    .WidthPercent(100f)
                    .HeightPercent(100f)
                    .Absolute(0f, 0f)
                    .Background("#06090D10")
                    .ZIndex(35),
                Ui.Column(
                        BuildTimePanel(state),
                        BuildMissionPanel(state))
                    .Gap(8f)
                    .Width(352f)
                    .Absolute(16f, 16f),
                Ui.Row(BuildEventChips(state))
                    .Width(540f)
                    .Absolute(392f, 16f)
                    .Justify(UiJustifyContent.Center),
                Ui.Column(
                        BuildResourcePanel(state),
                        state.ResourceDropdownOpen ? BuildResourceDropdown(state) : Ui.Column(),
                        BuildMinimapPanel(state))
                    .Gap(8f)
                    .Width(312f)
                    .Absolute(952f, 16f),
                Ui.Column(
                        BuildRosterSummaryPanel(state),
                        state.RosterCollapsed ? Ui.Column() : BuildRosterPanel(state))
                    .Gap(8f)
                    .Width(320f)
                    .Absolute(16f, 444f),
                BuildGlobalPanel(state)
                    .Width(280f)
                    .Absolute(980f, 226f),
                BuildProductionPanel(state)
                    .Width(280f)
                    .Absolute(980f, 502f),
                BuildTooltipBubble(state)
                    .Absolute(458f, 432f),
                BuildCommandDeckArea(state)
                    .Absolute(244f, 512f),
                BuildSpeedPanel(state)
                    .Absolute(1178f, 520f),
                state.SettingsOpen ? BuildSettingsModal(state) : Ui.Column())
            .WidthPercent(100f)
            .HeightPercent(100f)
            .Absolute(0f, 0f)
            .ZIndex(40);
    }

    private UiElementBuilder BuildTopBand(UxPrototypePanelState state)
    {
        return Ui.Row(
                Ui.Column(
                        BuildTimePanel(state),
                        BuildMissionPanel(state))
                    .Gap(8f)
                    .Width(352f),
                Ui.Row(BuildEventChips(state))
                    .Justify(UiJustifyContent.Center)
                    .Align(UiAlignItems.Start)
                    .FlexGrow(1f),
                Ui.Column(
                        BuildResourcePanel(state),
                        state.ResourceDropdownOpen ? BuildResourceDropdown(state) : Ui.Column(),
                        BuildMinimapPanel(state))
                    .Gap(8f)
                    .Width(312f))
            .Justify(UiJustifyContent.SpaceBetween)
            .Align(UiAlignItems.Start)
            .Gap(16f)
            .Padding(16f)
            .FlexShrink(0f);
    }

    private UiElementBuilder BuildMiddleBand(UxPrototypePanelState state)
    {
        return Ui.Row(
                Ui.Column().FlexGrow(1f),
                Ui.Column(
                        BuildGlobalPanel(state),
                        BuildProductionPanel(state))
                    .Gap(12f)
                    .Width(320f))
            .FlexGrow(1f)
            .Align(UiAlignItems.Center)
            .Padding(16f, 0f);
    }

    private UiElementBuilder BuildBottomBand(UxPrototypePanelState state)
    {
        return Ui.Row(
                Ui.Column(
                        BuildFactionPanel(state),
                        BuildRosterSummaryPanel(state),
                        state.RosterCollapsed ? Ui.Column() : BuildRosterPanel(state))
                    .Gap(10f)
                    .Width(360f),
                Ui.Column(
                        BuildTooltipPanel(state),
                        BuildControlDeck(state))
                    .Gap(10f)
                    .FlexGrow(1f)
                    .Align(UiAlignItems.Center),
                BuildSpeedPanel(state))
            .Gap(16f)
            .Align(UiAlignItems.End)
            .Padding(16f);
    }

    private UiElementBuilder BuildTimePanel(UxPrototypePanelState state)
    {
        return BuildGlassPanel(
            Ui.Row(
                    BuildDialMetric("YEAR", state.Year.ToString(), "#D4AF37"),
                    BuildDialMetric("MONTH", $"{state.Month:D2}", "#F59E0B"),
                    BuildDialMetric("DAY", $"{state.Day:D3}", "#EFB94D"),
                    BuildDialMetric("PHASE", state.MoonPhaseLabel, "#94A3B8"))
                .Gap(6f),
            Ui.Row(
                    Ui.Text(state.EraLabel)
                        .FontSize(18f)
                        .Bold()
                        .Color("#F6D77C"),
                    Ui.Text(state.TimeSummary)
                        .FontSize(12f)
                        .Color("#D7DEE6"))
                .Justify(UiJustifyContent.SpaceBetween),
            Ui.Row(
                    Ui.Text(state.GanzhiLabel).FontSize(11f).Color("#94A3B8"),
                    Ui.Text($"Day {MathF.Round(state.DayProgress * 100f):0}%").FontSize(11f).Color("#7DD3FC"))
                .Justify(UiJustifyContent.SpaceBetween))
            .Width(352f)
            .Padding(10f, 8f);
    }

    private UiElementBuilder BuildMissionPanel(UxPrototypePanelState state)
    {
        return BuildGlassPanel(
            "Current Objective",
            Ui.Text(state.Objective)
                .FontSize(12f)
                .Color("#E5E7EB")
                .WhiteSpace(UiWhiteSpace.Normal),
            Ui.Text("Hold the corridor, keep the lanes online, and pressure the eastern flank.")
                .FontSize(11f)
                .Color("#94A3B8")
                .WhiteSpace(UiWhiteSpace.Normal))
            .Width(352f);
    }

    private UiElementBuilder BuildEventChips(UxPrototypePanelState state)
    {
        var chips = new List<UiElementBuilder>();
        for (int i = 0; i < state.EventQueue.Count; i++)
        {
            string accent = i switch
            {
                0 => "#C94F46",
                1 => "#3B82F6",
                _ => "#D4AF37"
            };

            chips.Add(
                Ui.Row(
                        Ui.Text("*").FontSize(12f).Color(accent),
                        Ui.Column(
                                Ui.Text(i == 0 ? "EVENT" : "NOTICE").FontSize(10f).Bold().Color("#F8FAFC"),
                                Ui.Text(state.EventQueue[i]).FontSize(11f).Color("#CBD5E1").WhiteSpace(UiWhiteSpace.Normal))
                            .Gap(2f))
                    .Gap(8f)
                    .Padding(10f, 8f)
                    .Radius(20f)
                    .Background("#E0101010")
                    .Border(1f, Color("#33FFFFFF"))
                    .BackdropBlur(6f)
                    .BoxShadow(0f, 4f, 12f, Color("#55000000")));
        }

        return Ui.Row(chips.ToArray())
            .Gap(12f)
            .Wrap()
            .Justify(UiJustifyContent.Center);
    }

    private UiElementBuilder BuildResourcePanel(UxPrototypePanelState state)
    {
        return BuildGlassPanel(
            "Faction Stockpile",
            Ui.Row(
                    BuildResourceTicker("FOOD", state.Food, state.FoodDelta, "#10B981"),
                    BuildResourceTicker("IRON", state.Iron, state.IronDelta, "#CBD5E1"))
                .Gap(8f),
            Ui.Row(
                    BuildResourceTicker("CREDITS", state.Credits, state.CreditsDelta, "#D4AF37"),
                    BuildResourceTicker("ENERGY", state.Energy, state.EnergyDelta, "#F97316"))
                .Gap(8f),
            Ui.Row(
                    Ui.Text(state.ActiveModeLabel).FontSize(11f).Bold().Color("#F6D77C"),
                    Ui.Button(state.ResourceDropdownOpen ? "Hide Detail" : "Show Detail", _ => _state.ToggleResourceDropdown())
                        .Padding(8f, 6f)
                        .Radius(4f)
                        .Background("#1B222A")
                        .Border(1f, Color("#33FFFFFF"))
                        .Color("#E5E7EB"))
                .Justify(UiJustifyContent.SpaceBetween))
            .Width(312f);
    }

    private UiElementBuilder BuildResourceDropdown(UxPrototypePanelState state)
    {
        var rows = new List<UiElementBuilder>();
        for (int i = 0; i < state.ResourceBreakdown.Count; i++)
        {
            ResourceBreakdownEntry row = state.ResourceBreakdown[i];
            rows.Add(
                Ui.Row(
                        Ui.Text(row.Label).FontSize(11f).Color("#F8FAFC"),
                        Ui.Text($"x{row.Count}").FontSize(11f).Color("#F6D77C"),
                        Ui.Text(row.Summary).FontSize(10f).Color("#94A3B8"))
                    .Justify(UiJustifyContent.SpaceBetween));
        }

        return BuildGlassPanel(
            Ui.Text("RESOURCE BUILDINGS").FontSize(10f).Bold().Color("#F6D77C"),
            Ui.Column(rows.ToArray()).Gap(6f))
            .Width(312f)
            .Padding(10f, 8f);
    }

    private UiElementBuilder BuildMinimapPanel(UxPrototypePanelState state)
    {
        return BuildGlassPanel(
            Ui.Row(
                    Ui.Text("MINIMAP").FontSize(11f).Bold().Color("#F8FAFC"),
                    Ui.Text(state.MinimapSelectedLabel).FontSize(10f).Color("#F6D77C"))
                .Justify(UiJustifyContent.SpaceBetween),
            BuildMinimapField(state))
            .Width(312f)
            .Padding(10f, 8f);
    }

    private UiElementBuilder BuildMinimapField(UxPrototypePanelState state)
    {
        var children = new List<UiElementBuilder>
        {
            Ui.Text(" ")
                .Width(192f)
                .Height(192f)
                .Background("#CC081018")
                .Border(1f, Color("#44D4AF37"))
                .Radius(4f)
        };

        children.Add(Ui.Text(" ")
            .Absolute(96f, 0f)
            .Width(1f)
            .Height(192f)
            .Background("#22FFFFFF"));
        children.Add(Ui.Text(" ")
            .Absolute(0f, 96f)
            .Width(192f)
            .Height(1f)
            .Background("#22FFFFFF"));

        for (int i = 0; i < state.MinimapDots.Count; i++)
        {
            MinimapDot dot = state.MinimapDots[i];
            float left = 8f + Math.Clamp(dot.X, 0f, 1f) * 176f;
            float top = 8f + Math.Clamp(dot.Y, 0f, 1f) * 176f;
            children.Add(
                Ui.Text(dot.Selected ? "O" : ".")
                    .FontSize(dot.Selected ? 13f : 11f)
                    .Color(dot.Selected ? "#F6D77C" : dot.Color)
                    .Absolute(left, top)
                    .ZIndex(dot.Selected ? 4 : 3));
        }

        return Ui.Column(children.ToArray())
            .Width(192f)
            .Height(192f)
            .Overflow(UiOverflow.Hidden);
    }

    private UiElementBuilder BuildFactionPanel(UxPrototypePanelState state)
    {
        var tabs = new List<UiElementBuilder>();
        for (int i = 0; i < UxPrototypeIds.FactionTabs.Length; i++)
        {
            string tab = UxPrototypeIds.FactionTabs[i];
            tabs.Add(BuildTabButton(CultureLabel(tab), string.Equals(state.FactionTab, tab, StringComparison.OrdinalIgnoreCase), _ => _state.SetFactionTab(tab)));
        }

        var rows = new List<UiElementBuilder>();
        for (int i = 0; i < state.FactionOperations.Count; i++)
        {
            FactionOperationEntry item = state.FactionOperations[i];
            rows.Add(
                Ui.Row(
                        Ui.Text(item.Label).FontSize(11f).Bold().Color("#F8FAFC"),
                        Ui.Text(item.Summary).FontSize(10f).Color("#94A3B8").WhiteSpace(UiWhiteSpace.Normal))
                    .Justify(UiJustifyContent.SpaceBetween));
        }

        return BuildGlassPanel(
            "Faction Console",
            Ui.Row(tabs.ToArray()).Wrap().Gap(6f),
            Ui.Column(rows.ToArray()).Gap(6f))
            .Width(360f);
    }

    private UiElementBuilder BuildRosterSummaryPanel(UxPrototypePanelState state)
    {
        return BuildGlassPanel(
            Ui.Row(
                    Ui.Text("IN SERVICE").FontSize(11f).Bold().Color("#F6D77C"),
                    Ui.Row(
                            BuildTabButton("Grid", state.GridLayout, _ => _state.ToggleRosterLayout()),
                            BuildTabButton("List", !state.GridLayout, _ => _state.ToggleRosterLayout()),
                            BuildTabButton(state.RosterCollapsed ? "Expand" : "Collapse", false, _ => _state.ToggleRosterCollapsed()))
                        .Gap(6f))
                .Justify(UiJustifyContent.SpaceBetween),
            Ui.Text(state.RosterSummary).FontSize(12f).Color("#E5E7EB").WhiteSpace(UiWhiteSpace.Normal))
            .Width(360f)
            .Padding(10f, 8f);
    }

    private UiElementBuilder BuildRosterPanel(UxPrototypePanelState state)
    {
        var tabs = new List<UiElementBuilder>();
        for (int i = 0; i < UxPrototypeIds.RosterTabs.Length; i++)
        {
            string tab = UxPrototypeIds.RosterTabs[i];
            tabs.Add(BuildTabButton(CultureLabel(tab), string.Equals(state.RosterTab, tab, StringComparison.OrdinalIgnoreCase), _ => _state.SetRosterTab(tab)));
        }

        var rows = new List<UiElementBuilder>();
        for (int i = 0; i < state.RosterRows.Count; i++)
        {
            RosterRow row = state.RosterRows[i];
            rows.Add(
                Ui.Row(
                        Ui.Text(row.Label).FontSize(12f).Color("#F8FAFC"),
                        Ui.Text($"x{row.Count}").FontSize(12f).Bold().Color("#F6D77C"),
                        Ui.Text(row.Summary).FontSize(11f).Color("#94A3B8"))
                    .Justify(UiJustifyContent.SpaceBetween));
        }

        return BuildGlassPanel(
            "Roster",
            Ui.Row(tabs.ToArray()).Wrap().Gap(6f),
            Ui.ScrollView(Ui.Column(rows.ToArray()).Gap(8f))
                .Height(168f))
            .Width(360f);
    }

    private UiElementBuilder BuildGlobalPanel(UxPrototypePanelState state)
    {
        var tabs = new List<UiElementBuilder>();
        for (int i = 0; i < UxPrototypeIds.GlobalTabs.Length; i++)
        {
            string tab = UxPrototypeIds.GlobalTabs[i];
            tabs.Add(BuildTabButton(CultureLabel(tab), string.Equals(state.GlobalTab, tab, StringComparison.OrdinalIgnoreCase), _ => _state.SetGlobalTab(tab)));
        }

        return BuildGlassPanel(
            "Global Panel",
            Ui.Row(
                    Ui.Row(tabs.ToArray()).Wrap().Gap(6f).FlexGrow(1f),
                    Ui.Button(state.GlobalExpanded ? "Hide" : "Show", _ => _state.ToggleGlobalExpanded())
                        .Padding(6f, 4f)
                        .Radius(4f)
                        .Background("#151B21")
                        .Border(1f, Color("#3393A2B8"))
                        .Color("#E5E7EB"))
                .Justify(UiJustifyContent.SpaceBetween),
            state.GlobalExpanded ? BuildGlobalActionGrid(state) : Ui.Column())
            .Width(280f)
            .Padding(10f, 8f);
    }

    private UiElementBuilder BuildProductionPanel(UxPrototypePanelState state)
    {
        var rows = new List<UiElementBuilder>();
        for (int i = 0; i < state.ProductionRows.Count; i++)
        {
            ProductionRow row = state.ProductionRows[i];
            rows.Add(
                Ui.Row(
                        Ui.Text(row.Label).FontSize(11f).Bold().Color("#F8FAFC"),
                        Ui.Text(row.Summary).FontSize(10f).Color("#94A3B8").WhiteSpace(UiWhiteSpace.Normal))
                    .Justify(UiJustifyContent.SpaceBetween));
        }

        return BuildGlassPanel(
            "Production Lines",
            Ui.Column(rows.ToArray()).Gap(6f))
            .Width(280f)
            .Padding(10f, 8f);
    }

    private UiElementBuilder BuildTooltipPanel(UxPrototypePanelState state)
    {
        return BuildGlassPanel(
            "Tooltip",
            Ui.Text(state.TooltipTitle).FontSize(14f).Bold().Color("#F6D77C"),
            Ui.Text(state.TooltipBody).FontSize(12f).Color("#CBD5E1").WhiteSpace(UiWhiteSpace.Normal))
            .Width(628f)
            .Padding(10f, 8f);
    }

    private UiElementBuilder BuildControlDeck(UxPrototypePanelState state)
    {
        return Ui.Row(
                BuildPreferencesPanel(state),
                BuildSelectionDock(state))
            .Gap(10f)
            .Align(UiAlignItems.End);
    }

    private UiElementBuilder BuildPreferencesPanel(UxPrototypePanelState state)
    {
        if (!state.PreferencesOpen)
        {
            return BuildGlassPanel(
                Ui.Button("Prefs", _ => _state.TogglePreferences())
                    .Padding(8f, 6f)
                    .Radius(4f)
                    .Background("#171D24")
                    .Border(1f, Color("#447E8FA6"))
                    .Color("#F8FAFC"))
                .Width(72f)
                .Padding(8f, 6f);
        }

        return BuildGlassPanel(
            Ui.Row(
                    Ui.Text("PREFERENCES").FontSize(11f).Bold().Color("#F6D77C"),
                    Ui.Button("Hide", _ => _state.TogglePreferences())
                        .Padding(6f, 4f)
                        .Radius(4f)
                        .Background("#1B222A")
                        .Border(1f, Color("#3393A2B8"))
                        .Color("#E5E7EB"))
                .Justify(UiJustifyContent.SpaceBetween),
            BuildToggleRow("Batch Cast", state.SmartCastBatch, _ => _state.ToggleSmartCastBatch()),
            BuildToggleRow("Quick Release", state.QuickCastOnRelease, _ => _state.ToggleQuickCastOnRelease()),
            BuildToggleRow("Target Lines", state.ShowTargetLines, _ => _state.ToggleTargetLines()),
            BuildToggleRow("Range", state.ShowRangeIndicators, _ => _state.ToggleRangeIndicators()),
            BuildToggleRow("Tooltips", state.ShowTooltips, _ => _state.ToggleTooltips()),
            BuildModeStrip(state),
            Ui.Button("Settings", _ => _state.ToggleSettings())
                .Padding(8f, 6f)
                .Radius(4f)
                .Background("#251E10")
                .Border(1f, Color("#44D4AF37"))
                .Color("#F8FAFC"))
            .Width(150f)
            .Padding(10f, 8f);
    }

    private UiElementBuilder BuildSelectionDock(UxPrototypePanelState state)
    {
        return BuildGlassPanel(
            Ui.Row(
                    BuildPortraitPanel(state),
                    Ui.Column(
                            BuildSelectionHeader(state),
                            BuildQueueEntries(state.SelectedQueue),
                            Ui.Text("Commands route through the shared EntityCommandPanel runtime.")
                                .FontSize(10f)
                                .Color("#7DD3FC")
                                .WhiteSpace(UiWhiteSpace.Normal),
                            BuildModeStrip(state),
                            BuildSelectionFooter(state))
                        .Gap(8f)
                        .FlexGrow(1f))
                .Gap(10f)
                .Align(UiAlignItems.End))
            .Width(628f)
            .Padding(12f, 10f);
    }

    private UiElementBuilder BuildPortraitPanel(UxPrototypePanelState state)
    {
        string portraitTitle = string.Equals(state.SelectedEntityType, "None", StringComparison.OrdinalIgnoreCase)
            ? "COMMAND"
            : state.SelectedEntityType.ToUpperInvariant();

        return Ui.Column(
                Ui.Text(portraitTitle)
                    .FontSize(15f)
                    .Bold()
                    .Color("#F6D77C"),
                Ui.Text(state.SelectedEntityLabel)
                    .FontSize(11f)
                    .Color("#E5E7EB")
                    .WhiteSpace(UiWhiteSpace.Normal))
            .Justify(UiJustifyContent.SpaceBetween)
            .Width(96f)
            .Height(124f)
            .Padding(10f, 8f)
            .Background("#EE101010")
            .Border(1f, Color("#55D4AF37"))
            .Radius(4f)
            .BackdropBlur(6f)
            .BoxShadow(0f, 6f, 18f, Color("#66000000"));
    }

    private UiElementBuilder BuildSelectionHeader(UxPrototypePanelState state)
    {
        return Ui.Row(
                Ui.Column(
                        Ui.Text(state.SelectedEntityLabel).FontSize(22f).Bold().Color("#F8FAFC"),
                        Ui.Text(state.SelectedEntityType).FontSize(11f).Bold().Color("#F6D77C"))
                    .Gap(3f),
                Ui.Column(
                        Ui.Text($"Mode: {state.ActiveModeLabel}").FontSize(11f).Color("#CBD5E1"),
                        Ui.Text($"Rally: {state.RallySummary}").FontSize(10f).Color("#94A3B8").WhiteSpace(UiWhiteSpace.Normal))
                    .Gap(3f))
            .Justify(UiJustifyContent.SpaceBetween);
    }

    private UiElementBuilder BuildSkillGrid(UxPrototypePanelState state)
    {
        if (state.SelectedSkills.Count == 0)
        {
            return Ui.Column();
        }

        var rows = new List<UiElementBuilder>();
        for (int i = 0; i < state.SelectedSkills.Count; i += 4)
        {
            var rowChildren = new List<UiElementBuilder>();
            for (int j = i; j < Math.Min(i + 4, state.SelectedSkills.Count); j++)
            {
                rowChildren.Add(BuildSkillButton(state.SelectedSkills[j]));
            }

            rows.Add(Ui.Row(rowChildren.ToArray()).Gap(6f));
        }

        return Ui.Column(rows.ToArray()).Gap(6f);
    }

    private UiElementBuilder BuildSkillButton(SkillEntry skill)
    {
        string background = skill.Active
            ? "#EE2A1D10"
            : skill.Enabled
                ? "#CC101010"
                : "#AA141414";

        var button = Ui.Column(
                Ui.Row(
                        Ui.Text(skill.Hotkey).FontSize(10f).Bold().Color(skill.Enabled ? "#0B0B0B" : "#E5E7EB")
                            .Background(skill.Enabled ? "#F6D77C" : "#475569")
                            .Padding(4f, 1f)
                            .Radius(3f),
                        Ui.Text(skill.CountText).FontSize(9f).Color(skill.Active ? "#F6D77C" : "#7DD3FC"))
                    .Justify(UiJustifyContent.SpaceBetween),
                Ui.Text(CompactSkillLabel(skill.Label))
                    .FontSize(9f)
                    .Bold()
                    .Color("#F8FAFC")
                    .WhiteSpace(UiWhiteSpace.Normal))
            .Width(50f)
            .Height(50f)
            .Padding(4f)
            .Gap(2f)
            .Background(background)
            .Border(1f, Color(skill.Active ? "#66D4AF37" : "#33FFFFFF"))
            .Radius(4f)
            .BackdropBlur(4f);

        if (skill.Enabled)
        {
            button.OnClick(_ => TriggerAction(skill.ActionId));
        }

        return button;
    }

    private UiElementBuilder BuildModeStrip(UxPrototypePanelState state)
    {
        return Ui.Row(
                BuildModeButton("Play", string.Equals(state.ActiveModeId, UxPrototypeIds.PlayModeId, StringComparison.OrdinalIgnoreCase), _ => SwitchMode(UxPrototypeIds.PlayModeId)),
                BuildModeButton("Road", string.Equals(state.ActiveModeId, UxPrototypeIds.RoadEditorModeId, StringComparison.OrdinalIgnoreCase), _ => SwitchMode(UxPrototypeIds.RoadEditorModeId)),
                BuildModeButton("Obstacle", string.Equals(state.ActiveModeId, UxPrototypeIds.ObstacleEditorModeId, StringComparison.OrdinalIgnoreCase), _ => SwitchMode(UxPrototypeIds.ObstacleEditorModeId)),
                BuildModeButton("Navmesh", string.Equals(state.ActiveModeId, UxPrototypeIds.NavmeshModeId, StringComparison.OrdinalIgnoreCase), _ => SwitchMode(UxPrototypeIds.NavmeshModeId)))
            .Gap(6f)
            .Wrap();
    }

    private UiElementBuilder BuildSelectionFooter(UxPrototypePanelState state)
    {
        return Ui.Row(
                Ui.Text(state.SelectedSummary).FontSize(11f).Color("#CBD5E1").WhiteSpace(UiWhiteSpace.Normal).FlexGrow(1f),
                Ui.Text($"Queue Owner: {state.SelectedQueue.Header}").FontSize(11f).Color("#F6D77C"))
            .Justify(UiJustifyContent.SpaceBetween);
    }

    private UiElementBuilder BuildSpeedPanel(UxPrototypePanelState state)
    {
        return BuildGlassPanel(
            Ui.Column(
                    BuildTimeControlButton(state.Paused ? "RESUME" : "PAUSE", state.Paused, _ => _state.TogglePause()),
                    BuildTimeControlButton("1X", state.SpeedIndex == 0, _ => _state.SetSpeed(0)),
                    BuildTimeControlButton("2X", state.SpeedIndex == 1, _ => _state.SetSpeed(1)),
                    BuildTimeControlButton("5X", state.SpeedIndex == 2, _ => _state.SetSpeed(2)))
                .Gap(6f)
                .Align(UiAlignItems.Stretch))
            .Width(86f)
            .Padding(10f, 8f);
    }

    private UiElementBuilder BuildSettingsModal(UxPrototypePanelState state)
    {
        return Ui.Row(
                Ui.Column(
                        Ui.Text("Settings").FontSize(22f).Bold().Color("#F8FAFC"),
                        Ui.Text("This port keeps the prototype surface feel while routing through Ludots-native camera, selection, queue, and presentation systems.")
                            .FontSize(12f)
                            .Color("#CBD5E1")
                            .WhiteSpace(UiWhiteSpace.Normal),
                        Ui.Row(
                                Ui.Button("Close", _ => _state.ToggleSettings())
                                    .Padding(10f, 8f)
                                    .Radius(4f)
                                    .Background("#251E10")
                                    .Border(1f, Color("#44D4AF37"))
                                    .Color("#F8FAFC"),
                                Ui.Button("Save Layout", _ => _state.SaveLevel())
                                    .Padding(10f, 8f)
                                    .Radius(4f)
                                    .Background("#1B222A")
                                    .Border(1f, Color("#33FFFFFF"))
                                    .Color("#E5E7EB"))
                            .Gap(8f))
                    .Gap(12f)
                    .Width(460f)
                    .Padding(18f)
                    .Background("#F0101010")
                    .Border(1f, Color("#55D4AF37"))
                    .Radius(6f)
                    .BackdropBlur(10f)
                    .BoxShadow(0f, 10f, 30f, Color("#88000000")))
            .WidthPercent(100f)
            .HeightPercent(100f)
            .Absolute(0f, 0f)
            .Justify(UiJustifyContent.Center)
            .Align(UiAlignItems.Center)
            .Background("#7A000000")
            .ZIndex(120);
    }

    private UiElementBuilder BuildQueueEntries(ProductionQueueSnapshot queue)
    {
        if (queue.Entries.Count == 0)
        {
            return Ui.Text("Queue idle")
                .FontSize(10f)
                .Color("#94A3B8")
                .WhiteSpace(UiWhiteSpace.Normal);
        }

        var entries = new List<UiElementBuilder>();
        for (int i = 0; i < queue.Entries.Count; i++)
        {
            ProductionQueueEntry entry = queue.Entries[i];
            var card = Ui.Column(
                    Ui.Text(entry.Label).FontSize(10f).Bold().Color("#F8FAFC").WhiteSpace(UiWhiteSpace.Normal),
                    Ui.Text($"{entry.ProgressPercent}%").FontSize(9f).Color("#F6D77C"))
                .Gap(3f)
                .Width(68f)
                .Height(34f)
                .Padding(6f, 4f)
                .Background("#CC101010")
                .Border(1f, Color("#33FFFFFF"))
                .Radius(4f);

            if (entry.CanCancel)
            {
                card.OnClick(_ => CancelQueueEntry(entry.QueueToken));
            }

            entries.Add(card);
        }

        return Ui.Row(entries.ToArray()).Gap(4f).Wrap();
    }

    private UiElementBuilder BuildCompactEntry(string label, string summary, bool active, Action<UiActionContext> onClick)
    {
        return Ui.Row(
                Ui.Column(
                        Ui.Text(label).FontSize(12f).Bold().Color("#F8FAFC"),
                        Ui.Text(summary).FontSize(10f).Color("#94A3B8").WhiteSpace(UiWhiteSpace.Normal))
                    .Gap(3f)
                    .FlexGrow(1f),
                Ui.Text(active ? "ACTIVE" : "READY")
                    .FontSize(9f)
                    .Bold()
                    .Color(active ? "#F6D77C" : "#CBD5E1"))
            .Gap(8f)
            .Padding(10f, 8f)
            .Background(active ? "#251E10" : "#CC101010")
            .Border(1f, Color(active ? "#44D4AF37" : "#33FFFFFF"))
            .Radius(4f)
            .OnClick(onClick);
    }

    private UiElementBuilder BuildDialMetric(string label, string value, string accent)
    {
        return Ui.Column(
                Ui.Text(label).FontSize(9f).Color("#94A3B8"),
                Ui.Text(value).FontSize(11f).Bold().Color("#F8FAFC"),
                Ui.Text("#").FontSize(10f).Color(accent))
            .Gap(2f)
            .Width(78f)
            .Padding(8f, 6f)
            .Background("#CC101010")
            .Border(1f, Color("#33FFFFFF"))
            .Radius(4f);
    }

    private UiElementBuilder BuildResourceTicker(string label, int value, int delta, string accent)
    {
        return Ui.Column(
                Ui.Text(label).FontSize(9f).Color("#94A3B8"),
                Ui.Text(value.ToString()).FontSize(15f).Bold().Color("#F8FAFC"),
                Ui.Text($"+{delta}/d").FontSize(10f).Color(accent))
            .Gap(3f)
            .Width(140f)
            .Padding(8f, 6f)
            .Background("#CC101010")
            .Border(1f, Color("#33FFFFFF"))
            .Radius(4f);
    }

    private UiElementBuilder BuildToggleRow(string label, bool value, Action<UiActionContext> onClick)
    {
        return Ui.Row(
                Ui.Text(label).FontSize(11f).Color("#E5E7EB"),
                Ui.Button(value ? "ON" : "OFF", onClick)
                    .Padding(6f, 4f)
                    .Radius(4f)
                    .Background(value ? "#251E10" : "#1B222A")
                    .Border(1f, Color(value ? "#44D4AF37" : "#3393A2B8"))
                    .Color(value ? "#F6D77C" : "#CBD5E1"))
            .Justify(UiJustifyContent.SpaceBetween);
    }

    private UiElementBuilder BuildTabButton(string label, bool active, Action<UiActionContext> onClick)
    {
        return Ui.Button(label, onClick)
            .Padding(8f, 6f)
            .Radius(4f)
            .Background(active ? "#251E10" : "#171D24")
            .Border(1f, Color(active ? "#44D4AF37" : "#3393A2B8"))
            .Color(active ? "#F6D77C" : "#E5E7EB");
    }

    private UiElementBuilder BuildModeButton(string label, bool active, Action<UiActionContext> onClick)
    {
        return Ui.Button(label, onClick)
            .Padding(8f, 6f)
            .Radius(4f)
            .Background(active ? "#251E10" : "#151B21")
            .Border(1f, Color(active ? "#55D4AF37" : "#33879AB3"))
            .Color(active ? "#F6D77C" : "#E5E7EB");
    }

    private UiElementBuilder BuildTimeControlButton(string label, bool active, Action<UiActionContext> onClick)
    {
        return Ui.Button(label, onClick)
            .Padding(8f, 10f)
            .Radius(4f)
            .Background(active ? "#251E10" : "#151B21")
            .Border(1f, Color(active ? "#55D4AF37" : "#33879AB3"))
            .Color(active ? "#F6D77C" : "#E5E7EB");
    }

    private UiElementBuilder BuildGlobalActionGrid(UxPrototypePanelState state)
    {
        var rows = new List<UiElementBuilder>();
        for (int i = 0; i < state.GlobalEntries.Count; i += 2)
        {
            var cards = new List<UiElementBuilder>();
            for (int j = i; j < Math.Min(i + 2, state.GlobalEntries.Count); j++)
            {
                cards.Add(BuildGlobalActionCard(state.GlobalEntries[j], state.GlobalTab));
            }

            rows.Add(Ui.Row(cards.ToArray()).Gap(6f));
        }

        return Ui.Column(rows.ToArray()).Gap(6f);
    }

    private UiElementBuilder BuildGlobalActionCard(GlobalEntry entry, string activeTab)
    {
        return Ui.Column(
                Ui.Text(entry.Label).FontSize(11f).Bold().Color("#F8FAFC").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text(entry.Summary).FontSize(9f).Color("#94A3B8").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text(entry.Active ? "ACTIVE" : "READY").FontSize(8f).Bold().Color(entry.Active ? "#F6D77C" : "#CBD5E1"))
            .Gap(3f)
            .Width(122f)
            .Height(62f)
            .Padding(8f, 6f)
            .Background(entry.Active ? "#33251E10" : "#CC101010")
            .Border(1f, Color(entry.Active ? "#55D4AF37" : "#33879AB3"))
            .Radius(4f)
            .OnClick(_ => HandleGlobalAction(activeTab, entry.ActionId));
    }

    private UiElementBuilder BuildTooltipBubble(UxPrototypePanelState state)
    {
        return Ui.Column(
                Ui.Text(state.TooltipTitle).FontSize(14f).Bold().Color("#F6D77C"),
                Ui.Text(state.TooltipBody).FontSize(11f).Color("#CBD5E1").WhiteSpace(UiWhiteSpace.Normal))
            .Width(260f)
            .Padding(10f, 8f)
            .Gap(4f)
            .Background("#F4000000")
            .Border(1f, Color("#66D4AF37"))
            .Radius(4f)
            .BackdropBlur(10f)
            .BoxShadow(0f, 10f, 20f, Color("#66000000"));
    }

    private UiElementBuilder BuildCommandDeckArea(UxPrototypePanelState state)
    {
        return Ui.Row(
                BuildPreferencesPanel(state),
                BuildPortraitPanel(state),
                Ui.Column(
                        BuildQueueEntries(state.SelectedQueue),
                        Ui.Text("Shared command panel follows the current selection.")
                            .FontSize(10f)
                            .Color("#7DD3FC")
                            .WhiteSpace(UiWhiteSpace.Normal),
                        BuildSelectionSummaryStrip(state))
                    .Gap(6f)
                    .Width(320f)
                    .Padding(8f, 8f)
                    .Background("#D0101010")
                    .Border(1f, Color("#33879AB3"))
                    .Radius(4f)
                    .BackdropBlur(8f)
                    .BoxShadow(0f, 6f, 20f, Color("#66000000")))
            .Gap(8f)
            .Align(UiAlignItems.End);
    }

    private UiElementBuilder BuildCommandPanel(UxPrototypePanelState state)
    {
        return Ui.Column(
                BuildQueueEntries(state.SelectedQueue),
                BuildSkillGrid(state),
                BuildSelectionSummaryStrip(state))
            .Gap(6f)
            .Width(320f)
            .Padding(8f, 8f)
            .Background("#D0101010")
            .Border(1f, Color("#33879AB3"))
            .Radius(4f)
            .BackdropBlur(8f)
            .BoxShadow(0f, 6f, 20f, Color("#66000000"));
    }

    private UiElementBuilder BuildSelectionSummaryStrip(UxPrototypePanelState state)
    {
        return Ui.Row(
                Ui.Column(
                        Ui.Text(state.SelectedEntityLabel).FontSize(13f).Bold().Color("#F8FAFC").WhiteSpace(UiWhiteSpace.Normal),
                        Ui.Text(state.SelectedEntityType).FontSize(9f).Color("#F6D77C"))
                    .Gap(2f)
                    .FlexGrow(1f),
                Ui.Column(
                        Ui.Text($"Queue {state.SelectedQueue.Header}").FontSize(9f).Color("#CBD5E1").WhiteSpace(UiWhiteSpace.Normal),
                        Ui.Text(state.ActiveModeLabel).FontSize(9f).Color("#7DD3FC"))
                    .Gap(2f))
            .Justify(UiJustifyContent.SpaceBetween)
            .Align(UiAlignItems.Center)
            .Padding(6f, 5f)
            .Background("#E0181D24")
            .Border(1f, Color("#33879AB3"))
            .Radius(4f);
    }

    private UiElementBuilder BuildGlassPanel(params UiElementBuilder[] children)
    {
        return BuildGlassPanel(null, children);
    }

    private UiElementBuilder BuildGlassPanel(string? title = null, params UiElementBuilder[] children)
    {
        var content = new List<UiElementBuilder>();
        if (!string.IsNullOrWhiteSpace(title))
        {
            content.Add(Ui.Text(title).FontSize(11f).Bold().Color("#F6D77C"));
        }

        content.AddRange(children);
        return Ui.Card(content.ToArray())
            .Padding(12f)
            .Gap(8f)
            .Radius(4f)
            .Background("#D40B0E13")
            .Border(1f, Color("#33879AB3"))
            .BackdropBlur(10f)
            .BoxShadow(0f, 8f, 24f, Color("#66000000"));
    }

    private void SwitchMode(string modeId)
    {
        _state.SwitchMode(modeId);
        _viewModeManager?.SwitchTo(modeId);
    }

    private void HandleGlobalAction(string tab, string actionId)
    {
        if (string.Equals(tab, UxPrototypeIds.GlobalEditors, StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(actionId, "editor:road", StringComparison.OrdinalIgnoreCase))
            {
                SwitchMode(UxPrototypeIds.RoadEditorModeId);
            }
            else if (string.Equals(actionId, "editor:obstacle", StringComparison.OrdinalIgnoreCase))
            {
                SwitchMode(UxPrototypeIds.ObstacleEditorModeId);
            }
            else if (string.Equals(actionId, "editor:navmesh", StringComparison.OrdinalIgnoreCase))
            {
                SwitchMode(UxPrototypeIds.NavmeshModeId);
                _state.StartNavmeshBake();
            }

            return;
        }

        TriggerAction(actionId);
    }

    private void TriggerAction(string actionId)
    {
        if (_engine == null)
        {
            return;
        }

        _state.TriggerAction(_engine, actionId);
    }

    private void CancelQueueEntry(string queueToken)
    {
        if (_engine == null)
        {
            return;
        }

        _state.CancelQueueEntry(_engine, queueToken);
    }

    private static UiColor Color(string hex)
    {
        if (!UiColor.TryParse(hex, out UiColor color))
        {
            throw new InvalidOperationException($"Unsupported color literal '{hex}'.");
        }

        return color;
    }

    private static string CultureLabel(string value) =>
        string.IsNullOrWhiteSpace(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private static string CompactSkillLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return string.Empty;
        }

        return label.Length <= 8
            ? label
            : label[..7];
    }
}


