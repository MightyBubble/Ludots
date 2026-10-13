using System;
using System.Collections.Generic;
using Ludots.UI.Browser;

namespace Ludots.UI.Browser.CefLinux;

public static class CefLinuxBrowserRuntimeHost
{
	public static IBrowserRuntime Install(
		IDictionary<string, object> services,
		string runtimeRootPath,
		string? cacheRootPath = null)
	{
		ArgumentNullException.ThrowIfNull(services);
		if (string.IsNullOrWhiteSpace(runtimeRootPath))
		{
			throw new ArgumentException("CEF Linux runtime root path is required.", nameof(runtimeRootPath));
		}

		if (services.TryGetValue(BrowserRuntimeServiceNames.BrowserRuntime, out object? existing))
		{
			if (existing is not IBrowserRuntime existingRuntime)
			{
				throw new InvalidOperationException(
					$"Browser runtime service '{BrowserRuntimeServiceNames.BrowserRuntime}' is already registered with incompatible type '{existing.GetType().FullName}'.");
			}

			if (existingRuntime.Info.EngineKind != BrowserEngineKind.Cef)
			{
				throw new InvalidOperationException(
					$"CEF Linux browser runtime install requested, but existing browser runtime is '{existingRuntime.Info.EngineKind}'.");
			}

			EnsureHostLifecycleRegistered(services);
			return existingRuntime;
		}

		CefLinuxRuntimeLayoutPreflight.EnsureComplete(runtimeRootPath);
		var runtime = new CefLinuxBrowserRuntime(new CefLinuxBrowserRuntimeOptions(runtimeRootPath, cacheRootPath));
		services[BrowserRuntimeServiceNames.BrowserRuntime] = runtime;
		EnsureHostLifecycleRegistered(services);
		return runtime;
	}

	private static void EnsureHostLifecycleRegistered(IDictionary<string, object> services)
	{
		if (services.TryGetValue(BrowserRuntimeServiceNames.HostLifecycle, out object? existing))
		{
			if (existing is not IBrowserRuntimeHostLifecycle)
			{
				throw new InvalidOperationException(
					$"Browser runtime service '{BrowserRuntimeServiceNames.HostLifecycle}' is already registered with incompatible type '{existing.GetType().FullName}'.");
			}

			return;
		}

		services[BrowserRuntimeServiceNames.HostLifecycle] = new CefLinuxHostLifecycle();
	}

	private sealed class CefLinuxHostLifecycle : IBrowserRuntimeHostLifecycle
	{
		public void ShutdownProcessForHostExit()
		{
			CefLinuxProcessRuntime.ShutdownForHostExit();
		}
	}
}
