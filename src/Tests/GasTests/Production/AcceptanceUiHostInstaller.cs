using System.Numerics;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Camera;
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
        engine.SetService(CoreServiceKeys.ViewController, new FixedViewController(width, height));
        Ludots.UI.Panels.PanelPresentationInstaller.Install(engine);
        return uiRoot;
    }

    private sealed class FixedViewController : IViewController
    {
        public FixedViewController(float width, float height)
        {
            Resolution = new Vector2(width, height);
            AspectRatio = width / height;
        }

        public Vector2 Resolution { get; }

        public float Fov { get; } = 60f;

        public float AspectRatio { get; }
    }
}
