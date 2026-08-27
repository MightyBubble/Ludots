using System.Runtime.InteropServices;
using Ludots.UI.Browser;
using Ludots.UI.Browser.Ultralight;
using NUnit.Framework;

namespace Ludots.Tests.BrowserUltralight;

[TestFixture]
public sealed class UltralightBrowserRuntimeHostTests
{
	[Test]
	public void Install_ReturnsExistingUltralightRuntime()
	{
		var services = new Dictionary<string, object>();
		var existing = new FakeBrowserRuntime(BrowserEngineKind.Ultralight);
		services[BrowserRuntimeServiceNames.BrowserRuntime] = existing;

		IBrowserRuntime installed = UltralightBrowserRuntimeHost.Install(
			services,
			Path.GetTempPath(),
			cacheRootPath: null);

		Assert.That(installed, Is.SameAs(existing));
		Assert.That(services[BrowserRuntimeServiceNames.HostLifecycle], Is.InstanceOf<IBrowserRuntimeHostLifecycle>());
	}

	[Test]
	public void Install_RejectsExistingNonUltralightRuntime()
	{
		var services = new Dictionary<string, object>
		{
			[BrowserRuntimeServiceNames.BrowserRuntime] = new FakeBrowserRuntime(BrowserEngineKind.Cef)
		};

		Assert.That(
			() => UltralightBrowserRuntimeHost.Install(services, Path.GetTempPath()),
			Throws.InvalidOperationException.With.Message.Contains("existing browser runtime"));
	}

	[Test]
	public void Preflight_MissingManagedAssemblyFailsLoud()
	{
		string runtimeRoot = CreateTempDirectory();
		try
		{
			InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
				UltralightRuntimeLayoutPreflight.EnsureComplete(runtimeRoot))!;
			Assert.That(ex.Message, Does.Contain("Ultralight runtime root is incomplete"));
			Assert.That(ex.Message, Does.Contain("UltralightNet.dll"));
		}
		finally
		{
			Directory.Delete(runtimeRoot, recursive: true);
		}
	}

	[Test]
	public void Preflight_RequiresPlatformNativeLibraryPaths()
	{
		string runtimeRoot = CreateTempDirectory();
		try
		{
			IReadOnlyList<string> natives = UltralightRuntimeLayoutPreflight.EnumerateRequiredNativeLibraryPaths(runtimeRoot);
			Assert.That(natives, Is.Not.Empty);
			if (OperatingSystem.IsLinux())
			{
				Assert.That(natives.Any(path => path.EndsWith("libUltralight.so", StringComparison.Ordinal)), Is.True);
			}
			else if (OperatingSystem.IsWindows())
			{
				Assert.That(natives.Any(path => path.EndsWith("Ultralight.dll", StringComparison.OrdinalIgnoreCase)), Is.True);
			}
		}
		finally
		{
			Directory.Delete(runtimeRoot, recursive: true);
		}
	}

	private static string CreateTempDirectory()
	{
		string path = Path.Combine(Path.GetTempPath(), "ludots-ultralight-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(path);
		return path;
	}

	private sealed class FakeBrowserRuntime : IBrowserRuntime
	{
		public FakeBrowserRuntime(BrowserEngineKind kind)
		{
			Info = new BrowserRuntimeInfo(kind, "fake", "0", BrowserEngineCapabilityProfiles.Ultralight);
		}

		public BrowserRuntimeInfo Info { get; }

		public ValueTask<IBrowserSurface> CreateSurfaceAsync(
			BrowserViewport viewport,
			IBrowserResourceResolver? resourceResolver = null,
			CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}
}
