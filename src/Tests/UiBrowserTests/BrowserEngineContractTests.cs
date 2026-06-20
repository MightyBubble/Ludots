using Ludots.UI.Browser;
using NUnit.Framework;

namespace Ludots.Tests.UiBrowser;

[TestFixture]
public sealed class BrowserEngineContractTests
{
	[Test]
	public void BrowserEngineKind_OnlyContainsCefAndUltralight()
	{
		Assert.That(Enum.GetNames<BrowserEngineKind>(), Is.EqualTo(new[] { "Cef", "Ultralight" }));
	}

	[Test]
	public void CefProfile_IsFullChromiumOffscreenRuntime()
	{
		BrowserEngineCapabilities capabilities = BrowserEngineCapabilityProfiles.Cef;

		Assert.That(capabilities.HasFlag(BrowserEngineCapabilities.FullChromiumCompatibility), Is.True);
		Assert.That(capabilities.HasFlag(BrowserEngineCapabilities.OffscreenRendering), Is.True);
		Assert.That(capabilities.HasFlag(BrowserEngineCapabilities.WebGL), Is.True);
		Assert.That(capabilities.HasFlag(BrowserEngineCapabilities.LightweightGameUi), Is.False);
	}

	[Test]
	public void UltralightProfile_IsLightweightGameUiRuntime()
	{
		BrowserEngineCapabilities capabilities = BrowserEngineCapabilityProfiles.Ultralight;

		Assert.That(capabilities.HasFlag(BrowserEngineCapabilities.LightweightGameUi), Is.True);
		Assert.That(capabilities.HasFlag(BrowserEngineCapabilities.OffscreenRendering), Is.True);
		Assert.That(capabilities.HasFlag(BrowserEngineCapabilities.FullChromiumCompatibility), Is.False);
	}
}
