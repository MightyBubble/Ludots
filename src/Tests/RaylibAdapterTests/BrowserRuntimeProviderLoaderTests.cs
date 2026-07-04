using System.Runtime.Loader;
using Ludots.UI.Browser;
using NUnit.Framework;

namespace Ludots.Tests.RaylibAdapter;

[TestFixture]
public sealed class BrowserRuntimeProviderLoaderTests
{
    [Test]
    public void Install_LoadsProviderFromShadowCopyAndKeepsHostContractIdentity()
    {
        string sourceRoot = CopyProviderFixtureDirectory();
        string sourceAssemblyPath = Path.Combine(
            sourceRoot,
            Path.GetFileName(typeof(BrowserRuntimeProviderLoaderTests).Assembly.Location));
        string shadowRoot = CreateTempDirectory();
        var services = new Dictionary<string, object>(StringComparer.Ordinal);
        var logs = new List<string>();

        BrowserRuntimeProviderLoadHandle handle = BrowserRuntimeProviderLoader.Install(
            new BrowserRuntimeProviderLoadOptions(
                services,
                sourceAssemblyPath,
                typeof(FakeProviderHost).FullName!)
            {
                ProviderId = "fixture",
                ShadowCopyRootPath = shadowRoot,
                Log = logs.Add
            });

        try
        {
            Assert.That(handle.SourceAssemblyPath, Is.EqualTo(Path.GetFullPath(sourceAssemblyPath)));
            Assert.That(handle.ShadowAssemblyPath, Is.Not.EqualTo(handle.SourceAssemblyPath));
            Assert.That(handle.ShadowAssemblyPath, Does.StartWith(Path.GetFullPath(shadowRoot)));
            Assert.That(File.Exists(handle.ShadowAssemblyPath), Is.True);
            Assert.That(services[BrowserRuntimeServiceNames.BrowserRuntime], Is.InstanceOf<IBrowserRuntime>());
            Assert.That(services["FakeProviderAssemblyLocation"], Is.EqualTo(handle.ShadowAssemblyPath));
            Assert.That(
                services["FakeProviderContractLoadContext"],
                Is.SameAs(AssemblyLoadContext.GetLoadContext(typeof(IBrowserRuntime).Assembly)));

            File.Delete(sourceAssemblyPath);
            Assert.That(File.Exists(sourceAssemblyPath), Is.False, "Loading the provider must not lock the source output DLL.");
        }
        finally
        {
            handle.ShutdownProcessForHostExit();
            DeleteDirectoryIfExists(sourceRoot);
            DeleteDirectoryIfExists(shadowRoot);
        }
    }

    [Test]
    public void ShutdownProcessForHostExit_DisposesRuntimeRemovesServicesAndLogsAlcCollection()
    {
        string sourceRoot = CopyProviderFixtureDirectory();
        string sourceAssemblyPath = Path.Combine(
            sourceRoot,
            Path.GetFileName(typeof(BrowserRuntimeProviderLoaderTests).Assembly.Location));
        string shadowRoot = CreateTempDirectory();
        var services = new Dictionary<string, object>(StringComparer.Ordinal);
        var logs = new List<string>();

        BrowserRuntimeProviderLoadHandle handle = BrowserRuntimeProviderLoader.Install(
            new BrowserRuntimeProviderLoadOptions(
                services,
                sourceAssemblyPath,
                typeof(FakeProviderHost).FullName!)
            {
                ProviderId = "fixture",
                ShadowCopyRootPath = shadowRoot,
                Log = logs.Add
            });

        handle.ShutdownProcessForHostExit();

        Assert.That(services.ContainsKey(BrowserRuntimeServiceNames.BrowserRuntime), Is.False);
        Assert.That(services.ContainsKey(BrowserRuntimeServiceNames.HostLifecycle), Is.False);
        Assert.That(services["FakeProviderRuntimeDisposed"], Is.True);
        Assert.That(services["FakeProviderLifecycleShutdown"], Is.True);
        Assert.That(handle.LastUnloadCollected, Is.True);
        Assert.That(logs.Any(message => message.Contains("collectible ALC collected=True", StringComparison.Ordinal)), Is.True);

        DeleteDirectoryIfExists(sourceRoot);
        DeleteDirectoryIfExists(shadowRoot);
    }

