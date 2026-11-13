using Ludots.UI.Browser;
using NUnit.Framework;

namespace Ludots.Tests.UiBrowser;

[TestFixture]
public sealed class BrowserVfsUriTests
{
	private const string MountedPath = "WebUiVfsShowcaseMod:assets/Presentation/mark.png";
	private const string Address = "ludots-vfs://WebUiVfsShowcaseMod/assets/Presentation/mark.png";

	[Test]
	public void FromVfsPath_KeepsModIdCase()
	{
		Assert.That(BrowserVfsUri.FromVfsPath(MountedPath), Is.EqualTo(Address));
	}

	[Test]
	public void TryParse_ReadsModIdFromTheWrittenAddress_NotTheLowercasedHost()
	{
		var uri = new Uri(Address);

		Assert.That(uri.Host, Is.EqualTo("webuivfsshowcasemod"));
		Assert.That(BrowserVfsUri.TryParse(uri, out string modId, out string relativePath), Is.True);
		Assert.That(modId, Is.EqualTo("WebUiVfsShowcaseMod"));
		Assert.That(relativePath, Is.EqualTo("assets/Presentation/mark.png"));
	}

	[Test]
	public void TryParse_RejectsTraversalAndPathsOutsideAssets()
	{
		Assert.That(BrowserVfsUri.TryParse("ludots-vfs://WebUiVfsShowcaseMod/assets/../secret.png", out _, out _), Is.False);
		Assert.That(BrowserVfsUri.TryParse("ludots-vfs://WebUiVfsShowcaseMod/assets/%2e%2e/secret.png", out _, out _), Is.False);
		Assert.That(BrowserVfsUri.TryParse("ludots-vfs://WebUiVfsShowcaseMod/mod.json", out _, out _), Is.False);
		Assert.That(BrowserVfsUri.TryParse("ludots-vfs://WebUiVfsShowcaseMod/assets/", out _, out _), Is.False);
		Assert.That(BrowserVfsUri.TryParse("ludots-vfs://webuivfsshowcasemod/assets/Presentation/mark.png", out string modId, out _), Is.True);
		Assert.That(modId, Is.EqualTo("webuivfsshowcasemod"));
	}
}
