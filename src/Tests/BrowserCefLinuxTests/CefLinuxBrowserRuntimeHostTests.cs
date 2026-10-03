using Ludots.UI.Browser;
using Ludots.UI.Browser.CefLinux;
using NUnit.Framework;

namespace Ludots.Tests.BrowserCefLinux;

[TestFixture]
public sealed class CefLinuxBrowserRuntimeHostTests
{
	[Test]
	public void Install_ReturnsExistingCefRuntime()
	{
		var services = new Dictionary<string, object>();
		var existing = new FakeBrowserRuntime(BrowserEngineKind.Cef);
		services[BrowserRuntimeServiceNames.BrowserRuntime] = existing;

		IBrowserRuntime installed = CefLinuxBrowserRuntimeHost.Install(
			services,
			Path.GetTempPath(),
			cacheRootPath: null);

		Assert.That(installed, Is.SameAs(existing));
		Assert.That(services[BrowserRuntimeServiceNames.BrowserRuntime], Is.SameAs(existing));
		Assert.That(services[BrowserRuntimeServiceNames.HostLifecycle], Is.InstanceOf<IBrowserRuntimeHostLifecycle>());
	}

	[Test]
	public void Install_RejectsNonBrowserRuntimeService()
	{
		var services = new Dictionary<string, object>
		{
			[BrowserRuntimeServiceNames.BrowserRuntime] = new object()
		};

		Assert.That(
			() => CefLinuxBrowserRuntimeHost.Install(services, Path.GetTempPath()),
			Throws.InvalidOperationException.With.Message.Contains("incompatible type"));
	}

	[Test]
	public void Install_RejectsExistingNonCefRuntime()
	{
		var services = new Dictionary<string, object>();
		services[BrowserRuntimeServiceNames.BrowserRuntime] = new FakeBrowserRuntime(BrowserEngineKind.Ultralight);

		Assert.That(
			() => CefLinuxBrowserRuntimeHost.Install(services, Path.GetTempPath()),
			Throws.InvalidOperationException.With.Message.Contains("existing browser runtime"));
	}

	[Test]
	public void Install_RejectsNonLifecycleService()
	{
		var services = new Dictionary<string, object>
		{
			[BrowserRuntimeServiceNames.BrowserRuntime] = new FakeBrowserRuntime(BrowserEngineKind.Cef),
			[BrowserRuntimeServiceNames.HostLifecycle] = new object()
		};

		Assert.That(
			() => CefLinuxBrowserRuntimeHost.Install(services, Path.GetTempPath()),
			Throws.InvalidOperationException.With.Message.Contains("HostLifecycle"));
	}

