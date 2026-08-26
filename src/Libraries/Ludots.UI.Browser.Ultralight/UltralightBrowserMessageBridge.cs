using System;
using System.Threading;
using System.Threading.Tasks;
using Ludots.UI.Browser;
using UltralightNet;

namespace Ludots.UI.Browser.Ultralight;

internal sealed class UltralightBrowserMessageBridge : IBrowserMessageBridge
{
	private readonly UltralightBrowserSurface _surface;

	public UltralightBrowserMessageBridge(UltralightBrowserSurface surface)
	{
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
