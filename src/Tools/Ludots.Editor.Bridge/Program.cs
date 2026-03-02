using Ludots.Core.Map.Hex;
using Ludots.Core.Modding;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.NavBake.Recast;
using Ludots.Tool;
using Microsoft.AspNetCore.Http.Features;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Linq;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 1024L * 1024L * 256L;
});

builder.Services.AddCors(o =>
{
    o.AddPolicy("dev", p =>
        p.AllowAnyOrigin()
         .AllowAnyMethod()
         .AllowAnyHeader());
});

var app = builder.Build();
app.UseCors("dev");

app.MapGet("/health", () => Results.Ok(new { ok = true }));

app.MapGet("/api/mods", () =>
{
    string repoRoot = FindAssetsRoot();
    var mods = EditorRepo.DiscoverMods(repoRoot);
    return Results.Ok(new { ok = true, mods });
});

app.MapGet("/api/mods/{modId}/load-order", (string modId) =>
{
    string repoRoot = FindAssetsRoot();
    try
    {
        var ctx = EditorRepo.CreateContext(repoRoot, modId);
        return Results.Ok(new { ok = true, core = true, loadOrder = ctx.LoadOrder, mods = ctx.ModsById.Values });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }
});

app.MapGet("/api/mods/{modId}/maps", (string modId) =>
{
    string repoRoot = FindAssetsRoot();
    try
    {
        var ctx = EditorRepo.CreateContext(repoRoot, modId);
        var maps = EditorRepo.DiscoverMaps(ctx);
        return Results.Ok(new { ok = true, maps });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }
});

app.MapGet("/api/mods/{modId}/maps/{mapId}", (string modId, string mapId) =>
{
    string repoRoot = FindAssetsRoot();
    try
    {
        var ctx = EditorRepo.CreateContext(repoRoot, modId);
        var r = EditorRepo.LoadMergedMapConfig(ctx, mapId);
        if (!r.Found) return Results.NotFound(new { ok = false, error = $"Map not found: {mapId}" });
        return Results.Ok(new { ok = true, map = r.Map, sources = r.Sources });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }
});

app.MapPut("/api/mods/{modId}/maps/{mapId}", async (string modId, string mapId, HttpRequest req) =>
{
    string repoRoot = FindAssetsRoot();
    EditorRepo.ModContext ctx;
    try
    {
        ctx = EditorRepo.CreateContext(repoRoot, modId);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }

    using var sr = new StreamReader(req.Body, Encoding.UTF8, leaveOpen: false);
    string json = await sr.ReadToEndAsync();
    if (string.IsNullOrWhiteSpace(json)) return Results.BadRequest(new { ok = false, error = "Empty body." });

    var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    var map = JsonSerializer.Deserialize<Ludots.Core.Config.MapConfig>(json, opts);
    if (map == null) return Results.BadRequest(new { ok = false, error = "Failed to parse MapConfig." });

    map.Id = mapId;
    string outFile = EditorRepo.ResolveWritableMapConfigPath(ctx, mapId);
    Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);
    File.WriteAllText(outFile, JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true }));

    return Results.Ok(new { ok = true, path = outFile });
});

app.MapGet("/api/mods/{modId}/entity-templates", (string modId) =>
{
    string repoRoot = FindAssetsRoot();
    try
    {
        var ctx = EditorRepo.CreateContext(repoRoot, modId);
        var templates = EditorRepo.LoadMergedEntityTemplates(ctx, includeSources: false, out _);
        return Results.Ok(new { ok = true, templates });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }
});

app.MapGet("/api/mods/{modId}/performers", (string modId) =>
{
    string repoRoot = FindAssetsRoot();
    try
    {
        var ctx = EditorRepo.CreateContext(repoRoot, modId);
        var performers = EditorRepo.LoadMergedPerformers(ctx, includeSources: false, out _);
        return Results.Ok(new { ok = true, performers });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }
});

app.MapGet("/api/mods/{modId}/mesh-assets", (string modId) =>
{
    var primitives = new[]
    {
        new { meshId = Ludots.Core.Presentation.Assets.PrimitiveMeshAssetIds.Cube, kind = "Cube" },
        new { meshId = Ludots.Core.Presentation.Assets.PrimitiveMeshAssetIds.Sphere, kind = "Sphere" }
    };
    return Results.Ok(new { ok = true, primitives });
});

