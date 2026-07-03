using System;
using System.Collections.Generic;
using FourXAssociationShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;

namespace FourXAssociationShowcaseMod.UI;

internal sealed class FourXAssociationPanelController
{
    private readonly FourXAssociationRuntime _runtime;
    private ReactivePage<FourXAssociationPanelState>? _page;
    private GameEngine? _engine;
    private UiSurfaceLeaseHandle _lease;

    public FourXAssociationPanelController(FourXAssociationRuntime runtime)
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
        FourXAssociationPanelState state = _runtime.BuildPanelState();
        if (_page == null)
        {
            var textMeasurer = (IUiTextMeasurer)engine.GetService(CoreServiceKeys.UiTextMeasurer);
            var imageSizeProvider = (IUiImageSizeProvider)engine.GetService(CoreServiceKeys.UiImageSizeProvider);
            _page = new ReactivePage<FourXAssociationPanelState>(textMeasurer, imageSizeProvider, state, BuildRoot);
        }
        else if (!_page.State.Equals(state))
        {
            _page.SetState(_ => state);
        }

        surfaceHost.PublishReactivePage(
            ref _lease,
            new UiSurfaceLeaseRequest("Showcase.FourXAssociation.Panel", UiSurfaceSegment.Overlay, priority: 40),
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

    private static UiElementBuilder BuildRoot(ReactiveContext<FourXAssociationPanelState> context)
    {
        FourXAssociationPanelState state = context.State;
        return Ui.Column(
                Ui.Card(
                        Ui.Text(state.Header).FontSize(22f).Bold().Color("#F7FBFF"),
                        Ui.Text(state.Summary).FontSize(12f).Color("#D1DDE9").WhiteSpace(UiWhiteSpace.Normal),
                        Ui.Text(state.Controls).FontSize(11f).Color("#8AE0FF").WhiteSpace(UiWhiteSpace.Normal),
                        Ui.Text(state.Status).FontSize(13f).Bold().Color("#FFD27D"),
                        BuildSection("Association contracts", state.ContractLines, "#97E0A8"),
                        BuildSection("Session log", state.LogLines, "#F4AA7A"))
                    .Width(500f)
                    .Padding(16f)
                    .Gap(12f)
                    .Radius(8f)
                    .Background("#0A1420")
                    .Border(1f, Color("#2E465F")))
            .WidthPercent(100f)
            .HeightPercent(100f)
            .Padding(20f)
            .Background("#05101A")
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
            children.Add(Ui.Text(lines[i]).FontSize(11f).Color(i == 0 ? "#F4F8FB" : "#C7D1DE").WhiteSpace(UiWhiteSpace.Normal));
        }

        return Ui.Card(children.ToArray())
            .Width(468f)
            .Padding(12f)
            .Gap(8f)
            .Radius(8f)
            .Background("#0D1824")
            .Border(1f, Color("#274155"));
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
