using Ludots.Core.Map.Hex;
using Ludots.Core.Map.Board;
using Ludots.Core.Modding;
using Ludots.Launcher.Backend;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Map.Fields;
using Ludots.Core.Physics2D.Navigation;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.NavBake.Recast;
using Ludots.Tool;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.FileProviders;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Linq;
using System.Globalization;

var repoRoot = FindAssetsRoot();
var launcher = new LauncherService(repoRoot);
var launcherDistPath = Path.Combine(repoRoot, "src", "Tools", "Ludots.Launcher.React", "dist");

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o =>
{
    o.Limits.MaxRequestBodySize = 1024L * 1024L * 256L;
});

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
        var mapInfos = EditorRepo.DescribeMaps(ctx, maps);
        return Results.Ok(new { ok = true, maps, mapInfos });
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
    string? previous = File.Exists(outFile) ? File.ReadAllText(outFile) : null;
    try
    {
        File.WriteAllText(outFile, JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true }));
        var reloaded = EditorRepo.LoadMergedMapConfig(ctx, mapId);
        if (!reloaded.Found)
        {
            throw new InvalidOperationException($"Saved MapConfig '{mapId}' could not be loaded after write.");
        }
    }
    catch (Exception ex)
    {
        RestoreNavigationConfigFile(outFile, previous);
        return Results.BadRequest(new { ok = false, error = $"Saved MapConfig failed validation: {ex.Message}", path = outFile });
    }

    return Results.Ok(new { ok = true, path = outFile });
});

app.MapPost("/api/mods/{modId}/maps/{mapId}/boards", async (string modId, string mapId, HttpRequest req) =>
{
    string repoRoot = FindAssetsRoot();
    try
    {
        var ctx = EditorRepo.CreateContext(repoRoot, modId);
        var request = await JsonSerializer.DeserializeAsync<BoardCreateRequest>(
            req.Body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (request == null) return Results.BadRequest(new { ok = false, error = "Empty board create body." });

        var result = EditorRepo.CreateBoard(ctx, mapId, request);
        return Results.Ok(new
        {
            ok = true,
            map = result.Map,
            mapInfo = result.MapInfo,
            board = result.BoardInfo,
            mapPath = result.MapPath,
            dataPath = result.DataPath
        });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }
});

app.MapPut("/api/mods/{modId}/maps/{mapId}/boards/{boardName}", async (string modId, string mapId, string boardName, HttpRequest req) =>
{
    string repoRoot = FindAssetsRoot();
    try
    {
        var ctx = EditorRepo.CreateContext(repoRoot, modId);
        var request = await JsonSerializer.DeserializeAsync<BoardUpdateRequest>(
            req.Body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (request == null) return Results.BadRequest(new { ok = false, error = "Empty board update body." });

        var result = EditorRepo.UpdateBoard(ctx, mapId, boardName, request);
        return Results.Ok(new
        {
            ok = true,
            map = result.Map,
            mapInfo = result.MapInfo,
            board = result.BoardInfo,
            mapPath = result.MapPath,
            dataPath = result.DataPath
        });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }
});

app.MapDelete("/api/mods/{modId}/maps/{mapId}/boards/{boardName}", (string modId, string mapId, string boardName) =>
{
    string repoRoot = FindAssetsRoot();
    try
    {
        var ctx = EditorRepo.CreateContext(repoRoot, modId);
        var result = EditorRepo.DeleteBoard(ctx, mapId, boardName);
        return Results.Ok(new
        {
            ok = true,
            map = result.Map,
            mapInfo = result.MapInfo,
            removedBoard = result.BoardInfo,
            mapPath = result.MapPath,
            dataPath = result.DataPath,
            dataFileKept = true
        });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }
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

app.MapGet("/api/mods/{modId}/maps/{mapId}/terrain-react", (string modId, string mapId, string? boardName) =>
{
    string repoRoot = FindAssetsRoot();
    try
    {
        var ctx = EditorRepo.CreateContext(repoRoot, modId);
        var mapR = EditorRepo.LoadMergedMapConfig(ctx, mapId);
        if (!mapR.Found) return Results.NotFound(new { ok = false, error = $"Map not found: {mapId}" });
        var board = EditorRepo.ResolveRequiredBoardByName(mapR.Map, boardName);
        var dataFile = board.DataFile;
        if (string.IsNullOrWhiteSpace(dataFile))
            return Results.BadRequest(new { ok = false, error = $"MapConfig.Boards['{board.Name}'].DataFile is empty." });
        if (!EditorRepo.IsGridBoard(board) && !EditorRepo.IsHexGridBoard(board))
            return Results.BadRequest(new { ok = false, error = $"Board SpatialType '{board.SpatialType}' does not support React terrain editing." });
        EditorRepo.RequireLogicTerrainDataFile(dataFile);

        if (!EditorRepo.TryResolveDataFile(ctx, dataFile, out var fullPath, out var checkedPaths))
        {
            if (EditorRepo.CanServeVirtualEmptyTerrain(mapId, board))
            {
                return Results.File(
                    EditorRepo.CreateEmptyLogicTerrainBinary(board),
                    "application/octet-stream",
                    fileDownloadName: $"{mapId}_{board.Name}.ltrn");
            }

            return Results.NotFound(new { ok = false, error = $"DataFile not found for board '{board.Name}': {dataFile}", checkedPaths });
        }

        using (var validation = File.OpenRead(fullPath))
        {
            _ = LogicTerrainBinary.Read(validation);
        }

        return Results.File(File.ReadAllBytes(fullPath), "application/octet-stream", fileDownloadName: $"{mapId}_{board.Name}.ltrn");
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }
});

app.MapPut("/api/mods/{modId}/maps/{mapId}/terrain-react", async (string modId, string mapId, string? boardName, HttpRequest req) =>
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
    Ludots.Core.Map.Board.BoardConfig board;
    try
    {
        board = EditorRepo.ResolveRequiredBoardByName(mapR.Map, boardName);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }

    var dataFile = board.DataFile;
    if (string.IsNullOrWhiteSpace(dataFile))
        return Results.BadRequest(new { ok = false, error = $"MapConfig.Boards['{board.Name}'].DataFile is empty." });

    string outFile = EditorRepo.ResolveWritableDataFilePath(ctx, dataFile);
    Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);

    string tempPath = Path.Combine(Path.GetTempPath(), $"ludots_map_{Guid.NewGuid():N}.ltrn");
    try
    {
        await using (var fs = File.Create(tempPath))
        {
            await req.Body.CopyToAsync(fs);
        }

        if (!EditorRepo.IsGridBoard(board) && !EditorRepo.IsHexGridBoard(board))
        {
            return Results.BadRequest(new { ok = false, error = $"Board SpatialType '{board.SpatialType}' does not support React terrain editing." });
        }

        EditorRepo.RequireLogicTerrainDataFile(dataFile);
        await using (var validation = File.OpenRead(tempPath))
        {
            _ = LogicTerrainBinary.Read(validation);
        }

        File.Copy(tempPath, outFile, overwrite: true);
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
    if (mapFile == null) return Results.BadRequest(new { error = "Missing form file 'map' (.ltrn)" });

    var dirtyJson = form.TryGetValue("dirty", out var dirtyVal) ? dirtyVal.ToString() : null;
    var dirtyOnly = ParseBool(form.TryGetValue("dirtyOnly", out var dirtyOnlyVal) ? dirtyOnlyVal.ToString() : null, defaultValue: false);
    var includeNeighbors = ParseBool(form.TryGetValue("includeNeighbors", out var inclVal) ? inclVal.ToString() : null, defaultValue: true);
    var heightScale = ParseFloat(form.TryGetValue("heightScale", out var hsVal) ? hsVal.ToString() : null, 2.0f);
    var minUpDot = ParseFloat(form.TryGetValue("minUpDot", out var mudVal) ? mudVal.ToString() : null, 0.6f);
    var cliffThreshold = ParseInt(form.TryGetValue("cliffThreshold", out var ctVal) ? ctVal.ToString() : null, 1);
    var tileVersion = NavTileBinary.FormatVersion;
    var writeArtifact = ParseBool(form.TryGetValue("artifact", out var artVal) ? artVal.ToString() : null, defaultValue: true);
    var parallel = ParseBool(form.TryGetValue("parallel", out var parVal) ? parVal.ToString() : null, defaultValue: true);
    var maxDegree = ParseInt(form.TryGetValue("maxDegree", out var mdVal) ? mdVal.ToString() : null, Math.Max(1, Environment.ProcessorCount));

    string tempPath = Path.Combine(Path.GetTempPath(), $"ludots_map_{Guid.NewGuid():N}.ltrn");
    try
    {
        await using (var fs = File.Create(tempPath))
        {
            await mapFile.CopyToAsync(fs);
        }

        LogicTerrainField terrain = CreateReactEditorLogicTerrain(tempPath, new Ludots.Core.Map.Board.BoardConfig
        {
            Name = "editor-upload",
            SpatialType = "Grid"
        });

        IReadOnlyList<NavBakeTileCoord> targets = NavBakeTileSelection.Resolve(
            terrain,
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
    if (mapFile == null) return Results.BadRequest(new { error = "Missing form file 'map' (.ltrn)" });
    var mapId = form.TryGetValue("mapId", out var mapIdVal) ? mapIdVal.ToString() : null;
    if (string.IsNullOrWhiteSpace(mapId)) return Results.BadRequest(new { error = "Missing form field 'mapId'" });
    var modId = form.TryGetValue("modId", out var modIdVal) ? modIdVal.ToString() : null;
    var boardName = form.TryGetValue("boardName", out var boardNameVal) ? boardNameVal.ToString() : null;
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

    string tempPath = Path.Combine(Path.GetTempPath(), $"ludots_map_{Guid.NewGuid():N}.ltrn");
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
            boardName,
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
                    config = new { mapId, modId, boardName, dirtyOnly, includeNeighbors, heightScale, minUpDot, cliffThreshold, tileVersion, obstacleCount = obstacles.Obstacles.Count }
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
                    config = new { mapId, modId, boardName, dirtyOnly, includeNeighbors, heightScale, minUpDot, cliffThreshold, tileVersion, obstacleCount = obstacles.Obstacles.Count }
                });
            }

            var tiles = new List<object>(navBakeResult.SuccessCount);
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

                tiles.Add(new
                {
                    cx = r.Target.ChunkX,
                    cy = r.Target.ChunkY,
                    layer = r.Layer,
                    profileId = r.ProfileId,
                    detourBase64 = Convert.ToBase64String(r.DetourTileBytes),
                    debug = Ludots.Editor.Bridge.EditorNavTileDebugPayload.BuildFromDetourTileBytes(r.DetourTileBytes)
                });
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
                config = new { mapId, modId, boardName, dirtyOnly, includeNeighbors, heightScale, minUpDot, cliffThreshold, tileVersion, obstacleCount = obstacles.Obstacles.Count }
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
    if (mapFile == null) return Results.BadRequest(new { error = "Missing form file 'map' (.ltrn)" });
    var mapId = form.TryGetValue("mapId", out var mapIdVal) ? mapIdVal.ToString() : null;
    if (string.IsNullOrWhiteSpace(mapId)) return Results.BadRequest(new { error = "Missing form field 'mapId'" });
    var modId = form.TryGetValue("modId", out var modIdVal) ? modIdVal.ToString() : null;
    var boardName = form.TryGetValue("boardName", out var boardNameVal) ? boardNameVal.ToString() : null;
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

    string tempPath = Path.Combine(Path.GetTempPath(), $"ludots_map_{Guid.NewGuid():N}.ltrn");
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
            boardName,
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
                config = new { mapId, modId, boardName, dirtyOnly, includeNeighbors, heightScale, minUpDot, cliffThreshold, tileVersion, obstacleCount = obstacles.Obstacles.Count }
            });
        }

        return buildError!;
    }
    finally
    {
        try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
    }
});

