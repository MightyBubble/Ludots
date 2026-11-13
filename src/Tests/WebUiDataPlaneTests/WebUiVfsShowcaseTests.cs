using System.Text.Json;
using Ludots.Core.Presentation.Hud;
using Ludots.UI.Browser;
using Ludots.WebUI.Browser;
using NUnit.Framework;
using WebUiVfsShowcaseMod;

namespace Ludots.Tests.WebUiDataPlane;

[TestFixture]
public sealed class WebUiVfsShowcaseTests
{
	[Test]
	public async Task ShowcasePage_ShowsThePictureRegisteredInPresentationAssets()
	{
		string modRoot = Path.Combine(FindRepoRoot(), "mods", "showcases", "webui_vfs_assets", "WebUiVfsShowcaseMod");
		string template = await File.ReadAllTextAsync(Path.Combine(modRoot, "assets", "browser-app", "index.html"));
		string imageAssets = await File.ReadAllTextAsync(Path.Combine(modRoot, "assets", "Presentation", "image_assets.json"));
		string pageSource = await File.ReadAllTextAsync(Path.Combine(modRoot, "WebUiVfsShowcasePage.cs"));
		byte[] png = await File.ReadAllBytesAsync(Path.Combine(modRoot, "assets", "Presentation", "mark.png"));

		Assert.That(template, Does.Contain("data-image-asset=\"showcase.webui.vfs.mark\""));
		Assert.That(template, Does.Contain("src=\"{{image-src}}\""));
		Assert.That(template, Does.Not.Contain("ludots-vfs:"));
		Assert.That(pageSource, Does.Not.Contain("ludots-vfs:"));
		Assert.That(pageSource, Does.Not.Contain("mark.png"));

		using JsonDocument document = JsonDocument.Parse(imageAssets);
		JsonElement entry = document.RootElement.EnumerateArray().Single();
		Assert.That(entry.GetProperty("id").GetString(), Is.EqualTo("showcase.webui.vfs.mark"));
		string mountedPath = entry.GetProperty("path").GetString() ?? string.Empty;
		var catalog = new PresentationImageAssetCatalog();
		catalog.Register(new PresentationImageAssetDefinition
		{
			Id = entry.GetProperty("id").GetString()!,
			Kind = PresentationImageKind.Icon,
			Path = mountedPath
		});

		string html = WebUiVfsShowcasePage.Compose(template, catalog);
		string address = BrowserVfsUri.FromVfsPath(mountedPath);

		Assert.That(html, Does.Contain($"<img data-image-asset=\"showcase.webui.vfs.mark\" alt=\"一枚深蓝底、金色纹章的图\" src=\"{address}\">"));
		Assert.That(html, Does.Not.Contain("{{image-src}}"));
		Assert.That(address, Is.EqualTo("ludots-vfs://WebUiVfsShowcaseMod/assets/Presentation/mark.png"));

		var vfs = new Ludots.Core.Modding.VirtualFileSystem();
		vfs.Mount("WebUiVfsShowcaseMod", modRoot);
		BrowserResource? resource = await new VfsBrowserResourceResolver(vfs).ResolveAsync(new Uri(address));
		BrowserResource? unloaded = await new VfsBrowserResourceResolver(vfs).ResolveAsync(
			new Uri("ludots-vfs://NotLoadedMod/assets/Presentation/mark.png"));

		Assert.That(resource, Is.Not.Null);
		Assert.That(resource!.Content.ToArray(), Is.EqualTo(png));
		Assert.That(unloaded, Is.Null);
	}

	private static string FindRepoRoot()
	{
		string? directory = AppContext.BaseDirectory;
		while (!string.IsNullOrEmpty(directory))
		{
			if (File.Exists(Path.Combine(directory, "showcase.registry.json")))
			{
				return directory;
			}

			directory = Directory.GetParent(directory)?.FullName;
		}

		throw new InvalidOperationException("Repository root was not found.");
	}
}
