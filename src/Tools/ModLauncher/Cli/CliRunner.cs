using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Ludots.Core.Modding;
using Ludots.ModLauncher.Config;
using Ludots.ModLauncher.ModSdk;

namespace Ludots.ModLauncher.Cli
{
    public static class CliRunner
    {
        public static async Task<int> Run(string[] args)
        {
            if (args.Length < 2) return 2;

            var rootDir = FindRootDir();
            var parsed = CliArgs.Parse(args.Skip(1).ToArray());

            var configService = new LauncherConfigService(parsed.ConfigPath);
            var cfg = configService.LoadOrDefault();

            var context = new CliContext(rootDir, cfg);

            if (parsed.Primary == "sdk" && parsed.Secondary == "export")
            {
                ModSdkExporter.Export(rootDir, context.Log);
                return 0;
            }

            if (parsed.Primary == "mods" && parsed.Secondary == "build")
            {
                var mods = context.ResolveActiveMods(parsed.PresetId, parsed.ModNames);
                foreach (var m in mods)
                {
                    context.EnsureModProject(m);
                    context.BuildModRelease(m);
                }
                return 0;
            }

            if (parsed.Primary == "app" && parsed.Secondary == "build")
            {
                context.BuildRaylibRelease();
                return 0;
            }

            if (parsed.Primary == "gamejson" && parsed.Secondary == "write")
            {
                var mods = context.ResolveActiveMods(parsed.PresetId, parsed.ModNames);
                context.WriteGameJson(mods);
                return 0;
            }

            if (parsed.Primary == "run")
            {
                context.RunRaylib();
                return 0;
            }

            return 2;
        }

