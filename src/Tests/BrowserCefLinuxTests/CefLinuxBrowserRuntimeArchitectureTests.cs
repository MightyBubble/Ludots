using NUnit.Framework;

namespace Ludots.Tests.BrowserCefLinux;

[TestFixture]
public sealed class CefLinuxBrowserRuntimeArchitectureTests
{
	[Test]
	public void BrowserRuntimeFacade_DoesNotOwnProcessCefLifetime()
	{
		string repoRoot = FindRepoRoot();
		string runtimeSource = File.ReadAllText(Path.Combine(
			repoRoot,
			"src",
			"Libraries",
			"Ludots.UI.Browser.CefLinux",
			"CefLinuxBrowserRuntime.cs"));
		string processRuntimeSource = File.ReadAllText(Path.Combine(
			repoRoot,
			"src",
			"Libraries",
			"Ludots.UI.Browser.CefLinux",
			"CefLinuxProcessRuntime.cs"));
		string hostSource = File.ReadAllText(Path.Combine(
			repoRoot,
			"src",
			"Libraries",
			"Ludots.UI.Browser.CefLinux",
			"CefLinuxBrowserRuntimeHost.cs"));

		Assert.That(runtimeSource, Does.Not.Contain("CefApi.Initialize"));
		Assert.That(runtimeSource, Does.Not.Contain("new CefSettings"));
		Assert.That(runtimeSource, Does.Not.Contain("CefApi.Shutdown"));
		Assert.That(hostSource, Does.Not.Contain("InstallFromAssemblyLocation"));

		Assert.That(processRuntimeSource, Does.Contain("CefApi.Initialize"));
		Assert.That(processRuntimeSource, Does.Contain("NoSandbox = true"));
		Assert.That(processRuntimeSource, Does.Contain("ExternalMessagePump = true"));
		Assert.That(processRuntimeSource, Does.Contain("RegisterSchemeHandlerFactory"));
		Assert.That(processRuntimeSource, Does.Contain("ShutdownForHostExit"));
		Assert.That(processRuntimeSource, Does.Contain("CEF host exit shutdown has already been requested"));
		Assert.That(processRuntimeSource, Does.Contain("CefLinuxSchemeHandlerFactory"));
	}

	[Test]
	public void BrowserSurfaceRegistry_UsesProcessScopedStorageForSchemeHandlers()
	{
		string repoRoot = FindRepoRoot();
		string registrySource = File.ReadAllText(Path.Combine(
			repoRoot,
			"src",
			"Libraries",
			"Ludots.UI.Browser.CefLinux",
			"CefLinuxBrowserSurfaceRegistry.cs"));

		Assert.That(registrySource, Does.Contain("AppDomain.CurrentDomain.GetData"));
		Assert.That(registrySource, Does.Contain("AppDomain.CurrentDomain.SetData"));
		Assert.That(registrySource, Does.Not.Contain("ConcurrentDictionary<int, CefLinuxBrowserSurface>"));
	}

	[Test]
	public void Preflight_RequiresLinuxNativeLayoutAndDedicatedSubprocess()
	{
		string repoRoot = FindRepoRoot();
		string preflightSource = File.ReadAllText(Path.Combine(
			repoRoot,
			"src",
			"Libraries",
			"Ludots.UI.Browser.CefLinux",
			"CefLinuxRuntimeLayoutPreflight.cs"));

		Assert.That(preflightSource, Does.Contain("libcef.so"));
		Assert.That(preflightSource, Does.Contain("libEGL.so"));
		Assert.That(preflightSource, Does.Contain("libGLESv2.so"));
		Assert.That(preflightSource, Does.Contain("v8_context_snapshot.bin"));
		Assert.That(preflightSource, Does.Contain("Ludots.UI.Browser.CefLinux.Subprocess"));
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
