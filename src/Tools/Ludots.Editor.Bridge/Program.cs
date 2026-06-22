using Ludots.Core.Map.Hex;
using Ludots.Core.Map.Board;
using Ludots.Core.Modding;
using Ludots.Launcher.Backend;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.Terrain;
using Ludots.Core.Physics2D.Navigation;
using Ludots.NavBake.Recast;
using Ludots.Tool;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.FileProviders;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Linq;
using System.Globalization;

var repoRoot = FindAssetsRoot();
var launcher = new LauncherService(repoRoot);
var launcherDistPath = Path.Combine(repoRoot, "src", "Tools", "Ludots.Launcher.React", "dist");

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

if (Directory.Exists(launcherDistPath))
{
    var launcherDistProvider = new PhysicalFileProvider(launcherDistPath);
    app.Use(async (context, next) =>
    {
        if (string.Equals(context.Request.Path.Value, "/", StringComparison.Ordinal) ||
            string.Equals(context.Request.Path.Value, "/launcher", StringComparison.Ordinal))
        {
            context.Response.Redirect("/launcher/index.html");
            return;
        }

        await next();
    });
    app.UseDefaultFiles(new DefaultFilesOptions
    {
        FileProvider = launcherDistProvider,
        RequestPath = "/launcher"
    });
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = launcherDistProvider,
        RequestPath = "/launcher"
    });
}

app.MapGet("/health", () => Results.Ok(new { ok = true }));

app.MapGet("/api/launcher/state", () =>
{
    return Results.Ok(new
    {
        ok = true,
        state = launcher.GetState(),
        mods = launcher.DiscoverMods()
    });
});

app.MapGet("/api/presets", () =>
{
    var state = launcher.GetState();
    return Results.Ok(new
    {
        ok = true,
        presets = state.Presets,
        selectedPresetId = state.SelectedPresetId
    });
});

app.MapPost("/api/presets", async (HttpRequest req) =>
{
    using var sr = new StreamReader(req.Body);
    var body = await sr.ReadToEndAsync();
    var payload = JsonSerializer.Deserialize<JsonElement>(body);

    if (!payload.TryGetProperty("name", out var nameElement) || string.IsNullOrWhiteSpace(nameElement.GetString()))
    {
        return Results.BadRequest(new { ok = false, error = "Missing 'name'." });
    }

    var modIds = new List<string>();
    if (payload.TryGetProperty("activeModIds", out var modIdsElement) && modIdsElement.ValueKind == JsonValueKind.Array)
    {
        foreach (var modIdElement in modIdsElement.EnumerateArray())
        {
            if (modIdElement.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(modIdElement.GetString()))
            {
                modIds.Add(modIdElement.GetString()!);
            }
        }
    }

    var preset = launcher.SavePreset(
        payload.TryGetProperty("presetId", out var presetIdElement) && presetIdElement.ValueKind == JsonValueKind.String
            ? presetIdElement.GetString()
            : null,
        nameElement.GetString()!,
        modIds,
        includeDependencies: !payload.TryGetProperty("includeDependencies", out var includeDependenciesElement) || includeDependenciesElement.ValueKind != JsonValueKind.False,
        selectAfterSave: !payload.TryGetProperty("selectAfterSave", out var selectAfterSaveElement) || selectAfterSaveElement.ValueKind != JsonValueKind.False);

    return Results.Ok(new { ok = true, preset, state = launcher.GetState() });
});

app.MapPost("/api/presets/select", async (HttpRequest req) =>
{
    using var sr = new StreamReader(req.Body);
    var body = await sr.ReadToEndAsync();
    var payload = JsonSerializer.Deserialize<JsonElement>(body);
    var presetId = payload.TryGetProperty("presetId", out var presetIdElement) && presetIdElement.ValueKind == JsonValueKind.String
        ? presetIdElement.GetString()
        : null;

    var state = launcher.SelectPreset(presetId);
    return Results.Ok(new { ok = true, state });
});

app.MapDelete("/api/presets/{presetId}", (string presetId) =>
{
    launcher.DeletePreset(presetId);
    return Results.Ok(new { ok = true, state = launcher.GetState() });
});

app.MapGet("/api/platforms", () =>
{
    var state = launcher.GetState();
    return Results.Ok(new
    {
        ok = true,
        platforms = state.Platforms,
        selectedPlatformId = state.SelectedPlatformId
    });
});

app.MapPost("/api/platforms/select", async (HttpRequest req) =>
{
    using var sr = new StreamReader(req.Body);
    var body = await sr.ReadToEndAsync();
    var payload = JsonSerializer.Deserialize<JsonElement>(body);
    if (!payload.TryGetProperty("platformId", out var platformIdElement) || string.IsNullOrWhiteSpace(platformIdElement.GetString()))
    {
        return Results.BadRequest(new { ok = false, error = "Missing 'platformId'." });
    }

    var state = launcher.SelectPlatform(platformIdElement.GetString()!);
    return Results.Ok(new { ok = true, state });
});

app.MapGet("/api/mods", () =>
{
    var mods = launcher.DiscoverMods();
    return Results.Ok(new { ok = true, mods });
});

app.MapGet("/api/mods/{modId}/thumbnail", (string modId) =>
{
    var mods = launcher.DiscoverMods();
    var mod = mods.FirstOrDefault(m => string.Equals(m.Id, modId, StringComparison.OrdinalIgnoreCase));
    if (mod == null) return Results.NotFound();

    foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".webp" })
    {
        var path = Path.Combine(mod.RootPath, "assets", "Launcher", "thumbnail" + ext);
        if (File.Exists(path))
        {
            var contentType = ext switch { ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".webp" => "image/webp", _ => "application/octet-stream" };
            return Results.File(File.ReadAllBytes(path), contentType);
        }
    }
    return Results.NotFound();
});