    [Test]
    public void Install_MissingProviderAssemblyFailsFast()
    {
        string missingAssemblyPath = Path.Combine(CreateTempDirectory(), "Missing.Provider.dll");
        var services = new Dictionary<string, object>(StringComparer.Ordinal);

        Assert.Throws<FileNotFoundException>(() =>
            BrowserRuntimeProviderLoader.Install(
                new BrowserRuntimeProviderLoadOptions(
                    services,
                    missingAssemblyPath,
                    typeof(FakeProviderHost).FullName!)));
    }

    [Test]
    public void Loader_UsesCollectibleAlcShadowCopyAndDependencyResolver()
    {
        string repoRoot = FindRepoRoot();
        string loaderSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "Libraries",
            "Ludots.UI.Browser",
            "BrowserRuntimeProviderLoader.cs"));
        string loadContextSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "Libraries",
            "Ludots.UI.Browser",
            "BrowserRuntimeProviderAssemblyLoadContext.cs"));

        Assert.That(loaderSource, Does.Contain("SHA256"));
        Assert.That(loaderSource, Does.Contain("ShadowCopy"));
        Assert.That(loaderSource, Does.Contain("Unload()"));
        Assert.That(loadContextSource, Does.Contain("AssemblyDependencyResolver"));
        Assert.That(loadContextSource, Does.Contain("isCollectible: true"));
        Assert.That(loadContextSource, Does.Not.Contain("CefSharp"));
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

    private static string CopyProviderFixtureDirectory()
    {
        string sourceDirectory = AppContext.BaseDirectory;
        string targetDirectory = CreateTempDirectory();
        foreach (string sourceFile in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceDirectory, sourceFile);
            string targetFile = Path.Combine(targetDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.Copy(sourceFile, targetFile, overwrite: true);
        }

        return targetDirectory;
    }

    private static string CreateTempDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Ludots", "ProviderLoaderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteDirectoryIfExists(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public static class FakeProviderHost
    {
        public static IBrowserRuntime InstallFromAssemblyLocation(
            IDictionary<string, object> services,
            string? cacheRootPath = null)
        {
            string assemblyLocation = typeof(FakeProviderHost).Assembly.Location;
            string runtimeRootPath = Path.GetDirectoryName(assemblyLocation)
                ?? throw new DirectoryNotFoundException("Could not resolve fake provider runtime root.");
            return Install(services, runtimeRootPath, cacheRootPath);
        }

        public static IBrowserRuntime Install(
            IDictionary<string, object> services,
            string runtimeRootPath,
            string? cacheRootPath = null)
        {
            var runtime = new FakeBrowserRuntime(services);
            services[BrowserRuntimeServiceNames.BrowserRuntime] = runtime;
            services[BrowserRuntimeServiceNames.HostLifecycle] = new FakeHostLifecycle(services);
            services["FakeProviderAssemblyLocation"] = typeof(FakeProviderHost).Assembly.Location;
            services["FakeProviderRuntimeRootPath"] = runtimeRootPath;
            services["FakeProviderCacheRootPath"] = cacheRootPath ?? string.Empty;
            services["FakeProviderContractLoadContext"] = AssemblyLoadContext.GetLoadContext(typeof(IBrowserRuntime).Assembly)!;
            return runtime;
        }
    }

    private sealed class FakeBrowserRuntime : IBrowserRuntime
    {
        private readonly IDictionary<string, object> _services;

        public FakeBrowserRuntime(IDictionary<string, object> services)
        {
            _services = services;
            Info = new BrowserRuntimeInfo(
                BrowserEngineKind.Cef,
                "Fake Provider",
                "test",
                BrowserEngineCapabilityProfiles.Cef);
        }

        public BrowserRuntimeInfo Info { get; }

        public ValueTask<IBrowserSurface> CreateSurfaceAsync(
            BrowserViewport viewport,
            IBrowserResourceResolver? resourceResolver = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("The fake provider does not create browser surfaces.");
        }

        public ValueTask DisposeAsync()
        {
            _services["FakeProviderRuntimeDisposed"] = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeHostLifecycle : IBrowserRuntimeHostLifecycle
    {
        private readonly IDictionary<string, object> _services;

        public FakeHostLifecycle(IDictionary<string, object> services)
        {
            _services = services;
        }

        public void ShutdownProcessForHostExit()
        {
            _services["FakeProviderLifecycleShutdown"] = true;
        }
    }
}
