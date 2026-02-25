using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph;
using GraphProgramBlob = Ludots.Core.GraphRuntime.GraphProgramBlob;
using GraphProgramPackage = Ludots.Core.GraphRuntime.GraphProgramPackage;
using Ludots.Core.Map.Hex;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.NavBake.Recast;

namespace Ludots.Tool
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            var rootCommand = new RootCommand("Ludots Mod Development Tool");

            // --- 'mod' command group ---
            var modCommand = new Command("mod", "Manage Ludots mods");
            
            // 'init' command
            var initCommand = new Command("init", "Initialize a new mod project");
            var modIdOption = new Option<string>("--id", "The ID of the mod");
            modIdOption.IsRequired = true;
            initCommand.AddOption(modIdOption);
            initCommand.SetHandler((string id) => InitMod(id), modIdOption);
            
            // 'build' command
            var buildCommand = new Command("build", "Build the mod project");
            var buildIdOption = new Option<string>("--id", "The ID of the mod to build");
            buildIdOption.IsRequired = true;
            buildCommand.AddOption(buildIdOption);
            buildCommand.SetHandler((string id) => BuildMod(id), buildIdOption);

            modCommand.AddCommand(initCommand);
            modCommand.AddCommand(buildCommand);
            
            rootCommand.AddCommand(modCommand);

            var graphCommand = new Command("graph", "Compile graph assets");
            var compileGraphsCommand = new Command("compile", "Compile GAS graphs to binary blob");
            var graphModOption = new Option<string>("--mod", "The mod ID to compile graphs for") { IsRequired = true };
            var assetsRootOption = new Option<string?>("--assetsRoot", () => null, "Assets root (repo root containing 'assets/')");
            compileGraphsCommand.AddOption(graphModOption);
            compileGraphsCommand.AddOption(assetsRootOption);
            compileGraphsCommand.SetHandler((InvocationContext ctx) =>
            {
                var mod = ctx.ParseResult.GetValueForOption(graphModOption);
                var assetsRoot = ctx.ParseResult.GetValueForOption(assetsRootOption);
                ctx.ExitCode = CompileGraphs(mod, assetsRoot);
            });
            graphCommand.AddCommand(compileGraphsCommand);
            rootCommand.AddCommand(graphCommand);

            var mapCommand = new Command("map", "Map utilities");
            var importReactCommand = new Command("import-react", "Convert React web editor map_data.bin to VertexMap binary");
            var inputBinOption = new Option<string>("--in", "Input React map_data.bin path") { IsRequired = true };
            var outDirOption = new Option<string?>("--outDir", () => null, "Output directory (default: assets/Data/Maps)");
            var nameOption = new Option<string?>("--name", () => null, "Output base name (default: input filename)");
            var forceOption = new Option<bool>("--force", () => false, "Overwrite output files if exist");
            importReactCommand.AddOption(inputBinOption);
            importReactCommand.AddOption(outDirOption);
            importReactCommand.AddOption(nameOption);
            importReactCommand.AddOption(forceOption);
            importReactCommand.SetHandler((InvocationContext ctx) =>
            {
                var inputPath = ctx.ParseResult.GetValueForOption(inputBinOption);
                var outDir = ctx.ParseResult.GetValueForOption(outDirOption);
                var name = ctx.ParseResult.GetValueForOption(nameOption);
                var force = ctx.ParseResult.GetValueForOption(forceOption);
                ctx.ExitCode = ImportReactMap(inputPath, outDir, name, force);
            });
            mapCommand.AddCommand(importReactCommand);

            var genVtxmCommand = new Command("gen-vtxm", "Generate a VertexMap v2 .vtxm test map");
            var genOutOption = new Option<string>("--out", "Output .vtxm file path") { IsRequired = true };
            var genWidthOption = new Option<int>("--widthChunks", () => 16, "Map width in chunks");
            var genHeightOption = new Option<int>("--heightChunks", () => 16, "Map height in chunks");
            var genChunkSizeOption = new Option<int>("--chunkSize", () => 64, "Chunk size (power-of-two)");
            var genPresetOption = new Option<string>("--preset", () => "bench", "Preset: bench|flat|stripes|cliffs|lake");
            var genOverwriteOption = new Option<bool>("--overwrite", () => false, "Overwrite if output exists");
            genVtxmCommand.AddOption(genOutOption);
            genVtxmCommand.AddOption(genWidthOption);
            genVtxmCommand.AddOption(genHeightOption);
            genVtxmCommand.AddOption(genChunkSizeOption);
            genVtxmCommand.AddOption(genPresetOption);
            genVtxmCommand.AddOption(genOverwriteOption);
            genVtxmCommand.SetHandler((InvocationContext ctx) =>
            {
                var outFile = ctx.ParseResult.GetValueForOption(genOutOption);
                var w = ctx.ParseResult.GetValueForOption(genWidthOption);
                var h = ctx.ParseResult.GetValueForOption(genHeightOption);
                var chunkSize = ctx.ParseResult.GetValueForOption(genChunkSizeOption);
                var presetRaw = ctx.ParseResult.GetValueForOption(genPresetOption);
                var overwrite = ctx.ParseResult.GetValueForOption(genOverwriteOption);

                if (!Enum.TryParse<MapVtxmGenerator.Preset>(presetRaw, ignoreCase: true, out var preset))
                {
                    Console.WriteLine($"Unknown preset: {presetRaw}");
                    ctx.ExitCode = 2;
                    return;
                }

                MapVtxmGenerator.GenerateV2(outFile, w, h, chunkSize, preset, overwrite);
                var info = new FileInfo(Path.GetFullPath(outFile));
                Console.WriteLine($"Wrote: {info.FullName} ({info.Length} bytes)");
                ctx.ExitCode = 0;
            });
            mapCommand.AddCommand(genVtxmCommand);

            var genReactBinCommand = new Command("gen-reactbin", "Generate a React editor map_data.bin test file");
            var reactOutOption = new Option<string>("--out", "Output .bin file path") { IsRequired = true };
            var reactWidthOption = new Option<int>("--widthChunks", () => 16, "Map width in chunks");
            var reactHeightOption = new Option<int>("--heightChunks", () => 16, "Map height in chunks");
            var reactPresetOption = new Option<string>("--preset", () => "flat", "Preset: flat|stripes|cliffs|lake");
            var reactOverwriteOption = new Option<bool>("--overwrite", () => false, "Overwrite if output exists");
            genReactBinCommand.AddOption(reactOutOption);
            genReactBinCommand.AddOption(reactWidthOption);
            genReactBinCommand.AddOption(reactHeightOption);
            genReactBinCommand.AddOption(reactPresetOption);
            genReactBinCommand.AddOption(reactOverwriteOption);
            genReactBinCommand.SetHandler((InvocationContext ctx) =>
            {
                var outFile = ctx.ParseResult.GetValueForOption(reactOutOption);
                var w = ctx.ParseResult.GetValueForOption(reactWidthOption);
                var h = ctx.ParseResult.GetValueForOption(reactHeightOption);
                var preset = ctx.ParseResult.GetValueForOption(reactPresetOption);
                var overwrite = ctx.ParseResult.GetValueForOption(reactOverwriteOption);
                GenerateReactMapDataBin(outFile, w, h, preset, overwrite);
                var info = new FileInfo(Path.GetFullPath(outFile));
                Console.WriteLine($"Wrote: {info.FullName} ({info.Length} bytes)");
                ctx.ExitCode = 0;
            });
            mapCommand.AddCommand(genReactBinCommand);
            rootCommand.AddCommand(mapCommand);

            var navCommand = new Command("nav", "Navigation utilities");
            var bakeNavCommand = new Command("bake", "Bake NavTiles from VertexMap .vtxm");
            var navInOption = new Option<string>("--in", "Input .vtxm path") { IsRequired = true };
            var navOutDirOption = new Option<string?>("--outDir", () => null, "Output directory (default: assets/Data/Nav)");
            var navHeightScaleOption = new Option<float>("--heightScale", () => 2.0f, "Height scale in meters per height unit");
            var navMinUpDotOption = new Option<float>("--minUpDot", () => 0.6f, "Triangle walkability threshold by normal.Y");
            var navCliffThresholdOption = new Option<int>("--cliffThreshold", () => 1, "Max height delta allowed for non-ramp base triangles");
            var navArtifactOption = new Option<bool>("--artifact", () => true, "Write BakeArtifact json for each tile");
            var navParallelOption = new Option<bool>("--parallel", () => true, "Bake tiles in parallel");
            var navMaxDegreeOption = new Option<int>("--maxDegree", () => Math.Max(1, Environment.ProcessorCount), "Max degree of parallelism");
            var navTileVersionOption = new Option<int>("--tileVersion", () => 1, "TileVersion written into each NavTile");
            bakeNavCommand.AddOption(navInOption);
            bakeNavCommand.AddOption(navOutDirOption);
            bakeNavCommand.AddOption(navHeightScaleOption);
            bakeNavCommand.AddOption(navMinUpDotOption);
            bakeNavCommand.AddOption(navCliffThresholdOption);
            bakeNavCommand.AddOption(navArtifactOption);
            bakeNavCommand.AddOption(navParallelOption);
            bakeNavCommand.AddOption(navMaxDegreeOption);
            bakeNavCommand.AddOption(navTileVersionOption);
            bakeNavCommand.SetHandler((InvocationContext ctx) =>
            {
                var inputPath = ctx.ParseResult.GetValueForOption(navInOption);
                var outDir = ctx.ParseResult.GetValueForOption(navOutDirOption);
                var heightScale = ctx.ParseResult.GetValueForOption(navHeightScaleOption);
                var minUpDot = ctx.ParseResult.GetValueForOption(navMinUpDotOption);
                var cliffThreshold = ctx.ParseResult.GetValueForOption(navCliffThresholdOption);
                var writeArtifact = ctx.ParseResult.GetValueForOption(navArtifactOption);
                var parallel = ctx.ParseResult.GetValueForOption(navParallelOption);
                var maxDegree = ctx.ParseResult.GetValueForOption(navMaxDegreeOption);
                var tileVersion = ctx.ParseResult.GetValueForOption(navTileVersionOption);
                ctx.ExitCode = BakeNav(inputPath, outDir, heightScale, minUpDot, cliffThreshold, writeArtifact, parallel, maxDegree, tileVersion);
            });
            navCommand.AddCommand(bakeNavCommand);

            var bakeReactNavCommand = new Command("bake-react", "Bake NavTiles from React editor map_data.bin");
            var reactInOption = new Option<string>("--in", "Input React map_data.bin path") { IsRequired = true };
            var reactDirtyOption = new Option<string?>("--dirty", () => null, "Optional dirty chunk list json (array of \"cx,cy\")");
            var reactIncludeNeighborsOption = new Option<bool>("--includeNeighbors", () => true, "Include 4-neighbor tiles for dirty list");
            bakeReactNavCommand.AddOption(reactInOption);
            bakeReactNavCommand.AddOption(reactDirtyOption);
            bakeReactNavCommand.AddOption(navOutDirOption);
            bakeReactNavCommand.AddOption(navHeightScaleOption);
            bakeReactNavCommand.AddOption(navMinUpDotOption);
            bakeReactNavCommand.AddOption(navCliffThresholdOption);
            bakeReactNavCommand.AddOption(navArtifactOption);
            bakeReactNavCommand.AddOption(navParallelOption);
            bakeReactNavCommand.AddOption(navMaxDegreeOption);
            bakeReactNavCommand.AddOption(navTileVersionOption);
            bakeReactNavCommand.AddOption(reactIncludeNeighborsOption);
            bakeReactNavCommand.SetHandler((InvocationContext ctx) =>
            {
                var inputPath = ctx.ParseResult.GetValueForOption(reactInOption);
                var dirtyPath = ctx.ParseResult.GetValueForOption(reactDirtyOption);
                var includeNeighbors = ctx.ParseResult.GetValueForOption(reactIncludeNeighborsOption);
                var outDir = ctx.ParseResult.GetValueForOption(navOutDirOption);
                var heightScale = ctx.ParseResult.GetValueForOption(navHeightScaleOption);
                var minUpDot = ctx.ParseResult.GetValueForOption(navMinUpDotOption);
                var cliffThreshold = ctx.ParseResult.GetValueForOption(navCliffThresholdOption);
                var writeArtifact = ctx.ParseResult.GetValueForOption(navArtifactOption);
                var parallel = ctx.ParseResult.GetValueForOption(navParallelOption);
                var maxDegree = ctx.ParseResult.GetValueForOption(navMaxDegreeOption);
                var tileVersion = ctx.ParseResult.GetValueForOption(navTileVersionOption);
                ctx.ExitCode = BakeNavFromReact(inputPath, dirtyPath, includeNeighbors, outDir, heightScale, minUpDot, cliffThreshold, writeArtifact, parallel, maxDegree, tileVersion);
            });
            navCommand.AddCommand(bakeReactNavCommand);

            var bakeRecastReactNavCommand = new Command("bake-recast-react", "Bake NavTiles from React editor map_data.bin using Recast");
            var mapIdOption = new Option<string>("--mapId", "Target mapId (used for output paths)") { IsRequired = true };
            bakeRecastReactNavCommand.AddOption(mapIdOption);
            bakeRecastReactNavCommand.AddOption(reactInOption);
            bakeRecastReactNavCommand.AddOption(reactDirtyOption);
            bakeRecastReactNavCommand.AddOption(reactIncludeNeighborsOption);
            bakeRecastReactNavCommand.AddOption(navOutDirOption);
            bakeRecastReactNavCommand.AddOption(navHeightScaleOption);
            bakeRecastReactNavCommand.AddOption(navMinUpDotOption);
            bakeRecastReactNavCommand.AddOption(navCliffThresholdOption);
            bakeRecastReactNavCommand.AddOption(navArtifactOption);
            bakeRecastReactNavCommand.AddOption(navParallelOption);
            bakeRecastReactNavCommand.AddOption(navMaxDegreeOption);
            bakeRecastReactNavCommand.AddOption(navTileVersionOption);
            bakeRecastReactNavCommand.SetHandler((InvocationContext ctx) =>
            {
                var mapId = ctx.ParseResult.GetValueForOption(mapIdOption);
                var inputPath = ctx.ParseResult.GetValueForOption(reactInOption);
                var dirtyPath = ctx.ParseResult.GetValueForOption(reactDirtyOption);
                var includeNeighbors = ctx.ParseResult.GetValueForOption(reactIncludeNeighborsOption);
                var outDir = ctx.ParseResult.GetValueForOption(navOutDirOption);
                var heightScale = ctx.ParseResult.GetValueForOption(navHeightScaleOption);
                var minUpDot = ctx.ParseResult.GetValueForOption(navMinUpDotOption);
                var cliffThreshold = ctx.ParseResult.GetValueForOption(navCliffThresholdOption);
                var writeArtifact = ctx.ParseResult.GetValueForOption(navArtifactOption);
                var parallel = ctx.ParseResult.GetValueForOption(navParallelOption);
                var maxDegree = ctx.ParseResult.GetValueForOption(navMaxDegreeOption);
                var tileVersion = ctx.ParseResult.GetValueForOption(navTileVersionOption);
                ctx.ExitCode = BakeNavFromReactRecast(mapId, inputPath, dirtyPath, includeNeighbors, outDir, heightScale, minUpDot, cliffThreshold, writeArtifact, parallel, maxDegree, tileVersion);
            });
            navCommand.AddCommand(bakeRecastReactNavCommand);
            rootCommand.AddCommand(navCommand);

            return await rootCommand.InvokeAsync(args);
        }

        static void InitMod(string modId)
        {
            Console.WriteLine($"Initializing mod '{modId}'...");
            
            // Assuming we are in root or src/Mods?
            // Let's enforce a standard structure: src/Mods/{ModId}
            
            // Try to find src/Mods from current directory up to root
            var modsRoot = FindModsRoot();
            if (modsRoot == null)
            {
                 // Fallback: Create in current directory
                 modsRoot = Directory.GetCurrentDirectory();
                 Console.WriteLine($"Warning: Could not find 'src/Mods' in hierarchy. Creating in current directory: {modsRoot}");
            }
            
            var modDir = Path.Combine(modsRoot, modId);
            if (Directory.Exists(modDir))
            {
                Console.WriteLine($"Error: Directory '{modDir}' already exists.");
                return;
            }
            
            Directory.CreateDirectory(modDir);
            Directory.CreateDirectory(Path.Combine(modDir, "assets"));
            Directory.CreateDirectory(Path.Combine(modDir, "assets", "maps"));
            
            // Create mod.json using canonical schema shared with runtime loader.
            var manifest = new ModManifest
            {
                Name = modId,
                Version = "1.0.0",
                Description = "A new Ludots mod.",
                Main = $"bin/Release/net8.0/{modId}.dll",
                Priority = 0,
                Dependencies = new Dictionary<string, string>()
            };
            var jsonContent = ModManifestJson.ToCanonicalJson(manifest);
            File.WriteAllText(Path.Combine(modDir, "mod.json"), jsonContent);
            
            // Create .csproj
            // We need relative path to Ludots.Core
            // If we are in src/Mods/{ModId}, Core is at ../../Core/Ludots.Core.csproj
            var csprojContent = $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <OutputPath>..\..\..\assets\Mods\{modId}\bin</OutputPath>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include=""..\..\Core\Ludots.Core.csproj"">
        <Private>false</Private>
    </ProjectReference>
  </ItemGroup>
  
  <Target Name=""PostBuild"" AfterTargets=""PostBuildEvent"">
    <Copy SourceFiles=""mod.json"" DestinationFolder=""..\..\..\assets\Mods\{modId}"" />
    <ItemGroup>
        <AssetFiles Include=""assets\**\*.*"" />
    </ItemGroup>
    <Copy SourceFiles=""@(AssetFiles)"" DestinationFolder=""..\..\..\assets\Mods\{modId}\assets\%(RecursiveDir)"" />
  </Target>
</Project>";
            File.WriteAllText(Path.Combine(modDir, $"{modId}.csproj"), csprojContent);
            
            // Create Entry Class
            var classContent = $@"using System;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace {modId}
{{
    public class {modId}Entry : IMod
    {{
        public void OnLoad(IModContext context)
        {{
            context.Log(""{modId} Loaded!"");
        }}

        public void OnUnload()
        {{
            // Cleanup
        }}
    }}
}}";
            File.WriteAllText(Path.Combine(modDir, $"{modId}Entry.cs"), classContent);
            
            Console.WriteLine($"Mod '{modId}' initialized at {modDir}");
        }

        static void BuildMod(string modId)
        {
            Console.WriteLine($"Building mod '{modId}'...");
            
            var modsRoot = FindModsRoot();
            if (modsRoot == null)
            {
                Console.WriteLine("Error: Could not find 'src/Mods' directory.");
                return;
            }
            
            var modDir = Path.Combine(modsRoot, modId);
            var csprojPath = Path.Combine(modDir, $"{modId}.csproj");
            
            if (!File.Exists(csprojPath))
            {
                Console.WriteLine($"Error: Project file not found at {csprojPath}");
                return;
            }
            
            // Run dotnet build
            var process = System.Diagnostics.Process.Start("dotnet", $"build \"{csprojPath}\"");
            process.WaitForExit();
            
            if (process.ExitCode == 0)
            {
                Console.WriteLine($"Build success! Output deployed to assets/Mods/{modId}");
            }
            else
            {
                Console.WriteLine("Build failed.");
            }
        }

        static int CompileGraphs(string modId, string? assetsRoot)
        {
            assetsRoot ??= FindAssetsRoot();
            if (assetsRoot == null)
            {
                Console.WriteLine("Error: Could not determine assets root.");
                return 1;
            }

            var modDir = Path.Combine(assetsRoot, "assets", "Mods", modId);
            if (!Directory.Exists(modDir))
            {
                var srcModDir = Path.Combine(assetsRoot, "src", "Mods", modId);
                if (Directory.Exists(srcModDir))
                {
                    modDir = srcModDir;
                }
                else
                {
                    Console.WriteLine($"Error: Mod directory not found at {modDir}");
                    return 1;
                }
            }

            var graphsJsonPath = Path.Combine(modDir, "assets", "Configs", "GAS", "graphs.json");
            if (!File.Exists(graphsJsonPath))
            {
                Console.WriteLine($"No graphs.json found for mod '{modId}'.");
                return 0;
            }

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, IncludeFields = true };
            List<GraphConfig>? configs;
            using (var fs = File.OpenRead(graphsJsonPath))
            {
                configs = JsonSerializer.Deserialize<List<GraphConfig>>(fs, options);
            }

            if (configs == null || configs.Count == 0)
            {
                Console.WriteLine($"No graph entries found in {graphsJsonPath}");
                return 1;
            }

            configs.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Id, b.Id));

            var packages = new List<GraphProgramPackage>(configs.Count);
            bool hasErrors = false;

            for (int idx = 0; idx < configs.Count; idx++)
            {
                var cfg = configs[idx];
                var (pkg, diags) = GraphCompiler.Compile(cfg);
                for (int d = 0; d < diags.Count; d++)
                {
                    var diag = diags[d];
                    Console.WriteLine($"{diag.Severity} {diag.Code} graph='{diag.GraphId}' node='{diag.NodeId}': {diag.Message}");
                    if (diag.Severity == GraphDiagnosticSeverity.Error) hasErrors = true;
                }

                if (pkg.HasValue)
                {
                    packages.Add(pkg.Value);
                }
            }

            if (hasErrors)
            {
                Console.WriteLine("Graph compilation failed.");
                return 1;
            }

            var outDir = Path.Combine(modDir, "assets", "Compiled", "GAS");
            Directory.CreateDirectory(outDir);
            var outPath = Path.Combine(outDir, "graphs.bin");

            using (var fs = File.Create(outPath))
            {
                GraphProgramBlob.Write(fs, packages);
            }

            Console.WriteLine($"Compiled {packages.Count} graphs to {outPath}");
            return 0;
        }

        static int ImportReactMap(string inputPath, string? outDir, string? name, bool force)
        {
            if (string.IsNullOrWhiteSpace(inputPath) || !File.Exists(inputPath))
            {
                Console.WriteLine($"Error: Input file not found: {inputPath}");
                return 1;
            }

            string assetsRoot = FindAssetsRoot();
            outDir ??= Path.Combine(assetsRoot, "assets", "Data", "Maps");
            Directory.CreateDirectory(outDir);

            name ??= Path.GetFileNameWithoutExtension(inputPath);
            if (string.IsNullOrWhiteSpace(name)) name = "map";

            string outBin = Path.Combine(outDir, $"{name}.vertexmap.bin");
            string outJson = Path.Combine(outDir, $"{name}.vertexmap.summary.json");
            if (!force && (File.Exists(outBin) || File.Exists(outJson)))
            {
                Console.WriteLine($"Error: Output exists. Use --force to overwrite.\n  {outBin}\n  {outJson}");
                return 1;
            }

            var summary = ReactMapDataBinConverter.ConvertToVertexMapBinary(inputPath, outBin);
            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(outJson, JsonSerializer.Serialize(summary, jsonOptions));

            Console.WriteLine($"Converted React map to VertexMap binary:\n  In : {inputPath}\n  Out: {outBin}\n  Info: {outJson}");
            return 0;
        }

        static string FindModsRoot()
        {
            var current = Directory.GetCurrentDirectory();
            while (current != null)
            {
                var check = Path.Combine(current, "src", "Mods");
                if (Directory.Exists(check)) return check;
                
                // Also check if we are IN src
                if (Path.GetFileName(current) == "src" && Directory.Exists(Path.Combine(current, "Mods")))
                    return Path.Combine(current, "Mods");

                current = Directory.GetParent(current)?.FullName;
            }
            return null;
        }

        static string FindAssetsRoot()
        {
            var current = Directory.GetCurrentDirectory();
            while (current != null)
            {
                if (Directory.Exists(Path.Combine(current, "assets"))) return current;
                current = Directory.GetParent(current)?.FullName;
            }
            return Directory.GetCurrentDirectory();
        }

        static int BakeNav(string inputVtxmPath, string? outDir, float heightScale, float minUpDot, int cliffThreshold, bool writeArtifact, bool parallel, int maxDegree, int tileVersion)
        {
            if (!File.Exists(inputVtxmPath))
            {
                Console.WriteLine($"Input not found: {inputVtxmPath}");
                return 2;
            }

            string root = outDir ?? Path.Combine("assets", "Data", "Nav");
            string tilesDir = Path.Combine(root, "navtiles");
            string artifactsDir = Path.Combine(root, "artifacts");
            Directory.CreateDirectory(tilesDir);
            if (writeArtifact) Directory.CreateDirectory(artifactsDir);

            VertexMap map;
            using (var fs = File.OpenRead(inputVtxmPath))
            {
                map = VertexMapBinary.Read(fs);
            }

            var cfg = new NavBuildConfig(heightScale, minUpDot, cliffThreshold);
            ulong cfgHash = cfg.ComputeHash();
            Console.WriteLine($"BakeNav: map {map.WidthInChunks}x{map.HeightInChunks} chunks, configHash={cfgHash}");

            var targets = new List<(int cx, int cy)>(map.WidthInChunks * map.HeightInChunks);
            for (int cy = 0; cy < map.HeightInChunks; cy++)
                for (int cx = 0; cx < map.WidthInChunks; cx++)
                    targets.Add((cx, cy));

            return BakeTiles(map, targets, cfg, tilesDir, artifactsDir, writeArtifact, parallel, maxDegree, tileVersion, logPrefix: "BakeNav", outDirRoot: root);
        }

        static int BakeNavFromReact(string inputReactBinPath, string? dirtyChunksPath, bool includeNeighbors, string? outDir, float heightScale, float minUpDot, int cliffThreshold, bool writeArtifact, bool parallel, int maxDegree, int tileVersion)
        {
            if (!File.Exists(inputReactBinPath))
            {
                Console.WriteLine($"Input not found: {inputReactBinPath}");
                return 2;
            }

            string root = outDir ?? Path.Combine("assets", "Data", "Nav");
            string tilesDir = Path.Combine(root, "navtiles");
            string artifactsDir = Path.Combine(root, "artifacts");
            Directory.CreateDirectory(tilesDir);
            if (writeArtifact) Directory.CreateDirectory(artifactsDir);

            VertexMap map;
            using (var ms = new MemoryStream())
            {
                _ = ReactMapDataBinConverter.ConvertToVertexMapBinary(inputReactBinPath, ms);
                ms.Position = 0;
                map = VertexMapBinary.Read(ms);
            }

            var cfg = new NavBuildConfig(heightScale, minUpDot, cliffThreshold);
            ulong cfgHash = cfg.ComputeHash();
            Console.WriteLine($"BakeNavReact: map {map.WidthInChunks}x{map.HeightInChunks} chunks, configHash={cfgHash}");

            var targets = new List<(int cx, int cy)>();

            if (!string.IsNullOrWhiteSpace(dirtyChunksPath) && File.Exists(dirtyChunksPath))
            {
                var json = File.ReadAllText(dirtyChunksPath);
                var keys = JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>();
                var set = new HashSet<(int cx, int cy)>();
                for (int i = 0; i < keys.Length; i++)
                {
                    var parts = keys[i].Split(',');
                    if (parts.Length != 2) continue;
                    if (!int.TryParse(parts[0], out int cx)) continue;
                    if (!int.TryParse(parts[1], out int cy)) continue;
                    set.Add((cx, cy));
                    if (includeNeighbors)
                    {
                        set.Add((cx - 1, cy));
                        set.Add((cx + 1, cy));
                        set.Add((cx, cy - 1));
                        set.Add((cx, cy + 1));
                    }
                }

                foreach (var t in set)
                {
                    if (t.cx < 0 || t.cy < 0 || t.cx >= map.WidthInChunks || t.cy >= map.HeightInChunks) continue;
                    targets.Add(t);
                }
            }
            else
            {
                for (int cy = 0; cy < map.HeightInChunks; cy++)
                    for (int cx = 0; cx < map.WidthInChunks; cx++)
                        targets.Add((cx, cy));
            }

            return BakeTiles(map, targets, cfg, tilesDir, artifactsDir, writeArtifact, parallel, maxDegree, tileVersion, logPrefix: "BakeNavReact", outDirRoot: root);
        }

        static int BakeNavFromReactRecast(string mapId, string inputReactBinPath, string? dirtyChunksPath, bool includeNeighbors, string? outDir, float heightScale, float minUpDot, int cliffThreshold, bool writeArtifact, bool parallel, int maxDegree, int tileVersion)
        {
            if (string.IsNullOrWhiteSpace(mapId))
            {
                Console.WriteLine("mapId is required.");
                return 2;
            }
            if (!File.Exists(inputReactBinPath))
            {
                Console.WriteLine($"Input not found: {inputReactBinPath}");
                return 2;
            }

            string repoRoot = string.IsNullOrWhiteSpace(outDir) ? FindAssetsRoot() : Path.GetFullPath(outDir);
            if (!Directory.Exists(Path.Combine(repoRoot, "assets")))
            {
                Console.WriteLine($"Invalid repo root (missing assets/): {repoRoot}");
                return 2;
            }
            string bakeCfgPath = Path.Combine(repoRoot, NavMeshConfigPaths.BakeConfigPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(bakeCfgPath))
            {
                Console.WriteLine($"Missing bake config: {bakeCfgPath}");
                return 2;
            }

            var bakeConfig = JsonSerializer.Deserialize<NavMeshBakeConfig>(File.ReadAllText(bakeCfgPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new NavMeshBakeConfig();
            if (bakeConfig.Profiles == null || bakeConfig.Profiles.Count == 0) throw new InvalidOperationException("NavMeshBakeConfig.profiles is empty.");
            var profiles = bakeConfig.Profiles;
            if (bakeConfig.Layers == null || bakeConfig.Layers.Count == 0)
            {
                bakeConfig.Layers = new List<NavLayerConfig> { new NavLayerConfig { Id = "Ground", Layer = 0 } };
            }

            NavObstacleSet obstacles = new NavObstacleSet();
            string obsRel = NavAssetPaths.GetObstacleRelativePath(mapId);
            string obsPath = Path.Combine(repoRoot, obsRel.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(obsPath))
            {
                obstacles = JsonSerializer.Deserialize<NavObstacleSet>(File.ReadAllText(obsPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new NavObstacleSet();
            }

            VertexMap map;
            using (var ms = new MemoryStream())
            {
                _ = ReactMapDataBinConverter.ConvertToVertexMapBinary(inputReactBinPath, ms);
                ms.Position = 0;
                map = VertexMapBinary.Read(ms);
            }

            var targets = new List<(int cx, int cy)>();
            if (!string.IsNullOrWhiteSpace(dirtyChunksPath) && File.Exists(dirtyChunksPath))
            {
                var json = File.ReadAllText(dirtyChunksPath);
                var keys = JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>();
                var set = new HashSet<(int cx, int cy)>();
                for (int i = 0; i < keys.Length; i++)
                {
                    var parts = keys[i].Split(',');
                    if (parts.Length != 2) continue;
                    if (!int.TryParse(parts[0], out int cx)) continue;
                    if (!int.TryParse(parts[1], out int cy)) continue;
                    set.Add((cx, cy));
                    if (includeNeighbors)
                    {
                        set.Add((cx - 1, cy));
                        set.Add((cx + 1, cy));
                        set.Add((cx, cy - 1));
                        set.Add((cx, cy + 1));
                    }
                }
                foreach (var t in set)
                {
                    if (t.cx < 0 || t.cy < 0 || t.cx >= map.WidthInChunks || t.cy >= map.HeightInChunks) continue;
                    targets.Add(t);
                }
            }
            else
            {
                for (int cy = 0; cy < map.HeightInChunks; cy++)
                    for (int cx = 0; cx < map.WidthInChunks; cx++)
                        targets.Add((cx, cy));
            }

            var legacyCfg = new NavBuildConfig(heightScale, minUpDot, cliffThreshold);
            Console.WriteLine($"BakeNavRecastReact: mapId={mapId} map {map.WidthInChunks}x{map.HeightInChunks} chunks");

            int ok = 0;
            int fail = 0;
            var consoleLock = new object();

            void BakeOne((int cx, int cy) t)
            {
                for (int li = 0; li < bakeConfig.Layers.Count; li++)
                {
                    int layer = bakeConfig.Layers[li].Layer;
                    for (int pi = 0; pi < profiles.Count; pi++)
                    {
                        if (RecastNavTileBaker.TryBake(map, t.cx, t.cy, (uint)tileVersion, legacyCfg, profiles[pi], layer, obstacles, out var tile, out var artifact))
                        {
                            string profileId = profiles[pi].Id ?? throw new InvalidOperationException("NavMeshBakeConfig.profiles.id is required.");
                            string rel = NavAssetPaths.GetNavTileRelativePath(mapId, layer, profileId, t.cx, t.cy);
                            string outFile = Path.Combine(repoRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                            Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);
                            using (var fs = File.Create(outFile))
                            {
                                NavTileBinary.Write(fs, tile);
                            }

                            Interlocked.Increment(ref ok);

                            if (writeArtifact)
                            {
                                string artRel = rel.Replace("navtile_", "artifact_").Replace(".ntil", ".json");
                                string artFile = Path.Combine(repoRoot, artRel.Replace('/', Path.DirectorySeparatorChar));
                                var json = JsonSerializer.Serialize(artifact, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true });
                                File.WriteAllText(artFile, json);
                            }
                        }
                        else
                        {
                            Interlocked.Increment(ref fail);
                            lock (consoleLock)
                            {
                                Console.WriteLine($"BakeNavRecastReact failed: tile {t.cx},{t.cy} layer={layer} profile={pi} stage={artifact.Stage} code={artifact.ErrorCode} msg={artifact.Message}");
                            }
                        }
                    }
                }
            }

            if (parallel)
            {
                Parallel.ForEach(targets, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, maxDegree) }, BakeOne);
            }
            else
            {
                for (int i = 0; i < targets.Count; i++) BakeOne(targets[i]);
            }

            Console.WriteLine($"BakeNavRecastReact done. ok={ok} fail={fail} repoRoot={Path.GetFullPath(repoRoot)}");
            return fail == 0 ? 0 : 1;
        }

        static int BakeTiles(VertexMap map, List<(int cx, int cy)> targets, NavBuildConfig cfg, string tilesDir, string artifactsDir, bool writeArtifact, bool parallel, int maxDegree, int tileVersion, string logPrefix, string outDirRoot)
        {
            int ok = 0;
            int fail = 0;
            var consoleLock = new object();

            void BakeOne((int cx, int cy) t)
            {
                if (NavTileBuilder.TryBuildTile(map, t.cx, t.cy, (uint)tileVersion, cfg, out var tile, out var artifact))
                {
                    string outFile = Path.Combine(tilesDir, $"navtile_{t.cx}_{t.cy}.ntil");
                    using (var fs = File.Create(outFile))
                    {
                        NavTileBinary.Write(fs, tile);
                    }

                    Interlocked.Increment(ref ok);

                    if (writeArtifact)
                    {
                        string artFile = Path.Combine(artifactsDir, $"artifact_{t.cx}_{t.cy}.json");
                        var json = JsonSerializer.Serialize(artifact, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true });
                        File.WriteAllText(artFile, json);
                    }
                }
                else
                {
                    Interlocked.Increment(ref fail);
                    lock (consoleLock)
                    {
                        Console.WriteLine($"{logPrefix} failed: tile {t.cx},{t.cy} stage={artifact.Stage} code={artifact.ErrorCode} msg={artifact.Message}");
                    }

                    if (writeArtifact)
                    {
                        string artFile = Path.Combine(artifactsDir, $"artifact_{t.cx}_{t.cy}.json");
                        var json = JsonSerializer.Serialize(artifact, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true });
                        File.WriteAllText(artFile, json);
                    }
                }
            }

            if (parallel)
            {
                Parallel.ForEach(targets, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, maxDegree) }, BakeOne);
            }
            else
            {
                for (int i = 0; i < targets.Count; i++) BakeOne(targets[i]);
            }

            Console.WriteLine($"{logPrefix} done. ok={ok} fail={fail} outDir={Path.GetFullPath(outDirRoot)}");
            return fail == 0 ? 0 : 1;
        }

        static void GenerateReactMapDataBin(string outFile, int widthChunks, int heightChunks, string preset, bool overwrite)
        {
            if (File.Exists(outFile) && !overwrite) throw new IOException($"File exists: {outFile}");
            if (widthChunks <= 0 || heightChunks <= 0) throw new ArgumentOutOfRangeException();

            int mapW = widthChunks * 64;
            int mapH = heightChunks * 64;

            using var fs = File.Create(outFile);
            using var bw = new BinaryWriter(fs);
            bw.Write(widthChunks);
            bw.Write(heightChunks);
            bw.Write((byte)2);

            for (int cy = 0; cy < heightChunks; cy++)
            {
                for (int cx = 0; cx < widthChunks; cx++)
                {
                    var chunk = new byte[64 * 64 * 4];
                    for (int ly = 0; ly < 64; ly++)
                    {
                        for (int lx = 0; lx < 64; lx++)
                        {
                            int gc = cx * 64 + lx;
                            int gr = cy * 64 + ly;

                            byte height = 0;
                            byte water = 0;
                            byte biome = 0;
                            byte veg = 0;
                            byte flags = 0;
                            byte territory = 0;

                            if (string.Equals(preset, "stripes", StringComparison.OrdinalIgnoreCase))
                            {
                                height = (byte)(((gc / 4) & 1) == 0 ? 2 : 10);
                            }
                            else if (string.Equals(preset, "cliffs", StringComparison.OrdinalIgnoreCase))
                            {
                                height = (byte)(gc < mapW / 2 ? 2 : 12);
                            }
                            else if (string.Equals(preset, "lake", StringComparison.OrdinalIgnoreCase))
                            {
                                height = 2;
                                int cxm = mapW / 2;
                                int cym = mapH / 2;
                                int dx = gc - cxm;
                                int dy = gr - cym;
                                int d2 = dx * dx + dy * dy;
                                if (d2 < (mapW / 6) * (mapW / 6)) water = 10;
                            }

                            int cell = (ly * 64 + lx) * 4;
                            chunk[cell + 0] = (byte)(((height & 0x0F) << 4) | (water & 0x0F));
                            chunk[cell + 1] = (byte)(((biome & 0x0F) << 4) | (veg & 0x0F));
                            chunk[cell + 2] = flags;
                            chunk[cell + 3] = territory;
                        }
                    }
                    bw.Write(chunk);
                }
            }
        }
    }
}
