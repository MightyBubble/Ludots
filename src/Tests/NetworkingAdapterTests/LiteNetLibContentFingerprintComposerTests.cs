using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using Ludots.Adapter.LiteNetLib;
using Ludots.Core.Hosting;
using Ludots.Core.Modding;
using Ludots.Core.Networking.Session;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.NetworkingAdapter;

[TestFixture]
public sealed class LiteNetLibContentFingerprintComposerTests
{
    private static readonly ProtocolVersion Protocol = new(1, 1);
    private string _testRoot = null!;

    [SetUp]
    public void SetUp()
    {
        _testRoot = Path.Combine(
            Path.GetTempPath(),
            nameof(LiteNetLibContentFingerprintComposerTests),
            Guid.NewGuid().ToString("N"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }

    [Test]
    public void OrderedModPlan_ChangesFingerprintWhenLoadOrderChanges()
    {
        TestHost host = CreateHost(
            "order",
            new ModSpec("Alpha"),
            new ModSpec("Bravo"));

        ContentFingerprint alphaThenBravo = Compose(
            host,
            new[] { "Alpha", "Bravo" },
            ManagedAssembly("assemblies/gameplay/core/Ludots.Core.dll", 1));
        ContentFingerprint bravoThenAlpha = Compose(
            host,
            new[] { "Bravo", "Alpha" },
            ManagedAssembly("assemblies/gameplay/core/Ludots.Core.dll", 1));

        Assert.That(bravoThenAlpha, Is.Not.EqualTo(alphaThenBravo));
    }

    [Test]
    public void PrivateManagedDependencyBytes_ChangeFingerprint()
    {
        TestHost host = CreateHost("dependency", new ModSpec("RulesMod"));
        var plan = new[] { "RulesMod" };

        ContentFingerprint first = Compose(
            host,
            plan,
            ManagedAssembly("assemblies/gameplay/core/Ludots.Core.dll", 1),
            ManagedAssembly("assemblies/gameplay/private/Rules.Dependency.dll", 2, 3, 4));
        ContentFingerprint changed = Compose(
            host,
            plan,
            ManagedAssembly("assemblies/gameplay/core/Ludots.Core.dll", 1),
            ManagedAssembly("assemblies/gameplay/private/Rules.Dependency.dll", 2, 3, 5));

        Assert.That(changed, Is.Not.EqualTo(first));
    }

    [TestCase("bin")]
    [TestCase("obj")]
    public void AssetDirectoryNamedBuildOutput_StillParticipatesInFingerprint(string directoryName)
    {
        TestHost host = CreateHost("asset-tree", new ModSpec("RulesMod"));
        string assetPath = Path.Combine(
            host.ModRoots["RulesMod"],
            "assets",
            directoryName,
            "rules.json");
        Directory.CreateDirectory(Path.GetDirectoryName(assetPath)!);
        File.WriteAllText(assetPath, "{\"damage\":10}");

        ContentFingerprint first = Compose(
            host,
            new[] { "RulesMod" },
            ManagedAssembly("assemblies/gameplay/core/Ludots.Core.dll", 1));
        File.WriteAllText(assetPath, "{\"damage\":11}");
        ContentFingerprint changed = Compose(
            host,
            new[] { "RulesMod" },
            ManagedAssembly("assemblies/gameplay/core/Ludots.Core.dll", 1));

        Assert.That(changed, Is.Not.EqualTo(first));
    }

    [Test]
    public void MissingDeclaredMainAssembly_FailsExplicitly()
    {
        TestHost host = CreateHost(
            "missing-main",
            new ModSpec("RulesMod", Main: "missing.dll"));

        Assert.That(
            () => Compose(
                host,
                new[] { "RulesMod" },
                ManagedAssembly("assemblies/gameplay/core/Ludots.Core.dll", 1)),
            Throws.TypeOf<FileNotFoundException>()
                .With.Message.Contains("RulesMod:missing.dll"));
    }

    [Test]
    public void CoreGameplayClosure_IncludesGameplayDependenciesAndExcludesPlatformAdapters()
    {
        TestHost host = CreateHost("core-closure", new ModSpec("AssetOnly"));
        var loader = new ModLoader(
            host.Vfs,
            new FunctionRegistry(),
            new TriggerManager());

        try
        {
            loader.LoadResolvedPlan(new[]
            {
                new ResolvedModLoadEntry("AssetOnly", host.ModRoots["AssetOnly"]),
            });

            string[] logicalPaths = LiteNetLibContentFingerprintComposer
                .CollectCoreGameplayAssemblyClosure(loader)
                .Select(entry => entry.LogicalPath)
                .ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(logicalPaths, Has.Some.Contains("/Arch.dll/"));
                Assert.That(logicalPaths, Has.Some.Contains("/Arch.System.dll/"));
                Assert.That(logicalPaths, Has.Some.Contains("/Ludots.Platform.Abstractions.dll/"));
                Assert.That(logicalPaths, Has.None.Contains("Ludots.Adapter."));
                Assert.That(logicalPaths, Has.None.Contains("LiteNetLib"));
                Assert.That(logicalPaths, Has.None.Contains("Raylib"));
                Assert.That(logicalPaths, Has.None.Contains("Browser"));
                Assert.That(logicalPaths, Has.None.Contains("Ludots.UI"));
            });
        }
        finally
        {
            loader.UnloadAll();
        }
    }

