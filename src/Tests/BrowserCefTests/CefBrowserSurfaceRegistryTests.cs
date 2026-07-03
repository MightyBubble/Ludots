using CefSharp;
using Ludots.UI.Browser.Cef;
using NUnit.Framework;

namespace Ludots.Tests.BrowserCef;

[TestFixture]
public sealed class CefBrowserSurfaceRegistryTests
{
	[Test]
	public void RegistriesShareResourceResolversAcrossProviderInstances()
	{
		var schemeHandlerRegistry = new CefBrowserSurfaceRegistry();
		var reloadedProviderRegistry = new CefBrowserSurfaceRegistry();
		int browserId = Math.Abs(Guid.NewGuid().GetHashCode());
		string? resolvedUrl = null;

		try
		{
			reloadedProviderRegistry.RegisterResolver(browserId, url =>
			{
				resolvedUrl = url;
				return ResourceHandler.FromString("ok", mimeType: "text/plain");
			});

			bool resolved = schemeHandlerRegistry.TryResolveResource(
				browserId,
				"ludots-app://app.ludots.local/",
				out IResourceHandler? handler);

			Assert.That(resolved, Is.True);
			Assert.That(handler, Is.Not.Null);
			Assert.That(resolvedUrl, Is.EqualTo("ludots-app://app.ludots.local/"));
		}
		finally
		{
			reloadedProviderRegistry.Unregister(browserId);
		}
	}
}
