using System;
using System.Collections.Generic;
using EffectHistoryShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;

namespace EffectHistoryShowcaseMod.UI;

internal sealed class EffectHistoryPanelController
{
    private readonly EffectHistoryShowcaseRuntime _runtime;
    private ReactivePage<EffectHistoryPanelState>? _page;
    private GameEngine? _engine;
    private UiSurfaceLeaseHandle _lease;

    public EffectHistoryPanelController(EffectHistoryShowcaseRuntime runtime) => _runtime = runtime;

    public void MountOrRefresh(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost surfaceHost)
            return;

        _engine = engine;
        EffectHistoryPanelState state = _runtime.BuildPanelState();
        if (_page == null)
        {
            var textMeasurer = (IUiTextMeasurer)engine.GetService(CoreServiceKeys.UiTextMeasurer);
            var imageSizeProvider = (IUiImageSizeProvider)engine.GetService(CoreServiceKeys.UiImageSizeProvider);
            _page = new ReactivePage<EffectHistoryPanelState>(textMeasurer, imageSizeProvider, state, BuildRoot);
        }
        else if (!_page.State.Equals(state))
        {
            _page.SetState(_ => state);
        }

        surfaceHost.PublishReactivePage(
            ref _lease,
            new UiSurfaceLeaseRequest("Showcase.EffectHistory.Panel", UiSurfaceSegment.Overlay, priority: 50),
            _page);
    }

    public void Clear()
    {
        if (_lease.IsValid && _engine?.GetService(CoreServiceKeys.UiSurfaceHost) is IUiSurfaceHost surfaceHost)
            surfaceHost.ReleaseLease(ref _lease);
        _engine = null;
    }

    private static UiElementBuilder BuildRoot(ReactiveContext<EffectHistoryPanelState> context)
    {
        EffectHistoryPanelState state = context.State;
        return Ui.Column(
                Ui.Card(
                        Ui.Text(state.Header).FontSize(22f).Bold().Color("#F7FBFF"),
                        Ui.Text(state.Summary).FontSize(12f).Color("#C8D7E6").WhiteSpace(UiWhiteSpace.Normal),
                        Ui.Text(state.Controls).FontSize(11f).Color("#94D2FF").WhiteSpace(UiWhiteSpace.Normal),
                        BuildSection("Current target", new[] { state.Mode, state.Identity, state.Replacement, state.Knowledge }, "#8DE3AE"),
                        BuildSection("Last resolution", new[] { state.Result }, ResultColor(state.Result)),
                        BuildSection("Execution history", state.History, "#B5A7FF"))
                    .Width(455f)
                    .Padding(16f)
                    .Gap(10f)
                    .Radius(8f)
                    .Background("#0A1320")
                    .Border(1f, Color("#2F475E")))
            .WidthPercent(100f)
            .HeightPercent(100f)
            .Padding(20f)
            .Background("#05080D")
            .Align(UiAlignItems.End)
            .Justify(UiJustifyContent.Start)
            .ZIndex(42);
    }

    private static UiElementBuilder BuildSection(string title, IReadOnlyList<string> lines, string accent)
    {
        var children = new List<UiElementBuilder> { Ui.Text(title).FontSize(12f).Bold().Color(accent) };
        for (int i = 0; i < lines.Count; i++)
            children.Add(Ui.Text(lines[i]).FontSize(11f).Color(i == 0 ? "#F7FBFF" : "#C7D0DD").WhiteSpace(UiWhiteSpace.Normal));
        return Ui.Column(children.ToArray()).Width(423f).Padding(10f).Gap(6f).Background("#0E1823");
    }

    private static string ResultColor(string result) => result.Contains("Stale", StringComparison.OrdinalIgnoreCase) || result.Contains("Missing", StringComparison.OrdinalIgnoreCase) ? "#F5C66E" : "#8DE3AE";

    private static UiColor Color(string hex)
    {
        if (!UiColor.TryParse(hex, out UiColor color))
            throw new InvalidOperationException($"Unsupported color '{hex}'.");
        return color;
    }
}