app.MapPost("/api/nav/bootstrap-flat-grid-react", async (HttpRequest req) =>
{
    FlatGridNavBootstrapRequest? payload;
    try
    {
        payload = await JsonSerializer.DeserializeAsync<FlatGridNavBootstrapRequest>(
            req.Body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { ok = false, error = $"Invalid JSON: {ex.Message}" });
    }

    if (payload == null) return Results.BadRequest(new { ok = false, error = "Request body is required." });
    if (string.IsNullOrWhiteSpace(payload.MapId)) return Results.BadRequest(new { ok = false, error = "mapId is required." });
    if (string.IsNullOrWhiteSpace(payload.BoardName)) return Results.BadRequest(new { ok = false, error = "boardName is required." });
    if (string.IsNullOrWhiteSpace(payload.ProfileId)) return Results.BadRequest(new { ok = false, error = "profileId is required." });
    if (payload.Chunks == null || payload.Chunks.Count == 0) return Results.BadRequest(new { ok = false, error = "At least one chunk is required." });

    string repoRoot = FindAssetsRoot();
    Ludots.Core.Map.Board.BoardConfig boardConfig;
    EditorRepo.BoardInfo boardInfo;
    NavMeshBakeConfigContext bakeConfigContext;
    try
    {
        var ctx = EditorRepo.CreateContext(repoRoot, payload.ModId);
        var mapR = EditorRepo.LoadMergedMapConfig(ctx, payload.MapId);
        if (!mapR.Found) return Results.NotFound(new { ok = false, error = $"Map not found: {payload.MapId}" });
        boardConfig = EditorRepo.ResolveRequiredBoardByName(mapR.Map, payload.BoardName);
        boardInfo = EditorRepo.DescribeBoard(ctx, payload.MapId, boardConfig);
        if (!boardConfig.NavigationEnabled)
        {
            return Results.BadRequest(new { ok = false, error = $"Map board '{boardConfig.Name}' has NavigationEnabled=false." });
        }

        if (!EditorRepo.IsGridBoard(boardConfig))
        {
            return Results.BadRequest(new { ok = false, error = $"Flat baseline NavTile bootstrap only supports Grid boards. Board '{boardConfig.Name}' is {EditorRepo.NormalizeSpatialType(boardConfig)}." });
        }

        bakeConfigContext = NavMeshBakeConfigLoader.LoadContextFromRepoRoot(repoRoot, payload.ModId);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }

    int layer = payload.Layer;
    try
    {
        var profileRegistry = new NavMeshProfileRegistry(bakeConfigContext.Config, bakeConfigContext.AgentProfiles);
        if (!profileRegistry.TryGetIndex(payload.ProfileId, out _))
        {
            return Results.BadRequest(new { ok = false, error = $"NavMesh profile '{payload.ProfileId}' is not declared in Navigation/navmesh.json." });
        }

        bool layerDeclared = false;
        for (int i = 0; i < bakeConfigContext.Config.Layers.Count; i++)
        {
            if (bakeConfigContext.Config.Layers[i].Layer == layer)
            {
                layerDeclared = true;
                break;
            }
        }

        if (!layerDeclared)
        {
            return Results.BadRequest(new { ok = false, error = $"NavMesh layer {layer} is not declared in Navigation/navmesh.json." });
        }
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }

    int tileWidthCm;
    int tileHeightCm;
    int chunkSizeCells = boardConfig.ChunkSizeCells > 0
        ? boardConfig.ChunkSizeCells
        : Ludots.Core.Spatial.SpatialScaleDefaults.TerrainChunkCells;
    int tileVersion = NavTileBinary.FormatVersion;
    try
    {
        tileWidthCm = ResolveBoardTileWidthCm(boardConfig);
        tileHeightCm = ResolveBoardTileHeightCm(boardConfig);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }

    var seen = new HashSet<string>(StringComparer.Ordinal);
    var tiles = new List<object>(payload.Chunks.Count);
    try
    {
        for (int i = 0; i < payload.Chunks.Count; i++)
        {
            NavBootstrapChunk? chunk = payload.Chunks[i];
            if (chunk == null) continue;
            int cx = chunk.Cx;
            int cy = chunk.Cy;
            if (cx < 0 || cy < 0 || cx >= boardInfo.WidthChunks || cy >= boardInfo.HeightChunks)
            {
                return Results.BadRequest(new { ok = false, error = $"chunks[{i}] ({cx},{cy}) is outside board '{boardInfo.Name}' bounds {boardInfo.WidthChunks}x{boardInfo.HeightChunks}." });
            }

            string key = $"{cx},{cy},{layer},{payload.ProfileId}";
            if (!seen.Add(key)) continue;

            NavTile tile = DefaultGridNavTileFactory.CreateFlatTile(
                cx,
                cy,
                layer,
                checked((uint)tileVersion),
                tileWidthCm,
                tileHeightCm,
                chunkSizeCells,
                chunkSizeCells);
            byte[] detourBytes = DetourNavQueryEngine.BuildFlatGridBaselineDetourTileBytes(tile, tileWidthCm, tileHeightCm);
            tiles.Add(new
            {
                cx,
                cy,
                layer,
                profileId = payload.ProfileId,
                detourBase64 = Convert.ToBase64String(detourBytes),
                debug = Ludots.Editor.Bridge.EditorNavTileDebugPayload.BuildFromDetourTileBytes(detourBytes),
                source = DefaultGridNavTileFactory.SourceId
            });
        }
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }

    return Results.Ok(new
    {
        ok = true,
        source = DefaultGridNavTileFactory.SourceId,
        tiles,
        requestedChunkCount = payload.Chunks.Count,
        returnedTileCount = tiles.Count,
        tileWidthCm,
        tileHeightCm,
        chunkSizeCells,
        tileVersion,
        board = new { boardInfo.Name, boardInfo.WidthChunks, boardInfo.HeightChunks }
    });
});

