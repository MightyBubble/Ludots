using Ludots.Core.UI;
using Ludots.UI;

namespace Ludots.UI.HtmlEngine.Markup;

public sealed class MarkupUiSystem : IUiSystem
{
    private readonly UIRoot _root;
    private readonly UiMarkupLoader _markupLoader = new();

    public MarkupUiSystem(UIRoot root)
    {
        _root = root;
    }

    public void SetHtml(string html, string css)
    {
        var scene = _markupLoader.LoadScene(html, css);
        _root.MountScene(scene);
    }
}
