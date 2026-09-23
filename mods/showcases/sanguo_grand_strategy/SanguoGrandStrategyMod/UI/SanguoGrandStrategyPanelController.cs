using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Actions;
using Ludots.UI.Surface;
using SanguoGrandStrategyMod.Runtime;

namespace SanguoGrandStrategyMod.UI;

internal sealed class SanguoGrandStrategyPanelController
{
    private readonly SanguoGrandStrategyRuntime _runtime;
    private ReactivePage<SanguoGrandStrategyPanelState>? _page;
    private GameEngine? _engine;
    private UiSurfaceLeaseHandle _lease;

    public SanguoGrandStrategyPanelController(SanguoGrandStrategyRuntime runtime)
    {
        _runtime = runtime;
    }

    public void MountOrRefresh(UIRoot root, GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost surfaceHost)
        {
            return;
        }

        _engine = engine;
        SanguoGrandStrategyPanelState state = _runtime.BuildPanelState();
        if (_page == null)
        {
            var textMeasurer = (IUiTextMeasurer)engine.GetService(CoreServiceKeys.UiTextMeasurer);
            var imageSizeProvider = (IUiImageSizeProvider)engine.GetService(CoreServiceKeys.UiImageSizeProvider);
            _page = new ReactivePage<SanguoGrandStrategyPanelState>(textMeasurer, imageSizeProvider, state, BuildRoot);
        }
        else if (!_page.State.Equals(state))
        {
            _page.SetState(_ => state);
        }

