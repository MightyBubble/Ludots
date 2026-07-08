using System;
using System.IO;
using System.Linq;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Gas
{
    [TestFixture]
    public sealed class MapVisualHeightmapContractTests
    {
        private string _root = string.Empty;
        private string _coreRoot = string.Empty;
        private string _modRoot = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "Ludots_MapVisualHeightmapContractTests", Guid.NewGuid().ToString("N"));
            _coreRoot = Path.Combine(_root, "assets");
            _modRoot = Path.Combine(_root, "mods", "TestMapMod");
            Directory.CreateDirectory(Path.Combine(_coreRoot, "Configs", "Maps"));
            Directory.CreateDirectory(Path.Combine(_coreRoot, "Configs", "Navigation"));
            Directory.CreateDirectory(Path.Combine(_modRoot, "assets", "Configs", "Maps"));
            Directory.CreateDirectory(Path.Combine(_modRoot, "assets", "terrain"));

            string repoRoot = FindRepoRoot();
            CopyDirectory(Path.Combine(repoRoot, "assets", "Configs"), Path.Combine(_coreRoot, "Configs"));

            File.WriteAllText(Path.Combine(_coreRoot, "Configs", "game.json"), """
            {
              "startupMapId": "outer_map",
              "worldWidthInMacroTiles": 16,
              "worldHeightInMacroTiles": 16,
              "gridCellSizeCm": 100
            }
            """);

            File.WriteAllText(Path.Combine(_coreRoot, "Configs", "Navigation", "agent_profiles.json"), """
            [
              {
                "id": "Small",
                "radiusCm": 30,
                "heightCm": 180,
                "clearanceCm": 40,
                "draftCm": 0,
                "beamCm": 0,
                "mass": 1,
                "layer": 0
              }
            ]
            """);

            File.WriteAllText(Path.Combine(_coreRoot, "Configs", "Navigation", "pathing.json"), """
            {
              "agentTypes": [
                {
                  "id": "Humanoid",
                  "profileId": "Small",
                  "selection": {
                    "mode": "AutoCheapest",
                    "graphBias": 0.0,
                    "meshBias": 0.0,
                    "graphCostWeight": 1.0,
                    "meshCostWeight": 1.0
                  },
                  "navMesh": {
                    "areaCosts": [
                      { "areaId": 0, "cost": 1.0 }
                    ]
                  },
                  "nodeGraph": {
                    "projectionMaxRadiusCm": 200000,
                    "useDynamicOverlay": false,
                    "forbiddenTagsAny": [],
                    "requiredTagsAll": [],
                    "tagCostRules": []
                  }
                }
              ]
            }
            """);

            File.WriteAllText(Path.Combine(_modRoot, "mod.json"), """
            {
              "name": "TestMapMod",
              "version": "1.0.0",
              "description": "test mod",
              "main": "",
              "priority": 0,
              "dependencies": {}
            }
            """);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, recursive: true);
                }
            }
            catch
            {
            }
        }

        [Test]
        public void LoadMap_BindsDeclaredVisualHeightmapThroughCoreService()
        {
            WriteHeightmap("outer.vhtm", 50);
            WriteMap("outer_map", """
            {
              "id": "outer_map",
              "visualHeightmapAsset": "assets/terrain/outer.vhtm"
            }
            """);

            using var engine = CreateEngine();
            engine.LoadMap("outer_map");

            IVisualHeightmap heightmap = engine.GetService(CoreServiceKeys.VisualHeightmap);
            Assert.That(heightmap, Is.TypeOf<VisualHeightmapRuntime>());
            Assert.That(heightmap.TrySampleHeightCm(50f, 50f, out float heightCm), Is.True);
            Assert.That(heightCm, Is.EqualTo(50f).Within(0.001f));
        }

        [Test]
        public void LoadMap_WhenDeclaredVisualHeightmapMissing_ThrowsExplicitly()
        {
            WriteMap("outer_map", """
            {
              "id": "outer_map",
              "visualHeightmapAsset": "assets/terrain/missing.vhtm"
            }
            """);

            using var engine = CreateEngine();
            var ex = Assert.Throws<FileNotFoundException>(() => engine.LoadMap("outer_map"));
            Assert.That(ex!.Message, Does.Contain("visual heightmap asset"));
        }

        [Test]
        public void PushAndPopMap_RestoreFocusedMapVisualHeightmap()
        {
            WriteHeightmap("outer.vhtm", 25);
            WriteHeightmap("inner.vhtm", 125);
            WriteMap("outer_map", """
            {
              "id": "outer_map",
              "visualHeightmapAsset": "assets/terrain/outer.vhtm"
            }
            """);
            WriteMap("inner_map", """
            {
              "id": "inner_map",
              "visualHeightmapAsset": "assets/terrain/inner.vhtm"
            }
            """);

            using var engine = CreateEngine();
            engine.LoadMap("outer_map");
            IVisualHeightmap outer = engine.GetService(CoreServiceKeys.VisualHeightmap);
            Assert.That(outer.TrySampleHeightCm(50f, 50f, out float outerHeight), Is.True);
            Assert.That(outerHeight, Is.EqualTo(25f).Within(0.001f));

            engine.PushMap("inner_map");
            IVisualHeightmap inner = engine.GetService(CoreServiceKeys.VisualHeightmap);
            Assert.That(inner.TrySampleHeightCm(50f, 50f, out float innerHeight), Is.True);
            Assert.That(innerHeight, Is.EqualTo(125f).Within(0.001f));

            engine.PopMap();
            IVisualHeightmap restored = engine.GetService(CoreServiceKeys.VisualHeightmap);
            Assert.That(restored.TrySampleHeightCm(50f, 50f, out float restoredHeight), Is.True);
            Assert.That(restoredHeight, Is.EqualTo(25f).Within(0.001f));
        }

        [Test]
        public void LoadMap_WhenNoVisualHeightmapDeclared_DoesNotBindFallbackTruth()
        {
            WriteMap("outer_map", """
            {
              "id": "outer_map"
            }
            """);

            using var engine = CreateEngine();
            engine.LoadMap("outer_map");

            Assert.That(engine.TryGetService(CoreServiceKeys.VisualHeightmap, out IVisualHeightmap _), Is.False);
        }

        [Test]
        public void LoadMap_WhenGridTerrainHasNoVisualHeightmapDeclared_BindsDerivedLogicTerrainRenderSource()
        {
            WriteMap("outer_map", """
            {
              "id": "outer_map",
              "boards": [
                {
                  "name": "default",
                  "spatialType": "Grid",
                  "widthInMacroTiles": 1,
                  "heightInMacroTiles": 1,
                  "gridCellSizeCm": 100,
                  "chunkSizeCells": 4,
                  "navigationEnabled": false
                }
              ]
            }
            """);

            using var engine = CreateEngine();
            engine.LoadMap("outer_map");

            IVisualHeightmap heightmap = engine.GetService(CoreServiceKeys.VisualHeightmap);
            Assert.Multiple(() =>
            {
                Assert.That(heightmap.GetType().FullName, Is.EqualTo("Ludots.Core.Presentation.Terrain.LogicTerrainVisualHeightmapAdapter"));
                Assert.That(engine.CurrentMapSession.VisualHeightmap, Is.SameAs(heightmap));
                Assert.That(heightmap, Is.AssignableTo<IVisualTerrainRenderFeatureSource>());
                Assert.That(heightmap.TrySampleHeightCm(50f, 50f, out float heightCm), Is.True);
                Assert.That(heightCm, Is.EqualTo(0f).Within(0.001f));
            });
        }

        [Test]
        public void PushAndPopMap_WhenOuterMapHasNoVisualHeightmap_ClearsFocusedTruthOnRestore()
        {
            WriteHeightmap("inner.vhtm", 125);
            WriteMap("outer_map", """
            {
              "id": "outer_map"
            }
            """);
            WriteMap("inner_map", """
            {
              "id": "inner_map",
              "visualHeightmapAsset": "assets/terrain/inner.vhtm"
            }
            """);

            using var engine = CreateEngine();
            engine.LoadMap("outer_map");
            Assert.That(engine.TryGetService(CoreServiceKeys.VisualHeightmap, out IVisualHeightmap _), Is.False);

            engine.PushMap("inner_map");
            Assert.That(engine.TryGetService(CoreServiceKeys.VisualHeightmap, out IVisualHeightmap inner), Is.True);
            Assert.That(inner!.TrySampleHeightCm(50f, 50f, out float innerHeight), Is.True);
            Assert.That(innerHeight, Is.EqualTo(125f).Within(0.001f));

            engine.PopMap();
            Assert.That(engine.TryGetService(CoreServiceKeys.VisualHeightmap, out IVisualHeightmap _), Is.False);
        }

        [Test]
        public void LoadMap_WhenMapAndBoardDeclareDifferentVisualHeightmapAssets_ThrowsExplicitly()
        {
            WriteMap("outer_map", """
            {
              "id": "outer_map",
              "visualHeightmapAsset": "assets/terrain/map.vhtm",
              "boards": [
                {
                  "name": "default",
                  "visualHeightmapAsset": "assets/terrain/board.vhtm"
                }
              ]
            }
            """);

            using var engine = CreateEngine();
            var ex = Assert.Throws<InvalidOperationException>(() => engine.LoadMap("outer_map"));
            Assert.That(ex!.Message, Does.Contain("conflicting visual heightmap assets"));
        }

        [Test]
        public void LoadMap_WhenVisualHeightAssetResolvesToMultipleMountedSources_ThrowsExplicitly()
        {
            WriteHeightmap("shared.vhtm", 80);
            Directory.CreateDirectory(Path.Combine(_coreRoot, "assets", "terrain"));
            using (var stream = File.Create(Path.Combine(_coreRoot, "assets", "terrain", "shared.vhtm")))
            {
                VisualHeightmapBinary.Write(stream, VisualHeightmapAsset.CreateSingleLayer(
                    new Ludots.Core.Mathematics.WorldAabbCm(0, 0, 100, 100),
                    sampleColumns: 2,
                    sampleRows: 2,
                    new[] { (short)80, (short)80, (short)80, (short)80 }));
            }

            WriteMap("outer_map", """
            {
              "id": "outer_map",
              "visualHeightmapAsset": "assets/terrain/shared.vhtm"
            }
            """);

            using var engine = CreateEngine();
            var ex = Assert.Throws<InvalidOperationException>(() => engine.LoadMap("outer_map"));
            Assert.That(ex!.Message, Does.Contain("multiple mounted assets"));
        }

        private GameEngine CreateEngine()
        {
            string repoRoot = FindRepoRoot();
            var modPaths = new[]
            {
                Path.Combine(repoRoot, "mods", "LudotsCoreMod"),
                Path.Combine(repoRoot, "mods", "CoreInputMod"),
                _modRoot,
            }.ToList();

            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(modPaths, _coreRoot);
            engine.Start();
            return engine;
        }

        private void WriteMap(string mapId, string json)
        {
            File.WriteAllText(Path.Combine(_modRoot, "assets", "Configs", "Maps", $"{mapId}.json"), json);
        }

        private void WriteHeightmap(string fileName, short heightCm)
        {
            var asset = VisualHeightmapAsset.CreateSingleLayer(
                new Ludots.Core.Mathematics.WorldAabbCm(0, 0, 100, 100),
                sampleColumns: 2,
                sampleRows: 2,
                new[] { heightCm, heightCm, heightCm, heightCm });

            using var stream = File.Create(Path.Combine(_modRoot, "assets", "terrain", fileName));
            VisualHeightmapBinary.Write(stream, asset);
        }

        private static string FindRepoRoot()
        {
            string? current = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrWhiteSpace(current))
            {
                if (File.Exists(Path.Combine(current, "mods", "LudotsCoreMod", "mod.json")) &&
                    File.Exists(Path.Combine(current, "mods", "CoreInputMod", "mod.json")))
                {
                    return current;
                }

                current = Directory.GetParent(current)?.FullName;
            }

            throw new DirectoryNotFoundException("Could not locate repo root containing mods/LudotsCoreMod and mods/CoreInputMod.");
        }

        private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
        {
            Directory.CreateDirectory(destinationDirectory);
            foreach (string sourceFile in Directory.EnumerateFiles(sourceDirectory))
            {
                string destinationFile = Path.Combine(destinationDirectory, Path.GetFileName(sourceFile));
                File.Copy(sourceFile, destinationFile, overwrite: true);
            }

            foreach (string sourceChildDirectory in Directory.EnumerateDirectories(sourceDirectory))
            {
                string destinationChildDirectory = Path.Combine(destinationDirectory, Path.GetFileName(sourceChildDirectory));
                CopyDirectory(sourceChildDirectory, destinationChildDirectory);
            }
        }
    }
}