app.MapGet("/api/mods/{modId}/maps/{mapId}/terrain-react", (string modId, string mapId) =>
{
    string repoRoot = FindAssetsRoot();
    try
    {
        var ctx = EditorRepo.CreateContext(repoRoot, modId);
        var mapR = EditorRepo.LoadMergedMapConfig(ctx, mapId);
        if (!mapR.Found) return Results.NotFound(new { ok = false, error = $"Map not found: {mapId}" });
        if (string.IsNullOrWhiteSpace(EditorRepo.GetPrimaryDataFile(mapR.Map))) return Results.BadRequest(new { ok = false, error = "MapConfig.DataFile is empty." });

        if (!EditorRepo.TryResolveDataFile(ctx, EditorRepo.GetPrimaryDataFile(mapR.Map), out var fullPath, out var checkedPaths))
        {
            return Results.NotFound(new { ok = false, error = $"DataFile not found: {EditorRepo.GetPrimaryDataFile(mapR.Map)}", checkedPaths });
        }

        using var fs = File.OpenRead(fullPath);
        using var ms = new MemoryStream();
        EditorTerrainConverter.ConvertVertexMapBinaryToReactTerrain(fs, ms);
        ms.Position = 0;
        return Results.File(ms.ToArray(), "application/octet-stream", fileDownloadName: $"{mapId}_map_data.bin");
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }
});

app.MapPut("/api/mods/{modId}/maps/{mapId}/terrain-react", async (string modId, string mapId, HttpRequest req) =>
{
    string repoRoot = FindAssetsRoot();
    EditorRepo.ModContext ctx;
    try
    {
        ctx = EditorRepo.CreateContext(repoRoot, modId);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }

    var mapR = EditorRepo.LoadMergedMapConfig(ctx, mapId);
    if (!mapR.Found) return Results.NotFound(new { ok = false, error = $"Map not found: {mapId}" });
    if (string.IsNullOrWhiteSpace(EditorRepo.GetPrimaryDataFile(mapR.Map))) return Results.BadRequest(new { ok = false, error = "MapConfig.DataFile is empty." });

    string outFile = EditorRepo.ResolveWritableDataFilePath(ctx, EditorRepo.GetPrimaryDataFile(mapR.Map));
    Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);

    string tempPath = Path.Combine(Path.GetTempPath(), $"ludots_map_{Guid.NewGuid():N}.bin");
    try
    {
        await using (var fs = File.Create(tempPath))
        {
            await req.Body.CopyToAsync(fs);
        }

        using var vtxmStream = new MemoryStream();
        _ = ReactMapDataBinConverter.ConvertToVertexMapBinary(tempPath, vtxmStream);
        vtxmStream.Position = 0;

        await using (var outFs = File.Create(outFile))
        {
            await vtxmStream.CopyToAsync(outFs);
        }
    }
    finally
    {
        try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
    }

    return Results.Ok(new { ok = true, path = outFile });
});

