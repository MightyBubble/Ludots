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
		Assert.That(script, Does.Contain("acquireV8Buffer(descriptor)"));
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
	public void InjectionScript_ForTrueV8BufferOnlyForwardsNativeArrayBufferProvider()
	{
		string script = CefDataPlaneFacadeScript.Create();

		Assert.That(script, Does.Contain("__ludotsCefV8"));
		Assert.That(script, Does.Contain("v8Bridge.acquireV8Buffer(normalized)"));
		Assert.That(script, Does.Contain("missing acquireV8Buffer"));
		Assert.That(script, Does.Contain("value instanceof ArrayBuffer"));
		Assert.That(script, Does.Contain("Native V8 buffer bridge did not return ArrayBuffer"));

		string v8Method = ExtractMethod(script, "acquireV8Buffer(descriptor)", "addEventListener(type, listener, options)");
		Assert.That(v8Method, Does.Not.Contain("bindNativeBridge"));
		Assert.That(v8Method, Does.Not.Contain("nativeBridge.acquireV8Buffer"));
		Assert.That(v8Method, Does.Not.Contain("readSharedBuffer"));
		Assert.That(v8Method, Does.Not.Contain("Uint8Array.from"));
		Assert.That(v8Method, Does.Not.Contain("new ArrayBuffer"));
	}

	[Test]
	public void InjectionScript_IsIdempotentAndDoesNotOverwriteExternalHostFacade()
	{
		string script = CefDataPlaneFacadeScript.Create();

		Assert.That(script, Does.Contain("if (window.ludotsDataplane)"));
		Assert.That(script, Does.Contain("return;"));
	}

	private static string ExtractMethod(string script, string startMarker, string endMarker)
	{
		int start = script.IndexOf(startMarker, StringComparison.Ordinal);
		int end = script.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
		Assert.That(start, Is.GreaterThanOrEqualTo(0), $"Could not find '{startMarker}'.");
		Assert.That(end, Is.GreaterThan(start), $"Could not find '{endMarker}' after '{startMarker}'.");
		return script[start..end];
	}
}