app.MapGet("/api/mods/{modId}/readme", (string modId) =>
{
    var mods = launcher.DiscoverMods();
    var mod = mods.FirstOrDefault(m => string.Equals(m.Id, modId, StringComparison.OrdinalIgnoreCase));
    if (mod == null) return Results.NotFound(new { ok = false });

    var readmePath = Path.Combine(mod.RootPath, "README.md");
    if (!File.Exists(readmePath)) return Results.NotFound(new { ok = false });

    return Results.Ok(new { ok = true, content = File.ReadAllText(readmePath) });
});

app.MapGet("/api/mods/{modId}/changelog", (string modId) =>
{
    var mods = launcher.DiscoverMods();
    var mod = mods.FirstOrDefault(m => string.Equals(m.Id, modId, StringComparison.OrdinalIgnoreCase));
    if (mod == null) return Results.NotFound(new { ok = false });

    if (string.IsNullOrWhiteSpace(mod.ChangelogFile))
        return Results.NotFound(new { ok = false });

    var changelogPath = Path.Combine(mod.RootPath, mod.ChangelogFile);
    if (!File.Exists(changelogPath)) return Results.NotFound(new { ok = false });

    return Results.Ok(new { ok = true, content = File.ReadAllText(changelogPath) });
});

app.MapGet("/api/workspace", () =>
{
    var state = launcher.GetState();
    return Results.Ok(new { ok = true, sources = state.WorkspaceSources });
});

app.MapPost("/api/workspace/add-source", async (HttpRequest req) =>
{
    using var sr = new StreamReader(req.Body);
    string body = await sr.ReadToEndAsync();
    var payload = JsonSerializer.Deserialize<JsonElement>(body);
    if (!payload.TryGetProperty("path", out var pathEl))
        return Results.BadRequest(new { ok = false, error = "Missing 'path' field" });

    string newSource = pathEl.GetString() ?? "";
    if (string.IsNullOrWhiteSpace(newSource) || !Directory.Exists(newSource))
        return Results.BadRequest(new { ok = false, error = $"Directory not found: {newSource}" });

    var state = launcher.AddWorkspaceSource(newSource);
    return Results.Ok(new { ok = true, sources = state.WorkspaceSources, state });
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
        new { meshKey = Ludots.Core.Presentation.Assets.WellKnownMeshKeys.Cube, kind = "Cube" },
        new { meshKey = Ludots.Core.Presentation.Assets.WellKnownMeshKeys.Sphere, kind = "Sphere" }
    };
    return Results.Ok(new { ok = true, primitives });
});

app.MapGet("/api/mods/{modId}/navigation-config", (string modId) =>
{
    string repoRoot = FindAssetsRoot();
    try
    {
        var ctx = EditorRepo.CreateContext(repoRoot, modId);
        JsonNode agentProfiles = EditorRepo.LoadMergedNavigationJson(
            ctx,
            "agent_profiles.json",
            defaultValue: new JsonArray(),
            out List<string> agentSources);
        JsonNode navmesh = EditorRepo.LoadMergedNavigationJson(
            ctx,
            "navmesh.json",
            defaultValue: new JsonObject(),
            out List<string> navmeshSources);

        NavMeshBakeConfigContext loaded = NavMeshBakeConfigLoader.LoadContextFromRepoRoot(repoRoot, modId);
        return Results.Ok(new
        {
            ok = true,
            agentProfiles,
            navmesh,
            sources = new { agentProfiles = agentSources, navmesh = navmeshSources },
            validated = new
            {
                profileCount = loaded.AgentProfiles.Count,
                bakeProfileCount = loaded.Config.Profiles.Count,
                layerCount = loaded.Config.Layers.Count,
                areaCount = loaded.Config.Areas.Count,
                mode = loaded.Config.Mode,
                algorithm = loaded.Config.Algorithm
            }
        });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }
});

app.MapPut("/api/mods/{modId}/navigation-config", async (string modId, HttpRequest req) =>
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

    JsonNode? root;
    try
    {
        root = JsonNode.Parse(json);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { ok = false, error = $"Invalid JSON: {ex.Message}" });
    }

    if (root is not JsonObject obj)
    {
        return Results.BadRequest(new { ok = false, error = "Body must be a JSON object." });
    }

    JsonNode? agentProfiles = obj["agentProfiles"];
    JsonNode? navmesh = obj["navmesh"];
    if (agentProfiles is not JsonArray)
    {
        return Results.BadRequest(new { ok = false, error = "agentProfiles must be an array." });
    }

    if (navmesh is not JsonObject)
    {
        return Results.BadRequest(new { ok = false, error = "navmesh must be an object." });
    }

    string agentFile = EditorRepo.ResolveWritableNavigationConfigPath(ctx, "agent_profiles.json");
    string navmeshFile = EditorRepo.ResolveWritableNavigationConfigPath(ctx, "navmesh.json");
    Directory.CreateDirectory(Path.GetDirectoryName(agentFile)!);
    Directory.CreateDirectory(Path.GetDirectoryName(navmeshFile)!);

    string? previousAgentProfiles = File.Exists(agentFile) ? File.ReadAllText(agentFile) : null;
    string? previousNavmesh = File.Exists(navmeshFile) ? File.ReadAllText(navmeshFile) : null;
    string agentProfilesText = agentProfiles.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    string navmeshText = navmesh.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    try
    {
        File.WriteAllText(agentFile, agentProfilesText);
        File.WriteAllText(navmeshFile, navmeshText);
        NavMeshBakeConfigContext loaded = NavMeshBakeConfigLoader.LoadContextFromRepoRoot(repoRoot, modId);
        return Results.Ok(new
        {
            ok = true,
            paths = new { agentProfiles = agentFile, navmesh = navmeshFile },
            validated = new
            {
                profileCount = loaded.AgentProfiles.Count,
                bakeProfileCount = loaded.Config.Profiles.Count,
                layerCount = loaded.Config.Layers.Count,
                areaCount = loaded.Config.Areas.Count,
                mode = loaded.Config.Mode,
                algorithm = loaded.Config.Algorithm
            }
        });
    }
    catch (Exception ex)
    {
        RestoreNavigationConfigFile(agentFile, previousAgentProfiles);
        RestoreNavigationConfigFile(navmeshFile, previousNavmesh);
        return Results.BadRequest(new
        {
            ok = false,
            error = $"Saved files failed validation: {ex.Message}",
            paths = new { agentProfiles = agentFile, navmesh = navmeshFile }
        });
    }
});