app.MapPost("/api/nav/bake-react", async (HttpRequest req) =>
{
    if (!req.HasFormContentType) return Results.BadRequest(new { error = "Expected multipart/form-data" });
    var form = await req.ReadFormAsync();
    var mapFile = form.Files.GetFile("map");
    if (mapFile == null) return Results.BadRequest(new { error = "Missing form file 'map' (map_data.bin)" });

    var dirtyJson = form.TryGetValue("dirty", out var dirtyVal) ? dirtyVal.ToString() : null;
    var dirtyOnly = ParseBool(form.TryGetValue("dirtyOnly", out var dirtyOnlyVal) ? dirtyOnlyVal.ToString() : null, defaultValue: false);
    var includeNeighbors = ParseBool(form.TryGetValue("includeNeighbors", out var inclVal) ? inclVal.ToString() : null, defaultValue: true);
    var heightScale = ParseFloat(form.TryGetValue("heightScale", out var hsVal) ? hsVal.ToString() : null, 2.0f);
    var minUpDot = ParseFloat(form.TryGetValue("minUpDot", out var mudVal) ? mudVal.ToString() : null, 0.6f);
    var cliffThreshold = ParseInt(form.TryGetValue("cliffThreshold", out var ctVal) ? ctVal.ToString() : null, 1);
    var tileVersion = ParseInt(form.TryGetValue("tileVersion", out var tvVal) ? tvVal.ToString() : null, 1);
    var writeArtifact = ParseBool(form.TryGetValue("artifact", out var artVal) ? artVal.ToString() : null, defaultValue: true);
    var parallel = ParseBool(form.TryGetValue("parallel", out var parVal) ? parVal.ToString() : null, defaultValue: true);
    var maxDegree = ParseInt(form.TryGetValue("maxDegree", out var mdVal) ? mdVal.ToString() : null, Math.Max(1, Environment.ProcessorCount));

    string tempPath = Path.Combine(Path.GetTempPath(), $"ludots_map_{Guid.NewGuid():N}.bin");
    try
    {
        await using (var fs = File.Create(tempPath))
        {
            await mapFile.CopyToAsync(fs);
        }

        using var vtxmStream = new MemoryStream();
        _ = ReactMapDataBinConverter.ConvertToVertexMapBinary(tempPath, vtxmStream);
        vtxmStream.Position = 0;
        var map = VertexMapBinary.Read(vtxmStream);

        var targets = ResolveTargets(map, dirtyJson, includeNeighbors, fallbackToFullWhenNoTargets: !dirtyOnly);
        if (targets.Count == 0)
        {
            return Results.Ok(new
            {
                ok = true,
                okCount = 0,
                failCount = 0,
                tiles = Array.Empty<object>(),
                artifacts = Array.Empty<object>(),
                message = "No targets to bake (dirtyOnly=true and dirty set is empty).",
                config = new { dirtyOnly, includeNeighbors, heightScale, minUpDot, cliffThreshold, tileVersion }
            });
        }
        var cfg = new NavBuildConfig(heightScale, minUpDot, cliffThreshold);

        var results = new TileBakeResult[targets.Count];

        void BakeOne(int i)
        {
            var t = targets[i];
            
            // Use new CDT pipeline
            var pipelineResult = BakePipeline.Execute(map, t.cx, t.cy, (uint)tileVersion, cfg);
            
            // Log debug info to console
            if (pipelineResult.Artifact.DebugLog != null)
            {
                Console.WriteLine($"[Bake ({t.cx},{t.cy})] Debug log:");
                foreach (var line in pipelineResult.Artifact.DebugLog)
                {
                    Console.WriteLine($"  {line}");
                }
            }
            
            if (pipelineResult.Success && pipelineResult.Tile != null)
            {
                using var ms = new MemoryStream();
                NavTileBinary.Write(ms, pipelineResult.Tile);
                results[i] = new TileBakeResult(t.cx, t.cy, Ok: true, Convert.ToBase64String(ms.ToArray()), writeArtifact ? SerializeArtifact(pipelineResult.Artifact) : null);
            }
            else
            {
                results[i] = new TileBakeResult(t.cx, t.cy, Ok: false, null, writeArtifact ? SerializeArtifact(pipelineResult.Artifact) : null);
            }
        }

        if (parallel)
        {
            Parallel.For(0, targets.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, maxDegree) }, BakeOne);
        }
        else
        {
            for (int i = 0; i < targets.Count; i++) BakeOne(i);
        }

        int okCount = 0;
        int failCount = 0;
        var tiles = new List<object>(results.Length);
        var artifacts = new List<object>();
        for (int i = 0; i < results.Length; i++)
        {
            var r = results[i];
            if (r.Ok) okCount++; else failCount++;
            if (!string.IsNullOrEmpty(r.NavTileBase64))
            {
                tiles.Add(new { cx = r.Cx, cy = r.Cy, layer = 0, base64 = r.NavTileBase64 });
            }
            if (!string.IsNullOrEmpty(r.ArtifactJson))
            {
                artifacts.Add(new { cx = r.Cx, cy = r.Cy, json = r.ArtifactJson });
            }
        }

        return Results.Ok(new
        {
            ok = true,
            okCount,
            failCount,
            tiles,
            artifacts,
            targetsCount = targets.Count,
            config = new { dirtyOnly, includeNeighbors, heightScale, minUpDot, cliffThreshold, tileVersion }
        });
    }
    finally
    {
        try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
    }
});

