using System;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.Core.UI.PanelActivation;
using Ludots.Core.UI.PanelHosting;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;

namespace Ludots.UI.Panels;

/// <summary>
/// Single installation entry for engine-side panel presentation. Called by host
/// composers (raylib/web) and test harnesses after UIRoot/UiSurfaceHost exist.
/// Selection is read from merged game.json ("panelSkin"); panels appear with zero
/// mod code — the 0-encoding promise of contract four.
/// </summary>
public static class PanelPresentationInstaller
{
    public static void Install(GameEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);


        PanelHost panelHost = engine.GetService(CoreServiceKeys.PanelHost)
            ?? throw new InvalidOperationException("Panel presentation requires PanelHost engine service.");
        PanelTemplateRegistry templates = engine.GetService(CoreServiceKeys.PanelTemplateRegistry)
            ?? throw new InvalidOperationException("Panel presentation requires PanelTemplateRegistry engine service.");
        UiPanelActivationStore activation = engine.GetService(CoreServiceKeys.PanelActivationStore)
            ?? throw new InvalidOperationException("Panel presentation requires PanelActivationStore engine service.");
        IUiSurfaceHost surfaceHost = engine.GetService(CoreServiceKeys.UiSurfaceHost) as IUiSurfaceHost
            ?? throw new InvalidOperationException("Panel presentation requires UiSurfaceHost engine service.");
        UIRoot root = engine.GetService(CoreServiceKeys.UIRoot) as UIRoot
            ?? throw new InvalidOperationException("Panel presentation requires UIRoot engine service.");

        PanelTheme? theme = PanelThemeCatalog.TryLoad(engine);
        var textMeasurer = engine.GetService(CoreServiceKeys.UiTextMeasurer) as Ludots.UI.Runtime.IUiTextMeasurer;
        var imageSizeProvider = engine.GetService(CoreServiceKeys.UiImageSizeProvider) as Ludots.UI.Runtime.IUiImageSizeProvider;
        var seats = engine.TryGetService(CoreServiceKeys.ClientLocalSeatRegistry, out Ludots.Core.Client.ClientLocalSeatRegistry? seatRegistry)
            ? seatRegistry
            : null;
        var displayResolver = engine.GetService(CoreServiceKeys.PresentationDisplayResolver);
        engine.RegisterPresentationSystem(new PanelPresentationSystem(
            panelHost,
            templates,
            activation,
            surfaceHost,
            root,
            engine.MergedConfig?.PanelSkin,
            theme?.StyleSheet,
            textMeasurer,
            imageSizeProvider,
            displayResolver,
            seats));
    }
}
