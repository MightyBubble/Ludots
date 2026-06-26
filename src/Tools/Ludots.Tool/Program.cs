using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ludots.Core.Config;
using Ludots.Core.Map.Board;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph;
using GraphProgramBlob = Ludots.Core.GraphRuntime.GraphProgramBlob;
using GraphProgramPackage = Ludots.Core.GraphRuntime.GraphProgramPackage;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Map.Fields;
using Ludots.Core.Physics2D.Navigation;
using Ludots.Core.Spatial;
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
            var dirOption = new Option<string>("--dir", "Directory to create the mod in (default: mods/)");
            var templateOption = new Option<string>("--template", () => "empty", "Template: empty, gameplay");
            initCommand.AddOption(modIdOption);
            initCommand.AddOption(dirOption);
            initCommand.AddOption(templateOption);
            initCommand.SetHandler((InvocationContext ctx) =>
            {
                var id = ctx.ParseResult.GetValueForOption(modIdOption);
                var dir = ctx.ParseResult.GetValueForOption(dirOption);
                var template = ctx.ParseResult.GetValueForOption(templateOption) ?? "empty";
                InitMod(id, dir, template);
            });
            
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
            var graphModOption = new Option<string?>("--mod", () => null, "The mod ID to compile graphs for");
            var graphModPathOption = new Option<string?>("--modPath", () => null, "The full mod root path to compile graphs for");
            var assetsRootOption = new Option<string?>("--assetsRoot", () => null, "Assets root (repo root containing 'assets/')");
            compileGraphsCommand.AddOption(graphModOption);
            compileGraphsCommand.AddOption(graphModPathOption);
            compileGraphsCommand.AddOption(assetsRootOption);
            compileGraphsCommand.SetHandler((InvocationContext ctx) =>
            {
                var mod = ctx.ParseResult.GetValueForOption(graphModOption);
                var modPath = ctx.ParseResult.GetValueForOption(graphModPathOption);
                var assetsRoot = ctx.ParseResult.GetValueForOption(assetsRootOption);
                ctx.ExitCode = CompileGraphs(mod, modPath, assetsRoot);
            });
            graphCommand.AddCommand(compileGraphsCommand);
            rootCommand.AddCommand(graphCommand);

            var mapCommand = new Command("map", "Map utilities");
            var importReactCommand = new Command("import-react", "Explicit legacy import: convert React/Grid .bin terrain to .ltrn");
            var inputBinOption = new Option<string>("--in", "Input legacy React/Grid .bin path") { IsRequired = true };
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

            var importVtxmCommand = new Command("import-vtxm", "Explicit legacy import: convert VertexMap .vtxm terrain to .ltrn");
            var inputVtxmOption = new Option<string>("--in", "Input legacy VertexMap .vtxm path") { IsRequired = true };
            var vtxmOutDirOption = new Option<string?>("--outDir", () => null, "Output directory (default: assets/Data/Maps)");
            var vtxmNameOption = new Option<string?>("--name", () => null, "Output base name (default: input filename)");
            var vtxmForceOption = new Option<bool>("--force", () => false, "Overwrite output files if exist");
            importVtxmCommand.AddOption(inputVtxmOption);
            importVtxmCommand.AddOption(vtxmOutDirOption);
            importVtxmCommand.AddOption(vtxmNameOption);
            importVtxmCommand.AddOption(vtxmForceOption);
            importVtxmCommand.SetHandler((InvocationContext ctx) =>
            {
                var inputPath = ctx.ParseResult.GetValueForOption(inputVtxmOption);
                var outDir = ctx.ParseResult.GetValueForOption(vtxmOutDirOption);
                var name = ctx.ParseResult.GetValueForOption(vtxmNameOption);
                var force = ctx.ParseResult.GetValueForOption(vtxmForceOption);
                ctx.ExitCode = ImportVertexMap(inputPath, outDir, name, force);
            });
            mapCommand.AddCommand(importVtxmCommand);

            rootCommand.AddCommand(mapCommand);

            var navCommand = new Command("nav", "Navigation utilities");
            var navOutDirOption = new Option<string?>("--outDir", () => null, "Output directory (default: assets/Data/Nav)");
            var navHeightScaleOption = new Option<float>("--heightScale", () => 2.0f, "Height scale in meters per height unit");
            var navMinUpDotOption = new Option<float>("--minUpDot", () => 0.6f, "Triangle walkability threshold by normal.Y");
            var navCliffThresholdOption = new Option<int>("--cliffThreshold", () => 1, "Max height delta allowed for non-ramp base triangles");
            var navArtifactOption = new Option<bool>("--artifact", () => true, "Write BakeArtifact json for each tile");
            var navParallelOption = new Option<bool>("--parallel", () => true, "Bake tiles in parallel");
            var navMaxDegreeOption = new Option<int>("--maxDegree", () => Math.Max(1, Environment.ProcessorCount), "Max degree of parallelism");
            var navLargeBakeOption = new Option<bool>("--large-bake", () => false, "Allow a large nav bake after reviewing the matching estimate");
            var navEstimateHashOption = new Option<string?>("--estimateHash", () => null, "Estimate hash returned by nav estimate-recast-react");
            var reactInOption = new Option<string>("--in", "Input .ltrn LogicTerrain path") { IsRequired = true };
            var reactDirtyOption = new Option<string?>("--dirty", () => null, "Optional dirty chunk list json (array of \"cx,cy\")");
            var reactIncludeNeighborsOption = new Option<bool>("--includeNeighbors", () => true, "Include 4-neighbor tiles for dirty list");

            var bakeRecastReactNavCommand = new Command("bake-recast-react", "Bake NavTiles from .ltrn LogicTerrain using Recast");
            var mapIdOption = new Option<string>("--mapId", "Target mapId (used for output paths)") { IsRequired = true };
            var navModIdOption = new Option<string?>("--modId", () => null, "Optional mod id when mapId is authored by a mod");
            bakeRecastReactNavCommand.AddOption(mapIdOption);
            bakeRecastReactNavCommand.AddOption(navModIdOption);
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
            bakeRecastReactNavCommand.AddOption(navLargeBakeOption);
            bakeRecastReactNavCommand.AddOption(navEstimateHashOption);
            bakeRecastReactNavCommand.SetHandler((InvocationContext ctx) =>
            {
                var mapId = ctx.ParseResult.GetValueForOption(mapIdOption);
                var modId = ctx.ParseResult.GetValueForOption(navModIdOption);
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
                var tileVersion = (int)NavTileBinary.FormatVersion;
                var largeBake = ctx.ParseResult.GetValueForOption(navLargeBakeOption);
                var estimateHash = ctx.ParseResult.GetValueForOption(navEstimateHashOption);
                ctx.ExitCode = BakeNavFromReactRecast(mapId, modId, inputPath, dirtyPath, includeNeighbors, outDir, heightScale, minUpDot, cliffThreshold, writeArtifact, parallel, maxDegree, tileVersion, largeBake, estimateHash);
            });
            navCommand.AddCommand(bakeRecastReactNavCommand);

            var estimateRecastReactNavCommand = new Command("estimate-recast-react", "Estimate Recast NavTile bake cost from .ltrn LogicTerrain");
            estimateRecastReactNavCommand.AddOption(mapIdOption);
            estimateRecastReactNavCommand.AddOption(navModIdOption);
            estimateRecastReactNavCommand.AddOption(reactInOption);
            estimateRecastReactNavCommand.AddOption(reactDirtyOption);
            estimateRecastReactNavCommand.AddOption(reactIncludeNeighborsOption);
            estimateRecastReactNavCommand.AddOption(navOutDirOption);
            estimateRecastReactNavCommand.AddOption(navHeightScaleOption);
            estimateRecastReactNavCommand.AddOption(navMinUpDotOption);
            estimateRecastReactNavCommand.AddOption(navCliffThresholdOption);
            estimateRecastReactNavCommand.AddOption(navParallelOption);
            estimateRecastReactNavCommand.AddOption(navMaxDegreeOption);
            estimateRecastReactNavCommand.SetHandler((InvocationContext ctx) =>
            {
                var mapId = ctx.ParseResult.GetValueForOption(mapIdOption);
                var modId = ctx.ParseResult.GetValueForOption(navModIdOption);
                var inputPath = ctx.ParseResult.GetValueForOption(reactInOption);
                var dirtyPath = ctx.ParseResult.GetValueForOption(reactDirtyOption);
                var includeNeighbors = ctx.ParseResult.GetValueForOption(reactIncludeNeighborsOption);
                var outDir = ctx.ParseResult.GetValueForOption(navOutDirOption);
                var heightScale = ctx.ParseResult.GetValueForOption(navHeightScaleOption);
                var minUpDot = ctx.ParseResult.GetValueForOption(navMinUpDotOption);
                var cliffThreshold = ctx.ParseResult.GetValueForOption(navCliffThresholdOption);
                var parallel = ctx.ParseResult.GetValueForOption(navParallelOption);
                var maxDegree = ctx.ParseResult.GetValueForOption(navMaxDegreeOption);
                var tileVersion = (int)NavTileBinary.FormatVersion;
                ctx.ExitCode = EstimateNavFromReactRecast(mapId, modId, inputPath, dirtyPath, includeNeighbors, outDir, heightScale, minUpDot, cliffThreshold, parallel, maxDegree, tileVersion);
            });
            navCommand.AddCommand(estimateRecastReactNavCommand);
            rootCommand.AddCommand(navCommand);

            return await rootCommand.InvokeAsync(args);
        }

        static void InitMod(string modId, string dir, string template)
        {
            Console.WriteLine($"Initializing mod '{modId}' (template={template})...");

            string modDir;
            if (!string.IsNullOrWhiteSpace(dir))
            {
                modDir = Path.GetFullPath(Path.Combine(dir, modId));
            }
            else
            {
                var modsRoot = FindModsRoot();
                if (modsRoot == null)
                {
                    Console.WriteLine("Error: Could not find 'mods' in hierarchy. Use --dir to specify a target directory.");
                    return;
                }
                modDir = Path.Combine(modsRoot, modId);
            }

            if (Directory.Exists(modDir))
            {
                Console.WriteLine($"Error: Directory '{modDir}' already exists.");
                return;
            }

            bool isGameplay = string.Equals(template, "gameplay", StringComparison.OrdinalIgnoreCase);
            if (!isGameplay && !string.Equals(template, "empty", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"Error: Unknown template '{template}'. Valid values: empty, gameplay");
                return;
            }

            Directory.CreateDirectory(modDir);
            Directory.CreateDirectory(Path.Combine(modDir, "assets"));
            Directory.CreateDirectory(Path.Combine(modDir, "assets", "maps"));
            Directory.CreateDirectory(Path.Combine(modDir, "assets", "Launcher"));

            var manifest = new ModManifest
            {
                Name = modId,
                Version = "1.0.0",
                Description = "A new Ludots mod.",
                Main = $"bin/net8.0/{modId}.dll",
                Priority = 0,
                Dependencies = new Dictionary<string, string>(),
                Changelog = "CHANGELOG.md"
            };

            if (isGameplay)
            {
                manifest.Dependencies["LudotsCoreMod"] = "^1.0.0";
            }

            var jsonContent = ModManifestJson.ToCanonicalJson(manifest);
            File.WriteAllText(Path.Combine(modDir, "mod.json"), jsonContent);

            var changelogContent = $@"# {modId} Changelog

## 1.0.0
- Initial release
";
            File.WriteAllText(Path.Combine(modDir, "CHANGELOG.md"), changelogContent);

            var coreRelPath = Path.GetRelativePath(modDir, Path.Combine(FindAssetsRoot() ?? Directory.GetCurrentDirectory(), "src", "Core", "Ludots.Core.csproj"));
            var csprojContent = $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include=""{coreRelPath}"">
        <Private>false</Private>
    </ProjectReference>
  </ItemGroup>
</Project>";
            File.WriteAllText(Path.Combine(modDir, $"{modId}.csproj"), csprojContent);

            if (isGameplay)
            {
                var mapsDir = Path.Combine(modDir, "assets", "Maps");
                Directory.CreateDirectory(mapsDir);

                var mapConfig = $@"{{
  ""MapId"": ""{modId}_entry"",
  ""DisplayName"": ""{modId} Entry Map"",
  ""Width"": 64,
  ""Height"": 64
}}";
                File.WriteAllText(Path.Combine(mapsDir, $"{modId}_entry.json"), mapConfig);

                var gameJson = $@"{{
  ""StartupMapId"": ""{modId}_entry""
}}";
                File.WriteAllText(Path.Combine(modDir, "assets", "game.json"), gameJson);

                var triggerContent = $@"using Ludots.Core.Modding;
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
        }}
    }}
}}";
                File.WriteAllText(Path.Combine(modDir, $"{modId}Entry.cs"), triggerContent);
            }
            else
            {
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
        }}
    }}
}}";
                File.WriteAllText(Path.Combine(modDir, $"{modId}Entry.cs"), classContent);
            }

            Console.WriteLine($"Mod '{modId}' initialized at {modDir}");
        }

        static void BuildMod(string modId)
        {
            Console.WriteLine($"Building mod '{modId}'...");
            
            var modsRoot = FindModsRoot();
            if (modsRoot == null)
            {
                Console.WriteLine("Error: Could not find 'mods' directory.");
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
                Console.WriteLine($"Build success! Output at mods/{modId}/bin/net8.0");
            }
            else
            {
                Console.WriteLine("Build failed.");
            }
        }

        static int CompileGraphs(string? modId, string? modPath, string? assetsRoot)
        {
            assetsRoot ??= FindAssetsRoot();
            if (assetsRoot == null)
            {
                Console.WriteLine("Error: Could not determine assets root.");
                return 1;
            }

            string modDir;
            if (!string.IsNullOrWhiteSpace(modPath))
            {
                modDir = Path.GetFullPath(modPath);
            }
            else if (!string.IsNullOrWhiteSpace(modId))
            {
                modDir = Path.Combine(assetsRoot, "mods", modId);
            }
            else
            {
                Console.WriteLine("Error: graph compile requires --modPath or --mod.");
                return 1;
            }

            if (!Directory.Exists(modDir))
            {
                Console.WriteLine($"Error: Mod directory not found at {modDir}");
                return 1;
            }

            var graphsJsonPath = Path.Combine(modDir, "assets", "Configs", "GAS", "graphs.json");
            if (!File.Exists(graphsJsonPath))
            {
                Console.WriteLine($"No graphs.json found for mod '{modDir}'.");
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

            if (!string.Equals(Path.GetExtension(inputPath), ".bin", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"Error: import-react only accepts explicit legacy .bin input: {inputPath}");
                return 1;
            }

            string assetsRoot = FindAssetsRoot();
            outDir ??= Path.Combine(assetsRoot, "assets", "Data", "Maps");
            Directory.CreateDirectory(outDir);

            name ??= Path.GetFileNameWithoutExtension(inputPath);
            if (string.IsNullOrWhiteSpace(name)) name = "map";

            string outTerrain = Path.Combine(outDir, $"{name}.ltrn");
            string outJson = Path.Combine(outDir, $"{name}.ltrn.summary.json");
            if (!force && (File.Exists(outTerrain) || File.Exists(outJson)))
            {
                Console.WriteLine($"Error: Output exists. Use --force to overwrite.\n  {outTerrain}\n  {outJson}");
                return 1;
            }

            var summary = ReactMapDataBinConverter.ConvertToLogicTerrainBinary(inputPath, outTerrain);
            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(outJson, JsonSerializer.Serialize(summary, jsonOptions));

            Console.WriteLine($"Imported legacy React/Grid terrain to LogicTerrain binary:\n  In : {inputPath}\n  Out: {outTerrain}\n  Info: {outJson}");
            return 0;
        }

        static int ImportVertexMap(string inputPath, string? outDir, string? name, bool force)
        {
            if (string.IsNullOrWhiteSpace(inputPath) || !File.Exists(inputPath))
            {
                Console.WriteLine($"Error: Input file not found: {inputPath}");
                return 1;
            }

            if (!string.Equals(Path.GetExtension(inputPath), ".vtxm", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"Error: import-vtxm only accepts explicit legacy .vtxm input: {inputPath}");
                return 1;
            }

            string assetsRoot = FindAssetsRoot();
            outDir ??= Path.Combine(assetsRoot, "assets", "Data", "Maps");
            Directory.CreateDirectory(outDir);

            name ??= Path.GetFileNameWithoutExtension(inputPath);
            if (string.IsNullOrWhiteSpace(name)) name = "map";

            string outTerrain = Path.Combine(outDir, $"{name}.ltrn");
            string outJson = Path.Combine(outDir, $"{name}.ltrn.summary.json");
            if (!force && (File.Exists(outTerrain) || File.Exists(outJson)))
            {
                Console.WriteLine($"Error: Output exists. Use --force to overwrite.\n  {outTerrain}\n  {outJson}");
                return 1;
            }

            var summary = ReactMapDataBinConverter.ConvertVertexMapToLogicTerrainBinary(inputPath, outTerrain);
            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(outJson, JsonSerializer.Serialize(summary, jsonOptions));

            Console.WriteLine($"Imported legacy VertexMap terrain to LogicTerrain binary:\n  In : {inputPath}\n  Out: {outTerrain}\n  Info: {outJson}");
            return 0;
        }

        static string FindModsRoot()
        {
            var current = Directory.GetCurrentDirectory();
            while (current != null)
            {
                var check = Path.Combine(current, "mods");
                if (Directory.Exists(check)) return check;

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

        static int BakeNavFromReactRecast(
            string mapId,
            string? modId,
            string inputReactBinPath,
            string? dirtyChunksPath,
            bool includeNeighbors,
            string? outDir,
            float heightScale,
            float minUpDot,
            int cliffThreshold,
            bool writeArtifact,
            bool parallel,
            int maxDegree,
            int tileVersion,
            bool largeBakeApproved,
            string? acceptedEstimateHash)
        {
            try
            {
                NavBakeContext context = BuildReactRecastNavBakeContext(
                    mapId,
                    modId,
                    inputReactBinPath,
                    dirtyChunksPath,
                    includeNeighbors,
                    outDir,
                    heightScale,
                    minUpDot,
                    cliffThreshold,
                    parallel,
                    maxDegree,
                    tileVersion,
                    out string repoRoot,
                    out LogicTerrainField terrain);

                Console.WriteLine($"BakeNavRecastReact: mapId={mapId} modId={modId ?? "(auto)"} topology={terrain.Topology} chunks={terrain.WidthChunks}x{terrain.HeightChunks} obstacles={context.Obstacles.Obstacles.Count}");
                NavBakeEstimateReport estimate = NavBakeEstimator.Estimate(context);
                Console.WriteLine($"BakeNavRecastReact estimate: status={estimate.BudgetStatusText} hash={estimate.EstimateHash} terrainHash={estimate.TerrainContentHash} targets={estimate.TargetTileCount} operations={estimate.BakeOperationCount} workUnits={estimate.BudgetWorkUnitCount} seconds={estimate.EstimatedSecondsLow:F1}-{estimate.EstimatedSecondsHigh:F1}");
                NavBakeEstimator.EnsureBakeAllowed(estimate, largeBakeApproved, acceptedEstimateHash);

                var result = new NavBakeService(new RecastNavBakeAlgorithm(), new CdtNavBakeAlgorithm()).Bake(context);
                if (result.FailureCount > 0)
                {
                    PrintNavBakeFailures(result, "BakeNavRecastReact");
                    Console.WriteLine("BakeNavRecastReact failed; no NavTile artifacts were written.");
                    return 1;
                }

                WriteNavBakeResultToRepository(repoRoot, mapId, result, writeArtifact, "BakeNavRecastReact");
                Console.WriteLine($"BakeNavRecastReact done. ok={result.SuccessCount} fail={result.FailureCount} repoRoot={Path.GetFullPath(repoRoot)}");
                return result.FailureCount == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return 2;
            }
        }

        static int EstimateNavFromReactRecast(string mapId, string? modId, string inputReactBinPath, string? dirtyChunksPath, bool includeNeighbors, string? outDir, float heightScale, float minUpDot, int cliffThreshold, bool parallel, int maxDegree, int tileVersion)
        {
            try
            {
                NavBakeContext context = BuildReactRecastNavBakeContext(
                    mapId,
                    modId,
                    inputReactBinPath,
                    dirtyChunksPath,
                    includeNeighbors,
                    outDir,
                    heightScale,
                    minUpDot,
                    cliffThreshold,
                    parallel,
                    maxDegree,
                    tileVersion,
                    out _,
                    out _);
                NavBakeEstimateReport estimate = NavBakeEstimator.Estimate(context);
                var json = JsonSerializer.Serialize(estimate, new JsonSerializerOptions { WriteIndented = true });
                Console.WriteLine(json);
                return estimate.BudgetStatus == NavBakeBudgetStatus.Reject ? 1 : 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return 2;
            }
        }

        static NavBakeContext BuildReactRecastNavBakeContext(
            string mapId,
            string? modId,
            string inputReactBinPath,
            string? dirtyChunksPath,
            bool includeNeighbors,
            string? outDir,
            float heightScale,
            float minUpDot,
            int cliffThreshold,
            bool parallel,
            int maxDegree,
            int tileVersion,
            out string repoRoot,
            out LogicTerrainField terrain)
        {
            terrain = null!;
            if (string.IsNullOrWhiteSpace(mapId))
            {
                throw new InvalidOperationException("mapId is required.");
            }

            if (!File.Exists(inputReactBinPath))
            {
                throw new InvalidOperationException($"Input not found: {inputReactBinPath}");
            }

            repoRoot = string.IsNullOrWhiteSpace(outDir) ? FindAssetsRoot() : Path.GetFullPath(outDir);
            if (!Directory.Exists(Path.Combine(repoRoot, "assets")))
            {
                throw new InvalidOperationException($"Invalid repo root (missing assets/): {repoRoot}");
            }

            MapConfig mapConfig = ToolMapConfigResolver.LoadMap(repoRoot, mapId, modId);
            BoardConfig boardConfig = ToolMapConfigResolver.ResolvePrimaryNavigationBoard(mapConfig);
            if (boardConfig == null)
            {
                throw new InvalidOperationException($"Map '{mapId}' has no navigation-enabled board.");
            }

            NavMeshBakeConfigContext bakeConfigContext;
            try
            {
                bakeConfigContext = NavMeshBakeConfigLoader.LoadContextFromRepoRoot(repoRoot, modId);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to load navmesh bake config '{NavMeshConfigPaths.BakeConfigPath}': {ex.Message}", ex);
            }

            NavObstacleSet obstacles;
            try
            {
                obstacles = NavObstacleAuthoringCatalog.BuildForMap(repoRoot, mapId, modId);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to build nav obstacles from map authoring for '{mapId}': {ex.Message}", ex);
            }

            terrain = CreateReactEditorLogicTerrain(inputReactBinPath, boardConfig);

            IReadOnlyList<NavBakeTileCoord> targets;
            if (!string.IsNullOrWhiteSpace(dirtyChunksPath))
            {
                if (!File.Exists(dirtyChunksPath))
                {
                    throw new InvalidOperationException($"Dirty chunks file not found: {dirtyChunksPath}");
                }

                var json = File.ReadAllText(dirtyChunksPath);
                targets = NavBakeTileSelection.Resolve(terrain, json, includeNeighbors, dirtyOnly: true);
            }
            else
            {
                targets = NavBakeTileSelection.AllTiles(terrain);
            }

            NavMeshBakeConfig bakeConfig = bakeConfigContext.Config;
            return new NavBakeContext
            {
                MapId = mapId,
                ModId = modId ?? string.Empty,
                SourceUri = ToCoreSourceUri(repoRoot, inputReactBinPath),
                Terrain = terrain,
                Obstacles = obstacles,
                Config = bakeConfig,
                AgentProfiles = bakeConfigContext.AgentProfiles,
                Targets = targets,
                BuildConfig = new NavBuildConfig(heightScale, minUpDot, cliffThreshold),
                TileVersion = (uint)tileVersion,
                Mode = bakeConfig.ParsedMode,
                Algorithm = bakeConfig.ParsedAlgorithm,
                Execution = new NavBakeExecutionOptions
                {
                    Parallel = parallel,
                    MaxDegreeOfParallelism = Math.Max(1, maxDegree)
                }
            };
        }

        static LogicTerrainField CreateReactEditorLogicTerrain(string inputReactBinPath, BoardConfig boardConfig)
        {
            if (boardConfig == null) throw new ArgumentNullException(nameof(boardConfig));
            if (!Path.GetExtension(inputReactBinPath).Equals(".ltrn", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"LogicTerrain bake input must be .ltrn. Use 'map import-react' for explicit one-way legacy .bin import: {inputReactBinPath}");
            }

            string spatialType = (boardConfig.SpatialType ?? "Grid").Trim();
            if (spatialType.Equals("NodeGraph", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Map board '{boardConfig.Name}' is NodeGraph; NodeGraph boards use graph data and do not bake navmesh.");
            }
            if (!spatialType.Equals("Grid", StringComparison.OrdinalIgnoreCase) &&
                !spatialType.Equals("HexGrid", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Map board '{boardConfig.Name}' has unsupported SpatialType '{boardConfig.SpatialType}'. Expected Grid, HexGrid, or NodeGraph.");
            }

            using var input = File.OpenRead(inputReactBinPath);
            return LogicTerrainBinary.Read(input);
        }

        static void WriteNavBakeResultToRepository(string repoRoot, string mapId, NavBakeResult result, bool writeArtifact, string logPrefix)
        {
            if (result.FailureCount > 0)
            {
                PrintNavBakeFailures(result, logPrefix);
                throw new InvalidOperationException($"{logPrefix} refuses to publish partial NavTile output when any bake entry failed.");
            }

            for (int i = 0; i < result.Entries.Count; i++)
            {
                NavBakeResultEntry entry = result.Entries[i];
                if (!entry.Success)
                {
                    Console.WriteLine($"{logPrefix} failed: tile {entry.Target.ChunkX},{entry.Target.ChunkY} layer={entry.Layer} profile={entry.ProfileId} stage={entry.Artifact.Stage} code={entry.Artifact.ErrorCode} msg={entry.Artifact.Message}");
                    continue;
                }

                string rel = NavAssetPaths.GetNavTileRelativePath(mapId, entry.Layer, entry.ProfileId, entry.Target.ChunkX, entry.Target.ChunkY);
                string outFile = Path.Combine(repoRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);
                using (var fs = File.Create(outFile))
                {
                    NavTileBinary.Write(fs, entry.Tile);
                }

                if (writeArtifact)
                {
                    string artRel = rel.Replace("navtile_", "artifact_").Replace(".ntil", ".json");
                    string artFile = Path.Combine(repoRoot, artRel.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(artFile)!);
                    var json = JsonSerializer.Serialize(entry.Artifact, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true });
                    File.WriteAllText(artFile, json);
                }
            }
        }

        static void PrintNavBakeFailures(NavBakeResult result, string logPrefix)
        {
            for (int i = 0; i < result.Entries.Count; i++)
            {
                NavBakeResultEntry entry = result.Entries[i];
                if (entry.Success)
                {
                    continue;
                }

                Console.WriteLine($"{logPrefix} failed: tile {entry.Target.ChunkX},{entry.Target.ChunkY} layer={entry.Layer} profile={entry.ProfileId} stage={entry.Artifact.Stage} code={entry.Artifact.ErrorCode} msg={entry.Artifact.Message}");
            }
        }

        static string ToCoreSourceUri(string repoRoot, string inputPath)
        {
            string assetsRoot = Path.GetFullPath(Path.Combine(repoRoot, "assets"));
            string fullInput = Path.GetFullPath(inputPath);
            string assetsRootWithSep = assetsRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? assetsRoot
                : assetsRoot + Path.DirectorySeparatorChar;
            if (!fullInput.StartsWith(assetsRootWithSep, StringComparison.Ordinal))
            {
                return "Core:EditorUploads/" + Path.GetFileName(inputPath);
            }

            string relative = Path.GetRelativePath(assetsRoot, fullInput)
                .Replace('\\', '/');
            return "Core:" + relative;
        }

    }
}