app.MapPost("/api/nav/bake-recast-react", async (HttpRequest req) =>
{
    if (!req.HasFormContentType) return Results.BadRequest(new { error = "Expected multipart/form-data" });
    var form = await req.ReadFormAsync();
    var mapFile = form.Files.GetFile("map");
    if (mapFile == null) return Results.BadRequest(new { error = "Missing form file 'map' (map_data.bin)" });
    var mapId = form.TryGetValue("mapId", out var mapIdVal) ? mapIdVal.ToString() : null;
    if (string.IsNullOrWhiteSpace(mapId)) return Results.BadRequest(new { error = "Missing form field 'mapId'" });

    var dirtyJson = form.TryGetValue("dirty", out var dirtyVal) ? dirtyVal.ToString() : null;
    var dirtyOnly = ParseBool(form.TryGetValue("dirtyOnly", out var dirtyOnlyVal) ? dirtyOnlyVal.ToString() : null, defaultValue: false);
    var includeNeighbors = ParseBool(form.TryGetValue("includeNeighbors", out var inclVal) ? inclVal.ToString() : null, defaultValue: true);
    var heightScale = ParseFloat(form.TryGetValue("heightScale", out var hsVal) ? hsVal.ToString() : null, 2.0f);
    var minUpDot = ParseFloat(form.TryGetValue("minUpDot", out var mudVal) ? mudVal.ToString() : null, 0.6f);
    var cliffThreshold = ParseInt(form.TryGetValue("cliffThreshold", out var ctVal) ? ctVal.ToString() : null, 1);
    var tileVersion = ParseInt(form.TryGetValue("tileVersion", out var tvVal) ? tvVal.ToString() : null, 1);
    var writeArtifact = ParseBool(form.TryGetValue("artifact", out var artVal) ? artVal.ToString() : null, defaultValue: true);
    var parallel = ParseBool(form.TryGetValue("parallel", out var parVal) ? parVal.ToString() : null, defaultValue: true);
    var maxDegree = ParseInt(form.TryGetValue("maxDegree", out var mdVal) ? mdVal.ToString() : null, Math.Max(1, Environment.ProcessorCount));

    string tempPath = Path.Combine(Path.GetTempPath(), $"ludots_map_{Guid.NewGuid():N}.bin");
    try
    {
        await using (var fs = File.Create(tempPath))
        {
            await mapFile.CopyToAsync(fs);
        }

        using var vtxmStream = new MemoryStream();
        _ = ReactMapDataBinConverter.ConvertToVertexMapBinary(tempPath, vtxmStream);
        vtxmStream.Position = 0;
        var map = VertexMapBinary.Read(vtxmStream);

        string repoRoot = FindAssetsRoot();
        string bakeCfgPath = Path.Combine(repoRoot, NavMeshConfigPaths.BakeConfigPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(bakeCfgPath)) return Results.BadRequest(new { error = $"Missing bake config: {bakeCfgPath}" });
        var bakeConfig = JsonSerializer.Deserialize<NavMeshBakeConfig>(File.ReadAllText(bakeCfgPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new NavMeshBakeConfig();
        if (bakeConfig.Profiles == null || bakeConfig.Profiles.Count == 0) return Results.BadRequest(new { error = "NavMeshBakeConfig.profiles is empty." });
        var profiles = bakeConfig.Profiles;
        if (bakeConfig.Layers == null || bakeConfig.Layers.Count == 0) bakeConfig.Layers = new List<NavLayerConfig> { new NavLayerConfig { Id = "Ground", Layer = 0 } };

        NavObstacleSet obstacles = new NavObstacleSet();
        string obsRel = NavAssetPaths.GetObstacleRelativePath(mapId);
        string obsPath = Path.Combine(repoRoot, obsRel.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(obsPath))
        {
            obstacles = JsonSerializer.Deserialize<NavObstacleSet>(File.ReadAllText(obsPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new NavObstacleSet();
        }

        var targets = ResolveTargets(map, dirtyJson, includeNeighbors, fallbackToFullWhenNoTargets: !dirtyOnly);
        if (targets.Count == 0)
        {
            return Results.Ok(new
            {
                ok = true,
                okCount = 0,
                failCount = 0,
                tiles = Array.Empty<object>(),
                artifacts = Array.Empty<object>(),
                message = "No targets to bake (dirtyOnly=true and dirty set is empty).",
                config = new { mapId, dirtyOnly, includeNeighbors, heightScale, minUpDot, cliffThreshold, tileVersion }
            });
        }
        var legacyCfg = new NavBuildConfig(heightScale, minUpDot, cliffThreshold);

        var results = new TileBakeResult[targets.Count];

        void BakeOne(int i)
        {
            var t = targets[i];
            bool okAny = false;
            string? base64 = null;
            string? artJson = null;

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

                        if (layer == 0 && pi == 0)
                        {
                            using var ms = new MemoryStream();
                            NavTileBinary.Write(ms, tile);
                            base64 = Convert.ToBase64String(ms.ToArray());
                            if (writeArtifact) artJson = SerializeArtifact(artifact);
                        }
                        okAny = true;
                    }
                    else
                    {
                        if (layer == 0 && pi == 0 && writeArtifact) artJson = SerializeArtifact(artifact);
                    }
                }
            }

            results[i] = new TileBakeResult(t.cx, t.cy, okAny, base64, artJson);
        }

        if (parallel)
        {
            Parallel.For(0, targets.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, maxDegree) }, BakeOne);
        }
        else
        {
            for (int i = 0; i < targets.Count; i++) BakeOne(i);
        }

        int okCount = 0;
        int failCount = 0;
        var tiles = new List<object>(results.Length);
        var artifacts = new List<object>();
        for (int i = 0; i < results.Length; i++)
        {
            var r = results[i];
            if (r.Ok) okCount++; else failCount++;
            if (!string.IsNullOrEmpty(r.NavTileBase64))
            {
                tiles.Add(new { cx = r.Cx, cy = r.Cy, layer = 0, base64 = r.NavTileBase64 });
            }
            if (!string.IsNullOrEmpty(r.ArtifactJson))
            {
                artifacts.Add(new { cx = r.Cx, cy = r.Cy, json = r.ArtifactJson });
            }
        }

        return Results.Ok(new
        {
            ok = true,
            okCount,
            failCount,
            tiles,
            artifacts,
            targetsCount = targets.Count,
            config = new { mapId, dirtyOnly, includeNeighbors, heightScale, minUpDot, cliffThreshold, tileVersion }
        });
    }
    finally
    {
        try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
    }
});

