using System;
using System.Collections.Generic;
using System.IO;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Map;
using Ludots.Core.Map.Authoring;
using Ludots.Core.Map.Board;
using Ludots.Core.Navigation.Terrain;
using Ludots.Core.Spatial;
using NUnit.Framework;

namespace GasTests;

[TestFixture]
public sealed class MapAuthoringAssetWriterTests
{
    [Test]
    public void BoardAllocationPreview_UsesSpatialScaleDefaultsAndTerrainChunkBytes()
    {
        BoardAllocationPreview preview =
            BoardAllocationPreviewCalculator.FromDesiredMeters(512f, 256f, SpatialScaleDefaults.CellCm);

        Assert.That(preview.IsValid, Is.True);
        Assert.That(preview.WidthMacroTiles, Is.EqualTo(2));
        Assert.That(preview.HeightMacroTiles, Is.EqualTo(1));
        Assert.That(preview.AllocatedWidthCells, Is.EqualTo(512));
        Assert.That(preview.AllocatedHeightCells, Is.EqualTo(256));
        Assert.That(preview.WidthTerrainChunks, Is.EqualTo(8));
        Assert.That(preview.HeightTerrainChunks, Is.EqualTo(4));
        Assert.That(preview.TotalTerrainChunks, Is.EqualTo(32));
        Assert.That(preview.FullTerrainBytes, Is.EqualTo(32L * 64 * 64 * sizeof(int)));
        Assert.That(preview.SnappedToMacroTile, Is.False);
    }

