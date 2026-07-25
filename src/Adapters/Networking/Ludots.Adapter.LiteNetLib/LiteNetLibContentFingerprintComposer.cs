using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;
using Ludots.Core.Engine;
using Ludots.Core.Hosting;
using Ludots.Core.Modding;
using Ludots.Core.Networking.Session;

namespace Ludots.Adapter.LiteNetLib;

internal static class LiteNetLibContentFingerprintComposer
{
    private const string OrderedModPlanLogicalPath = "runtime/mod-load-order.v1";
    private static readonly string[] NonGameplayAssemblyPrefixes =
    {
        "Ludots.Adapter.",
        "Ludots.App.",
        "Ludots.Client.",
        "Ludots.Presentation.",
        "Ludots.UI",
        "Ludots.Web",
        "Raylib",
        "SkiaSharp",
        "ShimSkiaSharp",
        "Svg",
        "AngleSharp",
        "ExCSS",
        "CefSharp",
        "Microsoft.Web.WebView2",
        "Silk.NET.",
        "SDL2",
        "OpenTK",
        "Veldrid",
    };
    private static readonly HashSet<string> NonGameplayAssemblyNames = new(StringComparer.Ordinal)
    {
        "LiteNetLib",
    };

    public static ContentFingerprint Compose(
        GameEngine engine,
        ResolvedModLoadPlan modPlan,
        string baseAssetsRoot,
        ProtocolVersion protocolVersion)
    {
        ArgumentNullException.ThrowIfNull(engine);
        IVirtualFileSystem vfs = engine.VFS ??
            throw new InvalidOperationException("Network content fingerprint requires the initialized runtime VFS.");
        ModLoader modLoader = engine.ModLoader ??
            throw new InvalidOperationException("Network content fingerprint requires the initialized Mod loader.");

        List<ContentFingerprintContent> content = CollectMountedContent(
            vfs,
            modPlan,
            baseAssetsRoot,
            out List<ModManagedAssemblyRoot> managedRoots);
        AddExecutedManagedAssemblyClosure(content, modLoader, managedRoots);
        return ContentFingerprintCanonicalizer.FromContent(protocolVersion, content);
    }

    internal static IReadOnlyList<ContentFingerprintContent> CollectCoreGameplayAssemblyClosure(
        ModLoader modLoader)
    {
        ArgumentNullException.ThrowIfNull(modLoader);
        var content = new List<ContentFingerprintContent>();
        AddExecutedManagedAssemblyClosure(content, modLoader, Array.Empty<ModManagedAssemblyRoot>());
        return content;
    }

    internal static ManagedAssemblyResolverScope GetLoadedDependencyResolverScope(
        ManagedAssemblyResolverScope sourceScope,
        Assembly loadedDependency)
    {
        ArgumentNullException.ThrowIfNull(loadedDependency);
        // Loading location identifies the executed bytes; the root graph owns resolver policy.
        return sourceScope;
    }

    internal static IReadOnlyList<string> ReadGameplayAssemblyReferenceNames(byte[] assemblyBytes)
    {
        ArgumentNullException.ThrowIfNull(assemblyBytes);
        HashSet<string> runtimeFrameworkAssemblies = GetRuntimeFrameworkAssemblyNames();
        return ReadManagedAssemblyReferences(assemblyBytes, "Managed assembly reference test input")
            .Select(static reference => reference.Name ?? string.Empty)
            .Where(name => !IsExcludedAssemblyReference(name, runtimeFrameworkAssemblies))
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
    }

    internal static ContentFingerprint ComposeFromHostContent(
        IVirtualFileSystem vfs,
        ResolvedModLoadPlan modPlan,
        string baseAssetsRoot,
        ProtocolVersion protocolVersion,
        IReadOnlyList<ContentFingerprintContent> managedAssemblyContent)
    {
        ArgumentNullException.ThrowIfNull(managedAssemblyContent);
        if (managedAssemblyContent.Count == 0)
        {
            throw new InvalidOperationException(
                "Network content fingerprint requires host-provided managed assembly content.");
        }

        List<ContentFingerprintContent> content = CollectMountedContent(
            vfs,
            modPlan,
            baseAssetsRoot,
            out _);
        for (int i = 0; i < managedAssemblyContent.Count; i++)
        {
            content.Add(managedAssemblyContent[i]);
        }

        return ContentFingerprintCanonicalizer.FromContent(protocolVersion, content);
    }

