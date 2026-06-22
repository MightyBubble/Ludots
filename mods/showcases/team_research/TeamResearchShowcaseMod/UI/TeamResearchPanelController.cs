using System;
using System.Collections.Generic;
using Ludots.Core.Engine;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using TeamResearchShowcaseMod.Runtime;

namespace TeamResearchShowcaseMod.UI;

internal sealed class TeamResearchPanelController
{
    private readonly TeamResearchRuntime _runtime;
    private ReactivePage<TeamResearchPanelState>? _page;

    public TeamResearchPanelController(TeamResearchRuntime runtime)
    {
        _runtime = runtime;
    }

    public void MountOrRefresh(UIRoot root, GameEngine engine)
    {
        TeamResearchPanelState state = _runtime.BuildPanelState();
        if (_page == null)
        {
            var textMeasurer = (IUiTextMeasurer)engine.GetService(Ludots.Core.Scripting.CoreServiceKeys.UiTextMeasurer);
            var imageSizeProvider = (IUiImageSizeProvider)engine.GetService(Ludots.Core.Scripting.CoreServiceKeys.UiImageSizeProvider);
            _page = new ReactivePage<TeamResearchPanelState>(textMeasurer, imageSizeProvider, state, BuildRoot);
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

    private static UiElementBuilder BuildRoot(ReactiveContext<TeamResearchPanelState> context)
    {
        TeamResearchPanelState state = context.State;
        return Ui.Column(
                Ui.Card(
                        Ui.Text(state.Header).FontSize(22f).Bold().Color("#F8FAFC"),
                        Ui.Text(state.Summary).FontSize(12f).Color("#D7DEE8").WhiteSpace(UiWhiteSpace.Normal),
                        Ui.Text(state.Controls).FontSize(11f).Color("#76D7C4"),
                        Ui.Text(state.ProgressLine).FontSize(14f).Color("#F4C95D"),
                        Ui.Text(state.RequirementLine).FontSize(12f).Color("#B7E4C7"),
                        BuildSection("Team Members", state.MemberLines, "#76D7C4"),
                        BuildSection("Shared Unlock", state.UnlockLines, "#F6A6B2"),
                        Ui.Text(state.Status).FontSize(11f).Color("#C9D3DF").WhiteSpace(UiWhiteSpace.Normal))
                    .Width(460f)
                    .Padding(16f)
                    .Gap(12f)
                    .Radius(8f)
                    .Background("#111827")
                    .Border(1f, Color("#3A5266")))
            .WidthPercent(100f)
            .HeightPercent(100f)
            .Padding(20f)
            .Background("#05070B")
            .Align(UiAlignItems.End)
            .Justify(UiJustifyContent.Start)
            .ZIndex(44);
    }

    private static UiElementBuilder BuildSection(string title, IReadOnlyList<string> lines, string accent)
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
                children.Add(Ui.Text(lines[i]).FontSize(11f).Color(i == 0 ? "#F8FAFC" : "#CBD5E1").WhiteSpace(UiWhiteSpace.Normal));
            }
        }

        return Ui.Card(children.ToArray())
            .Width(428f)
            .Padding(12f)
            .Gap(8f)
            .Radius(8f)
            .Background("#162231")
            .Border(1f, Color("#2E455A"));
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
