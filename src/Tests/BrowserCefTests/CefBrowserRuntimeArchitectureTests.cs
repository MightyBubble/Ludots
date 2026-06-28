using NUnit.Framework;

namespace Ludots.Tests.BrowserCef;

[TestFixture]
public sealed class CefBrowserRuntimeArchitectureTests
{
	[Test]
	public void BrowserRuntimeFacade_DoesNotOwnProcessCefLifetime()
	{
		string repoRoot = FindRepoRoot();
		string runtimeSource = File.ReadAllText(Path.Combine(
			repoRoot,
			"src",
			"Libraries",
			"Ludots.UI.Browser.Cef",
			"CefBrowserRuntime.cs"));
		string processRuntimeSource = File.ReadAllText(Path.Combine(
			repoRoot,
			"src",
			"Libraries",
			"Ludots.UI.Browser.Cef",
			"CefProcessRuntime.cs"));

		Assert.That(runtimeSource, Does.Not.Contain("Cef.Initialize"));
		Assert.That(runtimeSource, Does.Not.Contain("CefSettings"));
		Assert.That(runtimeSource, Does.Not.Contain("AssemblyLoadContext.Default.Resolving"));
		Assert.That(runtimeSource, Does.Not.Contain("CefBrowserSurfaceRegistry = new"));
		Assert.That(runtimeSource, Does.Not.Contain("Cef.Shutdown"));

		Assert.That(processRuntimeSource, Does.Contain("Cef.Initialize"));
		Assert.That(processRuntimeSource, Does.Contain("CefBrowserSurfaceRegistry"));
		Assert.That(processRuntimeSource, Does.Contain("CefBrowserSchemeHandlerFactory"));
		Assert.That(processRuntimeSource, Does.Not.Contain("Cef.Shutdown"));
	}

	[Test]
	public void BrowserSurfaceRegistry_UsesProcessScopedStorageForSchemeHandlers()
	{
		string repoRoot = FindRepoRoot();
		string registrySource = File.ReadAllText(Path.Combine(
			repoRoot,
			"src",
			"Libraries",
			"Ludots.UI.Browser.Cef",
			"CefBrowserSurfaceRegistry.cs"));

		Assert.That(registrySource, Does.Contain("AppDomain.CurrentDomain.GetData"));
		Assert.That(registrySource, Does.Contain("AppDomain.CurrentDomain.SetData"));
		Assert.That(registrySource, Does.Not.Contain("ConcurrentDictionary<int, CefBrowserSurface>"));
	}

	private static string FindRepoRoot()
	{
		var current = new DirectoryInfo(AppContext.BaseDirectory);
		while (current != null)
		{
			if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")) &&
				Directory.Exists(Path.Combine(current.FullName, "src")) &&
				Directory.Exists(Path.Combine(current.FullName, "mods")))
			{
				return current.FullName;
			}

			current = current.Parent;
		}

		throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
	}
}
