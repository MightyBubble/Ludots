using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Scripting;
using NUnit.Framework;
using Ludots.Platform.Abstractions;

namespace Ludots.Tests.Gas
{
    [TestFixture]
    public sealed class MapContinuousHeightmapContractTests
    {
        private string _root = string.Empty;
        private string _coreRoot = string.Empty;
        private string _modRoot = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "Ludots_MapContinuousHeightmapContractTests", Guid.NewGuid().ToString("N"));
            _coreRoot = Path.Combine(_root, "assets");
            _modRoot = Path.Combine(_root, "mods", "TestMapMod");
            Directory.CreateDirectory(Path.Combine(_coreRoot, "Maps"));
            Directory.CreateDirectory(Path.Combine(_coreRoot, "Navigation"));
            Directory.CreateDirectory(Path.Combine(_modRoot, "assets", "Maps"));
            Directory.CreateDirectory(Path.Combine(_modRoot, "assets", "terrain"));

            string repoRoot = FindRepoRoot();
            CopyDirectory(Path.Combine(repoRoot, "assets"), _coreRoot);

            string gameConfigPath = Path.Combine(_coreRoot, "game.json");
            JsonObject gameConfig = JsonNode.Parse(File.ReadAllText(gameConfigPath))?.AsObject()
                ?? throw new InvalidOperationException("Copied core game.json must contain a JSON object.");
            gameConfig["startupMapId"] = "outer_map";
            gameConfig["worldWidthInMacroTiles"] = 16;
            gameConfig["worldHeightInMacroTiles"] = 16;
            gameConfig["gridCellSizeCm"] = 100;
            File.WriteAllText(gameConfigPath, gameConfig.ToJsonString());

