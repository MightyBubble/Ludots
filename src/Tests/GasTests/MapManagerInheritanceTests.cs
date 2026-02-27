using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
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
                      "widthInTiles": 128,
                      "heightInTiles": 64,
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
                Assert.That(cfg!.Boards, Is.Not.Null);
                Assert.That(cfg.Boards.Count, Is.EqualTo(1));
                var board = cfg.Boards[0];
                Assert.That(board.SpatialType, Is.EqualTo("Hex"));
                Assert.That(board.WidthInTiles, Is.EqualTo(128));
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

        private static MapManager CreateMapManager(string coreRoot)
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", coreRoot);
            var trigger = new TriggerManager();
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), trigger);
            return new MapManager(vfs, trigger, modLoader);
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