    [TestCase("Ludots.Adapter.Raylib")]
    [TestCase("Ludots.App.Raylib")]
    [TestCase("Ludots.Client.Raylib")]
    [TestCase("Ludots.Presentation.Skia")]
    [TestCase("Ludots.UI")]
    [TestCase("Ludots.WebUI")]
    [TestCase("Raylib-cs")]
    [TestCase("SkiaSharp")]
    [TestCase("ShimSkiaSharp")]
    [TestCase("Svg")]
    [TestCase("Svg.Skia")]
    [TestCase("AngleSharp")]
    [TestCase("ExCSS")]
    [TestCase("LiteNetLib")]
    public void PlatformAssemblyBoundary_ExcludesNonGameplayAssembly(string assemblyName)
    {
        Assert.That(
            LiteNetLibContentFingerprintComposer.IsNonGameplayAssembly(assemblyName),
            Is.True);
    }

    [TestCase("Arch")]
    [TestCase("Arch.System")]
    [TestCase("DotRecast.Detour")]
    [TestCase("Ludots.Physics2D")]
    [TestCase("Ludots.Platform.Abstractions")]
    public void PlatformAssemblyBoundary_KeepsGameplayAssembly(string assemblyName)
    {
        Assert.That(
            LiteNetLibContentFingerprintComposer.IsNonGameplayAssembly(assemblyName),
            Is.False);
    }

    [Test]
    public void PlatformAssemblyBoundary_PrunesARealManagedReferenceEdge()
    {
        byte[] rootAssembly = BuildManagedAssembly(
            "Gameplay.Root",
            Guid.NewGuid(),
            "Ludots.Presentation.Skia",
            "System.Runtime",
            "Arch");

        IReadOnlyList<string> gameplayReferences = LiteNetLibContentFingerprintComposer
            .ReadGameplayAssemblyReferenceNames(rootAssembly);

        Assert.That(gameplayReferences, Is.EqualTo(new[] { "Arch" }));
    }

    [Test]
    public void SharedFrameworkBoundary_CoversEveryFrameworkUnderTheRuntimeSharedRoot()
    {
        string sharedRoot = Path.Combine(_testRoot, "dotnet", "shared");
        string runtimeDirectory = Path.Combine(sharedRoot, "Microsoft.NETCore.App", "8.0.25");
        string aspNetAssembly = Path.Combine(
            sharedRoot,
            "Microsoft.AspNetCore.App",
            "8.0.25",
            "Microsoft.Extensions.DependencyInjection.dll");
        string windowsDesktopAssembly = Path.Combine(
            sharedRoot,
            "Microsoft.WindowsDesktop.App",
            "8.0.25",
            "PresentationCore.dll");
        string applicationAssembly = Path.Combine(_testRoot, "game", "Gameplay.Rules.dll");

        Assert.Multiple(() =>
        {
            Assert.That(
                LiteNetLibContentFingerprintComposer.IsSharedFrameworkAssemblyPath(
                    runtimeDirectory,
                    aspNetAssembly),
                Is.True);
            Assert.That(
                LiteNetLibContentFingerprintComposer.IsSharedFrameworkAssemblyPath(
                    runtimeDirectory,
                    windowsDesktopAssembly),
                Is.True);
            Assert.That(
                LiteNetLibContentFingerprintComposer.IsSharedFrameworkAssemblyPath(
                    runtimeDirectory,
                    applicationAssembly),
                Is.False);
        });
    }

    [Test]
    public void ResolverScope_BindsCoreAndLocationlessModDependenciesToTheirActualSources()
    {
        string coreDirectory = Path.Combine(_testRoot, "host");
        string firstModDirectory = Path.Combine(_testRoot, "mods", "First");
        string secondModDirectory = Path.Combine(_testRoot, "mods", "Second");
        Directory.CreateDirectory(coreDirectory);
        Directory.CreateDirectory(firstModDirectory);
        Directory.CreateDirectory(secondModDirectory);

        const string dependencyName = "Shared.Gameplay.Dependency";
        Guid coreMvid = Guid.NewGuid();
        Guid firstModMvid = Guid.NewGuid();
        Guid secondModMvid = Guid.NewGuid();
        string corePath = WriteManagedAssembly(coreDirectory, dependencyName, coreMvid);
        WriteManagedAssembly(firstModDirectory, dependencyName, firstModMvid);
        string secondModPath = WriteManagedAssembly(secondModDirectory, dependencyName, secondModMvid);
        AssemblyName reference = AssemblyName.GetAssemblyName(corePath);
        var noResolvers = Array.Empty<AssemblyDependencyResolver>();

        string resolvedCore = LiteNetLibContentFingerprintComposer.ResolveRequiredAssemblyPath(
            LiteNetLibContentFingerprintComposer.ManagedAssemblyResolverScope.Core,
            reference,
            noResolvers,
            new[] { coreDirectory },
            noResolvers,
            new[] { firstModDirectory, secondModDirectory },
            requiredMvid: null);
        string resolvedSecondMod = LiteNetLibContentFingerprintComposer.ResolveRequiredAssemblyPath(
            LiteNetLibContentFingerprintComposer.ManagedAssemblyResolverScope.ModPlan,
            reference,
            noResolvers,
            new[] { coreDirectory },
            noResolvers,
            new[] { firstModDirectory, secondModDirectory },
            secondModMvid);

        Assert.Multiple(() =>
        {
            Assert.That(resolvedCore, Is.EqualTo(Path.GetFullPath(corePath)));
            Assert.That(resolvedSecondMod, Is.EqualTo(Path.GetFullPath(secondModPath)));
        });
    }

