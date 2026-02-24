using Ludots.Core.UI;
using Ludots.UI;
using Ludots.UI.HtmlEngine;

namespace Ludots.Adapter.Raylib.UI
{
    public sealed class DesktopUiSystem : IUiSystem
    {
        private readonly UIRoot _root;

        public DesktopUiSystem(UIRoot root)
        {
            _root = root;
        }

        public void SetHtml(string html, string css)
        {
            var widget = new HtmlWidget
            {
                Html = html,
                Css = css,
                Width = _root.Width,
                Height = _root.Height
            };
            _root.Content = widget;
            _root.IsDirty = true;
        }
    }
}
