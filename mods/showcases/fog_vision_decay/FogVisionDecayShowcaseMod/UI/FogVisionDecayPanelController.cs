using System;
using System.Collections.Generic;
using FogVisionDecayShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;

namespace FogVisionDecayShowcaseMod.UI;

internal sealed class FogVisionDecayPanelController
{
    private readonly FogVisionDecayShowcaseRuntime _runtime;
    private ReactivePage<FogVisionDecayPanelState>? _page;

    public FogVisionDecayPanelController(FogVisionDecayShowcaseRuntime runtime)
    {
        _runtime = runtime;
    }

    public void MountOrRefresh(UIRoot root, GameEngine engine)
    {
        FogVisionDecayPanelState state = _runtime.BuildPanelState();
        if (_page == null)
        {
            var textMeasurer = (IUiTextMeasurer)engine.GetService(Ludots.Core.Scripting.CoreServiceKeys.UiTextMeasurer);
            var imageSizeProvider = (IUiImageSizeProvider)engine.GetService(Ludots.Core.Scripting.CoreServiceKeys.UiImageSizeProvider);
            _page = new ReactivePage<FogVisionDecayPanelState>(textMeasurer, imageSizeProvider, state, BuildRoot);
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

    private static UiElementBuilder BuildRoot(ReactiveContext<FogVisionDecayPanelState> context)
    {
        FogVisionDecayPanelState state = context.State;
        return Ui.Column(
                Ui.Card(
                        Ui.Text(state.Header).FontSize(22f).Bold().Color("#F7FBFF"),
                        Ui.Text(state.Summary).FontSize(12f).Color("#C8D7E6").WhiteSpace(UiWhiteSpace.Normal),
                        Ui.Text(state.Controls).FontSize(11f).Color("#94D2FF"),
                        Ui.Text(state.StatusLine).FontSize(12f).Color("#F5C66E"),
                        BuildSection("Telemetry", state.Metrics, "#8DE3AE"),
                        BuildSection("Contacts", state.ContactLines, "#B5A7FF"))
                    .Width(430f)
                    .Padding(16f)
                    .Gap(12f)
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
        var children = new List<UiElementBuilder>
        {
            Ui.Text(title).FontSize(12f).Bold().Color(accent)
        };

        if (lines.Count == 0)
        {
            children.Add(Ui.Text("(empty)").FontSize(11f).Color("#93A4B8"));
        }
        else
        {
            for (int i = 0; i < lines.Count; i++)
            {
                children.Add(Ui.Text(lines[i]).FontSize(11f).Color(i == 0 ? "#F7FBFF" : "#C7D0DD").WhiteSpace(UiWhiteSpace.Normal));
            }
        }

        return Ui.Column(children.ToArray())
            .Width(398f)
            .Padding(10f)
            .Gap(7f)
            .Background("#0E1823");
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
