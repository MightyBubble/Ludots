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
using Ludots.Core.Map.Hex;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.Surface;
using Ludots.Core.Navigation.Terrain;
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
            var genChunkSizeOption = new Option<int>("--chunkSize", () => SpatialScaleDefaults.TerrainChunkCells, "Chunk size (power-of-two)");
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
            var navLargeBakeOption = new Option<bool>("--large-bake", () => false, "Allow a large nav bake after reviewing the matching estimate");
            var navEstimateHashOption = new Option<string?>("--estimateHash", () => null, "Estimate hash returned by nav estimate-recast-react");
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
            bakeRecastReactNavCommand.AddOption(navTileVersionOption);
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
                var tileVersion = ctx.ParseResult.GetValueForOption(navTileVersionOption);
                var largeBake = ctx.ParseResult.GetValueForOption(navLargeBakeOption);
                var estimateHash = ctx.ParseResult.GetValueForOption(navEstimateHashOption);
                ctx.ExitCode = BakeNavFromReactRecast(mapId, modId, inputPath, dirtyPath, includeNeighbors, outDir, heightScale, minUpDot, cliffThreshold, writeArtifact, parallel, maxDegree, tileVersion, largeBake, estimateHash);
            });
            navCommand.AddCommand(bakeRecastReactNavCommand);

            var estimateRecastReactNavCommand = new Command("estimate-recast-react", "Estimate Recast NavTile bake cost from React editor map_data.bin");
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
            estimateRecastReactNavCommand.AddOption(navTileVersionOption);
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
                var tileVersion = ctx.ParseResult.GetValueForOption(navTileVersionOption);
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

                Console.WriteLine($"BakeNavRecastReact: mapId={mapId} modId={modId ?? "(auto)"} topology={terrain.Topology} chunks={terrain.WidthChunks}x{terrain.HeightChunks} obstacles={context.Obstacles.ObstacleCount}");
                NavBakeEstimateReport estimate = NavBakeEstimator.Estimate(context);
                Console.WriteLine($"BakeNavRecastReact estimate: status={estimate.BudgetStatusText} hash={estimate.EstimateHash} terrainHash={estimate.TerrainContentHash} targets={estimate.TargetTileCount} operations={estimate.BakeOperationCount} workUnits={estimate.BudgetWorkUnitCount} seconds={estimate.EstimatedSecondsLow:F1}-{estimate.EstimatedSecondsHigh:F1}");
                NavBakeEstimator.EnsureBakeAllowed(estimate, largeBakeApproved, acceptedEstimateHash);

                var result = new NavBakeService(new RecastNavBakeAlgorithm(), new ExactCdtNavBakeAlgorithm()).Bake(context);
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
            var build = new NavBuildConfig(heightScale, minUpDot, cliffThreshold);
            NavTriangleSurfaceTileIndex surface = LogicTerrainTriangleSurfaceCompiler.Compile(
                terrain,
                build,
                bakeConfigContext.Config.TriangleSurface?.HaloPaddingCm ?? 0);

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
                TriangleSurface = surface,
                Obstacles = obstacles,
                Config = bakeConfig,
                AgentProfiles = bakeConfigContext.AgentProfiles,
                Targets = targets,
                BuildConfig = build,
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
            string spatialType = (boardConfig.SpatialType ?? "Grid").Trim();

            if (spatialType.Equals("Grid", StringComparison.OrdinalIgnoreCase))
            {
                return ReactMapDataBinConverter.ReadGridLogicTerrainField(
                    inputReactBinPath,
                    boardConfig.GridCellSizeCm > 0 ? boardConfig.GridCellSizeCm : SpatialScaleDefaults.CellCm);
            }

            if (spatialType.Equals("HexGrid", StringComparison.OrdinalIgnoreCase) ||
                spatialType.Equals("Hex", StringComparison.OrdinalIgnoreCase) ||
                spatialType.Equals("Hybrid", StringComparison.OrdinalIgnoreCase))
            {
                using var ms = new MemoryStream();
                _ = ReactMapDataBinConverter.ConvertToVertexMapBinary(inputReactBinPath, ms);
                ms.Position = 0;
                VertexMap map = VertexMapBinary.Read(ms);
                return new VertexMapLogicTerrainField(map);
            }

            if (spatialType.Equals("NodeGraph", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Map board '{boardConfig.Name}' is NodeGraph; NodeGraph boards use graph data and do not bake navmesh.");
            }

            throw new InvalidOperationException(
                $"Map board '{boardConfig.Name}' has unsupported SpatialType '{boardConfig.SpatialType}'. Expected Grid, HexGrid, or NodeGraph.");
        }

        static int BakeTiles(VertexMap map, List<(int cx, int cy)> targets, NavBuildConfig cfg, string tilesDir, string artifactsDir, bool writeArtifact, bool parallel, int maxDegree, int tileVersion, string logPrefix, string outDirRoot)
        {
            throw new InvalidOperationException(
                "BakeTiles requires an authored NavMeshBakeConfig from the unified config pipeline; generated layer/profile defaults are forbidden.");
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

        static void WriteLegacyNavBakeResult(NavBakeResult result, string tilesDir, string artifactsDir, bool writeArtifact, string logPrefix)
        {
            for (int i = 0; i < result.Entries.Count; i++)
            {
                NavBakeResultEntry entry = result.Entries[i];
                string tileName = $"navtile_{entry.Target.ChunkX}_{entry.Target.ChunkY}.ntil";
                string artifactName = $"artifact_{entry.Target.ChunkX}_{entry.Target.ChunkY}.json";
                if (entry.Success)
                {
                    string outFile = Path.Combine(tilesDir, tileName);
                    using (var fs = File.Create(outFile))
                    {
                        NavTileBinary.Write(fs, entry.Tile);
                    }
                }
                else
                {
                    Console.WriteLine($"{logPrefix} failed: tile {entry.Target.ChunkX},{entry.Target.ChunkY} stage={entry.Artifact.Stage} code={entry.Artifact.ErrorCode} msg={entry.Artifact.Message}");
                }

                if (writeArtifact)
                {
                    string artFile = Path.Combine(artifactsDir, artifactName);
                    var json = JsonSerializer.Serialize(entry.Artifact, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true });
                    File.WriteAllText(artFile, json);
                }
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

        static void GenerateReactMapDataBin(string outFile, int widthChunks, int heightChunks, string preset, bool overwrite)
        {
            if (File.Exists(outFile) && !overwrite) throw new IOException($"File exists: {outFile}");
            if (widthChunks <= 0 || heightChunks <= 0) throw new ArgumentOutOfRangeException();

            const int chunkSize = SpatialScaleDefaults.TerrainChunkCells;
            int mapW = widthChunks * chunkSize;
            int mapH = heightChunks * chunkSize;

            using var fs = File.Create(outFile);
            using var bw = new BinaryWriter(fs);
            bw.Write(widthChunks);
            bw.Write(heightChunks);
            bw.Write((byte)4);

            for (int cy = 0; cy < heightChunks; cy++)
            {
                for (int cx = 0; cx < widthChunks; cx++)
                {
                    var chunk = new byte[chunkSize * chunkSize * 4];
                    for (int ly = 0; ly < chunkSize; ly++)
                    {
                        for (int lx = 0; lx < chunkSize; lx++)
                        {
                            int gc = cx * chunkSize + lx;
                            int gr = cy * chunkSize + ly;

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

                            int cell = (ly * chunkSize + lx) * 4;
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
