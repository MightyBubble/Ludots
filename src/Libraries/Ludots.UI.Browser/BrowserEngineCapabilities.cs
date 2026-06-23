using System;

namespace Ludots.UI.Browser;

[Flags]
public enum BrowserEngineCapabilities
{
	None = 0,
	JavaScript = 1,
	Dom = 1 << 1,
	Css = 1 << 2,
	Wasm = 1 << 3,
	WebGL = 1 << 4,
	OffscreenRendering = 1 << 5,
	TransparentBackground = 1 << 6,
	LocalResourceResolver = 1 << 7,
	InputMethodEditor = 1 << 8,
	FullChromiumCompatibility = 1 << 9,
	LightweightGameUi = 1 << 10
}
