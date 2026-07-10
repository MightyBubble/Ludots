using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Ludots.Core.Config;
using Ludots.Core.Map;
using Ludots.Core.Map.Board;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace GasTests
{
    [TestFixture]
    public class MapManagerInheritanceTests
    {
        [Test]
        public void LoadMap_WhenChildOmitsBoards_InheritsParentBoards()
        {
            var tempRoot = CreateTempDir();
            try
            {
                WriteMapConfig(tempRoot, "parent", """
                {
                  "id": "parent",
                  "boards": [
                    {
                      "name": "default",
                      "spatialType": "Hex",
                      "widthInMacroTiles": 128,
                      "heightInMacroTiles": 64,
                      "gridCellSizeCm": 200,
                      "hexEdgeLengthCm": 900,
                      "chunkSizeCells": 32
                    }
                  ]
                }
                """);

                WriteMapConfig(tempRoot, "child", """
                {
                  "id": "child",
                  "parentId": "parent"
                }
                """);

                var manager = CreateMapManager(tempRoot);
                var cfg = manager.LoadMap("child");

                Assert.That(cfg, Is.Not.Null);
                Assert.That(cfg!.Id, Is.EqualTo("child"));
                Assert.That(cfg!.Boards, Is.Not.Null);
                Assert.That(cfg.Boards.Count, Is.EqualTo(1));
                var board = cfg.Boards[0];
                Assert.That(board.SpatialType, Is.EqualTo("Hex"));
                Assert.That(board.WidthInMacroTiles, Is.EqualTo(128));
                Assert.That(board.HexEdgeLengthCm, Is.EqualTo(900));
            }
            finally
            {
                TryDelete(tempRoot);
            }
        }

        [Test]
        public void LoadMap_WhenParentCycleExists_Throws()
        {
            var tempRoot = CreateTempDir();
            try
            {
                WriteMapConfig(tempRoot, "a", """
                {
                  "id": "a",
                  "parentId": "b"
                }
                """);

                WriteMapConfig(tempRoot, "b", """
                {
                  "id": "b",
                  "parentId": "a"
                }
                """);

                var manager = CreateMapManager(tempRoot);
                var ex = Assert.Throws<InvalidOperationException>(() => manager.LoadMap("a"));
                Assert.That(ex!.Message, Does.Contain("Cyclic map inheritance detected"));
            }
            finally
            {
                TryDelete(tempRoot);
            }
        }

        [Test]
        public void LoadMap_WhenParentMapIsMissing_Throws()
        {
            var tempRoot = CreateTempDir();
            try
            {
                WriteMapConfig(tempRoot, "child", """
                {
                  "id": "child",
                  "parentId": "missing_parent"
                }
                """);

                var manager = CreateMapManager(tempRoot);
                var ex = Assert.Throws<InvalidOperationException>(() => manager.LoadMap("child"));

                Assert.That(ex!.Message, Does.Contain("missing parent map 'missing_parent'"));
            }
            finally
            {
                TryDelete(tempRoot);
            }
        }

        [Test]
        public void LoadMap_WhenBoardUsesLegacyTileExtentKey_Throws()
        {
            var tempRoot = CreateTempDir();
            try
            {
                WriteMapConfig(tempRoot, "legacy", """
                {
                  "id": "legacy",
                  "boards": [
                    {
                      "name": "default",
                      "widthInTiles": 2,
                      "heightInMacroTiles": 2
                    }
                  ]
                }
                """);

                var manager = CreateMapManager(tempRoot);
                var ex = Assert.Throws<InvalidOperationException>(() => manager.LoadMap("legacy"));

                Assert.That(ex!.Message, Does.Contain("legacy key 'widthInTiles'"));
                Assert.That(ex.Message, Does.Contain("widthInMacroTiles"));
            }
            finally
            {
                TryDelete(tempRoot);
            }
        }

        [Test]
        public void LoadMap_WhenEntityUsesLegacyPositionField_Throws()
        {
            var tempRoot = CreateTempDir();
            try
            {
                WriteMapConfig(tempRoot, "legacy_position", """
                {
                  "id": "legacy_position",
                  "entities": [
                    {
                      "instanceId": "unit.alpha",
                      "template": "unit.template",
                      "position": { "x": 10, "y": 20 }
                    }
                  ]
                }
                """);

                var manager = CreateMapManager(tempRoot);
                var ex = Assert.Throws<InvalidOperationException>(() => manager.LoadMap("legacy_position"));

                Assert.That(ex!.Message, Does.Contain("unsupported entity key 'position'"));
                Assert.That(ex.Message, Does.Contain("Overrides.WorldPositionCm"));
            }
            finally
            {
                TryDelete(tempRoot);
            }
        }

        [Test]
        public void LoadMap_TerrainBenchmarkEntryMap_UsesWorldPositionOverrideContract()
        {
            string repoRoot = FindRepoRoot();
            string terrainModRoot = Path.Combine(repoRoot, "mods", "TerrainBenchmarkMod");

            Assert.That(Directory.Exists(terrainModRoot), Is.True, $"Missing TerrainBenchmarkMod at {terrainModRoot}");

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", Path.Combine(repoRoot, "assets"));
            vfs.Mount("TerrainBenchmarkMod", terrainModRoot);

            var trigger = new TriggerManager();
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), trigger);
            modLoader.LoadedModIds.Add("TerrainBenchmarkMod");

            var pipeline = new ConfigPipeline(vfs, modLoader);
            var manager = new MapManager(vfs, trigger, modLoader, pipeline);

            MapConfig cfg = manager.LoadMap("entry");

            Assert.That(cfg, Is.Not.Null);
            Assert.That(cfg.Entities, Has.Count.EqualTo(3));
            Assert.That(
                cfg.Entities,
                Has.All.Matches<EntitySpawnData>(entity =>
                    entity.Overrides != null && entity.Overrides.ContainsKey("WorldPositionCm")));
        }

        [Test]
        public void LoadMap_WhenChildOmitsVisualHeightmapAsset_InheritsParentDeclaration()
        {
            var tempRoot = CreateTempDir();
            try
            {
                WriteMapConfig(tempRoot, "parent", """
                {
                  "id": "parent",
                  "visualHeightmapAsset": "terrain/parent.vhtm"
                }
                """);

                WriteMapConfig(tempRoot, "child", """
                {
                  "id": "child",
                  "parentId": "parent"
                }
                """);

                var manager = CreateMapManager(tempRoot);
                var cfg = manager.LoadMap("child");

                Assert.That(cfg, Is.Not.Null);
                Assert.That(cfg!.VisualHeightmapAsset, Is.EqualTo("terrain/parent.vhtm"));
            }
            finally
            {
                TryDelete(tempRoot);
            }
        }

        [Test]
        public void LoadMap_WhenChildOverridesBoardByName_ReplacesParentBoard()
        {
            var tempRoot = CreateTempDir();
            try
            {
                WriteMapConfig(tempRoot, "parent", """
                {
                  "id": "parent",
                  "boards": [
                    {
                      "name": "default",
                      "spatialType": "Hex",
                      "widthInMacroTiles": 128,
                      "heightInMacroTiles": 64,
                      "gridCellSizeCm": 200
                    }
                  ]
                }
                """);

                WriteMapConfig(tempRoot, "child", """
                {
                  "id": "child",
                  "parentId": "parent",
                  "boards": [
                    {
                      "name": "default",
                      "spatialType": "Square",
                      "widthInMacroTiles": 32,
                      "heightInMacroTiles": 32,
                      "gridCellSizeCm": 100
                    }
                  ]
                }
                """);

                var manager = CreateMapManager(tempRoot);
                var cfg = manager.LoadMap("child");

                Assert.That(cfg, Is.Not.Null);
                Assert.That(cfg!.Boards.Count, Is.EqualTo(1));
                Assert.That(cfg.Boards[0].SpatialType, Is.EqualTo("Square"));
                Assert.That(cfg.Boards[0].WidthInMacroTiles, Is.EqualTo(32));
            }
            finally
            {
                TryDelete(tempRoot);
            }
        }

        [Test]
        public void LoadMap_WhenBoardDeclaresVisualHeightmapAsset_UsesBoardScopedContract()
        {
            var tempRoot = CreateTempDir();
            try
            {
                WriteMapConfig(tempRoot, "board_map", """
                {
                  "id": "board_map",
                  "boards": [
                    {
                      "name": "default",
                      "visualHeightmapAsset": "terrain/board.vhtm"
                    }
                  ]
                }
                """);

                var manager = CreateMapManager(tempRoot);
                var cfg = manager.LoadMap("board_map");

                Assert.That(cfg, Is.Not.Null);
                Assert.That(cfg!.Boards.Count, Is.EqualTo(1));
                Assert.That(cfg.Boards[0].VisualHeightmapAsset, Is.EqualTo("terrain/board.vhtm"));
            }
            finally
            {
                TryDelete(tempRoot);
            }
        }

        [Test]
        public void LoadMap_WhenChildOverridesMetadata_MergesByTopLevelKey()
        {
            var tempRoot = CreateTempDir();
            try
            {
                WriteMapConfig(tempRoot, "parent", """
                {
                  "id": "parent",
                  "metadata": {
                    "terrain": {
                      "profile": "parent"
                    },
                    "benchmark": {
                      "count": 1000
                    }
                  }
                }
                """);

                WriteMapConfig(tempRoot, "child", """
                {
                  "id": "child",
                  "parentId": "parent",
                  "metadata": {
                    "benchmark": {
                      "count": 30000
                    }
                  }
                }
                """);

                var manager = CreateMapManager(tempRoot);
                var cfg = manager.LoadMap("child");

                Assert.That(cfg, Is.Not.Null);
                Assert.That(cfg!.Metadata["terrain"]!["profile"]!.GetValue<string>(), Is.EqualTo("parent"));
                Assert.That(cfg.Metadata["benchmark"]!["count"]!.GetValue<int>(), Is.EqualTo(30000));
            }
            finally
            {
                TryDelete(tempRoot);
            }
        }

        [Test]
        public void LoadMap_WhenChildDuplicatesParentInstanceId_Throws()
        {
            var tempRoot = CreateTempDir();
            try
            {
                WriteMapConfig(tempRoot, "parent", """
                {
                  "id": "parent",
                  "entities": [
                    { "instanceId": "unit.alpha", "template": "unit.parent" }
                  ]
                }
                """);

                WriteMapConfig(tempRoot, "child", """
                {
                  "id": "child",
                  "parentId": "parent",
                  "entities": [
                    { "instanceId": "unit.alpha", "template": "unit.child" }
                  ]
                }
                """);

                var manager = CreateMapManager(tempRoot);
                var ex = Assert.Throws<InvalidOperationException>(() => manager.LoadMap("child"));

                Assert.That(ex!.Message, Does.Contain("duplicate entity InstanceId 'unit.alpha'"));
            }
            finally
            {
                TryDelete(tempRoot);
            }
        }

        [Test]
        public void LoadMap_WhenChildDuplicatesParentParticipantIds_Throws()
        {
            var tempRoot = CreateTempDir();
            try
            {
                WriteMapConfig(tempRoot, "parent", """
                {
                  "id": "parent",
                  "entities": [
                    { "instanceId": "team.alpha", "template": "logical.team" },
                    { "instanceId": "player.alpha", "template": "logical.player" }
                  ],
                  "teams": [
                    { "teamId": 10, "representativeInstanceId": "team.alpha" }
                  ],
                  "players": [
                    { "playerId": 7, "teamId": 10, "representativeInstanceId": "player.alpha" }
                  ]
                }
                """);

                WriteMapConfig(tempRoot, "child", """
                {
                  "id": "child",
                  "parentId": "parent",
                  "entities": [
                    { "instanceId": "team.beta", "template": "logical.team" },
                    { "instanceId": "player.beta", "template": "logical.player" }
                  ],
                  "teams": [
                    { "teamId": 10, "representativeInstanceId": "team.beta" }
                  ],
                  "players": [
                    { "playerId": 7, "teamId": 10, "representativeInstanceId": "player.beta" }
                  ]
                }
                """);

                var manager = CreateMapManager(tempRoot);
                var ex = Assert.Throws<InvalidOperationException>(() => manager.LoadMap("child"));

                Assert.That(ex!.Message, Does.Match("duplicate (TeamId 10|PlayerId 7)"));
            }
            finally
            {
                TryDelete(tempRoot);
            }
        }

        [Test]
        public void LoadMap_WhenParticipantRepresentativeIsMissing_Throws()
        {
            var tempRoot = CreateTempDir();
            try
            {
                WriteMapConfig(tempRoot, "invalid_participants", """
                {
                  "id": "invalid_participants",
                  "entities": [
                    { "instanceId": "team.alpha", "template": "logical.team" }
                  ],
                  "teams": [
                    { "teamId": 10, "representativeInstanceId": "team.alpha" }
                  ],
                  "players": [
                    { "playerId": 7, "teamId": 10, "representativeInstanceId": "player.missing" }
                  ]
                }
                """);

                var manager = CreateMapManager(tempRoot);
                var ex = Assert.Throws<InvalidOperationException>(() => manager.LoadMap("invalid_participants"));

                Assert.That(ex!.Message, Does.Contain("Players[0].RepresentativeInstanceId"));
                Assert.That(ex.Message, Does.Contain("unknown entity InstanceId 'player.missing'"));
            }
            finally
            {
                TryDelete(tempRoot);
            }
        }

        [Test]
        public void LoadMap_WhenChildInheritsParticipantBindings_MergesParticipantAuthoring()
        {
            var tempRoot = CreateTempDir();
            try
            {
                WriteMapConfig(tempRoot, "parent", """
                {
                  "id": "parent",
                  "entities": [
                    { "instanceId": "team.alpha", "template": "logical.team" },
                    { "instanceId": "player.local", "template": "logical.player" }
                  ],
                  "teams": [
                    { "teamId": 10, "representativeInstanceId": "team.alpha" }
                  ],
                  "players": [
                    { "playerId": 7, "teamId": 10, "representativeInstanceId": "player.local" }
                  ],
                  "participantRelationships": {
                    "playerTeams": [
                      { "playerId": 7, "teamId": 10, "typeId": "Membership" }
                    ]
                  }
                }
                """);

                WriteMapConfig(tempRoot, "child", """
                {
                  "id": "child",
                  "parentId": "parent",
                  "entities": [
                    { "instanceId": "team.beta", "template": "logical.team" }
                  ],
                  "teams": [
                    { "teamId": 20, "representativeInstanceId": "team.beta" }
                  ],
                  "participantRelationships": {
                    "teams": [
                      { "teamA": 10, "teamB": 20, "typeId": "Alliance", "attitude": "Friendly" }
                    ]
                  }
                }
                """);

                var manager = CreateMapManager(tempRoot);
                var cfg = manager.LoadMap("child");

                Assert.That(cfg, Is.Not.Null);
                Assert.That(cfg!.Entities.Select(e => e.InstanceId), Is.EquivalentTo(new[] { "team.alpha", "player.local", "team.beta" }));
                Assert.That(cfg.Teams.Select(t => t.TeamId), Is.EquivalentTo(new[] { 10, 20 }));
                Assert.That(cfg.Players.Select(p => p.PlayerId), Is.EquivalentTo(new[] { 7 }));
                Assert.That(cfg.ParticipantRelationships.PlayerTeams.Count, Is.EqualTo(1));
                Assert.That(cfg.ParticipantRelationships.Teams.Count, Is.EqualTo(1));
            }
            finally
            {
                TryDelete(tempRoot);
            }
        }

        private static MapManager CreateMapManager(string coreRoot)
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", coreRoot);
            var trigger = new TriggerManager();
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), trigger);
            var pipeline = new ConfigPipeline(vfs, modLoader);
            return new MapManager(vfs, trigger, modLoader, pipeline);
        }

        private static void WriteMapConfig(string root, string mapId, string json)
        {
            var mapsDir = Path.Combine(root, "Configs", "Maps");
            Directory.CreateDirectory(mapsDir);
            File.WriteAllText(Path.Combine(mapsDir, $"{mapId}.json"), json);
        }

        private static string CreateTempDir()
        {
            var path = Path.Combine(Path.GetTempPath(), "ludots_mapmgr_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static string FindRepoRoot()
        {
            string? dir = AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir, "assets")) &&
                    Directory.Exists(Path.Combine(dir, "mods")) &&
                    File.Exists(Path.Combine(dir, "AGENTS.md")))
                {
                    return dir;
                }

                dir = Directory.GetParent(dir)?.FullName;
            }

            throw new InvalidOperationException("Cannot find repo root.");
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            }
            catch
            {
            }
        }
    }
}
