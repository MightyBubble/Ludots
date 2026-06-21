using System;
using System.Collections.Generic;
using GoldMarketShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;

namespace GoldMarketShowcaseMod.UI;

internal sealed class GoldMarketPanelController
{
    private readonly GoldMarketRuntime _runtime;
    private ReactivePage<GoldMarketPanelState>? _page;

    public GoldMarketPanelController(GoldMarketRuntime runtime)
    {
        _runtime = runtime;
    }

    public void MountOrRefresh(UIRoot root, GameEngine engine)
    {
        GoldMarketPanelState state = _runtime.BuildPanelState();
        if (_page == null)
        {
            var textMeasurer = (IUiTextMeasurer)engine.GetService(CoreServiceKeys.UiTextMeasurer);
            var imageSizeProvider = (IUiImageSizeProvider)engine.GetService(CoreServiceKeys.UiImageSizeProvider);
            _page = new ReactivePage<GoldMarketPanelState>(textMeasurer, imageSizeProvider, state, BuildRoot);
        }
        else if (!_page.State.Equals(state))
        {
            _page.SetState(_ => state);
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

    private static UiElementBuilder BuildRoot(ReactiveContext<GoldMarketPanelState> context)
    {
        GoldMarketPanelState state = context.State;
        return Ui.Column(
                Ui.Card(
                        Ui.Text(state.Header).FontSize(22f).Bold().Color("#F6FAFF"),
                        Ui.Text(state.Summary).FontSize(12f).Color("#D5E1EA").WhiteSpace(UiWhiteSpace.Normal),
                        Ui.Text(state.Controls).FontSize(11f).Color("#8AD7FF"),
                        Ui.Text(state.Status).FontSize(13f).Bold().Color("#F0C36B"),
                        BuildSection("Market ledger", state.Lines, "#8DE3AE"),
                        BuildSection("Execution trace", state.LogLines, "#FFB38A"))
                    .Width(460f)
                    .Padding(16f)
                    .Gap(12f)
                    .Radius(8f)
                    .Background("#0B1520")
                    .Border(1f, Color("#2F475E")))
            .WidthPercent(100f)
            .HeightPercent(100f)
            .Padding(20f)
            .Background("#061018")
            .Align(UiAlignItems.End)
            .Justify(UiJustifyContent.Start)
            .ZIndex(40);
    }

    private static UiElementBuilder BuildSection(string title, IReadOnlyList<string> lines, string accent)
    {
        var children = new List<UiElementBuilder>
        {
            Ui.Text(title).FontSize(12f).Bold().Color(accent)
        };

        for (int i = 0; i < lines.Count; i++)
        {
            children.Add(Ui.Text(lines[i]).FontSize(11f).Color(i == 0 ? "#F5F7FA" : "#C7D0DD").WhiteSpace(UiWhiteSpace.Normal));
        }

        return Ui.Card(children.ToArray())
            .Width(428f)
            .Padding(12f)
            .Gap(8f)
            .Radius(8f)
            .Background("#0E1823")
            .Border(1f, Color("#284154"));
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
