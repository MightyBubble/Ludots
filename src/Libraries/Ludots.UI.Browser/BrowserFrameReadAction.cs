namespace Ludots.UI.Browser;

public delegate void BrowserFrameReadAction<TState>(in BrowserFrameAccess frame, TState state);