        private static string FindRootDir()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "assets")))
            {
                dir = dir.Parent;
            }

            if (dir == null) throw new DirectoryNotFoundException("Could not locate Ludots root (missing assets directory).");
            return dir.FullName;
        }

        private sealed class CliContext
        {
            private readonly string _rootDir;
            private readonly string _assetsDir;
            private readonly string _modsDir;
            private readonly string _gameExePath;
            private readonly string _gameJsonPath;
            private readonly ModLauncherConfig _config;

            public CliContext(string rootDir, ModLauncherConfig config)
            {
                _rootDir = rootDir;
                _assetsDir = Path.Combine(_rootDir, "assets");
                _modsDir = Path.Combine(_rootDir, "src", "Mods");
                _gameExePath = Path.Combine(_rootDir, "src", "Apps", "Raylib", "Ludots.App.Raylib", "bin", "Release", "net8.0", "Ludots.App.Raylib.exe");
                _gameJsonPath = Path.Combine(Path.GetDirectoryName(_gameExePath) ?? _rootDir, "game.json");
                _config = config ?? new ModLauncherConfig();
            }

            public void Log(string msg)
            {
                Console.WriteLine(msg);
            }

            public List<ModInfo> ResolveActiveMods(string? presetId, List<string> modNamesOrPaths)
            {
                var all = ScanMods();

                if (modNamesOrPaths.Count > 0)
                {
                    var set = new HashSet<string>(modNamesOrPaths, StringComparer.OrdinalIgnoreCase);
                    var picked = all.Where(m => set.Contains(m.Name) || set.Contains(m.FullPath)).ToList();
                    if (picked.Count != modNamesOrPaths.Count)
                    {
                        var missing = modNamesOrPaths.Where(x => !picked.Any(m => string.Equals(m.Name, x, StringComparison.OrdinalIgnoreCase) || string.Equals(m.FullPath, x, StringComparison.OrdinalIgnoreCase))).ToList();
                        throw new InvalidOperationException($"Unknown mods: {string.Join(", ", missing)}");
                    }
                    return picked;
                }

                var effectivePresetId = string.IsNullOrWhiteSpace(presetId) ? _config.SelectedPresetId : presetId;
                if (string.IsNullOrWhiteSpace(effectivePresetId)) throw new InvalidOperationException("PresetId is required (no --preset and no SelectedPresetId).");
                if (!_config.Presets.TryGetValue(effectivePresetId, out var preset)) throw new InvalidOperationException($"Preset not found: {effectivePresetId}");

                var byName = all.ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);
                var active = new List<ModInfo>();
                foreach (var n in preset.ActiveModNames)
                {
                    if (!byName.TryGetValue(n, out var m)) throw new InvalidOperationException($"Preset refers to missing mod: {n}");
                    active.Add(m);
                }
                return active;
            }

            private List<ModInfo> ScanMods()
            {
                var directoriesToScan = new List<string>();
                if (Directory.Exists(_modsDir)) directoriesToScan.Add(_modsDir);
                directoriesToScan.AddRange(_config.ExtraModDirectories ?? new List<string>());

                var results = new List<ModInfo>();
                var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                void TryLoad(string dir)
                {
                    var modJson = Path.Combine(dir, "mod.json");
                    if (!File.Exists(modJson)) return;

                    var manifest = ModManifestJson.ParseStrict(File.ReadAllText(modJson), modJson);
                    var name = manifest.Name;
                    if (seenNames.Contains(name)) return;

                    var full = Path.GetFullPath(dir);
                    results.Add(new ModInfo(name, full));
                    seenNames.Add(name);
                }

                foreach (var root in directoriesToScan)
                {
                    if (!Directory.Exists(root)) continue;
                    TryLoad(root);
                    foreach (var d in Directory.GetDirectories(root)) TryLoad(d);
                }

                return results;
            }

            public void EnsureModProject(ModInfo mod)
            {
                var csproj = FindModProject(mod.FullPath);
                if (!string.IsNullOrWhiteSpace(csproj)) return;

                var sdkPropsPath = Path.Combine(_rootDir, "assets", "ModSdk", "ModSdk.props");
                if (!File.Exists(sdkPropsPath)) throw new FileNotFoundException($"Mod SDK props not found: {sdkPropsPath}");
                var relSdkProps = Path.GetRelativePath(mod.FullPath, sdkPropsPath);

                var csprojPath = Path.Combine(mod.FullPath, $"{mod.Name}.csproj");
                var content = $@"<Project Sdk=""Microsoft.NET.Sdk"">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <OutputPath>bin</OutputPath>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
  </PropertyGroup>

  <Import Project=""{relSdkProps}"" />

</Project>";

                File.WriteAllText(csprojPath, content);
                Log($"Created csproj: {csprojPath}");
            }

            public void BuildModRelease(ModInfo mod)
            {
                var csproj = FindModProject(mod.FullPath);
                if (string.IsNullOrWhiteSpace(csproj)) throw new InvalidOperationException($"No csproj found for mod: {mod.FullPath}");

                Log($"Building mod (Release): {mod.Name}");
                int exit = RunDotnet($"build \"{csproj}\" /p:ProduceReferenceAssembly=true -c Release");
                if (exit != 0) throw new InvalidOperationException($"Mod build failed: {mod.Name} (exit={exit})");
            }

            public void BuildRaylibRelease()
            {
                var csproj = Path.Combine(_rootDir, "src", "Apps", "Raylib", "Ludots.App.Raylib", "Ludots.App.Raylib.csproj");
                if (!File.Exists(csproj)) throw new FileNotFoundException($"Raylib app csproj not found: {csproj}");
                Log("Building Raylib app (Release)");
                int exit = RunDotnet($"build \"{csproj}\" -c Release");
                if (exit != 0) throw new InvalidOperationException($"Raylib app build failed (exit={exit})");
            }

            public void WriteGameJson(List<ModInfo> mods)
            {
                // Ensure LudotsCoreMod is always first (required for constants)
                var coreModPath = Path.Combine(_modsDir, "LudotsCoreMod");
                var orderedMods = new List<string>();
                
                if (Directory.Exists(coreModPath))
                {
                    orderedMods.Add(coreModPath);
                }
                
                foreach (var m in mods)
                {
                    if (!m.Name.Equals("LudotsCoreMod", StringComparison.OrdinalIgnoreCase))
                    {
                        orderedMods.Add(m.FullPath);
                    }
                }
                
                // Calculate relative paths from game.json location
                var gameJsonDir = Path.GetDirectoryName(_gameJsonPath) ?? _rootDir;
                var modPaths = orderedMods.Select(p => Path.GetRelativePath(gameJsonDir, p).Replace('\\', '/')).ToArray();
                var json = JsonSerializer.Serialize(new { ModPaths = modPaths }, new JsonSerializerOptions { WriteIndented = true });

                Directory.CreateDirectory(gameJsonDir);
                File.WriteAllText(_gameJsonPath, json);
                Log($"Wrote game.json: {_gameJsonPath}");
            }

            public void RunRaylib()
            {
                if (!File.Exists(_gameExePath)) throw new FileNotFoundException($"Game exe not found: {_gameExePath}");
                Log($"Starting: {_gameExePath}");
                Process.Start(new ProcessStartInfo
                {
                    FileName = _gameExePath,
                    WorkingDirectory = Path.GetDirectoryName(_gameExePath) ?? _rootDir,
                    UseShellExecute = true
                });
            }

            private int RunDotnet(string args)
            {
                var startInfo = new ProcessStartInfo("dotnet", args)
                {
                    WorkingDirectory = _rootDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                };

                using var p = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start dotnet process.");
                _ = p.StandardOutput.ReadToEnd();
                _ = p.StandardError.ReadToEnd();
                p.WaitForExit();
                return p.ExitCode;
            }

            private static string? FindModProject(string fullModPath)
            {
                var matches = Directory.GetFiles(fullModPath, "*.csproj", SearchOption.TopDirectoryOnly);
                return matches.Length == 0 ? null : matches[0];
            }
        }

        private readonly record struct ModInfo(string Name, string FullPath);
    }
}

