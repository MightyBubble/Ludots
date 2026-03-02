using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Ludots.Core.Diagnostics;
using Ludots.Core.Engine;
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
        private readonly List<IMod> _loadedMods = new List<IMod>();
        private readonly List<ModLoadContext> _loadContexts = new List<ModLoadContext>();
        private readonly Dictionary<string, Assembly> _sharedAssemblies = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _modDirectories = new Dictionary<string, string>();

        public IMapManager MapManager { get; set; }
        public List<string> LoadedModIds { get; private set; } = new List<string>();

        public ModLoader(IVirtualFileSystem vfs, FunctionRegistry fr, TriggerManager tm,
            SystemFactoryRegistry sfr = null, TriggerDecoratorRegistry tdr = null)
        {
            _vfs = vfs;
            _functionRegistry = fr;
            _triggerManager = tm;
            _systemFactoryRegistry = sfr ?? new SystemFactoryRegistry();
            _triggerDecoratorRegistry = tdr ?? new TriggerDecoratorRegistry();
        }

        public SystemFactoryRegistry SystemFactoryRegistry => _systemFactoryRegistry;
        public TriggerDecoratorRegistry TriggerDecoratorRegistry => _triggerDecoratorRegistry;

        public void LoadMods(string modsRootPath)
        {
            if (!Directory.Exists(modsRootPath))
            {
                Log.Warn(in LogChannels.ModLoader, $"Mods directory not found: {modsRootPath}");
                return;
            }

            var directories = Directory.GetDirectories(modsRootPath);
            LoadMods(directories);
        }

        public void LoadMods(IEnumerable<string> modDirectories)
        {
            // Unmount previous mod mounts before re-scan.
            var staleMounts = new HashSet<string>(LoadedModIds, StringComparer.OrdinalIgnoreCase);
            foreach (var id in _modDirectories.Keys) staleMounts.Add(id);
            foreach (var id in staleMounts) _vfs.Unmount(id);

            LoadedModIds.Clear();
            _modDirectories.Clear();
            _sharedAssemblies.Clear();

            // 1. Scan for mod.json
            var modNodes = new List<DependencyResolver.ModNode>();
            int scanIndex = 0;

            foreach (var dir in modDirectories)
            {
                var manifestPath = Path.Combine(dir, "mod.json");
                if (File.Exists(manifestPath))
                {
                    try
                    {
                        var json = File.ReadAllText(manifestPath);
                        var manifest = ModManifestJson.ParseStrict(json, manifestPath);
                        
                        if (manifest != null)
                        {
                            modNodes.Add(new DependencyResolver.ModNode
                            {
                                Manifest = manifest,
                                CreationIndex = scanIndex++
                            });
                            
                            // Store path for later DLL loading
                            _modDirectories[manifest.Name] = dir;

                            // Mount resources
                            _vfs.Mount(manifest.Name, dir);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(in LogChannels.ModLoader, $"Failed to load manifest from {dir}: {ex.Message}");
                    }
                }
            }

            // 2. Resolve Dependencies
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

            // 3. Load Assemblies and Init
            foreach (var manifest in sortedManifests)
            {
                LoadModAssembly(manifest);
                LoadedModIds.Add(manifest.Name);
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
                var matches = FindAllBuiltDllCandidates(modDir, manifest.Name);
                if (matches.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"Mod '{manifest.Name}' has built DLL candidates, but none match the expected path.\n" +
                        $"Expected: {dllPath}\n" +
                        $"Found:\n- {string.Join("\n- ", matches)}");
                }

                Log.Warn(in LogChannels.ModLoader, $"No DLL found for {manifest.Name} (asset-only mod?): expected {dllPath}");
                return;
            }

            try
                {
                    dllPath = Path.GetFullPath(dllPath);
                    Log.Info(in LogChannels.ModLoader, $"Loading DLL for {manifest.Name} at {dllPath}");
                    var loadContext = new ModLoadContext(dllPath, ResolveSharedAssembly);
                    var assembly = loadContext.LoadFromAssemblyPath(dllPath);
                    _loadContexts.Add(loadContext);
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
                        var modInstance = (IMod)Activator.CreateInstance(modType);
                        Log.Info(in LogChannels.ModLoader, $"Instantiated entry for {manifest.Name}. Calling OnLoad...");
                        var context = new ModContext(manifest.Name, _vfs, _functionRegistry, _triggerManager, _systemFactoryRegistry, _triggerDecoratorRegistry);
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
                Log.Error(in LogChannels.ModLoader, $"Failed to load DLL for {manifest.Name}: {ex}");
            }
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
                if (!primary.StartsWith(modDirFull, StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception($"Invalid mod.json ('main' escapes mod directory): {manifest.Name}");
                }

                dllPath = primary;
                var debugFallback = TryResolveDebugFallback(modDirFull, relative);
                bool primaryExists = File.Exists(primary);
                bool debugExists = !string.IsNullOrWhiteSpace(debugFallback) && File.Exists(debugFallback);

#if DEBUG
                if (debugExists)
                {
                    dllPath = debugFallback;
                    return true;
                }
#endif

                if (primaryExists && debugExists)
                {
                    var primaryTime = File.GetLastWriteTimeUtc(primary);
                    var debugTime = File.GetLastWriteTimeUtc(debugFallback);
                    if (debugTime > primaryTime)
                    {
                        dllPath = debugFallback;
                    }
                    return true;
                }

                if (primaryExists) return true;

                if (debugExists)
                {
                    dllPath = debugFallback;
                    return true;
                }

                return false;
            }

            var defaultName = $"{manifest.Name}.dll";
            var candidates = new List<string>(8)
            {
                Path.Combine(modDirFull, "bin", "Release", "net8.0", defaultName),
                Path.Combine(modDirFull, "bin", "Release", "net8.0-windows", defaultName),
                Path.Combine(modDirFull, "bin", "Debug", "net8.0", defaultName),
                Path.Combine(modDirFull, "bin", "Debug", "net8.0-windows", defaultName)
            };

            foreach (var c in candidates)
            {
                var full = Path.GetFullPath(c);
                if (!full.StartsWith(modDirFull, StringComparison.OrdinalIgnoreCase)) continue;
                if (File.Exists(full))
                {
                    dllPath = full;
                    return true;
                }
            }

            dllPath = candidates[0];
            return false;
        }

        private static string TryResolveDebugFallback(string modDirFull, string relativeMain)
        {
            if (string.IsNullOrWhiteSpace(relativeMain)) return null;
            var normalized = relativeMain.Replace('\\', '/');
            const string releaseToken = "bin/Release/";
            int idx = normalized.IndexOf(releaseToken, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;

            var debugRelative = normalized.Substring(0, idx) + "bin/Debug/" + normalized.Substring(idx + releaseToken.Length);
            var debugFull = Path.GetFullPath(Path.Combine(modDirFull, debugRelative.Replace('/', Path.DirectorySeparatorChar)));
            if (!debugFull.StartsWith(modDirFull, StringComparison.OrdinalIgnoreCase)) return null;
            return debugFull;
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
                    if (!full.StartsWith(modDirFull, StringComparison.OrdinalIgnoreCase)) continue;
                    results.Add(full);
                }
            }
            catch
            {
            }

            results.Sort(StringComparer.OrdinalIgnoreCase);
            return results;
        }

        public void UnloadAll()
        {
            foreach(var mod in _loadedMods)
            {
                try { mod.OnUnload(); } catch { }
            }
            _loadedMods.Clear();

            foreach (var ctx in _loadContexts)
            {
                try { ctx.Unload(); } catch { }
            }
            _loadContexts.Clear();

            foreach (var id in LoadedModIds)
            {
                _vfs.Unmount(id);
            }

            LoadedModIds.Clear();
            _modDirectories.Clear();
            _sharedAssemblies.Clear();

            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }
}
