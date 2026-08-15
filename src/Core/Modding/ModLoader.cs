using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Ludots.Core.Diagnostics;
using Ludots.Core.Engine;
using Ludots.Core.Config;
using Ludots.Core.Hosting;
using Ludots.Core.Scripting;
using Ludots.Core.Map;

namespace Ludots.Core.Modding
{
    public class ModLoader
    {
        private readonly IVirtualFileSystem _vfs;
        private readonly FunctionRegistry _functionRegistry;
        private readonly TriggerManager _triggerManager;
        private readonly SystemFactoryRegistry _systemFactoryRegistry;
        private readonly TriggerDecoratorRegistry _triggerDecoratorRegistry;
        private readonly ModExtensionHub _extensions;
        private readonly List<IMod> _loadedMods = new List<IMod>();
        private readonly List<ModLoadContext> _loadContexts = new List<ModLoadContext>();
        private readonly Dictionary<string, Assembly> _sharedAssemblies = new Dictionary<string, Assembly>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _modDirectories = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> _processSharedAssemblyNames = new HashSet<string>(StringComparer.Ordinal);
        private ModLoadContext? _activePlanLoadContext;
        private ISystemRegistrar _systems = UnavailableSystemRegistrar.Instance;
        private IRegistrySetView _registries = UnavailableRegistrySetView.Instance;

        public IMapManager MapManager { get; set; }
        public List<string> LoadedModIds { get; private set; } = new List<string>();
        public IReadOnlyList<Assembly> LoadedAssemblies
        {
            get
            {
                var assemblies = new List<Assembly>();
                var seen = new HashSet<Assembly>();

                foreach (Assembly assembly in _sharedAssemblies.Values)
                {
                    AddLoadedAssembly(assembly, assemblies, seen);
                }

                foreach (ModLoadContext context in _loadContexts)
                {
                    foreach (Assembly assembly in context.Assemblies)
                    {
                        AddLoadedAssembly(assembly, assemblies, seen);
                    }
                }

                return assemblies
                    .OrderBy(assembly => assembly.GetName().Name, StringComparer.Ordinal)
                    .ThenBy(assembly => assembly.FullName, StringComparer.Ordinal)
                    .ToArray();
            }
        }

        public ModLoader(IVirtualFileSystem vfs, FunctionRegistry fr, TriggerManager tm,
            SystemFactoryRegistry sfr = null, TriggerDecoratorRegistry tdr = null, ModExtensionHub extensions = null)
        {
            _vfs = vfs;
            _functionRegistry = fr;
            _triggerManager = tm;
            _systemFactoryRegistry = sfr ?? new SystemFactoryRegistry();
            _triggerDecoratorRegistry = tdr ?? new TriggerDecoratorRegistry();
            _extensions = extensions ?? new ModExtensionHub();
        }

        public SystemFactoryRegistry SystemFactoryRegistry => _systemFactoryRegistry;
        public TriggerDecoratorRegistry TriggerDecoratorRegistry => _triggerDecoratorRegistry;
        internal ModExtensionHub Extensions => _extensions;

        public void BindHostPorts(ISystemRegistrar systems, IRegistrySetView registries)
        {
            _systems = systems ?? throw new ArgumentNullException(nameof(systems));
            _registries = registries ?? throw new ArgumentNullException(nameof(registries));
        }

        public void LoadMods(string modsRootPath)
        {
            if (!Directory.Exists(modsRootPath))
            {
                throw new DirectoryNotFoundException($"Mods directory not found: {Path.GetFullPath(modsRootPath)}");
            }

            var directories = ModDiscovery.DiscoverModDirectories(modsRootPath);
            LoadMods(directories);
        }

        public void LoadMods(IEnumerable<string> modDirectories)
        {
            var scannedMods = ScanModDirectories(modDirectories);
            var modNodes = scannedMods
                .Select(item => new DependencyResolver.ModNode
                {
                    Manifest = item.Manifest,
                    CreationIndex = item.CreationIndex
                })
                .ToList();

            var resolver = new DependencyResolver();
            List<ModManifest> sortedManifests;
            try
            {
                sortedManifests = resolver.Resolve(modNodes);
            }
            catch (Exception ex)
            {
                Log.Error(in LogChannels.ModLoader, $"Dependency resolution failed: {ex.Message}");
                throw;
            }

            Log.Info(in LogChannels.ModLoader, "Mod Load Order:");
            foreach(var m in sortedManifests)
            {
                Log.Info(in LogChannels.ModLoader, $"- {m.Name} (P:{m.Priority})");
            }

            var scannedByName = scannedMods.ToDictionary(item => item.Manifest.Name, StringComparer.Ordinal);
            var orderedPlan = sortedManifests
                .Select(manifest => new ResolvedModLoadEntry(manifest.Name, scannedByName[manifest.Name].Directory))
                .ToList();
            LoadResolvedPlan(orderedPlan);
        }