app.MapGet("/api/mods/{modId}/maps/{mapId}/terrain-react", (string modId, string mapId) =>
{
    string repoRoot = FindAssetsRoot();
    try
    {
        var ctx = EditorRepo.CreateContext(repoRoot, modId);
        var mapR = EditorRepo.LoadMergedMapConfig(ctx, mapId);
        if (!mapR.Found) return Results.NotFound(new { ok = false, error = $"Map not found: {mapId}" });
        var dataFile = EditorRepo.ResolvePrimaryBoardDataFile(mapR.Map);
        if (string.IsNullOrWhiteSpace(dataFile))
            return Results.BadRequest(new { ok = false, error = "MapConfig.Boards[*].DataFile is empty." });

        if (!EditorRepo.TryResolveDataFile(ctx, dataFile, out var fullPath, out var checkedPaths))
        {
            return Results.NotFound(new { ok = false, error = $"DataFile not found: {dataFile}", checkedPaths });
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
    var dataFile = EditorRepo.ResolvePrimaryBoardDataFile(mapR.Map);
    if (string.IsNullOrWhiteSpace(dataFile))
        return Results.BadRequest(new { ok = false, error = "MapConfig.Boards[*].DataFile is empty." });

    string outFile = EditorRepo.ResolveWritableDataFilePath(ctx, dataFile);
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

        IReadOnlyList<NavBakeTileCoord> targets = NavBakeTileSelection.Resolve(
            new VertexMapLogicTerrainField(map),
            dirtyJson,
            includeNeighbors,
            dirtyOnly);
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
        return Results.BadRequest(new
        {
            ok = false,
            error = "Editor CDT preview requires an authored NavMeshBakeConfig from the unified config pipeline; generated layer/profile defaults are forbidden.",
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
    var modId = form.TryGetValue("modId", out var modIdVal) ? modIdVal.ToString() : null;
    var dirtyJson = form.TryGetValue("dirty", out var dirtyVal) ? dirtyVal.ToString() : null;

    if (TryReadRecastReactCommonOptions(
        form,
        out bool dirtyOnly,
        out bool includeNeighbors,
        out float heightScale,
        out float minUpDot,
        out int cliffThreshold,
        out int tileVersion,
        out bool parallel,
        out int maxDegree) is { } commonError)
    {
        return commonError;
    }

    if (TryReadOptionalBool(form, "artifact", true, out bool writeArtifact) is { } artifactError) return artifactError;
    if (TryReadOptionalBool(form, "largeBake", false, out bool largeBakeApproved) is { } largeBakeError) return largeBakeError;
    string? acceptedEstimateHash = ReadOptionalString(form, "estimateHash");

    string tempPath = Path.Combine(Path.GetTempPath(), $"ludots_map_{Guid.NewGuid():N}.bin");
    try
    {
        await using (var fs = File.Create(tempPath))
        {
            await mapFile.CopyToAsync(fs);
        }

        if (TryBuildEditorUploadRecastContext(
            tempPath,
            mapFile.FileName,
            mapId,
            modId,
            dirtyJson,
            dirtyOnly,
            includeNeighbors,
            heightScale,
            minUpDot,
            cliffThreshold,
            tileVersion,
            parallel,
            maxDegree,
            out string repoRoot,
            out NavBakeContext navBakeContext,
            out NavObstacleSet obstacles,
            out IReadOnlyList<NavBakeTileCoord> targets,
            out IResult? buildError))
        {
            NavBakeEstimateReport estimate = NavBakeEstimator.Estimate(navBakeContext);
            try
            {
                NavBakeEstimator.EnsureBakeAllowed(estimate, largeBakeApproved, acceptedEstimateHash);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { ok = false, error = ex.Message, estimate });
            }

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
                    estimate,
                    config = new { mapId, modId, dirtyOnly, includeNeighbors, heightScale, minUpDot, cliffThreshold, tileVersion, obstacleCount = obstacles.Obstacles.Count }
                });
            }

            var navBakeResult = new NavBakeService(new RecastNavBakeAlgorithm(), new CdtNavBakeAlgorithm()).Bake(navBakeContext);
            var artifacts = new List<object>(navBakeResult.Entries.Count);
            for (int i = 0; i < navBakeResult.Entries.Count; i++)
            {
                var r = navBakeResult.Entries[i];
                if (writeArtifact)
                {
                    artifacts.Add(new { cx = r.Target.ChunkX, cy = r.Target.ChunkY, layer = r.Layer, profileId = r.ProfileId, json = SerializeArtifact(r.Artifact) });
                }
            }

            if (navBakeResult.FailureCount > 0)
            {
                return Results.BadRequest(new
                {
                    ok = false,
                    error = "Nav bake failed; no NavTile artifacts were written.",
                    okCount = navBakeResult.SuccessCount,
                    failCount = navBakeResult.FailureCount,
                    artifacts,
                    targetsCount = targets.Count,
                    estimate,
                    config = new { mapId, modId, dirtyOnly, includeNeighbors, heightScale, minUpDot, cliffThreshold, tileVersion, obstacleCount = obstacles.Obstacles.Count }
                });
            }

            var tiles = new List<object>(targets.Count);
            string previewProfileId = navBakeContext.Config.Profiles[0].Id;
            for (int i = 0; i < navBakeResult.Entries.Count; i++)
            {
                var r = navBakeResult.Entries[i];
                string rel = NavAssetPaths.GetNavTileRelativePath(mapId, r.Layer, r.ProfileId, r.Target.ChunkX, r.Target.ChunkY);
                string outFile = Path.Combine(repoRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);
                using (var fs = File.Create(outFile))
                {
                    NavTileBinary.Write(fs, r.Tile);
                }

                if (r.Layer == 0 && string.Equals(r.ProfileId, previewProfileId, StringComparison.Ordinal))
                {
                    tiles.Add(new { cx = r.Target.ChunkX, cy = r.Target.ChunkY, layer = r.Layer, profileId = r.ProfileId, base64 = Convert.ToBase64String(r.ToTileBytes()) });
                }
            }

            return Results.Ok(new
            {
                ok = true,
                okCount = navBakeResult.SuccessCount,
                failCount = navBakeResult.FailureCount,
                tiles,
                artifacts,
                targetsCount = targets.Count,
                estimate,
                config = new { mapId, modId, dirtyOnly, includeNeighbors, heightScale, minUpDot, cliffThreshold, tileVersion, obstacleCount = obstacles.Obstacles.Count }
            });
        }

        return buildError!;
    }
    finally
    {
        try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
    }
});