    [Test]
    public void SaveConfig_CreatesNewMapAssetThatLoadMapCanDiscover()
    {
        string root = CreateTempDir();
        try
        {
            string modRoot = CreateAssetOnlyMod(root, "AuthoringMod", priority: 0);
            using var engine = CreateEngine(modRoot);
            var mapConfig = new MapConfig
            {
                Id = "created_map",
                Boards = new List<BoardConfig>
                {
                    new BoardConfig
                    {
                        Name = "default",
                        SpatialType = "Grid",
                        WidthInMacroTiles = 2,
                        HeightInMacroTiles = 1,
                        GridCellSizeCm = 100,
                        ChunkSizeCells = SpatialScaleDefaults.PartitionChunkCells,
                        NavigationEnabled = true
                    }
                }
            };

            MapAuthoringConfigSaveResult result =
                new MapAuthoringAssetWriter(engine).SaveConfig("AuthoringMod", mapConfig, overwriteExisting: false);
            engine.LoadMap("created_map");

            Assert.That(result.MapConfigPath, Is.EqualTo(Path.Combine(modRoot, "assets", "Maps", "created_map.json")));
            Assert.That(File.Exists(result.MapConfigPath), Is.True);
            Assert.That(engine.CurrentMapSession.AllBoards, Has.Count.EqualTo(1));
            Assert.That(engine.CurrentMapSession.PrimaryBoard, Is.TypeOf<GridBoard>());
            Assert.That(engine.LogicTerrain, Is.Not.Null);
            Assert.That(engine.LogicTerrain.WidthCells, Is.EqualTo(512));
            Assert.That(engine.LogicTerrain.HeightCells, Is.EqualTo(256));
            Assert.That(engine.LogicTerrain.HorizontalStepCm, Is.EqualTo(100));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Test]
    public void Save_CurrentSessionMapConfigWithBoardLifecycleChanges_ReloadsBoardStack()
    {
        string root = CreateTempDir();
        try
        {
            string modRoot = CreateAssetOnlyMod(root, "AuthoringMod", priority: 0);
            WriteMap(modRoot, "board_lifecycle_map", """
            {
              "id": "board_lifecycle_map",
              "boards": [
                {
                  "name": "default",
                  "spatialType": "Grid",
                  "widthInMacroTiles": 1,
                  "heightInMacroTiles": 1,
                  "gridCellSizeCm": 100,
                  "chunkSizeCells": 64,
                  "navigationEnabled": true
                }
              ],
              "entities": []
            }
            """);

            using var engine = CreateEngine(modRoot);
            engine.LoadMap("board_lifecycle_map");
            MapSession session = engine.CurrentMapSession;
            session.MapConfig.Boards.Add(new BoardConfig
            {
                Name = "secondary",
                SpatialType = "HexGrid",
                WidthInMacroTiles = 1,
                HeightInMacroTiles = 1,
                GridCellSizeCm = 200,
                HexEdgeLengthCm = 500,
                ChunkSizeCells = SpatialScaleDefaults.PartitionChunkCells,
                NavigationEnabled = false
            });

            new MapAuthoringAssetWriter(engine).Save(new MapAuthoringSaveRequest
            {
                Session = session,
                WriteNavTiles = false
            });
            engine.LoadMap("board_lifecycle_map");

            Assert.That(engine.CurrentMapSession.AllBoards, Has.Count.EqualTo(2));
            Assert.That(engine.CurrentMapSession.GetBoard("secondary"), Is.TypeOf<HexGridBoard>());

            session = engine.CurrentMapSession;
            session.MapConfig.Boards[0].GridCellSizeCm = 200;
            session.MapConfig.Boards.RemoveAt(1);
            new MapAuthoringAssetWriter(engine).Save(new MapAuthoringSaveRequest
            {
                Session = session,
                WriteNavTiles = false
            });
            engine.LoadMap("board_lifecycle_map");

            Assert.That(engine.CurrentMapSession.AllBoards, Has.Count.EqualTo(1));
            Assert.That(engine.CurrentMapSession.PrimaryBoard, Is.TypeOf<GridBoard>());
            Assert.That(((GridBoard)engine.CurrentMapSession.PrimaryBoard).GridCellSizeCm, Is.EqualTo(200));
            Assert.That(engine.LogicTerrain.HorizontalStepCm, Is.EqualTo(200));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Test]
    public void Save_IgnoresTagOnlyOverlayAndWritesAuthoringMapFragment()
    {
        string root = CreateTempDir();
        try
        {
            string baseMod = CreateAssetOnlyMod(root, "BaseAuthoringMod", priority: 0);
            string overlayMod = CreateAssetOnlyMod(root, "TagOnlyOverlayMod", priority: 100);
            WriteMap(baseMod, "authoring_map", """
            {
              "id": "authoring_map",
              "tags": ["base"],
              "boards": [
                {
                  "name": "default",
                  "spatialType": "Grid",
                  "widthInMacroTiles": 1,
                  "heightInMacroTiles": 1,
                  "gridCellSizeCm": 100,
                  "chunkSizeCells": 4,
                  "dataFile": "authoring_map_default.ltrn"
                }
              ],
              "entities": []
            }
            """);
            WriteMap(overlayMod, "authoring_map", """
            {
              "id": "authoring_map",
              "tags": ["LiveEditorOverlay"]
            }
            """);
            WriteLogicTerrain(baseMod, "authoring_map_default.ltrn", CreateTerrain(heightLevel: 2));
            string overlayPath = Path.Combine(overlayMod, "assets", "Maps", "authoring_map.json");
            string overlayBefore = File.ReadAllText(overlayPath);

            using var engine = CreateEngine(baseMod, overlayMod);
            engine.LoadMap("authoring_map");
            Assert.That(engine.CurrentMapSession, Is.Not.Null);
            Assert.That(engine.LogicTerrain, Is.Not.Null);

            MapAuthoringSaveResult result = new MapAuthoringAssetWriter(engine).Save(new MapAuthoringSaveRequest
            {
                Session = engine.CurrentMapSession,
                LogicTerrain = engine.LogicTerrain,
                Entities = Array.Empty<EntitySpawnData>(),
                WriteNavTiles = false
            });

            Assert.That(result.ModId, Is.EqualTo("BaseAuthoringMod"));
            Assert.That(result.MapConfigPath, Is.EqualTo(Path.Combine(baseMod, "assets", "Maps", "authoring_map.json")));
            Assert.That(result.TerrainPaths, Has.Count.EqualTo(1));
            Assert.That(File.ReadAllText(overlayPath), Is.EqualTo(overlayBefore));
            Assert.That(File.Exists(result.TerrainPaths[0]), Is.True);

            using FileStream stream = File.OpenRead(result.TerrainPaths[0]);
            LogicTerrainField savedTerrain = LogicTerrainBinary.Read(stream);
            Assert.That(savedTerrain.GetCell(0, 0).HeightLevel, Is.EqualTo(2));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Test]
    public void Save_FailsFastWhenMultipleAuthoringFragmentsDeclareBoards()
    {
        string root = CreateTempDir();
        try
        {
            string firstMod = CreateAssetOnlyMod(root, "FirstAuthoringMod", priority: 0);
            string secondMod = CreateAssetOnlyMod(root, "SecondAuthoringMod", priority: 100);
            WriteFullGridMap(firstMod, "authoring_map", "first.ltrn");
            WriteFullGridMap(secondMod, "authoring_map", "second.ltrn");
            WriteLogicTerrain(firstMod, "first.ltrn", CreateTerrain(heightLevel: 1));
            WriteLogicTerrain(secondMod, "second.ltrn", CreateTerrain(heightLevel: 3));

            using var engine = CreateEngine(firstMod, secondMod);
            engine.LoadMap("authoring_map");

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                new MapAuthoringAssetWriter(engine).Save(new MapAuthoringSaveRequest
                {
                    Session = engine.CurrentMapSession,
                    LogicTerrain = engine.LogicTerrain,
                    WriteNavTiles = false
                }))!;

            Assert.That(ex.Message, Does.Contain("multiple writable authoring map fragments"));
            Assert.That(ex.Message, Does.Contain("FirstAuthoringMod"));
            Assert.That(ex.Message, Does.Contain("SecondAuthoringMod"));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Test]
    public void Save_UsesExplicitLiveEditorSaveTargetWhenOverlayDeclaresBoards()
    {
        string root = CreateTempDir();
        try
        {
            string baseMod = CreateAssetOnlyMod(root, "BaseAuthoringMod", priority: 0);
            string overlayMod = CreateAssetOnlyMod(root, "ExplicitEditorOverlayMod", priority: 100);
            WriteFullGridMap(baseMod, "authoring_map", "base.ltrn");
            WriteFullGridMap(overlayMod, "authoring_map", "overlay.ltrn", explicitSaveTarget: true);
            WriteLogicTerrain(baseMod, "base.ltrn", CreateTerrain(heightLevel: 1));
            WriteLogicTerrain(overlayMod, "overlay.ltrn", CreateTerrain(heightLevel: 4));

            using var engine = CreateEngine(baseMod, overlayMod);
            engine.LoadMap("authoring_map");

            MapAuthoringSaveResult result = new MapAuthoringAssetWriter(engine).Save(new MapAuthoringSaveRequest
            {
                Session = engine.CurrentMapSession,
                LogicTerrain = engine.LogicTerrain,
                WriteNavTiles = false
            });

            Assert.That(result.ModId, Is.EqualTo("ExplicitEditorOverlayMod"));
            Assert.That(result.MapConfigPath, Is.EqualTo(Path.Combine(overlayMod, "assets", "Maps", "authoring_map.json")));
        }
        finally
        {
            TryDelete(root);
        }
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

    private static void WriteFullGridMap(string modRoot, string mapId, string dataFile, bool explicitSaveTarget = false)
    {
        string metadata = explicitSaveTarget
            ? """
              "metadata": {
                "liveMapEditor": {
                  "saveTarget": true
                }
              },
            """
            : string.Empty;
        WriteMap(modRoot, mapId, $$"""
        {
          "id": "{{mapId}}",
          {{metadata}}
          "boards": [
            {
              "name": "default",
              "spatialType": "Grid",
              "widthInMacroTiles": 1,
              "heightInMacroTiles": 1,
              "gridCellSizeCm": 100,
              "chunkSizeCells": 4,
              "dataFile": "{{dataFile}}"
            }
          ]
        }
        """);
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
        string path = Path.Combine(Path.GetTempPath(), "ludots_map_authoring_writer_" + Guid.NewGuid().ToString("N"));
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