        public void LoadResolvedPlan(IReadOnlyList<ResolvedModLoadEntry> orderedMods)
        {
            if (orderedMods == null)
            {
                throw new ArgumentNullException(nameof(orderedMods));
            }

            ResetLoadedState();

            var validatedMods = new List<ValidatedResolvedMod>(orderedMods.Count);
            var orderById = new Dictionary<string, int>(StringComparer.Ordinal);
            var versionById = new Dictionary<string, DependencyResolver.SemVersion>(StringComparer.Ordinal);
            var seenRoots = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < orderedMods.Count; i++)
            {
                var entry = orderedMods[i];
                if (string.IsNullOrWhiteSpace(entry.Id))
                {
                    throw new InvalidOperationException($"Resolved mod plan entry {i} has an empty id.");
                }

                if (string.IsNullOrWhiteSpace(entry.RootPath))
                {
                    throw new InvalidOperationException($"Resolved mod plan entry '{entry.Id}' has an empty root path.");
                }

                var modDir = Path.GetFullPath(entry.RootPath);
                if (!seenRoots.Add(modDir))
                {
                    throw new InvalidOperationException($"Duplicate mod root in resolved mod plan: '{modDir}'.");
                }

                if (!Directory.Exists(modDir))
                {
                    throw new DirectoryNotFoundException($"Mod directory not found: {modDir}");
                }

                if (!TryGetExactChildFile(modDir, "mod.json", out var manifestPath))
                {
                    throw new FileNotFoundException($"mod.json not found in mod directory: {modDir}");
                }

                var manifest = ModManifestJson.ParseStrict(File.ReadAllText(manifestPath), manifestPath)
                    ?? throw new InvalidOperationException($"Failed to parse mod manifest from '{manifestPath}'.");
                if (!string.Equals(manifest.Name, entry.Id, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Resolved mod plan mismatch: entry id '{entry.Id}' does not match manifest '{manifest.Name}' at '{modDir}'.");
                }

                if (orderById.ContainsKey(manifest.Name))
                {
                    throw new InvalidOperationException($"Duplicate mod id in resolved mod plan: '{manifest.Name}'.");
                }

                if (!DependencyResolver.SemVersion.TryParse(manifest.Version, out var version))
                {
                    throw new InvalidOperationException($"Invalid version '{manifest.Version}' for mod '{manifest.Name}'.");
                }

                validatedMods.Add(new ValidatedResolvedMod(modDir, manifest, version));
                orderById[manifest.Name] = i;
                versionById[manifest.Name] = version;
            }

            ValidateResolvedPlanDependencies(validatedMods.Select(item => item.Manifest).ToList(), orderById, versionById);

            Log.Info(in LogChannels.ModLoader, "Resolved Mod Load Order (launcher graph):");
            foreach (var item in validatedMods)
            {
                Log.Info(in LogChannels.ModLoader, $"- {item.Manifest.Name} (P:{item.Manifest.Priority})");
            }

            foreach (var item in validatedMods)
            {
                _modDirectories[item.Manifest.Name] = item.ModDirectory;
                _vfs.Mount(item.Manifest.Name, item.ModDirectory);
            }

            ConfigureProcessSharedAssemblies(validatedMods.Select(item => item.Manifest));
            _activePlanLoadContext = new ModLoadContext(ResolveSharedAssembly, _processSharedAssemblyNames);
            _loadContexts.Add(_activePlanLoadContext);

            foreach (var item in validatedMods)
            {
                LoadModAssembly(item.Manifest);
                LoadedModIds.Add(item.Manifest.Name);
            }
        }

        private List<ScannedModDirectory> ScanModDirectories(IEnumerable<string> modDirectories)
        {
            var scanned = new List<ScannedModDirectory>();
            int scanIndex = 0;

            foreach (var dir in modDirectories)
            {
                if (!TryGetExactChildFile(dir, "mod.json", out var manifestPath))
                {
                    throw new FileNotFoundException($"mod.json not found in explicit mod directory: {Path.GetFullPath(dir)}");
                }

                try
                {
                    var json = File.ReadAllText(manifestPath);
                    var manifest = ModManifestJson.ParseStrict(json, manifestPath);
                    scanned.Add(new ScannedModDirectory(Path.GetFullPath(dir), manifest, scanIndex++));
                }
                catch (Exception ex)
                {
                    Log.Error(in LogChannels.ModLoader, $"Failed to load manifest from {dir}: {ex.Message}");
                    throw;
                }
            }

            return scanned;
        }

