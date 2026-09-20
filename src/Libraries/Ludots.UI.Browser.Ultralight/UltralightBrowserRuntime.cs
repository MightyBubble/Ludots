using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ludots.UI.Browser;

namespace Ludots.UI.Browser.Ultralight;

public sealed class UltralightBrowserRuntime : IBrowserRuntime
{
	private readonly List<UltralightBrowserSurface> _surfaces = new();
	private readonly object _surfacesSync = new();
	private readonly UltralightBrowserRuntimeOptions _options;
	private bool _disposed;

	public UltralightBrowserRuntime(UltralightBrowserRuntimeOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		UltralightRuntimeLayoutPreflight.EnsureComplete(options.RuntimeRootPath);
		UltralightProcessRuntime.AcquireRuntimeOwner(options);
		_options = options;
		Info = new BrowserRuntimeInfo(
			BrowserEngineKind.Ultralight,
			"UltralightNet OffScreen",
			"1.3.0",
			BrowserEngineCapabilityProfiles.Ultralight);
	}

	public BrowserRuntimeInfo Info { get; }

	public ValueTask<IBrowserSurface> CreateSurfaceAsync(
		BrowserViewport viewport,
		IBrowserResourceResolver? resourceResolver = null,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		ThrowIfDisposed();

		var surface = new UltralightBrowserSurface(viewport, resourceResolver, _options);
		lock (_surfacesSync)
		{
			_surfaces.Add(surface);
		}

		return ValueTask.FromResult<IBrowserSurface>(surface);
	}

	public ValueTask DisposeAsync()
	{
		if (_disposed)
		{
			return ValueTask.CompletedTask;
		}

		_disposed = true;
		UltralightBrowserSurface[] surfaces;
		lock (_surfacesSync)
		{
			surfaces = _surfaces.ToArray();
			_surfaces.Clear();
		}

		foreach (UltralightBrowserSurface surface in surfaces)
		{
			surface.DisposeAsync().AsTask().GetAwaiter().GetResult();
		}

		UltralightProcessRuntime.ReleaseRuntimeOwner();
		return ValueTask.CompletedTask;
	}

	private void ThrowIfDisposed()
	{
		if (_disposed)
		{
			throw new ObjectDisposedException(nameof(UltralightBrowserRuntime));
		}
	}
}
