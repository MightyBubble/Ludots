using System;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;
using UiPlayerAggregateGraphMvpShowcaseMod.Runtime;

namespace UiPlayerAggregateGraphMvpShowcaseMod.UI;

internal sealed class UiPlayerAggregateGraphMvpPanelController
{
    private readonly UiPlayerAggregateGraphMvpRuntime _runtime;
    private ReactivePage<UiPlayerAggregateGraphMvpPanelState>? _page;
    private GameEngine? _engine;
    private UiSurfaceLeaseHandle _lease;

    public UiPlayerAggregateGraphMvpPanelController(UiPlayerAggregateGraphMvpRuntime runtime)
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
        UiPlayerAggregateGraphMvpPanelState state = _runtime.BuildPanelState();
        if (_page == null)
        {
            var textMeasurer = (IUiTextMeasurer)engine.GetService(CoreServiceKeys.UiTextMeasurer);
            var imageSizeProvider = (IUiImageSizeProvider)engine.GetService(CoreServiceKeys.UiImageSizeProvider);
            _page = new ReactivePage<UiPlayerAggregateGraphMvpPanelState>(textMeasurer, imageSizeProvider, state, BuildRoot);
        }
        else if (!_page.State.Equals(state))
        {
            _page.SetState(_ => state);
        }

        surfaceHost.PublishReactivePage(
            ref _lease,
            new UiSurfaceLeaseRequest("Showcase.UiPlayerAggregateGraphMvp.Panel", UiSurfaceSegment.Overlay, priority: 40),
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

    private UiElementBuilder BuildRoot(ReactiveContext<UiPlayerAggregateGraphMvpPanelState> context)
    {
        UiPlayerAggregateGraphMvpPanelState state = context.State;
        return Ui.Column(
                Ui.Panel(
                        Ui.Text(state.Title).FontSize(20f).Bold().Color("#F4F7FB"),
                        Ui.Row(
                                ResourceChip("Ore", state.OreTotal, "#F0C36B"),
                                ResourceChip("Crystal", state.CrystalTotal, "#7CC4FF"))
                            .Gap(18f),
                        Ui.Text(state.Copy)
                            .FontSize(11.5f)
                            .Color("#B7C9DA")
                            .WhiteSpace(UiWhiteSpace.Normal),
                        Ui.Text($"Graph `{state.GraphId}` → `{state.OreSummaryKey}` / `{state.CrystalSummaryKey}`")
                            .FontSize(10.5f)
                            .Color("#8096AA")
                            .WhiteSpace(UiWhiteSpace.Normal),
                        Ui.Text(state.Status).FontSize(12f).Color("#F5C66E").WhiteSpace(UiWhiteSpace.Normal),
                        Ui.Text(state.Controls).FontSize(11f).Color("#94D2FF"),
                        Ui.Button(
                                state.BuildingShutDown
                                    ? $"{state.ShutDownBuildingName} offline"
                                    : $"Shut down {state.ShutDownBuildingName}",
                                _ =>
                                {
                                    if (_engine == null)
                                    {
                                        return;
                                    }

                                    _runtime.ShutDownBuilding(_engine);
                                    if (_engine.GetService(CoreServiceKeys.UIRoot) is UIRoot uiRoot)
                                    {
                                        MountOrRefresh(uiRoot, _engine);
                                    }
                                })
                            .Id(UiPlayerAggregateGraphMvpIds.ShutDownButtonElementId)
                            .Padding(12f, 8f)
                            .Radius(8f)
                            .Background(state.BuildingShutDown ? "#243140" : "#3B5F7A")
                            .Color("#F5F7FA"))
                    .Id(UiPlayerAggregateGraphMvpIds.PanelRootElementId)
                    .Width(560f)
                    .Padding(16f)
                    .Gap(10f)
                    .Radius(10f)
                    .Background("#0A1320")
                    .Border(1f, ParseColor("#2F475E"))
                    .Absolute(16f, 16f))
            .WidthPercent(100f)
            .HeightPercent(100f)
            .ZIndex(42);
    }

    private static UiElementBuilder ResourceChip(string label, float total, string accent)
    {
        return Ui.Column(
                Ui.Text(label).FontSize(11f).Bold().Color(accent),
                Ui.Text(FormatNumber(total)).FontSize(28f).Bold().Color("#F7FBFF"))
            .Gap(2f);
    }

    private static string FormatNumber(float value)
    {
        return MathF.Abs(value - MathF.Round(value)) < 0.001f
            ? MathF.Round(value).ToString("0")
            : value.ToString("0.#");
    }

    private static UiColor ParseColor(string hex)
    {
        if (!UiColor.TryParse(hex, out UiColor color))
        {
            throw new InvalidOperationException($"Unsupported color '{hex}'.");
        }

        return color;
    }
}