        private void ResetLoadedState()
        {
            try
            {
                for (int i = _loadedMods.Count - 1; i >= 0; i--)
                {
                    var mod = _loadedMods[i];
                    try
                    {
                        mod.OnUnload();
                    }
                    catch (Exception ex)
                    {
                        Log.Error(in LogChannels.ModLoader, $"Mod unload failed for {mod.GetType().FullName}: {ex}");
                        throw;
                    }
                }
                _loadedMods.Clear();
                UnregisterModComponentAuthoring(LoadedModIds);

                foreach (var ctx in _loadContexts)
                {
                    try
                    {
                        ctx.Unload();
                    }
                    catch (Exception ex)
                    {
                        Log.Error(in LogChannels.ModLoader, $"Mod load context unload failed: {ex}");
                        throw;
                    }
                }
                _loadContexts.Clear();

                var staleMounts = new HashSet<string>(LoadedModIds, StringComparer.Ordinal);
                foreach (var id in _modDirectories.Keys)
                {
                    staleMounts.Add(id);
                }

                foreach (var id in staleMounts)
                {
                    _vfs.Unmount(id);
                }
            }
            finally
            {
                _loadedMods.Clear();
                _loadContexts.Clear();
                LoadedModIds.Clear();
                _modDirectories.Clear();
                _sharedAssemblies.Clear();
                _processSharedAssemblyNames.Clear();
                _activePlanLoadContext = null;
                _extensions.Reset();
            }
        }

        private void ConfigureProcessSharedAssemblies(IEnumerable<ModManifest> manifests)
        {
            _processSharedAssemblyNames.Clear();
            foreach (var manifest in manifests)
            {
                if (manifest.ProcessSharedAssemblies == null)
                {
                    continue;
                }

                foreach (var assemblyName in manifest.ProcessSharedAssemblies)
                {
                    if (!string.IsNullOrWhiteSpace(assemblyName))
                    {
                        _processSharedAssemblyNames.Add(assemblyName.Trim());
                    }
                }
            }
        }

        private static void UnregisterModComponentAuthoring(IEnumerable<string> modIds)
        {
            foreach (var modId in modIds)
            {
                ComponentRegistry.UnregisterSource(modId);
            }
        }

        private static void ValidateResolvedPlanDependencies(
            IReadOnlyList<ModManifest> orderedManifests,
            IReadOnlyDictionary<string, int> orderById,
            IReadOnlyDictionary<string, DependencyResolver.SemVersion> versionById)
        {
            for (int i = 0; i < orderedManifests.Count; i++)
            {
                var manifest = orderedManifests[i];
                foreach (var dependency in manifest.Dependencies)
                {
                    var dependencyId = dependency.Key;
                    if (!orderById.TryGetValue(dependencyId, out var dependencyIndex))
                    {
                        throw new InvalidOperationException(
                            $"Resolved mod plan missing dependency: mod '{manifest.Name}' requires '{dependencyId}'.");
                    }

                    if (!DependencyResolver.SemVersionRange.TryParse(dependency.Value, out var range))
                    {
                        throw new InvalidOperationException(
                            $"Invalid dependency version range '{dependency.Value}' for '{manifest.Name}' -> '{dependencyId}'.");
                    }

                    if (!range.Matches(versionById[dependencyId]))
                    {
                        throw new InvalidOperationException(
                            $"Version mismatch: mod '{manifest.Name}' requires '{dependencyId}' {dependency.Value} but found {versionById[dependencyId]}.");
                    }

                    if (dependencyIndex >= i)
                    {
                        throw new InvalidOperationException(
                            $"Launch plan order is invalid: Mod '{manifest.Name}' depends on '{dependencyId}', but the graph ordered '{dependencyId}' after '{manifest.Name}'.");
                    }
                }
            }
        }

        private void LoadModAssembly(ModManifest manifest)
        {
            Log.Dbg(in LogChannels.ModLoader, $"Entering LoadModAssembly for {manifest.Name}");

            if (!_modDirectories.TryGetValue(manifest.Name, out var modDir))
                return;

            // Look for DLL
            var hasDll = TryResolveMainAssemblyPath(manifest, modDir, out var dllPath);

            if (!hasDll)
            {
                if (string.IsNullOrWhiteSpace(manifest.Main))
                {
                    Log.Info(in LogChannels.ModLoader, $"Skip code load for {manifest.Name}: manifest has no 'main' (asset-only mod).");
                    return;
                }

                var matches = FindAllBuiltDllCandidates(modDir, manifest.Name);
                if (matches.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"Mod '{manifest.Name}' has built DLL candidates, but none match the expected path.\n" +
                        $"Expected: {dllPath}\n" +
                        $"Found:\n- {string.Join("\n- ", matches)}");
                }

                throw new FileNotFoundException(
                    $"Mod '{manifest.Name}' declares main assembly but the DLL was not found.",
                    dllPath);
            }

