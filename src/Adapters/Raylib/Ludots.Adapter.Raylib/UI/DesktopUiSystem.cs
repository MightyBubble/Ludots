using Ludots.Core.UI;
using Ludots.UI;
using Ludots.UI.HtmlEngine.Markup;
using Ludots.UI.Runtime;
using Ludots.UI.Skia;

namespace Ludots.Adapter.Raylib.UI
{
    public sealed class DesktopUiSystem : IUiSystem
    {
        private readonly UIRoot _root;
        private readonly UiMarkupLoader _markupLoader = new();
        private readonly IUiTextMeasurer _textMeasurer = new SkiaTextMeasurer();
        private readonly IUiImageSizeProvider _imageSizeProvider = new SkiaImageSizeProvider();

        public DesktopUiSystem(UIRoot root)
        {
            _root = root;
        }

        public void SetHtml(string html, string css)
        {
            var scene = _markupLoader.LoadScene(_textMeasurer, _imageSizeProvider, html, css);
            _root.MountScene(scene);
        }
    }
}
