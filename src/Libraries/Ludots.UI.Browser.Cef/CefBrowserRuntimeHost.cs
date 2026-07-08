using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Ludots.UI.Browser;

namespace Ludots.UI.Browser.Cef;

public static class CefBrowserRuntimeHost
{
	public static IBrowserRuntime InstallFromAssemblyLocation(
		IDictionary<string, object> services,
		string? cacheRootPath = null)
	{
		string runtimeRootPath = ResolveAssemblyDirectory();
		return Install(services, runtimeRootPath, cacheRootPath);
	}

	public static IBrowserRuntime Install(
		IDictionary<string, object> services,
		string runtimeRootPath,
		string? cacheRootPath = null)
	{
		ArgumentNullException.ThrowIfNull(services);
		if (string.IsNullOrWhiteSpace(runtimeRootPath))
		{
			throw new ArgumentException("CEF runtime root path is required.", nameof(runtimeRootPath));
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
					$"CEF browser runtime install requested, but existing browser runtime is '{existingRuntime.Info.EngineKind}'.");
			}

			EnsureHostLifecycleRegistered(services);
			return existingRuntime;
		}

		CefRuntimeLayoutPreflight.EnsureComplete(runtimeRootPath);
		var runtime = new CefBrowserRuntime(new CefBrowserRuntimeOptions(runtimeRootPath, cacheRootPath));
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

		services[BrowserRuntimeServiceNames.HostLifecycle] = new CefHostLifecycle();
	}

	private sealed class CefHostLifecycle : IBrowserRuntimeHostLifecycle
	{
		public void ShutdownProcessForHostExit()
		{
			CefProcessRuntime.ShutdownForHostExit();
		}
	}

	private static string ResolveAssemblyDirectory()
	{
		string assemblyLocation = typeof(CefBrowserRuntimeHost).Assembly.Location;
		if (string.IsNullOrWhiteSpace(assemblyLocation))
		{
			assemblyLocation = Assembly.GetExecutingAssembly().Location;
		}

		string? directory = Path.GetDirectoryName(assemblyLocation);
		if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
		{
			throw new DirectoryNotFoundException(
				$"CEF runtime root could not be resolved from provider assembly location '{assemblyLocation}'.");
		}

		return directory;
	}
}
