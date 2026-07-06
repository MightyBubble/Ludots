using System.Reflection;
using System.Text.Json;
using Ludots.Core.Engine;
using Ludots.Core.Navigation.Terrain;
using Ludots.WebUI.DataPlane;
using NUnit.Framework;

namespace GasTests;

[TestFixture]
public sealed class LiveMapEditorBrushCommandTests
{
    [Test]
    public async Task PaintTerrainCommand_WithExplicitCell_UpdatesFocusedLogicTerrain()
    {
        string root = CreateTempDir();
        try
        {
            string modRoot = CreateAssetOnlyMod(root, "LiveEditorBrushTestMod", priority: 0);
            WriteMap(modRoot, "brush_map", """
            {
              "id": "brush_map",
              "boards": [
                {
                  "name": "default",
                  "spatialType": "Grid",
                  "widthInMacroTiles": 1,
                  "heightInMacroTiles": 1,
                  "gridCellSizeCm": 100,
                  "chunkSizeCells": 4,
                  "navigationEnabled": false,
                  "dataFile": "brush_map_default.ltrn"
                }
              ],
              "entities": []
            }
            """);
            WriteLogicTerrain(modRoot, "brush_map_default.ltrn", CreateTerrain(heightLevel: 1));

            using var engine = CreateEngine(modRoot);
            engine.LoadMap("brush_map");
            IWebUiCommandHandler handler = CreateCommandHandler(engine);

            WebUiCommandResult brush = await HandleAsync(handler, "setBrush", new
            {
                mode = "set",
                target = "height",
                radiusCells = 0,
                heightLevel = 7,
                waterHeightLevel = 0,
                areaId = 0,
                cost = 1,
                blocked = false,
                water = false,
                ramp = false
            });
            Assert.That(brush.Success, Is.True, brush.Message);

            WebUiCommandResult paint = await HandleAsync(handler, "paintTerrain", new
            {
                col = 2,
                row = 1,
                radiusCells = 0
            });

            Assert.That(paint.Success, Is.True, paint.Message);
            Assert.That(engine.LogicTerrain.GetCell(2, 1).HeightLevel, Is.EqualTo(7));
            Assert.That(engine.LogicTerrain.GetCell(0, 0).HeightLevel, Is.EqualTo(1));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Test]
    public async Task PaintTerrainCommand_WithoutPickOrExplicitCell_ReturnsNoPick()
    {
        string root = CreateTempDir();
        try
        {
            string modRoot = CreateAssetOnlyMod(root, "LiveEditorBrushNoPickMod", priority: 0);
            WriteMap(modRoot, "brush_map", """
            {
              "id": "brush_map",
              "boards": [
                {
                  "name": "default",
                  "spatialType": "Grid",
                  "widthInMacroTiles": 1,
                  "heightInMacroTiles": 1,
                  "gridCellSizeCm": 100,
                  "chunkSizeCells": 4,
                  "navigationEnabled": false,
                  "dataFile": "brush_map_default.ltrn"
                }
              ],
              "entities": []
            }
            """);
            WriteLogicTerrain(modRoot, "brush_map_default.ltrn", CreateTerrain(heightLevel: 1));

            using var engine = CreateEngine(modRoot);
            engine.LoadMap("brush_map");
            IWebUiCommandHandler handler = CreateCommandHandler(engine);

            WebUiCommandResult paint = await HandleAsync(handler, "paintTerrain", new
            {
                radiusCells = 0
            });

            Assert.That(paint.Success, Is.False);
            Assert.That(paint.ErrorCode, Is.EqualTo("no_pick"));
            Assert.That(engine.LogicTerrain.GetCell(0, 0).HeightLevel, Is.EqualTo(1));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Test]
    public async Task PaintTerrainCommand_WithExplicitOutOfBoundsCell_ReturnsOutOfBoundsAndKeepsTerrainClean()
    {
        string root = CreateTempDir();
        try
        {
            string modRoot = CreateAssetOnlyMod(root, "LiveEditorBrushOutOfBoundsMod", priority: 0);
            WriteMap(modRoot, "brush_map", """
            {
              "id": "brush_map",
              "boards": [
                {
                  "name": "default",
                  "spatialType": "Grid",
                  "widthInMacroTiles": 1,
                  "heightInMacroTiles": 1,
                  "gridCellSizeCm": 100,
                  "chunkSizeCells": 4,
                  "navigationEnabled": false,
                  "dataFile": "brush_map_default.ltrn"
                }
              ],
              "entities": []
            }
            """);
            WriteLogicTerrain(modRoot, "brush_map_default.ltrn", CreateTerrain(heightLevel: 1));

            using var engine = CreateEngine(modRoot);
            engine.LoadMap("brush_map");
            IWebUiCommandHandler handler = CreateCommandHandler(engine);

            WebUiCommandResult paint = await HandleAsync(handler, "paintTerrain", new
            {
                col = -1,
                row = 1,
                radiusCells = 0
            });

            Assert.That(paint.Success, Is.False);
            Assert.That(paint.ErrorCode, Is.EqualTo("paint_out_of_bounds"));
            Assert.That(engine.LogicTerrain.GetCell(0, 1).HeightLevel, Is.EqualTo(1));
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static async Task<WebUiCommandResult> HandleAsync(
        IWebUiCommandHandler handler,
        string name,
        object payload)
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

    private static GameEngine CreateEngine(params string[] modRoots)
    {
        string repoRoot = FindRepoRoot();
        var engine = new GameEngine();
        var paths = new List<string>
        {
            Path.Combine(repoRoot, "mods", "LudotsCoreMod")
        };
        paths.AddRange(modRoots);
        engine.InitializeWithConfigPipeline(paths, Path.Combine(repoRoot, "assets"));
        return engine;
    }

    private static MutableGridLogicTerrainField CreateTerrain(byte heightLevel)
    {
        var terrain = new MutableGridLogicTerrainField(widthCells: 4, heightCells: 4, cellSizeCm: 100, chunkSizeCells: 4);
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

    private static string CreateTempDir()
    {
        string path = Path.Combine(Path.GetTempPath(), "ludots_live_editor_brush_" + Guid.NewGuid().ToString("N"));
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