app.MapPost("/api/nav/estimate-recast-react", async (HttpRequest req) =>
{
    if (!req.HasFormContentType) return Results.BadRequest(new { error = "Expected multipart/form-data" });
    var form = await req.ReadFormAsync();
    var mapFile = form.Files.GetFile("map");
    if (mapFile == null) return Results.BadRequest(new { error = "Missing form file 'map' (map_data.bin)" });
    var mapId = form.TryGetValue("mapId", out var mapIdVal) ? mapIdVal.ToString() : null;
    if (string.IsNullOrWhiteSpace(mapId)) return Results.BadRequest(new { error = "Missing form field 'mapId'" });
    var modId = form.TryGetValue("modId", out var modIdVal) ? modIdVal.ToString() : null;
    var dirtyJson = form.TryGetValue("dirty", out var dirtyVal) ? dirtyVal.ToString() : null;

    if (TryReadRecastReactCommonOptions(
        form,
        out bool dirtyOnly,
        out bool includeNeighbors,
        out float heightScale,
        out float minUpDot,
        out int cliffThreshold,
        out int tileVersion,
        out bool parallel,
        out int maxDegree) is { } commonError)
    {
        return commonError;
    }

    string tempPath = Path.Combine(Path.GetTempPath(), $"ludots_map_{Guid.NewGuid():N}.bin");
    try
    {
        await using (var fs = File.Create(tempPath))
        {
            await mapFile.CopyToAsync(fs);
        }

        if (TryBuildEditorUploadRecastContext(
            tempPath,
            mapFile.FileName,
            mapId,
            modId,
            dirtyJson,
            dirtyOnly,
            includeNeighbors,
            heightScale,
            minUpDot,
            cliffThreshold,
            tileVersion,
            parallel,
            maxDegree,
            out _,
            out NavBakeContext navBakeContext,
            out NavObstacleSet obstacles,
            out _,
            out IResult? buildError))
        {
            NavBakeEstimateReport estimate = NavBakeEstimator.Estimate(navBakeContext);
            return Results.Ok(new
            {
                ok = true,
                estimate,
                config = new { mapId, modId, dirtyOnly, includeNeighbors, heightScale, minUpDot, cliffThreshold, tileVersion, obstacleCount = obstacles.Obstacles.Count }
            });
        }

        return buildError!;
    }
    finally
    {
        try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
    }
});

app.MapPost("/api/mods/create", async (HttpRequest req) =>
{
    using var sr = new StreamReader(req.Body);
    string body = await sr.ReadToEndAsync();
    var payload = JsonSerializer.Deserialize<JsonElement>(body);
    
    if (!payload.TryGetProperty("id", out var idEl) || string.IsNullOrWhiteSpace(idEl.GetString()))
        return Results.BadRequest(new { ok = false, error = "Missing 'id'" });
    
    string modId = idEl.GetString()!;
    string template = "empty";
    if (payload.TryGetProperty("template", out var tplEl) && tplEl.ValueKind == JsonValueKind.String)
        template = tplEl.GetString() ?? "empty";
    
    string? targetDir = null;
    if (payload.TryGetProperty("dir", out var dirEl) && dirEl.ValueKind == JsonValueKind.String)
        targetDir = dirEl.GetString();

    try
    {
        var output = await launcher.CreateModAsync(modId, template, targetDir);
        return Results.Ok(new { ok = true, modId, output, mods = launcher.DiscoverMods() });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }
});

app.MapPost("/api/mods/{modId}/fix-project", (string modId) =>
{
    try
    {
        var projectPath = launcher.FixModProject(modId);
        return Results.Ok(new { ok = true, projectPath, mods = launcher.DiscoverMods() });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }
});

app.MapPost("/api/mods/{modId}/build", async (string modId) =>
{
    try
    {
        var result = await launcher.BuildModAsync(modId);
        return Results.Ok(new { ok = result.Ok, result, mods = launcher.DiscoverMods() });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }
});

app.MapPost("/api/mods/build-all", async (HttpRequest req) =>
{
    using var sr = new StreamReader(req.Body);
    string body = await sr.ReadToEndAsync();
    var payload = JsonSerializer.Deserialize<JsonElement>(body);

    try
    {
        var selectors = ResolveSelectorsFromPayload(launcher, payload, allowDefaultPreset: false);
        var results = await launcher.BuildAsync(selectors, ResolveAdapterFromPayload(launcher, payload), ResolveBuildModeFromPayload(payload));
        return Results.Ok(new
        {
            ok = results.All(result => result.Ok),
            results,
            mods = launcher.DiscoverMods()
        });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }
});

