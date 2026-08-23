using System;
using FogMobaTerrainShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;

namespace FogMobaTerrainShowcaseMod.UI;

internal sealed class FogMobaTerrainPanelController
{
    private ReactivePage<FogMobaTerrainSnapshot>? _page;
    private UiSurfaceLeaseHandle _lease;
    private GameEngine? _engine;

    public void Refresh(GameEngine engine, FogMobaTerrainSnapshot snapshot)
    {
        if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot ||
            engine.GetService(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost host)
            return;
        _engine = engine;
        if (_page == null)
        {
            var text = (IUiTextMeasurer)engine.GetService(CoreServiceKeys.UiTextMeasurer);
            var images = (IUiImageSizeProvider)engine.GetService(CoreServiceKeys.UiImageSizeProvider);
            _page = new ReactivePage<FogMobaTerrainSnapshot>(text, images, snapshot, BuildRoot);
        }
        else if (!_page.State.Equals(snapshot)) _page.SetState(_ => snapshot);
        host.PublishReactivePage(ref _lease, new UiSurfaceLeaseRequest("Showcase.FogMobaTerrain.Panel", UiSurfaceSegment.Overlay, 44), _page);
    }

    public void Clear(GameEngine engine)
    {
        if (_lease.IsValid && _engine?.GetService(CoreServiceKeys.UiSurfaceHost) is IUiSurfaceHost host) host.ReleaseLease(ref _lease);
        _engine = null;
        _page = null;
    }

    private static UiElementBuilder BuildRoot(ReactiveContext<FogMobaTerrainSnapshot> context)
    {
        FogMobaTerrainSnapshot s = context.State;
        return Ui.Column(
            Ui.Card(
                Ui.Text("WAR FOG / MOBA TERRAIN").FontSize(20f).Bold().Color("#F7FBFF"),
                Ui.Text("WASD move  |  Left/Right turn  |  V shape  |  F rules  |  M memory  |  R range").FontSize(11f).Color("#94D2FF"),
                Ui.Text(s.Status).FontSize(12f).Color("#F5C66E"),
                Ui.Text($"Observer ({s.XCm}, {s.YCm}) cm  facing {s.FacingDegrees} deg").FontSize(12f).Color("#F7FBFF"),
                Ui.Text($"{s.Shape}  range {s.RangeCm} cm  |  visible {s.VisibleCells}  explored {s.ExploredCells}  unknown {s.UnseenCells}").FontSize(12f).Color("#8DE3AE"),
                Ui.Text($"Rules {(s.RulesEnabled ? "ON" : "OFF")}  |  memory {(s.MemoryEnabled ? "ON" : "OFF")}  |  walls {s.WallCells}  |  brush {s.BrushCells}").FontSize(12f).Color("#B5A7FF"),
                Ui.Text("cyan visible   blue explored   dark unknown   orange wall   green brush").FontSize(10f).Color("#C8D7E6"))
            .Width(430f).Padding(15f).Gap(8f).Radius(8f).Background("#08131E").Border(1f, Color("#2F475E")))
            .WidthPercent(100f).HeightPercent(100f).Padding(18f).Align(UiAlignItems.End).Justify(UiJustifyContent.Start).ZIndex(42);
    }

    private static UiColor Color(string hex) => UiColor.TryParse(hex, out UiColor color) ? color : throw new InvalidOperationException($"Unsupported color '{hex}'.");
}
