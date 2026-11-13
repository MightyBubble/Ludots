using System.Text;
using Ludots.UI.Browser;
using Ludots.UI.Browser.Ultralight;
using NUnit.Framework;

namespace Ludots.Tests.BrowserUltralight;

[TestFixture]
public sealed class UltralightVfsStagingTests
{
	[Test]
	public async Task StageAsync_RewritesLoadedVfsImage_AndLeavesUnloadedModUntouched()
	{
		byte[] png = [137, 80, 78, 71, 13, 10, 26, 10, 9];
		string stagingRoot = Path.Combine(Path.GetTempPath(), "ludots-ultralight-vfs-" + Guid.NewGuid().ToString("N"));
		var resolver = new RecordingResolver(png);
		try
		{
			string fileUrl = await UltralightLocalAppStager.StageAsync(
				stagingRoot,
				BrowserLocalAppUri.Root,
				resolver,
				CancellationToken.None);

			Assert.That(fileUrl, Does.StartWith("file:"));
			string html = await File.ReadAllTextAsync(Path.Combine(stagingRoot, "index.html"));
			Assert.That(html, Does.Contain("styles.css"));
			Assert.That(File.Exists(Path.Combine(stagingRoot, "styles.css")), Is.True);
			Assert.That(html, Does.Not.Contain("ludots-vfs://WebUiVfsShowcaseMod/"));
			Assert.That(html, Does.Contain("ludots-vfs://MissingMod/assets/Presentation/missing.png"));
			Assert.That(resolver.VfsOriginals, Does.Contain("ludots-vfs://WebUiVfsShowcaseMod/assets/Presentation/mark.png"));

			string staged = Path.Combine(
				stagingRoot,
				"_ludots_vfs",
				"WebUiVfsShowcaseMod",
				"assets",
				"Presentation",
				"mark.png");
			Assert.That(await File.ReadAllBytesAsync(staged), Is.EqualTo(png));
			Assert.That(html, Does.Contain("_ludots_vfs/WebUiVfsShowcaseMod/assets/Presentation/mark.png"));
		}
		finally
		{
			if (Directory.Exists(stagingRoot))
			{
				Directory.Delete(stagingRoot, recursive: true);
			}
		}
	}

	private sealed class RecordingResolver : IBrowserResourceResolver
	{
		private readonly byte[] _png;

		public RecordingResolver(byte[] png)
		{
			_png = png;
		}

		public List<string> VfsOriginals { get; } = [];

		public ValueTask<BrowserResource?> ResolveAsync(Uri uri, CancellationToken cancellationToken = default)
		{
			if (uri.OriginalString.StartsWith("ludots-vfs://", StringComparison.OrdinalIgnoreCase))
			{
				VfsOriginals.Add(uri.OriginalString);
				if (uri.OriginalString.Contains("MissingMod", StringComparison.Ordinal))
				{
					return ValueTask.FromResult<BrowserResource?>(null);
				}

				return ValueTask.FromResult<BrowserResource?>(new BrowserResource("image/png", _png));
			}

			if (!string.Equals(uri.Scheme, BrowserLocalAppUri.Scheme, StringComparison.OrdinalIgnoreCase))
			{
				return ValueTask.FromResult<BrowserResource?>(null);
			}

			string body = uri.AbsolutePath.EndsWith("styles.css", StringComparison.Ordinal)
				? "body{background:#121820}"
				: """
				<!doctype html>
				<html><head><link rel="stylesheet" href="./styles.css"></head>
				<body>
				<img src="ludots-vfs://WebUiVfsShowcaseMod/assets/Presentation/mark.png">
				<img src="ludots-vfs://MissingMod/assets/Presentation/missing.png">
				</body></html>
				""";
			string contentType = uri.AbsolutePath.EndsWith("styles.css", StringComparison.Ordinal)
				? "text/css; charset=utf-8"
				: "text/html; charset=utf-8";
			return ValueTask.FromResult<BrowserResource?>(new BrowserResource(contentType, Encoding.UTF8.GetBytes(body)));
		}
	}
}
