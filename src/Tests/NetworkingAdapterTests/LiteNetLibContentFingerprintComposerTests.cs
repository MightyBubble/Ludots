using System.Reflection;
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
    public void LoadedAssemblyResolverScope_FollowsHostOrIsolatedLoadOwnership()
    {
        Assembly defaultLoaded = typeof(LiteNetLibContentFingerprintComposerTests).Assembly;
        Assert.That(
            LiteNetLibContentFingerprintComposer.IsCoreResolverOwnedAssembly(defaultLoaded),
            Is.True);

        var isolatedContext = new AssemblyLoadContext(
            $"{nameof(LoadedAssemblyResolverScope_FollowsHostOrIsolatedLoadOwnership)}-{Guid.NewGuid():N}",
            isCollectible: true);
        try
        {
            using Stream assemblyBytes = File.OpenRead(defaultLoaded.Location);
            Assembly isolated = isolatedContext.LoadFromStream(assemblyBytes);
            Assert.That(
                LiteNetLibContentFingerprintComposer.IsCoreResolverOwnedAssembly(isolated),
                Is.False);
        }
        finally
        {
            isolatedContext.Unload();
        }
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

    private sealed record TestHost(
        IVirtualFileSystem Vfs,
        string AssetsRoot,
        IReadOnlyDictionary<string, string> ModRoots);

    private sealed record ModSpec(string Id, string? Main = null);
}
