using System.Reflection;
using System.Runtime.Loader;
using Ludots.UI.Browser.Cef;
using NUnit.Framework;

namespace Ludots.Tests.BrowserCef;

[TestFixture]
public sealed class CefBrowserRuntimeAssemblyResolutionTests
{
	[Test]
	public void PrepareAssemblyResolution_LoadsCefSharpProcessAssembliesFromRuntimeRoot()
	{
		string runtimeRootPath = AppContext.BaseDirectory;

		CefBrowserRuntime.PrepareAssemblyResolution(runtimeRootPath);

		AssertProcessAssemblyLoaded("CefSharp.Core.Runtime");
		AssertProcessAssemblyLoaded("CefSharp.Core");
		AssertProcessAssemblyLoaded("CefSharp");
		AssertProcessAssemblyLoaded("CefSharp.OffScreen");
	}

	private static void AssertProcessAssemblyLoaded(string assemblyName)
	{
		string expectedPath = Path.Combine(AppContext.BaseDirectory, $"{assemblyName}.dll");
		Assert.That(File.Exists(expectedPath), Is.True, $"Missing test runtime assembly: {expectedPath}");

		AssemblyName expectedIdentity = AssemblyName.GetAssemblyName(expectedPath);
		bool loaded = AssemblyLoadContext.Default.Assemblies.Any(candidate =>
			AssemblyName.ReferenceMatchesDefinition(expectedIdentity, candidate.GetName()));
		Assert.That(loaded, Is.True, $"{assemblyName} should be loaded into the default AssemblyLoadContext.");
	}
}
