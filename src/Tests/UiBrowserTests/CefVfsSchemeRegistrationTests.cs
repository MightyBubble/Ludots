using Ludots.UI.Browser;
using NUnit.Framework;

namespace Ludots.Tests.UiBrowser;

[TestFixture]
public sealed class CefVfsSchemeRegistrationTests
{
	[Test]
	public void VfsScheme_IsRegisteredWithoutHostCanonicalization()
	{
		string source = ReadRepoFile("src", "Libraries", "Ludots.UI.Browser.Cef", "CefProcessRuntime.cs");
		int localApp = source.IndexOf("CefBrowserRuntime.LocalAppSchemeName", StringComparison.Ordinal);
		int vfs = source.IndexOf("BrowserVfsUri.Scheme", StringComparison.Ordinal);
		Assert.That(localApp, Is.GreaterThanOrEqualTo(0));
		Assert.That(vfs, Is.GreaterThan(localApp));

		string vfsRegistration = source[vfs..];
		int nextMethod = vfsRegistration.IndexOf("private static class", StringComparison.Ordinal);
		if (nextMethod > 0)
		{
			vfsRegistration = vfsRegistration[..nextMethod];
		}

		Assert.That(vfsRegistration, Does.Contain("IsStandard = false"));
		Assert.That(vfsRegistration, Does.Contain("SchemeHandlerFactory"));
		Assert.That(BrowserVfsUri.Scheme, Is.EqualTo("ludots-vfs"));
	}

	[Test]
	public void ResolveResource_KeepsTheRequestTextForTheResolver()
	{
		string source = ReadRepoFile("src", "Libraries", "Ludots.UI.Browser.Cef", "CefBrowserSurface.cs");
		int resolve = source.IndexOf("internal IResourceHandler? ResolveResource", StringComparison.Ordinal);
		Assert.That(resolve, Is.GreaterThanOrEqualTo(0));
		string method = source[resolve..];
		int nextMethod = method.IndexOf("internal ValueTask PostHostMessageAsync", StringComparison.Ordinal);
		Assert.That(nextMethod, Is.GreaterThan(0));
		method = method[..nextMethod];
		Assert.That(method, Does.Contain("Uri.TryCreate(requestUrl, UriKind.Absolute, out Uri? uri)"));
		Assert.That(method, Does.Not.Contain("uri.AbsoluteUri"));
		Assert.That(method, Does.Not.Contain("uri.Host"));
	}

	private static string ReadRepoFile(params string[] segments)
	{
		string? directory = AppContext.BaseDirectory;
		while (!string.IsNullOrEmpty(directory))
		{
			string candidate = Path.Combine(new[] { directory }.Concat(segments).ToArray());
			if (File.Exists(candidate))
			{
				return File.ReadAllText(candidate);
			}

			directory = Directory.GetParent(directory)?.FullName;
		}

		throw new FileNotFoundException("Repository file was not found.", Path.Combine(segments));
	}
}
