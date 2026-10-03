using Ludots.UI.Browser.CefLinux.Core;
using NUnit.Framework;

namespace Ludots.Tests.BrowserCefLinux;

[TestFixture]
public sealed class CefLinuxDataPlaneFacadeScriptTests
{
	[Test]
	public void InjectionScript_InstallsStandardLudotsDataplaneFacadeOverProviderBridge()
	{
		string script = CefLinuxFacadeScript.Create();

		Assert.That(script, Does.Contain("window.ludotsBrowser"));
		Assert.That(script, Does.Contain("name: 'ludots.browser'"));
		Assert.That(script, Does.Contain("window.ludotsDataplane"));
		Assert.That(script, Does.Contain("name: 'ludots.dataplane'"));
		Assert.That(script, Does.Contain("mode: 'message-only'"));
		Assert.That(script, Does.Contain("postMessage(message)"));
		Assert.That(script, Does.Contain("addEventListener(type, listener, options)"));
		Assert.That(script, Does.Contain("removeEventListener(type, listener, options)"));
		Assert.That(script, Does.Contain("__ludotsNativeBridge.postHostMessage"));
	}

	[Test]
	public void InjectionScript_DoesNotDependOnProviderPrivateEngineGlobals()
	{
		string script = CefLinuxFacadeScript.Create();

		Assert.That(script, Does.Not.Contain("CefSharp"));
		Assert.That(script, Does.Not.Contain("readSharedBuffer"));
		Assert.That(script, Does.Not.Contain("ludotsDataplaneNative"));
	}

	[Test]
	public void InjectionScript_IsIdempotentAndDoesNotOverwriteExternalHostFacade()
	{
		string script = CefLinuxFacadeScript.Create();

		Assert.That(script, Does.Contain("if (!window.ludotsBrowser)"));
		Assert.That(script, Does.Contain("if (window.ludotsDataplane)"));
		Assert.That(script, Does.Contain("return;"));
	}
}
