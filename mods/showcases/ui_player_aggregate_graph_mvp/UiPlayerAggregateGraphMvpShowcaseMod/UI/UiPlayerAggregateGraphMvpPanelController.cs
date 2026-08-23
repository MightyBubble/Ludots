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
        UiPlayerAggregatePanelStyle style = state.PanelStyle;
        return Ui.Column(
                Ui.Panel(
                        Ui.Text(state.Title).FontSize(20f).Bold().Color(style.TitleColor),
                        Ui.Row(
                                ResourceChip(state.OreBinding.Label, state.OreTotal, state.OreBinding.Accent, style.ChipValueColor),
                                ResourceChip(state.CrystalBinding.Label, state.CrystalTotal, state.CrystalBinding.Accent, style.ChipValueColor))
                            .Gap(style.ChipGap),
                        Ui.Text(state.Copy)
                            .FontSize(11.5f)
                            .Color(style.CopyColor)
                            .WhiteSpace(UiWhiteSpace.Normal),
                        Ui.Text($"Graph `{state.GraphId}` → `{state.OreBinding.GraphOutputKey}` / `{state.CrystalBinding.GraphOutputKey}`")
                            .FontSize(10.5f)
                            .Color(style.GraphMetaColor)
                            .WhiteSpace(UiWhiteSpace.Normal),
                        Ui.Text(state.Status).FontSize(12f).Color(style.StatusColor).WhiteSpace(UiWhiteSpace.Normal),
                        Ui.Text(state.Controls).FontSize(11f).Color(style.ControlsColor),
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
                            .Padding(style.ButtonPaddingX, style.ButtonPaddingY)
                            .Radius(style.ButtonRadius)
                            .Background(state.BuildingShutDown ? style.ButtonOfflineBackground : style.ButtonBackground)
                            .Color(style.ButtonColor))
                    .Id(UiPlayerAggregateGraphMvpIds.PanelRootElementId)
                    .Width(style.Width)
                    .Padding(style.Padding)
                    .Gap(style.Gap)
                    .Radius(style.Radius)
                    .Background(style.Background)
                    .Border(style.BorderThickness, ParseColor(style.BorderColor))
                    .Absolute(16f, 16f))
            .WidthPercent(100f)
            .HeightPercent(100f)
            .ZIndex(42);
    }

    private static UiElementBuilder ResourceChip(string label, float total, string accent, string valueColor)
    {
        return Ui.Column(
                Ui.Text(label).FontSize(11f).Bold().Color(accent),
                Ui.Text(FormatNumber(total)).FontSize(28f).Bold().Color(valueColor))
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
