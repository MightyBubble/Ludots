namespace Ludots.UI.Browser;

public static class BrowserEngineCapabilityProfiles
{
	public const BrowserEngineCapabilities Cef =
		BrowserEngineCapabilities.JavaScript |
		BrowserEngineCapabilities.Dom |
		BrowserEngineCapabilities.Css |
		BrowserEngineCapabilities.Wasm |
		BrowserEngineCapabilities.WebGL |
		BrowserEngineCapabilities.OffscreenRendering |
		BrowserEngineCapabilities.TransparentBackground |
		BrowserEngineCapabilities.LocalResourceResolver |
		BrowserEngineCapabilities.InputMethodEditor |
		BrowserEngineCapabilities.FullChromiumCompatibility;

	public const BrowserEngineCapabilities Ultralight =
		BrowserEngineCapabilities.JavaScript |
		BrowserEngineCapabilities.Dom |
		BrowserEngineCapabilities.Css |
		BrowserEngineCapabilities.Wasm |
		BrowserEngineCapabilities.OffscreenRendering |
		BrowserEngineCapabilities.TransparentBackground |
		BrowserEngineCapabilities.LocalResourceResolver |
		BrowserEngineCapabilities.InputMethodEditor |
		BrowserEngineCapabilities.LightweightGameUi;
}
