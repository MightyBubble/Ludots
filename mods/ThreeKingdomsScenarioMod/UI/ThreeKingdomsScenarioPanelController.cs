using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using ThreeKingdomsScenarioMod.Runtime;

namespace ThreeKingdomsScenarioMod.UI;

internal sealed class ThreeKingdomsScenarioPanelController
{
    private ReactivePage<ThreeKingdomsScenarioHudState>? _page;

    public void MountOrRefresh(UIRoot root, GameEngine engine, in ThreeKingdomsScenarioHudState state)
    {
        ThreeKingdomsScenarioHudState nextState = state;
        if (_page == null)
        {
            var textMeasurer = (IUiTextMeasurer)engine.GetService(CoreServiceKeys.UiTextMeasurer);
            var imageSizeProvider = (IUiImageSizeProvider)engine.GetService(CoreServiceKeys.UiImageSizeProvider);
            _page = new ReactivePage<ThreeKingdomsScenarioHudState>(
                textMeasurer,
                imageSizeProvider,
                nextState,
                BuildRoot);
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

    private static UiElementBuilder BuildRoot(ReactiveContext<ThreeKingdomsScenarioHudState> context)
    {
        ThreeKingdomsScenarioHudState state = context.State;
        return Ui.Column(
                Ui.Row(
                        Ui.Column(
                                Ui.Text(state.ScenarioTitle).FontSize(24f).Bold().Color("#F5E7B7"),
                                Ui.Text(state.ModeSummary).FontSize(11f).Color("#B7C4D4").WhiteSpace(UiWhiteSpace.Normal))
                            .Gap(4f)
                            .FlexGrow(1f),
                        Ui.Column(
                                Ui.Text("Selection").FontSize(10f).Bold().Color("#E8B04F"),
                                Ui.Text(state.SelectionLabel).FontSize(16f).Bold().Color("#F8FAFC"),
                                Ui.Text(state.SelectionType).FontSize(10f).Color("#8FB2D1"))
                            .Gap(2f))
                    .Padding(18f, 12f)
                    .Background("#D30C121B")
                    .Border(1f, Color("#447793B4"))
                    .Radius(10f)
                    .Absolute(18f, 14f)
                    .Width(980f))
            .WidthPercent(100f)
            .HeightPercent(100f)
            .Absolute(0f, 0f)
            .ZIndex(38);
    }

    private static UiColor Color(string hex)
    {
        return UiColor.TryParse(hex, out UiColor color) ? color : default;
    }
}