app.MapPost("/api/app/build", async (HttpRequest req) =>
{
    using var sr = new StreamReader(req.Body);
    var body = await sr.ReadToEndAsync();
    var payload = JsonSerializer.Deserialize<JsonElement>(body);
    if (!payload.TryGetProperty("platformId", out var platformIdElement) || string.IsNullOrWhiteSpace(platformIdElement.GetString()))
    {
        return Results.BadRequest(new { ok = false, error = "Missing 'platformId'." });
    }

    try
    {
        var result = await launcher.BuildAppAsync(platformIdElement.GetString()!);
        return Results.Ok(new { ok = result.Ok, result });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }
});

app.MapPost("/api/launch", async (HttpRequest req) =>
{
    using var sr = new StreamReader(req.Body);
    string body = await sr.ReadToEndAsync();
    var payload = JsonSerializer.Deserialize<JsonElement>(body);

    try
    {
        var selectors = ResolveSelectorsFromPayload(launcher, payload, allowDefaultPreset: true);
        var result = await launcher.LaunchAsync(selectors, ResolveAdapterFromPayload(launcher, payload), ResolveBuildModeFromPayload(payload));
        return Results.Ok(new { ok = result.Ok, pid = result.Pid, url = result.Url, error = result.Error, plan = result.Plan });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }
});

app.MapPost("/api/mods/generate-sln", async (HttpRequest req) =>
{
    using var sr = new StreamReader(req.Body);
    string body = await sr.ReadToEndAsync();
    var payload = JsonSerializer.Deserialize<JsonElement>(body);
    
    if (!payload.TryGetProperty("modId", out var idEl) || string.IsNullOrWhiteSpace(idEl.GetString()))
        return Results.BadRequest(new { ok = false, error = "Missing 'modId'" });
    
    string modId = idEl.GetString()!;

    try
    {
        var slnPath = await launcher.GenerateSolutionAsync(modId);
        return Results.Ok(new { ok = true, slnPath });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }
});

app.MapGet("/api/bindings", () =>
{
    return Results.Ok(new { ok = true, bindings = launcher.GetState().Bindings });
});

app.MapPost("/api/bindings", async (HttpRequest req) =>
{
    using var sr = new StreamReader(req.Body);
    string body = await sr.ReadToEndAsync();
    var payload = JsonSerializer.Deserialize<JsonElement>(body);

    if (!payload.TryGetProperty("name", out var nameEl) || string.IsNullOrWhiteSpace(nameEl.GetString()))
        return Results.BadRequest(new { ok = false, error = "Missing 'name'" });
    if (!payload.TryGetProperty("targetType", out var typeEl) || string.IsNullOrWhiteSpace(typeEl.GetString()))
        return Results.BadRequest(new { ok = false, error = "Missing 'targetType'" });
    if (!payload.TryGetProperty("targetValue", out var valueEl) || string.IsNullOrWhiteSpace(valueEl.GetString()))
        return Results.BadRequest(new { ok = false, error = "Missing 'targetValue'" });

    try
    {
        var state = launcher.UpsertBinding(
            NormalizeBindingName(nameEl.GetString()!),
            typeEl.GetString()!,
            valueEl.GetString()!,
            payload.TryGetProperty("projectPath", out var projectEl) && projectEl.ValueKind == JsonValueKind.String
                ? projectEl.GetString()
                : null);
        return Results.Ok(new { ok = true, state });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }
});

app.MapDelete("/api/bindings/{name}", (string name) =>
{
    try
    {
        var state = launcher.DeleteBinding(NormalizeBindingName(name));
        return Results.Ok(new { ok = true, state });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }
});

app.Run("http://localhost:5299");

static IResult? TryReadRecastReactCommonOptions(
    IFormCollection form,
    out bool dirtyOnly,
    out bool includeNeighbors,
    out float heightScale,
    out float minUpDot,
    out int cliffThreshold,
    out int tileVersion,
    out bool parallel,
    out int maxDegree)
{
    dirtyOnly = false;
    includeNeighbors = true;
    heightScale = 2.0f;
    minUpDot = 0.6f;
    cliffThreshold = 1;
    tileVersion = 1;
    parallel = true;
    maxDegree = Math.Max(1, Environment.ProcessorCount);

    if (TryReadOptionalBool(form, "dirtyOnly", false, out dirtyOnly) is { } dirtyOnlyError) return dirtyOnlyError;
    if (TryReadOptionalBool(form, "includeNeighbors", true, out includeNeighbors) is { } includeNeighborsError) return includeNeighborsError;
    if (TryReadOptionalFloat(form, "heightScale", 2.0f, out heightScale) is { } heightScaleError) return heightScaleError;
    if (TryReadOptionalFloat(form, "minUpDot", 0.6f, out minUpDot) is { } minUpDotError) return minUpDotError;
    if (TryReadOptionalInt(form, "cliffThreshold", 1, out cliffThreshold) is { } cliffThresholdError) return cliffThresholdError;
    if (TryReadOptionalInt(form, "tileVersion", 1, out tileVersion) is { } tileVersionError) return tileVersionError;
    if (TryReadOptionalBool(form, "parallel", true, out parallel) is { } parallelError) return parallelError;
    if (TryReadOptionalInt(form, "maxDegree", Math.Max(1, Environment.ProcessorCount), out maxDegree) is { } maxDegreeError) return maxDegreeError;

    if (heightScale <= 0f) return Results.BadRequest(new { error = "Form field 'heightScale' must be > 0." });
    if (minUpDot <= 0f || minUpDot > 1f) return Results.BadRequest(new { error = "Form field 'minUpDot' must be > 0 and <= 1." });
    if (cliffThreshold < 0) return Results.BadRequest(new { error = "Form field 'cliffThreshold' must be >= 0." });
    if (tileVersion <= 0) return Results.BadRequest(new { error = "Form field 'tileVersion' must be > 0." });
    if (maxDegree <= 0) return Results.BadRequest(new { error = "Form field 'maxDegree' must be > 0." });
    return null;
}