    private static List<ContentFingerprintContent> CollectMountedContent(
        IVirtualFileSystem vfs,
        ResolvedModLoadPlan modPlan,
        string baseAssetsRoot,
        out List<ModManagedAssemblyRoot> managedRoots)
    {
        ArgumentNullException.ThrowIfNull(vfs);
        ArgumentNullException.ThrowIfNull(modPlan);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseAssetsRoot);
        if (modPlan.OrderedMods == null || modPlan.OrderedMods.Count == 0)
        {
            throw new InvalidOperationException(
                "Network content fingerprint requires a non-empty resolved mod load plan.");
        }

        ValidateModPlan(modPlan.OrderedMods);
        var content = new List<ContentFingerprintContent>(128)
        {
            new(OrderedModPlanLogicalPath, BuildOrderedModPlanBytes(modPlan.OrderedMods)),
        };
        managedRoots = new List<ModManagedAssemblyRoot>(modPlan.OrderedMods.Count);

        AddMountedTree(
            content,
            vfs,
            "Core",
            Path.GetFullPath(baseAssetsRoot),
            physicalRelativeRoot: string.Empty,
            logicalRoot: "base-assets");

        for (int i = 0; i < modPlan.OrderedMods.Count; i++)
        {
            ResolvedModLoadEntry entry = modPlan.OrderedMods[i];
            string modRoot = Path.GetFullPath(entry.RootPath);
            if (!Directory.Exists(modRoot))
            {
                throw new DirectoryNotFoundException(
                    $"Resolved mod '{entry.Id}' root does not exist: {modRoot}");
            }

            byte[] manifestBytes = ReadRequiredMountedFile(vfs, entry.Id, "mod.json");
            content.Add(new ContentFingerprintContent(
                $"mods/{entry.Id}/mod.json",
                manifestBytes));
            ModManifest manifest = ModManifestJson.ParseStrict(
                DecodeUtf8Text(manifestBytes, $"{entry.Id}:mod.json"),
                $"{entry.Id}:mod.json");
            if (!string.Equals(manifest.Name, entry.Id, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Resolved mod id '{entry.Id}' does not match manifest name '{manifest.Name}'.");
            }

            string assetsRoot = Path.Combine(modRoot, "assets");
            if (Directory.Exists(assetsRoot))
            {
                AddMountedTree(
                    content,
                    vfs,
                    entry.Id,
                    modRoot,
                    physicalRelativeRoot: "assets",
                    logicalRoot: $"mods/{entry.Id}/assets");
            }

            if (!string.IsNullOrWhiteSpace(manifest.Main))
            {
                string mainRelative = NormalizeMountedRelativePath(
                    modRoot,
                    manifest.Main,
                    $"Resolved mod '{entry.Id}' main assembly");
                byte[] declaredBytes = ReadRequiredMountedFile(vfs, entry.Id, mainRelative);
                string mainFullPath = Path.GetFullPath(Path.Combine(
                    modRoot,
                    mainRelative.Replace('/', Path.DirectorySeparatorChar)));
                managedRoots.Add(new ModManagedAssemblyRoot(
                    entry.Id,
                    mainRelative,
                    mainFullPath,
                    declaredBytes));
            }
        }

        return content;
    }

