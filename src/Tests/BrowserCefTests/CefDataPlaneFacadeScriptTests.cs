using Ludots.UI.Browser.Cef;
using NUnit.Framework;

namespace Ludots.Tests.BrowserCef;

[TestFixture]
public sealed class CefDataPlaneFacadeScriptTests
{
	[Test]
	public void InjectionScript_InstallsStandardLudotsDataplaneFacadeOverProviderBridge()
	{
		string script = CefDataPlaneFacadeScript.Create();

		Assert.That(script, Does.Contain("window.ludotsDataplane"));
		Assert.That(script, Does.Contain("name: 'cef.ludots-dataplane'"));
		Assert.That(script, Does.Contain("mode: 'browser-native-bridge'"));
		Assert.That(script, Does.Contain("postMessage(message)"));
		Assert.That(script, Does.Contain("readSharedBuffer(descriptor)"));
		Assert.That(script, Does.Contain("addEventListener(type, listener, options)"));
		Assert.That(script, Does.Contain("removeEventListener(type, listener, options)"));
		Assert.That(script, Does.Contain("window.CefSharp.PostMessage(message)"));
	}

	[Test]
	public void InjectionScript_BindsNativeSharedBufferReaderBehindStandardFacade()
	{
		string script = CefDataPlaneFacadeScript.Create();

		Assert.That(script, Does.Contain("ludotsDataplaneNative"));
		Assert.That(script, Does.Contain("window.CefSharp.BindObjectAsync"));
		Assert.That(script, Does.Contain("window.cefSharp.bindObjectAsync"));
		Assert.That(script, Does.Contain("nativeBridge.readSharedBuffer"));
		Assert.That(script, Does.Contain("Uint8Array.from"));
	}

	[Test]
	public void InjectionScript_IsIdempotentAndDoesNotOverwriteExternalHostFacade()
	{
		string script = CefDataPlaneFacadeScript.Create();

		Assert.That(script, Does.Contain("if (window.ludotsDataplane)"));
		Assert.That(script, Does.Contain("return;"));
	}
}
