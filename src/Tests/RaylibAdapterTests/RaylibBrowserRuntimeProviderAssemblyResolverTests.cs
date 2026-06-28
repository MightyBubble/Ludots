using System.Reflection;
using Ludots.Adapter.Raylib;
using NUnit.Framework;

namespace Ludots.Tests.RaylibAdapter;

[TestFixture]
public sealed class RaylibBrowserRuntimeProviderAssemblyResolverTests
{
    [Test]
    public void ResolveManagedAssemblyPath_UsesProviderDepsManifest()
    {
        string providerAssemblyPath = typeof(RaylibBrowserRuntimeInstaller).Assembly.Location;
        var resolver = new RaylibBrowserRuntimeProviderAssemblyResolver(providerAssemblyPath);

        string? resolvedPath = resolver.ResolveManagedAssemblyPath(new AssemblyName("Ludots.UI.Browser"));

        Assert.That(resolvedPath, Is.Not.Null);
        Assert.That(Path.GetFileName(resolvedPath!), Is.EqualTo("Ludots.UI.Browser.dll"));
        Assert.That(
            Path.GetDirectoryName(Path.GetFullPath(resolvedPath!)),
            Is.EqualTo(Path.GetDirectoryName(Path.GetFullPath(providerAssemblyPath))));
    }

    [Test]
    public void Resolver_DoesNotHardcodeCefSharpDependencyNames()
    {
        string repoRoot = FindRepoRoot();
        string source = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "Adapters",
            "Raylib",
            "Ludots.Adapter.Raylib",
            "RaylibBrowserRuntimeProviderAssemblyResolver.cs"));

        Assert.That(source, Does.Contain("AssemblyDependencyResolver"));
        Assert.That(source, Does.Not.Contain("CefSharp"));
        Assert.That(source, Does.Not.Contain("Ludots.UI.Browser.Cef"));
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")) &&
                Directory.Exists(Path.Combine(current.FullName, "src")) &&
                Directory.Exists(Path.Combine(current.FullName, "mods")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
