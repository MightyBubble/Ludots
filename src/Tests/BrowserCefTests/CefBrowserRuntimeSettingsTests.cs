using System.Reflection;
using Ludots.UI.Browser.Cef;
using NUnit.Framework;

namespace Ludots.Tests.BrowserCef;

[TestFixture]
public sealed class CefBrowserRuntimeSettingsTests
{
	[Test]
	public void BuildSettings_ForV8BackingStorePath_DoesNotInjectSandboxDisableFlags()
	{
		var options = new CefBrowserRuntimeOptions(
			AppContext.BaseDirectory,
			Path.Combine(Path.GetTempPath(), "Ludots", "CefSettingsTests", Guid.NewGuid().ToString("N")));
		MethodInfo buildSettings = typeof(CefBrowserRuntime).GetMethod(
			"BuildSettings",
			BindingFlags.Static | BindingFlags.NonPublic)!;

		using var settings = (global::CefSharp.OffScreen.CefSettings)buildSettings.Invoke(null, new object[] { options })!;

		Assert.That(settings.CefCommandLineArgs, Does.Not.ContainKey("disable-features"));
		Assert.That(settings.CefCommandLineArgs, Does.Not.ContainKey("no-sandbox"));
	}
}