    private static void AddExecutedManagedAssemblyClosure(
        List<ContentFingerprintContent> content,
        ModLoader modLoader,
        IReadOnlyList<ModManagedAssemblyRoot> managedRoots)
    {
        IReadOnlyList<Assembly> loadedAssemblies = modLoader.LoadedAssemblies;
        Assembly coreAssembly = typeof(GameEngine).Assembly;
        List<Assembly> coreAssemblyCatalog = BuildCoreHostAssemblyCatalog(coreAssembly);
        List<Assembly> modAssemblyCatalog = BuildAssemblyCatalog(
            loadedAssemblies,
            coreAssemblyCatalog);
        var coreResolvers = new List<AssemblyDependencyResolver>(1);
        var coreSearchDirectories = new List<string>(1);
        var modResolvers = new List<AssemblyDependencyResolver>(managedRoots.Count);
        var modSearchDirectories = new List<string>(managedRoots.Count);
        var pending = new Queue<ManagedAssemblyNode>();
        var seenMvids = new HashSet<Guid>();
        HashSet<string> runtimeFrameworkAssemblies = GetRuntimeFrameworkAssemblyNames();

        string coreAssemblyPath = RequireLoadedAssemblyLocation(coreAssembly, "Ludots Core assembly");
        byte[] coreBytes = ReadRequiredPhysicalFile(coreAssemblyPath, "Ludots Core assembly");
        ManagedAssemblyNode coreNode = CreateLoadedAssemblyNode(
            coreAssembly,
            coreBytes,
            "Ludots Core assembly",
            ManagedAssemblyResolverScope.Core);
        content.Add(new ContentFingerprintContent(
            "assemblies/gameplay/core/Ludots.Core.dll",
            coreBytes));
        EnqueueOnce(coreNode, pending, seenMvids);
        AddAssemblySearchRoot(coreAssemblyPath, coreResolvers, coreSearchDirectories);
        DrainManagedAssemblyClosure(
            content,
            pending,
            seenMvids,
            runtimeFrameworkAssemblies,
            coreAssemblyCatalog,
            modAssemblyCatalog,
            coreResolvers,
            coreSearchDirectories,
            modResolvers,
            modSearchDirectories);

        for (int i = 0; i < managedRoots.Count; i++)
        {
            ModManagedAssemblyRoot root = managedRoots[i];
            Guid declaredMvid = ReadManagedAssemblyMvid(
                root.DeclaredBytes,
                $"Resolved mod '{root.ModId}' main assembly '{root.MainRelativePath}'");
            Assembly loadedMain = FindLoadedModMain(
                loadedAssemblies,
                root.ModId,
                declaredMvid);
            byte[] executedBytes;
            if (TryReadLoadedAssemblyBytes(
                    loadedMain,
                    $"Loaded mod assembly '{root.ModId}'",
                    out byte[] locatedBytes))
            {
                if (!root.DeclaredBytes.AsSpan().SequenceEqual(locatedBytes))
                {
                    throw new InvalidOperationException(
                        $"Loaded mod assembly '{root.ModId}' does not match its declared source " +
                        $"'{root.MainRelativePath}'.");
                }

                executedBytes = locatedBytes;
            }
            else
            {
                executedBytes = root.DeclaredBytes;
            }

            ManagedAssemblyNode mainNode = CreateLoadedAssemblyNode(
                loadedMain,
                executedBytes,
                $"Loaded mod assembly '{root.ModId}'",
                ManagedAssemblyResolverScope.ModPlan);
            content.Add(new ContentFingerprintContent(
                $"assemblies/gameplay/mods/{root.ModId}/{LogicalAssemblyFileName(loadedMain)}",
                executedBytes));
            EnqueueOnce(mainNode, pending, seenMvids);
            modResolvers.Add(new AssemblyDependencyResolver(root.MainFullPath));
            AddSearchDirectory(modSearchDirectories, Path.GetDirectoryName(root.MainFullPath));
        }

        DrainManagedAssemblyClosure(
            content,
            pending,
            seenMvids,
            runtimeFrameworkAssemblies,
            coreAssemblyCatalog,
            modAssemblyCatalog,
            coreResolvers,
            coreSearchDirectories,
            modResolvers,
            modSearchDirectories);
    }

    private static void DrainManagedAssemblyClosure(
        List<ContentFingerprintContent> content,
        Queue<ManagedAssemblyNode> pending,
        HashSet<Guid> seenMvids,
        IReadOnlySet<string> runtimeFrameworkAssemblies,
        IReadOnlyList<Assembly> coreAssemblyCatalog,
        IReadOnlyList<Assembly> modAssemblyCatalog,
        IReadOnlyList<AssemblyDependencyResolver> coreResolvers,
        IReadOnlyList<string> coreSearchDirectories,
        IReadOnlyList<AssemblyDependencyResolver> modResolvers,
        IReadOnlyList<string> modSearchDirectories)
    {
        while (pending.Count > 0)
        {
            ManagedAssemblyNode source = pending.Dequeue();
            AssemblyName[] references = source.References;
            Array.Sort(references, static (left, right) =>
                StringComparer.Ordinal.Compare(left.FullName, right.FullName));
            for (int i = 0; i < references.Length; i++)
            {
                AssemblyName reference = references[i];
                string referenceName = reference.Name ?? string.Empty;
                if (IsExcludedAssemblyReference(referenceName, runtimeFrameworkAssemblies))
                {
                    continue;
                }

                ManagedAssemblyNode dependency = ResolveManagedAssemblyNode(
                    source,
                    reference,
                    coreAssemblyCatalog,
                    modAssemblyCatalog,
                    coreResolvers,
                    coreSearchDirectories,
                    modResolvers,
                    modSearchDirectories);
                if (!seenMvids.Add(dependency.Mvid))
                {
                    continue;
                }

                content.Add(new ContentFingerprintContent(
                    $"assemblies/gameplay/closure/{LogicalAssemblyFileName(dependency.Identity)}/" +
                    $"{dependency.Mvid:N}.dll",
                    dependency.Bytes));
                pending.Enqueue(dependency);
            }
        }
    }