app.MapPost("/api/nav/query-recast-react", async (HttpRequest req) =>
{
    NavPathQueryRequest? payload;
    try
    {
        payload = await JsonSerializer.DeserializeAsync<NavPathQueryRequest>(
            req.Body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { ok = false, error = $"Invalid JSON: {ex.Message}" });
    }

    if (payload == null) return Results.BadRequest(new { ok = false, error = "Request body is required." });
    if (string.IsNullOrWhiteSpace(payload.MapId)) return Results.BadRequest(new { ok = false, error = "mapId is required." });
    if (string.IsNullOrWhiteSpace(payload.BoardName)) return Results.BadRequest(new { ok = false, error = "boardName is required." });
    if (string.IsNullOrWhiteSpace(payload.ProfileId)) return Results.BadRequest(new { ok = false, error = "profileId is required." });
    if (payload.Tiles == null || payload.Tiles.Count == 0) return Results.BadRequest(new { ok = false, error = "At least one current NavTile payload is required." });

    string repoRoot = FindAssetsRoot();
    Ludots.Core.Map.Board.BoardConfig boardConfig;
    NavMeshBakeConfigContext bakeConfigContext;
    try
    {
        var mapConfig = ToolMapConfigResolver.LoadMap(repoRoot, payload.MapId, payload.ModId);
        boardConfig = EditorRepo.ResolveRequiredBoardByName(mapConfig, payload.BoardName);
        if (!boardConfig.NavigationEnabled)
        {
            return Results.BadRequest(new { ok = false, error = $"Map board '{boardConfig.Name}' has NavigationEnabled=false." });
        }

        bakeConfigContext = NavMeshBakeConfigLoader.LoadContextFromRepoRoot(repoRoot, payload.ModId);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }

    int layer = payload.Layer;
    int profileIndex;
    try
    {
        var profileRegistry = new NavMeshProfileRegistry(bakeConfigContext.Config, bakeConfigContext.AgentProfiles);
        if (!profileRegistry.TryGetIndex(payload.ProfileId, out profileIndex))
        {
            return Results.BadRequest(new { ok = false, error = $"NavMesh profile '{payload.ProfileId}' is not declared in Navigation/navmesh.json." });
        }
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }

    bool layerDeclared = false;
    for (int i = 0; i < bakeConfigContext.Config.Layers.Count; i++)
    {
        if (bakeConfigContext.Config.Layers[i].Layer == layer)
        {
            layerDeclared = true;
            break;
        }
    }

    if (!layerDeclared)
    {
        return Results.BadRequest(new { ok = false, error = $"NavMesh layer {layer} is not declared in Navigation/navmesh.json." });
    }

    int tileWidthCm;
    int tileHeightCm;
    try
    {
        tileWidthCm = ResolveBoardTileWidthCm(boardConfig);
        tileHeightCm = ResolveBoardTileHeightCm(boardConfig);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }

    var detourTiles = new List<byte[]>(payload.Tiles.Count);
    var tileSources = new HashSet<string>(StringComparer.Ordinal);
    int acceptedTiles = 0;
    int ignoredTiles = 0;
    try
    {
        for (int i = 0; i < payload.Tiles.Count; i++)
        {
            var t = payload.Tiles[i];
            if (t == null || string.IsNullOrWhiteSpace(t.DetourBase64))
            {
                ignoredTiles++;
                continue;
            }

            if (!string.Equals(t.ProfileId, payload.ProfileId, StringComparison.Ordinal))
            {
                return Results.BadRequest(new { ok = false, error = $"tiles[{i}].profileId '{t.ProfileId ?? "<null>"}' does not match requested profileId '{payload.ProfileId}'." });
            }

            if (t.Layer != layer)
            {
                return Results.BadRequest(new { ok = false, error = $"tiles[{i}].layer {t.Layer} does not match requested layer {layer}." });
            }

            detourTiles.Add(Convert.FromBase64String(t.DetourBase64));
            if (!string.IsNullOrWhiteSpace(t.Source))
            {
                tileSources.Add(t.Source);
            }
            acceptedTiles++;
        }
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { ok = false, error = $"Failed to decode Detour tile payload: {ex.Message}" });
    }

    if (acceptedTiles == 0)
    {
        return Results.BadRequest(new { ok = false, error = $"No supplied Detour tile payload matched layer {layer}." });
    }

    NavAreaCostTable areaCosts;
    try
    {
        areaCosts = BuildEditorNavAreaCostTable(bakeConfigContext.Config, payload.AreaCosts);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }

    int maxPortals = payload.MaxPortals <= 0 ? 256 : payload.MaxPortals;
    var sw = Stopwatch.StartNew();
    NavPathResult result = DetourNavQueryEngine.FindPathFromDetourTileBytes(
        detourTiles,
        layer,
        areaCosts,
        payload.Start.XCm,
        payload.Start.ZCm,
        payload.Goal.XCm,
        payload.Goal.ZCm,
        maxPortals);
    sw.Stop();

    var points = new List<object>(result.PathXcm.Length);
    int count = Math.Min(result.PathXcm.Length, result.PathZcm.Length);
    for (int i = 0; i < count; i++)
    {
        points.Add(new { xCm = result.PathXcm[i], zCm = result.PathZcm[i] });
    }

    bool usesFlatBaseline = tileSources.Contains(DefaultGridNavTileFactory.SourceId);
    string tileSource = tileSources.Count == 0
        ? "editor-supplied-detour-tile-bytes"
        : $"editor-supplied-detour-tile-bytes ({string.Join("+", tileSources.OrderBy(x => x, StringComparer.Ordinal))})";

    return Results.Ok(new
    {
        ok = true,
        status = result.Status.ToString(),
        points,
        travelCost = result.TravelCost.ToDouble(),
        elapsedMs = sw.Elapsed.TotalMilliseconds,
        engine = "Ludots.Core.DetourNavQueryEngine + DotRecast.Detour",
        algorithmSource = usesFlatBaseline
            ? "Default Grid flat NavTile footprint -> convex DotRecast DtMeshData poly -> DtNavMeshQuery FindNearestPoly/Raycast visibility shortcut/FindPath/FindStraightPath fallback"
            : "Recast bake RcPolyMesh/RcPolyMeshDetail -> official DotRecast DtMeshData -> DtNavMeshQuery FindNearestPoly/Raycast visibility shortcut/FindPath/FindStraightPath fallback",
        profileId = payload.ProfileId,
        profileIndex,
        layer,
        maxPortals,
        acceptedTiles,
        ignoredTiles,
        tileWidthCm,
        tileHeightCm,
        tileSource,
        warning = string.Equals(EditorRepo.NormalizeSpatialType(boardConfig), "Grid", StringComparison.Ordinal)
            ? null
            : "Hex boards use pointy-hex world spacing; query uses the board-derived tile footprint."
    });
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
    tileVersion = NavTileBinary.FormatVersion;
    parallel = true;
    maxDegree = Math.Max(1, Environment.ProcessorCount);

    if (TryReadOptionalBool(form, "dirtyOnly", false, out dirtyOnly) is { } dirtyOnlyError) return dirtyOnlyError;
    if (TryReadOptionalBool(form, "includeNeighbors", true, out includeNeighbors) is { } includeNeighborsError) return includeNeighborsError;
    if (TryReadOptionalFloat(form, "heightScale", 2.0f, out heightScale) is { } heightScaleError) return heightScaleError;
    if (TryReadOptionalFloat(form, "minUpDot", 0.6f, out minUpDot) is { } minUpDotError) return minUpDotError;
    if (TryReadOptionalInt(form, "cliffThreshold", 1, out cliffThreshold) is { } cliffThresholdError) return cliffThresholdError;
    if (TryReadOptionalBool(form, "parallel", true, out parallel) is { } parallelError) return parallelError;
    if (TryReadOptionalInt(form, "maxDegree", Math.Max(1, Environment.ProcessorCount), out maxDegree) is { } maxDegreeError) return maxDegreeError;

    if (heightScale <= 0f) return Results.BadRequest(new { error = "Form field 'heightScale' must be > 0." });
    if (minUpDot <= 0f || minUpDot > 1f) return Results.BadRequest(new { error = "Form field 'minUpDot' must be > 0 and <= 1." });
    if (cliffThreshold < 0) return Results.BadRequest(new { error = "Form field 'cliffThreshold' must be >= 0." });
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
    string? boardName,
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
        boardConfig = !string.IsNullOrWhiteSpace(boardName)
            ? EditorRepo.ResolveRequiredBoardByName(mapConfig, boardName)
            : ToolMapConfigResolver.ResolvePrimaryNavigationBoard(mapConfig);
    }
    catch (Exception ex)
    {
        error = Results.BadRequest(new { error = $"Failed to resolve navigation board for map '{mapId}': {ex.Message}" });
        return false;
    }

    if (!boardConfig.NavigationEnabled)
    {
        error = Results.BadRequest(new { error = $"Map board '{boardConfig.Name}' has NavigationEnabled=false and cannot bake navmesh." });
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

static NavAreaCostTable BuildEditorNavAreaCostTable(
    NavMeshBakeConfig cfg,
    IReadOnlyList<NavAreaCostOverride>? overrides)
{
    var arr = new Fix64[256];
    for (int i = 0; i < arr.Length; i++) arr[i] = Fix64.OneValue;

    if (cfg?.Areas != null)
    {
        for (int i = 0; i < cfg.Areas.Count; i++)
        {
            NavAreaCostConfig? area = cfg.Areas[i];
            if (area == null) continue;
            if (area.AreaId < 0 || area.AreaId > 255)
                throw new InvalidOperationException($"NavMeshBakeConfig.areas has invalid areaId: {area.AreaId}");
            if (area.Cost <= 0f || float.IsNaN(area.Cost))
                throw new InvalidOperationException($"NavMeshBakeConfig.areas has invalid cost for areaId={area.AreaId}");
            arr[area.AreaId] = Fix64.FromFloat(area.Cost);
        }
    }

    if (overrides != null)
    {
        for (int i = 0; i < overrides.Count; i++)
        {
            NavAreaCostOverride? area = overrides[i];
            if (area == null) continue;
            if (area.AreaId < 0 || area.AreaId > 255)
                throw new InvalidOperationException($"areaCosts[{i}].areaId must be 0..255.");
            if (area.Cost <= 0f || float.IsNaN(area.Cost))
                throw new InvalidOperationException($"areaCosts[{i}].cost must be > 0.");
            arr[area.AreaId] = Fix64.FromFloat(area.Cost);
        }
    }

    return new NavAreaCostTable(arr);
}

static int ResolveBoardTileWidthCm(Ludots.Core.Map.Board.BoardConfig board)
{
    int chunkSizeCells = board.ChunkSizeCells > 0
        ? board.ChunkSizeCells
        : Ludots.Core.Spatial.SpatialScaleDefaults.TerrainChunkCells;

    string spatialType = EditorRepo.NormalizeSpatialType(board);
    if (string.Equals(spatialType, "Grid", StringComparison.Ordinal))
    {
        return checked(chunkSizeCells * EditorRepo.RequireGridCellSizeCm(board));
    }

    if (string.Equals(spatialType, "HexGrid", StringComparison.Ordinal))
    {
        return checked((int)MathF.Round(Ludots.Core.Map.Hex.HexCoordinates.HexWidth * chunkSizeCells * 100f));
    }

    throw new InvalidOperationException($"Map board '{board.Name}' is {spatialType}; only Grid and HexGrid boards can query navmesh.");
}

static int ResolveBoardTileHeightCm(Ludots.Core.Map.Board.BoardConfig board)
{
    int chunkSizeCells = board.ChunkSizeCells > 0
        ? board.ChunkSizeCells
        : Ludots.Core.Spatial.SpatialScaleDefaults.TerrainChunkCells;

    string spatialType = EditorRepo.NormalizeSpatialType(board);
    if (string.Equals(spatialType, "Grid", StringComparison.Ordinal))
    {
        return checked(chunkSizeCells * EditorRepo.RequireGridCellSizeCm(board));
    }

    if (string.Equals(spatialType, "HexGrid", StringComparison.Ordinal))
    {
        return checked((int)MathF.Round(Ludots.Core.Map.Hex.HexCoordinates.RowSpacing * chunkSizeCells * 100f));
    }

    throw new InvalidOperationException($"Map board '{board.Name}' is {spatialType}; only Grid and HexGrid boards can query navmesh.");
}

static LogicTerrainField CreateReactEditorLogicTerrain(
    string inputReactBinPath,
    Ludots.Core.Map.Board.BoardConfig boardConfig)
{
    if (boardConfig == null) throw new ArgumentNullException(nameof(boardConfig));
    if (!Path.GetExtension(inputReactBinPath).Equals(".ltrn", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            $"Editor terrain upload must be .ltrn. Use Ludots.Tool map import-react for explicit one-way legacy .bin import: {inputReactBinPath}");
    }

    string spatialType = EditorRepo.NormalizeSpatialType(boardConfig);
    if (string.Equals(spatialType, "NodeGraph", StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            $"Map board '{boardConfig.Name}' is NodeGraph; NodeGraph boards use graph data and do not bake navmesh.");
    }
    if (!string.Equals(spatialType, "Grid", StringComparison.Ordinal) &&
        !string.Equals(spatialType, "HexGrid", StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            $"Map board '{boardConfig.Name}' has unsupported SpatialType '{boardConfig.SpatialType}'. Expected Grid, HexGrid, or NodeGraph.");
    }

    using var input = File.OpenRead(inputReactBinPath);
    return LogicTerrainBinary.Read(input);
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
    string leaf = Path.GetFileName(string.IsNullOrWhiteSpace(fileName) ? "terrain.ltrn" : fileName);
    return "EditorUpload:" + leaf;
}

static class EditorRepo
{
    private const int EagerEmptyTerrainFileMacroTileLimit = 16;

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
    public sealed record BoardMutationResult(
        Ludots.Core.Config.MapConfig Map,
        MapInfo MapInfo,
        BoardInfo BoardInfo,
        string MapPath,
        string? DataPath);

    public sealed record MapInfo(
        string Id,
        bool Found,
        bool HasBoards,
        string? BoardName,
        string? SpatialType,
        int WidthChunks,
        int HeightChunks,
        int CellSizeCm,
        int HexEdgeLengthCm,
        int ChunkSizeCells,
        bool NavigationEnabled,
        bool HasDataFile,
        bool DataFileExists,
        string? DataFile,
        bool CanEditTerrain,
        bool CanBake,
        string Reason,
        IReadOnlyList<BoardInfo> Boards);

    public sealed record BoardInfo(
        string Name,
        string? SpatialType,
        int WidthChunks,
        int HeightChunks,
        int CellSizeCm,
        int HexEdgeLengthCm,
        int ChunkSizeCells,
        bool NavigationEnabled,
        bool HasDataFile,
        bool DataFileExists,
        string? DataFile,
        bool CanEditTerrain,
        bool CanBake,
        string Reason);

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

    public static List<MapInfo> DescribeMaps(ModContext ctx, IReadOnlyList<string> mapIds)
    {
        var result = new List<MapInfo>(mapIds.Count);
        for (int i = 0; i < mapIds.Count; i++)
        {
            result.Add(DescribeMap(ctx, mapIds[i]));
        }

        return result;
    }

    public static MapInfo DescribeMap(ModContext ctx, string mapId)
    {
        var mapR = LoadMergedMapConfig(ctx, mapId);
        if (!mapR.Found)
        {
            return new MapInfo(
                mapId,
                Found: false,
                HasBoards: false,
                BoardName: null,
                SpatialType: null,
                WidthChunks: 0,
                HeightChunks: 0,
                CellSizeCm: Ludots.Core.Spatial.SpatialScaleDefaults.CellCm,
                HexEdgeLengthCm: Ludots.Core.Spatial.SpatialScaleDefaults.DefaultHexEdgeLengthCm,
                ChunkSizeCells: Ludots.Core.Spatial.SpatialScaleDefaults.TerrainChunkCells,
                NavigationEnabled: false,
                HasDataFile: false,
                DataFileExists: false,
                DataFile: null,
                CanEditTerrain: false,
                CanBake: false,
                Reason: "Map config was discovered but could not be merged.",
                Boards: Array.Empty<BoardInfo>());
        }

        var board = ResolvePrimaryBoard(mapR.Map);
        if (board == null)
        {
            return new MapInfo(
                mapId,
                Found: true,
                HasBoards: false,
                BoardName: null,
                SpatialType: null,
                WidthChunks: 0,
                HeightChunks: 0,
                CellSizeCm: Ludots.Core.Spatial.SpatialScaleDefaults.CellCm,
                HexEdgeLengthCm: Ludots.Core.Spatial.SpatialScaleDefaults.DefaultHexEdgeLengthCm,
                ChunkSizeCells: Ludots.Core.Spatial.SpatialScaleDefaults.TerrainChunkCells,
                NavigationEnabled: false,
                HasDataFile: false,
                DataFileExists: false,
                DataFile: null,
                CanEditTerrain: false,
                CanBake: false,
                Reason: "Map has no BoardConfig entries.",
                Boards: Array.Empty<BoardInfo>());
        }

        var boards = DescribeBoards(ctx, mapR.Map, mapId);
        var primary = boards.FirstOrDefault(b => string.Equals(b.Name, board.Name, StringComparison.Ordinal))
            ?? DescribeBoard(ctx, mapId, board);

        return new MapInfo(
            mapId,
            Found: true,
            HasBoards: true,
            BoardName: board.Name,
            SpatialType: primary.SpatialType,
            WidthChunks: primary.WidthChunks,
            HeightChunks: primary.HeightChunks,
            CellSizeCm: primary.CellSizeCm,
            HexEdgeLengthCm: primary.HexEdgeLengthCm,
            ChunkSizeCells: primary.ChunkSizeCells,
            NavigationEnabled: primary.NavigationEnabled,
            HasDataFile: primary.HasDataFile,
            DataFileExists: primary.DataFileExists,
            DataFile: primary.DataFile,
            CanEditTerrain: primary.CanEditTerrain,
            CanBake: primary.CanBake,
            Reason: primary.Reason,
            Boards: boards);
    }

    public static IReadOnlyList<BoardInfo> DescribeBoards(ModContext ctx, Ludots.Core.Config.MapConfig map, string mapId)
    {
        if (map?.Boards == null || map.Boards.Count == 0)
            return Array.Empty<BoardInfo>();

        var result = new List<BoardInfo>(map.Boards.Count);
        for (int i = 0; i < map.Boards.Count; i++)
        {
            var board = map.Boards[i];
            if (board == null) continue;
            result.Add(DescribeBoard(ctx, mapId, board));
        }

        return result;
    }

    public static BoardInfo DescribeBoard(ModContext ctx, string mapId, Ludots.Core.Map.Board.BoardConfig board)
    {
        int chunkSizeCells = board.ChunkSizeCells > 0
            ? board.ChunkSizeCells
            : Ludots.Core.Spatial.SpatialScaleDefaults.TerrainChunkCells;
        int widthChunks = checked(board.WidthInMacroTiles * (Ludots.Core.Spatial.SpatialScaleDefaults.MacroTileCells / Ludots.Core.Spatial.SpatialScaleDefaults.TerrainChunkCells));
        int heightChunks = checked(board.HeightInMacroTiles * (Ludots.Core.Spatial.SpatialScaleDefaults.MacroTileCells / Ludots.Core.Spatial.SpatialScaleDefaults.TerrainChunkCells));
        if (chunkSizeCells != Ludots.Core.Spatial.SpatialScaleDefaults.TerrainChunkCells)
        {
            widthChunks = checked((board.WidthInMacroTiles * Ludots.Core.Spatial.SpatialScaleDefaults.MacroTileCells) / chunkSizeCells);
            heightChunks = checked((board.HeightInMacroTiles * Ludots.Core.Spatial.SpatialScaleDefaults.MacroTileCells) / chunkSizeCells);
        }

        string? spatialType = null;
        string? topologyError = null;
        try
        {
            spatialType = NormalizeSpatialType(board);
        }
        catch (Exception ex)
        {
            topologyError = ex.Message;
        }

        bool supportedTerrainTopology = string.Equals(spatialType, "Grid", StringComparison.Ordinal) ||
            string.Equals(spatialType, "HexGrid", StringComparison.Ordinal);
        bool hasDataFile = !string.IsNullOrWhiteSpace(board.DataFile);
        bool dataFileExists = hasDataFile && TryResolveDataFile(ctx, board.DataFile, out _, out _);
        bool chunkSizeEditable = chunkSizeCells == Ludots.Core.Spatial.SpatialScaleDefaults.TerrainChunkCells;
        bool canUseVirtualEmptyTerrain = topologyError == null && !dataFileExists && CanServeVirtualEmptyTerrain(mapId, board);
        bool canEditTerrain = topologyError == null && supportedTerrainTopology && chunkSizeEditable && hasDataFile && (dataFileExists || canUseVirtualEmptyTerrain);
        bool canBake = canEditTerrain && board.NavigationEnabled;
        string reason =
            topologyError != null ? topologyError :
            canBake ? "Ready for nav bake." :
            canUseVirtualEmptyTerrain ? "Sparse empty terrain is virtual; missing chunks are treated as flat until saved." :
            !board.NavigationEnabled ? "Board NavigationEnabled is false." :
            !supportedTerrainTopology ? $"Board SpatialType '{board.SpatialType}' is not terrain-editable." :
            !chunkSizeEditable ? $"React terrain editor requires ChunkSizeCells={Ludots.Core.Spatial.SpatialScaleDefaults.TerrainChunkCells}; map uses {chunkSizeCells}." :
            !hasDataFile ? "Board DataFile is empty." :
            !dataFileExists ? "Board DataFile could not be resolved." :
            "Not bakeable.";

        return new BoardInfo(
            Name: board.Name,
            SpatialType: spatialType,
            WidthChunks: widthChunks,
            HeightChunks: heightChunks,
            CellSizeCm: board.GridCellSizeCm > 0 ? board.GridCellSizeCm : Ludots.Core.Spatial.SpatialScaleDefaults.CellCm,
            HexEdgeLengthCm: board.HexEdgeLengthCm > 0 ? board.HexEdgeLengthCm : Ludots.Core.Spatial.SpatialScaleDefaults.DefaultHexEdgeLengthCm,
            ChunkSizeCells: chunkSizeCells,
            NavigationEnabled: board.NavigationEnabled,
            HasDataFile: hasDataFile,
            DataFileExists: dataFileExists,
            DataFile: hasDataFile ? board.DataFile : null,
            CanEditTerrain: canEditTerrain,
            CanBake: canBake,
            Reason: reason);
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

    public static BoardMutationResult CreateBoard(ModContext ctx, string mapId, BoardCreateRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        string name = RequireBoardName(request.Name);
        string spatialType = NormalizeRequestedSpatialType(request.SpatialType);
        if (string.Equals(spatialType, "NodeGraph", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("NodeGraph board creation needs graph-data authoring and is not supported by the terrain editor yet.");
        }

        var mapR = LoadMergedMapConfig(ctx, mapId);
        if (!mapR.Found) throw new InvalidOperationException($"Map not found: {mapId}");
        var map = mapR.Map;
        map.Id = mapId;
        map.Boards ??= new List<Ludots.Core.Map.Board.BoardConfig>();
        EnsureNoBoardNameConflict(map, name);

        int widthMacroTiles = request.WidthInMacroTiles > 0
            ? request.WidthInMacroTiles
            : Ludots.Core.Spatial.SpatialScaleDefaults.DefaultWorldWidthMacroTiles;
        int heightMacroTiles = request.HeightInMacroTiles > 0
            ? request.HeightInMacroTiles
            : Ludots.Core.Spatial.SpatialScaleDefaults.DefaultWorldHeightMacroTiles;
        int chunkSizeCells = request.ChunkSizeCells > 0
            ? request.ChunkSizeCells
            : Ludots.Core.Spatial.SpatialScaleDefaults.TerrainChunkCells;
        int cellSizeCm = request.CellSizeCm > 0
            ? request.CellSizeCm
            : Ludots.Core.Spatial.SpatialScaleDefaults.CellCm;

        ValidateBoardDimensions(widthMacroTiles, heightMacroTiles, chunkSizeCells);
        string dataFile = string.IsNullOrWhiteSpace(request.DataFile)
            ? BuildDefaultBoardDataFile(mapId, name, spatialType)
            : request.DataFile.Trim();
        RequireLogicTerrainDataFile(dataFile);

        var board = new Ludots.Core.Map.Board.BoardConfig
        {
            Name = name,
            SpatialType = spatialType,
            WidthInMacroTiles = widthMacroTiles,
            HeightInMacroTiles = heightMacroTiles,
            GridCellSizeCm = cellSizeCm,
            HexEdgeLengthCm = request.HexEdgeLengthCm > 0 ? request.HexEdgeLengthCm : Ludots.Core.Spatial.SpatialScaleDefaults.DefaultHexEdgeLengthCm,
            ChunkSizeCells = chunkSizeCells,
            DataFile = dataFile,
            NavigationEnabled = request.NavigationEnabled,
        };

        string? dataPath = null;
        if (!string.IsNullOrWhiteSpace(board.DataFile))
        {
            dataPath = ResolveWritableDataFilePath(ctx, board.DataFile);
            if (File.Exists(dataPath))
            {
                throw new InvalidOperationException($"Board DataFile already exists: {board.DataFile}");
            }

            if (ShouldCreateFullEmptyTerrainDataFile(board))
            {
                CreateEmptyTerrainDataFile(board, dataPath);
            }
            else
            {
                dataPath = null;
            }
        }

        map.Boards.Add(board);
        string mapPath = WriteWritableMapConfig(ctx, mapId, map);
        var mapInfo = DescribeMap(ctx, mapId);
        var boardInfo = DescribeBoard(ctx, mapId, board);
        return new BoardMutationResult(map, mapInfo, boardInfo, mapPath, dataPath);
    }

    public static BoardMutationResult UpdateBoard(ModContext ctx, string mapId, string boardName, BoardUpdateRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        string name = RequireBoardName(boardName);

        var mapR = LoadMergedMapConfig(ctx, mapId);
        if (!mapR.Found) throw new InvalidOperationException($"Map not found: {mapId}");
        var map = mapR.Map;
        map.Id = mapId;
        if (map.Boards == null || map.Boards.Count == 0)
            throw new InvalidOperationException("MapConfig.Boards is empty.");

        Ludots.Core.Map.Board.BoardConfig? board = null;
        for (int i = 0; i < map.Boards.Count; i++)
        {
            var candidate = map.Boards[i];
            if (candidate == null) continue;
            if (string.Equals(candidate.Name, name, StringComparison.Ordinal))
            {
                board = candidate;
                break;
            }
        }

        if (board == null)
        {
            string available = string.Join(", ", map.Boards.Where(b => b != null).Select(b => b.Name));
            throw new InvalidOperationException($"Map board '{name}' was not found. Board names are case-sensitive. Available boards: {available}");
        }

        if (request.CellSizeCm.HasValue)
        {
            if (request.CellSizeCm.Value <= 0)
                throw new InvalidOperationException("CellSizeCm must be positive.");
            board.GridCellSizeCm = request.CellSizeCm.Value;
        }

        if (request.HexEdgeLengthCm.HasValue)
        {
            if (!string.Equals(board.SpatialType, "HexGrid", StringComparison.Ordinal))
                throw new InvalidOperationException("HexEdgeLengthCm can only be updated on HexGrid boards.");
            if (request.HexEdgeLengthCm.Value <= 0)
                throw new InvalidOperationException("HexEdgeLengthCm must be positive.");
            board.HexEdgeLengthCm = request.HexEdgeLengthCm.Value;
        }

        if (request.NavigationEnabled.HasValue)
        {
            board.NavigationEnabled = request.NavigationEnabled.Value;
        }

        string mapPath = WriteWritableMapConfig(ctx, mapId, map);
        var mapInfo = DescribeMap(ctx, mapId);
        var boardInfo = DescribeBoard(ctx, mapId, board);
        return new BoardMutationResult(map, mapInfo, boardInfo, mapPath, null);
    }

    public static BoardMutationResult DeleteBoard(ModContext ctx, string mapId, string boardName)
    {
        string name = RequireBoardName(boardName);
        var mapR = LoadMergedMapConfig(ctx, mapId);
        if (!mapR.Found) throw new InvalidOperationException($"Map not found: {mapId}");
        var map = mapR.Map;
        map.Id = mapId;
        if (map.Boards == null || map.Boards.Count == 0)
            throw new InvalidOperationException("MapConfig.Boards is empty.");
        if (map.Boards.Count == 1)
            throw new InvalidOperationException("Cannot delete the last board from a map.");

        int index = -1;
        for (int i = 0; i < map.Boards.Count; i++)
        {
            var candidate = map.Boards[i];
            if (candidate == null) continue;
            if (string.Equals(candidate.Name, name, StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            string available = string.Join(", ", map.Boards.Where(b => b != null).Select(b => b.Name));
            throw new InvalidOperationException($"Map board '{name}' was not found. Board names are case-sensitive. Available boards: {available}");
        }

        var removed = map.Boards[index];
        var removedInfo = DescribeBoard(ctx, mapId, removed);
        string? dataPath = null;
        if (!string.IsNullOrWhiteSpace(removed.DataFile))
        {
            dataPath = ResolveWritableDataFilePath(ctx, removed.DataFile);
        }

        map.Boards.RemoveAt(index);
        string mapPath = WriteWritableMapConfig(ctx, mapId, map);
        var mapInfo = DescribeMap(ctx, mapId);
        return new BoardMutationResult(map, mapInfo, removedInfo, mapPath, dataPath);
    }

    public static Ludots.Core.Map.Board.BoardConfig? ResolvePrimaryBoard(Ludots.Core.Config.MapConfig map)
    {
        if (map?.Boards == null || map.Boards.Count == 0)
            return null;

        Ludots.Core.Map.Board.BoardConfig? firstNavigationBoard = null;
        for (int i = 0; i < map.Boards.Count; i++)
        {
            var board = map.Boards[i];
            if (board == null) continue;
            if (!board.NavigationEnabled) continue;

            firstNavigationBoard ??= board;
            if (string.Equals(board.Name, "default", StringComparison.OrdinalIgnoreCase))
                return board;
        }

        if (firstNavigationBoard != null)
            return firstNavigationBoard;

        for (int i = 0; i < map.Boards.Count; i++)
        {
            var board = map.Boards[i];
            if (board == null) continue;
            if (string.Equals(board.Name, "default", StringComparison.OrdinalIgnoreCase))
                return board;
        }

        for (int i = 0; i < map.Boards.Count; i++)
        {
            if (map.Boards[i] != null)
                return map.Boards[i];
        }

        return null;
    }

    public static Ludots.Core.Map.Board.BoardConfig ResolveRequiredBoardByName(Ludots.Core.Config.MapConfig map, string? boardName)
    {
        if (map?.Boards == null || map.Boards.Count == 0)
            throw new InvalidOperationException("MapConfig.Boards is empty.");

        if (string.IsNullOrWhiteSpace(boardName))
            throw new InvalidOperationException("boardName query/form field is required when loading, saving, or baking editor terrain.");

        string requested = boardName.Trim();
        for (int i = 0; i < map.Boards.Count; i++)
        {
            var board = map.Boards[i];
            if (board == null) continue;
            if (string.Equals(board.Name, requested, StringComparison.Ordinal))
                return board;
        }

        string available = string.Join(", ", map.Boards.Where(b => b != null).Select(b => b.Name));
        throw new InvalidOperationException(
            $"Map board '{requested}' was not found. Board names are case-sensitive. Available boards: {available}");
    }

    public static string? ResolvePrimaryBoardDataFile(Ludots.Core.Config.MapConfig map)
    {
        return ResolvePrimaryBoard(map)?.DataFile;
    }

    public static bool IsGridBoard(Ludots.Core.Map.Board.BoardConfig board)
    {
        return string.Equals(NormalizeSpatialType(board), "Grid", StringComparison.Ordinal);
    }

    public static bool IsHexGridBoard(Ludots.Core.Map.Board.BoardConfig board)
    {
        return string.Equals(NormalizeSpatialType(board), "HexGrid", StringComparison.Ordinal);
    }

    public static string NormalizeSpatialType(Ludots.Core.Map.Board.BoardConfig board)
    {
        if (board == null) throw new ArgumentNullException(nameof(board));
        string? raw = board.SpatialType;
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidOperationException(
                $"Map board '{board.Name}' has empty SpatialType. Expected exact value Grid, HexGrid, or NodeGraph.");
        }

        string spatialType = raw.Trim();
        if (string.Equals(spatialType, "Grid", StringComparison.Ordinal))
        {
            return "Grid";
        }

        if (string.Equals(spatialType, "HexGrid", StringComparison.Ordinal))
        {
            return "HexGrid";
        }

        if (string.Equals(spatialType, "NodeGraph", StringComparison.Ordinal))
        {
            return "NodeGraph";
        }

        throw new InvalidOperationException(
            $"Map board '{board.Name}' has unsupported SpatialType '{board.SpatialType}'. Expected exact value Grid, HexGrid, or NodeGraph.");
    }

    public static int RequireGridCellSizeCm(Ludots.Core.Map.Board.BoardConfig board)
    {
        if (board == null) throw new ArgumentNullException(nameof(board));
        if (board.GridCellSizeCm <= 0)
        {
            throw new InvalidOperationException(
                $"Map board '{board.Name}' has invalid GridCellSizeCm {board.GridCellSizeCm}; value must be positive.");
        }

        return board.GridCellSizeCm;
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

    private static string WriteWritableMapConfig(ModContext ctx, string mapId, Ludots.Core.Config.MapConfig map)
    {
        map.Id = mapId;
        string outFile = ResolveWritableMapConfigPath(ctx, mapId);
        Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);
        File.WriteAllText(outFile, JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true }));
        return outFile;
    }

    private static string RequireBoardName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException("Board name is required.");

        string name = raw.Trim();
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\'))
            throw new InvalidOperationException($"Board name '{name}' is invalid for editor-managed board data files.");

        return name;
    }

    private static string NormalizeRequestedSpatialType(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException("SpatialType is required. Expected exact value Grid, HexGrid, or NodeGraph.");

        string spatialType = raw.Trim();
        if (string.Equals(spatialType, "Grid", StringComparison.Ordinal) ||
            string.Equals(spatialType, "HexGrid", StringComparison.Ordinal) ||
            string.Equals(spatialType, "NodeGraph", StringComparison.Ordinal))
        {
            return spatialType;
        }

        throw new InvalidOperationException($"Unsupported SpatialType '{raw}'. Expected exact value Grid, HexGrid, or NodeGraph.");
    }

    private static void EnsureNoBoardNameConflict(Ludots.Core.Config.MapConfig map, string name)
    {
        for (int i = 0; i < map.Boards.Count; i++)
        {
            var board = map.Boards[i];
            if (board == null) continue;
            if (string.Equals(board.Name, name, StringComparison.Ordinal))
                throw new InvalidOperationException($"Board '{name}' already exists.");
            if (string.Equals(board.Name, name, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Board name '{name}' conflicts with existing board '{board.Name}'. Board names are case-sensitive.");
        }
    }

    private static void ValidateBoardDimensions(int widthMacroTiles, int heightMacroTiles, int chunkSizeCells)
    {
        if (widthMacroTiles <= 0) throw new InvalidOperationException("WidthInMacroTiles must be positive.");
        if (heightMacroTiles <= 0) throw new InvalidOperationException("HeightInMacroTiles must be positive.");
        if (chunkSizeCells != Ludots.Core.Spatial.SpatialScaleDefaults.TerrainChunkCells)
        {
            throw new InvalidOperationException(
                $"React terrain editor creates boards with ChunkSizeCells={Ludots.Core.Spatial.SpatialScaleDefaults.TerrainChunkCells}; requested {chunkSizeCells}.");
        }
    }

    public static void RequireLogicTerrainDataFile(string dataFile)
    {
        if (string.IsNullOrWhiteSpace(dataFile))
        {
            throw new InvalidOperationException("Board DataFile is required for LogicTerrain.");
        }

        string extension = Path.GetExtension(dataFile.Trim());
        if (!string.Equals(extension, ".ltrn", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Board DataFile must use .ltrn LogicTerrain format. Legacy terrain files require explicit one-way import: {dataFile}");
        }
    }

    private static string BuildDefaultBoardDataFile(string mapId, string boardName, string spatialType)
    {
        return $"{SanitizeId(mapId)}_{SanitizeId(boardName)}.ltrn";
    }

    public static bool CanServeVirtualEmptyTerrain(string mapId, Ludots.Core.Map.Board.BoardConfig board)
    {
        if (string.IsNullOrWhiteSpace(mapId)) return false;
        if (string.IsNullOrWhiteSpace(board.DataFile)) return false;
        if (board.WidthInMacroTiles <= 0 || board.HeightInMacroTiles <= 0) return false;
        string spatialType = NormalizeSpatialType(board);
        if (!string.Equals(spatialType, "Grid", StringComparison.Ordinal) &&
            !string.Equals(spatialType, "HexGrid", StringComparison.Ordinal))
        {
            return false;
        }

        string defaultDataFile = BuildDefaultBoardDataFile(mapId, board.Name, spatialType);
        return string.Equals(board.DataFile.Trim(), defaultDataFile, StringComparison.Ordinal);
    }

    public static byte[] CreateEmptyLogicTerrainBinary(Ludots.Core.Map.Board.BoardConfig board)
    {
        int chunksPerMacro = Ludots.Core.Spatial.SpatialScaleDefaults.MacroTileCells / Ludots.Core.Spatial.SpatialScaleDefaults.TerrainChunkCells;
        int widthChunks = checked(board.WidthInMacroTiles * chunksPerMacro);
        int heightChunks = checked(board.HeightInMacroTiles * chunksPerMacro);
        var terrain = new SparseGridLogicTerrainField(
            checked(widthChunks * Ludots.Core.Spatial.SpatialScaleDefaults.TerrainChunkCells),
            checked(heightChunks * Ludots.Core.Spatial.SpatialScaleDefaults.TerrainChunkCells),
            board.GridCellSizeCm > 0 ? board.GridCellSizeCm : Ludots.Core.Spatial.SpatialScaleDefaults.CellCm,
            board.ChunkSizeCells > 0 ? board.ChunkSizeCells : Ludots.Core.Spatial.SpatialScaleDefaults.TerrainChunkCells);
        using var ms = new MemoryStream();
        LogicTerrainBinary.Write(ms, terrain);
        return ms.ToArray();
    }

    private static bool ShouldCreateFullEmptyTerrainDataFile(Ludots.Core.Map.Board.BoardConfig board)
    {
        return true;
    }

    private static void CreateEmptyTerrainDataFile(Ludots.Core.Map.Board.BoardConfig board, string outFile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);
        RequireLogicTerrainDataFile(outFile);
        File.WriteAllBytes(outFile, CreateEmptyLogicTerrainBinary(board));
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

sealed class BoardCreateRequest
{
    public string Name { get; set; } = string.Empty;
    public string SpatialType { get; set; } = "Grid";
    public int WidthInMacroTiles { get; set; }
    public int HeightInMacroTiles { get; set; }
    public int CellSizeCm { get; set; }
    public int HexEdgeLengthCm { get; set; }
    public int ChunkSizeCells { get; set; }
    public bool NavigationEnabled { get; set; } = true;
    public string? DataFile { get; set; }
}

sealed class BoardUpdateRequest
{
    public int? CellSizeCm { get; set; }
    public int? HexEdgeLengthCm { get; set; }
    public bool? NavigationEnabled { get; set; }
}

sealed class FlatGridNavBootstrapRequest
{
    public string? ModId { get; set; }
    public string MapId { get; set; } = string.Empty;
    public string BoardName { get; set; } = string.Empty;
    public string ProfileId { get; set; } = string.Empty;
    public int Layer { get; set; }
    public List<NavBootstrapChunk> Chunks { get; set; } = new List<NavBootstrapChunk>();
}

sealed class NavBootstrapChunk
{
    public int Cx { get; set; }
    public int Cy { get; set; }
}

sealed class NavPathQueryRequest
{
    public string? ModId { get; set; }
    public string MapId { get; set; } = string.Empty;
    public string BoardName { get; set; } = string.Empty;
    public string ProfileId { get; set; } = string.Empty;
    public int Layer { get; set; }
    public NavPointCm Start { get; set; } = new NavPointCm();
    public NavPointCm Goal { get; set; } = new NavPointCm();
    public int MaxPortals { get; set; } = 256;
    public List<NavAreaCostOverride>? AreaCosts { get; set; }
    public List<NavTilePayload> Tiles { get; set; } = new List<NavTilePayload>();
}

sealed class NavPointCm
{
    public int XCm { get; set; }
    public int ZCm { get; set; }
}

sealed class NavAreaCostOverride
{
    public int AreaId { get; set; }
    public float Cost { get; set; } = 1f;
}

sealed class NavTilePayload
{
    public string? ProfileId { get; set; }
    public int Layer { get; set; }
    public string DetourBase64 { get; set; } = string.Empty;
    public string? Source { get; set; }
}
