using System.Text;
using Ludots.Core.Modding;
using Ludots.UI.Browser;
using Ludots.WebUI.Browser;
using NUnit.Framework;

namespace Ludots.Tests.WebUiDataPlane;

[TestFixture]
public sealed class VfsBrowserResourceResolverTests
{
	[Test]
	public async Task ResolveAsync_ReadsMountedAsset_AndKeepsModIdCase()
	{
		string root = CreateTempDirectory();
		try
		{
			byte[] png = [137, 80, 78, 71, 1, 2, 3];
			string asset = Path.Combine(root, "assets", "Presentation");
			Directory.CreateDirectory(asset);
			await File.WriteAllBytesAsync(Path.Combine(asset, "mark.png"), png);
			await File.WriteAllTextAsync(Path.Combine(root, "mod.json"), "{}");
			var vfs = new VirtualFileSystem();
			vfs.Mount("WebUiVfsShowcaseMod", root);
			IBrowserResourceResolver resolver = new VfsBrowserResourceResolver(vfs);
			var uri = new Uri(BrowserVfsUri.FromVfsPath("WebUiVfsShowcaseMod:assets/Presentation/mark.png"));

			BrowserResource? resource = await resolver.ResolveAsync(uri);

			Assert.That(uri.Host, Is.EqualTo("webuivfsshowcasemod"));
			Assert.That(resource, Is.Not.Null);
			Assert.That(resource!.ContentType, Is.EqualTo("image/png"));
			Assert.That(resource.Content.ToArray(), Is.EqualTo(png));
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[Test]
	public async Task ResolveAsync_UnloadedModAndEscapeAndDirectory_FailClosed()
	{
		string root = CreateTempDirectory();
		string outside = Path.Combine(Path.GetDirectoryName(root)!, "secret.png");
		try
		{
			string asset = Path.Combine(root, "assets", "Presentation");
			Directory.CreateDirectory(asset);
			await File.WriteAllBytesAsync(Path.Combine(asset, "mark.png"), [1, 2, 3, 4]);
			await File.WriteAllBytesAsync(outside, [9, 9, 9]);
			var vfs = new VirtualFileSystem();
			vfs.Mount("WebUiVfsShowcaseMod", root);
			var resolver = new VfsBrowserResourceResolver(vfs);

			Assert.That(await resolver.ResolveAsync(new Uri("ludots-vfs://OtherMod/assets/Presentation/mark.png")), Is.Null);
			Assert.That(await resolver.ResolveAsync(new Uri("ludots-vfs://webuivfsshowcasemod/assets/Presentation/mark.png")), Is.Null);
			Assert.That(await resolver.ResolveAsync(new Uri("ludots-vfs://WebUiVfsShowcaseMod/assets/../secret.png")), Is.Null);
			Assert.That(await resolver.ResolveAsync(new Uri("ludots-vfs://WebUiVfsShowcaseMod/assets/%2e%2e/secret.png")), Is.Null);
			Assert.That(await resolver.ResolveAsync(new Uri("ludots-vfs://WebUiVfsShowcaseMod/mod.json")), Is.Null);
			Assert.That(await resolver.ResolveAsync(new Uri("ludots-vfs://WebUiVfsShowcaseMod/assets/Presentation")), Is.Null);
		}
		finally
		{
			if (File.Exists(outside))
			{
				File.Delete(outside);
			}

			Directory.Delete(root, recursive: true);
		}
	}

	[Test]
	public async Task PanelResolver_KeepsDistRoot_AndDoesNotFallThroughVfsMisses()
	{
		string appRoot = CreateTempDirectory();
		string modRoot = CreateTempDirectory();
		try
		{
			await File.WriteAllTextAsync(Path.Combine(appRoot, "index.html"), "<p>dist</p>");
			await File.WriteAllBytesAsync(Path.Combine(appRoot, "decoy.png"), [7, 7]);
			Directory.CreateDirectory(Path.Combine(modRoot, "assets", "Presentation"));
			await File.WriteAllBytesAsync(Path.Combine(modRoot, "assets", "Presentation", "mark.png"), [4, 5, 6]);
			var vfs = new VirtualFileSystem();
			vfs.Mount("WebUiVfsShowcaseMod", modRoot);
			IBrowserResourceResolver resolver = WebPanelBrowserResources.Create(appRoot, vfs);

			BrowserResource? page = await resolver.ResolveAsync(BrowserLocalAppUri.Root);
			BrowserResource? image = await resolver.ResolveAsync(
				new Uri("ludots-vfs://WebUiVfsShowcaseMod/assets/Presentation/mark.png"));
			BrowserResource? missing = await resolver.ResolveAsync(
				new Uri("ludots-vfs://OtherMod/assets/Presentation/mark.png"));

			Assert.That(page, Is.Not.Null);
			Assert.That(Encoding.UTF8.GetString(page!.Content.Span), Does.Contain("dist"));
			Assert.That(image, Is.Not.Null);
			Assert.That(image!.Content.ToArray(), Is.EqualTo(new byte[] { 4, 5, 6 }));
			Assert.That(missing, Is.Null);
		}
		finally
		{
			Directory.Delete(appRoot, recursive: true);
			Directory.Delete(modRoot, recursive: true);
		}
	}

	private static string CreateTempDirectory()
	{
		string path = Path.Combine(Path.GetTempPath(), "ludots-vfs-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(path);
		return path;
	}
}