    private static ManagedAssemblyNode ResolveManagedAssemblyNode(
        ManagedAssemblyNode source,
        AssemblyName reference,
        IReadOnlyList<Assembly> coreAssemblyCatalog,
        IReadOnlyList<Assembly> modAssemblyCatalog,
        IReadOnlyList<AssemblyDependencyResolver> coreResolvers,
        IReadOnlyList<string> coreSearchDirectories,
        IReadOnlyList<AssemblyDependencyResolver> modResolvers,
        IReadOnlyList<string> modSearchDirectories)
    {
        ManagedAssemblyResolverScope scope = source.Scope;
        IReadOnlyList<Assembly> assemblyCatalog = scope == ManagedAssemblyResolverScope.Core
            ? coreAssemblyCatalog
            : modAssemblyCatalog;
        Assembly? loaded = FindLoadedAssembly(source.SourceLoadContext?.Assemblies, reference) ??
            FindLoadedAssembly(assemblyCatalog, reference);
        if (loaded != null)
        {
            byte[] bytes;
            if (!TryReadLoadedAssemblyBytes(
                    loaded,
                    $"Gameplay dependency '{reference.FullName}'",
                    out bytes))
            {
                string path = ResolveRequiredAssemblyPath(
                    scope,
                    reference,
                    coreResolvers,
                    coreSearchDirectories,
                    modResolvers,
                    modSearchDirectories,
                    loaded.ManifestModule.ModuleVersionId);
                bytes = ReadRequiredPhysicalFile(path, $"Gameplay dependency '{reference.FullName}'");
            }

            return CreateLoadedAssemblyNode(
                loaded,
                bytes,
                $"Gameplay dependency '{reference.FullName}'",
                GetLoadedDependencyResolverScope(scope, loaded));
        }

        string sourcePath = ResolveRequiredAssemblyPath(
            scope,
            reference,
            coreResolvers,
            coreSearchDirectories,
            modResolvers,
            modSearchDirectories,
            requiredMvid: null);
        byte[] sourceBytes = ReadRequiredPhysicalFile(
            sourcePath,
            $"Gameplay dependency '{reference.FullName}'");
        AssemblyName identity = AssemblyName.GetAssemblyName(sourcePath);
        if (!AssemblyName.ReferenceMatchesDefinition(reference, identity))
        {
            throw new InvalidOperationException(
                $"Resolved gameplay dependency '{sourcePath}' does not match '{reference.FullName}'.");
        }

        return CreateFileAssemblyNode(identity, sourceBytes, sourcePath, scope);
    }

    private static ManagedAssemblyNode CreateLoadedAssemblyNode(
        Assembly loadedAssembly,
        byte[] bytes,
        string description,
        ManagedAssemblyResolverScope scope)
    {
        Guid loadedMvid;
        try
        {
            loadedMvid = loadedAssembly.ManifestModule.ModuleVersionId;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Cannot inspect loaded managed assembly '{loadedAssembly.FullName}'.",
                ex);
        }

        Guid sourceMvid = ReadManagedAssemblyMvid(bytes, description);
        if (sourceMvid != loadedMvid)
        {
            throw new InvalidOperationException(
                $"Managed assembly source no longer matches the loaded module: '{loadedAssembly.FullName}'.");
        }

