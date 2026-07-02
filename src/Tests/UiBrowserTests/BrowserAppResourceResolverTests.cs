using System.Text;
using Ludots.UI.Browser;
using NUnit.Framework;

namespace Ludots.Tests.UiBrowser;

[TestFixture]
public sealed class BrowserAppResourceResolverTests
{
	[Test]
	public async Task ResolveAsync_LoadsPackagedWebAppIndex()
	{
		string root = CreateTempAppRoot();
		try
		{
			string html = "<!doctype html><script type=\"module\" src=\"/main.js\"></script>";
			await File.WriteAllTextAsync(Path.Combine(root, "index.html"), html, Encoding.UTF8);
			var resolver = new BrowserAppResourceResolver(root);

			BrowserResource? resource = await resolver.ResolveAsync(BrowserLocalAppUri.Root);

			Assert.That(resource, Is.Not.Null);
			Assert.That(resource!.ContentType, Is.EqualTo("text/html; charset=utf-8"));
			Assert.That(Encoding.UTF8.GetString(resource.Content.Span), Does.Contain("main.js"));
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[Test]
	public async Task ResolveAsync_LoadsWasmWithBrowserContentType()
	{
		string root = CreateTempAppRoot();
		try
		{
			string assets = Path.Combine(root, "assets");
			Directory.CreateDirectory(assets);
			await File.WriteAllBytesAsync(Path.Combine(assets, "app.wasm"), [0, 97, 115, 109]);
			var resolver = new BrowserAppResourceResolver(root);

			BrowserResource? resource = await resolver.ResolveAsync(BrowserLocalAppUri.Create("/assets/app.wasm"));

			Assert.That(resource, Is.Not.Null);
			Assert.That(resource!.ContentType, Is.EqualTo("application/wasm"));
			Assert.That(resource.Content.ToArray(), Is.EqualTo(new byte[] { 0, 97, 115, 109 }));
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[Test]
	public void BrowserLocalAppUri_UsesStandardHostForCefCustomSchemeNavigation()
	{
		Assert.That(BrowserLocalAppUri.Scheme, Is.EqualTo("ludots-app"));
		Assert.That(BrowserLocalAppUri.Host, Is.EqualTo("app.ludots.local"));
		Assert.That(BrowserLocalAppUri.Root.AbsoluteUri, Is.EqualTo("ludots-app://app.ludots.local/"));
		Assert.That(BrowserLocalAppUri.Create("/", "perf=baseline").AbsoluteUri, Is.EqualTo("ludots-app://app.ludots.local/?perf=baseline"));
	}

	[Test]
	public async Task ResolveAsync_RejectsPathTraversalOutsideAppRoot()
	{
		string root = CreateTempAppRoot();
		try
		{
			string outside = Path.Combine(Path.GetDirectoryName(root)!, "secret.txt");
			await File.WriteAllTextAsync(outside, "nope", Encoding.UTF8);
			var resolver = new BrowserAppResourceResolver(root);

			BrowserResource? resource = await resolver.ResolveAsync(new Uri("ludots-app://ui/../secret.txt"));

			Assert.That(resource, Is.Null);
			File.Delete(outside);
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[Test]
	public async Task ResolveAsync_RejectsEncodedPathTraversalOutsideAppRoot()
	{
		string root = CreateTempAppRoot();
		try
		{
			string outside = Path.Combine(Path.GetDirectoryName(root)!, "secret.txt");
			await File.WriteAllTextAsync(outside, "nope", Encoding.UTF8);
			var resolver = new BrowserAppResourceResolver(root);

			BrowserResource? resource = await resolver.ResolveAsync(new Uri("ludots-app://ui/%2e%2e/secret.txt"));

			Assert.That(resource, Is.Null);
			File.Delete(outside);
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	private static string CreateTempAppRoot()
	{
		string root = Path.Combine(Path.GetTempPath(), "ludots-browser-ui-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		return root;
	}
}
