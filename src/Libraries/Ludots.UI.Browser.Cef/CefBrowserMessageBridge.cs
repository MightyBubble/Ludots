using System;
using System.Threading;
using System.Threading.Tasks;
using CefSharp.OffScreen;
using Ludots.UI.Browser;

namespace Ludots.UI.Browser.Cef;

internal sealed class CefBrowserMessageBridge : IBrowserMessageBridge
{
	private readonly ChromiumWebBrowser _browser;
	private readonly CefBrowserSurface _surface;

	public CefBrowserMessageBridge(ChromiumWebBrowser browser, CefBrowserSurface surface)
	{
		_browser = browser ?? throw new ArgumentNullException(nameof(browser));
		_surface = surface ?? throw new ArgumentNullException(nameof(surface));
	}

	public event EventHandler<BrowserScriptMessage>? MessageReceived;

	public ValueTask PostMessageAsync(BrowserScriptMessage message, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(message);
		return _surface.PostHostMessageAsync(message, cancellationToken);
	}

	public ValueTask ExecuteScriptAsync(string script, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(script))
		{
			throw new ArgumentException("Script is required.", nameof(script));
		}

		return _surface.ExecuteScriptAsync(script, cancellationToken);
	}

	internal void RaiseMessage(BrowserScriptMessage message)
	{
		MessageReceived?.Invoke(this, message);
	}
}