            try
                {
                    dllPath = Path.GetFullPath(dllPath);
                    Assembly assembly;
                    if (TryResolvePreloadedAssembly(manifest.Name, out Assembly? preloadedAssembly))
                    {
                        assembly = preloadedAssembly;
                        CacheSharedAssembly(assembly);
                        Log.Info(in LogChannels.ModLoader, $"Reusing preloaded assembly for {manifest.Name}: {assembly.Location}");
                    }
                    else
                    {
                        Log.Info(in LogChannels.ModLoader, $"Loading DLL for {manifest.Name} at {dllPath}");
                        var loadContext = _activePlanLoadContext ?? new ModLoadContext(ResolveSharedAssembly, _processSharedAssemblyNames);
                        if (_activePlanLoadContext == null)
                        {
                            _activePlanLoadContext = loadContext;
                            _loadContexts.Add(loadContext);
                        }

                        loadContext.RegisterMainAssemblyPath(dllPath);
                        assembly = loadContext.LoadMainAssembly(dllPath);
                        CacheSharedAssembly(assembly);
                    }

                    CacheSharedAssembly(assembly);

                    Type[] allTypes;
                    try
                    {
                        allTypes = assembly.GetTypes();
                    }
                    catch (ReflectionTypeLoadException rtle)
                    {
                        allTypes = rtle.Types.Where(t => t != null).ToArray();
                        Log.Warn(in LogChannels.ModLoader, $"Type load failures while scanning {manifest.Name}: {rtle.LoaderExceptions?.Length ?? 0}");
                        if (rtle.LoaderExceptions != null)
                        {
                            foreach (var le in rtle.LoaderExceptions)
                            {
                                Log.Warn(in LogChannels.ModLoader, $"  LoaderException: {le}");
                            }
                        }
                    }

                    Log.Info(in LogChannels.ModLoader, $"Scanning {allTypes.Length} types in assembly...");
                    
                    // Scan for IMod
                    var modType = allTypes.FirstOrDefault(t => typeof(IMod).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);
                    if (modType != null)
                    {
                        if (Activator.CreateInstance(modType) is not IMod modInstance)
                        {
                            throw new InvalidOperationException(
                                $"Mod entry type '{modType.FullName}' for '{manifest.Name}' could not be instantiated.");
                        }
                        Log.Info(in LogChannels.ModLoader, $"Instantiated entry for {manifest.Name}. Calling OnLoad...");
                        var context = new ModContext(manifest.Name, _vfs, _functionRegistry, _triggerManager, _systemFactoryRegistry, _triggerDecoratorRegistry, _extensions);
                        context.BindHostPorts(_systems, _registries);
                        modInstance.OnLoad(context);
                        Log.Info(in LogChannels.ModLoader, $"{manifest.Name} OnLoad completed.");
                        _loadedMods.Add(modInstance);
                        Log.Info(in LogChannels.ModLoader, $"Loaded {manifest.Name}");
                        
                        // Fire ModLoaded event (will be implemented in future TriggerManager)
                        // _triggerManager.FireEvent(GameEvents.ModLoaded, ...); 
                    }
                    else
                    {
                        Log.Info(in LogChannels.ModLoader, $"No IMod implementation found in {dllPath}");
                    }
                    
                    // Scan for MapDefinition
                    if (MapManager != null)
                    {
                        var mapTypes = allTypes.Where(t => typeof(MapDefinition).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);
                        foreach (var mapType in mapTypes)
                        {
                            try 
                            {
                                var mapDef = (MapDefinition)Activator.CreateInstance(mapType);
                                MapManager.RegisterMap(mapDef);
                            }
                            catch (Exception ex)
                            {
                                Log.Error(in LogChannels.ModLoader, $"Failed to register map {mapType.Name}: {ex}");
                            }
                        }
                    }
            }
            catch (Exception ex)
            {
                ComponentRegistry.UnregisterSource(manifest.Name);
                throw new InvalidOperationException($"Failed to load code mod '{manifest.Name}'.", ex);
            }
        }

        private static bool TryResolvePreloadedAssembly(string assemblySimpleName, out Assembly? assembly)
        {
            assembly = null;
            if (string.IsNullOrWhiteSpace(assemblySimpleName))
            {
                return false;
            }

            assembly = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(candidate =>
                string.Equals(candidate.GetName().Name, assemblySimpleName, StringComparison.Ordinal));

            return assembly != null;
        }

