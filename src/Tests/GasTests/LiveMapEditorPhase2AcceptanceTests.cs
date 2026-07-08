using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Engine;
using Ludots.Core.Map.Board;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.Terrain;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Scripting;
using Ludots.WebUI.DataPlane;
using NUnit.Framework;

namespace GasTests;

[TestFixture]
public sealed class LiveMapEditorPhase2AcceptanceTests
{
    [Test]
    public async Task Phase2BrushButtons_EditGridTerrainAndRaylibHeightmapSource()
    {
        string root = CreateTempDir("phase2_brush");
        try
        {
            string modRoot = CreateAssetOnlyMod(root, "LiveEditorPhase2BrushMod", priority: 0);
            WriteMap(modRoot, "phase2_grid_map", """
            {
              "id": "phase2_grid_map",
              "metadata": { "liveMapEditor": { "saveTarget": true } },
              "boards": [
                {
                  "name": "default",
                  "spatialType": "Grid",
                  "widthInMacroTiles": 1,
                  "heightInMacroTiles": 1,
                  "gridCellSizeCm": 100,
                  "chunkSizeCells": 4,
                  "navigationEnabled": false,
                  "dataFile": "phase2_grid_map_default.ltrn"
                }
              ],
              "entities": []
            }
            """);
            WriteLogicTerrain(modRoot, "phase2_grid_map_default.ltrn", CreateTerrain(width: 8, height: 8, heightLevel: 1, chunkSizeCells: 4));

            using GameEngine engine = CreateEngine(FindRepoRoot(), modRoot);
            engine.LoadMap("phase2_grid_map");
            IWebUiCommandHandler handler = CreateCommandHandler(engine);

            await Ok(handler, "setBrush", new
            {
                mode = "set",
                target = "all",
                radiusCells = 1,
                heightLevel = 7,
                waterHeightLevel = 8,
                areaId = 3,
                cost = 2.5f,
                blocked = true,
                water = true,
                ramp = true
            });
            await Ok(handler, "paintTerrain", new { col = 3, row = 3 });

            LogicTerrainCell painted = engine.LogicTerrain.GetCell(3, 3);
            Assert.Multiple(() =>
            {
                Assert.That(painted.HeightLevel, Is.EqualTo(7), "Paint button must update Core LogicTerrain height.");
                Assert.That(painted.WaterHeightLevel, Is.EqualTo(8), "Water height must survive through the brush command.");
                Assert.That(painted.AreaId, Is.EqualTo(3), "Area tint source must be Core LogicTerrain.");
                Assert.That(painted.Cost, Is.EqualTo(2.5f).Within(0.0001f));
                Assert.That(painted.SurfaceFlags.HasFlag(LogicTerrainSurfaceFlags.Blocked), Is.True);
                Assert.That(painted.SurfaceFlags.HasFlag(LogicTerrainSurfaceFlags.Water), Is.True);
                Assert.That(painted.SurfaceFlags.HasFlag(LogicTerrainSurfaceFlags.Ramp), Is.True);
            });

            Assert.That(engine.CurrentMapSession?.VisualHeightmap, Is.TypeOf(GetCoreType("Ludots.Core.Presentation.Terrain.LogicTerrainVisualHeightmapAdapter")));
            var featureSource = (IVisualTerrainRenderFeatureSource)engine.CurrentMapSession!.VisualHeightmap!;
            Assert.That(featureSource.TryReadFeatureCell(3, 3, out VisualTerrainRenderCell visualCell), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(visualCell.HeightLevel, Is.EqualTo(7), "Raylib feature source must reflect edited height.");
                Assert.That(visualCell.WaterHeightLevel, Is.EqualTo(8), "Raylib water mesh source must reflect edited water.");
                Assert.That(visualCell.AreaId, Is.EqualTo(3), "Raylib area tint source must reflect edited area.");
                Assert.That(visualCell.SurfaceFlags.HasFlag(VisualTerrainSurfaceFlags.Blocked), Is.True);
                Assert.That(visualCell.SurfaceFlags.HasFlag(VisualTerrainSurfaceFlags.Water), Is.True);
                Assert.That(visualCell.SurfaceFlags.HasFlag(VisualTerrainSurfaceFlags.Ramp), Is.True);
            });

            await Ok(handler, "setBrush", new
            {
                mode = "raise",
                target = "height",
                radiusCells = 0,
                heightLevel = 2
            });
            await Ok(handler, "paintTerrain", new { col = 3, row = 3, radiusCells = 0 });
            Assert.That(engine.LogicTerrain.GetCell(3, 3).HeightLevel, Is.EqualTo(9), "Raise brush mode must mutate the real terrain.");

            await Ok(handler, "bucketFillWater", new { col = 0, row = 0, waterHeightLevel = 5 });
            LogicTerrainCell bucketed = engine.LogicTerrain.GetCell(0, 0);
            Assert.Multiple(() =>
            {
                Assert.That(bucketed.WaterHeightLevel, Is.EqualTo(5));
                Assert.That(bucketed.SurfaceFlags.HasFlag(LogicTerrainSurfaceFlags.Water), Is.True);
            });
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Test]
    public async Task Phase2IntegratedUat_EditsTerrainBakesValidNavmeshAndRunsPathAndTransportRoutes()
    {
        string repoRoot = FindRepoRoot();
        using GameEngine engine = CreateEngine(
            repoRoot,
            Path.Combine(repoRoot, "mods", "LudotsCoreMod"),
            Path.Combine(repoRoot, "mods", "CoreInputMod"),
            Path.Combine(repoRoot, "mods", "capabilities", "live_map_editor", "LiveMapEditorMod"),
            Path.Combine(repoRoot, "mods", "capabilities", "live_map_editor", "LiveMapEditorIntegratedUatMod"));
        engine.LoadMap("live_editor_integrated_nav_transport");
        IWebUiCommandHandler handler = CreateCommandHandler(engine);
        object runtime = ExtractRuntime(handler);

        Assert.That(engine.CurrentMapSession?.MapConfig.Boards, Has.Count.EqualTo(2));
        Assert.That(engine.CurrentMapSession!.MapConfig.Boards.Any(board => board.SpatialType == "Grid"), Is.True);
        Assert.That(engine.CurrentMapSession.MapConfig.Boards.Any(board => board.SpatialType == "NodeGraph"), Is.True);
        Assert.That(engine.CurrentMapSession.VisualHeightmap, Is.AssignableTo<IVisualTerrainRenderFeatureSource>());

        await Ok(handler, "setBrush", new
        {
            mode = "set",
            target = "all",
            radiusCells = 2,
            heightLevel = 2,
            waterHeightLevel = 4,
            areaId = 1,
            cost = 1.25f,
            blocked = false,
            water = true,
            ramp = false
        });
        await Ok(handler, "paintTerrain", new { col = 20, row = 20 });
        await Ok(handler, "estimateNavBake", new { scope = "dirty+n", includeNeighbors = true });
        Assert.That(ReadRuntimeInt(runtime, "Nav", "LastEstimatedTiles"), Is.GreaterThan(0), "Dirty+N estimate should see painted terrain.");

        await Ok(handler, "rebakeNav", new { scope = "full", maxTiles = 32, includeNeighbors = true, parallel = false });
        Assert.That(ReadRuntimeInt(runtime, "Nav", "LastFailedTiles"), Is.EqualTo(0), "Runtime CDT bake must not publish failed entries.");
        Assert.That(ReadRuntimeInt(runtime, "Nav", "LastRebuiltTiles"), Is.GreaterThan(0), "Runtime CDT bake must rebuild at least one tile.");
        AssertNavTilesAreValid(engine);

        await Ok(handler, "setPathOptions", new { profileId = "Small", layer = 0, maxPortals = 256 });
        await Ok(handler, "queryPath", new { startXcm = 1200, startYcm = 1200, goalXcm = 5200, goalYcm = 5200 });
        Assert.That(ReadRuntimeEnumName(runtime, "Nav", "PathStatus"), Is.EqualTo("Ok"));
        Assert.That(ReadRuntimeIntArray(runtime, "Nav", "PathXcm").Length, Is.GreaterThan(1));

        await Ok(handler, "transportSetMode", new { mode = "route" });
        await Ok(handler, "transportQueryRoute", new
        {
            agentTypeId = "Transport.ShallowBoat",
            startXcm = 7200,
            startYcm = 12800,
            goalXcm = 18400,
            goalYcm = 12800
        });
        object transport = runtime.GetType().GetProperty("Transport", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(runtime)!;
        Assert.Multiple(() =>
        {
            Assert.That(ReadObjectBool(transport, "Available"), Is.True, "Integrated UAT must expose a NodeGraph board.");
            Assert.That(ReadObjectEnumName(transport, "RouteStatus"), Is.EqualTo("Found"));
            Assert.That(ReadObjectIntArray(transport, "RoutePathXcm").Length, Is.GreaterThan(1));
        });

        await Ok(handler, "transportQueryRoute", new
        {
            agentTypeId = "Transport.DeepDraftShip",
            startXcm = 7200,
            startYcm = 12800,
            goalXcm = 18400,
            goalYcm = 12800
        });
        Assert.That(ReadObjectEnumName(transport, "RouteStatus"), Is.EqualTo("Found"), "Deep draft routing should use the deep channel instead of failing.");
        Assert.That(ReadObjectIntArray(transport, "RoutePathXcm").Length, Is.GreaterThan(1));
    }

    [Test]
    public async Task Phase2MapLifecycle_CreatesHexBoardAndPreviewsHugeMapWithoutManualSteps()
    {
        string root = CreateTempDir("phase2_map_lifecycle");
        try
        {
            string modRoot = CreateAssetOnlyMod(root, "LiveEditorPhase2LifecycleMod", priority: 0);
            WriteMap(modRoot, "phase2_seed_map", """
            {
              "id": "phase2_seed_map",
              "metadata": { "liveMapEditor": { "saveTarget": true } },
              "boards": [
                {
                  "name": "default",
                  "spatialType": "Grid",
                  "widthInMacroTiles": 1,
                  "heightInMacroTiles": 1,
                  "gridCellSizeCm": 100,
                  "chunkSizeCells": 4,
                  "navigationEnabled": false,
                  "dataFile": "phase2_seed_map_default.ltrn"
                }
              ],
              "entities": []
            }
            """);
            WriteLogicTerrain(modRoot, "phase2_seed_map_default.ltrn", CreateTerrain(width: 4, height: 4, heightLevel: 1, chunkSizeCells: 4));

            using GameEngine engine = CreateEngine(FindRepoRoot(), modRoot);
            engine.LoadMap("phase2_seed_map");
            IWebUiCommandHandler handler = CreateCommandHandler(engine);
            object runtime = ExtractRuntime(handler);

            await Ok(handler, "previewBoardAllocation", new
            {
                slot = "createMap",
                widthMeters = 128,
                heightMeters = 128,
                cellSizeCm = 100
            });
            object createPreview = ReadRuntimeObject(runtime, "MapLifecycle", "CreateMapPreview");
            Assert.Multiple(() =>
            {
                Assert.That(ReadObjectInt(createPreview, "WidthMacroTiles"), Is.EqualTo(1));
                Assert.That(ReadObjectInt(createPreview, "HeightMacroTiles"), Is.EqualTo(1));
                Assert.That(ReadObjectBool(createPreview, "IsValid"), Is.True);
            });

            await Ok(handler, "createMap", new
            {
                mapId = "phase2_hex_created",
                boardName = "hex_main",
                topology = "HexGrid",
                widthMeters = 256,
                heightMeters = 256,
                cellSizeCm = 100,
                hexEdgeLengthCm = 350,
                navigationEnabled = false,
                loadAfterCreate = false
            });
            JsonObject hexMap = ReadMapObject(modRoot, "phase2_hex_created");
            JsonObject hexBoard = ((JsonArray)hexMap["Boards"]!).OfType<JsonObject>().Single();
            Assert.Multiple(() =>
            {
                Assert.That(hexBoard["SpatialType"]?.GetValue<string>(), Is.EqualTo("HexGrid"));
                Assert.That(hexBoard["HexEdgeLengthCm"]?.GetValue<int>(), Is.EqualTo(350));
                Assert.That(hexBoard["Name"]?.GetValue<string>(), Is.EqualTo("hex_main"));
            });

            await Ok(handler, "previewBoardAllocation", new
            {
                slot = "addBoard",
                widthMeters = 40960,
                heightMeters = 40960,
                cellSizeCm = 100
            });
            object hugePreview = ReadRuntimeObject(runtime, "MapLifecycle", "AddBoardPreview");
            Assert.Multiple(() =>
            {
                Assert.That(ReadObjectInt(hugePreview, "WidthMacroTiles"), Is.EqualTo(160));
                Assert.That(ReadObjectInt(hugePreview, "HeightMacroTiles"), Is.EqualTo(160));
                Assert.That(ReadObjectInt(hugePreview, "TotalTerrainChunks"), Is.EqualTo(640 * 640));
                Assert.That(ReadObjectBool(hugePreview, "ExceedsDefaultWorldFootprint"), Is.True);
            });

            await Ok(handler, "cameraPanTo", new { xCm = 32000, yCm = 48000 });
            Vector2 target = engine.GameSession.Camera.State.TargetCm;
            Assert.Multiple(() =>
            {
                Assert.That(target.X, Is.EqualTo(32000f).Within(0.01f));
                Assert.That(target.Y, Is.EqualTo(48000f).Within(0.01f));
                Assert.That(float.IsFinite(engine.GameSession.Camera.State.DistanceCm), Is.True, "Camera state must remain finite after large-map pan.");
                Assert.That(float.IsFinite(engine.GameSession.Camera.State.Pitch), Is.True);
                Assert.That(float.IsFinite(engine.GameSession.Camera.State.FovYDeg), Is.True);
            });
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static async Task Ok(IWebUiCommandHandler handler, string name, object payload)
    {
        WebUiCommandResult result = await HandleAsync(handler, name, payload);
        Assert.That(result.Success, Is.True, $"{name} failed: {result.ErrorCode} {result.Message}");
    }

    private static async Task<WebUiCommandResult> HandleAsync(IWebUiCommandHandler handler, string name, object payload)
    {
        var request = new WebUiCommandRequest(
            name,
            ClientSeq: 1,
            Array.Empty<WebUiEntityRef>(),
            JsonSerializer.SerializeToElement(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return await handler.HandleAsync(request, TestContext.CurrentContext.CancellationToken);
    }

    private static IWebUiCommandHandler CreateCommandHandler(GameEngine engine)
    {
        Assembly assembly = LoadLiveMapEditorAssembly();
        Type runtimeType = assembly.GetType(
            "LiveMapEditorMod.Runtime.LiveMapEditorRuntime",
            throwOnError: true)!;
        Type handlerType = assembly.GetType(
            "LiveMapEditorMod.WebUi.LiveMapEditorCommandHandler",
            throwOnError: true)!;

        object runtime = Activator.CreateInstance(
            runtimeType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: Array.Empty<object>(),
            culture: null)!;
        object handler = Activator.CreateInstance(
            handlerType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: new[] { engine, runtime },
            culture: null)!;
        return (IWebUiCommandHandler)handler;
    }

    private static Assembly LoadLiveMapEditorAssembly()
    {
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LiveMapEditorMod.dll");
        return File.Exists(path) ? Assembly.LoadFrom(path) : Assembly.Load("LiveMapEditorMod");
    }

    private static object ExtractRuntime(IWebUiCommandHandler handler)
    {
        FieldInfo field = handler.GetType().GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(handler.GetType().FullName, "_runtime");
        return field.GetValue(handler)!;
    }

    private static GameEngine CreateEngine(string repoRoot, params string[] modRoots)
    {
        var engine = new GameEngine();
        var paths = new List<string>();
        if (!modRoots.Any(path => Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) == "LudotsCoreMod"))
        {
            paths.Add(Path.Combine(repoRoot, "mods", "LudotsCoreMod"));
        }

        paths.AddRange(modRoots);
        engine.InitializeWithConfigPipeline(paths, Path.Combine(repoRoot, "assets"));
        return engine;
    }

    private static MutableGridLogicTerrainField CreateTerrain(int width, int height, byte heightLevel, int chunkSizeCells)
    {
        var terrain = new MutableGridLogicTerrainField(width, height, cellSizeCm: 100, chunkSizeCells);
        terrain.Fill(new LogicTerrainCell(heightLevel, 0, LogicTerrainSurfaceFlags.None));
        return terrain;
    }

    private static void WriteLogicTerrain(string modRoot, string dataFile, LogicTerrainField terrain)
    {
        string dir = Path.Combine(modRoot, "assets", "Data", "Maps");
        Directory.CreateDirectory(dir);
        using FileStream stream = File.Create(Path.Combine(dir, dataFile));
        LogicTerrainBinary.Write(stream, terrain);
    }

    private static void WriteMap(string modRoot, string mapId, string json)
    {
        string dir = Path.Combine(modRoot, "assets", "Maps");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, $"{mapId}.json"), json);
    }

    private static JsonObject ReadMapObject(string modRoot, string mapId)
        => JsonNode.Parse(File.ReadAllText(Path.Combine(modRoot, "assets", "Maps", $"{mapId}.json"))) as JsonObject
           ?? throw new InvalidDataException($"Map '{mapId}' must be a JSON object.");

    private static string CreateAssetOnlyMod(string root, string name, int priority)
    {
        string modRoot = Path.Combine(root, name);
        Directory.CreateDirectory(modRoot);
        File.WriteAllText(Path.Combine(modRoot, "mod.json"), $$"""
        {
          "name": "{{name}}",
          "version": "1.0.0",
          "main": "",
          "priority": {{priority}},
          "dependencies": {}
        }
        """);
        return modRoot;
    }

    private static void AssertNavTilesAreValid(GameEngine engine)
    {
        NavQueryServiceRegistry registry = engine.GetService(CoreServiceKeys.NavQueryServices)
            ?? throw new InvalidOperationException("NavQueryServices missing.");
        IReadOnlyList<KeyValuePair<NavQueryServiceKey, NavTileStore>> stores = registry.SnapshotStores();
        Assert.That(stores, Is.Not.Empty);

        int tileCount = 0;
        int triangleCount = 0;
        foreach (KeyValuePair<NavQueryServiceKey, NavTileStore> pair in stores)
        {
            foreach (NavTile tile in pair.Value.SnapshotLoadedTiles())
            {
                tileCount++;
                Assert.That(tile.VertexCount, Is.GreaterThan(0), $"Nav tile {tile.TileId} must have vertices.");
                Assert.That(tile.TriangleCount, Is.GreaterThan(0), $"Nav tile {tile.TileId} must have triangles.");
                Assert.That(tile.VertexXcm.Length, Is.EqualTo(tile.VertexYcm.Length));
                Assert.That(tile.VertexXcm.Length, Is.EqualTo(tile.VertexZcm.Length));
                Assert.That(tile.TriB.Length, Is.EqualTo(tile.TriangleCount));
                Assert.That(tile.TriC.Length, Is.EqualTo(tile.TriangleCount));
                Assert.That(tile.N0.Length, Is.EqualTo(tile.TriangleCount));
                Assert.That(tile.N1.Length, Is.EqualTo(tile.TriangleCount));
                Assert.That(tile.N2.Length, Is.EqualTo(tile.TriangleCount));

                for (int i = 0; i < tile.TriangleCount; i++)
                {
                    int a = tile.TriA[i];
                    int b = tile.TriB[i];
                    int c = tile.TriC[i];
                    Assert.That(a, Is.InRange(0, tile.VertexCount - 1), $"Nav tile {tile.TileId} triangle {i} A index invalid.");
                    Assert.That(b, Is.InRange(0, tile.VertexCount - 1), $"Nav tile {tile.TileId} triangle {i} B index invalid.");
                    Assert.That(c, Is.InRange(0, tile.VertexCount - 1), $"Nav tile {tile.TileId} triangle {i} C index invalid.");
                    long area2 =
                        ((long)tile.VertexXcm[b] - tile.VertexXcm[a]) * (tile.VertexZcm[c] - tile.VertexZcm[a]) -
                        ((long)tile.VertexZcm[b] - tile.VertexZcm[a]) * (tile.VertexXcm[c] - tile.VertexXcm[a]);
                    Assert.That(area2, Is.Not.EqualTo(0), $"Nav tile {tile.TileId} triangle {i} is degenerate in XZ.");
                    triangleCount++;
                }
            }
        }

        Assert.That(tileCount, Is.GreaterThan(0), "Rebake must publish visible nav tiles.");
        Assert.That(triangleCount, Is.GreaterThan(0), "Rebake must publish visible nav triangles.");
    }

    private static int ReadRuntimeInt(object runtime, string ownerProperty, string property)
        => ReadObjectInt(ReadRuntimeObject(runtime, ownerProperty), property);

    private static string ReadRuntimeEnumName(object runtime, string ownerProperty, string property)
        => ReadObjectEnumName(ReadRuntimeObject(runtime, ownerProperty), property);

    private static int[] ReadRuntimeIntArray(object runtime, string ownerProperty, string property)
        => ReadObjectIntArray(ReadRuntimeObject(runtime, ownerProperty), property);

    private static object ReadRuntimeObject(object runtime, string property)
        => runtime.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(runtime)!;

    private static object ReadRuntimeObject(object runtime, string ownerProperty, string property)
        => ReadRuntimeObject(ReadRuntimeObject(runtime, ownerProperty), property);

    private static int ReadObjectInt(object target, string property)
        => (int)target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(target)!;

    private static bool ReadObjectBool(object target, string property)
        => (bool)target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(target)!;

    private static string ReadObjectEnumName(object target, string property)
        => target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(target)!.ToString()!;

    private static int[] ReadObjectIntArray(object target, string property)
        => (int[])target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(target)!;

    private static Type GetCoreType(string fullName)
        => typeof(GameEngine).Assembly.GetType(fullName, throwOnError: true)!;

    private static string CreateTempDir(string prefix)
    {
        string path = Path.Combine(Path.GetTempPath(), $"ludots_live_editor_{prefix}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? current = new(AppDomain.CurrentDomain.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "src", "Core", "Ludots.Core.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repo root containing src/Core/Ludots.Core.csproj");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }
}
