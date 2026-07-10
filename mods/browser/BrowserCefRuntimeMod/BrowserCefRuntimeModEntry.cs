using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.UI.Browser;
using Ludots.UI.Browser.Cef;

namespace BrowserCefRuntimeMod;

public sealed class BrowserCefRuntimeModEntry : IMod
{
	private const string BrowserServiceKeyName = "BrowserRuntime";

	private readonly ServiceKey<IBrowserRuntime> _browserRuntimeKey = new(BrowserServiceKeyName);
	private CefBrowserRuntime? _runtime;
	private string? _runtimeRootPath;

	public void OnLoad(IModContext context)
	{
		ArgumentNullException.ThrowIfNull(context);
		_runtimeRootPath = ResolveRuntimeRootPath(context);
		CefBrowserRuntime.PrepareAssemblyResolution(_runtimeRootPath);
		context.Log($"[BrowserCefRuntimeMod] Prepared CEF runtime root: {_runtimeRootPath}");
		context.OnEvent(GameEvents.GameStart, InstallRuntimeAsync);
	}

	public void OnUnload()
	{
		if (_runtime != null)
		{
			_runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
			_runtime = null;
		}
	}

	private Task InstallRuntimeAsync(ScriptContext context)
	{
		ArgumentNullException.ThrowIfNull(context);
		try
		{
			GameEngine engine = context.GetEngine()
				?? throw new InvalidOperationException("GameEngine service is missing from ScriptContext.");

			if (engine.TryGetService(_browserRuntimeKey, out IBrowserRuntime? existingRuntime) && existingRuntime != null)
			{
				context.Set(_browserRuntimeKey, existingRuntime);
				return Task.CompletedTask;
			}

			string runtimeRootPath = _runtimeRootPath
				?? throw new InvalidOperationException("CEF runtime root path was not resolved during mod load.");
			string cacheRootPath = Path.Combine(
				Path.GetTempPath(),
				"Ludots",
				"BrowserCefRuntime",
				$"Process-{Process.GetCurrentProcess().Id}");
			_runtime ??= new CefBrowserRuntime(new CefBrowserRuntimeOptions(
				runtimeRootPath,
				cacheRootPath));

			engine.SetService(_browserRuntimeKey, _runtime);
			context.Set(_browserRuntimeKey, _runtime);
			return Task.CompletedTask;
		}
		catch (Exception ex)
		{
			throw new InvalidOperationException($"BrowserCefRuntimeMod failed to install CEF runtime: {ex}", ex);
		}
	}

	private static string ResolveRuntimeRootPath(IModContext context)
	{
		if (!context.VFS.TryResolveFullPath("BrowserCefRuntimeMod:mod.json", out string manifestPath))
		{
			throw new InvalidOperationException("BrowserCefRuntimeMod root path could not be resolved from VFS.");
		}

		string? modRoot = Path.GetDirectoryName(manifestPath);
		if (string.IsNullOrWhiteSpace(modRoot) || !Directory.Exists(modRoot))
		{
			throw new DirectoryNotFoundException($"BrowserCefRuntimeMod root directory was not found: {modRoot}");
		}

		string outputRoot = Path.Combine(modRoot, "bin", "net8.0");
		if (!Directory.Exists(outputRoot))
		{
			throw new DirectoryNotFoundException($"CEF runtime output directory was not found: {outputRoot}");
		}

		return outputRoot;
	}
}