static IResult? TryReadOptionalBool(IFormCollection form, string field, bool defaultValue, out bool value)
{
    value = defaultValue;
    string? text = ReadOptionalString(form, field);
    if (text == null) return null;
    if (string.Equals(text, "true", StringComparison.Ordinal))
    {
        value = true;
        return null;
    }

    if (string.Equals(text, "false", StringComparison.Ordinal))
    {
        value = false;
        return null;
    }

    return Results.BadRequest(new { error = $"Form field '{field}' must be exactly 'true' or 'false'." });
}

static IResult? TryReadOptionalFloat(IFormCollection form, string field, float defaultValue, out float value)
{
    value = defaultValue;
    string? text = ReadOptionalString(form, field);
    if (text == null) return null;
    if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) &&
        !float.IsNaN(parsed) &&
        !float.IsInfinity(parsed))
    {
        value = parsed;
        return null;
    }

    return Results.BadRequest(new { error = $"Form field '{field}' must be a finite number using invariant culture." });
}

static IResult? TryReadOptionalInt(IFormCollection form, string field, int defaultValue, out int value)
{
    value = defaultValue;
    string? text = ReadOptionalString(form, field);
    if (text == null) return null;
    if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
    {
        value = parsed;
        return null;
    }

    return Results.BadRequest(new { error = $"Form field '{field}' must be an integer using invariant culture." });
}

static string? ReadOptionalString(IFormCollection form, string field)
{
    if (!form.TryGetValue(field, out var values)) return null;
    string text = values.ToString();
    return string.IsNullOrWhiteSpace(text) ? null : text;
}

static void RestoreNavigationConfigFile(string path, string? previousText)
{
    if (previousText == null)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return;
    }

    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, previousText);
}

static bool TryBuildEditorUploadRecastContext(
    string inputReactBinPath,
    string fileName,
    string mapId,
    string? modId,
    string? dirtyJson,
    bool dirtyOnly,
    bool includeNeighbors,
    float heightScale,
    float minUpDot,
    int cliffThreshold,
    int tileVersion,
    bool parallel,
    int maxDegree,
    out string repoRoot,
    out NavBakeContext navBakeContext,
    out NavObstacleSet obstacles,
    out IReadOnlyList<NavBakeTileCoord> targets,
    out IResult? error)
{
    repoRoot = FindAssetsRoot();
    navBakeContext = null!;
    obstacles = null!;
    targets = Array.Empty<NavBakeTileCoord>();
    error = null;

    Ludots.Core.Config.MapConfig mapConfig;
    Ludots.Core.Map.Board.BoardConfig boardConfig;
    try
    {
        mapConfig = ToolMapConfigResolver.LoadMap(repoRoot, mapId, modId);
        boardConfig = ToolMapConfigResolver.ResolvePrimaryNavigationBoard(mapConfig);
    }
    catch (Exception ex)
    {
        error = Results.BadRequest(new { error = $"Failed to resolve navigation board for map '{mapId}': {ex.Message}" });
        return false;
    }

    NavMeshBakeConfigContext bakeConfigContext;
    try
    {
        bakeConfigContext = NavMeshBakeConfigLoader.LoadContextFromRepoRoot(repoRoot, modId);
    }
    catch (Exception ex)
    {
        error = Results.BadRequest(new { error = $"Failed to load navmesh bake config '{NavMeshConfigPaths.BakeConfigPath}': {ex.Message}" });
        return false;
    }

    try
    {
        obstacles = NavObstacleAuthoringCatalog.BuildForMap(repoRoot, mapId, modId);
    }
    catch (Exception ex)
    {
        error = Results.BadRequest(new { error = $"Failed to build nav obstacles from map authoring for '{mapId}': {ex.Message}" });
        return false;
    }

    try
    {
        var terrain = CreateReactEditorLogicTerrain(inputReactBinPath, boardConfig);
        targets = NavBakeTileSelection.Resolve(terrain, dirtyJson, includeNeighbors, dirtyOnly);
        NavMeshBakeConfig bakeConfig = bakeConfigContext.Config;
        navBakeContext = new NavBakeContext
        {
            MapId = mapId,
            ModId = modId ?? string.Empty,
            SourceUri = ToEditorUploadSourceUri(fileName),
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
                MaxDegreeOfParallelism = maxDegree
            }
        };
        return true;
    }
    catch (Exception ex)
    {
        error = Results.BadRequest(new { error = $"Failed to build nav bake context for '{mapId}': {ex.Message}" });
        return false;
    }
}