    [Test]
    public void LoadedModDependency_PreloadedInDefaultContext_KeepsModResolverScope()
    {
        Assembly preloadedModDependency = typeof(LiteNetLibContentFingerprintComposerTests).Assembly;
        Assert.That(
            AssemblyLoadContext.GetLoadContext(preloadedModDependency),
            Is.SameAs(AssemblyLoadContext.Default));

        LiteNetLibContentFingerprintComposer.ManagedAssemblyResolverScope scope =
            LiteNetLibContentFingerprintComposer.GetLoadedDependencyResolverScope(
                LiteNetLibContentFingerprintComposer.ManagedAssemblyResolverScope.ModPlan,
                preloadedModDependency);

        Assert.That(
            scope,
            Is.EqualTo(LiteNetLibContentFingerprintComposer.ManagedAssemblyResolverScope.ModPlan));
    }

    private ContentFingerprint Compose(
        TestHost host,
        IReadOnlyList<string> orderedModIds,
        params ContentFingerprintContent[] managedAssemblies)
    {
        var orderedMods = new ResolvedModLoadEntry[orderedModIds.Count];
        for (int i = 0; i < orderedModIds.Count; i++)
        {
            string id = orderedModIds[i];
            orderedMods[i] = new ResolvedModLoadEntry(id, host.ModRoots[id]);
        }

        return LiteNetLibContentFingerprintComposer.ComposeFromHostContent(
            host.Vfs,
            ResolvedModLoadPlan.CreateExplicit(orderedMods),
            host.AssetsRoot,
            Protocol,
            managedAssemblies);
    }

    private TestHost CreateHost(string name, params ModSpec[] mods)
    {
        string root = Path.Combine(_testRoot, name);
        string assetsRoot = Path.Combine(root, "assets");
        Directory.CreateDirectory(assetsRoot);
        File.WriteAllText(Path.Combine(assetsRoot, "game.json"), "{}");

        var vfs = new VirtualFileSystem();
        vfs.Mount("Core", assetsRoot);
        var modRoots = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < mods.Length; i++)
        {
            ModSpec mod = mods[i];
            string modRoot = Path.Combine(root, "mods", mod.Id);
            Directory.CreateDirectory(Path.Combine(modRoot, "assets"));
            string mainField = mod.Main == null ? string.Empty : $",\"main\":\"{mod.Main}\"";
            File.WriteAllText(
                Path.Combine(modRoot, "mod.json"),
                $"{{\"name\":\"{mod.Id}\",\"version\":\"1.0.0\"{mainField},\"dependencies\":{{}}}}");
            vfs.Mount(mod.Id, modRoot);
            modRoots.Add(mod.Id, modRoot);
        }

        return new TestHost(vfs, assetsRoot, modRoots);
    }

    private static ContentFingerprintContent ManagedAssembly(string logicalPath, params byte[] bytes) =>
        new(logicalPath, bytes);

    private static string WriteManagedAssembly(string directory, string assemblyName, Guid mvid)
    {
        string path = Path.Combine(directory, assemblyName + ".dll");
        File.WriteAllBytes(path, BuildManagedAssembly(assemblyName, mvid));
        return path;
    }

    private static byte[] BuildManagedAssembly(
        string assemblyName,
        Guid mvid,
        params string[] referencedAssemblies)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString(assemblyName + ".dll"),
            mvid: metadata.GetOrAddGuid(mvid),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            name: metadata.GetOrAddString(assemblyName),
            version: new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: (AssemblyFlags)0,
            hashAlgorithm: AssemblyHashAlgorithm.None);
        for (int i = 0; i < referencedAssemblies.Length; i++)
        {
            metadata.AddAssemblyReference(
                name: metadata.GetOrAddString(referencedAssemblies[i]),
                version: new Version(1, 0, 0, 0),
                culture: default,
                publicKeyOrToken: default,
                flags: (AssemblyFlags)0,
                hashValue: default);
        }

        metadata.AddTypeDefinition(
            attributes: TypeAttributes.NotPublic,
            @namespace: default,
            name: metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        var peBuilder = new ManagedPEBuilder(
            new PEHeaderBuilder(
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        peBuilder.Serialize(image);
        return image.ToArray();
    }

    private sealed record TestHost(
        IVirtualFileSystem Vfs,
        string AssetsRoot,
        IReadOnlyDictionary<string, string> ModRoots);

    private sealed record ModSpec(string Id, string? Main = null);
}