app.Run("http://localhost:5299");

static float ParseFloat(string? s, float fallback)
{
    if (string.IsNullOrWhiteSpace(s)) return fallback;
    return float.TryParse(s, out var v) ? v : fallback;
}

static int ParseInt(string? s, int fallback)
{
    if (string.IsNullOrWhiteSpace(s)) return fallback;
    return int.TryParse(s, out var v) ? v : fallback;
}

static bool ParseBool(string? s, bool defaultValue)
{
    if (string.IsNullOrWhiteSpace(s)) return defaultValue;
    if (bool.TryParse(s, out var b)) return b;
    return defaultValue;
}

static string SerializeArtifact(NavBakeArtifact artifact)
{
    return JsonSerializer.Serialize(artifact, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true });
}

static List<(int cx, int cy)> ResolveTargets(VertexMap map, string? dirtyJson, bool includeNeighbors, bool fallbackToFullWhenNoTargets)
{
    if (string.IsNullOrWhiteSpace(dirtyJson))
    {
        if (!fallbackToFullWhenNoTargets) return new List<(int cx, int cy)>(0);
        var all = new List<(int cx, int cy)>(map.WidthInChunks * map.HeightInChunks);
        for (int cy = 0; cy < map.HeightInChunks; cy++)
            for (int cx = 0; cx < map.WidthInChunks; cx++)
                all.Add((cx, cy));
        return all;
    }

    string[] keys;
    try
    {
        keys = JsonSerializer.Deserialize<string[]>(dirtyJson) ?? Array.Empty<string>();
    }
    catch
    {
        keys = dirtyJson
            .Split(new[] { '\r', '\n', '\t', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
    }
    var set = new HashSet<(int cx, int cy)>();
    for (int i = 0; i < keys.Length; i++)
    {
        var raw = keys[i].Trim().Trim('"');
        var parts = raw.Split(',');
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

    var targets = new List<(int cx, int cy)>(set.Count);
    foreach (var t in set)
    {
        if (t.cx < 0 || t.cy < 0 || t.cx >= map.WidthInChunks || t.cy >= map.HeightInChunks) continue;
        targets.Add(t);
    }
    if (targets.Count == 0)
    {
        if (!fallbackToFullWhenNoTargets) return targets;
        for (int cy = 0; cy < map.HeightInChunks; cy++)
            for (int cx = 0; cx < map.WidthInChunks; cx++)
                targets.Add((cx, cy));
    }
    return targets;
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

readonly record struct TileBakeResult(int Cx, int Cy, bool Ok, string? NavTileBase64, string? ArtifactJson);

static class EditorRepo
{
    public sealed record ModInfo(string Id, string Name, string Version, int Priority, Dictionary<string, string> Dependencies, string RootPath);

    public sealed class ModContext
    {
        public required string RepoRoot { get; init; }
        public required string TargetModId { get; init; }
        public required Dictionary<string, ModInfo> ModsById { get; init; }
        public required List<string> LoadOrder { get; init; }
    }

    public sealed record MergedMapResult(bool Found, Ludots.Core.Config.MapConfig Map, List<string> Sources);

    public static List<ModInfo> DiscoverMods(string repoRoot)
    {
        var mods = new Dictionary<string, ModInfo>(StringComparer.OrdinalIgnoreCase);

        void ScanModsRoot(string modsRoot)
        {
            if (!Directory.Exists(modsRoot)) return;
            foreach (var dir in Directory.GetDirectories(modsRoot))
            {
                string id = Path.GetFileName(dir);
                string jsonPath = Path.Combine(dir, "mod.json");
                if (!File.Exists(jsonPath)) continue;
                if (mods.ContainsKey(id)) continue;
                mods[id] = ReadModInfo(id, dir, jsonPath);
            }
        }

        ScanModsRoot(Path.Combine(repoRoot, "src", "Mods"));
        ScanModsRoot(Path.Combine(repoRoot, "assets", "Mods"));

        return mods.Values.OrderBy(m => m.Priority).ThenBy(m => m.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static ModContext CreateContext(string repoRoot, string targetModId)
    {
        var mods = DiscoverMods(repoRoot).ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
        if (!mods.ContainsKey(targetModId))
        {
            throw new InvalidOperationException($"Unknown mod: {targetModId}");
        }
        var order = ResolveLoadOrder(mods, targetModId);
        return new ModContext
        {
            RepoRoot = repoRoot,
            TargetModId = targetModId,
            ModsById = mods,
            LoadOrder = order
        };
    }

    public static List<string> DiscoverMaps(ModContext ctx)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddMapsFromDir(string dir)
        {
            if (!Directory.Exists(dir)) return;
            foreach (var file in Directory.GetFiles(dir, "*.json", SearchOption.TopDirectoryOnly))
            {
                set.Add(Path.GetFileNameWithoutExtension(file));
            }
        }

        AddMapsFromDir(Path.Combine(ctx.RepoRoot, "assets", "Configs", "Maps"));
        AddMapsFromDir(Path.Combine(ctx.RepoRoot, "assets", "Maps"));

        for (int i = 0; i < ctx.LoadOrder.Count; i++)
        {
            var mod = ctx.ModsById[ctx.LoadOrder[i]];
            AddMapsFromDir(Path.Combine(mod.RootPath, "assets", "Configs", "Maps"));
            AddMapsFromDir(Path.Combine(mod.RootPath, "assets", "Maps"));
        }

        return set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static MergedMapResult LoadMergedMapConfig(ModContext ctx, string mapId)
    {
        var sources = new List<string>();
        var merged = new Ludots.Core.Config.MapConfig { Id = mapId };
        bool foundAny = false;

        void TryLoad(string path)
        {
            if (!File.Exists(path)) return;
            foundAny = true;
            var cfg = JsonSerializer.Deserialize<Ludots.Core.Config.MapConfig>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (cfg == null) return;
            MergeMapConfig(merged, cfg);
            sources.Add(path);
        }

        string coreCfg = Path.Combine(ctx.RepoRoot, "assets", "Configs", "Maps", $"{mapId}.json");
        string coreAssets = Path.Combine(ctx.RepoRoot, "assets", "Maps", $"{mapId}.json");
        TryLoad(coreCfg);
        TryLoad(coreAssets);

        for (int i = 0; i < ctx.LoadOrder.Count; i++)
        {
            var mod = ctx.ModsById[ctx.LoadOrder[i]];
            TryLoad(Path.Combine(mod.RootPath, "assets", "Configs", "Maps", $"{mapId}.json"));
            TryLoad(Path.Combine(mod.RootPath, "assets", "Maps", $"{mapId}.json"));
        }

        if (!foundAny) return new MergedMapResult(false, merged, sources);

        if (!string.IsNullOrWhiteSpace(merged.ParentId))
        {
            var parent = LoadMergedMapConfig(ctx, merged.ParentId);
            if (parent.Found)
            {
                var child = merged;
                merged = parent.Map;
                MergeMapConfig(merged, child);
                sources.AddRange(parent.Sources);
            }
        }

        return new MergedMapResult(true, merged, sources);
    }

    public static string ResolveWritableMapConfigPath(ModContext ctx, string mapId)
    {
        var mod = ctx.ModsById[ctx.TargetModId];
        return Path.Combine(mod.RootPath, "assets", "Maps", $"{SanitizeId(mapId)}.json");
    }

    public static bool TryResolveDataFile(ModContext ctx, string dataFile, out string fullPath, out List<string> checkedPaths)
    {
        var checkedLocal = new List<string>();
        string found = string.Empty;

        if (string.IsNullOrWhiteSpace(dataFile))
        {
            fullPath = string.Empty;
            checkedPaths = checkedLocal;
            return false;
        }
        string rel = dataFile.TrimStart('\\', '/').Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var candidates = new List<string>(6) { rel };
        if (!rel.StartsWith("assets" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add("assets" + Path.DirectorySeparatorChar + rel);
        }
        if (!rel.Contains("Data" + Path.DirectorySeparatorChar + "Maps", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(Path.Combine("assets", "Data", "Maps", rel));
        }

        bool TryFindInRootLocal(string root)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                string p = Path.Combine(root, candidates[i]);
                checkedLocal.Add(p);
                if (File.Exists(p))
                {
                    found = p;
                    return true;
                }
            }
            return false;
        }

        if (TryFindInRootLocal(ctx.RepoRoot))
        {
            fullPath = found;
            checkedPaths = checkedLocal;
            return true;
        }
        for (int i = 0; i < ctx.LoadOrder.Count; i++)
        {
            var mod = ctx.ModsById[ctx.LoadOrder[i]];
            if (TryFindInRootLocal(mod.RootPath))
            {
                fullPath = found;
                checkedPaths = checkedLocal;
                return true;
            }
        }

        fullPath = string.Empty;
        checkedPaths = checkedLocal;
        return false;
    }

    public static string ResolveWritableDataFilePath(ModContext ctx, string dataFile)
    {
        var mod = ctx.ModsById[ctx.TargetModId];
        string rel = dataFile.TrimStart('\\', '/').Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        if (rel.StartsWith("assets" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            rel = rel.Substring(("assets" + Path.DirectorySeparatorChar).Length);
        }
        if (rel.StartsWith(Path.Combine("Data", "Maps") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            rel = rel.Substring((Path.Combine("Data", "Maps") + Path.DirectorySeparatorChar).Length);
        }
        return Path.Combine(mod.RootPath, "assets", "Data", "Maps", rel);
    }

    public static JsonNode[] LoadMergedEntityTemplates(ModContext ctx, bool includeSources, out List<string> sources)
    {
        var sourcesLocal = new List<string>();
        var mergedNodes = new Dictionary<string, JsonNode>(StringComparer.OrdinalIgnoreCase);

        void Load(string path)
        {
            if (!File.Exists(path)) return;
            sourcesLocal.Add(path);
            var node = JsonNode.Parse(File.ReadAllText(path));
            if (node is not JsonArray arr) return;
            foreach (var item in arr)
            {
                if (item is not JsonObject obj) continue;
                if (!TryReadId(obj, out var id)) continue;
                if (mergedNodes.TryGetValue(id, out var existing))
                {
                    Ludots.Core.Config.JsonMerger.Merge(existing, obj);
                }
                else
                {
                    mergedNodes[id] = obj.DeepClone();
                }
            }
        }

        Load(Path.Combine(ctx.RepoRoot, "assets", "Configs", "Entities", "templates.json"));
        Load(Path.Combine(ctx.RepoRoot, "assets", "Entities", "templates.json"));
        for (int i = 0; i < ctx.LoadOrder.Count; i++)
        {
            var mod = ctx.ModsById[ctx.LoadOrder[i]];
            Load(Path.Combine(mod.RootPath, "assets", "Entities", "templates.json"));
            Load(Path.Combine(mod.RootPath, "assets", "Configs", "Entities", "templates.json"));
        }

        sources = sourcesLocal;
        return mergedNodes.Values.OrderBy(n => n?["Id"]?.ToString() ?? n?["id"]?.ToString() ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static JsonNode[] LoadMergedPerformers(ModContext ctx, bool includeSources, out List<string> sources)
    {
        var sourcesLocal = new List<string>();
        var defs = new Dictionary<int, JsonNode>();

        void Load(string path)
        {
            if (!File.Exists(path)) return;
            sourcesLocal.Add(path);
            var node = JsonNode.Parse(File.ReadAllText(path));
            if (node is not JsonArray arr) return;
            foreach (var item in arr)
            {
                if (item is not JsonObject obj) continue;
                int id = obj["id"]?.GetValue<int>() ?? 0;
                if (id <= 0) continue;
                defs[id] = obj.DeepClone();
            }
        }

        Load(Path.Combine(ctx.RepoRoot, "assets", "Configs", "Presentation", "performers.json"));
        Load(Path.Combine(ctx.RepoRoot, "assets", "Presentation", "performers.json"));
        for (int i = 0; i < ctx.LoadOrder.Count; i++)
        {
            var mod = ctx.ModsById[ctx.LoadOrder[i]];
            Load(Path.Combine(mod.RootPath, "assets", "Presentation", "performers.json"));
            Load(Path.Combine(mod.RootPath, "assets", "Configs", "Presentation", "performers.json"));
        }

        sources = sourcesLocal;
        return defs.OrderBy(kvp => kvp.Key).Select(kvp => kvp.Value).ToArray();
    }


    public static string GetPrimaryDataFile(Ludots.Core.Config.MapConfig map)
    {
        if (map.Boards != null)
        {
            foreach (var b in map.Boards)
            {
                if (!string.IsNullOrWhiteSpace(b.DataFile)) return b.DataFile;
            }
        }
        return null;
    }

    private static void MergeMapConfig(Ludots.Core.Config.MapConfig target, Ludots.Core.Config.MapConfig source)
    {
        if (!string.IsNullOrEmpty(source.ParentId)) target.ParentId = source.ParentId;

        if (source.Dependencies != null)
        {
            foreach (var kvp in source.Dependencies)
            {
                target.Dependencies[kvp.Key] = kvp.Value;
            }
        }
        if (source.Entities != null) target.Entities.AddRange(source.Entities);
        if (source.Tags != null)
        {
            for (int i = 0; i < source.Tags.Count; i++)
            {
                var t = source.Tags[i];
                if (string.IsNullOrWhiteSpace(t)) continue;
                bool exists = false;
                for (int j = 0; j < target.Tags.Count; j++)
                {
                    if (string.Equals(target.Tags[j], t, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists)
                {
                    target.Tags.Add(t);
                }
            }
        }
        if (source.Boards != null)
        {
            foreach (var srcBoard in source.Boards)
            {
                bool found = false;
                for (int i = 0; i < target.Boards.Count; i++)
                {
                    if (string.Equals(target.Boards[i].Name, srcBoard.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        target.Boards[i] = srcBoard;
                        found = true;
                        break;
                    }
                }
                if (!found) target.Boards.Add(srcBoard);
            }
        }
        if (source.TriggerTypes != null)
        {
            foreach (var tt in source.TriggerTypes)
            {
                if (!target.TriggerTypes.Contains(tt)) target.TriggerTypes.Add(tt);
            }
        }
    }

    private static ModInfo ReadModInfo(string id, string rootPath, string modJsonPath)
    {
        var manifest = ModManifestJson.ParseStrict(File.ReadAllText(modJsonPath), modJsonPath);
        return new ModInfo(
            id,
            manifest.Name,
            manifest.Version,
            manifest.Priority,
            new Dictionary<string, string>(manifest.Dependencies, StringComparer.Ordinal),
            rootPath);
    }

    private static List<string> ResolveLoadOrder(Dictionary<string, ModInfo> mods, string root)
    {
        var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddRec(string id)
        {
            if (!required.Add(id)) return;
            if (!mods.TryGetValue(id, out var m)) throw new InvalidOperationException($"Missing mod dependency: {id}");
            foreach (var dep in m.Dependencies.Keys) AddRec(dep);
        }

        AddRec(root);

        var indeg = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var edges = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in required)
        {
            indeg[id] = 0;
            edges[id] = new List<string>();
        }
        foreach (var id in required)
        {
            var m = mods[id];
            foreach (var dep in m.Dependencies.Keys)
            {
                if (!required.Contains(dep)) continue;
                edges[dep].Add(id);
                indeg[id]++;
            }
        }

        var result = new List<string>(required.Count);
        var chosen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (result.Count < required.Count)
        {
            string next = indeg
                .Where(kvp => kvp.Value == 0 && !chosen.Contains(kvp.Key))
                .Select(kvp => kvp.Key)
                .OrderBy(id => mods[id].Priority)
                .ThenBy(id => id, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (next == null) throw new InvalidOperationException("Dependency cycle detected.");

            result.Add(next);
            chosen.Add(next);
            indeg[next] = -1;
            var outs = edges[next];
            for (int i = 0; i < outs.Count; i++) indeg[outs[i]]--;
        }

        return result;
    }

    private static bool TryReadId(JsonObject obj, out string id)
    {
        id = string.Empty;
        if (obj.TryGetPropertyValue("Id", out var idNode) && idNode != null)
        {
            id = idNode.ToString();
            return !string.IsNullOrWhiteSpace(id);
        }
        if (obj.TryGetPropertyValue("id", out idNode) && idNode != null)
        {
            id = idNode.ToString();
            return !string.IsNullOrWhiteSpace(id);
        }
        foreach (var kvp in obj)
        {
            if (!string.Equals(kvp.Key, "Id", StringComparison.OrdinalIgnoreCase)) continue;
            if (kvp.Value == null) return false;
            id = kvp.Value.ToString();
            return !string.IsNullOrWhiteSpace(id);
        }
        return false;
    }

    private static string SanitizeId(string raw)
    {
        if (raw == null) return "null";
        raw = raw.Trim();
        if (raw.Length == 0) return "empty";
        var sb = new StringBuilder(raw.Length);
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            bool ok = char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.';
            sb.Append(ok ? c : '_');
        }
        return sb.ToString();
    }
}