static LogicTerrainField CreateReactEditorLogicTerrain(
    string inputReactBinPath,
    Ludots.Core.Map.Board.BoardConfig boardConfig)
{
    if (boardConfig == null) throw new ArgumentNullException(nameof(boardConfig));
    string spatialType = (boardConfig.SpatialType ?? "Grid").Trim();

    if (spatialType.Equals("Grid", StringComparison.OrdinalIgnoreCase))
    {
        return ReactMapDataBinConverter.ReadGridLogicTerrainField(
            inputReactBinPath,
            boardConfig.GridCellSizeCm > 0 ? boardConfig.GridCellSizeCm : Ludots.Core.Spatial.SpatialScaleDefaults.CellCm);
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

static string ResolveAdapterFromPayload(LauncherService launcher, JsonElement payload)
{
    if (payload.TryGetProperty("platformId", out var platformIdElement) &&
        platformIdElement.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(platformIdElement.GetString()))
    {
        return platformIdElement.GetString()!;
    }

    if (payload.TryGetProperty("adapterId", out var adapterIdElement) &&
        adapterIdElement.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(adapterIdElement.GetString()))
    {
        return adapterIdElement.GetString()!;
    }

    return launcher.GetState().SelectedPlatformId;
}

static LauncherBuildMode ResolveBuildModeFromPayload(JsonElement payload)
{
    if (payload.TryGetProperty("buildMode", out var buildModeElement) &&
        buildModeElement.ValueKind == JsonValueKind.String &&
        Enum.TryParse<LauncherBuildMode>(buildModeElement.GetString(), true, out var buildMode))
    {
        return buildMode;
    }

    return LauncherBuildMode.Auto;
}

static IReadOnlyList<string> ResolveSelectorsFromPayload(LauncherService launcher, JsonElement payload, bool allowDefaultPreset)
{
    var selectors = new List<string>();
    if (payload.TryGetProperty("presetId", out var presetIdElement) &&
        presetIdElement.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(presetIdElement.GetString()))
    {
        selectors.Add($"preset:{presetIdElement.GetString()!}");
    }

    if (payload.TryGetProperty("selectors", out var selectorsElement) && selectorsElement.ValueKind == JsonValueKind.Array)
    {
        foreach (var selectorElement in selectorsElement.EnumerateArray())
        {
            if (selectorElement.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(selectorElement.GetString()))
            {
                selectors.Add(NormalizeSelector(launcher, selectorElement.GetString()!));
            }
        }
    }

    if (payload.TryGetProperty("modIds", out var modIdsElement) && modIdsElement.ValueKind == JsonValueKind.Array)
    {
        foreach (var modIdElement in modIdsElement.EnumerateArray())
        {
            if (modIdElement.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(modIdElement.GetString()))
            {
                selectors.Add($"mod:{modIdElement.GetString()!}");
            }
        }
    }

    if (payload.TryGetProperty("paths", out var pathsElement) && pathsElement.ValueKind == JsonValueKind.Array)
    {
        foreach (var pathElement in pathsElement.EnumerateArray())
        {
            if (pathElement.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(pathElement.GetString()))
            {
                selectors.Add($"path:{pathElement.GetString()!}");
            }
        }
    }

    if (selectors.Count > 0)
    {
        return selectors;
    }

    if (!allowDefaultPreset)
    {
        throw new InvalidOperationException("At least one selector is required.");
    }

    var selectedPresetId = launcher.GetState().SelectedPresetId;
    if (!string.IsNullOrWhiteSpace(selectedPresetId))
    {
        return new[] { $"preset:{selectedPresetId}" };
    }

    throw new InvalidOperationException("No selectors supplied and no preset is currently selected.");
}

static string NormalizeSelector(LauncherService launcher, string raw)
{
    var trimmed = raw.Trim();
    if (trimmed.StartsWith('$') || trimmed.Contains(':'))
    {
        return trimmed;
    }

    if (Directory.Exists(trimmed) && File.Exists(Path.Combine(trimmed, "mod.json")))
    {
        return $"path:{trimmed}";
    }

    if (launcher.GetState().Bindings.Any(binding => string.Equals(binding.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
    {
        return $"${trimmed}";
    }

    return $"mod:{trimmed}";
}

static string NormalizeBindingName(string raw)
{
    return raw.Trim().TrimStart('$');
}

static async Task<(int exitCode, string output)> RunProcessAsync(string fileName, string arguments, string workingDirectory, int timeoutMs = 60000)
{
    var psi = new System.Diagnostics.ProcessStartInfo(fileName, arguments)
    {
        WorkingDirectory = workingDirectory,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
    };
    
    using var proc = System.Diagnostics.Process.Start(psi);
    if (proc == null) return (-1, "Failed to start process");
    
    var stdout = proc.StandardOutput.ReadToEndAsync();
    var stderr = proc.StandardError.ReadToEndAsync();
    
    bool exited = proc.WaitForExit(timeoutMs);
    if (!exited)
    {
        try { proc.Kill(entireProcessTree: true); } catch { }
        return (-1, "Process timed out");
    }
    
    string output = (await stdout) + "\n" + (await stderr);
    return (proc.ExitCode, output.Trim());
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

static string ToEditorUploadSourceUri(string fileName)
{
    string leaf = Path.GetFileName(string.IsNullOrWhiteSpace(fileName) ? "map_data.bin" : fileName);
    return "EditorUpload:" + leaf;
}

static class EditorRepo
{
    public sealed record ModInfo(
        string Id, string Name, string Version, int Priority,
        Dictionary<string, string> Dependencies, string RootPath, string RelativePath, string LayerPath,
        string Description, string Author, List<string> Tags,
        string ChangelogFile, bool HasThumbnail, bool HasReadme);

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
        return new LauncherService(repoRoot)
            .DiscoverMods()
            .Select(mod => new ModInfo(
                mod.Id,
                mod.Name,
                mod.Version,
                mod.Priority,
                new Dictionary<string, string>(mod.Dependencies, StringComparer.Ordinal),
                mod.RootPath,
                mod.RelativePath,
                mod.LayerPath,
                mod.Description,
                mod.Author,
                mod.Tags.ToList(),
                mod.ChangelogFile,
                mod.HasThumbnail,
                mod.HasReadme))
            .OrderBy(mod => mod.Priority)
            .ThenBy(mod => mod.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
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

    public static string? ResolvePrimaryBoardDataFile(Ludots.Core.Config.MapConfig map)
    {
        if (map?.Boards == null || map.Boards.Count == 0)
            return null;

        for (int i = 0; i < map.Boards.Count; i++)
        {
            var board = map.Boards[i];
            if (board == null) continue;
            if (!string.Equals(board.Name, "default", StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.IsNullOrWhiteSpace(board.DataFile))
                return board.DataFile;
        }

        for (int i = 0; i < map.Boards.Count; i++)
        {
            var board = map.Boards[i];
            if (board == null) continue;
            if (!string.IsNullOrWhiteSpace(board.DataFile))
                return board.DataFile;
        }

        return null;
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
        return mergedNodes.Values.OrderBy(n => n?["id"]?.ToString() ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToArray();
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
                int id = int.TryParse(obj["id"]?.GetValue<string>(), out int parsedId) ? parsedId : 0;
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

    public static JsonNode LoadMergedNavigationJson(
        ModContext ctx,
        string fileName,
        JsonNode defaultValue,
        out List<string> sources)
    {
        if (ctx == null) throw new ArgumentNullException(nameof(ctx));
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("fileName is required.", nameof(fileName));
        if (Path.GetFileName(fileName) != fileName)
        {
            throw new InvalidOperationException($"Navigation config file name must be a leaf name: {fileName}");
        }

        var sourceList = new List<string>();
        JsonNode? merged = null;

        void Load(string path)
        {
            if (!File.Exists(path)) return;
            JsonNode? node = JsonNode.Parse(File.ReadAllText(path));
            if (node == null) return;
            sourceList.Add(path);

            if (merged == null)
            {
                merged = node.DeepClone();
                return;
            }

            if (merged is JsonArray targetArray && node is JsonArray sourceArray)
            {
                MergeArrayById(targetArray, sourceArray);
                return;
            }

            if (merged is JsonObject targetObject && node is JsonObject sourceObject)
            {
                Ludots.Core.Config.JsonMerger.Merge(targetObject, sourceObject);
                return;
            }

            throw new InvalidOperationException($"Navigation config '{fileName}' has incompatible JSON root types across sources.");
        }

        Load(Path.Combine(ctx.RepoRoot, "assets", "Configs", "Navigation", fileName));
        Load(Path.Combine(ctx.RepoRoot, "assets", "Navigation", fileName));
        for (int i = 0; i < ctx.LoadOrder.Count; i++)
        {
            var mod = ctx.ModsById[ctx.LoadOrder[i]];
            Load(Path.Combine(mod.RootPath, "assets", "Navigation", fileName));
            Load(Path.Combine(mod.RootPath, "assets", "Configs", "Navigation", fileName));
        }

        sources = sourceList;
        return merged ?? defaultValue.DeepClone();
    }

    public static string ResolveWritableNavigationConfigPath(ModContext ctx, string fileName)
    {
        if (ctx == null) throw new ArgumentNullException(nameof(ctx));
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("fileName is required.", nameof(fileName));
        if (Path.GetFileName(fileName) != fileName)
        {
            throw new InvalidOperationException($"Navigation config file name must be a leaf name: {fileName}");
        }

        var mod = ctx.ModsById[ctx.TargetModId];
        return Path.Combine(mod.RootPath, "assets", "Configs", "Navigation", fileName);
    }

    private static void MergeArrayById(JsonArray target, JsonArray source)
    {
        for (int i = 0; i < source.Count; i++)
        {
            if (source[i] is not JsonObject sourceObject || !TryReadId(sourceObject, out string id))
            {
                continue;
            }

            JsonObject? targetObject = null;
            for (int j = 0; j < target.Count; j++)
            {
                if (target[j] is JsonObject candidate &&
                    TryReadId(candidate, out string candidateId) &&
                    string.Equals(candidateId, id, StringComparison.Ordinal))
                {
                    targetObject = candidate;
                    break;
                }
            }

            if (targetObject == null)
            {
                target.Add(sourceObject.DeepClone());
            }
            else
            {
                Ludots.Core.Config.JsonMerger.Merge(targetObject, sourceObject);
            }
        }
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
                        target.Boards[i] = srcBoard.Clone();
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    target.Boards.Add(srcBoard.Clone());
                }
            }
        }

        if (source.TriggerTypes != null)
        {
            foreach (var tt in source.TriggerTypes)
            {
                if (!target.TriggerTypes.Contains(tt))
                {
                    target.TriggerTypes.Add(tt);
                }
            }
        }
    }

    private static ModInfo ReadModInfo(string repoRoot, string rootPath, string modJsonPath, ModManifest manifest)
    {
        bool hasThumbnail = File.Exists(Path.Combine(rootPath, "assets", "Launcher", "thumbnail.png"))
                         || File.Exists(Path.Combine(rootPath, "assets", "Launcher", "thumbnail.jpg"));
        bool hasReadme = File.Exists(Path.Combine(rootPath, "README.md"));
        string relativePath = GetRepoRelativePath(repoRoot, rootPath);
        string layerPath = GetLayerPath(relativePath);

        return new ModInfo(
            manifest.Name,
            manifest.Name,
            manifest.Version,
            manifest.Priority,
            new Dictionary<string, string>(manifest.Dependencies, StringComparer.Ordinal),
            rootPath,
            relativePath,
            layerPath,
            manifest.Description ?? "",
            manifest.Author ?? "",
            manifest.Tags ?? new List<string>(),
            manifest.Changelog ?? "",
            hasThumbnail,
            hasReadme);
    }

    private static string GetRepoRelativePath(string repoRoot, string rootPath)
    {
        var relative = Path.GetRelativePath(repoRoot, rootPath).Replace('\\', '/');
        return relative.StartsWith("../", StringComparison.Ordinal) ? rootPath.Replace('\\', '/') : relative;
    }

    private static string GetLayerPath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        const string modsPrefix = "mods/";
        if (!normalized.StartsWith(modsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return "external";
        }

        var modRelative = normalized.Substring(modsPrefix.Length);
        var lastSlash = modRelative.LastIndexOf('/');
        if (lastSlash < 0)
        {
            return "root";
        }

        return modRelative.Substring(0, lastSlash);
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
        if (!obj.TryGetPropertyValue("id", out var idNode) || idNode == null) return false;
        if (idNode.GetValueKind() != JsonValueKind.String) return false;
        id = idNode.GetValue<string>();
        return !string.IsNullOrWhiteSpace(id);
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
