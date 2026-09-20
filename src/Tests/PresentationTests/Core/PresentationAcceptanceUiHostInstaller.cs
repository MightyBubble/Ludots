using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Skia;
using Ludots.UI.Surface;

namespace Ludots.Tests.Presentation
{
    internal static class PresentationAcceptanceUiHostInstaller
    {
        internal static UIRoot Install(GameEngine engine, float width, float height)
        {
            var renderer = new SkiaUiRenderer();
            var textMeasurer = new SkiaTextMeasurer();
            var imageSizeProvider = new SkiaImageSizeProvider();
            var uiRoot = new UIRoot(renderer);
            uiRoot.Resize(width, height);

            engine.SetService(CoreServiceKeys.UIRoot, uiRoot);
            engine.SetService(CoreServiceKeys.UiTextMeasurer, textMeasurer);
            engine.SetService(CoreServiceKeys.UiImageSizeProvider, imageSizeProvider);
            engine.SetService(CoreServiceKeys.UiSurfaceHost, new UiSurfaceHost(uiRoot, textMeasurer, imageSizeProvider));
            return uiRoot;
        }
    }
}