            File.WriteAllText(Path.Combine(_coreRoot, "Navigation", "agent_profiles.json"), """
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

            File.WriteAllText(Path.Combine(_coreRoot, "Navigation", "pathing.json"), """
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
        public void LoadMap_BindsDeclaredContinuousHeightmapThroughCoreService()
        {
            WriteHeightmap("outer.height", 50);
            WriteMap("outer_map", """
            {
              "id": "outer_map",
              "continuousHeightmapAsset": "assets/terrain/outer.height"
            }
            """);

            using var engine = CreateEngine();
            engine.LoadMap("outer_map");

            IContinuousHeightmap heightmap = engine.GetService(CoreServiceKeys.ContinuousHeightmap);
            Assert.That(heightmap, Is.TypeOf<ContinuousHeightmapRuntime>());
            Assert.That(heightmap.TrySampleHeightCm(50f, 50f, out float heightCm), Is.True);
            Assert.That(heightCm, Is.EqualTo(50f).Within(0.001f));
        }

        [Test]
        public void LoadMap_BindsContinuousHeightmapRenderProfileThroughRenderSource()
        {
            WriteHeightmap("outer.height", -75);
            WriteMap("outer_map", """
            {
              "id": "outer_map",
              "continuousHeightmap": {
                "asset": "assets/terrain/outer.height",
                "renderProfile": {
                  "waterEnabled": true,
                  "seaLevelCm": 0,
                  "displayHeightScale": 500,
                  "colorContrast": 1.4
                }
              }
            }
            """);

            using var engine = CreateEngine();
            engine.LoadMap("outer_map");

            IContinuousHeightmap heightmap = engine.GetService(CoreServiceKeys.ContinuousHeightmap);
            Assert.That(heightmap, Is.AssignableTo<IContinuousHeightmapRenderSource>());
            var renderSource = (IContinuousHeightmapRenderSource)heightmap;
            Assert.That(renderSource.RenderProfile.WaterEnabled, Is.True);
            Assert.That(renderSource.RenderProfile.SeaLevelCm, Is.EqualTo(0f));
            Assert.That(renderSource.RenderProfile.DisplayHeightScale, Is.EqualTo(500f));
            Assert.That(renderSource.RenderProfile.ColorContrast, Is.EqualTo(1.4f));
        }

        [Test]
        public void LoadMap_WhenDisableDistanceFog_IsHonoredOnRenderProfile()
        {
            WriteHeightmap("outer.height", 40);
            WriteMap("outer_map", """
            {
              "id": "outer_map",
              "continuousHeightmap": {
                "asset": "assets/terrain/outer.height",
                "renderProfile": {
                  "disableDistanceFog": true
                }
              }
            }
            """);

            using var engine = CreateEngine();
            engine.LoadMap("outer_map");

            var renderSource = (IContinuousHeightmapRenderSource)engine.GetService(CoreServiceKeys.ContinuousHeightmap);
            Assert.That(renderSource.RenderProfile.DisableDistanceFog, Is.True);
        }

        [Test]
        public void LoadMap_WhenWorldWidthCmOverride_RemapsBoundsKeepingAspectAndSamples()
        {
            WriteHeightmap("outer.height", 40);
            WriteMap("outer_map", """
            {
              "id": "outer_map",
              "continuousHeightmap": {
                "asset": "assets/terrain/outer.height",
                "worldWidthCm": 6400000
              }
            }
            """);

            using var engine = CreateEngine();
            engine.LoadMap("outer_map");

            IContinuousHeightmap heightmap = engine.GetService(CoreServiceKeys.ContinuousHeightmap);
            var renderSource = (IContinuousHeightmapRenderSource)heightmap;
            Assert.That(renderSource.Bounds.Width, Is.EqualTo(6_400_000));
            Assert.That(renderSource.Bounds.Height, Is.EqualTo(6_400_000),
                "Fixture heightmap is square; scaled height must match width.");
            Assert.That(heightmap.TrySampleHeightCm(0f, 0f, out float heightCm), Is.True);
            Assert.That(heightCm, Is.EqualTo(40f).Within(0.001f));
        }

        [Test]
        public void ApplyWorldWidthOverride_ScalesEastAsiaAspectAroundCenter()
        {
            var source = ContinuousHeightmapAsset.CreateSingleLayer(
                new WorldAabbCm(-450_326_016, -257_329_152, 900_652_032, 514_658_304),
                sampleColumns: 3,
                sampleRows: 3,
                new short[] { 0, 0, 0, 0, 100, 0, 0, 0, 0 });
            var binding = new ContinuousHeightmapBindingConfig { WorldWidthCm = 6_399_232 };

            ContinuousHeightmapAsset scaled = MapContinuousHeightmapLoader.ApplyWorldWidthOverride(source, binding);

            Assert.That(scaled.Bounds.Width, Is.EqualTo(6_399_232));
            Assert.That(scaled.Bounds.Height, Is.EqualTo(3_656_704));
            Assert.That(scaled.Bounds.Left + (scaled.Bounds.Width / 2), Is.EqualTo(0));
            Assert.That(scaled.SampleColumns, Is.EqualTo(3));
            Assert.That(scaled.HeightSamplesCm[4], Is.EqualTo((short)100));
        }

        [Test]
        public void LoadMap_WhenDeclaredContinuousHeightmapMissing_ThrowsExplicitly()
        {
            WriteMap("outer_map", """
            {
              "id": "outer_map",
              "continuousHeightmapAsset": "assets/terrain/missing.height"
            }
            """);

            using var engine = CreateEngine();
            var ex = Assert.Throws<FileNotFoundException>(() => engine.LoadMap("outer_map"));
            Assert.That(ex!.Message, Does.Contain("visual heightmap asset"));
        }

        [Test]
        public void PushAndPopMap_RestoreFocusedMapContinuousHeightmap()
        {
            WriteHeightmap("outer.height", 25);
            WriteHeightmap("inner.height", 125);
            WriteMap("outer_map", """
            {
              "id": "outer_map",
              "continuousHeightmapAsset": "assets/terrain/outer.height"
            }
            """);
            WriteMap("inner_map", """
            {
              "id": "inner_map",
              "continuousHeightmapAsset": "assets/terrain/inner.height"
            }
            """);

            using var engine = CreateEngine();
            engine.LoadMap("outer_map");
            IContinuousHeightmap outer = engine.GetService(CoreServiceKeys.ContinuousHeightmap);
            Assert.That(outer.TrySampleHeightCm(50f, 50f, out float outerHeight), Is.True);
            Assert.That(outerHeight, Is.EqualTo(25f).Within(0.001f));

            engine.PushMap("inner_map");
            IContinuousHeightmap inner = engine.GetService(CoreServiceKeys.ContinuousHeightmap);
            Assert.That(inner.TrySampleHeightCm(50f, 50f, out float innerHeight), Is.True);
            Assert.That(innerHeight, Is.EqualTo(125f).Within(0.001f));

            engine.PopMap();
            IContinuousHeightmap restored = engine.GetService(CoreServiceKeys.ContinuousHeightmap);
            Assert.That(restored.TrySampleHeightCm(50f, 50f, out float restoredHeight), Is.True);
            Assert.That(restoredHeight, Is.EqualTo(25f).Within(0.001f));
        }

        [Test]
        public void LoadMap_WhenNoContinuousHeightmapDeclared_DoesNotBindFallbackTruth()
        {
            WriteMap("outer_map", """
            {
              "id": "outer_map"
            }
            """);

            using var engine = CreateEngine();
            engine.LoadMap("outer_map");

            Assert.That(engine.TryGetService(CoreServiceKeys.ContinuousHeightmap, out IContinuousHeightmap _), Is.False);
        }

        [Test]
        public void PushAndPopMap_WhenOuterMapHasNoContinuousHeightmap_ClearsFocusedTruthOnRestore()
        {
            WriteHeightmap("inner.height", 125);
            WriteMap("outer_map", """
            {
              "id": "outer_map"
            }
            """);
            WriteMap("inner_map", """
            {
              "id": "inner_map",
              "continuousHeightmapAsset": "assets/terrain/inner.height"
            }
            """);

            using var engine = CreateEngine();
            engine.LoadMap("outer_map");
            Assert.That(engine.TryGetService(CoreServiceKeys.ContinuousHeightmap, out IContinuousHeightmap _), Is.False);

            engine.PushMap("inner_map");
            Assert.That(engine.TryGetService(CoreServiceKeys.ContinuousHeightmap, out IContinuousHeightmap inner), Is.True);
            Assert.That(inner!.TrySampleHeightCm(50f, 50f, out float innerHeight), Is.True);
            Assert.That(innerHeight, Is.EqualTo(125f).Within(0.001f));

            engine.PopMap();
            Assert.That(engine.TryGetService(CoreServiceKeys.ContinuousHeightmap, out IContinuousHeightmap _), Is.False);
        }

        [Test]
        public void LoadMap_WhenMapAndBoardDeclareDifferentContinuousHeightmapAssets_ThrowsExplicitly()
        {
            WriteMap("outer_map", """
            {
              "id": "outer_map",
              "continuousHeightmapAsset": "assets/terrain/map.height",
              "boards": [
                {
                  "name": "default",
                  "continuousHeightmapAsset": "assets/terrain/board.height"
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
            WriteHeightmap("shared.height", 80);
            Directory.CreateDirectory(Path.Combine(_coreRoot, "assets", "terrain"));
            using (var stream = File.Create(Path.Combine(_coreRoot, "assets", "terrain", "shared.height")))
            {
                ContinuousHeightmapBinary.Write(stream, ContinuousHeightmapAsset.CreateSingleLayer(
                    new Ludots.Platform.Abstractions.WorldAabbCm(0, 0, 100, 100),
                    sampleColumns: 2,
                    sampleRows: 2,
                    new[] { (short)80, (short)80, (short)80, (short)80 }));
            }

            WriteMap("outer_map", """
            {
              "id": "outer_map",
              "continuousHeightmapAsset": "assets/terrain/shared.height"
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
            File.WriteAllText(Path.Combine(_modRoot, "assets", "Maps", $"{mapId}.json"), json);
        }

        private void WriteHeightmap(string fileName, short heightCm)
        {
            var asset = ContinuousHeightmapAsset.CreateSingleLayer(
                new Ludots.Platform.Abstractions.WorldAabbCm(0, 0, 100, 100),
                sampleColumns: 2,
                sampleRows: 2,
                new[] { heightCm, heightCm, heightCm, heightCm });

            using var stream = File.Create(Path.Combine(_modRoot, "assets", "terrain", fileName));
            ContinuousHeightmapBinary.Write(stream, asset);
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
