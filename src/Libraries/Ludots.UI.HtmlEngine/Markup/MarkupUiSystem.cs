using Ludots.Core.UI;
using Ludots.UI;
using Ludots.UI.Runtime;

namespace Ludots.UI.HtmlEngine.Markup;

public sealed class MarkupUiSystem : IUiSystem
{
    private readonly UIRoot _root;
    private readonly IUiTextMeasurer _textMeasurer;
    private readonly IUiImageSizeProvider _imageSizeProvider;
    private readonly UiMarkupLoader _markupLoader = new();

    public MarkupUiSystem(UIRoot root, IUiTextMeasurer textMeasurer, IUiImageSizeProvider imageSizeProvider)
    {
        _root = root;
        _textMeasurer = textMeasurer;
        _imageSizeProvider = imageSizeProvider;
    }

    public void SetHtml(string html, string css)
    {
        var scene = _markupLoader.LoadScene(_textMeasurer, _imageSizeProvider, html, css);
        _root.MountScene(scene);
    }
}