	[Test]
	public void Install_MissingRuntimeFilesFailsFastWithCompleteMissingList()
	{
		string runtimeRoot = CreateTempDirectory();
		var services = new Dictionary<string, object>();

		try
		{
			InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
				CefLinuxBrowserRuntimeHost.Install(services, runtimeRoot))!;

			Assert.That(ex.Message, Does.Contain("CEF Linux runtime root is incomplete"));
			Assert.That(ex.Message, Does.Contain(Path.Combine(runtimeRoot, "libcef.so")));
			Assert.That(ex.Message, Does.Contain(Path.Combine(runtimeRoot, "libEGL.so")));
			Assert.That(ex.Message, Does.Contain(Path.Combine(runtimeRoot, "libGLESv2.so")));
			Assert.That(ex.Message, Does.Contain(Path.Combine(runtimeRoot, "resources.pak")));
			Assert.That(ex.Message, Does.Contain(Path.Combine(runtimeRoot, "icudtl.dat")));
			Assert.That(ex.Message, Does.Contain(Path.Combine(runtimeRoot, "v8_context_snapshot.bin")));
			Assert.That(ex.Message, Does.Contain(Path.Combine(runtimeRoot, "locales")));
			Assert.That(ex.Message, Does.Contain(Path.Combine(runtimeRoot, "locales", "en-US.pak")));
			Assert.That(ex.Message, Does.Contain(Path.Combine(runtimeRoot, "Ludots.UI.Browser.CefLinux.Subprocess")));
			Assert.That(ex.Message, Does.Contain(Path.Combine(runtimeRoot, "CefNet.dll")));
			Assert.That(services.ContainsKey(BrowserRuntimeServiceNames.BrowserRuntime), Is.False);
			Assert.That(services.ContainsKey(BrowserRuntimeServiceNames.HostLifecycle), Is.False);
		}
		finally
		{
			DeleteDirectoryIfExists(runtimeRoot);
		}
	}

	[Test]
	public void Install_RejectsNonLinuxHostWhenLayoutIsComplete()
	{
		if (OperatingSystem.IsLinux())
		{
			Assert.Ignore("On Linux the complete layout proceeds into native CEF initialization.");
		}

		string runtimeRoot = CreateCompleteRuntimeLayout();
		var services = new Dictionary<string, object>();

		try
		{
			PlatformNotSupportedException ex = Assert.Throws<PlatformNotSupportedException>(() =>
				CefLinuxBrowserRuntimeHost.Install(services, runtimeRoot))!;

			Assert.That(ex.Message, Does.Contain("cannot load libcef.so"));
			Assert.That(services.ContainsKey(BrowserRuntimeServiceNames.BrowserRuntime), Is.False);
		}
		finally
		{
			DeleteDirectoryIfExists(runtimeRoot);
		}
	}

	private static string CreateTempDirectory()
	{
		string path = Path.Combine(Path.GetTempPath(), "ludots-ceflinux-host-tests-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(path);
		return path;
	}

	private static string CreateCompleteRuntimeLayout()
	{
		string root = CreateTempDirectory();
		string[] files =
		{
			"Ludots.UI.Browser.CefLinux.dll",
			"Ludots.UI.Browser.CefLinux.deps.json",
			"Ludots.UI.Browser.CefLinux.Core.dll",
			"Ludots.UI.Browser.CefLinux.Subprocess",
			"Ludots.UI.Browser.CefLinux.Subprocess.dll",
			"Ludots.UI.Browser.CefLinux.Subprocess.runtimeconfig.json",
			"CefNet.dll",
			"Ludots.UI.Browser.dll",
			"libcef.so",
			"libEGL.so",
			"libGLESv2.so",
			"icudtl.dat",
			"resources.pak",
			"chrome_100_percent.pak",
			"chrome_200_percent.pak",
			"v8_context_snapshot.bin"
		};
		foreach (string file in files)
		{
			File.WriteAllText(Path.Combine(root, file), string.Empty);
		}

		Directory.CreateDirectory(Path.Combine(root, "locales"));
		File.WriteAllText(Path.Combine(root, "locales", "en-US.pak"), string.Empty);
		return root;
	}

	private static void DeleteDirectoryIfExists(string path)
	{
		try
		{
			if (Directory.Exists(path))
			{
				Directory.Delete(path, recursive: true);
			}
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
	}

	private sealed class FakeBrowserRuntime : IBrowserRuntime
	{
		public FakeBrowserRuntime(BrowserEngineKind engineKind)
		{
			Info = new BrowserRuntimeInfo(
				engineKind,
				$"{engineKind} fake",
				"test",
				engineKind == BrowserEngineKind.Cef
					? BrowserEngineCapabilityProfiles.Cef
					: BrowserEngineCapabilityProfiles.Ultralight);
		}

		public BrowserRuntimeInfo Info { get; }

		public ValueTask<IBrowserSurface> CreateSurfaceAsync(
			BrowserViewport viewport,
			IBrowserResourceResolver? resourceResolver = null,
			CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public ValueTask DisposeAsync()
		{
			return ValueTask.CompletedTask;
		}
	}
}
