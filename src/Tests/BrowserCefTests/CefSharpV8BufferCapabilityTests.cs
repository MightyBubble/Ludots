using System.Reflection;
using CefSharp.RenderProcess;
using NUnit.Framework;

namespace Ludots.Tests.BrowserCef;

[TestFixture]
public sealed class CefSharpV8BufferCapabilityTests
{
	[Test]
	public void ManagedRenderProcessApi_DoesNotExposeArrayBufferBackingStoreCreation()
	{
		Type contextType = typeof(IV8Context);
		string[] publicMethodNames = contextType
			.GetMethods(BindingFlags.Instance | BindingFlags.Public)
			.Select(static method => method.Name)
			.ToArray();

		Assert.That(publicMethodNames, Does.Contain("Execute"));
		Assert.That(publicMethodNames, Does.Not.Contain("CreateArrayBuffer"));
		Assert.That(publicMethodNames, Does.Not.Contain("CreateObject"));
		Assert.That(publicMethodNames, Does.Not.Contain("CreateFunction"));
	}

	[Test]
	public void ManagedCefSharpAssemblies_DoNotExposePublicV8ValueOrBackingStoreTypes()
	{
		Assembly[] assemblies =
		{
			typeof(IV8Context).Assembly,
			typeof(global::CefSharp.Cef).Assembly
		};

		string[] publicTypeNames = assemblies
			.SelectMany(static assembly => assembly.GetExportedTypes())
			.Select(static type => type.FullName ?? type.Name)
			.ToArray();

		Assert.That(publicTypeNames.Any(static name => name.EndsWith(".IV8Context", StringComparison.Ordinal)), Is.True);
		Assert.That(publicTypeNames.Any(static name => name.Contains("V8Value", StringComparison.Ordinal)), Is.False);
		Assert.That(publicTypeNames.Any(static name => name.Contains("V8BackingStore", StringComparison.Ordinal)), Is.False);
	}

	[Test]
	public void NativeProvider_IsBuiltAsRendererSubprocessExtensionInsteadOfManagedCopyFacade()
	{
		string repoRoot = FindRepoRoot();
		string nativeSource = File.ReadAllText(Path.Combine(
			repoRoot,
			"src",
			"Libraries",
			"Ludots.UI.Browser.Cef.Native",
			"ludots_cef_v8_buffer_bridge.cpp"));
		string subprocessSource = File.ReadAllText(Path.Combine(
			repoRoot,
			"src",
			"Libraries",
			"Ludots.UI.Browser.Cef.Subprocess",
			"Program.cs"));
		string cefProject = File.ReadAllText(Path.Combine(
			repoRoot,
			"src",
			"Libraries",
			"Ludots.UI.Browser.Cef",
			"Ludots.UI.Browser.Cef.csproj"));

		Assert.That(nativeSource, Does.Contain("CefRegisterExtension"));
		Assert.That(nativeSource, Does.Contain("CefV8BackingStore::Create"));
		Assert.That(nativeSource, Does.Contain("CefV8Value::CreateArrayBufferFromBackingStore"));
		Assert.That(nativeSource, Does.Contain("std::memcpy"));
		Assert.That(nativeSource, Does.Contain("OpenFileMappingW"));
		Assert.That(nativeSource, Does.Contain("MapViewOfFile"));
		Assert.That(nativeSource, Does.Contain("__ludotsCefV8.acquireV8Buffer"));
		Assert.That(nativeSource, Does.Not.Contain("CefV8Value::CreateArrayBuffer("));
		Assert.That(nativeSource, Does.Not.Contain("CefV8ArrayBufferReleaseCallback"));
		Assert.That(nativeSource, Does.Not.Contain("CreateArrayBufferWithCopy"));
		Assert.That(subprocessSource, Does.Contain("BrowserSubprocessExecutable().Main(args, new LudotsRenderProcessHandler())"));
		Assert.That(subprocessSource, Does.Contain("OnWebKitInitialized"));
		Assert.That(subprocessSource, Does.Contain("LudotsCefV8Install"));
		Assert.That(cefProject, Does.Contain("Ludots.UI.Browser.Cef.Subprocess.csproj"));
		Assert.That(cefProject, Does.Contain("Ludots.UI.Browser.Cef.Native.vcxproj"));
	}

	private static string FindRepoRoot()
	{
		string? current = TestContext.CurrentContext.TestDirectory;
		while (!string.IsNullOrWhiteSpace(current))
		{
			if (File.Exists(Path.Combine(current, "launcher.config.json")) &&
				Directory.Exists(Path.Combine(current, "src")))
			{
				return current;
			}

			current = Directory.GetParent(current)?.FullName;
		}

		throw new DirectoryNotFoundException("Could not find repository root.");
	}
}
