using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ludots.Core.Config;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph;
using GraphProgramBlob = Ludots.Core.GraphRuntime.GraphProgramBlob;
using GraphProgramPackage = Ludots.Core.GraphRuntime.GraphProgramPackage;
using Ludots.Core.Map.Hex;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.Diagnostics;
using Ludots.Core.Navigation.NavMesh.LogicHeightmap;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Scripting;
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
            var genPresetOption = new Option<string>("--preset", () => "bench", "Preset: bench|flat|stripes|cliffs|lake|mountainRiver");
            var genOverwriteOption = new Option<bool>("--overwrite", () => false, "Overwrite if output exists");
            var genChunkMinXOption = new Option<int?>("--chunkMinX", () => null, "Optional first chunk X for sparse LogicHeightmap fixture");
            var genChunkMinYOption = new Option<int?>("--chunkMinY", () => null, "Optional first chunk Y for sparse LogicHeightmap fixture");
            var genChunkMaxXOption = new Option<int?>("--chunkMaxX", () => null, "Optional last chunk X for sparse LogicHeightmap fixture");
            var genChunkMaxYOption = new Option<int?>("--chunkMaxY", () => null, "Optional last chunk Y for sparse LogicHeightmap fixture");
            var genIncludeNeighborsOption = new Option<bool>("--includeNeighbors", () => true, "Include 1 chunk neighbor ring for sparse LogicHeightmap fixture");
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

            var genVhtmCommand = new Command("gen-vhtm", "Generate a VisualHeightmap .vhtm test map");
            genVhtmCommand.AddOption(genOutOption);
            genVhtmCommand.AddOption(genWidthOption);
            genVhtmCommand.AddOption(genHeightOption);
            genVhtmCommand.AddOption(genPresetOption);
            genVhtmCommand.AddOption(genOverwriteOption);
            genVhtmCommand.SetHandler((InvocationContext ctx) =>
            {
                var outFile = ctx.ParseResult.GetValueForOption(genOutOption);
                var w = ctx.ParseResult.GetValueForOption(genWidthOption);
                var h = ctx.ParseResult.GetValueForOption(genHeightOption);
                var presetRaw = ctx.ParseResult.GetValueForOption(genPresetOption);
                var overwrite = ctx.ParseResult.GetValueForOption(genOverwriteOption);

                if (!Enum.TryParse<MapVtxmGenerator.Preset>(presetRaw, ignoreCase: true, out var preset))
                {
                    Console.WriteLine($"Unknown preset: {presetRaw}");
                    ctx.ExitCode = 2;
                    return;
                }

                VisualHeightmapFixtureGenerator.Generate(outFile, w, h, preset, overwrite);
                var info = new FileInfo(Path.GetFullPath(outFile));
                Console.WriteLine($"Wrote: {info.FullName} ({info.Length} bytes)");
                ctx.ExitCode = 0;
            });
            mapCommand.AddCommand(genVhtmCommand);

            var genLhtmCommand = new Command("gen-lhtm", "Generate a LogicHeightmap .lhtm test map");
            genLhtmCommand.AddOption(genOutOption);
            genLhtmCommand.AddOption(genWidthOption);
            genLhtmCommand.AddOption(genHeightOption);
            genLhtmCommand.AddOption(genPresetOption);
            genLhtmCommand.AddOption(genOverwriteOption);
            genLhtmCommand.AddOption(genChunkMinXOption);
            genLhtmCommand.AddOption(genChunkMinYOption);
            genLhtmCommand.AddOption(genChunkMaxXOption);
            genLhtmCommand.AddOption(genChunkMaxYOption);
            genLhtmCommand.AddOption(genIncludeNeighborsOption);
            genLhtmCommand.SetHandler((InvocationContext ctx) =>
            {
                var outFile = ctx.ParseResult.GetValueForOption(genOutOption);
                var w = ctx.ParseResult.GetValueForOption(genWidthOption);
                var h = ctx.ParseResult.GetValueForOption(genHeightOption);
                var presetRaw = ctx.ParseResult.GetValueForOption(genPresetOption);
                var overwrite = ctx.ParseResult.GetValueForOption(genOverwriteOption);
                var chunkMinX = ctx.ParseResult.GetValueForOption(genChunkMinXOption);
                var chunkMinY = ctx.ParseResult.GetValueForOption(genChunkMinYOption);
                var chunkMaxX = ctx.ParseResult.GetValueForOption(genChunkMaxXOption);
                var chunkMaxY = ctx.ParseResult.GetValueForOption(genChunkMaxYOption);
                var includeNeighbors = ctx.ParseResult.GetValueForOption(genIncludeNeighborsOption);

                if (!Enum.TryParse<MapVtxmGenerator.Preset>(presetRaw, ignoreCase: true, out var preset))
                {
                    Console.WriteLine($"Unknown preset: {presetRaw}");
                    ctx.ExitCode = 2;
                    return;
                }

                bool hasSparseWindow = chunkMinX.HasValue || chunkMinY.HasValue || chunkMaxX.HasValue || chunkMaxY.HasValue;
                if (hasSparseWindow)
                {
                    if (!chunkMinX.HasValue || !chunkMinY.HasValue || !chunkMaxX.HasValue || !chunkMaxY.HasValue)
                    {
                        Console.WriteLine("--chunkMinX, --chunkMinY, --chunkMaxX, and --chunkMaxY must be provided together.");
                        ctx.ExitCode = 2;
                        return;
                    }

                    LogicHeightmapFixtureGenerator.GenerateQuadGridSubset(
                        outFile,
                        w,
                        h,
                        preset,
                        chunkMinX.Value,
                        chunkMinY.Value,
                        chunkMaxX.Value,
                        chunkMaxY.Value,
                        includeNeighbors,
                        overwrite);
                }
                else
                {
                    LogicHeightmapFixtureGenerator.GenerateQuadGrid(outFile, w, h, preset, overwrite);
                }

                var info = new FileInfo(Path.GetFullPath(outFile));
                Console.WriteLine($"Wrote: {info.FullName} ({info.Length} bytes)");
                ctx.ExitCode = 0;
            });
            mapCommand.AddCommand(genLhtmCommand);

            var toLhtmCommand = new Command("to-lhtm", "Convert map source data to a LogicHeightmap .lhtm");
            var toLhtmInOption = new Option<string>("--in", "Input map source path") { IsRequired = true };
            var toLhtmOutOption = new Option<string>("--out", "Output .lhtm path") { IsRequired = true };
            var toLhtmSourceKindOption = new Option<string>("--sourceKind", "Source kind: vtxm|vhtm|react|lhtm") { IsRequired = true };
            var toLhtmHeightScaleOption = new Option<float>("--heightScale", () => 2.0f, "VertexMap height scale in meters per height unit");
            var toLhtmHeightmapLayerOption = new Option<int>("--heightmapLayer", () => 0, "VisualHeightmap layer index sampled as terrain height");
            var toLhtmNavChunkSamplesOption = new Option<int>("--navChunkSamples", () => VertexChunk.ChunkSize, "LogicHeightmap samples per nav chunk");
            toLhtmCommand.AddOption(toLhtmInOption);
            toLhtmCommand.AddOption(toLhtmOutOption);
            toLhtmCommand.AddOption(toLhtmSourceKindOption);
            toLhtmCommand.AddOption(toLhtmHeightScaleOption);
            toLhtmCommand.AddOption(toLhtmHeightmapLayerOption);
            toLhtmCommand.AddOption(toLhtmNavChunkSamplesOption);
            toLhtmCommand.AddOption(genOverwriteOption);
            toLhtmCommand.SetHandler((InvocationContext ctx) =>
            {
                var inputPath = ctx.ParseResult.GetValueForOption(toLhtmInOption);
                var outFile = ctx.ParseResult.GetValueForOption(toLhtmOutOption);
                var sourceKind = ctx.ParseResult.GetValueForOption(toLhtmSourceKindOption);
                var heightScale = ctx.ParseResult.GetValueForOption(toLhtmHeightScaleOption);
                var heightmapLayer = ctx.ParseResult.GetValueForOption(toLhtmHeightmapLayerOption);
                var navChunkSamples = ctx.ParseResult.GetValueForOption(toLhtmNavChunkSamplesOption);
                var overwrite = ctx.ParseResult.GetValueForOption(genOverwriteOption);
                ctx.ExitCode = ConvertToLogicHeightmap(inputPath, outFile, sourceKind, heightScale, heightmapLayer, navChunkSamples, overwrite);
            });
            mapCommand.AddCommand(toLhtmCommand);

            var patchLhtmCommand = new Command("patch-lhtm", "Apply a LogicHeightmap editor patch and write a dirty chunk list");
            var patchLhtmInOption = new Option<string>("--in", "Input .lhtm path") { IsRequired = true };
            var patchLhtmPatchOption = new Option<string>("--patch", "LogicHeightmap edit patch JSON") { IsRequired = true };
            var patchLhtmOutOption = new Option<string>("--out", "Output .lhtm path") { IsRequired = true };
            var patchLhtmDirtyOutOption = new Option<string?>("--dirtyOut", () => null, "Optional dirty chunk list json path for nav bake --dirty");
            patchLhtmCommand.AddOption(patchLhtmInOption);
            patchLhtmCommand.AddOption(patchLhtmPatchOption);
            patchLhtmCommand.AddOption(patchLhtmOutOption);
            patchLhtmCommand.AddOption(patchLhtmDirtyOutOption);
            patchLhtmCommand.AddOption(genOverwriteOption);
            patchLhtmCommand.SetHandler((InvocationContext ctx) =>
            {
                var inputPath = ctx.ParseResult.GetValueForOption(patchLhtmInOption);
                var patchPath = ctx.ParseResult.GetValueForOption(patchLhtmPatchOption);
                var outFile = ctx.ParseResult.GetValueForOption(patchLhtmOutOption);
                var dirtyOut = ctx.ParseResult.GetValueForOption(patchLhtmDirtyOutOption);
                var overwrite = ctx.ParseResult.GetValueForOption(genOverwriteOption);
                ctx.ExitCode = ApplyLogicHeightmapPatch(inputPath, patchPath, outFile, dirtyOut, overwrite);
            });
            mapCommand.AddCommand(patchLhtmCommand);

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
            var bakeNavCommand = new Command("bake", "Disabled legacy direct VertexMap bake; use map to-lhtm + nav bake-recast-lhtm");
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
                ctx.ExitCode = RejectLegacyDirectNavBake(
                    "nav bake",
                    "map to-lhtm --sourceKind vtxm -> nav bake-recast-lhtm");
            });
            navCommand.AddCommand(bakeNavCommand);

            var bakeReactNavCommand = new Command("bake-react", "Disabled legacy direct React bake; use map to-lhtm --sourceKind react + nav bake-recast-lhtm");
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
                ctx.ExitCode = RejectLegacyDirectNavBake(
                    "nav bake-react",
                    "map to-lhtm --sourceKind react -> nav bake-recast-lhtm");
            });
            navCommand.AddCommand(bakeReactNavCommand);

            var bakeRecastReactNavCommand = new Command("bake-recast-react", "Bake NavTiles from React editor map_data.bin using Recast");
            var mapIdOption = new Option<string>("--mapId", "Target mapId (used for output paths)") { IsRequired = true };
            var navModRootOption = new Option<string?>("--modRoot", () => null, "Optional mod root(s) whose Navigation/navmesh.json should override Core; separate multiple roots with ';'");
            var navOutputRootOption = new Option<string?>("--repoRoot", () => null, "Repository root for nav output (default: nearest parent containing assets/)");
            var navLayerFilterOption = new Option<string?>("--layer", () => null, "Optional layer id or numeric layer to bake (default: all configured layers)");
            var navProfileFilterOption = new Option<string?>("--profile", () => null, "Optional profile id to bake (default: all configured profiles)");
            bakeRecastReactNavCommand.AddOption(mapIdOption);
            bakeRecastReactNavCommand.AddOption(reactInOption);
            bakeRecastReactNavCommand.AddOption(reactDirtyOption);
            bakeRecastReactNavCommand.AddOption(reactIncludeNeighborsOption);
            bakeRecastReactNavCommand.AddOption(navModRootOption);
            bakeRecastReactNavCommand.AddOption(navOutputRootOption);
            bakeRecastReactNavCommand.AddOption(navLayerFilterOption);
            bakeRecastReactNavCommand.AddOption(navProfileFilterOption);
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
                var modRoot = ctx.ParseResult.GetValueForOption(navModRootOption);
                var outDir = ctx.ParseResult.GetValueForOption(navOutputRootOption);
                var layerFilter = ctx.ParseResult.GetValueForOption(navLayerFilterOption);
                var profileFilter = ctx.ParseResult.GetValueForOption(navProfileFilterOption);
                var heightScale = ctx.ParseResult.GetValueForOption(navHeightScaleOption);
                var minUpDot = ctx.ParseResult.GetValueForOption(navMinUpDotOption);
                var cliffThreshold = ctx.ParseResult.GetValueForOption(navCliffThresholdOption);
                var writeArtifact = ctx.ParseResult.GetValueForOption(navArtifactOption);
                var parallel = ctx.ParseResult.GetValueForOption(navParallelOption);
                var maxDegree = ctx.ParseResult.GetValueForOption(navMaxDegreeOption);
                var tileVersion = ctx.ParseResult.GetValueForOption(navTileVersionOption);
                ctx.ExitCode = BakeNavFromReactRecast(mapId, inputPath, dirtyPath, includeNeighbors, modRoot, outDir, layerFilter, profileFilter, heightScale, minUpDot, cliffThreshold, writeArtifact, parallel, maxDegree, tileVersion);
            });
            navCommand.AddCommand(bakeRecastReactNavCommand);

            var bakeRecastVtxmNavCommand = new Command("bake-recast-vtxm", "Bake NavTiles from VertexMap .vtxm using Recast");
            bakeRecastVtxmNavCommand.AddOption(mapIdOption);
            bakeRecastVtxmNavCommand.AddOption(navInOption);
            bakeRecastVtxmNavCommand.AddOption(reactDirtyOption);
            bakeRecastVtxmNavCommand.AddOption(reactIncludeNeighborsOption);
            bakeRecastVtxmNavCommand.AddOption(navModRootOption);
            bakeRecastVtxmNavCommand.AddOption(navOutputRootOption);
            bakeRecastVtxmNavCommand.AddOption(navLayerFilterOption);
            bakeRecastVtxmNavCommand.AddOption(navProfileFilterOption);
            bakeRecastVtxmNavCommand.AddOption(navHeightScaleOption);
            bakeRecastVtxmNavCommand.AddOption(navMinUpDotOption);
            bakeRecastVtxmNavCommand.AddOption(navCliffThresholdOption);
            bakeRecastVtxmNavCommand.AddOption(navArtifactOption);
            bakeRecastVtxmNavCommand.AddOption(navParallelOption);
            bakeRecastVtxmNavCommand.AddOption(navMaxDegreeOption);
            bakeRecastVtxmNavCommand.AddOption(navTileVersionOption);
            bakeRecastVtxmNavCommand.SetHandler((InvocationContext ctx) =>
            {
                var mapId = ctx.ParseResult.GetValueForOption(mapIdOption);
                var inputPath = ctx.ParseResult.GetValueForOption(navInOption);
                var dirtyPath = ctx.ParseResult.GetValueForOption(reactDirtyOption);
                var includeNeighbors = ctx.ParseResult.GetValueForOption(reactIncludeNeighborsOption);
                var modRoot = ctx.ParseResult.GetValueForOption(navModRootOption);
                var outDir = ctx.ParseResult.GetValueForOption(navOutputRootOption);
                var layerFilter = ctx.ParseResult.GetValueForOption(navLayerFilterOption);
                var profileFilter = ctx.ParseResult.GetValueForOption(navProfileFilterOption);
                var heightScale = ctx.ParseResult.GetValueForOption(navHeightScaleOption);
                var minUpDot = ctx.ParseResult.GetValueForOption(navMinUpDotOption);
                var cliffThreshold = ctx.ParseResult.GetValueForOption(navCliffThresholdOption);
                var writeArtifact = ctx.ParseResult.GetValueForOption(navArtifactOption);
                var parallel = ctx.ParseResult.GetValueForOption(navParallelOption);
                var maxDegree = ctx.ParseResult.GetValueForOption(navMaxDegreeOption);
                var tileVersion = ctx.ParseResult.GetValueForOption(navTileVersionOption);
                ctx.ExitCode = BakeNavFromVtxmRecast(mapId, inputPath, dirtyPath, includeNeighbors, modRoot, outDir, layerFilter, profileFilter, heightScale, minUpDot, cliffThreshold, writeArtifact, parallel, maxDegree, tileVersion);
            });
            navCommand.AddCommand(bakeRecastVtxmNavCommand);

            var bakeRecastVhtmNavCommand = new Command("bake-recast-vhtm", "Bake NavTiles from VisualHeightmap .vhtm using Recast");
            var vhtmLayerOption = new Option<int>("--heightmapLayer", () => 0, "VisualHeightmap layer index sampled as terrain height");
            var vhtmTileSizeOption = new Option<int>("--navChunkSamples", () => VertexChunk.ChunkSize, "Generated VertexMap samples per nav chunk");
            bakeRecastVhtmNavCommand.AddOption(mapIdOption);
            bakeRecastVhtmNavCommand.AddOption(navInOption);
            bakeRecastVhtmNavCommand.AddOption(reactDirtyOption);
            bakeRecastVhtmNavCommand.AddOption(reactIncludeNeighborsOption);
            bakeRecastVhtmNavCommand.AddOption(navModRootOption);
            bakeRecastVhtmNavCommand.AddOption(navOutputRootOption);
            bakeRecastVhtmNavCommand.AddOption(navLayerFilterOption);
            bakeRecastVhtmNavCommand.AddOption(navProfileFilterOption);
            bakeRecastVhtmNavCommand.AddOption(navHeightScaleOption);
            bakeRecastVhtmNavCommand.AddOption(navMinUpDotOption);
            bakeRecastVhtmNavCommand.AddOption(navCliffThresholdOption);
            bakeRecastVhtmNavCommand.AddOption(navArtifactOption);
            bakeRecastVhtmNavCommand.AddOption(navParallelOption);
            bakeRecastVhtmNavCommand.AddOption(navMaxDegreeOption);
            bakeRecastVhtmNavCommand.AddOption(navTileVersionOption);
            bakeRecastVhtmNavCommand.AddOption(vhtmLayerOption);
            bakeRecastVhtmNavCommand.AddOption(vhtmTileSizeOption);
            bakeRecastVhtmNavCommand.SetHandler((InvocationContext ctx) =>
            {
                var mapId = ctx.ParseResult.GetValueForOption(mapIdOption);
                var inputPath = ctx.ParseResult.GetValueForOption(navInOption);
                var dirtyPath = ctx.ParseResult.GetValueForOption(reactDirtyOption);
                var includeNeighbors = ctx.ParseResult.GetValueForOption(reactIncludeNeighborsOption);
                var modRoot = ctx.ParseResult.GetValueForOption(navModRootOption);
                var outDir = ctx.ParseResult.GetValueForOption(navOutputRootOption);
                var layerFilter = ctx.ParseResult.GetValueForOption(navLayerFilterOption);
                var profileFilter = ctx.ParseResult.GetValueForOption(navProfileFilterOption);
                var heightScale = ctx.ParseResult.GetValueForOption(navHeightScaleOption);
                var minUpDot = ctx.ParseResult.GetValueForOption(navMinUpDotOption);
                var cliffThreshold = ctx.ParseResult.GetValueForOption(navCliffThresholdOption);
                var writeArtifact = ctx.ParseResult.GetValueForOption(navArtifactOption);
                var parallel = ctx.ParseResult.GetValueForOption(navParallelOption);
                var maxDegree = ctx.ParseResult.GetValueForOption(navMaxDegreeOption);
                var tileVersion = ctx.ParseResult.GetValueForOption(navTileVersionOption);
                var heightmapLayer = ctx.ParseResult.GetValueForOption(vhtmLayerOption);
                var navChunkSamples = ctx.ParseResult.GetValueForOption(vhtmTileSizeOption);
                ctx.ExitCode = BakeNavFromVhtmRecast(mapId, inputPath, dirtyPath, includeNeighbors, modRoot, outDir, layerFilter, profileFilter, heightScale, minUpDot, cliffThreshold, writeArtifact, parallel, maxDegree, tileVersion, heightmapLayer, navChunkSamples);
            });
            navCommand.AddCommand(bakeRecastVhtmNavCommand);

            var bakeRecastLhtmNavCommand = new Command("bake-recast-lhtm", "Bake NavTiles from LogicHeightmap .lhtm using Recast");
            bakeRecastLhtmNavCommand.AddOption(mapIdOption);
            bakeRecastLhtmNavCommand.AddOption(navInOption);
            bakeRecastLhtmNavCommand.AddOption(reactDirtyOption);
            bakeRecastLhtmNavCommand.AddOption(reactIncludeNeighborsOption);
            bakeRecastLhtmNavCommand.AddOption(navModRootOption);
            bakeRecastLhtmNavCommand.AddOption(navOutputRootOption);
            bakeRecastLhtmNavCommand.AddOption(navLayerFilterOption);
            bakeRecastLhtmNavCommand.AddOption(navProfileFilterOption);
            bakeRecastLhtmNavCommand.AddOption(navHeightScaleOption);
            bakeRecastLhtmNavCommand.AddOption(navMinUpDotOption);
            bakeRecastLhtmNavCommand.AddOption(navCliffThresholdOption);
            bakeRecastLhtmNavCommand.AddOption(navArtifactOption);
            bakeRecastLhtmNavCommand.AddOption(navParallelOption);
            bakeRecastLhtmNavCommand.AddOption(navMaxDegreeOption);
            bakeRecastLhtmNavCommand.AddOption(navTileVersionOption);
            bakeRecastLhtmNavCommand.SetHandler((InvocationContext ctx) =>
            {
                var mapId = ctx.ParseResult.GetValueForOption(mapIdOption);
                var inputPath = ctx.ParseResult.GetValueForOption(navInOption);
                var dirtyPath = ctx.ParseResult.GetValueForOption(reactDirtyOption);
                var includeNeighbors = ctx.ParseResult.GetValueForOption(reactIncludeNeighborsOption);
                var modRoot = ctx.ParseResult.GetValueForOption(navModRootOption);
                var outDir = ctx.ParseResult.GetValueForOption(navOutputRootOption);
                var layerFilter = ctx.ParseResult.GetValueForOption(navLayerFilterOption);
                var profileFilter = ctx.ParseResult.GetValueForOption(navProfileFilterOption);
                var heightScale = ctx.ParseResult.GetValueForOption(navHeightScaleOption);
                var minUpDot = ctx.ParseResult.GetValueForOption(navMinUpDotOption);
                var cliffThreshold = ctx.ParseResult.GetValueForOption(navCliffThresholdOption);
                var writeArtifact = ctx.ParseResult.GetValueForOption(navArtifactOption);
                var parallel = ctx.ParseResult.GetValueForOption(navParallelOption);
                var maxDegree = ctx.ParseResult.GetValueForOption(navMaxDegreeOption);
                var tileVersion = ctx.ParseResult.GetValueForOption(navTileVersionOption);
                ctx.ExitCode = BakeNavFromLhtmRecast(mapId, inputPath, dirtyPath, includeNeighbors, modRoot, outDir, layerFilter, profileFilter, heightScale, minUpDot, cliffThreshold, writeArtifact, parallel, maxDegree, tileVersion);
            });
            navCommand.AddCommand(bakeRecastLhtmNavCommand);
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

        static int ConvertToLogicHeightmap(
            string inputPath,
            string outFile,
            string sourceKind,
            float heightScale,
            int heightmapLayer,
            int navChunkSamples,
            bool overwrite)
        {
            if (string.IsNullOrWhiteSpace(inputPath) || !File.Exists(inputPath))
            {
                Console.WriteLine($"Input not found: {inputPath}");
                return 2;
            }

            if (string.IsNullOrWhiteSpace(outFile))
            {
                Console.WriteLine("Output .lhtm path is required.");
                return 2;
            }

            if (navChunkSamples <= 0 || navChunkSamples != LogicHeightmapChunk.ChunkSize)
            {
                Console.WriteLine($"navChunkSamples must be {LogicHeightmapChunk.ChunkSize} for the current NavTile pipeline.");
                return 2;
            }

            outFile = Path.GetFullPath(outFile);
            string? outDir = Path.GetDirectoryName(outFile);
            if (!string.IsNullOrWhiteSpace(outDir))
            {
                Directory.CreateDirectory(outDir);
            }

            if (File.Exists(outFile) && !overwrite)
            {
                Console.WriteLine($"Output exists: {outFile} (pass --overwrite to replace)");
                return 2;
            }

            var cfg = new NavBuildConfig(heightScale, minWalkableUpDot: 0.6f, cliffHeightThreshold: 1);
            string normalizedKind = sourceKind?.Trim().ToLowerInvariant() ?? string.Empty;
            switch (normalizedKind)
            {
                case "vtxm":
                    using (var output = File.Create(outFile))
                    {
                        WriteLogicHeightmapFromSource(output, inputPath, normalizedKind, cfg, heightmapLayer, navChunkSamples);
                    }

                    var vertexInfo = new FileInfo(outFile);
                    using (var reader = LogicHeightmapFileReader.Open(outFile))
                    {
                        Console.WriteLine($"Wrote LogicHeightmap: {vertexInfo.FullName} ({vertexInfo.Length} bytes) sourceKind={normalizedKind} chunks={reader.WidthInChunks}x{reader.HeightInChunks} grid={reader.GridKind}");
                    }

                    return 0;

                case "react":
                    using (var output = File.Create(outFile))
                    {
                        WriteLogicHeightmapFromSource(output, inputPath, normalizedKind, cfg, heightmapLayer, navChunkSamples);
                    }

                    var reactInfo = new FileInfo(outFile);
                    using (var reader = LogicHeightmapFileReader.Open(outFile))
                    {
                        Console.WriteLine($"Wrote LogicHeightmap: {reactInfo.FullName} ({reactInfo.Length} bytes) sourceKind={normalizedKind} chunks={reader.WidthInChunks}x{reader.HeightInChunks} grid={reader.GridKind}");
                    }

                    return 0;

                case "vhtm":
                    using (var fs = File.Create(outFile))
                    {
                        WriteLogicHeightmapFromSource(fs, inputPath, normalizedKind, cfg, heightmapLayer, navChunkSamples);
                    }

                    var visualInfo = new FileInfo(outFile);
                    using (var reader = LogicHeightmapFileReader.Open(outFile))
                    {
                        Console.WriteLine($"Wrote LogicHeightmap: {visualInfo.FullName} ({visualInfo.Length} bytes) sourceKind={normalizedKind} chunks={reader.WidthInChunks}x{reader.HeightInChunks} grid={reader.GridKind}");
                    }

                    return 0;

                case "lhtm":
                    using (var src = File.OpenRead(inputPath))
                    using (var dst = File.Create(outFile))
                    {
                        src.CopyTo(dst);
                    }

                    var lhtmInfo = new FileInfo(outFile);
                    using (var reader = LogicHeightmapFileReader.Open(outFile))
                    {
                        Console.WriteLine($"Wrote LogicHeightmap: {lhtmInfo.FullName} ({lhtmInfo.Length} bytes) sourceKind={normalizedKind} chunks={reader.WidthInChunks}x{reader.HeightInChunks} grid={reader.GridKind}");
                    }

                    return 0;

                default:
                    Console.WriteLine($"Unknown sourceKind: {sourceKind}. Expected vtxm, vhtm, react, or lhtm.");
                    return 2;
            }
        }

        static void WriteLogicHeightmapFromSource(
            Stream output,
            string inputPath,
            string sourceKind,
            in NavBuildConfig cfg,
            int heightmapLayer,
            int navChunkSamples)
        {
            string normalizedKind = sourceKind?.Trim().ToLowerInvariant() ?? string.Empty;
            switch (normalizedKind)
            {
                case "vtxm":
                    using (var input = File.OpenRead(inputPath))
                    {
                        LogicHeightmapVertexMapAdapter.WriteVertexMap(output, input, cfg);
                    }
                    return;

                case "react":
                    using (var ms = new MemoryStream())
                    {
                        _ = ReactMapDataBinConverter.ConvertToVertexMapBinary(inputPath, ms);
                        ms.Position = 0;
                        LogicHeightmapVertexMapAdapter.WriteVertexMap(output, ms, cfg);
                    }
                    return;

                case "vhtm":
                    using (var fs = File.OpenRead(inputPath))
                    {
                        LogicHeightmapVisualHeightmapAdapter.WriteVisualHeightmap(output, fs, heightmapLayer, navChunkSamples);
                    }
                    return;

                default:
                    throw new ArgumentException($"Unknown sourceKind: {sourceKind}. Expected vtxm, vhtm, or react.", nameof(sourceKind));
            }
        }

        static int ApplyLogicHeightmapPatch(
            string inputPath,
            string patchPath,
            string outFile,
            string? dirtyOut,
            bool overwrite)
        {
            try
            {
                LogicHeightmapEditPatch patch = LogicHeightmapEditPatch.Load(patchPath);
                LogicHeightmapEditPatch.ApplyResult result = patch.Apply(inputPath, outFile, overwrite);

                string dirtyPath = dirtyOut;
                if (string.IsNullOrWhiteSpace(dirtyPath))
                {
                    dirtyPath = Path.Combine(
                        Path.GetDirectoryName(Path.GetFullPath(outFile)) ?? Directory.GetCurrentDirectory(),
                        $"{Path.GetFileNameWithoutExtension(outFile)}.dirty-chunks.json");
                }

                string? dirtyDir = Path.GetDirectoryName(Path.GetFullPath(dirtyPath));
                if (!string.IsNullOrWhiteSpace(dirtyDir))
                {
                    Directory.CreateDirectory(dirtyDir);
                }

                File.WriteAllText(dirtyPath, JsonSerializer.Serialize(result.DirtyChunks, new JsonSerializerOptions { WriteIndented = true }));
                patch.Save(patchPath);
                Console.WriteLine($"Applied LogicHeightmap patch: operations={result.OperationCount} cells={result.AppliedCellCount} dirtyChunks={result.DirtyChunks.Length}");
                Console.WriteLine($"Output .lhtm: {Path.GetFullPath(outFile)}");
                Console.WriteLine($"Dirty chunks: {Path.GetFullPath(dirtyPath)}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to apply LogicHeightmap patch: {ex.Message}");
                return 2;
            }
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

        static int RejectLegacyDirectNavBake(string commandName, string replacement)
        {
            Console.WriteLine($"{commandName} is disabled because NavMesh bake sources must first become LogicHeightmap (.lhtm).");
            Console.WriteLine($"Use: {replacement}");
            Console.WriteLine("This keeps VertexMap, VisualHeightmap, React/editor data, and procedural quad sources on the same bake semantics.");
            return 2;
        }

        static int BakeNavFromReactRecast(
            string mapId,
            string inputReactBinPath,
            string? dirtyChunksPath,
            bool includeNeighbors,
            string? modRoot,
            string? outDir,
            string? layerFilter,
            string? profileFilter,
            float heightScale,
            float minUpDot,
            int cliffThreshold,
            bool writeArtifact,
            bool parallel,
            int maxDegree,
            int tileVersion)
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

            var cfg = new NavBuildConfig(heightScale, minUpDot, cliffThreshold);
            return BakeNavFromConvertedLogicHeightmap(
                mapId,
                inputReactBinPath,
                sourceKind: "react",
                cfg,
                heightmapLayer: 0,
                navChunkSamples: VertexChunk.ChunkSize,
                dirtyChunksPath,
                includeNeighbors,
                modRoot,
                outDir,
                layerFilter,
                profileFilter,
                heightScale,
                minUpDot,
                cliffThreshold,
                writeArtifact,
                parallel,
                maxDegree,
                tileVersion);
        }

        static int BakeNavFromVtxmRecast(
            string mapId,
            string inputVtxmPath,
            string? dirtyChunksPath,
            bool includeNeighbors,
            string? modRoot,
            string? outDir,
            string? layerFilter,
            string? profileFilter,
            float heightScale,
            float minUpDot,
            int cliffThreshold,
            bool writeArtifact,
            bool parallel,
            int maxDegree,
            int tileVersion)
        {
            if (string.IsNullOrWhiteSpace(mapId))
            {
                Console.WriteLine("mapId is required.");
                return 2;
            }
            if (!File.Exists(inputVtxmPath))
            {
                Console.WriteLine($"Input not found: {inputVtxmPath}");
                return 2;
            }

            var cfg = new NavBuildConfig(heightScale, minUpDot, cliffThreshold);
            return BakeNavFromConvertedLogicHeightmap(
                mapId,
                inputVtxmPath,
                sourceKind: "vtxm",
                cfg,
                heightmapLayer: 0,
                navChunkSamples: VertexChunk.ChunkSize,
                dirtyChunksPath,
                includeNeighbors,
                modRoot,
                outDir,
                layerFilter,
                profileFilter,
                heightScale,
                minUpDot,
                cliffThreshold,
                writeArtifact,
                parallel,
                maxDegree,
                tileVersion);
        }

        static int BakeNavFromVhtmRecast(
            string mapId,
            string inputVhtmPath,
            string? dirtyChunksPath,
            bool includeNeighbors,
            string? modRoot,
            string? outDir,
            string? layerFilter,
            string? profileFilter,
            float heightScale,
            float minUpDot,
            int cliffThreshold,
            bool writeArtifact,
            bool parallel,
            int maxDegree,
            int tileVersion,
            int heightmapLayer,
            int navChunkSamples)
        {
            if (string.IsNullOrWhiteSpace(mapId))
            {
                Console.WriteLine("mapId is required.");
                return 2;
            }

            if (!File.Exists(inputVhtmPath))
            {
                Console.WriteLine($"Input not found: {inputVhtmPath}");
                return 2;
            }

            if (navChunkSamples <= 0 || navChunkSamples != VertexChunk.ChunkSize)
            {
                Console.WriteLine($"navChunkSamples must be {VertexChunk.ChunkSize} for the current NavTile pipeline.");
                return 2;
            }

            var cfg = new NavBuildConfig(heightScale, minUpDot, cliffThreshold);
            return BakeNavFromConvertedLogicHeightmap(
                mapId,
                inputVhtmPath,
                sourceKind: "vhtm",
                cfg,
                heightmapLayer,
                navChunkSamples,
                dirtyChunksPath,
                includeNeighbors,
                modRoot,
                outDir,
                layerFilter,
                profileFilter,
                heightScale,
                minUpDot,
                cliffThreshold,
                writeArtifact,
                parallel,
                maxDegree,
                tileVersion);
        }

        static int BakeNavFromConvertedLogicHeightmap(
            string mapId,
            string sourcePath,
            string sourceKind,
            in NavBuildConfig cfg,
            int heightmapLayer,
            int navChunkSamples,
            string? dirtyChunksPath,
            bool includeNeighbors,
            string? modRoot,
            string? outDir,
            string? layerFilter,
            string? profileFilter,
            float heightScale,
            float minUpDot,
            int cliffThreshold,
            bool writeArtifact,
            bool parallel,
            int maxDegree,
            int tileVersion)
        {
            string tempPath = Path.Combine(Path.GetTempPath(), $"ludots_nav_{mapId}_{Guid.NewGuid():N}.lhtm");
            try
            {
                using (var output = File.Create(tempPath))
                {
                    WriteLogicHeightmapFromSource(output, sourcePath, sourceKind, cfg, heightmapLayer, navChunkSamples);
                }

                return BakeNavFromLhtmRecast(
                    mapId,
                    tempPath,
                    sourcePath,
                    dirtyChunksPath,
                    includeNeighbors,
                    modRoot,
                    outDir,
                    layerFilter,
                    profileFilter,
                    heightScale,
                    minUpDot,
                    cliffThreshold,
                    writeArtifact,
                    parallel,
                    maxDegree,
                    tileVersion);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to convert {sourceKind} source to LogicHeightmap for Recast bake: {ex.Message}");
                return 2;
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                }
            }
        }

        static int BakeNavFromLhtmRecast(
            string mapId,
            string inputLhtmPath,
            string? dirtyChunksPath,
            bool includeNeighbors,
            string? modRoot,
            string? outDir,
            string? layerFilter,
            string? profileFilter,
            float heightScale,
            float minUpDot,
            int cliffThreshold,
            bool writeArtifact,
            bool parallel,
            int maxDegree,
            int tileVersion)
        {
            return BakeNavFromLhtmRecast(
                mapId,
                inputLhtmPath,
                sourceMapPath: inputLhtmPath,
                dirtyChunksPath,
                includeNeighbors,
                modRoot,
                outDir,
                layerFilter,
                profileFilter,
                heightScale,
                minUpDot,
                cliffThreshold,
                writeArtifact,
                parallel,
                maxDegree,
                tileVersion);
        }

        static int BakeNavFromLhtmRecast(
            string mapId,
            string inputLhtmPath,
            string sourceMapPath,
            string? dirtyChunksPath,
            bool includeNeighbors,
            string? modRoot,
            string? outDir,
            string? layerFilter,
            string? profileFilter,
            float heightScale,
            float minUpDot,
            int cliffThreshold,
            bool writeArtifact,
            bool parallel,
            int maxDegree,
            int tileVersion)
        {
            if (string.IsNullOrWhiteSpace(mapId))
            {
                Console.WriteLine("mapId is required.");
                return 2;
            }

            if (!File.Exists(inputLhtmPath))
            {
                Console.WriteLine($"Input not found: {inputLhtmPath}");
                return 2;
            }

            try
            {
                using var reader = LogicHeightmapFileReader.Open(inputLhtmPath);
                return BakeNavRecastInternal(
                    mapId,
                    toolName: "ludots nav bake-recast-lhtm",
                    logPrefix: "BakeNavRecastLhtm",
                    sourceMapPath: sourceMapPath,
                    reader.WidthInChunks,
                    reader.HeightInChunks,
                    reader.GridKind,
                    loadTileWindow: (cx, cy) => reader.ReadTileWindow(cx, cy),
                    dirtyChunksPath,
                    includeNeighbors,
                    modRoot,
                    outDir,
                    layerFilter,
                    profileFilter,
                    heightScale,
                    minUpDot,
                    cliffThreshold,
                    writeArtifact,
                    parallel,
                    maxDegree,
                    tileVersion);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to read LogicHeightmap: {ex.Message}");
                return 2;
            }
        }

        static int BakeNavRecast(
            string mapId,
            string toolName,
            string logPrefix,
            string sourceMapPath,
            LogicHeightmap logicHeightmap,
            string? dirtyChunksPath,
            bool includeNeighbors,
            string? modRoot,
            string? outDir,
            string? layerFilter,
            string? profileFilter,
            float heightScale,
            float minUpDot,
            int cliffThreshold,
            bool writeArtifact,
            bool parallel,
            int maxDegree,
            int tileVersion)
        {
            if (logicHeightmap == null)
            {
                Console.WriteLine("LogicHeightmap is required.");
                return 2;
            }

            return BakeNavRecastInternal(
                mapId,
                toolName,
                logPrefix,
                sourceMapPath,
                logicHeightmap.WidthInChunks,
                logicHeightmap.HeightInChunks,
                logicHeightmap.GridKind,
                loadTileWindow: (_, _) => logicHeightmap,
                dirtyChunksPath,
                includeNeighbors,
                modRoot,
                outDir,
                layerFilter,
                profileFilter,
                heightScale,
                minUpDot,
                cliffThreshold,
                writeArtifact,
                parallel,
                maxDegree,
                tileVersion);
        }

        static int BakeNavRecastInternal(
            string mapId,
            string toolName,
            string logPrefix,
            string sourceMapPath,
            int widthInChunks,
            int heightInChunks,
            LogicHeightmapGridKind gridKind,
            Func<int, int, LogicHeightmap> loadTileWindow,
            string? dirtyChunksPath,
            bool includeNeighbors,
            string? modRoot,
            string? outDir,
            string? layerFilter,
            string? profileFilter,
            float heightScale,
            float minUpDot,
            int cliffThreshold,
            bool writeArtifact,
            bool parallel,
            int maxDegree,
            int tileVersion)
        {
            if (widthInChunks <= 0 || heightInChunks <= 0)
            {
                Console.WriteLine("LogicHeightmap dimensions are invalid.");
                return 2;
            }

            if (loadTileWindow == null)
            {
                Console.WriteLine("LogicHeightmap tile window loader is required.");
                return 2;
            }

            string repoRoot = ResolveNavRecastRepoRoot(outDir);
            if (!Directory.Exists(Path.Combine(repoRoot, "assets")))
            {
                Console.WriteLine($"Invalid repo root (missing assets/): {repoRoot}");
                return 2;
            }

            NavMeshBakeConfig bakeConfig;
            try
            {
                bakeConfig = LoadNavMeshBakeConfigForTool(repoRoot, modRoot);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load navmesh bake config '{NavMeshConfigPaths.BakeConfigPath}': {ex.Message}");
                return 2;
            }

            try
            {
                ApplyNavBakeFilters(bakeConfig, layerFilter, profileFilter);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Invalid nav bake filter: {ex.Message}");
                return 2;
            }

            var targets = new List<(int cx, int cy)>();
            try
            {
                targets = ResolveNavBakeTargets(widthInChunks, heightInChunks, dirtyChunksPath, includeNeighbors);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to resolve nav bake targets: {ex.Message}");
                return 2;
            }

            var profiles = bakeConfig.Profiles;
            NavObstacleSet obstacles = LoadNavObstacles(repoRoot, mapId);
            var legacyCfg = new NavBuildConfig(heightScale, minUpDot, cliffThreshold);
            Console.WriteLine($"{logPrefix}: mapId={mapId} logicHeightmap {widthInChunks}x{heightInChunks} chunks grid={gridKind} targets={targets.Count} layers={bakeConfig.Layers.Count} profiles={profiles.Count} repoRoot={Path.GetFullPath(repoRoot)}");

            int ok = 0;
            int fail = 0;
            var consoleLock = new object();
            var counters = CreateBakeCounters(bakeConfig);
            var failureSamples = new List<NavBakeFailureSample>(NavBakeDiagnosticsContract.MaxFailureSamples);

            void BakeOne((int cx, int cy) t)
            {
                LogicHeightmap tileWindow;
                try
                {
                    tileWindow = loadTileWindow(t.cx, t.cy);
                }
                catch (Exception ex)
                {
                    lock (consoleLock)
                    {
                        Console.WriteLine($"{logPrefix} failed: tile {t.cx},{t.cy} source-window-load msg={ex.Message}");
                    }

                    for (int li = 0; li < bakeConfig.Layers.Count; li++)
                    {
                        var layerConfig = bakeConfig.Layers[li];
                        for (int pi = 0; pi < profiles.Count; pi++)
                        {
                            var profileConfig = profiles[pi];
                            Interlocked.Increment(ref fail);
                            counters[li, pi].MarkFailed();
                            lock (consoleLock)
                            {
                                if (failureSamples.Count < NavBakeDiagnosticsContract.MaxFailureSamples)
                                {
                                    failureSamples.Add(new NavBakeFailureSample
                                    {
                                        ChunkX = t.cx,
                                        ChunkY = t.cy,
                                        Layer = layerConfig.Layer,
                                        LayerId = layerConfig.Id ?? string.Empty,
                                        ProfileId = profileConfig.Id ?? string.Empty,
                                        Stage = "LoadSourceWindow",
                                        ErrorCode = NavBakeErrorCode.InvalidInput.ToString(),
                                        Message = ex.Message
                                    });
                                }
                            }
                        }
                    }

                    return;
                }

                for (int li = 0; li < bakeConfig.Layers.Count; li++)
                {
                    var layerConfig = bakeConfig.Layers[li];
                    int layer = layerConfig.Layer;
                    for (int pi = 0; pi < profiles.Count; pi++)
                    {
                        var profileConfig = profiles[pi];
                        if (RecastNavTileBaker.TryBake(tileWindow, t.cx, t.cy, (uint)tileVersion, legacyCfg, profileConfig, layer, obstacles, out var tile, out var artifact))
                        {
                            string profileId = profileConfig.Id ?? throw new InvalidOperationException("NavMeshBakeConfig.profiles.id is required.");
                            string rel = NavAssetPaths.GetNavTileRelativePath(mapId, layer, profileId, t.cx, t.cy);
                            string outFile = Path.Combine(repoRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                            Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);
                            using (var fs = File.Create(outFile))
                            {
                                NavTileBinary.Write(fs, tile);
                            }

                            Interlocked.Increment(ref ok);
                            counters[li, pi].MarkBaked();

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
                            counters[li, pi].MarkFailed();
                            lock (consoleLock)
                            {
                                Console.WriteLine($"{logPrefix} failed: tile {t.cx},{t.cy} layer={layer} profile={profileConfig.Id} stage={artifact.Stage} code={artifact.ErrorCode} msg={artifact.Message}");
                                if (failureSamples.Count < NavBakeDiagnosticsContract.MaxFailureSamples)
                                {
                                    failureSamples.Add(new NavBakeFailureSample
                                    {
                                        ChunkX = t.cx,
                                        ChunkY = t.cy,
                                        Layer = layer,
                                        LayerId = layerConfig.Id ?? string.Empty,
                                        ProfileId = profileConfig.Id ?? string.Empty,
                                        Stage = artifact.Stage.ToString(),
                                        ErrorCode = artifact.ErrorCode.ToString(),
                                        Message = artifact.Message
                                    });
                                }
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

            WriteNavBakeDiagnostics(repoRoot, mapId, sourceMapPath, toolName, widthInChunks, heightInChunks, targets, bakeConfig, counters, failureSamples);
            Console.WriteLine($"{logPrefix} done. ok={ok} fail={fail} repoRoot={Path.GetFullPath(repoRoot)}");
            return fail == 0 ? 0 : 1;
        }

        static string ResolveNavRecastRepoRoot(string? outDir)
        {
            if (string.IsNullOrWhiteSpace(outDir))
            {
                return FindAssetsRoot();
            }

            string candidate = Path.GetFullPath(outDir);
            if (Directory.Exists(Path.Combine(candidate, "assets")))
            {
                return candidate;
            }

            var current = Directory.Exists(candidate)
                ? new DirectoryInfo(candidate)
                : new FileInfo(candidate).Directory;
            while (current != null)
            {
                if (string.Equals(current.Name, "assets", StringComparison.OrdinalIgnoreCase) &&
                    current.Parent != null)
                {
                    return current.Parent.FullName;
                }

                if (Directory.Exists(Path.Combine(current.FullName, "assets")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            return candidate;
        }

        static NavMeshBakeConfig LoadNavMeshBakeConfigForTool(string repoRoot, string? modRootSpec)
        {
            if (string.IsNullOrWhiteSpace(modRootSpec))
            {
                return NavMeshBakeConfigLoader.LoadFromRepoRoot(repoRoot);
            }

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", Path.Combine(Path.GetFullPath(repoRoot), "assets"));

            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            foreach (string rawRoot in modRootSpec.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string modRoot = Path.GetFullPath(rawRoot);
                string manifestPath = Path.Combine(modRoot, "mod.json");
                if (!File.Exists(manifestPath))
                {
                    throw new FileNotFoundException($"mod.json not found for --modRoot '{modRoot}'.", manifestPath);
                }

                ModManifest manifest = ModManifestJson.ParseStrict(File.ReadAllText(manifestPath), manifestPath);
                if (string.IsNullOrWhiteSpace(manifest.Name))
                {
                    throw new InvalidOperationException($"mod.json has empty name: {manifestPath}");
                }

                vfs.Mount(manifest.Name, modRoot);
                modLoader.LoadedModIds.Add(manifest.Name);
            }

            var pipeline = new ConfigPipeline(vfs, modLoader);
            return new NavMeshBakeConfigLoader(pipeline).Load();
        }

        static void ApplyNavBakeFilters(NavMeshBakeConfig bakeConfig, string? layerFilter, string? profileFilter)
        {
            if (!string.IsNullOrWhiteSpace(layerFilter))
            {
                string filter = layerFilter.Trim();
                bool numericLayer = int.TryParse(filter, out int layerValue);
                var layers = bakeConfig.Layers
                    .Where(layer => numericLayer
                        ? layer.Layer == layerValue
                        : string.Equals(layer.Id, filter, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (layers.Count == 0)
                {
                    throw new InvalidOperationException($"no configured layer matched '{layerFilter}'.");
                }

                bakeConfig.Layers = layers;
            }

            if (!string.IsNullOrWhiteSpace(profileFilter))
            {
                string filter = profileFilter.Trim();
                var profiles = bakeConfig.Profiles
                    .Where(profile => string.Equals(profile.Id, filter, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (profiles.Count == 0)
                {
                    throw new InvalidOperationException($"no configured profile matched '{profileFilter}'.");
                }

                bakeConfig.Profiles = profiles;
            }
        }

        static NavObstacleSet LoadNavObstacles(string repoRoot, string mapId)
        {
            string obsRel = NavAssetPaths.GetObstacleRelativePath(mapId);
            string obsPath = Path.Combine(repoRoot, obsRel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(obsPath))
            {
                return new NavObstacleSet();
            }

            return JsonSerializer.Deserialize<NavObstacleSet>(
                File.ReadAllText(obsPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new NavObstacleSet();
        }

        static List<(int cx, int cy)> ResolveNavBakeTargets(int widthInChunks, int heightInChunks, string? dirtyChunksPath, bool includeNeighbors)
        {
            if (widthInChunks <= 0) throw new ArgumentOutOfRangeException(nameof(widthInChunks));
            if (heightInChunks <= 0) throw new ArgumentOutOfRangeException(nameof(heightInChunks));

            var targets = new List<(int cx, int cy)>();
            if (string.IsNullOrWhiteSpace(dirtyChunksPath))
            {
                for (int cy = 0; cy < heightInChunks; cy++)
                    for (int cx = 0; cx < widthInChunks; cx++)
                        targets.Add((cx, cy));
                return targets;
            }

            if (!File.Exists(dirtyChunksPath))
            {
                throw new FileNotFoundException($"Dirty chunk list not found: {dirtyChunksPath}", dirtyChunksPath);
            }

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
                    if (t.cx < 0 || t.cy < 0 || t.cx >= widthInChunks || t.cy >= heightInChunks) continue;
                targets.Add(t);
            }

            return targets;
        }

        static BakeCounter[,] CreateBakeCounters(NavMeshBakeConfig bakeConfig)
        {
            var counters = new BakeCounter[bakeConfig.Layers.Count, bakeConfig.Profiles.Count];
            for (int li = 0; li < bakeConfig.Layers.Count; li++)
            {
                for (int pi = 0; pi < bakeConfig.Profiles.Count; pi++)
                {
                    counters[li, pi] = new BakeCounter();
                }
            }

            return counters;
        }

        static void WriteNavBakeDiagnostics(
            string repoRoot,
            string mapId,
            string sourceMapPath,
            string toolName,
            int widthInChunks,
            int heightInChunks,
            IReadOnlyCollection<(int cx, int cy)> targets,
            NavMeshBakeConfig bakeConfig,
            BakeCounter[,] counters,
            List<NavBakeFailureSample> failureSamples)
        {
            int worldChunks = checked(widthInChunks * heightInChunks);
            int targetChunks = targets?.Count ?? worldChunks;
            int minChunkX = -1;
            int minChunkY = -1;
            int maxChunkX = -1;
            int maxChunkY = -1;
            if (targets != null && targets.Count > 0)
            {
                minChunkX = targets.Min(t => t.cx);
                minChunkY = targets.Min(t => t.cy);
                maxChunkX = targets.Max(t => t.cx);
                maxChunkY = targets.Max(t => t.cy);
            }

            var doc = new NavBakeDiagnosticsDocument
            {
                SchemaVersion = NavBakeDiagnosticsContract.SchemaVersion,
                MapId = mapId,
                Tool = toolName,
                SourceMapPath = Path.GetFullPath(sourceMapPath),
                GeneratedAtUtc = DateTime.UtcNow.ToString("O"),
                TargetChunkCount = targetChunks,
                WorldChunkCount = worldChunks,
                ActiveWindowMinChunkX = minChunkX,
                ActiveWindowMinChunkY = minChunkY,
                ActiveWindowMaxChunkX = maxChunkX,
                ActiveWindowMaxChunkY = maxChunkY,
                ActiveWindowChunkCount = targetChunks,
                IsPartialCoverage = targetChunks != worldChunks,
                LayerCount = bakeConfig.Layers.Count,
                ProfileCount = bakeConfig.Profiles.Count,
                TotalExpectedTileBakes = targetChunks * bakeConfig.Layers.Count * bakeConfig.Profiles.Count,
                FailureSamples = failureSamples
            };

            for (int li = 0; li < bakeConfig.Layers.Count; li++)
            {
                var layer = bakeConfig.Layers[li];
                for (int pi = 0; pi < bakeConfig.Profiles.Count; pi++)
                {
                    var profile = bakeConfig.Profiles[pi];
                    BakeCounter counter = counters[li, pi];
                    int baked = counter.Baked;
                    int failed = counter.Failed;
                    int notLoaded = Math.Max(0, targetChunks - baked - failed);
                    doc.TotalBakedTiles += baked;
                    doc.TotalFailedTiles += failed;
                    doc.LayerProfiles.Add(NavBakeLayerProfileSummary.Create(
                        layer.Layer,
                        layer.Id,
                        profile.Id,
                        targetChunks,
                        baked,
                        failed,
                        missingTiles: 0,
                        dirtyTiles: 0,
                        notLoaded));
                }
            }

            string outFile = Path.Combine(
                repoRoot,
                "assets",
                "Data",
                "Nav",
                mapId,
                NavMeshConfigPaths.BakeDiagnosticsFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);
            File.WriteAllText(outFile, JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));
        }

        private sealed class BakeCounter
        {
            private int _baked;
            private int _failed;

            public int Baked => _baked;
            public int Failed => _failed;

            public void MarkBaked() => Interlocked.Increment(ref _baked);
            public void MarkFailed() => Interlocked.Increment(ref _failed);
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
