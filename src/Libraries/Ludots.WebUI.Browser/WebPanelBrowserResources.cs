using Ludots.Core.Modding;
using Ludots.UI.Browser;

namespace Ludots.WebUI.Browser;

public static class WebPanelBrowserResources
{
    public static IBrowserResourceResolver Create(string panelRootDirectory, IVirtualFileSystem virtualFileSystem)
    {
        ArgumentNullException.ThrowIfNull(virtualFileSystem);
        return new SchemeRoutingBrowserResourceResolver(
            new BrowserAppResourceResolver(panelRootDirectory),
            new VfsBrowserResourceResolver(virtualFileSystem));
    }
}
