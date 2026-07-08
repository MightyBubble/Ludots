using Ludots.UI.Browser;
using Ludots.UI.Browser.Cef;
using NUnit.Framework;

namespace Ludots.Tests.BrowserCef;

[TestFixture]
public sealed class CefBrowserRuntimeHostTests
{
	[Test]
	public void Install_ReturnsExistingCefRuntime()
	{
		var services = new Dictionary<string, object>();
		var existing = new FakeBrowserRuntime(BrowserEngineKind.Cef);
		services[BrowserRuntimeServiceNames.BrowserRuntime] = existing;

		IBrowserRuntime installed = CefBrowserRuntimeHost.Install(
			services,
			Path.GetTempPath(),
			cacheRootPath: null);

		Assert.That(installed, Is.SameAs(existing));
		Assert.That(services[BrowserRuntimeServiceNames.BrowserRuntime], Is.SameAs(existing));
		Assert.That(services[BrowserRuntimeServiceNames.HostLifecycle], Is.InstanceOf<IBrowserRuntimeHostLifecycle>());
	}

	[Test]
	public void InstallFromAssemblyLocation_ReturnsExistingCefRuntime()
	{
		var services = new Dictionary<string, object>();
		var existing = new FakeBrowserRuntime(BrowserEngineKind.Cef);
		services[BrowserRuntimeServiceNames.BrowserRuntime] = existing;

		IBrowserRuntime installed = CefBrowserRuntimeHost.InstallFromAssemblyLocation(services);

		Assert.That(installed, Is.SameAs(existing));
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
			() => CefBrowserRuntimeHost.Install(services, Path.GetTempPath()),
			Throws.InvalidOperationException.With.Message.Contains("incompatible type"));
	}

	[Test]
	public void Install_RejectsExistingNonCefRuntime()
	{
		var services = new Dictionary<string, object>();
		services[BrowserRuntimeServiceNames.BrowserRuntime] = new FakeBrowserRuntime(BrowserEngineKind.Ultralight);

		Assert.That(
			() => CefBrowserRuntimeHost.Install(services, Path.GetTempPath()),
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
			() => CefBrowserRuntimeHost.Install(services, Path.GetTempPath()),
			Throws.InvalidOperationException.With.Message.Contains("HostLifecycle"));
	}

	[Test]
	public void Install_MissingCefRuntimeFilesFailsFastWithCompleteMissingList()
	{
		string runtimeRoot = CreateTempDirectory();
		var services = new Dictionary<string, object>();

		try
		{
			InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
				CefBrowserRuntimeHost.Install(services, runtimeRoot))!;

			Assert.That(ex.Message, Does.Contain("CEF runtime root is incomplete"));
			Assert.That(ex.Message, Does.Contain(Path.Combine(runtimeRoot, "libcef.dll")));
			Assert.That(ex.Message, Does.Contain(Path.Combine(runtimeRoot, "resources.pak")));
			Assert.That(ex.Message, Does.Contain(Path.Combine(runtimeRoot, "icudtl.dat")));
			Assert.That(ex.Message, Does.Contain(Path.Combine(runtimeRoot, "locales")));
			Assert.That(ex.Message, Does.Contain(Path.Combine(runtimeRoot, "locales", "en-US.pak")));
			Assert.That(ex.Message, Does.Contain(Path.Combine(runtimeRoot, "chrome_elf.dll")));
			Assert.That(ex.Message, Does.Contain(Path.Combine(runtimeRoot, "v8_context_snapshot.bin")));
			Assert.That(services.ContainsKey(BrowserRuntimeServiceNames.BrowserRuntime), Is.False);
			Assert.That(services.ContainsKey(BrowserRuntimeServiceNames.HostLifecycle), Is.False);
		}
		finally
		{
			DeleteDirectoryIfExists(runtimeRoot);
		}
	}

	[Test]
	public void ProviderLoader_IncompleteCefRuntimeRootFailsFastAndRestoresServices()
	{
		string providerRoot = CopyProviderPackageWithout(
			"libcef.dll",
			"resources.pak",
			"icudtl.dat",
			Path.Combine("locales", "en-US.pak"));
		string shadowRoot = CreateTempDirectory();
		string providerAssemblyPath = Path.Combine(providerRoot, "Ludots.UI.Browser.Cef.dll");
		var existingService = new object();
		var services = new Dictionary<string, object>
		{
			["ExistingService"] = existingService
		};

		try
		{
			InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
				BrowserRuntimeProviderLoader.Install(
					new BrowserRuntimeProviderLoadOptions(
						services,
						providerAssemblyPath,
						"Ludots.UI.Browser.Cef.CefBrowserRuntimeHost")
					{
						ProviderId = "cef",
						RuntimeRootPath = providerRoot,
						ShadowCopyRootPath = shadowRoot
					}))!;

			Assert.That(ex.Message, Does.Contain("CEF runtime root is incomplete"));
			Assert.That(ex.Message, Does.Contain("libcef.dll"));
			Assert.That(ex.Message, Does.Contain("resources.pak"));
			Assert.That(ex.Message, Does.Contain("icudtl.dat"));
			Assert.That(ex.Message, Does.Contain(Path.Combine("locales", "en-US.pak")));
			Assert.That(services.Keys, Is.EquivalentTo(new[] { "ExistingService" }));
			Assert.That(services["ExistingService"], Is.SameAs(existingService));
		}
		finally
		{
			DeleteDirectoryIfExists(providerRoot);
			DeleteDirectoryIfExists(shadowRoot);
		}
	}

	[Test]
	public void PrepareAssemblyResolution_MissingCefRuntimeFilesFailsBeforeCefSharpLoad()
	{
		string runtimeRoot = CreateTempDirectory();

		try
		{
			InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
				CefBrowserRuntime.PrepareAssemblyResolution(runtimeRoot))!;

			Assert.That(ex.Message, Does.Contain("CEF runtime root is incomplete"));
			Assert.That(ex.Message, Does.Contain(Path.Combine(runtimeRoot, "CefSharp.dll")));
			Assert.That(ex.Message, Does.Contain(Path.Combine(runtimeRoot, "CefSharp.OffScreen.dll")));
			Assert.That(ex.Message, Does.Contain(Path.Combine(runtimeRoot, "libcef.dll")));
		}
		finally
		{
			DeleteDirectoryIfExists(runtimeRoot);
		}
	}

	private static string CreateTempDirectory()
	{
		string path = Path.Combine(Path.GetTempPath(), "ludots-cef-host-tests-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(path);
		return path;
	}

	private static string CopyProviderPackageWithout(params string[] omittedRelativePaths)
	{
		string sourceDirectory = AppContext.BaseDirectory;
		string targetDirectory = CreateTempDirectory();
		var omitted = new HashSet<string>(omittedRelativePaths, StringComparer.OrdinalIgnoreCase);

		foreach (string sourceFile in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
		{
			string relativePath = Path.GetRelativePath(sourceDirectory, sourceFile);
			if (omitted.Contains(relativePath))
			{
				continue;
			}

			string targetFile = Path.Combine(targetDirectory, relativePath);
			Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
			File.Copy(sourceFile, targetFile, overwrite: true);
		}

		return targetDirectory;
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
