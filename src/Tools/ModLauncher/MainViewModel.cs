using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using Ludots.ModLauncher.Config;

namespace Ludots.ModLauncher
{
    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _statusMessage = "Ready";

        [ObservableProperty]
        private double _dependencyGraphWidth = 260;

        [ObservableProperty]
        private double _dependencyGraphHeight = 300;

        public ObservableCollection<string> AvailableMaps { get; } = new ObservableCollection<string>();

        public ObservableCollection<ModViewModel> Mods { get; } = new ObservableCollection<ModViewModel>();
        public ObservableCollection<DependencyGraphNodeViewModel> DependencyGraphNodes { get; } = new ObservableCollection<DependencyGraphNodeViewModel>();
        public ObservableCollection<DependencyGraphEdgeViewModel> DependencyGraphEdges { get; } = new ObservableCollection<DependencyGraphEdgeViewModel>();
        public ObservableCollection<ModPreset> Presets { get; } = new ObservableCollection<ModPreset>();

        [ObservableProperty]
        private ModPreset? _selectedPreset;

        [ObservableProperty]
        private bool _isSelectedPresetDirty;

        [ObservableProperty]
        private LauncherViewMode _viewMode = LauncherViewMode.Cards;

        private string _rootDir;
        private string _assetsDir;
        private string _modsDir;
        private string _gameJsonPath;
        private string _gameExePath;
        private List<string> _extraModDirs = new List<string>();
        private readonly LauncherConfigService _configService;
        private ModLauncherConfig _launcherConfig;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsNotBuilding))]
        private bool _isBuilding;

        [ObservableProperty]
        private string _buildLog = "";

        public bool IsNotBuilding => !IsBuilding;

        private bool _isUpdatingActivation;

        public MainViewModel()
        {
            InitializePaths();
            _configService = new LauncherConfigService();
            _launcherConfig = _configService.LoadOrDefault();
            ApplyLauncherConfig();
            LoadPresetsFromConfig();
            LoadMods();
            ApplySelectedPresetOrDefault();
        }

        private void ApplyLauncherConfig()
        {
            _extraModDirs = _launcherConfig.ExtraModDirectories?
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => Path.GetFullPath(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();

            ViewMode = _launcherConfig.ViewMode;
        }

        private void PersistLauncherConfig()
        {
            _launcherConfig.ExtraModDirectories = _extraModDirs.ToList();
            if (SelectedPreset != null) _launcherConfig.SelectedPresetId = SelectedPreset.Id;
            _launcherConfig.ViewMode = ViewMode;
            _launcherConfig.Presets = Presets.ToDictionary(p => p.Id, p => p, StringComparer.OrdinalIgnoreCase);
            _configService.Save(_launcherConfig);
        }

        partial void OnViewModeChanged(LauncherViewMode value)
        {
            _launcherConfig.ViewMode = value;
            PersistLauncherConfig();
        }

        [RelayCommand]
        private void SetCardView() => ViewMode = LauncherViewMode.Cards;

        [RelayCommand]
        private void SetListView() => ViewMode = LauncherViewMode.List;

        private void LoadPresetsFromConfig()
        {
            Presets.Clear();
            var presets = _launcherConfig.Presets?.Values?.ToList() ?? new List<ModPreset>();
            if (presets.Count == 0)
            {
                presets.Add(new ModPreset
                {
                    Id = "last",
                    Name = "Last Used",
                    IncludeDependencies = true
                });
            }

            foreach (var p in presets.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(p.Id)) continue;
                if (string.IsNullOrWhiteSpace(p.Name)) p.Name = p.Id;
                Presets.Add(p);
            }
        }

        partial void OnSelectedPresetChanged(ModPreset? value)
        {
            if (value == null) return;
            _launcherConfig.SelectedPresetId = value.Id;
            PersistLauncherConfig();
            if (value.Id.Equals("last", StringComparison.OrdinalIgnoreCase) && (value.ActiveModNames == null || value.ActiveModNames.Count == 0))
            {
                value.ActiveModNames = Mods.Where(m => m.IsActive).Select(m => m.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                IsSelectedPresetDirty = false;
                PersistLauncherConfig();
                return;
            }

            ApplyPreset(value);
        }

        private void ApplySelectedPresetOrDefault()
        {
            var id = _launcherConfig.SelectedPresetId;
            var preset = Presets.FirstOrDefault(p => p.Id.Equals(id ?? "", StringComparison.OrdinalIgnoreCase));
            preset ??= Presets.FirstOrDefault(p => p.Id.Equals("last", StringComparison.OrdinalIgnoreCase)) ?? Presets.FirstOrDefault();
            if (preset == null) return;
            SelectedPreset = preset;
        }

        private void OnModSelectionChanged()
        {
            if (SelectedPreset == null) return;
            if (SelectedPreset.Id.Equals("last", StringComparison.OrdinalIgnoreCase))
            {
                SelectedPreset.ActiveModNames = Mods.Where(m => m.IsActive).Select(m => m.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                IsSelectedPresetDirty = false;
                PersistLauncherConfig();
                return;
            }

            IsSelectedPresetDirty = true;
        }

        private void ApplyPreset(ModPreset preset)
        {
            _isUpdatingActivation = true;
            try
            {
                var desired = new HashSet<string>(preset.ActiveModNames ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < Mods.Count; i++)
                {
                    var m = Mods[i];
                    m.SetIsActiveFromParent(desired.Contains(m.Name));
                }

                if (preset.IncludeDependencies)
                {
                    ValidateDependencies();
                }
                else
                {
                    RebuildDependencyGraph();
                }

                IsSelectedPresetDirty = false;
            }
            finally
            {
                _isUpdatingActivation = false;
            }
        }

        [RelayCommand]
        private void SavePreset()
        {
            if (SelectedPreset == null) return;
            SelectedPreset.ActiveModNames = Mods.Where(m => m.IsActive).Select(m => m.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            IsSelectedPresetDirty = false;
            PersistLauncherConfig();
        }

        [RelayCommand]
        private void DeletePreset()
        {
            if (SelectedPreset == null) return;
            if (SelectedPreset.Id.Equals("last", StringComparison.OrdinalIgnoreCase)) return;
            var result = MessageBox.Show($"Delete preset '{SelectedPreset.Name}'?", "Delete Preset", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            var id = SelectedPreset.Id;
            var next = Presets.FirstOrDefault(p => !p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            Presets.Remove(SelectedPreset);
            if (next != null) SelectedPreset = next;
            IsSelectedPresetDirty = false;
            PersistLauncherConfig();
        }

        [RelayCommand]
        private void SavePresetAs()
        {
            var dialog = new TextPromptWindow("Save Playset As", "Playset name:");
            dialog.Owner = Application.Current.MainWindow;
            if (dialog.ShowDialog() != true) return;
            var name = dialog.Value?.Trim();
            if (string.IsNullOrWhiteSpace(name)) return;

            var id = Guid.NewGuid().ToString("N");
            var preset = new ModPreset
            {
                Id = id,
                Name = name,
                IncludeDependencies = true,
                ActiveModNames = Mods.Where(m => m.IsActive).Select(m => m.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            };
            Presets.Add(preset);
            SelectedPreset = preset;
            IsSelectedPresetDirty = false;
            PersistLauncherConfig();
        }

        [RelayCommand]
        private async System.Threading.Tasks.Task BuildActiveMods()
        {
            if (IsBuilding) return;

            IsBuilding = true;
            StatusMessage = "Building active mods...";
            BuildLog = "Building active mods...\n";

            await System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var orderedMods = ResolveBuildOrderForActiveMods(out var resolveError);
                    if (!string.IsNullOrEmpty(resolveError))
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            StatusMessage = "Build Failed: dependency resolution error";
                            BuildLog += $"ERROR: {resolveError}\n";
                        });
                        return;
                    }

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        foreach (var m in orderedMods)
                        {
                            m.BuildState = ModBuildState.Queued;
                            m.LastBuildMessage = "";
                        }
                    });

                    foreach (var mod in orderedMods)
                    {
                        if (!BuildModInternal(mod, exportReferenceAssembly: true))
                        {
                            return;
                        }
                    }
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        StatusMessage = "Mods build successful.";
                        BuildLog += "\nMods build successful.\n";
                    });
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.Invoke(() => {
                        StatusMessage = $"Build Error: {ex.Message}";
                        BuildLog += $"\nEXCEPTION: {ex.Message}";
                    });
                }
                finally
                {
                    Application.Current.Dispatcher.Invoke(() => IsBuilding = false);
                }
            });
        }

        [RelayCommand]
        private async System.Threading.Tasks.Task BuildDesktop()
        {
            if (IsBuilding) return;

            IsBuilding = true;
            StatusMessage = "Building Raylib App...";
            BuildLog = "Building Raylib App...\n";

            await System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var desktopProjectPath = Path.Combine(_rootDir, "src", "Apps", "Raylib", "Ludots.App.Raylib", "Ludots.App.Raylib.csproj");
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        StatusMessage = "Building Raylib App...";
                        BuildLog += $"Building Raylib App: {desktopProjectPath}\n";
                    });

                    var desktopExit = RunDotnetBuild(desktopProjectPath, _rootDir, additionalArgs: "-c Release");
                    if (desktopExit != 0)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            StatusMessage = "Raylib App build failed.";
                            BuildLog += "\nRaylib App build failed.\n";
                        });
                        return;
                    }

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        StatusMessage = "Raylib App build successful.";
                        BuildLog += "\nRaylib App build successful.\n";
                    });
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        StatusMessage = $"Build Error: {ex.Message}";
                        BuildLog += $"\nEXCEPTION: {ex.Message}";
                    });
                }
                finally
                {
                    Application.Current.Dispatcher.Invoke(() => IsBuilding = false);
                }
            });
        }

        [RelayCommand]
        private async System.Threading.Tasks.Task BuildMod(ModViewModel mod)
        {
            if (IsBuilding) return;

            IsBuilding = true;
            StatusMessage = $"Building Mod '{mod.Name}'...";
            BuildLog = $"Starting build for mod '{mod.Name}'...\n";

            await System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    BuildModInternal(mod, exportReferenceAssembly: true);
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.Invoke(() => {
                        StatusMessage = $"Build Error: {ex.Message}";
                        BuildLog += $"\nEXCEPTION: {ex.Message}";
                    });
                }
                finally
                {
                    Application.Current.Dispatcher.Invoke(() => IsBuilding = false);
                }
            });
        }

        private bool BuildModInternal(ModViewModel mod, bool exportReferenceAssembly)
        {
            string fullModPath = ResolveFullModPath(mod.Path);
            var csproj = ResolveBuildProjectPath(mod, fullModPath);

            Application.Current.Dispatcher.Invoke(() =>
            {
                mod.BuildState = ModBuildState.Building;
                mod.LastBuildMessage = "";
            });

            if (string.IsNullOrEmpty(csproj) || !File.Exists(csproj))
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    StatusMessage = $"Build Failed: No suitable .csproj found for {mod.Name}";
                    BuildLog += $"\nERROR: No suitable .csproj found. ModPath={fullModPath}\n";
                    mod.HasNoProject = true;
                    mod.BuildState = ModBuildState.NoProject;
                    mod.LastBuildMessage = "No project";
                });
                return false;
            }

            var projectDir = Path.GetDirectoryName(csproj) ?? fullModPath;
            Application.Current.Dispatcher.Invoke(() =>
            {
                StatusMessage = $"Building Mod '{mod.Name}'...";
                BuildLog += $"Building Mod {mod.Name}: {csproj}\n";
            });

            var exitCode = RunDotnetBuild(csproj, projectDir, additionalArgs: "/p:ProduceReferenceAssembly=true -c Release");
            if (exitCode != 0)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    StatusMessage = $"Mod '{mod.Name}' Build Failed.";
                    BuildLog += $"\nMod '{mod.Name}' Build Failed!\n";
                    mod.BuildState = ModBuildState.Failed;
                    mod.LastBuildMessage = "dotnet build failed";
                });
                return false;
            }

            if (exportReferenceAssembly)
            {
                ExportReferenceAssembly(mod, fullModPath, projectDir);
            }

            var graphExitCode = RunCompileGraphs(mod.Name);
            if (graphExitCode != 0)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    StatusMessage = $"Mod '{mod.Name}' Graph Compile Failed.";
                    BuildLog += $"\nMod '{mod.Name}' Graph Compile Failed!\n";
                    mod.BuildState = ModBuildState.Failed;
                    mod.LastBuildMessage = "graph compile failed";
                });
                return false;
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                StatusMessage = $"Mod '{mod.Name}' Build Successful!";
                BuildLog += $"\nMod '{mod.Name}' Build Successful!\n";
                mod.HasNoProject = false;
                mod.BuildState = ModBuildState.Succeeded;
                mod.LastBuildMessage = "OK";
            });

            return true;
        }

        private int RunDotnetBuild(string csprojPath, string workingDirectory, string additionalArgs)
        {
            var startInfo = new ProcessStartInfo("dotnet", $"build \"{csprojPath}\" {additionalArgs}".Trim())
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };

            using (var process = Process.Start(startInfo))
            {
                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                    {
                        Debug.WriteLine(e.Data);
                        Application.Current.Dispatcher.Invoke(() => BuildLog += e.Data + "\n");
                    }
                };
                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                    {
                        Debug.WriteLine(e.Data);
                        Application.Current.Dispatcher.Invoke(() => BuildLog += "ERROR: " + e.Data + "\n");
                    }
                };

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();
                return process.ExitCode;
            }
        }

        private int RunCompileGraphs(string modId)
        {
            var toolProject = Path.Combine(_rootDir, "src", "Tools", "Ludots.Tool", "Ludots.Tool.csproj");
            var args = $"run --project \"{toolProject}\" -- graph compile --mod \"{modId}\" --assetsRoot \"{_rootDir}\"";

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

            using (var process = Process.Start(startInfo))
            {
                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                    {
                        Debug.WriteLine(e.Data);
                        Application.Current.Dispatcher.Invoke(() => BuildLog += e.Data + "\n");
                    }
                };
                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                    {
                        Debug.WriteLine(e.Data);
                        Application.Current.Dispatcher.Invoke(() => BuildLog += "ERROR: " + e.Data + "\n");
                    }
                };

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();
                return process.ExitCode;
            }
        }

        private List<ModViewModel> ResolveBuildOrderForActiveMods(out string error)
        {
            error = null;

            var byName = Mods.ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);
            var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in Mods.Where(m => m.IsActive))
            {
                CollectDependencies(m.Name, byName, required, ref error);
                if (!string.IsNullOrEmpty(error)) return new List<ModViewModel>();
            }

            var result = new List<ModViewModel>();
            var temp = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var perm = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var id in required)
            {
                Visit(id, byName, required, temp, perm, result, ref error);
                if (!string.IsNullOrEmpty(error)) return new List<ModViewModel>();
            }

            return result;
        }

        private void CollectDependencies(string modName, Dictionary<string, ModViewModel> byName, HashSet<string> required, ref string error)
        {
            if (!required.Add(modName)) return;
            if (!byName.TryGetValue(modName, out var mod))
            {
                error = $"Missing dependency mod: {modName}";
                return;
            }

            foreach (var dep in mod.Dependencies.Keys)
            {
                if (string.IsNullOrWhiteSpace(dep)) continue;
                CollectDependencies(dep, byName, required, ref error);
                if (!string.IsNullOrEmpty(error)) return;
            }
        }

        private void Visit(
            string modName,
            Dictionary<string, ModViewModel> byName,
            HashSet<string> required,
            HashSet<string> temp,
            HashSet<string> perm,
            List<ModViewModel> result,
            ref string error)
        {
            if (perm.Contains(modName)) return;
            if (temp.Contains(modName))
            {
                error = $"Dependency cycle detected at: {modName}";
                return;
            }

            if (!byName.TryGetValue(modName, out var mod))
            {
                error = $"Missing dependency mod: {modName}";
                return;
            }

            temp.Add(modName);
            foreach (var dep in mod.Dependencies.Keys)
            {
                if (string.IsNullOrWhiteSpace(dep)) continue;
                if (!required.Contains(dep)) continue;
                Visit(dep, byName, required, temp, perm, result, ref error);
                if (!string.IsNullOrEmpty(error)) return;
            }
            temp.Remove(modName);
            perm.Add(modName);
            result.Add(mod);
        }

        private void ExportReferenceAssembly(ModViewModel mod, string fullModPath, string projectDir)
        {
            if (!TryFindReferenceAssembly(projectDir, mod.Name, out var referenceAssemblyPath))
            {
                throw new InvalidOperationException($"Reference assembly not found for {mod.Name} under {projectDir}\\obj\\**\\ref\\ (expected Release).");
            }

            var exportDir = Path.Combine(fullModPath, "ref");
            Directory.CreateDirectory(exportDir);
            var targetPath = Path.Combine(exportDir, $"{mod.Name}.dll");
            File.Copy(referenceAssemblyPath, targetPath, overwrite: true);

            Application.Current.Dispatcher.Invoke(() =>
            {
                BuildLog += $"Exported ref: {targetPath}\n";
            });
        }

        private bool TryFindReferenceAssembly(string projectDir, string assemblyName, out string path)
        {
            path = null;
            var objDir = Path.Combine(projectDir, "obj");
            if (!Directory.Exists(objDir)) return false;

            var candidates = Directory.EnumerateFiles(objDir, $"{assemblyName}.dll", SearchOption.AllDirectories)
                .Where(p =>
                {
                    var normalized = p.Replace('\\', '/');
                    if (!normalized.Contains("/ref/", StringComparison.OrdinalIgnoreCase)) return false;
                    if (normalized.Contains("/refint/", StringComparison.OrdinalIgnoreCase)) return false;
                    if (!normalized.Contains("/release/", StringComparison.OrdinalIgnoreCase)) return false;
                    return true;
                })
                .ToList();

            if (candidates.Count == 0) return false;
            path = candidates.OrderByDescending(p => File.GetLastWriteTimeUtc(p)).First();
            return true;
        }

        private string ResolveFullModPath(string modPath)
        {
            if (Path.IsPathRooted(modPath)) return modPath;
            return Path.Combine(_rootDir, modPath);
        }

        private void RefreshBuildStateFromDisk(ModViewModel mod, string fullModPath)
        {
            if (mod.HasNoProject)
            {
                mod.BuildState = ModBuildState.NoProject;
                mod.LastBuildMessage = "No project";
                return;
            }

            if (string.IsNullOrWhiteSpace(mod.Main))
            {
                mod.BuildState = ModBuildState.Failed;
                mod.LastBuildMessage = "Invalid main";
                return;
            }

            var dllRelative = mod.Main.Replace("/", "\\");
            var dllPath = Path.GetFullPath(Path.Combine(fullModPath, dllRelative));

            if (!File.Exists(dllPath))
            {
                mod.BuildState = ModBuildState.Idle;
                mod.LastBuildMessage = "Not built";
                return;
            }

            var dllTime = File.GetLastWriteTimeUtc(dllPath);
            var latestSource = GetLatestSourceWriteUtc(fullModPath);

            if (latestSource > dllTime)
            {
                mod.BuildState = ModBuildState.Outdated;
                mod.LastBuildMessage = "Outdated";
                return;
            }

            mod.BuildState = ModBuildState.Succeeded;
            mod.LastBuildMessage = "OK";
        }

        private static DateTime GetLatestSourceWriteUtc(string fullModPath)
        {
            var latest = DateTime.MinValue;
            foreach (var file in Directory.EnumerateFiles(fullModPath, "*.*", SearchOption.AllDirectories))
            {
                var normalized = file.Replace('\\', '/');
                if (normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)) continue;
                if (normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)) continue;
                if (normalized.Contains("/ref/", StringComparison.OrdinalIgnoreCase)) continue;

                var t = File.GetLastWriteTimeUtc(file);
                if (t > latest) latest = t;
            }
            return latest;
        }

        private string ResolveBuildProjectPath(ModViewModel mod, string fullModPath)
        {
            var assetCsproj = Directory.GetFiles(fullModPath, "*.csproj").FirstOrDefault();
            if (!string.IsNullOrEmpty(assetCsproj) && ProjectHasUserSourceFiles(assetCsproj))
            {
                return assetCsproj;
            }

            var repoProject = Path.Combine(_rootDir, "src", "Mods", mod.Name, $"{mod.Name}.csproj");
            if (File.Exists(repoProject))
            {
                return repoProject;
            }

            return assetCsproj;
        }

        private bool ProjectHasUserSourceFiles(string csprojPath)
        {
            var projectDir = Path.GetDirectoryName(csprojPath);
            if (string.IsNullOrEmpty(projectDir) || !Directory.Exists(projectDir)) return false;

            foreach (var file in Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories))
            {
                var normalized = file.Replace('\\', '/');
                if (normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)) continue;
                if (normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)) continue;
                return true;
            }

            return false;
        }

        [RelayCommand]
        private void FixModProject(ModViewModel mod)
        {
             try
             {
                string fullModPath = mod.Path;
                if (!Path.IsPathRooted(fullModPath))
                {
                    fullModPath = Path.Combine(_rootDir, mod.Path);
                }

                var repoProject = Path.Combine(_rootDir, "src", "Mods", mod.Name, $"{mod.Name}.csproj");
                if (File.Exists(repoProject))
                {
                    StatusMessage = $"Using repo project for {mod.Name}: {repoProject}";
                    BuildLog += $"Using repo project for {mod.Name}: {repoProject}\n";
                    mod.HasNoProject = false;
                    return;
                }

                string csprojPath = Path.Combine(fullModPath, $"{mod.Name}.csproj");
                if (File.Exists(csprojPath))
                {
                    StatusMessage = $"Project already exists for {mod.Name}";
                    mod.HasNoProject = false;
                    return;
                }

                var sdkPropsPath = Path.Combine(_rootDir, "assets", "ModSdk", "ModSdk.props");
                if (!File.Exists(sdkPropsPath))
                {
                    throw new FileNotFoundException($"Mod SDK props not found: {sdkPropsPath}");
                }
                var relSdkPropsPath = Path.GetRelativePath(fullModPath, sdkPropsPath);

                // Generate .csproj
                string csprojContent = $@"<Project Sdk=""Microsoft.NET.Sdk"">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <OutputPath>bin</OutputPath>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
  </PropertyGroup>

  <Import Project=""{relSdkPropsPath}"" />

</Project>";

                // Adjust relative paths if Mod is not in assets/Mods
                // For now, assume standard location or user can fix it manually if weird path.
                // If mod.Path is absolute (external), relative paths to Core won't work easily without calculation.
                // Let's try to calculate relative path from mod dir to src/Core
                
                if (Path.IsPathRooted(mod.Path))
                {
                    // External mod. We can't easily reference local projects via relative path unless we know where src is relative to mod.
                    // Best effort: Reference compiled DLLs or warn user.
                    // For now, let's just create it and let user fix reference if needed.
                    // Or, if we can find _rootDir, we can try to make a relative path.
                    
                    // Actually, if it's external, maybe we should just reference Core.dll from the game bin?
                    // But we want source reference for intellisense if possible.
                    
                    // Let's stick to the template above, which works for assets/Mods/ModName
                    // If file path is different, we might need to adjust "..\..\..\..\"
                }

                File.WriteAllText(csprojPath, csprojContent);
                StatusMessage = $"Created .csproj for {mod.Name}";
                BuildLog += $"Created project file: {csprojPath}\n";
                mod.HasNoProject = false;
             }
             catch (Exception ex)
             {
                 StatusMessage = $"Error fixing project: {ex.Message}";
             }
        }

        [RelayCommand]
        private void AddModDirectory()
        {
            // Ideally use FolderBrowserDialog, but in pure MVVM/WPF without extra libs it's tricky.
            // For now, let's use a simple InputBox or MessageBox hack, or just hardcode for demo.
            // In a real app we'd use a service for Dialogs.
            
            // Let's assume we want to support C:\MyMods
            // We can add a simple dialog later.
            // For now, let's try to prompt via Console? No.
            // Let's use WinForms dialog (add ref) or just a simple WPF dialog.
            
            // To keep it simple and dependency free, I'll simulate adding a path
            // or just rely on manual config editing if we don't add FolderBrowser.
            
            // Actually, let's use Microsoft.Win32.OpenFolderDialog if available in .NET 8 WPF?
            // Yes, OpenFolderDialog is available in .NET 8.
            
            var dialog = new Microsoft.Win32.OpenFolderDialog();
            dialog.Title = "Select Mod Directory";
            if (dialog.ShowDialog() == true)
            {
                var dir = Path.GetFullPath(dialog.FolderName);
                if (!_extraModDirs.Any(x => x.Equals(dir, StringComparison.OrdinalIgnoreCase)))
                {
                    _extraModDirs.Add(dir);
                    PersistLauncherConfig();
                    LoadMods();
                }
            }
        }

        private void InitializePaths()
        {
            // Assume running from src/Tools/ModLauncher/bin/Debug/net8.0-windows
            // We need to find root (Ludots)
            _rootDir = AppDomain.CurrentDomain.BaseDirectory;
            
            // Traverse up until we find 'assets' or 'src'
            var dir = new DirectoryInfo(_rootDir);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "assets")))
            {
                dir = dir.Parent;
            }

            if (dir != null)
            {
                _rootDir = dir.FullName;
                _assetsDir = Path.Combine(_rootDir, "assets");
                _modsDir = Path.Combine(_assetsDir, "Mods");
                
                // Let's find the Raylib app build output
                _gameExePath = Path.Combine(_rootDir, "src", "Apps", "Raylib", "Ludots.App.Raylib", "bin", "Release", "net8.0", "Ludots.App.Raylib.exe");
                
                // We will write game.json next to the EXE
                _gameJsonPath = Path.Combine(Path.GetDirectoryName(_gameExePath), "game.json");
            }
            else
            {
                StatusMessage = "Error: Could not locate assets directory.";
            }
        }

        [RelayCommand]
        private void LoadMods()
        {
            if (_modsDir == null || !Directory.Exists(_modsDir))
            {
                StatusMessage = "Mods directory not found.";
                return;
            }

            Mods.Clear();

            // Read active mods from game.json
            var activePaths = new HashSet<string>();

            if (File.Exists(_gameJsonPath))
            {
                try
                {
                    var json = File.ReadAllText(_gameJsonPath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("ModPaths", out var paths))
                    {
                        foreach (var p in paths.EnumerateArray())
                        {
                            activePaths.Add(p.GetString()?.Replace("\\", "/"));
                        }
                    }
                }
                catch { }
            }

            // Scan Mods
            var directoriesToScan = new List<string>();
            if (Directory.Exists(_modsDir)) directoriesToScan.Add(_modsDir);
            directoriesToScan.AddRange(_extraModDirs);

            void TryLoadModFromDirectory(string dir)
            {
                var modJson = Path.Combine(dir, "mod.json");
                if (!File.Exists(modJson)) return;

                try
                {
                    var content = File.ReadAllText(modJson);
                    if (!TryParseManifestStrict(content, modJson, out var manifestName, out var manifestVersion, out var manifestDescription, out var manifestMain, out var manifestDependencies, out var manifestError))
                    {
                        Debug.WriteLine($"Invalid mod.json at {modJson}: {manifestError}");
                        Application.Current.Dispatcher.Invoke(() => BuildLog += $"ERROR: {manifestError}\n");
                        return;
                    }

                    var modPath = dir.Replace("\\", "/");
                    bool isActive = activePaths.Contains(modPath);

                    var modVm = new ModViewModel
                    {
                        Name = manifestName,
                        Version = manifestVersion,
                        Description = manifestDescription,
                        Main = manifestMain,
                        Path = modPath,
                        IsActive = isActive,
                        Dependencies = manifestDependencies,
                        Parent = this
                    };

                    if (Mods.Any(m => string.Equals(m.Name, modVm.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        return;
                    }

                    string fullModPath = ResolveFullModPath(modPath);
                    bool hasProject = Directory.GetFiles(fullModPath, "*.csproj").Any();

                    modVm.HasNoProject = !hasProject;
                    modVm.ThumbnailImage = TryLoadThumbnail(fullModPath);
                    RefreshBuildStateFromDisk(modVm, fullModPath);
                    Mods.Add(modVm);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to load mod {dir}: {ex}");
                }
            }

            foreach (var rootDir in directoriesToScan)
            {
                if (!Directory.Exists(rootDir)) continue;

                TryLoadModFromDirectory(rootDir);

                foreach (var dir in Directory.GetDirectories(rootDir))
                {
                    TryLoadModFromDirectory(dir);
                }
            }
            
            StatusMessage = $"Loaded {Mods.Count} mods.";
            RebuildDependencyGraph();
        }

        private static BitmapImage? TryLoadThumbnail(string fullModPath)
        {
            var candidates = new[]
            {
                Path.Combine(fullModPath, "assets", "Launcher", "thumbnail.png"),
                Path.Combine(fullModPath, "assets", "Launcher", "thumbnail.jpg"),
                Path.Combine(fullModPath, "assets", "Launcher", "banner.png"),
                Path.Combine(fullModPath, "assets", "Launcher", "icon.png")
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                var p = candidates[i];
                if (!File.Exists(p)) continue;

                try
                {
                    var img = new BitmapImage();
                    img.BeginInit();
                    img.CacheOption = BitmapCacheOption.OnLoad;
                    img.UriSource = new Uri(p, UriKind.Absolute);
                    img.EndInit();
                    img.Freeze();
                    return img;
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }

        public void ValidateDependencies()
        {
            bool changed = true;
            while (changed)
            {
                changed = false;
                var byName = Mods.ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);
                var active = Mods.Where(m => m.IsActive).ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);

                foreach (var mod in Mods)
                {
                    if (!mod.IsActive) continue;

                    if (!SemVersion.TryParse(mod.Version, out var modVersion))
                    {
                        MessageBox.Show($"Invalid version '{mod.Version}' for mod '{mod.Name}'.", "Dependency Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        mod.SetIsActiveFromParent(false);
                        changed = true;
                        break;
                    }

                    foreach (var dep in mod.Dependencies)
                    {
                        var depName = dep.Key;
                        var rangeText = dep.Value;
                        if (!byName.TryGetValue(depName, out var depMod))
                        {
                            MessageBox.Show($"Missing dependency '{depName}' for mod '{mod.Name}'. Cannot activate.", "Dependency Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            mod.SetIsActiveFromParent(false);
                            changed = true;
                            break;
                        }

                        if (!SemVersion.TryParse(depMod.Version, out var depVersion))
                        {
                            MessageBox.Show($"Invalid version '{depMod.Version}' for dependency '{depName}'.", "Dependency Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            mod.SetIsActiveFromParent(false);
                            changed = true;
                            break;
                        }

                        if (!SemVersionRange.TryParse(rangeText, out var range))
                        {
                            MessageBox.Show($"Invalid dependency range '{rangeText}' for '{mod.Name}' -> '{depName}'.", "Dependency Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            mod.SetIsActiveFromParent(false);
                            changed = true;
                            break;
                        }

                        if (!range.Matches(depVersion))
                        {
                            MessageBox.Show($"Version mismatch: '{mod.Name}' requires '{depName}' {rangeText} but found {depMod.Version}.", "Dependency Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            mod.SetIsActiveFromParent(false);
                            changed = true;
                            break;
                        }

                        if (!active.ContainsKey(depName))
                        {
                            depMod.SetIsActiveFromParent(true);
                            StatusMessage = $"Auto-activated dependency: {depName} for {mod.Name}";
                            changed = true;
                        }
                    }

                    if (changed) break;
                }
            }
            RebuildDependencyGraph();
        }

        internal void OnModActivationToggled(ModViewModel mod, bool isActive)
        {
            if (_isUpdatingActivation) return;
            _isUpdatingActivation = true;
            try
            {
                if (isActive)
                {
                    mod.SetIsActiveFromParent(true);
                    ValidateDependencies();
                    return;
                }

                var dependents = CollectActiveDependents(mod.Name);
                if (dependents.Count > 0)
                {
                    var list = string.Join(", ", dependents.Select(m => m.Name));
                    var result = MessageBox.Show($"取消勾选 '{mod.Name}' 将级联取消: {list}。是否继续？", "Cascade Deactivate", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (result != MessageBoxResult.Yes)
                    {
                        mod.SetIsActiveFromParent(true);
                        RebuildDependencyGraph();
                        return;
                    }
                }

                mod.SetIsActiveFromParent(false);
                foreach (var dep in dependents)
                {
                    dep.SetIsActiveFromParent(false);
                }

                RebuildDependencyGraph();
            }
            finally
            {
                _isUpdatingActivation = false;
                OnModSelectionChanged();
            }
        }

        private List<ModViewModel> CollectActiveDependents(string dependencyName)
        {
            var byName = Mods.Where(m => m.IsActive).ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);
            var collected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>();
            queue.Enqueue(dependencyName);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var mod in byName.Values)
                {
                    if (mod.Dependencies.ContainsKey(current) && collected.Add(mod.Name))
                    {
                        queue.Enqueue(mod.Name);
                    }
                }
            }

            return collected.Select(n => byName[n]).OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private void RebuildDependencyGraph()
        {
            DependencyGraphNodes.Clear();
            DependencyGraphEdges.Clear();

            var byName = Mods
                .Where(m => !string.IsNullOrWhiteSpace(m.Name))
                .ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);

            var seeds = Mods.Where(m => m.IsActive).Select(m => m.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
            if (seeds.Count == 0)
                seeds = Mods.Select(m => m.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();

            var included = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>(seeds);
            while (queue.Count > 0)
            {
                var name = queue.Dequeue();
                if (!included.Add(name)) continue;

                if (byName.TryGetValue(name, out var mod))
                {
                    foreach (var dep in mod.Dependencies.Keys)
                    {
                        if (!string.IsNullOrWhiteSpace(dep))
                            queue.Enqueue(dep);
                    }
                }
            }

            var nodes = new Dictionary<string, DependencyGraphNodeViewModel>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in included.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                if (byName.TryGetValue(name, out var mod))
                    nodes[name] = new DependencyGraphNodeViewModel(mod);
                else
                    nodes[name] = DependencyGraphNodeViewModel.CreateMissing(name);
            }

            var adjacency = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var indegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in included)
            {
                adjacency[name] = new List<string>();
                indegree[name] = 0;
            }

            foreach (var mod in byName.Values)
            {
                if (!included.Contains(mod.Name)) continue;
                foreach (var dep in mod.Dependencies)
                {
                    var depName = dep.Key;
                    if (string.IsNullOrWhiteSpace(depName)) continue;
                    if (!included.Contains(depName)) continue;

                    adjacency[depName].Add(mod.Name);
                    indegree[mod.Name] = indegree[mod.Name] + 1;
                }
            }

            var level = included.ToDictionary(n => n, _ => 0, StringComparer.OrdinalIgnoreCase);
            var topoQueue = new Queue<string>(indegree.Where(kvp => kvp.Value == 0).Select(kvp => kvp.Key).OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
            var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var maxLevel = 0;

            while (topoQueue.Count > 0)
            {
                var n = topoQueue.Dequeue();
                processed.Add(n);
                var baseLevel = level[n];
                if (baseLevel > maxLevel) maxLevel = baseLevel;

                foreach (var to in adjacency[n])
                {
                    var nextLevel = baseLevel + 1;
                    if (nextLevel > level[to]) level[to] = nextLevel;
                    indegree[to] = indegree[to] - 1;
                    if (indegree[to] == 0)
                        topoQueue.Enqueue(to);
                }
            }

            foreach (var name in included)
            {
                if (!processed.Contains(name))
                {
                    nodes[name].IsCycle = true;
                    level[name] = maxLevel + 1;
                }
            }

            const double startX = 10;
            const double startY = 10;
            const double colWidth = 190;
            const double rowHeight = 48;

            foreach (var g in nodes.Values.GroupBy(n => level[n.Name]).OrderBy(g => g.Key))
            {
                var x = startX + g.Key * colWidth;
                var ordered = g.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ToList();
                for (int i = 0; i < ordered.Count; i++)
                {
                    ordered[i].X = x;
                    ordered[i].Y = startY + i * rowHeight;
                }
            }

            if (nodes.Count > 0)
            {
                DependencyGraphWidth = Math.Max(260, nodes.Values.Max(n => n.X) + DependencyGraphNodeViewModel.NodeWidth + 20);
                DependencyGraphHeight = Math.Max(300, nodes.Values.Max(n => n.Y) + DependencyGraphNodeViewModel.NodeHeight + 20);
            }
            else
            {
                DependencyGraphWidth = 260;
                DependencyGraphHeight = 300;
            }

            foreach (var node in nodes.Values.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase))
            {
                DependencyGraphNodes.Add(node);
            }

            foreach (var mod in byName.Values)
            {
                if (!included.Contains(mod.Name)) continue;
                foreach (var dep in mod.Dependencies.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
                {
                    var depName = dep.Key;
                    var rangeText = dep.Value;
                    if (string.IsNullOrWhiteSpace(depName)) continue;
                    if (!included.Contains(depName)) continue;

                    var from = nodes[depName];
                    var to = nodes[mod.Name];

                    var isError = false;
                    if (from.IsMissing)
                    {
                        isError = true;
                    }
                    else
                    {
                        if (!SemVersion.TryParse(from.Version, out var depVer) || !SemVersionRange.TryParse(rangeText, out var range) || !range.Matches(depVer))
                            isError = true;
                    }

                    DependencyGraphEdges.Add(new DependencyGraphEdgeViewModel(from, to, rangeText ?? "", isError));
                }
            }
        }

        private static bool TryParseManifestStrict(
            string json,
            string manifestPath,
            out string name,
            out string version,
            out string description,
            out string main,
            out Dictionary<string, string> dependencies,
            out string error)
        {
            name = null;
            version = null;
            description = "";
            main = null;
            dependencies = new Dictionary<string, string>(StringComparer.Ordinal);
            error = null;

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    error = $"Invalid mod.json (root is not object): {manifestPath}";
                    return false;
                }

                var root = doc.RootElement;
                var allowed = new HashSet<string>(StringComparer.Ordinal)
                {
                    "name",
                    "version",
                    "description",
                    "main",
                    "priority",
                    "dependencies"
                };

                foreach (var prop in root.EnumerateObject())
                {
                    if (!allowed.Contains(prop.Name))
                    {
                        error = $"Invalid mod.json (unknown/forbidden field '{prop.Name}'): {manifestPath}";
                        return false;
                    }
                }

                if (!root.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
                {
                    error = $"Invalid mod.json (missing 'name'): {manifestPath}";
                    return false;
                }
                if (!root.TryGetProperty("version", out var verEl) || verEl.ValueKind != JsonValueKind.String)
                {
                    error = $"Invalid mod.json (missing 'version'): {manifestPath}";
                    return false;
                }
                if (!root.TryGetProperty("main", out var mainEl) || mainEl.ValueKind != JsonValueKind.String)
                {
                    error = $"Invalid mod.json (missing 'main'): {manifestPath}";
                    return false;
                }

                name = nameEl.GetString();
                version = verEl.GetString();
                main = mainEl.GetString();

                if (string.IsNullOrWhiteSpace(name))
                {
                    error = $"Invalid mod.json ('name' is empty): {manifestPath}";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(version))
                {
                    error = $"Invalid mod.json ('version' is empty): {manifestPath}";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(main))
                {
                    error = $"Invalid mod.json ('main' is empty): {manifestPath}";
                    return false;
                }

                if (root.TryGetProperty("description", out var descEl) && descEl.ValueKind == JsonValueKind.String)
                {
                    description = descEl.GetString() ?? "";
                }

                if (root.TryGetProperty("dependencies", out var depsEl))
                {
                    if (depsEl.ValueKind != JsonValueKind.Object)
                    {
                        error = $"Invalid mod.json ('dependencies' must be object): {manifestPath}";
                        return false;
                    }

                    foreach (var depProp in depsEl.EnumerateObject())
                    {
                        if (depProp.Value.ValueKind != JsonValueKind.String)
                        {
                            error = $"Invalid mod.json ('dependencies.{depProp.Name}' must be string): {manifestPath}";
                            return false;
                        }

                        dependencies[depProp.Name] = depProp.Value.GetString();
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                error = $"Invalid mod.json ({ex.Message}): {manifestPath}";
                return false;
            }
        }

        private readonly struct SemVersion : IComparable<SemVersion>
        {
            public readonly int Major;
            public readonly int Minor;
            public readonly int Patch;

            public SemVersion(int major, int minor, int patch)
            {
                Major = major;
                Minor = minor;
                Patch = patch;
            }

            public int CompareTo(SemVersion other)
            {
                var c = Major.CompareTo(other.Major);
                if (c != 0) return c;
                c = Minor.CompareTo(other.Minor);
                if (c != 0) return c;
                return Patch.CompareTo(other.Patch);
            }

            public override string ToString() => $"{Major}.{Minor}.{Patch}";

            public static bool TryParse(string text, out SemVersion version)
            {
                version = default;
                if (string.IsNullOrWhiteSpace(text)) return false;
                var parts = text.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length != 3) return false;
                if (!int.TryParse(parts[0], out var major)) return false;
                if (!int.TryParse(parts[1], out var minor)) return false;
                if (!int.TryParse(parts[2], out var patch)) return false;
                if (major < 0 || minor < 0 || patch < 0) return false;
                version = new SemVersion(major, minor, patch);
                return true;
            }
        }

        private readonly struct SemVersionRange
        {
            private readonly List<Comparator> _comparators;

            private SemVersionRange(List<Comparator> comparators)
            {
                _comparators = comparators;
            }

            public bool Matches(SemVersion v)
            {
                if (_comparators == null || _comparators.Count == 0) return true;
                foreach (var c in _comparators)
                {
                    if (!c.Matches(v)) return false;
                }
                return true;
            }

            public static bool TryParse(string text, out SemVersionRange range)
            {
                range = default;
                if (string.IsNullOrWhiteSpace(text) || text.Trim() == "*")
                {
                    range = new SemVersionRange(new List<Comparator>());
                    return true;
                }

                var comparators = new List<Comparator>();
                var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var token in tokens)
                {
                    if (token.StartsWith("^", StringComparison.Ordinal))
                    {
                        if (!SemVersion.TryParse(token[1..], out var baseVer)) return false;
                        comparators.Add(new Comparator(CompareOp.Gte, baseVer));
                        comparators.Add(new Comparator(CompareOp.Lt, new SemVersion(baseVer.Major + 1, 0, 0)));
                        continue;
                    }

                    if (token.StartsWith("~", StringComparison.Ordinal))
                    {
                        if (!SemVersion.TryParse(token[1..], out var baseVer)) return false;
                        comparators.Add(new Comparator(CompareOp.Gte, baseVer));
                        comparators.Add(new Comparator(CompareOp.Lt, new SemVersion(baseVer.Major, baseVer.Minor + 1, 0)));
                        continue;
                    }

                    if (token.StartsWith(">=", StringComparison.Ordinal))
                    {
                        if (!SemVersion.TryParse(token[2..], out var ver)) return false;
                        comparators.Add(new Comparator(CompareOp.Gte, ver));
                        continue;
                    }
                    if (token.StartsWith("<=", StringComparison.Ordinal))
                    {
                        if (!SemVersion.TryParse(token[2..], out var ver)) return false;
                        comparators.Add(new Comparator(CompareOp.Lte, ver));
                        continue;
                    }
                    if (token.StartsWith(">", StringComparison.Ordinal))
                    {
                        if (!SemVersion.TryParse(token[1..], out var ver)) return false;
                        comparators.Add(new Comparator(CompareOp.Gt, ver));
                        continue;
                    }
                    if (token.StartsWith("<", StringComparison.Ordinal))
                    {
                        if (!SemVersion.TryParse(token[1..], out var ver)) return false;
                        comparators.Add(new Comparator(CompareOp.Lt, ver));
                        continue;
                    }
                    if (token.StartsWith("=", StringComparison.Ordinal))
                    {
                        if (!SemVersion.TryParse(token[1..], out var ver)) return false;
                        comparators.Add(new Comparator(CompareOp.Eq, ver));
                        continue;
                    }

                    if (!SemVersion.TryParse(token, out var exact)) return false;
                    comparators.Add(new Comparator(CompareOp.Eq, exact));
                }

                range = new SemVersionRange(comparators);
                return true;
            }

            private enum CompareOp
            {
                Eq,
                Gt,
                Gte,
                Lt,
                Lte
            }

            private readonly struct Comparator
            {
                private readonly CompareOp _op;
                private readonly SemVersion _value;

                public Comparator(CompareOp op, SemVersion value)
                {
                    _op = op;
                    _value = value;
                }

                public bool Matches(SemVersion v)
                {
                    var c = v.CompareTo(_value);
                    return _op switch
                    {
                        CompareOp.Eq => c == 0,
                        CompareOp.Gt => c > 0,
                        CompareOp.Gte => c >= 0,
                        CompareOp.Lt => c < 0,
                        CompareOp.Lte => c <= 0,
                        _ => false
                    };
                }
            }
        }

        [RelayCommand]
        private void LaunchGame()
        {
            ValidateDependencies();

            var invalidBuild = Mods
                .Where(m => m.IsActive)
                .Where(m => m.BuildState != ModBuildState.Succeeded)
                .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (invalidBuild.Count > 0)
            {
                var list = string.Join(", ", invalidBuild.Select(m => $"{m.Name}({m.BuildState})"));
                MessageBox.Show($"存在未构建成功的已启用 Mod，禁止启动：{list}", "Launch Blocked", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!File.Exists(_gameExePath))
            {
                MessageBox.Show($"未找到 Raylib 可执行文件，请先构建 Raylib App：{_gameExePath}", "Launch Blocked", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!SaveConfig())
                return;

            StatusMessage = "Launching Game...";
            var startInfo = new ProcessStartInfo(_gameExePath)
            {
                WorkingDirectory = Path.GetDirectoryName(_gameExePath)
            };
            Process.Start(startInfo);
        }

        private bool SaveConfig()
        {
            if (string.IsNullOrWhiteSpace(_gameJsonPath))
            {
                MessageBox.Show("game.json 路径不可用，请先构建 Raylib App。", "Save Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            var activeMods = Mods.Where(m => m.IsActive).Select(m => m.Path).ToList();
            var config = new { ModPaths = activeMods };
            
            try
            {
                var dir = Path.GetDirectoryName(_gameJsonPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_gameJsonPath, json);
                StatusMessage = "Config saved.";
                return true;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error saving config: {ex.Message}";
                MessageBox.Show(ex.Message, "Save Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }
    }

    public enum ModBuildState
    {
        NoProject,
        Idle,
        Outdated,
        Queued,
        Building,
        Succeeded,
        Failed
    }

    public partial class DependencyGraphNodeViewModel : ObservableObject
    {
        private readonly string _name;
        private readonly string _version;

        public ModViewModel Mod { get; }
        public bool IsMissing { get; }

        [ObservableProperty]
        private bool _isCycle;

        [ObservableProperty]
        private double _x;

        [ObservableProperty]
        private double _y;

        public static double NodeWidth => 160;
        public static double NodeHeight => 28;

        public string Name => Mod?.Name ?? _name;
        public string Version => Mod?.Version ?? _version;
        public bool IsActive => Mod?.IsActive ?? false;
        public ModBuildState BuildState => Mod?.BuildState ?? ModBuildState.Idle;

        public string DisplayText
        {
            get
            {
                if (IsMissing) return $"{Name} (missing)";
                if (IsCycle) return $"↻ {Name}";
                return $"{Name}@{Version}";
            }
        }

        public DependencyGraphNodeViewModel(ModViewModel mod)
        {
            Mod = mod;
            _name = mod?.Name ?? "";
            _version = mod?.Version ?? "";
            IsMissing = false;

            if (Mod != null)
            {
                Mod.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(ModViewModel.BuildState))
                    {
                        OnPropertyChanged(nameof(BuildState));
                    }
                    if (e.PropertyName == nameof(ModViewModel.IsActive))
                    {
                        OnPropertyChanged(nameof(IsActive));
                    }
                    if (e.PropertyName == nameof(ModViewModel.Version))
                    {
                        OnPropertyChanged(nameof(Version));
                        OnPropertyChanged(nameof(DisplayText));
                    }
                };
            }
        }

        private DependencyGraphNodeViewModel(string name, bool isMissing)
        {
            _name = name ?? "";
            _version = "";
            IsMissing = isMissing;
            IsCycle = false;
        }

        public static DependencyGraphNodeViewModel CreateMissing(string name) => new DependencyGraphNodeViewModel(name, isMissing: true);
    }

    public sealed class DependencyGraphEdgeViewModel
    {
        public DependencyGraphNodeViewModel From { get; }
        public DependencyGraphNodeViewModel To { get; }
        public string Label { get; }
        public bool IsError { get; }

        public double X1 { get; }
        public double Y1 { get; }
        public double X2 { get; }
        public double Y2 { get; }
        public double LabelX { get; }
        public double LabelY { get; }

        public DependencyGraphEdgeViewModel(DependencyGraphNodeViewModel from, DependencyGraphNodeViewModel to, string label, bool isError)
        {
            From = from;
            To = to;
            Label = label ?? "";
            IsError = isError;

            X1 = from.X + DependencyGraphNodeViewModel.NodeWidth;
            Y1 = from.Y + DependencyGraphNodeViewModel.NodeHeight / 2;
            X2 = to.X;
            Y2 = to.Y + DependencyGraphNodeViewModel.NodeHeight / 2;

            LabelX = (X1 + X2) / 2;
            LabelY = (Y1 + Y2) / 2 - 10;
        }
    }

    public partial class ModViewModel : ObservableObject
    {
        public string Name { get; set; }
        public string Version { get; set; }
        public string Description { get; set; }
        public string Main { get; set; }
        public string Path { get; set; }
        public Dictionary<string, string> Dependencies { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
        public MainViewModel Parent { get; set; }

        [ObservableProperty]
        private BitmapImage? _thumbnailImage;

        [ObservableProperty]
        private bool _hasNoProject;

        [ObservableProperty]
        private ModBuildState _buildState;

        [ObservableProperty]
        private string _lastBuildMessage = "";

        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive == value) return;
                if (Parent != null)
                {
                    Parent.OnModActivationToggled(this, value);
                    return;
                }

                _isActive = value;
                OnPropertyChanged(nameof(IsActive));
            }
        }

        internal void SetIsActiveFromParent(bool value)
        {
            if (_isActive == value) return;
            _isActive = value;
            OnPropertyChanged(nameof(IsActive));
        }
    }
}
