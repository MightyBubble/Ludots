using System;
using System.Collections.Generic;
using Ludots.Core.Engine;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using ScopeSwitchShowcaseMod.Runtime;

namespace ScopeSwitchShowcaseMod.UI;

internal sealed class ScopeSwitchPanelController
{
    private readonly ScopeSwitchRuntime _runtime;
    private ReactivePage<ScopeSwitchPanelState>? _page;

    public ScopeSwitchPanelController(ScopeSwitchRuntime runtime)
    {
        _runtime = runtime;
    }

    public void MountOrRefresh(UIRoot root, GameEngine engine)
    {
        ScopeSwitchPanelState state = _runtime.BuildPanelState();
        if (_page == null)
        {
            var textMeasurer = (IUiTextMeasurer)engine.GetService(Ludots.Core.Scripting.CoreServiceKeys.UiTextMeasurer);
            var imageSizeProvider = (IUiImageSizeProvider)engine.GetService(Ludots.Core.Scripting.CoreServiceKeys.UiImageSizeProvider);
            _page = new ReactivePage<ScopeSwitchPanelState>(textMeasurer, imageSizeProvider, state, BuildRoot);
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

    private static UiElementBuilder BuildRoot(ReactiveContext<ScopeSwitchPanelState> context)
    {
        ScopeSwitchPanelState state = context.State;
        return Ui.Column(
                Ui.Card(
                        Ui.Text(state.Header).FontSize(22f).Bold().Color("#F5F7FA"),
                        Ui.Text(state.Summary).FontSize(12f).Color("#D6E0EA").WhiteSpace(UiWhiteSpace.Normal),
                        Ui.Text(state.Controls).FontSize(11f).Color("#8AD7FF"),
                        Ui.Text(state.ActiveLine).FontSize(13f).Color("#F0C36B"),
                        BuildSection("Scopes", state.ScopeLines, "#8AD7FF"),
                        BuildSection("Visible", state.VisibleLines, "#8DE3AE"),
                        BuildSection("Selectable", state.SelectedLines, "#FFB38A"),
                        Ui.Text(state.Status).FontSize(11f).Color("#C7D0DD").WhiteSpace(UiWhiteSpace.Normal))
                    .Width(440f)
                    .Padding(16f)
                    .Gap(12f)
                    .Radius(8f)
                    .Background("#0B1520")
                    .Border(1f, Color("#2F475E")))
            .WidthPercent(100f)
            .HeightPercent(100f)
            .Padding(20f)
            .Background("#050A0F")
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

        if (lines.Count == 0)
        {
            children.Add(Ui.Text("(empty)").FontSize(11f).Color("#93A4B8"));
        }
        else
        {
            for (int i = 0; i < lines.Count; i++)
            {
                children.Add(Ui.Text(lines[i]).FontSize(11f).Color(i == 0 ? "#F5F7FA" : "#C7D0DD").WhiteSpace(UiWhiteSpace.Normal));
            }
        }

        return Ui.Card(children.ToArray())
            .Width(408f)
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
