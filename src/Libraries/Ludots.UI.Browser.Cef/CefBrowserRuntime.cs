using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ludots.UI.Browser;

namespace Ludots.UI.Browser.Cef;

public sealed class CefBrowserRuntime : IBrowserRuntime
{
	public const string LocalAppSchemeName = BrowserLocalAppUri.Scheme;
	public const string LocalAppHostName = BrowserLocalAppUri.Host;

	private readonly List<CefBrowserSurface> _surfaces = new();
	private readonly object _surfacesSync = new();

	private bool _disposed;

	public CefBrowserRuntime(CefBrowserRuntimeOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		CefRuntimeLayoutPreflight.EnsureComplete(options.RuntimeRootPath);
		CefProcessRuntime.AcquireRuntimeOwner(options);
		Info = new BrowserRuntimeInfo(
			BrowserEngineKind.Cef,
			"CefSharp OffScreen",
			ResolveVersion(),
			BrowserEngineCapabilityProfiles.Cef);
	}

	public BrowserRuntimeInfo Info { get; }

	public static void PrepareAssemblyResolution(string runtimeRootPath)
	{
		CefRuntimeLayoutPreflight.EnsureComplete(runtimeRootPath);
		CefProcessRuntime.PrepareAssemblyResolution(runtimeRootPath);
	}

	public ValueTask<IBrowserSurface> CreateSurfaceAsync(
		BrowserViewport viewport,
		IBrowserResourceResolver? resourceResolver = null,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		ThrowIfDisposed();

		var surface = new CefBrowserSurface(viewport, resourceResolver, CefProcessRuntime.SurfaceRegistry);
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
		CefBrowserSurface[] surfaces;
		lock (_surfacesSync)
		{
			surfaces = _surfaces.ToArray();
			_surfaces.Clear();
		}

		foreach (CefBrowserSurface surface in surfaces)
		{
			surface.DisposeAsync().AsTask().GetAwaiter().GetResult();
		}

		CefProcessRuntime.ReleaseRuntimeOwner();
		return ValueTask.CompletedTask;
	}

	private static string ResolveVersion()
	{
		return string.IsNullOrWhiteSpace(global::CefSharp.Cef.CefSharpVersion)
			? "unknown"
			: global::CefSharp.Cef.CefSharpVersion;
	}

	private void ThrowIfDisposed()
	{
		if (_disposed)
		{
			throw new ObjectDisposedException(nameof(CefBrowserRuntime));
		}
	}
}
