using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Skia;
using Ludots.UI.Surface;

namespace Ludots.Tests.GAS.Production;

internal static class AcceptanceUiHostInstaller
{
    public static UIRoot Install(GameEngine engine, float width = 1920f, float height = 1080f)
    {
        var uiRoot = new UIRoot(new SkiaUiRenderer());
        uiRoot.Resize(width, height);

        var textMeasurer = new SkiaTextMeasurer();
        var imageSizeProvider = new SkiaImageSizeProvider();
        var surfaceHost = new UiSurfaceHost(uiRoot, textMeasurer, imageSizeProvider);

        engine.SetService(CoreServiceKeys.UIRoot, uiRoot);
        engine.SetService(CoreServiceKeys.UiTextMeasurer, textMeasurer);
        engine.SetService(CoreServiceKeys.UiImageSizeProvider, imageSizeProvider);
        engine.SetService(CoreServiceKeys.UiSurfaceHost, surfaceHost);
        return uiRoot;
    }
}
