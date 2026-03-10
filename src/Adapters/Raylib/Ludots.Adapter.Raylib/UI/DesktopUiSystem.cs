using Ludots.Core.UI;
using Ludots.UI;
using Ludots.UI.HtmlEngine.Markup;

namespace Ludots.Adapter.Raylib.UI
{
    public sealed class DesktopUiSystem : IUiSystem
    {
        private readonly UIRoot _root;
        private readonly UiMarkupLoader _markupLoader = new();

        public DesktopUiSystem(UIRoot root)
        {
            _root = root;
        }

        public void SetHtml(string html, string css)
        {
            var scene = _markupLoader.LoadScene(html, css);
            _root.MountScene(scene);
        }
    }
}
