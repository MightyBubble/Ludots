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
	}

	[Test]
	public void InstallFromAssemblyLocation_ReturnsExistingCefRuntime()
	{
		var services = new Dictionary<string, object>();
		var existing = new FakeBrowserRuntime(BrowserEngineKind.Cef);
		services[BrowserRuntimeServiceNames.BrowserRuntime] = existing;

		IBrowserRuntime installed = CefBrowserRuntimeHost.InstallFromAssemblyLocation(services);

		Assert.That(installed, Is.SameAs(existing));
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
