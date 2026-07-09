using System.Reflection;
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
                RuntimeRootPath = sourceRoot,
                ShadowCopyRootPath = shadowRoot,
                Log = logs.Add
            });

        try
        {
            Assert.That(handle.SourceAssemblyPath, Is.EqualTo(Path.GetFullPath(sourceAssemblyPath)));
            Assert.That(handle.ShadowAssemblyPath, Is.Not.EqualTo(handle.SourceAssemblyPath));
            Assert.That(handle.ShadowAssemblyPath, Does.StartWith(Path.GetFullPath(shadowRoot)));
            Assert.That(handle.UsesCollectibleLoadContext, Is.True);
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
    public void Install_WithExplicitRuntimeRootMapsProviderPackagePathsIntoShadowCopy()
    {
        string sourceRoot = CopyProviderFixtureDirectory();
        string sourceAssemblyPath = Path.Combine(
            sourceRoot,
            Path.GetFileName(typeof(BrowserRuntimeProviderLoaderTests).Assembly.Location));
        string sourceRuntimeRoot = Path.Combine(sourceRoot, "cef-runtime-package");
        string shadowRoot = CreateTempDirectory();
        string cacheRoot = CreateTempDirectory();
        var services = new Dictionary<string, object>(StringComparer.Ordinal);

        Directory.CreateDirectory(sourceRuntimeRoot);
        File.WriteAllText(Path.Combine(sourceRuntimeRoot, "runtime.marker"), "provider-owned runtime root");

        BrowserRuntimeProviderLoadHandle handle = BrowserRuntimeProviderLoader.Install(
            new BrowserRuntimeProviderLoadOptions(
                services,
                sourceAssemblyPath,
                typeof(FakeProviderHost).FullName!)
            {
                ProviderId = "fixture",
                RuntimeRootPath = sourceRuntimeRoot,
                BrowserCacheRootPath = cacheRoot,
                ShadowCopyRootPath = shadowRoot
            });

        try
        {
            string expectedRuntimeRoot = Path.GetFullPath(Path.Combine(handle.ShadowCopyDirectory, "cef-runtime-package"));
            Assert.That(services["FakeProviderRuntimeRootPath"], Is.EqualTo(expectedRuntimeRoot));
            Assert.That(File.Exists(Path.Combine(expectedRuntimeRoot, "runtime.marker")), Is.True);
            Assert.That(services["FakeProviderCacheRootPath"], Is.EqualTo(cacheRoot));
            Assert.That(services[BrowserRuntimeServiceNames.BrowserRuntime], Is.SameAs(handle.Runtime));
        }
        finally
        {
            handle.ShutdownProcessForHostExit();
            DeleteDirectoryIfExists(sourceRoot);
            DeleteDirectoryIfExists(shadowRoot);
            DeleteDirectoryIfExists(cacheRoot);
        }
    }

    [Test]
    public void Install_WithRuntimeRootMappingDisabledPassesOriginalRuntimeRoot()
    {
        string sourceRoot = CopyProviderFixtureDirectory();
        string sourceAssemblyPath = Path.Combine(
            sourceRoot,
            Path.GetFileName(typeof(BrowserRuntimeProviderLoaderTests).Assembly.Location));
        string externalRuntimeRoot = CreateTempDirectory();
        string shadowRoot = CreateTempDirectory();
        var services = new Dictionary<string, object>(StringComparer.Ordinal);

        BrowserRuntimeProviderLoadHandle handle = BrowserRuntimeProviderLoader.Install(
            new BrowserRuntimeProviderLoadOptions(
                services,
                sourceAssemblyPath,
                typeof(FakeProviderHost).FullName!)
            {
                ProviderId = "fixture",
                RuntimeRootPath = externalRuntimeRoot,
                ShadowCopyRootPath = shadowRoot,
                MapRuntimeRootToShadowCopy = false
            });

        try
        {
            Assert.That(services["FakeProviderRuntimeRootPath"], Is.EqualTo(Path.GetFullPath(externalRuntimeRoot)));
            Assert.That((string)services["FakeProviderRuntimeRootPath"], Is.Not.EqualTo(handle.ShadowCopyDirectory));
            Assert.That(services[BrowserRuntimeServiceNames.BrowserRuntime], Is.SameAs(handle.Runtime));
        }
        finally
        {
            handle.ShutdownProcessForHostExit();
            DeleteDirectoryIfExists(sourceRoot);
            DeleteDirectoryIfExists(externalRuntimeRoot);
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
                RuntimeRootPath = sourceRoot,
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
    public void Install_CanUseNonCollectibleProviderAlcForMixedNativeProviders()
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
                ProviderId = "cef",
                RuntimeRootPath = sourceRoot,
                ShadowCopyRootPath = shadowRoot,
                UseCollectibleLoadContext = false,
                Log = logs.Add,
            });

        try
        {
            Assert.That(handle.UsesCollectibleLoadContext, Is.False);
            Assert.That(handle.ShadowAssemblyPath, Is.Not.EqualTo(handle.SourceAssemblyPath));
            Assert.That(services[BrowserRuntimeServiceNames.BrowserRuntime], Is.InstanceOf<IBrowserRuntime>());
        }
        finally
        {
            handle.ShutdownProcessForHostExit();
            DeleteDirectoryIfExists(sourceRoot);
            DeleteDirectoryIfExists(shadowRoot);
        }

        Assert.That(handle.LastUnloadCollected, Is.Null);
        Assert.That(logs.Any(message => message.Contains("non-collectible provider ALC", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void Install_CanShareProcessBoundProviderDependenciesWithDefaultAlc()
    {
        string sourceRoot = CopyProviderFixtureDirectory();
        string sourceAssemblyPath = Path.Combine(
            sourceRoot,
            Path.GetFileName(typeof(BrowserRuntimeProviderLoaderTests).Assembly.Location));
        string shadowRoot = CreateTempDirectory();
        var services = new Dictionary<string, object>(StringComparer.Ordinal);

        BrowserRuntimeProviderLoadHandle handle = BrowserRuntimeProviderLoader.Install(
            new BrowserRuntimeProviderLoadOptions(
                services,
                sourceAssemblyPath,
                typeof(ProcessSharedDependencyProviderHost).FullName!)
            {
                ProviderId = "cef",
                RuntimeRootPath = sourceRoot,
                ShadowCopyRootPath = shadowRoot,
                UseCollectibleLoadContext = false,
                ProcessSharedAssemblyNamePrefixes = new[] { "Ludots.UI" },
            });

        try
        {
            Assert.That(
                services["ProcessSharedDependencyLoadContext"],
                Is.SameAs(AssemblyLoadContext.Default));
            Assert.That(services["ProcessSharedDependencyLocation"], Is.Not.EqualTo(string.Empty));
        }
        finally
        {
            handle.ShutdownProcessForHostExit();
            DeleteDirectoryIfExists(sourceRoot);
            DeleteDirectoryIfExists(shadowRoot);
        }
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
    public void Install_MissingRuntimeRootPathFailsFast()
    {
        string sourceRoot = CopyProviderFixtureDirectory();
        string sourceAssemblyPath = Path.Combine(
            sourceRoot,
            Path.GetFileName(typeof(BrowserRuntimeProviderLoaderTests).Assembly.Location));
        string shadowRoot = CreateTempDirectory();
        var services = new Dictionary<string, object>(StringComparer.Ordinal);

        try
        {
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                BrowserRuntimeProviderLoader.Install(
                    new BrowserRuntimeProviderLoadOptions(
                        services,
                        sourceAssemblyPath,
                        typeof(FakeProviderHost).FullName!)
                    {
                        ProviderId = "fixture",
                        ShadowCopyRootPath = shadowRoot
                    }))!;

            Assert.That(ex.Message, Does.Contain("browserRuntime.runtimeRootPath is required"));
            Assert.That(services, Is.Empty);
        }
        finally
        {
            DeleteDirectoryIfExists(sourceRoot);
            DeleteDirectoryIfExists(shadowRoot);
        }
    }

    [Test]
    public void Install_RuntimeRootOutsideProviderPackageFailsFast()
    {
        string sourceRoot = CopyProviderFixtureDirectory();
        string sourceAssemblyPath = Path.Combine(
            sourceRoot,
            Path.GetFileName(typeof(BrowserRuntimeProviderLoaderTests).Assembly.Location));
        string externalRuntimeRoot = CreateTempDirectory();
        string shadowRoot = CreateTempDirectory();
        var services = new Dictionary<string, object>(StringComparer.Ordinal);

        try
        {
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                BrowserRuntimeProviderLoader.Install(
                    new BrowserRuntimeProviderLoadOptions(
                        services,
                        sourceAssemblyPath,
                        typeof(FakeProviderHost).FullName!)
                    {
                        ProviderId = "fixture",
                        RuntimeRootPath = externalRuntimeRoot,
                        ShadowCopyRootPath = shadowRoot
                    }))!;

            Assert.That(ex.Message, Does.Contain("must be inside the browser runtime provider package"));
            Assert.That(ex.Message, Does.Contain(Path.GetFullPath(externalRuntimeRoot)));
            Assert.That(services, Is.Empty);
        }
        finally
        {
            DeleteDirectoryIfExists(sourceRoot);
            DeleteDirectoryIfExists(externalRuntimeRoot);
            DeleteDirectoryIfExists(shadowRoot);
        }
    }

    [Test]
    public void Install_RefreshesShadowCopyWhenProviderPrivateDependencyChanges()
    {
        string sourceRoot = CopyProviderFixtureDirectory();
        string sourceAssemblyPath = Path.Combine(
            sourceRoot,
            Path.GetFileName(typeof(BrowserRuntimeProviderLoaderTests).Assembly.Location));
        string shadowRoot = CreateTempDirectory();
        string privateDependencyPath = Path.Combine(sourceRoot, "FakeProvider.PrivateDependency.dll");
        BrowserRuntimeProviderLoadHandle? firstHandle = null;
        BrowserRuntimeProviderLoadHandle? secondHandle = null;

        try
        {
            DateTime fixedTimestamp = new DateTime(2026, 7, 7, 0, 0, 0, DateTimeKind.Utc);
            File.WriteAllText(privateDependencyPath, "version-one");
            File.SetLastWriteTimeUtc(privateDependencyPath, fixedTimestamp);
            firstHandle = BrowserRuntimeProviderLoader.Install(
                new BrowserRuntimeProviderLoadOptions(
                    new Dictionary<string, object>(StringComparer.Ordinal),
                    sourceAssemblyPath,
                    typeof(FakeProviderHost).FullName!)
                {
                    ProviderId = "fixture",
                    RuntimeRootPath = sourceRoot,
                    ShadowCopyRootPath = shadowRoot
                });
            string firstShadowDependencyPath = Path.Combine(
                firstHandle.ShadowCopyDirectory,
                Path.GetFileName(privateDependencyPath));
            Assert.That(File.ReadAllText(firstShadowDependencyPath), Is.EqualTo("version-one"));
            firstHandle.ShutdownProcessForHostExit();

            File.WriteAllText(privateDependencyPath, "version-two");
            File.SetLastWriteTimeUtc(privateDependencyPath, fixedTimestamp);
            secondHandle = BrowserRuntimeProviderLoader.Install(
                new BrowserRuntimeProviderLoadOptions(
                    new Dictionary<string, object>(StringComparer.Ordinal),
                    sourceAssemblyPath,
                    typeof(FakeProviderHost).FullName!)
                {
                    ProviderId = "fixture",
                    RuntimeRootPath = sourceRoot,
                    ShadowCopyRootPath = shadowRoot
                });

            string secondShadowDependencyPath = Path.Combine(
                secondHandle.ShadowCopyDirectory,
                Path.GetFileName(privateDependencyPath));
            Assert.That(secondHandle.ShadowCopyDirectory, Is.Not.EqualTo(firstHandle.ShadowCopyDirectory));
            Assert.That(File.ReadAllText(secondShadowDependencyPath), Is.EqualTo("version-two"));
        }
        finally
        {
            secondHandle?.ShutdownProcessForHostExit();
            firstHandle?.ShutdownProcessForHostExit();
            DeleteDirectoryIfExists(sourceRoot);
            DeleteDirectoryIfExists(shadowRoot);
        }
    }

    [Test]
    public void Install_RejectsProviderIdPathTraversalSegments()
    {
        string sourceRoot = CopyProviderFixtureDirectory();
        string sourceAssemblyPath = Path.Combine(
            sourceRoot,
            Path.GetFileName(typeof(BrowserRuntimeProviderLoaderTests).Assembly.Location));
        string shadowRoot = CreateTempDirectory();

        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                BrowserRuntimeProviderLoader.Install(
                    new BrowserRuntimeProviderLoadOptions(
                        new Dictionary<string, object>(StringComparer.Ordinal),
                        sourceAssemblyPath,
                        typeof(FakeProviderHost).FullName!)
                    {
                        ProviderId = "..",
                        ShadowCopyRootPath = shadowRoot
                    }));
        }
        finally
        {
            DeleteDirectoryIfExists(sourceRoot);
            DeleteDirectoryIfExists(shadowRoot);
        }
    }

    [Test]
    public void Install_FailedProviderInstallRestoresHostServices()
    {
        string sourceRoot = CopyProviderFixtureDirectory();
        string sourceAssemblyPath = Path.Combine(
            sourceRoot,
            Path.GetFileName(typeof(BrowserRuntimeProviderLoaderTests).Assembly.Location));
        string shadowRoot = CreateTempDirectory();
        var existingService = new object();
        var services = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["ExistingService"] = existingService
        };

        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                BrowserRuntimeProviderLoader.Install(
                    new BrowserRuntimeProviderLoadOptions(
                        services,
                        sourceAssemblyPath,
                        typeof(MismatchedRuntimeProviderHost).FullName!)
                    {
                        ProviderId = "fixture",
                        RuntimeRootPath = sourceRoot,
                        ShadowCopyRootPath = shadowRoot
                    }));

            Assert.That(ex!.Message, Does.Contain("does not match the provider return value"));
            Assert.That(services.Keys, Is.EquivalentTo(new[] { "ExistingService" }));
            Assert.That(services["ExistingService"], Is.SameAs(existingService));
        }
        finally
        {
            DeleteDirectoryIfExists(sourceRoot);
            DeleteDirectoryIfExists(shadowRoot);
        }
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
        Assert.That(loaderSource, Does.Contain("MapRuntimeRootToShadowCopy"));
        Assert.That(loaderSource, Does.Contain("Unload()"));
        Assert.That(loaderSource, Does.Not.Contain("InstallFromAssemblyLocation"));
        Assert.That(loadContextSource, Does.Contain("AssemblyDependencyResolver"));
        Assert.That(loadContextSource, Does.Contain("defaultLoadContextAssemblyNamePrefixes"));
        Assert.That(loadContextSource, Does.Contain("ResolveDefaultLoadContextAssembly"));
        Assert.That(loadContextSource, Does.Contain("isCollectible"));
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

    public static class MismatchedRuntimeProviderHost
    {
        public static IBrowserRuntime InstallFromAssemblyLocation(
            IDictionary<string, object> services,
            string? cacheRootPath = null)
        {
            string assemblyLocation = typeof(MismatchedRuntimeProviderHost).Assembly.Location;
            string runtimeRootPath = Path.GetDirectoryName(assemblyLocation)
                ?? throw new DirectoryNotFoundException("Could not resolve fake provider runtime root.");
            return Install(services, runtimeRootPath, cacheRootPath);
        }

        public static IBrowserRuntime Install(
            IDictionary<string, object> services,
            string runtimeRootPath,
            string? cacheRootPath = null)
        {
            var registeredRuntime = new FakeBrowserRuntime(services);
            var returnedRuntime = new FakeBrowserRuntime(services);
            services[BrowserRuntimeServiceNames.BrowserRuntime] = registeredRuntime;
            services[BrowserRuntimeServiceNames.HostLifecycle] = new FakeHostLifecycle(services);
            services["MismatchedProviderTouchedServices"] = true;
            return returnedRuntime;
        }
    }

    public static class ProcessSharedDependencyProviderHost
    {
        public static IBrowserRuntime InstallFromAssemblyLocation(
            IDictionary<string, object> services,
            string? cacheRootPath = null)
        {
            string assemblyLocation = typeof(ProcessSharedDependencyProviderHost).Assembly.Location;
            string runtimeRootPath = Path.GetDirectoryName(assemblyLocation)
                ?? throw new DirectoryNotFoundException("Could not resolve fake provider runtime root.");
            return Install(services, runtimeRootPath, cacheRootPath);
        }

        public static IBrowserRuntime Install(
            IDictionary<string, object> services,
            string runtimeRootPath,
            string? cacheRootPath = null)
        {
            Assembly assembly = Assembly.Load("Ludots.UI");
            services["ProcessSharedDependencyLoadContext"] = AssemblyLoadContext.GetLoadContext(assembly)!;
            services["ProcessSharedDependencyLocation"] = assembly.Location;
            return FakeProviderHost.Install(services, runtimeRootPath, cacheRootPath);
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