        private static void AddLoadedAssembly(
            Assembly? assembly,
            List<Assembly> assemblies,
            HashSet<Assembly> seen)
        {
            if (assembly == null || assembly.IsDynamic || !seen.Add(assembly))
            {
                return;
            }

            assemblies.Add(assembly);
        }

        private Assembly ResolveSharedAssembly(AssemblyName assemblyName)
        {
            var name = assemblyName?.Name;
            if (string.IsNullOrWhiteSpace(name)) return null;
            return _sharedAssemblies.TryGetValue(name, out var assembly) ? assembly : null;
        }

        private void CacheSharedAssembly(Assembly assembly)
        {
            var name = assembly?.GetName()?.Name;
            if (string.IsNullOrWhiteSpace(name)) return;
            _sharedAssemblies[name] = assembly;
        }

        private static bool TryResolveMainAssemblyPath(ModManifest manifest, string modDir, out string dllPath)
        {
            var modDirFull = Path.GetFullPath(modDir);
            string relative = manifest.Main;
            if (!string.IsNullOrWhiteSpace(relative))
            {
                if (Path.IsPathRooted(relative))
                {
                    throw new Exception($"Invalid mod.json ('main' must be relative): {manifest.Name}");
                }

                var primary = Path.GetFullPath(Path.Combine(modDirFull, relative));
                if (!primary.StartsWith(modDirFull, StringComparison.Ordinal))
                {
                    throw new Exception($"Invalid mod.json ('main' escapes mod directory): {manifest.Name}");
                }

                dllPath = primary;
                return File.Exists(primary);
            }

            dllPath = "(no main)";
            return false;
        }

        private static bool TryGetExactChildFile(string directory, string fileName, out string fullPath)
        {
            foreach (var candidate in Directory.EnumerateFiles(directory))
            {
                if (string.Equals(Path.GetFileName(candidate), fileName, StringComparison.Ordinal))
                {
                    fullPath = Path.GetFullPath(candidate);
                    return true;
                }
            }

            fullPath = string.Empty;
            return false;
        }

        private static List<string> FindAllBuiltDllCandidates(string modDir, string modName)
        {
            var modDirFull = Path.GetFullPath(modDir);
            var defaultName = $"{modName}.dll";
            var results = new List<string>(16);
            try
            {
                var binDir = Path.Combine(modDirFull, "bin");
                if (!Directory.Exists(binDir)) return results;
                foreach (var p in Directory.EnumerateFiles(binDir, defaultName, SearchOption.AllDirectories))
                {
                    var full = Path.GetFullPath(p);
                    if (!full.StartsWith(modDirFull, StringComparison.Ordinal)) continue;
                    results.Add(full);
                }
            }
            catch (Exception ex)
            {
                throw new IOException($"Failed to enumerate built DLL candidates under '{Path.Combine(modDirFull, "bin")}'.", ex);
            }

            results.Sort(StringComparer.Ordinal);
            return results;
        }

        public void UnloadAll()
        {
            try
            {
                for (int i = _loadedMods.Count - 1; i >= 0; i--)
                {
                    var mod = _loadedMods[i];
                    try
                    {
                        mod.OnUnload();
                    }
                    catch (Exception ex)
                    {
                        Log.Error(in LogChannels.ModLoader, $"Mod unload failed for {mod.GetType().FullName}: {ex}");
                        throw;
                    }
                }
                _loadedMods.Clear();
                UnregisterModComponentAuthoring(LoadedModIds);

                foreach (var ctx in _loadContexts)
                {
                    try
                    {
                        ctx.Unload();
                    }
                    catch (Exception ex)
                    {
                        Log.Error(in LogChannels.ModLoader, $"Mod load context unload failed: {ex}");
                        throw;
                    }
                }
                _loadContexts.Clear();

                foreach (var id in LoadedModIds)
                {
                    _vfs.Unmount(id);
                }
            }
            finally
            {
                _loadedMods.Clear();
                _loadContexts.Clear();
                LoadedModIds.Clear();
                _modDirectories.Clear();
                _sharedAssemblies.Clear();
                _processSharedAssemblyNames.Clear();
                _activePlanLoadContext = null;
                _extensions.Reset();
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        private sealed record ValidatedResolvedMod(
            string ModDirectory,
            ModManifest Manifest,
            DependencyResolver.SemVersion Version);

        private sealed record ScannedModDirectory(string Directory, ModManifest Manifest, int CreationIndex);
    }
}
