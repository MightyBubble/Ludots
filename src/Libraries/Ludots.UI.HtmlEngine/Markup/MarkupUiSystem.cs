using Ludots.Core.UI;
using Ludots.UI.Compose;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;

namespace Ludots.UI.HtmlEngine.Markup;

public sealed class MarkupUiSystem : IUiSystem
{
    private const string OwnerId = "Core.UISystem.Markup";

    private readonly IUiSurfaceHost _surfaceHost;
    private readonly UiMarkupLoader _markupLoader = new();
    private readonly UiSurfaceLeaseHandle _lease;

    public MarkupUiSystem(IUiSurfaceHost surfaceHost)
    {
        _surfaceHost = surfaceHost;
        _lease = _surfaceHost.Acquire(new UiSurfaceLeaseRequest(OwnerId, UiSurfaceSegment.Main, exclusive: true));
    }

    public void SetHtml(string html, string css)
    {
        UiDocument document = _markupLoader.LoadDocument(html, css);
        _surfaceHost.Publish(
            _lease,
            UiSurfaceContribution.FromBuilder(
                () => UiElementBuilder.FromElement(document.Root),
                styleSheets: document.StyleSheets));
    }
}