        surfaceHost.PublishReactivePage(
            ref _lease,
            new UiSurfaceLeaseRequest("Showcase.SanguoGrandStrategy.Panel", UiSurfaceSegment.Overlay, priority: 42),
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

    private UiElementBuilder BuildRoot(ReactiveContext<SanguoGrandStrategyPanelState> context)
    {
        SanguoGrandStrategyPanelState state = context.State;
        return Ui.Column(
                Ui.ScrollView(
                        Ui.Column(
                                Ui.Row(
                                        BuildCommandPanel(state),
                                        BuildMapReadout(state))
                                    .Gap(12f)
                                    .Wrap()
                                    .Align(UiAlignItems.Start),
                                Ui.Row(
                                        BuildSection("Factions", state.FactionLines, "#7DD3FC", 420f, 220f),
                                        BuildSection("Unit Types", state.UnitLines, "#F4C95D", 420f, 220f),
                                        BuildSection("Campaign Log", state.LogLines, "#F6A6B2", 520f, 220f))
                                    .Gap(12f)
                                    .Wrap()
                                    .Align(UiAlignItems.Start))
                            .Gap(12f)
                            .Padding(14f))
                    .WidthPercent(100f)
                    .HeightPercent(100f))
            .WidthPercent(100f)
            .HeightPercent(100f)
            .Background("#06080D")
            .ZIndex(46);
    }

    private UiElementBuilder BuildCommandPanel(SanguoGrandStrategyPanelState state)
    {
        return Ui.Card(
                Ui.Text(state.Header)
                    .FontSize(22f)
                    .Bold()
                    .Color("#F8FAFC"),
                Ui.Text(state.Summary)
                    .FontSize(12f)
                    .Color("#CBD5E1")
                    .WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text(state.WebUiStatus)
                    .FontSize(11f)
                    .Color("#8DE3AE")
                    .WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text(state.SelectedCityLine)
                    .FontSize(14f)
                    .Bold()
                    .Color("#F4C95D")
                    .WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text(state.SelectedUnitLine)
                    .FontSize(12f)
                    .Color("#D7DEE8")
                    .WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text(state.EquipmentLine)
                    .FontSize(12f)
                    .Color("#8DE3AE")
                    .WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text(state.CommanderLine)
                    .FontSize(12f)
                    .Color("#CBD5E1")
                    .WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text(state.PendingBattleLine)
                    .FontSize(12f)
                    .Color("#F6A6B2")
                    .WhiteSpace(UiWhiteSpace.Normal),
                Ui.Row(
                        BuildButton("Prev City", "#25405F", _ => Run(engine => _runtime.SelectPreviousCity(engine))),
                        BuildButton("Next City", "#25405F", _ => Run(engine => _runtime.SelectNextCity(engine))),
                        BuildButton("Next Unit", "#5C4A1A", _ => Run(engine => _runtime.SelectNextUnitType(engine))))
                    .Gap(8f)
                    .Wrap(),
                Ui.Row(
                        BuildButton("Conscript", "#315E45", _ => Run(engine => _runtime.Conscript(engine))),
                        BuildButton("Train Elite", "#315E45", _ => Run(engine => _runtime.TrainElite(engine))),
                        BuildButton("Develop", "#5C4A1A", _ => Run(engine => _runtime.Develop(engine))),
                        BuildButton("Tax", "#6A3344", _ => Run(engine => _runtime.Tax(engine))),
                        BuildButton("Harvest", "#3E5D34", _ => Run(engine => _runtime.Harvest(engine))),
                        BuildButton("March", "#5B3A6C", _ => Run(engine => _runtime.March(engine))),
                        BuildButton("Resolve Battle", "#783A32", _ => Run(engine => _runtime.ResolveBattle(engine))),
                        BuildButton("Research", "#274C66", _ => Run(engine => _runtime.Research(engine))),
                        BuildButton("Diplomacy", "#4F496E", _ => Run(engine => _runtime.Diplomacy(engine))))
                    .Gap(8f)
                    .Wrap())
            .Width(460f)
            .Padding(14f)
            .Gap(10f)
            .Radius(8f)
            .Background("#101722")
            .Border(1f, Color("#2D4860"));
    }

    private static UiElementBuilder BuildMapReadout(SanguoGrandStrategyPanelState state)
    {
        return Ui.Card(
                BuildSection("Overview", state.OverviewLines, "#8DE3AE", 430f, 190f),
                BuildSection("Selected City", state.CityLines, "#F4C95D", 430f, 210f))
            .Width(462f)
            .Padding(12f)
            .Gap(12f)
            .Radius(8f)
            .Background("#0D1320")
            .Border(1f, Color("#2D4860"));
    }

    private static UiElementBuilder BuildSection(string title, IReadOnlyList<string> lines, string accent, float width, float height)
    {
        var children = new List<UiElementBuilder>
        {
            Ui.Text(title).FontSize(12f).Bold().Color(accent)
        };

        if (lines.Count == 0)
        {
            children.Add(Ui.Text("(empty)").FontSize(11f).Color("#8FA3B8"));
        }
        else
        {
            for (int i = 0; i < lines.Count; i++)
            {
                children.Add(
                    Ui.Text(lines[i])
                        .FontSize(11f)
                        .Color(i == 0 ? "#F8FAFC" : "#CBD5E1")
                        .WhiteSpace(UiWhiteSpace.Normal));
            }
        }

        return Ui.Card(children.ToArray())
            .Width(width)
            .Height(height)
            .Padding(12f)
            .Gap(7f)
            .Radius(8f)
            .Background("#151F2D")
            .Border(1f, Color("#334A5F"));
    }

    private static UiElementBuilder BuildButton(string label, string background, Action<UiActionContext> onClick)
    {
        return Ui.Button(label, onClick)
            .Padding(9f, 7f)
            .Radius(8f)
            .Background(background)
            .Border(1f, Color("#44FFFFFF"))
            .Color("#F8FAFC")
            .FontSize(11f)
            .WhiteSpace(UiWhiteSpace.Normal);
    }

    private void Run(Action<GameEngine> action)
    {
        if (_engine == null)
        {
            return;
        }

        action(_engine);
    }

    private static UiColor Color(string hex)
    {
        if (!UiColor.TryParse(hex, out UiColor color))
        {
            throw new InvalidOperationException($"Unsupported color '{hex}'.");
        }

        return color;
    }
}