        return new ManagedAssemblyNode(
            loadedAssembly.GetName(),
            sourceMvid,
            bytes,
            loadedAssembly.GetReferencedAssemblies(),
            scope,
            AssemblyLoadContext.GetLoadContext(loadedAssembly));
    }

    private static ManagedAssemblyNode CreateFileAssemblyNode(
        AssemblyName identity,
        byte[] bytes,
        string description,
        ManagedAssemblyResolverScope scope) =>
        new(
            identity,
            ReadManagedAssemblyMvid(bytes, description),
            bytes,
            ReadManagedAssemblyReferences(bytes, description),
            scope,
            SourceLoadContext: null);

    private static Assembly FindLoadedModMain(
        IReadOnlyList<Assembly> loadedAssemblies,
        string modId,
        Guid declaredMvid)
    {
        Assembly? nameMatch = null;
        Assembly? mvidMatch = null;
        for (int i = 0; i < loadedAssemblies.Count; i++)
        {
            Assembly candidate = loadedAssemblies[i];
            if (string.Equals(candidate.GetName().Name, modId, StringComparison.Ordinal))
            {
                if (nameMatch != null && !ReferenceEquals(nameMatch, candidate))
                {
                    throw new InvalidOperationException(
                        $"Resolved mod '{modId}' has multiple loaded main assembly candidates.");
                }

                nameMatch = candidate;
            }

            if (candidate.ManifestModule.ModuleVersionId == declaredMvid)
            {
                if (mvidMatch != null && !ReferenceEquals(mvidMatch, candidate))
                {
                    throw new InvalidOperationException(
                        $"Resolved mod '{modId}' main module identity is ambiguous in the active Mod loader.");
                }

                mvidMatch = candidate;
            }
        }

        return nameMatch ?? mvidMatch ?? throw new InvalidOperationException(
            $"Resolved mod '{modId}' main assembly is not present in the active Mod loader.");
    }

    private static Assembly? FindLoadedAssembly(
        IEnumerable<Assembly>? assemblyCatalog,
        AssemblyName reference)
    {
        if (assemblyCatalog == null)
        {
            return null;
        }

        foreach (Assembly candidate in assemblyCatalog)
        {
            if (AssemblyName.ReferenceMatchesDefinition(reference, candidate.GetName()))
            {
                return candidate;
            }
        }

        return null;
    }

    internal static string ResolveRequiredAssemblyPath(
        ManagedAssemblyResolverScope scope,
        AssemblyName assemblyName,
        IReadOnlyList<AssemblyDependencyResolver> coreResolvers,
        IReadOnlyList<string> coreSearchDirectories,
        IReadOnlyList<AssemblyDependencyResolver> modResolvers,
        IReadOnlyList<string> modSearchDirectories,
        Guid? requiredMvid)
    {
        if (scope == ManagedAssemblyResolverScope.ModPlan &&
            TryResolveAssemblyPath(
                modResolvers,
                modSearchDirectories,
                assemblyName,
                requiredMvid,
                out string? modPath))
        {
            return modPath;
        }

        if (TryResolveAssemblyPath(
                coreResolvers,
                coreSearchDirectories,
                assemblyName,
                requiredMvid,
                out string? corePath))
        {
            return corePath;
        }

        throw new FileNotFoundException(
            $"Cannot resolve source bytes for gameplay dependency '{assemblyName.FullName}'.");
    }

    private static bool TryResolveAssemblyPath(
        IReadOnlyList<AssemblyDependencyResolver> resolvers,
        IReadOnlyList<string> searchDirectories,
        AssemblyName assemblyName,
        Guid? requiredMvid,
        out string path)
    {
        for (int i = 0; i < resolvers.Count; i++)
        {
            string? resolvedPath = resolvers[i].ResolveAssemblyToPath(assemblyName);
            if (!string.IsNullOrWhiteSpace(resolvedPath) &&
                IsMatchingAssemblySource(resolvedPath, assemblyName, requiredMvid))
            {
                path = Path.GetFullPath(resolvedPath);
                return true;
            }
        }

        string? simpleName = assemblyName.Name;
        if (!string.IsNullOrWhiteSpace(simpleName))
        {
            for (int i = 0; i < searchDirectories.Count; i++)
            {
                string candidate = Path.Combine(searchDirectories[i], simpleName + ".dll");
                if (!File.Exists(candidate))
                {
                    continue;
                }

                if (IsMatchingAssemblySource(candidate, assemblyName, requiredMvid))
                {
                    path = Path.GetFullPath(candidate);
                    return true;
                }
            }
        }

        path = string.Empty;
        return false;
    }

    private static bool IsMatchingAssemblySource(
        string candidatePath,
        AssemblyName requestedIdentity,
        Guid? requiredMvid)
    {
        AssemblyName candidateIdentity = AssemblyName.GetAssemblyName(candidatePath);
        if (!AssemblyName.ReferenceMatchesDefinition(requestedIdentity, candidateIdentity))
        {
            return false;
        }

        return !requiredMvid.HasValue ||
            ReadManagedAssemblyMvid(
                ReadRequiredPhysicalFile(candidatePath, $"Managed assembly candidate '{candidatePath}'"),
                $"Managed assembly candidate '{candidatePath}'") == requiredMvid.Value;
    }

    private static List<Assembly> BuildAssemblyCatalog(
        IEnumerable<Assembly> primaryAssemblies,
        IEnumerable<Assembly> secondaryAssemblies)
    {
        var catalog = new List<Assembly>(64);
        var seen = new HashSet<Assembly>();
        foreach (Assembly assembly in primaryAssemblies)
        {
            if (!assembly.IsDynamic && seen.Add(assembly))
            {
                catalog.Add(assembly);
            }
        }

        foreach (Assembly assembly in secondaryAssemblies)
        {
            if (!assembly.IsDynamic && seen.Add(assembly))
            {
                catalog.Add(assembly);
            }
        }

        return catalog;
    }

    private static List<Assembly> BuildCoreHostAssemblyCatalog(Assembly coreAssembly)
    {
        AssemblyLoadContext coreLoadContext = AssemblyLoadContext.GetLoadContext(coreAssembly) ??
            throw new InvalidOperationException("Ludots Core assembly has no active load context.");
        return BuildAssemblyCatalog(
            coreLoadContext.Assemblies,
            AssemblyLoadContext.Default.Assemblies);
    }

    private static HashSet<string> GetRuntimeFrameworkAssemblyNames()
    {
        string? trustedPlatformAssemblies =
            AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
        {
            throw new InvalidOperationException(
                "Runtime trusted platform assembly metadata is unavailable for gameplay content classification.");
        }

        string runtimeDirectory = Path.GetFullPath(RuntimeEnvironment.GetRuntimeDirectory());
        string sharedFrameworkRoot = GetRequiredSharedFrameworkRoot(runtimeDirectory);
        var names = new HashSet<string>(StringComparer.Ordinal);
        string[] paths = trustedPlatformAssemblies.Split(
            Path.PathSeparator,
            StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < paths.Length; i++)
        {
            string fullPath = Path.GetFullPath(paths[i]);
            if (IsPathWithinRoot(sharedFrameworkRoot, fullPath))
            {
                names.Add(Path.GetFileNameWithoutExtension(fullPath));
            }
        }

        return names;
    }

    internal static bool IsSharedFrameworkAssemblyPath(
        string runtimeDirectory,
        string candidateAssemblyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateAssemblyPath);
        return IsPathWithinRoot(
            GetRequiredSharedFrameworkRoot(Path.GetFullPath(runtimeDirectory)),
            Path.GetFullPath(candidateAssemblyPath));
    }

    private static bool IsPathWithinRoot(string rootPath, string candidatePath)
    {
        string relative = Path.GetRelativePath(rootPath, candidatePath);
        return relative != ".." &&
            !Path.IsPathRooted(relative) &&
            !relative.StartsWith("../", StringComparison.Ordinal) &&
            !relative.StartsWith("..\\", StringComparison.Ordinal);
    }

    private static string GetRequiredSharedFrameworkRoot(string runtimeDirectory)
    {
        var versionDirectory = new DirectoryInfo(
            runtimeDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        DirectoryInfo? sharedDirectory = versionDirectory.Parent?.Parent;
        if (sharedDirectory == null ||
            !string.Equals(sharedDirectory.Name, "shared", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Runtime directory is not inside a supported .NET shared framework layout: {runtimeDirectory}");
        }

        return sharedDirectory.FullName;
    }

    internal static bool IsNonGameplayAssembly(string assemblyName)
    {
        if (NonGameplayAssemblyNames.Contains(assemblyName))
        {
            return true;
        }

        for (int i = 0; i < NonGameplayAssemblyPrefixes.Length; i++)
        {
            if (assemblyName.StartsWith(
                    NonGameplayAssemblyPrefixes[i],
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsExcludedAssemblyReference(
        string assemblyName,
        IReadOnlySet<string> runtimeFrameworkAssemblies) =>
        runtimeFrameworkAssemblies.Contains(assemblyName) ||
        IsNonGameplayAssembly(assemblyName);

    private static void EnqueueOnce(
        ManagedAssemblyNode node,
        Queue<ManagedAssemblyNode> pending,
        HashSet<Guid> seenMvids)
    {
        if (seenMvids.Add(node.Mvid))
        {
            pending.Enqueue(node);
        }
    }

    private static void AddAssemblySearchRoot(
        string assemblyPath,
        List<AssemblyDependencyResolver> resolvers,
        List<string> searchDirectories)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath))
        {
            throw new FileNotFoundException(
                "Gameplay root assembly has no physical location for dependency resolution.");
        }

        string fullPath = Path.GetFullPath(assemblyPath);
        resolvers.Add(new AssemblyDependencyResolver(fullPath));
        AddSearchDirectory(searchDirectories, Path.GetDirectoryName(fullPath));
    }

    private static void AddSearchDirectory(List<string> directories, string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new DirectoryNotFoundException("Gameplay assembly search directory is unavailable.");
        }

        string fullPath = Path.GetFullPath(directory);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException(
                $"Gameplay assembly search directory does not exist: {fullPath}");
        }

        if (!directories.Contains(fullPath, StringComparer.Ordinal))
        {
            directories.Add(fullPath);
        }
    }

    private static byte[] ReadLoadedAssemblyBytes(Assembly assembly, string description)
        => ReadRequiredPhysicalFile(
            RequireLoadedAssemblyLocation(assembly, description),
            description);

    private static bool TryReadLoadedAssemblyBytes(
        Assembly assembly,
        string description,
        out byte[] bytes)
    {
        string location;
        try
        {
            location = assembly.Location;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"{description} has no inspectable physical location.", ex);
        }

        if (string.IsNullOrWhiteSpace(location))
        {
            bytes = Array.Empty<byte>();
            return false;
        }

        bytes = ReadRequiredPhysicalFile(location, description);
        return true;
    }

    private static string RequireLoadedAssemblyLocation(Assembly assembly, string description)
    {
        string location;
        try
        {
            location = assembly.Location;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"{description} has no inspectable physical location.", ex);
        }

        if (string.IsNullOrWhiteSpace(location))
        {
            throw new FileNotFoundException(
                $"{description} has no physical location for content fingerprinting.");
        }

        return Path.GetFullPath(location);
    }

    private static byte[] ReadRequiredPhysicalFile(string path, string description)
    {
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"{description} is missing.", fullPath);
        }

        try
        {
            return File.ReadAllBytes(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"{description} cannot be read: {fullPath}", ex);
        }
    }

    private static Guid ReadManagedAssemblyMvid(byte[] bytes, string description)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var peReader = new PEReader(stream, PEStreamOptions.LeaveOpen);
            if (!peReader.HasMetadata)
            {
                throw new BadImageFormatException($"{description} has no managed metadata.");
            }

            MetadataReader metadata = peReader.GetMetadataReader();
            return metadata.GetGuid(metadata.GetModuleDefinition().Mvid);
        }
        catch (BadImageFormatException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new BadImageFormatException($"{description} is not a readable managed assembly.", ex);
        }
    }

    private static AssemblyName[] ReadManagedAssemblyReferences(byte[] bytes, string description)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var peReader = new PEReader(stream, PEStreamOptions.LeaveOpen);
            if (!peReader.HasMetadata)
            {
                throw new BadImageFormatException($"{description} has no managed metadata.");
            }

            MetadataReader metadata = peReader.GetMetadataReader();
            var references = new AssemblyName[metadata.AssemblyReferences.Count];
            int index = 0;
            foreach (AssemblyReferenceHandle handle in metadata.AssemblyReferences)
            {
                AssemblyReference reference = metadata.GetAssemblyReference(handle);
                var identity = new AssemblyName
                {
                    Name = metadata.GetString(reference.Name),
                    Version = reference.Version,
                };

                string culture = metadata.GetString(reference.Culture);
                if (culture.Length > 0)
                {
                    identity.CultureName = culture;
                }

                byte[] publicKeyOrToken = metadata.GetBlobBytes(reference.PublicKeyOrToken);
                if (publicKeyOrToken.Length > 0)
                {
                    if ((reference.Flags & AssemblyFlags.PublicKey) != 0)
                    {
                        identity.SetPublicKey(publicKeyOrToken);
                    }
                    else
                    {
                        identity.SetPublicKeyToken(publicKeyOrToken);
                    }
                }

                references[index++] = identity;
            }

            return references;
        }
        catch (BadImageFormatException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new BadImageFormatException($"{description} is not a readable managed assembly.", ex);
        }
    }

    private static string LogicalAssemblyFileName(Assembly assembly)
        => LogicalAssemblyFileName(assembly.GetName());

    private static string LogicalAssemblyFileName(AssemblyName identity)
    {
        string? name = identity.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Managed assembly has no simple name.");
        }

        return Uri.EscapeDataString(name.Normalize(NormalizationForm.FormC)) + ".dll";
    }

    private static byte[] BuildOrderedModPlanBytes(IReadOnlyList<ResolvedModLoadEntry> orderedMods)
    {
        using var stream = new MemoryStream();
        WriteUInt32(stream, checked((uint)orderedMods.Count));
        for (int i = 0; i < orderedMods.Count; i++)
        {
            byte[] idBytes = Encoding.UTF8.GetBytes(orderedMods[i].Id.Normalize(NormalizationForm.FormC));
            WriteUInt32(stream, checked((uint)idBytes.Length));
            stream.Write(idBytes);
        }

        return stream.ToArray();
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void ValidateModPlan(IReadOnlyList<ResolvedModLoadEntry> orderedMods)
    {
        var seenModIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < orderedMods.Count; i++)
        {
            ResolvedModLoadEntry entry = orderedMods[i] ??
                throw new InvalidOperationException($"Resolved mod load plan entry {i} is null.");
            ValidateModId(entry.Id, i);
            if (!seenModIds.Add(entry.Id))
            {
                throw new InvalidOperationException(
                    $"Resolved mod load plan contains duplicate mod id '{entry.Id}'.");
            }

            if (string.IsNullOrWhiteSpace(entry.RootPath))
            {
                throw new InvalidOperationException(
                    $"Resolved mod load plan entry '{entry.Id}' has no root path.");
            }
        }
    }

    private static void AddMountedTree(
        List<ContentFingerprintContent> content,
        IVirtualFileSystem vfs,
        string mountId,
        string physicalRoot,
        string physicalRelativeRoot,
        string logicalRoot)
    {
        string treeRoot = string.IsNullOrEmpty(physicalRelativeRoot)
            ? physicalRoot
            : Path.GetFullPath(Path.Combine(physicalRoot, physicalRelativeRoot));
        if (!Directory.Exists(treeRoot))
        {
            throw new DirectoryNotFoundException($"Content root does not exist: {treeRoot}");
        }

        var relativePaths = new List<string>(128);
        CollectFiles(physicalRoot, treeRoot, relativePaths);
        for (int i = 0; i < relativePaths.Count; i++)
        {
            string relativePath = relativePaths[i];
            string logicalRelativePath;
            if (physicalRelativeRoot.Length == 0)
            {
                logicalRelativePath = relativePath;
            }
            else
            {
                string expectedPrefix = physicalRelativeRoot.Replace('\\', '/') + "/";
                if (!relativePath.StartsWith(expectedPrefix, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Enumerated content '{relativePath}' is outside expected tree '{physicalRelativeRoot}'.");
                }

                logicalRelativePath = relativePath[expectedPrefix.Length..];
            }

            content.Add(new ContentFingerprintContent(
                logicalRoot + "/" + logicalRelativePath,
                ReadRequiredMountedFile(vfs, mountId, relativePath)));
        }
    }

    private static void CollectFiles(string physicalRoot, string directory, List<string> destination)
    {
        foreach (string file in Directory.EnumerateFiles(directory))
        {
            destination.Add(ToRootRelative(physicalRoot, file));
        }

        foreach (string child in Directory.EnumerateDirectories(directory))
        {
            CollectFiles(physicalRoot, child, destination);
        }
    }

    private static byte[] ReadRequiredMountedFile(
        IVirtualFileSystem vfs,
        string mountId,
        string relativePath)
    {
        string uri = mountId + ":" + relativePath;
        try
        {
            using Stream source = vfs.GetStream(uri);
            using var destination = source.CanSeek && source.Length <= int.MaxValue
                ? new MemoryStream(checked((int)source.Length))
                : new MemoryStream();
            source.CopyTo(destination);
            return destination.ToArray();
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new FileNotFoundException(
                $"Required network fingerprint content is missing: {uri}",
                uri,
                ex);
        }
    }

    private static string DecodeUtf8Text(byte[] bytes, string description)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = new StreamReader(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidDataException($"Required network fingerprint content is not valid UTF-8: {description}", ex);
        }
    }

    private static string NormalizeMountedRelativePath(
        string physicalRoot,
        string declaredPath,
        string description)
    {
        string portablePath = declaredPath.Replace('\\', '/');
        if ((portablePath.Length > 0 && portablePath[0] == '/') ||
            portablePath.IndexOf(':') >= 0)
        {
            throw new InvalidOperationException($"{description} must be relative.");
        }

        string fullPath = Path.GetFullPath(Path.Combine(
            physicalRoot,
            portablePath.Replace('/', Path.DirectorySeparatorChar)));
        string relativePath = ToRootRelative(physicalRoot, fullPath);
        if (relativePath.Length == 0)
        {
            throw new InvalidOperationException($"{description} must name a file.");
        }

        return relativePath;
    }

    private static string ToRootRelative(string physicalRoot, string fullPath)
    {
        string relative = Path.GetRelativePath(physicalRoot, fullPath);
        if (relative == ".." ||
            relative.StartsWith("../", StringComparison.Ordinal) ||
            relative.StartsWith("..\\", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Content path escapes its mounted root: {fullPath}");
        }

        return relative.Replace('\\', '/');
    }

    private static void ValidateModId(string modId, int index)
    {
        if (string.IsNullOrWhiteSpace(modId))
        {
            throw new InvalidOperationException($"Resolved mod load plan entry {index} has no mod id.");
        }

        for (int i = 0; i < modId.Length; i++)
        {
            char value = modId[i];
            if (value is '/' or '\\' or ':' || char.IsControl(value))
            {
                throw new InvalidOperationException(
                    $"Resolved mod id '{modId}' cannot be represented as a mounted content scope.");
            }
        }
    }

    private readonly record struct ModManagedAssemblyRoot(
        string ModId,
        string MainRelativePath,
        string MainFullPath,
        byte[] DeclaredBytes);

    private sealed record ManagedAssemblyNode(
        AssemblyName Identity,
        Guid Mvid,
        byte[] Bytes,
        AssemblyName[] References,
        ManagedAssemblyResolverScope Scope,
        AssemblyLoadContext? SourceLoadContext);

    internal enum ManagedAssemblyResolverScope : byte
    {
        Core = 0,
        ModPlan = 1,
    }
}
